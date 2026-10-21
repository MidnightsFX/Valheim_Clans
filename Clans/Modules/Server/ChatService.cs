using Clans.Core;
using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using Splatform;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Clans.Server {
    // Clans' chat channels: Global (everyone on the server), Clan, Party and Raid. Vanilla's say, shout and
    // whisper keep going over vanilla's own network; these go over Clans', so membership is checked here and
    // a message only reaches the people it is meant for.
    //
    // Messages can carry links: items the sender shows off (their data travels with the message), and
    // invites to join a party or a clan that the server issues when a player posts one.
    //
    // Vanilla's say, shout and whisper carry text only, so their links ride beside them: the sender registers
    // the links here first, they go to everyone who will hear the message, and only after the answer does the
    // sender say it. Both travel over the same connection, so the links always arrive first; the chat panel
    // then turns the matching names in that sender's line into links.
    internal static class ChatService {
        private const int RateWindowSeconds = 10;
        private const long InviteSeconds = 15 * 60;
        // Vanilla hears say within 15 m and whisper within 4 m (Talker); positions here lag a little, so wider.
        private const float SayRange = 40f;
        private const float WhisperRange = 15f;

        private sealed class ChatInvite {
            internal long Id;
            internal ChatLinkKind Kind;
            internal long TargetId;
            internal string Poster;
            internal long ExpiresUtc;
        }

        private static readonly Dictionary<long, ChatInvite> invites = new Dictionary<long, ChatInvite>();
        private static readonly Dictionary<string, Queue<float>> recent = new Dictionary<string, Queue<float>>();
        private static long nextInviteId = 1;

        internal static void Register() {
            ClanNet.RegisterServer(Op.ChatSend, OnSend);
            ClanNet.RegisterServer(Op.ChatPostInvite, OnPostInvite);
            ClanNet.RegisterServer(Op.ChatJoinInvite, OnJoinInvite);
            ClanNet.RegisterServer(Op.ChatAttach, OnAttach);
        }

        internal static void Reset() {
            invites.Clear();
            recent.Clear();
        }

        internal static bool IsClansChannel(ChatChannel channel) => channel >= ChatChannel.Global && channel <= ChatChannel.Raid;

        // ---------------------------------------------------------------- messages

        private static void OnSend(ServerRequest request) {
            ChatChannel channel = (ChatChannel)request.Body.ReadByte();
            string text = request.Body.ReadString();
            int count = request.Body.ReadInt();
            if (count < 0 || count > ChatLimits.MaxLinks) { request.Fail("$clans_err_bad_request"); return; }
            List<ChatLink> links = ReadItemLinks(request, count);
            if (links == null) { return; }

            text = Clean(text);
            if (text.Length == 0) { request.Fail("$clans_err_chat_empty"); return; }
            if (Throttled(request.Sender.Account)) { request.Fail("$clans_err_chat_too_fast"); return; }
            List<ClanSender> to = Recipients(request.Sender.Account, channel, out string error);
            if (error != null) { request.Fail(error); return; }

            ChatMessageInfo message = Message(request.Sender, channel, text);
            message.Links.AddRange(links);
            Deliver(to, message);
            request.Ok();
        }

        // Players may only attach items; invites are issued by the server. Null (and the request failed) when
        // something is off.
        private static List<ChatLink> ReadItemLinks(ServerRequest request, int count) {
            List<ChatLink> links = new List<ChatLink>();
            for (int i = 0; i < count; i++) {
                ChatLink link = ChatLink.Read(request.Body);
                if (link.Kind != ChatLinkKind.Item || link.Payload == null || link.Payload.Length == 0 || link.Payload.Length > ChatLimits.MaxItemPayload) {
                    request.Fail("$clans_err_bad_request");
                    return null;
                }
                link.Id = 0;
                link.Label = Clip(NoMarkup(link.Label), ChatLimits.MaxLinkLabel);
                links.Add(link);
            }
            return links;
        }

        // Links for a vanilla say, shout or whisper the sender is about to send.
        private static void OnAttach(ServerRequest request) {
            Talker.Type type = (Talker.Type)request.Body.ReadByte();
            string platform = request.Body.ReadString();
            int count = request.Body.ReadInt();
            if (count <= 0 || count > ChatLimits.MaxLinks || string.IsNullOrEmpty(platform) || platform.Length > ChatLimits.MaxPlatformId) {
                request.Fail("$clans_err_bad_request");
                return;
            }
            List<ChatLink> links = ReadItemLinks(request, count);
            if (links == null) { return; }
            if (Throttled(request.Sender.Account)) { request.Fail("$clans_err_chat_too_fast"); return; }
            Attach(Hearers(request.Sender, type), SpeakerPlatform(request.Sender, platform), links);
            request.Ok();
        }

        // The platform id the sender's vanilla line will carry, taken from their connection rather than from
        // what their client says, so links cannot be pinned to someone else's lines. Only a backend without
        // platform ids leaves the client's word.
        private static string SpeakerPlatform(ClanSender sender, string claimed) {
            PlatformUserID id = PlatformUserID.None;
            try {
                id = sender.Peer != null ? AccountId.PlatformIdOf(sender.Peer) : PlatformManager.DistributionPlatform.LocalUser.PlatformUserID;
            } catch (Exception) { /* no platform ids here */ }
            return id.IsValid ? id.ToString() : claimed;
        }

        private static void Attach(List<ClanSender> to, string platform, List<ChatLink> links) {
            foreach (ClanSender session in to) { ClanNet.Send(session, Op.ChatAttachments, 0, pkg => ChatAttachmentsDto.Write(pkg, platform, links)); }
        }

        // Who will hear a vanilla line: everyone for a shout (vanilla sends it to every player), players near
        // the sender otherwise. Always the sender too, who sees their own line.
        private static List<ClanSender> Hearers(ClanSender sender, Talker.Type type) {
            if (type == Talker.Type.Shout) { return Sessions.All.ToList(); }
            float range = type == Talker.Type.Whisper ? WhisperRange : SayRange;
            Vector3 origin = sender.Position;
            List<ClanSender> hearers = Sessions.All.Where(s => s.Account == sender.Account || Utils.DistanceXZ(s.Position, origin) <= range).ToList();
            return hearers;
        }

        private static ChatMessageInfo Message(ClanSender sender, ChatChannel channel, string text) {
            return new ChatMessageInfo {
                Channel = channel,
                SenderAccount = sender.Account,
                SenderName = sender.CharacterName,
                SenderClanId = ClanRules.ClanOf(sender.Account)?.Id ?? 0,
                Text = text,
            };
        }

        private static void Deliver(List<ClanSender> to, ChatMessageInfo message) {
            foreach (ClanSender session in to) { ClanNet.Send(session, Op.ChatMessage, 0, message.Write); }
        }

        // Who hears a channel, or why the sender cannot use it.
        private static List<ClanSender> Recipients(string account, ChatChannel channel, out string error) {
            error = null;
            switch (channel) {
                case ChatChannel.Global:
                    if (ClansConfig.GlobalChat.Value == false) { error = "$clans_err_global_chat_off"; return null; }
                    return Sessions.All.ToList();
                case ChatChannel.Clan: {
                    ClanRecord clan = ClanRules.ClanOf(account);
                    if (clan == null) { error = "$clans_err_not_in_clan"; return null; }
                    return Online(clan.Members.Keys);
                }
                case ChatChannel.Party: {
                    PartyService.Party party = PartyService.PartyOf(account);
                    if (party == null) { error = "$clans_err_not_in_party"; return null; }
                    return Online(party.Members);
                }
                case ChatChannel.Raid: {
                    PartyService.Party party = PartyService.PartyOf(account);
                    PartyService.Raid raid = party != null ? PartyService.RaidOf(party) : null;
                    if (raid == null) { error = "$clans_err_not_in_raid"; return null; }
                    return Online(PartyService.RaidMembers(raid));
                }
                default:
                    error = "$clans_err_bad_request";
                    return null;
            }
        }

        private static List<ClanSender> Online(IEnumerable<string> accounts) {
            return accounts.Select(Sessions.Get).Where(s => s != null).ToList();
        }

        // Vanilla's rule for chat text: no markup. Placeholders for links survive; the receiver checks them.
        private static string Clean(string text) {
            text = (text ?? "").Replace('<', ' ').Replace('>', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim();
            return Clip(text, ClansConfig.ChatMaxLength.Value);
        }

        internal static string Clip(string text, int max) => text == null ? "" : text.Length <= max ? text : text.Substring(0, max);

        // Labels are drawn inside rich text on every client.
        internal static string NoMarkup(string text) => (text ?? "").Replace('<', ' ').Replace('>', ' ');

        private static bool Throttled(string account) {
            int max = ClansConfig.ChatRateLimit.Value;
            if (max <= 0) { return false; }
            if (recent.TryGetValue(account, out Queue<float> times) == false) {
                times = new Queue<float>();
                recent[account] = times;
            }
            float now = Time.unscaledTime;
            while (times.Count > 0 && now - times.Peek() > RateWindowSeconds) { times.Dequeue(); }
            if (times.Count >= max) { return true; }
            times.Enqueue(now);
            return false;
        }

        // A shared bounty's line in clan chat, from its owner, with a link that shows its area on the map.
        // Clients cannot post these: ReadItemLinks takes item links only.
        internal static void PostBounty(ClanSender owner, SharedBountyRecord bounty) {
            List<ClanSender> to = Recipients(owner.Account, ChatChannel.Clan, out string error);
            if (error != null || to == null || to.Count == 0) { return; }
            ChatLink link = new ChatLink {
                Kind = ChatLinkKind.Bounty,
                Label = Clip(NoMarkup(bounty.Name), ChatLimits.MaxLinkLabel),
                Payload = BountyLink.Write(new Vector3(bounty.X, bounty.Y, bounty.Z), bounty.Biome),
            };
            ChatMessageInfo message = Message(owner, ChatChannel.Clan, "[[0]]");
            message.Links.Add(link);
            Deliver(to, message);
        }

        // ---------------------------------------------------------------- invites

        // A party leader posts an invite to their party (in Global or Clan chat, or by say or shout); a member
        // allowed to invite posts one to their clan (in Global chat, or by say or shout). Anyone who clicks it
        // joins while it lasts. By say or shout the invite goes ahead as a link and the poster then says it.
        private static void OnPostInvite(ServerRequest request) {
            ChatLinkKind kind = (ChatLinkKind)request.Body.ReadByte();
            ChatChannel channel = (ChatChannel)request.Body.ReadByte();
            string platform = request.Body.ReadString();
            string account = request.Sender.Account;
            bool spoken = channel == ChatChannel.Say || channel == ChatChannel.Shout;
            if (spoken && (string.IsNullOrEmpty(platform) || platform.Length > ChatLimits.MaxPlatformId)) { request.Fail("$clans_err_bad_request"); return; }
            Prune();
            if (Throttled(account)) { request.Fail("$clans_err_chat_too_fast"); return; }

            ChatInvite invite = new ChatInvite { Id = nextInviteId++, Kind = kind, Poster = account, ExpiresUtc = ClanStore.Now + InviteSeconds };
            string label;
            if (kind == ChatLinkKind.PartyInvite) {
                if (channel != ChatChannel.Global && channel != ChatChannel.Clan && spoken == false) { request.Fail("$clans_err_bad_request"); return; }
                if (PartyService.Enabled == false) { request.Fail("$clans_err_parties_off"); return; }
                PartyService.Party party = PartyService.EnsureParty(account);
                if (party.Leader != account) { request.Fail("$clans_err_party_leader_only"); return; }
                if (party.Members.Count >= ClansConfig.MaxPartySize.Value) { request.Fail("$clans_err_party_full", ClansConfig.MaxPartySize.Value.ToString()); return; }
                invite.TargetId = party.Id;
                label = request.Sender.CharacterName;
            } else if (kind == ChatLinkKind.ClanInvite) {
                if (channel != ChatChannel.Global && spoken == false) { request.Fail("$clans_err_bad_request"); return; }
                ClanRecord clan = ClanRules.ClanOf(account);
                if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
                if (ClanRules.Can(clan, account, ClanPermission.Invite) == false) { request.Fail("$clans_err_no_permission"); return; }
                if (clan.Members.Count >= ClanRules.MaxMembers(clan)) { request.Fail("$clans_err_clan_full", ClanRules.MaxMembers(clan).ToString()); return; }
                invite.TargetId = clan.Id;
                label = clan.Name;
            } else {
                request.Fail("$clans_err_bad_request");
                return;
            }

            ChatLink link = new ChatLink { Kind = kind, Label = Clip(NoMarkup(label), ChatLimits.MaxLinkLabel), Id = invite.Id };
            if (spoken) {
                invites[invite.Id] = invite;
                Attach(Hearers(request.Sender, channel == ChatChannel.Shout ? Talker.Type.Shout : Talker.Type.Normal), SpeakerPlatform(request.Sender, platform), new List<ChatLink> { link });
                request.Ok();
                return;
            }
            List<ClanSender> to = Recipients(account, channel, out string error);
            if (error != null) { request.Fail(error); return; }
            invites[invite.Id] = invite;
            ChatMessageInfo message = Message(request.Sender, channel, "[[0]]");
            message.Links.Add(link);
            Deliver(to, message);
            request.Ok();
        }

        private static void OnJoinInvite(ServerRequest request) {
            long id = request.Body.ReadLong();
            Prune();
            if (invites.TryGetValue(id, out ChatInvite invite) == false) { request.Fail("$clans_err_invite_gone"); return; }
            if (invite.Poster == request.Sender.Account) { request.Fail("$clans_err_own_invite"); return; }
            if (invite.Kind == ChatLinkKind.PartyInvite) {
                PartyService.JoinFromChat(request, invite.TargetId, invite.Poster);
            } else {
                ClanService.JoinFromChat(request, invite.TargetId, invite.Poster);
            }
        }

        private static void Prune() {
            long now = ClanStore.Now;
            foreach (long id in invites.Where(e => e.Value.ExpiresUtc <= now).Select(e => e.Key).ToList()) { invites.Remove(id); }
        }
    }
}
