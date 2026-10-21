using Clans.Net;
using Clans.Settings;
using HarmonyLib;
using System.Collections.Generic;

namespace Clans.Client {
    // Reports this player's skill gains for their squad, and applies the shares squadmates send.
    //
    // Hooks Skills.RaiseSkill, the one funnel every skill gain goes through (some callers skip
    // Player.RaiseSkill). The factor recorded is what was passed in, after status effects; the recipient
    // raises their own skill by its share through Skills.RaiseSkill too, so their skill gain rate applies
    // but no status effect is counted twice. A received share is never reported again.
    internal static class SkillShareClient {
        private static readonly Dictionary<Skills.SkillType, float> pending = new Dictionary<Skills.SkillType, float>();
        private static bool applyingShare;
        private static float flushTimer;

        internal static void Init() {
            ClanNet.RegisterClient(Op.SkillXpShare, OnShare);
        }

        internal static void Reset() {
            pending.Clear();
            flushTimer = 0f;
        }

        [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
        private static class RaiseSkillPatch {
            private static void Postfix(Skills __instance, Skills.SkillType skillType, float factor) {
                if (applyingShare || factor <= 0f || skillType == Skills.SkillType.None) { return; }
                if (__instance.m_player == null || __instance.m_player != Player.m_localPlayer) { return; }
                if (PartyClient.InParty == false || ClansConfig.XpShareEnabled.Value == false) { return; }
                if (ClansConfig.IsSkillExcluded(skillType)) { return; }
                pending.TryGetValue(skillType, out float total);
                pending[skillType] = total + factor;
            }
        }

        internal static void Tick(float dt) {
            flushTimer -= dt;
            if (flushTimer > 0f) { return; }
            flushTimer = ClansConfig.XpShareBatchSeconds.Value;
            if (pending.Count == 0) { return; }
            if (PartyClient.InParty == false) {
                pending.Clear();
                return;
            }
            List<KeyValuePair<Skills.SkillType, float>> batch = new List<KeyValuePair<Skills.SkillType, float>>(pending);
            pending.Clear();
            ClanRequests.Report(Op.SkillXpBatch, pkg => {
                pkg.Write(batch.Count);
                foreach (KeyValuePair<Skills.SkillType, float> gain in batch) {
                    pkg.Write((int)gain.Key);
                    pkg.Write(gain.Value);
                }
            });
        }

        private static void OnShare(uint reqId, ZPackage body) {
            string from = body.ReadString();
            int count = body.ReadInt();
            Player player = Player.m_localPlayer;
            Skills skills = player != null ? player.GetSkills() : null;
            for (int i = 0; i < count; i++) {
                Skills.SkillType skill = (Skills.SkillType)body.ReadInt();
                float factor = body.ReadSingle();
                // A skill another mod added that this player does not have would throw inside RaiseSkill.
                if (skills == null || factor <= 0f || skills.GetSkillDef(skill) == null || ClansConfig.IsSkillExcluded(skill)) { continue; }
                applyingShare = true;
                try {
                    skills.RaiseSkill(skill, factor);
                } finally {
                    applyingShare = false;
                }
            }
            Logger.LogDebug($"Received a skill share from {from} ({count} skills).");
        }
    }
}
