using Clans.Compat;
using Clans.Net;
using HarmonyLib;
using Splatform;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Clans.Client {
    // One line of chat, ready to show.
    internal sealed class ChatEntry {
        internal ChatChannel Channel;
        // Empty for system lines.
        internal string Sender = "";
        // Consecutive lines with the same key (and channel) are grouped under one header.
        internal string SenderKey = "";
        internal string Body = "";
        internal DateTime Time;
        internal readonly List<string> LinkIds = new List<string>();
    }

    // What a chat link points at.
    internal sealed class ChatLinkTarget {
        internal ChatLinkKind Kind;
        internal long InviteId;
        internal ItemDrop.ItemData Item;
        internal string Label = "";
        // Bounties: the centre of the bounty's circle.
        internal Vector3 Position;
    }

    // Everything said in chat this session, for the Clans chat panel: vanilla's say, shout and whisper and
    // system lines (captured as vanilla adds them), and Clans' Global, Clan, Party and Raid messages.
    //
    // Without the panel (it is turned off, or Chatter is installed) Clans' messages are written into the
    // usual chat window as coloured lines instead, with invites joinable through /joininvite.
    internal static class ChatLog {
        internal const int MaxEntries = 300;

        internal static readonly List<ChatEntry> Entries = new List<ChatEntry>();
        internal static event Action<ChatEntry> Added;
        // Set while the Clans panel shows chat.
        internal static bool PanelActive;

        private static readonly Dictionary<string, ChatLinkTarget> links = new Dictionary<string, ChatLinkTarget>();
        private static long nextLink;

        // Links sent ahead of a vanilla say, shout or whisper, by sender platform id, until that line arrives.
        private sealed class Attachment {
            internal ChatLink Link;
            internal float Time;
        }

        // A line follows its links within moments. Links whose line never came here (the sender was just out
        // of earshot) must not linger and land on a later line.
        private const float AttachmentSeconds = 10f;
        private static readonly Dictionary<string, List<Attachment>> attachments = new Dictionary<string, List<Attachment>>();
        // Set while vanilla formats a player's line, so the formatted string is not added a second time.
        private static bool capturing;

        internal static void Clear() {
            Entries.Clear();
            links.Clear();
            attachments.Clear();
            capturing = false;
        }

        internal static void AddAttachments(string senderPlatform, List<ChatLink> sent) {
            if (string.IsNullOrEmpty(senderPlatform) || sent.Count == 0) { return; }
            if (attachments.TryGetValue(senderPlatform, out List<Attachment> list) == false) {
                list = new List<Attachment>();
                attachments[senderPlatform] = list;
            }
            float now = UnityEngine.Time.unscaledTime;
            list.RemoveAll(a => now - a.Time > AttachmentSeconds);
            foreach (ChatLink link in sent) { list.Add(new Attachment { Link = link, Time = now }); }
        }

        internal static ChatLinkTarget Link(string id) => id != null && links.TryGetValue(id, out ChatLinkTarget target) ? target : null;

        // ---------------------------------------------------------------- adding

        internal static void AddPlayer(ChatChannel channel, string sender, string senderKey, string text) {
            ChatEntry entry = new ChatEntry { Channel = channel, Sender = sender ?? "", SenderKey = senderKey ?? "" };
            entry.Body = ApplyMyLittleUiLinks(ApplyAttachments(senderKey, text ?? "", entry), entry);
            Add(entry);
        }

        // My Little UI's item links: a marker where the item goes (see MyLittleUiLinks), matched in any case
        // since whispers are lower case. Shown like Clans' own links; the stack size follows, as My Little UI
        // shows it.
        private static string ApplyMyLittleUiLinks(string text, ChatEntry entry) {
            if (PanelActive == false) { return text; }
            int at = text.IndexOf(MyLittleUiLinks.MarkerPrefix, StringComparison.OrdinalIgnoreCase);
            while (at >= 0) {
                int idStart = at + MyLittleUiLinks.MarkerPrefix.Length;
                int end = text.IndexOf(MyLittleUiLinks.MarkerSuffix, idStart, StringComparison.Ordinal);
                if (end < 0) { break; }
                ItemDrop.ItemData item = int.TryParse(text.Substring(idStart, end - idStart), NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                    ? MyLittleUiLinks.Item(id) : null;
                string link;
                if (item == null || item.m_shared == null) {
                    link = ChatItems.DecoratedName(null, UI.UiBind.Loc("$clans_chat_linked_item"));
                } else {
                    ChatLinkTarget target = new ChatLinkTarget { Kind = ChatLinkKind.Item, Item = item, Label = ChatItems.PlainName(item) };
                    link = Linked(target, ChatItems.DecoratedName(item, target.Label), entry) + (item.m_stack > 1 ? " x" + item.m_stack : "");
                }
                text = text.Substring(0, at) + link + text.Substring(end + MyLittleUiLinks.MarkerSuffix.Length);
                at = text.IndexOf(MyLittleUiLinks.MarkerPrefix, at + link.Length, StringComparison.OrdinalIgnoreCase);
            }
            return text;
        }

        // Turns the names in a vanilla line into the links its sender sent ahead: an item's "[name]" (any case,
        // since shouts are capitals and whispers lower case), and for an invite the first "[...]", which the
        // poster said in their own language.
        private static string ApplyAttachments(string senderKey, string text, ChatEntry entry) {
            if (PanelActive == false || string.IsNullOrEmpty(senderKey) || attachments.TryGetValue(senderKey, out List<Attachment> list) == false) { return text; }
            float now = UnityEngine.Time.unscaledTime;
            list.RemoveAll(a => now - a.Time > AttachmentSeconds);
            List<(int Start, int Length, ChatLink Link)> hits = new List<(int, int, ChatLink)>();
            foreach (Attachment attachment in list.ToArray()) {
                int start = -1;
                int length = 0;
                if (attachment.Link.Kind == ChatLinkKind.Item) {
                    string token = "[" + attachment.Link.Label + "]";
                    start = FreeIndex(text, token, hits);
                    length = token.Length;
                } else {
                    int open = text.IndexOf('[');
                    int close = open >= 0 ? text.IndexOf(']', open) : -1;
                    if (close > open && hits.TrueForAll(h => close < h.Start || open >= h.Start + h.Length)) {
                        start = open;
                        length = close - open + 1;
                    }
                }
                if (start < 0) { continue; }
                hits.Add((start, length, attachment.Link));
                list.Remove(attachment);
            }
            hits.Sort((a, b) => b.Start.CompareTo(a.Start));
            foreach ((int start, int length, ChatLink link) in hits) {
                text = text.Substring(0, start) + Markup(link, entry) + text.Substring(start + length);
            }
            return text;
        }

        // The first occurrence of a token (ignoring case) that does not overlap a link already placed.
        private static int FreeIndex(string text, string token, List<(int Start, int Length, ChatLink Link)> hits) {
            int from = 0;
            while (from <= text.Length - token.Length) {
                int at = text.IndexOf(token, from, StringComparison.OrdinalIgnoreCase);
                if (at < 0) { return -1; }
                if (hits.TrueForAll(h => at + token.Length <= h.Start || at >= h.Start + h.Length)) { return at; }
                from = at + 1;
            }
            return -1;
        }

        internal static void AddSystem(string richText) {
            if (PanelActive == false) {
                Chat.instance?.AddString(richText);
                return;
            }
            Add(new ChatEntry { Channel = ChatChannel.System, Body = richText ?? "" });
        }

        internal static void AddClans(ChatMessageInfo message) {
            bool mine = message.SenderAccount == ClanClient.MyAccount;
            // The platform's text filter, as vanilla applies it to other players' chat.
            string text = mine ? message.Text : CensorShittyWords.FilterUGC(message.Text, UGCType.Chat, 0L);
            string sender = ChatItems.Plain(CensorShittyWords.FilterUGC(message.SenderName, UGCType.CharacterName, 0L));
            ChatEntry entry = new ChatEntry { Channel = message.Channel, Sender = sender, SenderKey = message.SenderAccount };
            entry.Body = InsertLinks(ChatItems.Plain(text), message.Links, entry);
            Add(entry);
            if (PanelActive == false) { Chat.instance?.AddString(Line(entry)); }
            // Shows the chat for a while, as a vanilla message does.
            if (Chat.instance != null) { Chat.instance.m_hideTimer = 0f; }
        }

        // The clan's status, as a Clan line from the clan itself. Added on this machine only, so nobody else
        // sees it; a clan without a status shows nothing.
        internal static void AddClanStatus(string clanName, string status) {
            if (string.IsNullOrWhiteSpace(status)) { return; }
            string sender = UI.UiBind.Loc("$clans_chat_login_status", ChatItems.Plain(clanName));
            ChatEntry entry = new ChatEntry { Channel = ChatChannel.Clan, Sender = sender, SenderKey = "status:" + clanName };
            entry.Body = ChatItems.Plain(CensorShittyWords.FilterUGC(status, UGCType.Chat, 0L));
            Add(entry);
            if (PanelActive == false) { Chat.instance?.AddString(Line(entry)); }
            if (Chat.instance != null) { Chat.instance.m_hideTimer = 0f; }
        }

        private static void Add(ChatEntry entry) {
            entry.Time = DateTime.Now;
            if (PanelActive == false) { return; }
            Entries.Add(entry);
            while (Entries.Count > MaxEntries) {
                foreach (string id in Entries[0].LinkIds) { links.Remove(id); }
                Entries.RemoveAt(0);
            }
            Added?.Invoke(entry);
        }

        // ---------------------------------------------------------------- formatting

        // "[[n]]" in the text becomes link n; anything else is left as typed.
        private static string InsertLinks(string text, List<ChatLink> messageLinks, ChatEntry entry) {
            for (int i = 0; i < messageLinks.Count; i++) {
                string placeholder = "[[" + i + "]]";
                int at = text.IndexOf(placeholder, StringComparison.Ordinal);
                if (at < 0) { continue; }
                text = text.Substring(0, at) + Markup(messageLinks[i], entry) + text.Substring(at + placeholder.Length);
            }
            return text;
        }

        private static string Markup(ChatLink link, ChatEntry entry) {
            ChatLinkTarget target = new ChatLinkTarget { Kind = link.Kind, InviteId = link.Id, Label = ChatItems.Plain(link.Label) };
            string label;
            switch (link.Kind) {
                case ChatLinkKind.Item:
                    target.Item = ChatItems.Deserialize(link.Payload);
                    label = ChatItems.DecoratedName(target.Item, target.Label);
                    break;
                case ChatLinkKind.PartyInvite:
                    label = "<color=" + ColorHex(ChatChannel.Party) + "><u>[" + UI.UiBind.Loc("$clans_chat_join_party", target.Label) + "]</u></color>";
                    break;
                case ChatLinkKind.Bounty:
                    return BountyMarkup(link, target, entry);
                default:
                    label = "<color=" + ColorHex(ChatChannel.Clan) + "><u>[" + UI.UiBind.Loc("$clans_chat_join_clan", target.Label) + "]</u></color>";
                    break;
            }
            if (PanelActive == false) {
                // The usual chat window has no clickable links: say how to accept instead.
                return link.Kind == ChatLinkKind.Item ? label : label + " " + UI.UiBind.Loc("$clans_chat_join_hint", link.Id.ToString());
            }
            return Linked(target, label, entry);
        }

        // "took a bounty: [Troll, Black Forest - show on map]". Without the panel there is nothing to click, so
        // the line names the place only.
        private static string BountyMarkup(ChatLink link, ChatLinkTarget target, ChatEntry entry) {
            BountyLink.TryRead(link.Payload, out target.Position, out string biome);
            string name = Localization.instance.Localize(target.Label);
            string where = string.IsNullOrEmpty(biome) ? name : name + ", " + UI.UiBind.Loc("$biome_" + biome.ToLowerInvariant());
            if (PanelActive == false) { return UI.UiBind.Loc("$clans_chat_bounty_shared", "[" + where + "]"); }
            string label = "<color=" + ColorHex(ChatChannel.Clan) + "><u>[" + UI.UiBind.Loc("$clans_chat_show_on_map", where) + "]</u></color>";
            return UI.UiBind.Loc("$clans_chat_bounty_shared", Linked(target, label, entry));
        }

        // The label as a link the panel knows, kept until its line leaves the log.
        private static string Linked(ChatLinkTarget target, string label, ChatEntry entry) {
            string id = "L" + (++nextLink);
            links[id] = target;
            entry.LinkIds.Add(id);
            return "<link=\"" + id + "\">" + label + "</link>";
        }

        // A whole line, for the usual chat window: "[Clan] Name: text".
        internal static string Line(ChatEntry entry) {
            string tag = "<color=" + ColorHex(entry.Channel) + ">[" + Localization.instance.Localize(Tag(entry.Channel)) + "]</color> ";
            return tag + "<color=orange>" + entry.Sender + "</color>: " + entry.Body;
        }

        internal static string Tag(ChatChannel channel) => "$clans_chat_tag_" + channel.ToString().ToLowerInvariant();

        // WoW-style channel colours, so each channel reads at a glance.
        internal static string ColorHex(ChatChannel channel) {
            switch (channel) {
                case ChatChannel.Global: return "#F2C48C";
                case ChatChannel.Clan: return "#40FF40";
                case ChatChannel.Party: return "#AAAAFF";
                case ChatChannel.Raid: return "#FF7F00";
                case ChatChannel.Shout: return "#FFEB04";
                case ChatChannel.Whisper: return "#C8C8C8";
                case ChatChannel.System: return "#A0A0A0";
                default: return "#FFFFFF";
            }
        }

        // ---------------------------------------------------------------- vanilla's lines, while the panel is up

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.AddString), typeof(PlatformUserID), typeof(string), typeof(Talker.Type), typeof(bool))]
        private static class PlayerLinePatch {
            private static void Prefix(Terminal __instance, PlatformUserID user, string text, Talker.Type type) {
                if (PanelActive == false || !(__instance is Chat)) { return; }
                // Vanilla drops a line from a player it cannot find; so does the panel.
                if (ZNet.TryGetPlayerByPlatformUserID(user, out ZNet.PlayerInfo info) == false) { return; }
                capturing = true;
                string name = ChatItems.Plain(CensorShittyWords.FilterUGC(info.m_name, UGCType.CharacterName, user, 0L));
                AddPlayer(Channel(type), name, user.ToString(), Cased(text, type));
            }

            private static void Postfix() => capturing = false;
        }

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.AddString), typeof(string), typeof(string), typeof(Talker.Type), typeof(bool))]
        private static class TitledLinePatch {
            private static void Prefix(Terminal __instance, string title, string text, Talker.Type type) {
                if (PanelActive == false || !(__instance is Chat)) { return; }
                capturing = true;
                AddPlayer(Channel(type), title, "title:" + title, Cased(text, type));
            }

            private static void Postfix() => capturing = false;
        }

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.AddString), typeof(string))]
        private static class SystemLinePatch {
            private static void Postfix(Terminal __instance, string text) {
                if (PanelActive == false || capturing || !(__instance is Chat)) { return; }
                Add(new ChatEntry { Channel = ChatChannel.System, Body = text ?? "" });
            }
        }

        private static ChatChannel Channel(Talker.Type type) {
            switch (type) {
                case Talker.Type.Shout: return ChatChannel.Shout;
                case Talker.Type.Whisper: return ChatChannel.Whisper;
                default: return ChatChannel.Say;
            }
        }

        // Vanilla shouts in capitals and whispers in lower case.
        private static string Cased(string text, Talker.Type type) {
            if (text == null) { return ""; }
            if (type == Talker.Type.Shout) { return text.ToUpper(); }
            if (type == Talker.Type.Whisper) { return text.ToLowerInvariant(); }
            return text;
        }
    }
}
