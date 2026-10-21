using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Server {
    // Forwards a share of each player's skill gains to their squad. The client reports what it gained (as
    // the factor passed to Skills.RaiseSkill) every couple of seconds; the server decides who gets a share:
    // squadmates within range, or clanmates in the squad at any distance (if allowed). Clanmates get the share
    // and range of the gainer's clan level (levels.yaml), anyone else the .cfg's. Clamped per sender, so a
    // modified client cannot pour experience into its friends.
    internal static class XpShareService {
        private const int MaxEntries = 64;

        // How much each sender may still share: it refills at the allowed rate (up to half a minute's worth)
        // however often batches come, so sending many small batches gains nothing.
        private const float BudgetSeconds = 30f;

        private sealed class Budget {
            internal float Left;
            internal float At;
        }

        private static readonly Dictionary<string, Budget> budgets = new Dictionary<string, Budget>();

        internal static void Register() {
            ClanNet.RegisterServer(Op.SkillXpBatch, OnBatch);
        }

        internal static void Reset() => budgets.Clear();

        private static void OnBatch(ServerRequest request) {
            int count = request.Body.ReadInt();
            if (count <= 0 || count > MaxEntries) { return; }
            List<KeyValuePair<int, float>> gains = new List<KeyValuePair<int, float>>();
            float total = 0f;
            for (int i = 0; i < count; i++) {
                int skill = request.Body.ReadInt();
                float factor = request.Body.ReadSingle();
                if (factor <= 0f || float.IsNaN(factor) || float.IsInfinity(factor)) { continue; }
                gains.Add(new KeyValuePair<int, float>(skill, factor));
                total += factor;
            }
            if (gains.Count == 0 || ClansConfig.XpShareEnabled.Value == false || PartyService.Enabled == false) { return; }

            ClanSender sender = request.Sender;
            PartyService.Party party = PartyService.PartyOf(sender.Account);
            if (party == null || party.Members.Count < 2) { return; }

            float now = Time.unscaledTime;
            float rate = Mathf.Max(0f, ClansConfig.XpShareMaxFactorPerSecond.Value);
            if (budgets.TryGetValue(sender.Account, out Budget budget) == false) {
                budget = new Budget { Left = rate * ClansConfig.XpShareBatchSeconds.Value, At = now };
                budgets[sender.Account] = budget;
            }
            budget.Left = Mathf.Min(rate * BudgetSeconds, budget.Left + (now - budget.At) * rate);
            budget.At = now;
            float granted = Mathf.Min(total, budget.Left);
            budget.Left -= granted;
            float scale = total > 0f ? granted / total : 0f;
            if (scale <= 0f) { return; }

            // Clanmates share as their clan's level allows; anyone else in the party by the .cfg values.
            ClanRecord senderClan = ClanRules.ClanOf(sender.Account);
            ClanLevelDef perks = senderClan != null ? ClanRules.Perks(senderClan) : null;
            Vector3 senderPos = sender.Position;
            foreach (string member in party.Members) {
                if (member == sender.Account) { continue; }
                ClanSender to = Sessions.Get(member);
                if (to == null) { continue; }
                bool clanmate = perks != null && (ClanRules.Account(member)?.ClanId ?? 0) == senderClan.Id;
                float fraction = (clanmate ? perks.XpShareFraction : ClansConfig.XpShareFraction.Value) * scale;
                float range = clanmate ? perks.XpShareRange : ClansConfig.XpShareRange.Value;
                bool anyDistance = clanmate && perks.XpShareAnyDistance && ClansConfig.XpShareClanmatesAnyDistance.Value;
                if (fraction <= 0f) { continue; }
                if (anyDistance == false && Utils.DistanceXZ(senderPos, to.Position) > range) { continue; }
                ClanNet.Send(to, Op.SkillXpShare, 0, pkg => {
                    pkg.Write(sender.CharacterName);
                    pkg.Write(gains.Count);
                    foreach (KeyValuePair<int, float> gain in gains) {
                        pkg.Write(gain.Key);
                        pkg.Write(gain.Value * fraction);
                    }
                });
            }
        }
    }
}
