using BepInEx.Configuration;
using Jotunn;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Clans.Common {
    class JotunBatchLoader {
        internal static List<ItemDefinition> resourceDefinitions = new List<ItemDefinition>();
        internal static bool runningQueuedChanges = false;
        internal static AssetBundle Assets;
        internal static Dictionary<string, string> AddedItems = new Dictionary<string, string>();
        internal static List<string> ArcheryAmmoToAdd = new List<string>();

        // Pending in-world item updates, drained once per frame so an entire burst of SettingChanged
        // handlers (e.g. a full server config sync) costs a single pass over the live item drops
        // instead of one pass per changed setting.
        private static readonly List<KeyValuePair<string, Action<ItemDrop.ItemData>>> pendingWorldUpdates = new List<KeyValuePair<string, Action<ItemDrop.ItemData>>>();
        private static bool worldUpdateScheduled = false;

        // When non-null, GetRecipeIndex answers from these maps instead of searching the recipe list. Built
        // for the duration of one ReapplyAllRecipeConfig pass (see BuildRecipeIndexCache).
        private static Dictionary<string, int> recipeIndexByName = null;
        private static Dictionary<string, int> singleRecipeIndexByItem = null;

        internal static readonly AcceptableValueList<string> allowedModifiers = new AcceptableValueList<string>(new string[] {
            HitData.DamageModifier.Normal.ToString(),
            HitData.DamageModifier.VeryWeak.ToString(),
            HitData.DamageModifier.Weak.ToString(),
            HitData.DamageModifier.VeryWeak.ToString(),
            HitData.DamageModifier.Resistant.ToString(),
            HitData.DamageModifier.VeryResistant.ToString(),
            HitData.DamageModifier.Immune.ToString()
        });

        public bool BatchSetup(AssetBundle assetBundle, bool reverse_order = true) {
            Assets = assetBundle;
            // Since configs are ordered by when they are connected this allows us to add things in the order they are defined.
            if (reverse_order) {
                resourceDefinitions.Reverse();
            }
            WireConfigDefs();

            // BatchSetup runs from plugin Awake, before any world exists -- ZNet.instance is always null
            // here, so the ZNet probe could never detect a server. The headless graphics-device check is
            // what actually identifies a dedicated server this early.
            bool on_server = UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
            bool has_frost_forged = resourceDefinitions.Any(itemdef => itemdef.FrostForged != null);
            if (has_frost_forged) { FrostForged.Init(); }

            // A dedicated server rolls the chest loot for the zones it generates, which is where frost forged
            // moulds turn up, so it needs the items registered whenever there are any.
            if (on_server == false || has_frost_forged) {
                // Otherwise not needed on the server
                // The server does not actually do anything with prefabs, and is not responsible for modifying them
                BatchAddItems();
                SetupOnChange();
                ItemManager.OnItemsRegistered += AddAmmoItemsToArcheryTarget;
                // Re-apply config driven recipe values whenever the ObjectDB is (re)built. Jotunn re-adds the
                // cached (local-config) recipes on every ObjectDB.Awake, so this reconciles them to the current
                // (possibly server-synced) config values. Also re-apply when admin config arrives from the server.
                ItemManager.OnItemsRegistered += ReapplyAllRecipeConfig;
                SynchronizationManager.OnConfigurationSynchronized += OnModConfigsChanged;
            }

            // Flush to disk. SaveOnConfigSet stays off; later changes are written in batches (ValConfig.EnableDeferredSave).
            ValConfig.Save();
            return true;
        }

        public bool AddDefinition(ItemDefinition itemdef) {
            resourceDefinitions.Add(itemdef);
            return true;
        }

        private static bool WireConfigDefs() {
            // Ensure save on set is false, we will save at the end of this process.
            foreach (ItemDefinition itemdef in resourceDefinitions) {
                // A definition registered without a Recipe (or with a null item list) used to NRE here,
                // killing the whole mod during Awake. Normalize instead: the item is still registered,
                // it just gets no crafting recipe (see BatchAddItems).
                if (itemdef.Recipe == null) { itemdef.Recipe = new RecipeDefinition(); }
                if (itemdef.Recipe.RecipeItems == null) {
                    Logger.LogError($"Item definition '{itemdef.Name}' has no recipe items; it will be registered without a crafting recipe.");
                    itemdef.Recipe.RecipeItems = new List<RecipeIngredient>();
                }
                // Build a compacted display name for reference, this primarily just needs spaces removed.
                itemdef.DisplayName = string.Join("", itemdef.Name.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
                // Skip over all loading of items that are disabled.
                // Blow up if adding a non-unique data control
                AddedItems.Add(itemdef.DisplayName, itemdef.Prefab);
                // if (!itemdef.enabled) { continue; }
                itemdef.CraftableCfg = ValConfig.BindServerConfig($"{itemdef.Category} - {itemdef.Name}", $"{itemdef.DisplayName}-craftable", itemdef.Craftable, $"Enable/Disable the crafting recipe for {itemdef.Name}.");
                itemdef.StationLVLCfg = ValConfig.BindServerConfig($"{itemdef.Category} - {itemdef.Name}", $"{itemdef.DisplayName}-stationRequiredLevel", itemdef.ReqStationlevel, $"Sets the required minimum crafting station level to craft {itemdef.Name}", true, 1, 4);
                itemdef.CraftAmountCfg = ValConfig.BindServerConfig($"{itemdef.Category} - {itemdef.Name}", $"{itemdef.DisplayName}-craftAmount", itemdef.CraftAmount, $"Sets the amount of {itemdef.Name} crafted per recipe.", true, 1, 50);
                itemdef.CraftedAtCfg = ValConfig.BindServerConfig($"{itemdef.Category} - {itemdef.Name}", $"{itemdef.DisplayName}-craftedAt", itemdef.CraftedAt, $"Sets the crafting station for {itemdef.Name}.");
                // Setup the modifiable stats that this item has defined
                foreach (KeyValuePair<ItemStat, ItemStatConfig> stat in itemdef.ModifableStats) {
                    if (stat.Value.Configurable == false) { continue; }
                    if (stat.Value.IsInt) {
                        stat.Value.CfgInt = ValConfig.BindServerConfig($"{itemdef.Category} - {itemdef.Name}", $"{itemdef.DisplayName}-{stat.Key}", (int)stat.Value.Default_value, $"Value for {stat.Key} on {itemdef.Name}", true, (int)stat.Value.Min, (int)stat.Value.Max);
                    } else {
                        stat.Value.Cfg = ValConfig.BindServerConfig($"{itemdef.Category} - {itemdef.Name}", $"{itemdef.DisplayName}-{stat.Key}", stat.Value.Default_value, $"Value for {stat.Key} on {itemdef.Name}", true, stat.Value.Min, stat.Value.Max);
                    }
                }
                // Set the damage modifiers for this item
                if (itemdef.DamageMods != null) {
                    foreach (KeyValuePair<HitData.DamageType, HitCustomDamageMod> dmgmod in itemdef.DamageMods) {
                        dmgmod.Value.DmgModCfg = ValConfig.BindServerConfig($"{itemdef.Category} - {itemdef.Name}", $"{itemdef.DisplayName}-{dmgmod.Key}-DamageModifier", dmgmod.Value.DamageModifier.ToString(), $"Damage modifier for {dmgmod.Key} on {itemdef.Name}", allowedModifiers, true);
                    }
                }

                // Build the item recipe
                string frostForgedNote = itemdef.FrostForged != null ? $" {itemdef.Name} is frost forged: the amounts make its cast, which the Frost Foundry hardens into the weapon, and its mould is required on top of what is listed here." : "";
                itemdef.Recipe.RecipeConfig = ValConfig.BindServerConfig($"{itemdef.Category} - {itemdef.Name}", $"{itemdef.DisplayName}-recipe", BuildStringRecipeFromItemDef(itemdef), $"Recipe for {itemdef.Name}. Should be in the format of Prefab,Amount,AmountPerLevel|Prefab,Amount,AmountPerLevel eg: Wood,12,2|Stone,2,0{frostForgedNote}");
                if (ValidateRecipeConfig(itemdef) == false) {
                    BuildRecipeReqsFromDefault(itemdef);
                }
                // A frost forged weapon is never crafted directly, only its cast is, so a bulk recipe for it would
                // skip the mould and the Frost Foundry altogether.
                if (itemdef.FrostForged != null && itemdef.Recipe.BulkMultiplier > 1) {
                    Logger.LogWarning($"{itemdef.Name} is frost forged, so its bulk recipe is ignored.");
                    itemdef.Recipe.BulkMultiplier = 0;
                }
                // Bulk variant of the recipe above, for the items that define one.
                if (itemdef.Recipe.BulkMultiplier > 1) {
                    itemdef.Recipe.BulkMultiplierCfg = ValConfig.BindServerConfig($"{itemdef.Category} - {itemdef.Name}", $"{itemdef.DisplayName}-bulkRecipeMultiplier", itemdef.Recipe.BulkMultiplier, $"How many {itemdef.Name} the bulk recipe crafts at once, and the factor it multiplies every ingredient cost by. The bulk recipe otherwise follows the recipe above.", true, 2, 50);
                }
                // itemdef.recipe.resolvedRecipe = BuildRecipeFromConfig(itemdef);

                // Collapse this item's entries into a single grouped custom drawer to keep the in-game
                // Configuration Manager responsive (one visible row per item instead of ~10-20).
                ItemConfigDrawer.Attach(itemdef);
            }
            return true;
        }

        // TODO: Change batch onchange actions to pass to a queue and execute queue from a couroutine.
        private bool SetupOnChange() {
            foreach (ItemDefinition itemdef in resourceDefinitions) {
                // Need to have config onchange settings available for items which are not enabled to ensure that we can enable them when joining a remote server with different items enabled
                // if (!itemdef.enabled) { continue; }
                // Craftable config toggle
                itemdef.CraftableCfg.SettingChanged += (_, _) => {
                    ConfigChangeDebouncer.Schedule(itemdef.CraftableCfg, () => EnableDisableItemInDB(itemdef, itemdef.CraftableCfg.Value));
                };
                // Logger.LogInfo("Setup Craftable toggle");
                // Station level config
                itemdef.StationLVLCfg.SettingChanged += (_, _) => {
                    ConfigChangeDebouncer.Schedule(itemdef.StationLVLCfg, () => ModifyItemRecipeLevel(itemdef, itemdef.StationLVLCfg.Value));
                };
                // Logger.LogInfo("Setup Crafting station level");
                // Modify where the item is crafted
                itemdef.CraftedAtCfg.SettingChanged += (_, _) => {
                    ConfigChangeDebouncer.Schedule(itemdef.CraftedAtCfg, () => ModifyItemRecipeCraftedAt(itemdef));
                };
                // Modify how many of the item are crafted per recipe
                itemdef.CraftAmountCfg.SettingChanged += (_, _) => {
                    ConfigChangeDebouncer.Schedule(itemdef.CraftAmountCfg, () => ModifyItemRecipeCraftAmount(itemdef, itemdef.CraftAmountCfg.Value));
                };
                // Logger.LogInfo("Setup single value changes");

                // All of the configurable stat variables
                foreach (KeyValuePair<ItemStat, ItemStatConfig> stat in itemdef.ModifableStats) {
                    if (stat.Value.Configurable == false) { continue; }
                    object statKey = stat.Value.IsInt ? (object)stat.Value.CfgInt : stat.Value.Cfg;
                    void UpdateFromConfig(object sender, EventArgs args) {
                        ConfigChangeDebouncer.Schedule(statKey, () => {
                            if (ZNet.instance == null || ZNet.instance.enabled == false) { return; }
                            if (stat.Value.IsInt) {
                                stat.Value.Default_value = stat.Value.CfgInt.Value;
                            } else {
                                stat.Value.Default_value = stat.Value.Cfg.Value;
                            }
                            // Update player items
                            UpdateItemInPlayerInventory(itemdef.Prefab, (ItemDrop.ItemData item) => { ItemDataConfigModifier(stat.Key, stat.Value.Default_value, item); });
                            // Update in world items, batched into a single scan to prevent lag spikes (e.g. on server config sync).
                            EnqueueWorldUpdate(itemdef.Prefab, (ItemDrop.ItemData item) => { ItemDataConfigModifier(stat.Key, stat.Value.Default_value, item); });
                        });
                    }

                    if (stat.Value.IsInt) {
                        stat.Value.CfgInt.SettingChanged += UpdateFromConfig;
                    } else {
                        stat.Value.Cfg.SettingChanged += UpdateFromConfig;
                    }

                }
                // Logger.LogInfo("Setup stat changes");

                // Modify the recipe in the object DB
                itemdef.Recipe.RecipeConfig.SettingChanged += (sender, args) => {
                    ConfigChangeDebouncer.Schedule(itemdef.Recipe.RecipeConfig, () => {
                        if (ValidateRecipeConfig(itemdef)) {
                            ModifyItemRecipeInODB(itemdef);
                        }
                    });
                };
                // Logger.LogInfo("Setup recipe changes");

                // Rescale the bulk recipe. Both its ingredient costs and how many it yields are derived from
                // the multiplier, so a change has to re-run each of them.
                if (itemdef.Recipe.BulkMultiplierCfg != null) {
                    itemdef.Recipe.BulkMultiplierCfg.SettingChanged += (_, _) => {
                        ConfigChangeDebouncer.Schedule(itemdef.Recipe.BulkMultiplierCfg, () => {
                            if (ValidateRecipeConfig(itemdef)) {
                                ModifyItemRecipeInODB(itemdef);
                            }
                            ModifyItemRecipeCraftAmount(itemdef, itemdef.CraftAmountCfg.Value);
                        });
                    };
                }

                //Modify the damage modifiers
                if (itemdef.DamageMods == null) { continue; }
                foreach (KeyValuePair<HitData.DamageType, HitCustomDamageMod> dmgmod in itemdef.DamageMods) {
                    dmgmod.Value.DmgModCfg.SettingChanged += (_, _) => {
                        ConfigChangeDebouncer.Schedule(dmgmod.Value.DmgModCfg, () => {
                            if (ZNet.instance == null || ZNet.instance.enabled == false) { return; }
                            HitData.DamageModifier modifier = (HitData.DamageModifier)Enum.Parse(typeof(HitData.DamageModifier), dmgmod.Value.DmgModCfg.Value);
                            // Update player items
                            UpdateItemInPlayerInventory(itemdef.Prefab, (ItemDrop.ItemData item) => { SetItemDamageModifier(modifier, dmgmod.Key, item); });
                            // Update world items, batched into a single scan to prevent lag spikes (e.g. on server config sync).
                            EnqueueWorldUpdate(itemdef.Prefab, (ItemDrop.ItemData item) => { SetItemDamageModifier(modifier, dmgmod.Key, item); });
                        });
                    };
                }
            }
            return true;
        }


        // Idempotently reconciles every item recipe in the live ObjectDB to the current config values.
        // Safe to call repeatedly and from multiple lifecycle events; self guards when no ObjectDB exists.
        private static void ReapplyAllRecipeConfig() {
            if (ObjectDB.instance == null || ObjectDB.instance.m_recipes == null) { return; }
            // Every item below looks its recipes up over a dozen times. Without the index each lookup is a
            // linear search of the whole recipe list reading native names (each call allocates), and this pass
            // runs on every client for every config sync.
            BuildRecipeIndexCache();
            try {
                foreach (ItemDefinition itemdef in resourceDefinitions) {
                    // Make sure the recipe is present before we try to modify it. A prior ObjectDB.CopyOtherDB
                    // (server join) can drop our custom recipes, so re-add disabled items too - their config is
                    // applied here and EnableDisableItemInDB sets the disabled flag last.
                    EnsureRecipeInDB(itemdef);
                    if (ValidateRecipeConfig(itemdef)) { ModifyItemRecipeInODB(itemdef); }
                    ModifyItemRecipeLevel(itemdef, itemdef.StationLVLCfg.Value);
                    ModifyItemRecipeCraftedAt(itemdef);
                    ModifyItemRecipeCraftAmount(itemdef, itemdef.CraftAmountCfg.Value);
                    EnableDisableItemInDB(itemdef, itemdef.CraftableCfg.Value);
                }
            } finally {
                recipeIndexByName = null;
                singleRecipeIndexByItem = null;
            }
            // Refresh an open crafting panel so changed recipes/amounts/enabled state are reflected immediately.
            if (Player.m_localPlayer != null) { Player.m_localPlayer.UpdateKnownRecipesList(); }
        }

        // Fires when admin (server) config is synchronized to this client. Only re-apply when our plugin's
        // config was part of the sync payload to avoid needless work when other mods sync.
        private static void OnModConfigsChanged(object sender, ConfigurationSynchronizationEventArgs e) {
            if (e.UpdatedPluginGUIDs != null && e.UpdatedPluginGUIDs.Count > 0 && !e.UpdatedPluginGUIDs.Contains(global::Clans.Clans.PluginGUID)) {
                return;
            }
            ReapplyAllRecipeConfig();
        }

        private static bool BatchAddItems() {
            foreach (ItemDefinition itemdef in resourceDefinitions) {
                GameObject ItemPrefab = Assets.LoadAsset<GameObject>($"Assets/Custom/Weapons/{itemdef.Category}/{itemdef.Prefab}.prefab");
                Sprite ItemSprite = string.IsNullOrEmpty(itemdef.Icon)
                    ? null
                    : Assets.LoadAsset<Sprite>($"Assets/Custom/Icons/{itemdef.Icon}.png");
                //Logger.LogInfo($"Adding {itemdef.Name} gopath: {prefabPath} go: {ItemPrefab} sprite: {ItemSprite}");
                ItemDrop ItemD = ItemPrefab.GetComponent<ItemDrop>();
                // Modify this items stats
                foreach (KeyValuePair<ItemStat, ItemStatConfig> modstat in itemdef.ModifableStats) {
                    if (modstat.Value.Configurable == false) {
                        ItemDataConfigModifier(modstat.Key, modstat.Value.Default_value, ItemD.m_itemData);
                    } else {
                        if (modstat.Value.IsInt) {
                            ItemDataConfigModifier(modstat.Key, modstat.Value.CfgInt.Value, ItemD.m_itemData);
                        } else {
                            ItemDataConfigModifier(modstat.Key, modstat.Value.Cfg.Value, ItemD.m_itemData);
                        }
                    }
                }
                // Modify this items resistances
                if (itemdef.DamageMods != null) {
                    foreach (KeyValuePair<HitData.DamageType, HitCustomDamageMod> dmgmod in itemdef.DamageMods) {
                        if (dmgmod.Value.Configurable == false || dmgmod.Value.DmgModCfg == null) { continue; }
                        HitData.DamageModifier modifier = (HitData.DamageModifier)Enum.Parse(typeof(HitData.DamageModifier), dmgmod.Value.DmgModCfg.Value);
                        SetItemDamageModifier(modifier, dmgmod.Key, ItemD.m_itemData);
                    }
                }
                if (ItemSprite == null) {
                    // ItemData.GetIcon indexes m_icons directly, so an item with no icon in the bundle and
                    // none authored on its prefab throws the moment the crafting list tries to draw it.
                    // Better to leave the item out than to take the inventory UI down with it.
                    Sprite[] prefabIcons = ItemD.m_itemData.m_shared.m_icons;
                    if (prefabIcons == null || prefabIcons.Length == 0) {
                        Logger.LogWarning($"{itemdef.Name} has no icon in the asset bundle and none on the {itemdef.Prefab} prefab, it will not be added. Export the icon and rebuild the asset bundle.");
                        continue;
                    }
                    // Not a problem: Icon is optional, and a definition that does not name one (or names one
                    // the bundle does not have) is meant to fall back to the prefab's own icon.
                    Logger.LogDebug($"No icon loaded from the asset bundle for {itemdef.Name}, using the icon on the {itemdef.Prefab} prefab.");
                }
                if (itemdef.Recipe.RecipeReqs == null || itemdef.Recipe.RecipeReqs.Count == 0) {
                    // No valid requirements resolved (empty/missing recipe definition and no usable config
                    // value). Registering a recipe with zero requirements would make the item free to craft,
                    // so register the item without one.
                    Logger.LogError($"Item '{itemdef.Name}' has no valid recipe requirements; registering it without a crafting recipe.");
                    // There is no ItemConfig on this path, so the bundle icon has to go on the prefab itself.
                    if (ItemSprite != null) { ItemD.m_itemData.m_shared.m_icons = new[] { ItemSprite }; }
                    ItemManager.Instance.AddItem(new CustomItem(ItemPrefab, fixReference: true));
                } else {
                    ItemConfig itemcfg = new ItemConfig() {
                        Amount = itemdef.CraftAmountCfg.Value,
                        CraftingStation = $"{itemdef.CraftedAtCfg.Value}",
                        MinStationLevel = itemdef.StationLVLCfg.Value,
                        // Always register as enabled so the recipe is added to and retained in the ObjectDB (a
                        // recipe registered disabled never gets cached/retained). The real craftable state is
                        // applied immediately after by ReapplyAllRecipeConfig -> EnableDisableItemInDB, so a
                        // disabled item still lives in the DB (m_enabled=false), stays modifiable, and re-enables
                        // correctly - including after a server ObjectDB copy replaces the recipe list.
                        Enabled = true,
                        Requirements = itemdef.Recipe.RecipeReqs.ToArray()
                    };
                    // Never leave this null: whether Jotunn skips a null Icons or assigns it straight through
                    // is its business, and either way an item registered without icons breaks the inventory UI.
                    itemcfg.Icons = ItemSprite != null ? new[] { ItemSprite } : ItemD.m_itemData.m_shared.m_icons;
                    ItemManager.Instance.AddItem(new CustomItem(ItemPrefab, fixReference: true, itemcfg));

                    // A CustomItem carries exactly one recipe, so the bulk variant is registered on its own.
                    // It goes in after the item so the single craft stays the first recipe listed for the
                    // item, which is the one ObjectDB.GetRecipe hands back to repair and tooltip lookups.
                    if (BulkMultiplier(itemdef) > 1) { AddBulkRecipe(itemdef); }
                }

                // This item needs to be included as a returnable arrow/bolt
                if (itemdef.Category == ItemCategory.Arrows) {
                    ArcheryAmmoToAdd.Add(itemdef.Prefab);
                }

                // This weapon is made from a cast that the Frost Foundry hardens
                if (itemdef.FrostForged != null) {
                    FrostForged.Register(itemdef, ItemD);
                }
            }
            return true;
        }



        // Registers the bulk ("craft N at once") companion recipe for an item. Seeded from the current config
        // values; from here on it is kept in step with the single craft recipe by ResolveOwnedRecipes callers.
        // Registered enabled for the same reason the item itself is - see the ItemConfig comment above.
        private static void AddBulkRecipe(ItemDefinition itemdef) {
            int multiplier = BulkMultiplier(itemdef);
            List<RequirementConfig> requirements = new List<RequirementConfig>();
            foreach (RequirementConfig req in itemdef.Recipe.RecipeReqs) {
                requirements.Add(new RequirementConfig {
                    Item = req.Item,
                    Amount = req.Amount * multiplier,
                    AmountPerLevel = req.AmountPerLevel * multiplier,
                    Recover = req.Recover
                });
            }
            ItemManager.Instance.AddRecipe(new CustomRecipe(new RecipeConfig {
                Name = BulkRecipeName(itemdef),
                Item = itemdef.Prefab,
                Amount = itemdef.CraftAmountCfg.Value * multiplier,
                CraftingStation = itemdef.CraftedAtCfg.Value,
                RepairStation = itemdef.CraftedAtCfg.Value,
                MinStationLevel = itemdef.StationLVLCfg.Value,
                Enabled = true,
                Requirements = requirements.ToArray()
            }));
        }

        private static void ItemDataConfigModifier(ItemStat target_attribute, float updatedValue, ItemDrop.ItemData itemData) {
            if (itemData == null) { return; }
            switch (target_attribute) {
                // Standard Dmg types
                case ItemStat.slash:
                    itemData.m_shared.m_damages.m_slash = updatedValue;
                    break;
                case ItemStat.slash_per_level:
                    itemData.m_shared.m_damagesPerLevel.m_slash = updatedValue;
                    break;
                case ItemStat.blunt:
                    itemData.m_shared.m_damages.m_blunt = updatedValue;
                    break;
                case ItemStat.blunt_per_level:
                    itemData.m_shared.m_damagesPerLevel.m_blunt = updatedValue;
                    break;
                case ItemStat.pierce:
                    itemData.m_shared.m_damages.m_pierce = updatedValue;
                    break;
                case ItemStat.pierce_per_level:
                    itemData.m_shared.m_damagesPerLevel.m_pierce = updatedValue;
                    break;
                // Special Damage Types
                case ItemStat.pickaxe:
                    itemData.m_shared.m_damages.m_pickaxe = updatedValue;
                    break;
                case ItemStat.pickaxe_per_level:
                    itemData.m_shared.m_damagesPerLevel.m_pickaxe = updatedValue;
                    break;
                case ItemStat.chop:
                    itemData.m_shared.m_damages.m_chop = updatedValue;
                    break;
                case ItemStat.chop_per_level:
                    itemData.m_shared.m_damagesPerLevel.m_chop = updatedValue;
                    break;
                case ItemStat.attack_force:
                    itemData.m_shared.m_attackForce = updatedValue;
                    break;
                case ItemStat.secondary_attack_force_multiply:
                    itemData.m_shared.m_secondaryAttack.m_forceMultiplier = updatedValue;
                    break;
                case ItemStat.primary_attack_force_multiply:
                    itemData.m_shared.m_attack.m_forceMultiplier = updatedValue;
                    break;
                // Elemental Damage Types
                case ItemStat.fire:
                    itemData.m_shared.m_damages.m_fire = updatedValue;
                    break;
                case ItemStat.fire_per_level:
                    itemData.m_shared.m_damagesPerLevel.m_fire = updatedValue;
                    break;
                case ItemStat.lightning:
                    itemData.m_shared.m_damages.m_lightning = updatedValue;
                    break;
                case ItemStat.lightning_per_level:
                    itemData.m_shared.m_damagesPerLevel.m_lightning = updatedValue;
                    break;
                case ItemStat.frost:
                    itemData.m_shared.m_damages.m_frost = updatedValue;
                    break;
                case ItemStat.frost_per_level:
                    itemData.m_shared.m_damagesPerLevel.m_frost = updatedValue;
                    break;
                case ItemStat.poison:
                    itemData.m_shared.m_damages.m_poison = updatedValue;
                    break;
                case ItemStat.poison_per_level:
                    itemData.m_shared.m_damagesPerLevel.m_poison = updatedValue;
                    break;
                case ItemStat.spirit:
                    itemData.m_shared.m_damages.m_spirit = updatedValue;
                    break;
                case ItemStat.spirit_per_level:
                    itemData.m_shared.m_damagesPerLevel.m_spirit = updatedValue;
                    break;
                // Block and parry
                case ItemStat.block_armor:
                    itemData.m_shared.m_blockPower = updatedValue;
                    break;
                case ItemStat.block_armor_per_level:
                    itemData.m_shared.m_blockPowerPerLevel = updatedValue;
                    break;
                case ItemStat.parry:
                    itemData.m_shared.m_timedBlockBonus = updatedValue;
                    break;
                case ItemStat.block_force:
                    itemData.m_shared.m_deflectionForce = updatedValue;
                    break;
                case ItemStat.block_force_per_level:
                    itemData.m_shared.m_deflectionForcePerLevel = updatedValue;
                    break;
                // Costs for attack types
                case ItemStat.primary_attack_stamina:
                    itemData.m_shared.m_attack.m_attackStamina = updatedValue;
                    break;
                case ItemStat.primary_attack_eitr:
                    itemData.m_shared.m_attack.m_attackEitr = updatedValue;
                    break;
                case ItemStat.primary_attack_flat_health_cost:
                    itemData.m_shared.m_attack.m_attackHealth = updatedValue;
                    break;
                case ItemStat.primary_attack_percent_health_cost:
                    itemData.m_shared.m_attack.m_attackHealthPercentage = updatedValue;
                    break;
                case ItemStat.primary_attack_health_returned:
                    itemData.m_shared.m_attack.m_attackHealthReturnHit = updatedValue;
                    break;
                case ItemStat.primary_attack_damage_bonus_per_missing_hp:
                    itemData.m_shared.m_attack.m_damageMultiplierPerMissingHP = updatedValue;
                    break;
                case ItemStat.primary_attack_projectile_count:
                    itemData.m_shared.m_attack.m_projectiles = (int)updatedValue;
                    break;
                case ItemStat.secondary_attack_stamina:
                    itemData.m_shared.m_secondaryAttack.m_attackStamina = updatedValue;
                    break;
                case ItemStat.secondary_attack_eitr:
                    itemData.m_shared.m_secondaryAttack.m_attackEitr = updatedValue;
                    break;
                case ItemStat.secondary_attack_flat_health_cost:
                    itemData.m_shared.m_secondaryAttack.m_attackHealth = updatedValue;
                    break;
                case ItemStat.secondary_attack_percent_health_cost:
                    itemData.m_shared.m_secondaryAttack.m_attackHealthPercentage = updatedValue;
                    break;
                // Speed Modifiers
                case ItemStat.movement_speed:
                    itemData.m_shared.m_movementModifier = updatedValue;
                    break;
                case ItemStat.bow_draw_speed:
                    itemData.m_shared.m_attack.m_drawDurationMin = updatedValue;
                    break;
                case ItemStat.crossbow_reload_speed:
                    itemData.m_shared.m_attack.m_reloadTime = updatedValue;
                    break;
                case ItemStat.crossbow_reload_stamina_drain:
                    itemData.m_shared.m_attack.m_reloadStaminaDrain = updatedValue;
                    break;
                case ItemStat.draw_stamina_drain:
                    itemData.m_shared.m_attack.m_drawStaminaDrain = updatedValue;
                    break;
                case ItemStat.projectile_velocity:
                    itemData.m_shared.m_attack.m_projectileVel = updatedValue;
                    break;
                case ItemStat.projectile_accuracy_max:
                    itemData.m_shared.m_attack.m_projectileAccuracy = (100f - updatedValue);
                    break;
                // Item Modifiers
                case ItemStat.durability:
                    itemData.m_shared.m_maxDurability = updatedValue;
                    break;
                case ItemStat.durability_per_level:
                    itemData.m_shared.m_durabilityPerLevel = updatedValue;
                    break;
                case ItemStat.max_item_level:
                    itemData.m_shared.m_maxQuality = (int)updatedValue;
                    break;
                case ItemStat.tool_level:
                    itemData.m_shared.m_toolTier = (int)updatedValue;
                    break;
                default:
                    Logger.LogWarning($"Unknown item stat {target_attribute} for {itemData.m_shared.m_name}");
                    break;
            }
        }

        private static bool ValidateRecipeConfig(ItemDefinition itemdef) {
            List<RequirementConfig> requirements = new List<RequirementConfig>();
            try {
                string[] recipeConfig = itemdef.Recipe.RecipeConfig.Value.Split('|');
                foreach (string ingredient in recipeConfig) {
                    // Logger.LogInfo($"Ingrediant details: {ingredient}");
                    string[] ingredientConfig = ingredient.Split(',');
                    if (ingredientConfig.Length == 1) {
                        // This is the first run or deleted config entry scenario
                        return false;
                    }
                    if (ingredientConfig.Length != 3) {
                        Logger.LogWarning($"Invalid ({itemdef.Name}) recipe config detected: {ingredient}. Needs three entries eg: Wood,1,1");
                        return false;
                    }
                    requirements.Add(new RequirementConfig { Item = ingredientConfig[0], Amount = int.Parse(ingredientConfig[1]), AmountPerLevel = int.Parse(ingredientConfig[2]) });
                }
                // Only happens if the recipe is valid
                itemdef.Recipe.RecipeReqs = requirements;
                return true;
            } catch {
                Logger.LogWarning($"Recipe is Invalid. Should have the format of Wood,1,1|Stone,2,0 - Prefab,cost,upgrade.");
                return false;
            }
        }

        private static void BuildRecipeReqsFromDefault(ItemDefinition itemdef) {
            List<RequirementConfig> requirements = new List<RequirementConfig>();
            foreach (var recipeIng in itemdef.Recipe.RecipeItems) {
                requirements.Add(new RequirementConfig { Item = recipeIng.Prefab, Amount = recipeIng.Amount, AmountPerLevel = recipeIng.UpgradeCost });
            }
            itemdef.Recipe.RecipeReqs = requirements;
        }

        private static string BuildStringRecipeFromItemDef(ItemDefinition itemdef) {
            List<string> recipe = new();
            foreach (var req in itemdef.Recipe.RecipeItems) {
                recipe.Add($"{req.Prefab},{req.Amount},{req.UpgradeCost}");
            }
            return string.Join("|", recipe);
        }

        private static bool ModifyItemRecipeCraftedAt(ItemDefinition itemdef) {
            List<KeyValuePair<Recipe, int>> recipes = ResolveOwnedRecipes(itemdef, "crafting station");
            if (recipes.Count == 0) { return false; }

            CraftingStation craftable_at = PrefabManager.Instance.GetPrefab(itemdef.CraftedAtCfg.Value)?.GetComponent<CraftingStation>();
            if (craftable_at == null) {
                Logger.LogWarning($"Crafting Station {itemdef.CraftedAtCfg.Value} prefab not found, or does not have a crafting station componet.");
                return false;
            }
            foreach (KeyValuePair<Recipe, int> recipe in recipes) {
                recipe.Key.m_craftingStation = craftable_at;
                // repair station should likely be split out into a seperate config
                recipe.Key.m_repairStation = craftable_at;
            }
            SyncFrostForgedRecipes(itemdef);
            return true;
        }


        private static void ModifyItemRecipeInODB(ItemDefinition itemdef) {
            // if (itemdef.enabled == false) { return; }
            // Logger.LogInfo($"Modifying {itemdef.Name} recipe in OODB");
            List<KeyValuePair<Recipe, int>> recipes = ResolveOwnedRecipes(itemdef, "recipe");
            if (recipes.Count == 0) { return; }

            // Resolved once; the bulk variant reuses the same ItemDrops at a multiplied cost.
            List<Piece.Requirement> newRequirements = new List<Piece.Requirement>();
            foreach (var req in itemdef.Recipe.RecipeReqs) {
                GameObject resgo = ObjectDB.instance.GetItemPrefab(req.Item);
                if (resgo == null) {
                    Logger.LogWarning($"Recipe of {itemdef.Prefab} has an invalid requirement {req.Item}.");
                    return;
                }
                newRequirements.Add(new Piece.Requirement { m_resItem = resgo.GetComponent<ItemDrop>(), m_amount = req.Amount, m_amountPerLevel = req.AmountPerLevel });
            }

            foreach (KeyValuePair<Recipe, int> recipe in recipes) {
                recipe.Key.m_resources = ScaleRequirements(newRequirements, recipe.Value);
            }
            SyncFrostForgedRecipes(itemdef);
        }

        // For a frost forged weapon (see FrostForged): makes its recipe upgrade only, with its mould among the
        // requirements as vanilla's gold weapons have, and mirrors that recipe onto the one that crafts the cast:
        // same station, level, amount, enabled state and ingredients, less any Forge of Potential idol. Call after
        // any change to the weapon's recipe. If the mould or cast is missing the weapon stays craftable directly,
        // so it can still be made.
        private static void SyncFrostForgedRecipes(ItemDefinition itemdef) {
            FrostForgedDefinition frostForged = itemdef.FrostForged;
            if (frostForged == null || ObjectDB.instance == null || ObjectDB.instance.m_recipes == null) { return; }
            int index = GetRecipeIndex(BaseRecipeName(itemdef), itemdef.Prefab);
            if (index == -1) { return; }
            Recipe weaponRecipe = ObjectDB.instance.m_recipes[index];
            ItemDrop mould = ObjectDB.instance.GetItemPrefab(frostForged.MouldPrefab)?.GetComponent<ItemDrop>();
            ItemDrop cast = ObjectDB.instance.GetItemPrefab(frostForged.CastPrefab)?.GetComponent<ItemDrop>();
            if (mould == null || cast == null) {
                weaponRecipe.m_noCraftOnlyUpgrade = false;
                return;
            }
            weaponRecipe.m_noCraftOnlyUpgrade = true;
            if (weaponRecipe.m_resources.Any(req => req.m_resItem == mould) == false) {
                // One mould per cast and none for upgrades, listed first as vanilla lists its moulds.
                Piece.Requirement mouldReq = new Piece.Requirement { m_resItem = mould, m_amount = 1, m_amountPerLevel = 0 };
                weaponRecipe.m_resources = new[] { mouldReq }.Concat(weaponRecipe.m_resources).ToArray();
            }

            string castRecipeName = $"Recipe_{frostForged.CastPrefab}";
            if (frostForged.CastRecipe == null) {
                frostForged.CastRecipe = ScriptableObject.CreateInstance<Recipe>();
                frostForged.CastRecipe.name = castRecipeName;
            }
            Recipe castRecipe = frostForged.CastRecipe;
            castRecipe.m_item = cast;
            castRecipe.m_amount = weaponRecipe.m_amount;
            castRecipe.m_enabled = weaponRecipe.m_enabled;
            castRecipe.m_craftingStation = weaponRecipe.m_craftingStation;
            castRecipe.m_repairStation = weaponRecipe.m_repairStation;
            castRecipe.m_minStationLevel = weaponRecipe.m_minStationLevel;
            castRecipe.m_resources = weaponRecipe.m_resources.Where(req => req.m_upgraderResource == false).ToArray();
            // Not registered through Jotunn, so it is put back here whenever a new ObjectDB lacks it.
            if (GetRecipeIndex(castRecipeName, null) == -1) {
                AddRecipeToDB(castRecipe);
            }
        }

        // Copies a requirement list with every cost multiplied. Always returns a fresh array, so the single
        // craft and bulk recipes never end up sharing one.
        private static Piece.Requirement[] ScaleRequirements(List<Piece.Requirement> requirements, int multiplier) {
            Piece.Requirement[] scaled = new Piece.Requirement[requirements.Count];
            for (int i = 0; i < requirements.Count; i++) {
                scaled[i] = new Piece.Requirement {
                    m_resItem = requirements[i].m_resItem,
                    m_amount = requirements[i].m_amount * multiplier,
                    m_amountPerLevel = requirements[i].m_amountPerLevel * multiplier
                };
            }
            return scaled;
        }

        // Re-add itemdef's cached recipes to the live ObjectDB if a prior ObjectDB.CopyOtherDB (server join)
        // replaced m_recipes and dropped them. Keeps even disabled items present so their recipe can still be
        // modified and correctly re-enabled later.
        private static void EnsureRecipeInDB(ItemDefinition itemdef) {
            if (ObjectDB.instance == null || ObjectDB.instance.m_recipes == null) { return; }
            if (GetRecipeIndex(BaseRecipeName(itemdef), itemdef.Prefab) == -1 && itemdef.Recipe.ResolvedRecipe != null) {
                AddRecipeToDB(itemdef.Recipe.ResolvedRecipe);
            }
            if (BulkMultiplier(itemdef) > 1 && GetRecipeIndex(BulkRecipeName(itemdef), null) == -1 && itemdef.Recipe.ResolvedBulkRecipe != null) {
                AddRecipeToDB(itemdef.Recipe.ResolvedBulkRecipe);
            }
        }

        // Appends a recipe to the live ObjectDB, recording it in an in-progress pass's index so the rest of
        // that pass can find it.
        private static void AddRecipeToDB(Recipe recipe) {
            ObjectDB.instance.m_recipes.Add(recipe);
            if (recipeIndexByName != null) { IndexRecipe(recipe, ObjectDB.instance.m_recipes.Count - 1); }
        }

        private static void EnableDisableItemInDB(ItemDefinition itemdef, bool enable) {
            if (ObjectDB.instance == null || ObjectDB.instance.m_recipes == null) { return; }
            // A recipe may have been dropped (e.g. server ObjectDB copy). Re-add our cached one so a disabled
            // item still lives in the DB and can be modified / re-enabled later.
            EnsureRecipeInDB(itemdef);
            foreach (KeyValuePair<Recipe, int> recipe in ResolveOwnedRecipes(itemdef, "craftable state")) {
                recipe.Key.m_enabled = enable;
            }
            SyncFrostForgedRecipes(itemdef);
        }

        private static void ModifyItemRecipeLevel(ItemDefinition itemdef, int level) {
            // if (itemdef.enabled == false) { return; }
            foreach (KeyValuePair<Recipe, int> recipe in ResolveOwnedRecipes(itemdef, "required level")) {
                recipe.Key.m_minStationLevel = level;
            }
            SyncFrostForgedRecipes(itemdef);
        }

        private static void ModifyItemRecipeCraftAmount(ItemDefinition itemdef, int amount) {
            foreach (KeyValuePair<Recipe, int> recipe in ResolveOwnedRecipes(itemdef, "craft amount")) {
                recipe.Key.m_amount = amount * recipe.Value;
            }
            SyncFrostForgedRecipes(itemdef);
        }

        // Jotunn names the recipe it builds from an ItemConfig "Recipe_<prefab>"; the bulk variant is
        // registered under a name of our own. Both point at the same ItemDrop, which is why every lookup below
        // goes by recipe name - matching on the crafted item would resolve the two to whichever the ObjectDB
        // happens to list first.
        private const string BulkRecipeSuffix = "_Bulk";
        private static string BaseRecipeName(ItemDefinition itemdef) => $"Recipe_{itemdef.Prefab}";
        private static string BulkRecipeName(ItemDefinition itemdef) => $"Recipe_{itemdef.Prefab}{BulkRecipeSuffix}";

        // The live bulk multiplier: the bound config once it exists, otherwise the definition's own value.
        // Anything below 2 means the item has no bulk recipe.
        private static int BulkMultiplier(ItemDefinition itemdef) =>
            itemdef.Recipe.BulkMultiplierCfg != null ? itemdef.Recipe.BulkMultiplierCfg.Value : itemdef.Recipe.BulkMultiplier;

        // Looks up one of our recipes by its own name, falling back to the crafted item (the match this
        // replaced) for a recipe that does not carry that name. The fallback skips bulk variants so it can
        // only ever land on a single-craft recipe. Pass a null prefab to look up by name only.
        private static int GetRecipeIndex(string recipeName, string prefab) {
            if (recipeIndexByName != null) {
                if (recipeIndexByName.TryGetValue(recipeName, out int byName)) { return byName; }
                if (prefab == null) { return -1; }
                return singleRecipeIndexByItem.TryGetValue(prefab, out int byItem) ? byItem : -1;
            }
            int index = ObjectDB.instance.m_recipes.FindIndex(m => m.name == recipeName);
            if (index != -1 || prefab == null) { return index; }
            return ObjectDB.instance.m_recipes.FindIndex(m => m.m_item != null && m.m_item.name == prefab && m.name.EndsWith(BulkRecipeSuffix) == false);
        }

        // Indexes the live recipe list for one ReapplyAllRecipeConfig pass, mirroring the two searches above.
        private static void BuildRecipeIndexCache() {
            recipeIndexByName = new Dictionary<string, int>();
            singleRecipeIndexByItem = new Dictionary<string, int>();
            List<Recipe> recipes = ObjectDB.instance.m_recipes;
            for (int i = 0; i < recipes.Count; i++) { IndexRecipe(recipes[i], i); }
        }

        // Records one recipe under its name, and under its crafted item unless it is a bulk variant. The
        // first occurrence wins in both, so lookups land on the same recipe the FindIndex calls would.
        private static void IndexRecipe(Recipe recipe, int index) {
            if (recipe == null) { return; }
            string name = recipe.name;
            if (recipeIndexByName.ContainsKey(name) == false) { recipeIndexByName.Add(name, index); }
            if (recipe.m_item == null || name.EndsWith(BulkRecipeSuffix)) { return; }
            string item = recipe.m_item.name;
            if (singleRecipeIndexByItem.ContainsKey(item) == false) { singleRecipeIndexByItem.Add(item, index); }
        }

        // Every recipe this definition owns in the live ObjectDB - its own, plus the bulk variant when one is
        // configured - paired with the factor its amounts are scaled by (1 for the single craft, N for bulk).
        // Also refreshes the cached references EnsureRecipeInDB re-adds after a server ObjectDB copy. The
        // context argument names what the caller is about to change, for the not-found warning.
        private static List<KeyValuePair<Recipe, int>> ResolveOwnedRecipes(ItemDefinition itemdef, string context) {
            List<KeyValuePair<Recipe, int>> resolved = new List<KeyValuePair<Recipe, int>>();
            if (ObjectDB.instance == null || ObjectDB.instance.m_recipes == null) { return resolved; }

            int index = GetRecipeIndex(BaseRecipeName(itemdef), itemdef.Prefab);
            if (index == -1) {
                Logger.LogWarning($"Recipe of {itemdef.Prefab} not found in ObjectDB, {context} will not be modified.");
            } else {
                itemdef.Recipe.ResolvedRecipe = ObjectDB.instance.m_recipes[index];
                resolved.Add(new KeyValuePair<Recipe, int>(itemdef.Recipe.ResolvedRecipe, 1));
            }

            int multiplier = BulkMultiplier(itemdef);
            if (multiplier < 2) { return resolved; }

            int bulkIndex = GetRecipeIndex(BulkRecipeName(itemdef), null);
            if (bulkIndex == -1) {
                Logger.LogWarning($"Bulk recipe of {itemdef.Prefab} not found in ObjectDB, {context} will not be modified.");
            } else {
                itemdef.Recipe.ResolvedBulkRecipe = ObjectDB.instance.m_recipes[bulkIndex];
                resolved.Add(new KeyValuePair<Recipe, int>(itemdef.Recipe.ResolvedBulkRecipe, multiplier));
            }
            return resolved;
        }

        private static void SetItemDamageModifier(HitData.DamageModifier modifier, HitData.DamageType type, ItemDrop.ItemData itemData) {
            // Logger.LogInfo($"Setting {itemData.m_shared.m_name} damage modifier {modifier} for {type}");
            List<HitData.DamageModPair> temp = itemData.m_shared.m_damageModifiers.Where(entry => entry.m_type != type).ToList();
            if (temp.Count == 0) {
                itemData.m_shared.m_damageModifiers.Clear();
                itemData.m_shared.m_damageModifiers.Add(new HitData.DamageModPair() { m_modifier = modifier, m_type = type });
            } else {
                temp.Add(new HitData.DamageModPair() { m_modifier = modifier, m_type = type });
                itemData.m_shared.m_damageModifiers = temp;
            }
        }

        private static void UpdateItemInPlayerInventory(string prefab, Action<ItemDrop.ItemData> callback) {
            if (Player.m_localPlayer == null) { return; }
            foreach (ItemDrop.ItemData user_item in Player.m_localPlayer.m_inventory.GetAllItems()) {
                if (user_item == null) { continue; }
                if (user_item.m_dropPrefab.name != prefab) { continue; }
                callback(user_item);
            }
        }

        // Queues an in-world item update. All updates enqueued within a frame are applied together in a single
        // pass (see DrainWorldUpdates), collapsing N passes - one per changed setting during a config sync - into one.
        private static void EnqueueWorldUpdate(string prefab, Action<ItemDrop.ItemData> callback) {
            // Skip during game shutdown: the ThreadingHelper's MonoBehaviour is destroyed (StartCoroutine
            // would throw) and there are no in-world items left to update. Unity's '==' treats it as null.
            BepInEx.ThreadingHelper host = BepInEx.ThreadingHelper.Instance;
            if (host == null) { return; }
            pendingWorldUpdates.Add(new KeyValuePair<string, Action<ItemDrop.ItemData>>(prefab, callback));
            if (worldUpdateScheduled) { return; }
            worldUpdateScheduled = true;
            host.StartCoroutine(DrainWorldUpdates());
        }

        // Applies all queued in-world item updates to each prefab and its live drops, in a single pass.
        private static IEnumerator DrainWorldUpdates() {
            // Wait a frame so the full burst of SettingChanged handlers (e.g. an entire config sync) enqueues first.
            yield return null;
            try {
                if (pendingWorldUpdates.Count > 0) {
                    // Grouped by prefab, so each live drop costs one lookup however many updates are queued.
                    Dictionary<string, List<Action<ItemDrop.ItemData>>> updatesByPrefab = new Dictionary<string, List<Action<ItemDrop.ItemData>>>();
                    foreach (KeyValuePair<string, Action<ItemDrop.ItemData>> update in pendingWorldUpdates) {
                        if (updatesByPrefab.TryGetValue(update.Key, out List<Action<ItemDrop.ItemData>> callbacks) == false) {
                            callbacks = new List<Action<ItemDrop.ItemData>>();
                            updatesByPrefab.Add(update.Key, callbacks);
                        }
                        callbacks.Add(update.Value);
                    }
                    // Exact name matches only (see LiveItemDrops).
                    LiveItemDrops.ForEach(updatesByPrefab.Keys, (prefab, id) => {
                        foreach (Action<ItemDrop.ItemData> callback in updatesByPrefab[prefab]) {
                            callback(id.m_itemData);
                        }
                    });
                }
            } finally {
                pendingWorldUpdates.Clear();
                worldUpdateScheduled = false;
            }
        }

        static void AddAmmoItemsToArcheryTarget() {
            GameObject archerTarget = PrefabManager.Instance.GetPrefab("piece_ArcheryTarget");
            ArcheryTarget ArcherAmmoManger = archerTarget.GetComponentInChildren<ArcheryTarget>(true);

            foreach (string ammoPrefab in ArcheryAmmoToAdd) {
                Logger.LogDebug($"Adding {ammoPrefab} to Archery Target Ammo Return.");
                ItemDrop ammoID = PrefabManager.Instance.GetPrefab(ammoPrefab).GetComponent<ItemDrop>();
                if (ammoID != null && ArcherAmmoManger.m_returnAmmo.Contains(ammoID) == false) {
                    ArcherAmmoManger.m_returnAmmo.Add(ammoID);
                }
            }
        }
    }
}
