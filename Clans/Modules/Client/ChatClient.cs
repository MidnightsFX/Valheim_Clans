using Clans.Net;
using Clans.Settings;
using HarmonyLib;
using Splatform;
using System;
using System.Collections.Generic;

namespace Clans.Client {
    // Clans' chat channels on the client: which channel plain typing goes to, the /g /c /p /r commands,
    // sending, and posting and accepting chat invites.
    //
    // A channel command with text sends one message (/c hello); without text it makes that channel the one
    // plain typing goes to, until another is chosen (/c, then just type). /say alone switches back to say;
    // /say, /s and /w with text are vanilla's.
    internal static class ChatClient {
        internal static ChatChannel Current { get; private set; } = ChatChannel.Say;
        internal static event Action ChannelChanged;

        private static bool commandsRegistered;
        // Set while a held-back vanilla line is finally sent.
        private static bool sendingHeld;

        internal static void Init() {
            ClanNet.RegisterClient(Op.ChatMessage, (reqId, body) => ChatLog.AddClans(ChatMessageInfo.Read(body)));
            ClanNet.RegisterClient(Op.ChatAttachments, (reqId, body) => {
                List<ChatLink> links = ChatAttachmentsDto.Read(body, out string platform);
                ChatLog.AddAttachments(platform, links);
            });
            RegisterCommands();
        }

        internal static void Reset() {
            Current = ChatChannel.Say;
            sendingHeld = false;
            ChatLog.Clear();
            ChatItems.Reset();
        }

        // How vanilla's chat names this player: the platform user id its lines carry.
        private static string LocalPlatformId() {
            try {
                return PlatformManager.DistributionPlatform.LocalUser.PlatformUserID.ToString();
            } catch (Exception) {
                return "";
            }
        }

        internal static void Tick() {
            ChatItems.Tick();
            // Leaving a clan, party or raid takes its channel away.
            if (IsClansChannel(Current) && Available(Current) == false) { SetChannel(ChatChannel.Say, quiet: true); }
        }

        internal static bool IsClansChannel(ChatChannel channel) => channel >= ChatChannel.Global && channel <= ChatChannel.Raid;

        internal static bool Available(ChatChannel channel) {
            switch (channel) {
                case ChatChannel.Global: return ClansConfig.GlobalChat.Value && ClanClient.Welcomed;
                case ChatChannel.Clan: return ClanClient.InClan;
                case ChatChannel.Party: return PartyClient.InParty;
                case ChatChannel.Raid: return PartyClient.InRaid;
                default: return true;
            }
        }

        internal static bool SetChannel(ChatChannel channel, bool quiet = false) {
            if (Available(channel) == false) {
                if (quiet == false) { Error(UI.UiBind.Loc(Unavailable(channel))); }
                return false;
            }
            if (Current == channel) { return true; }
            Current = channel;
            ChannelChanged?.Invoke();
            return true;
        }

        private static string Unavailable(ChatChannel channel) {
            switch (channel) {
                case ChatChannel.Global: return "$clans_err_global_chat_off";
                case ChatChannel.Clan: return "$clans_err_not_in_clan";
                case ChatChannel.Party: return "$clans_err_not_in_party";
                default: return "$clans_err_not_in_raid";
            }
        }

        // ---------------------------------------------------------------- sending

        internal static void Send(ChatChannel channel, string text) {
            if (string.IsNullOrWhiteSpace(text)) { return; }
            if (Available(channel) == false) {
                Error(UI.UiBind.Loc(Unavailable(channel)));
                return;
            }
            List<ChatLink> links = ChatItems.TakeLinks(ref text);
            ClanRequests.Send(Op.ChatSend, pkg => {
                pkg.Write((byte)channel);
                pkg.Write(text);
                pkg.Write(links.Count);
                foreach (ChatLink link in links) { link.Write(pkg); }
            }, Report);
        }

        // Party invites can go to Global, Clan, say or shout; clan invites to Global, say or shout. By say or
        // shout the server sends the invite ahead and this player then says it. Without a callback a refusal
        // is shown in chat.
        internal static void PostInvite(ChatLinkKind kind, ChatChannel channel, Action<NetResult> done = null) {
            ClanRequests.Send(Op.ChatPostInvite, pkg => {
                pkg.Write((byte)kind);
                pkg.Write((byte)channel);
                pkg.Write(LocalPlatformId());
            }, result => {
                if (result.Ok && (channel == ChatChannel.Say || channel == ChatChannel.Shout)) {
                    SendHeld(channel == ChatChannel.Shout ? Talker.Type.Shout : Talker.Type.Normal, "[" + InviteLabel(kind) + "]");
                }
                if (done != null) { done(result); } else { Report(result); }
            });
        }

        // The invite as the poster says it; listeners turn it into a link in their own language.
        private static string InviteLabel(ChatLinkKind kind) {
            if (kind == ChatLinkKind.PartyInvite) {
                return UI.UiBind.Loc("$clans_chat_join_party", Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "");
            }
            return UI.UiBind.Loc("$clans_chat_join_clan", ClanClient.MyClan?.Name ?? "");
        }

        private static void SendHeld(Talker.Type type, string text) {
            if (Chat.instance == null) { return; }
            sendingHeld = true;
            try {
                Chat.instance.SendText(type, text);
            } finally {
                sendingHeld = false;
            }
        }

        // Vanilla's say, shout and whisper carry text only. A line with linked items is held back while the
        // items go to the server, which passes them to everyone who will hear the line, and is sent once the
        // server answers: the items always arrive first, and the chat panel turns the names into links. If the
        // server cannot take them the line still goes out, with the names as plain text.
        [HarmonyPatch(typeof(Chat), nameof(Chat.SendText))]
        private static class SendTextPatch {
            private static bool Prefix(Talker.Type type, string text) {
                if (sendingHeld || type == Talker.Type.Ping || string.IsNullOrEmpty(text) || ChatItems.HasPending(text) == false) { return true; }
                List<ChatLink> links = ChatItems.CollectLinks(text);
                if (links.Count == 0) { return true; }
                ClanRequests.Send(Op.ChatAttach, pkg => {
                    pkg.Write((byte)type);
                    pkg.Write(LocalPlatformId());
                    pkg.Write(links.Count);
                    foreach (ChatLink link in links) { link.Write(pkg); }
                }, result => {
                    SendHeld(type, text);
                    Report(result);
                });
                return false;
            }
        }

        internal static void JoinInvite(long id) {
            ClanRequests.Send(Op.ChatJoinInvite, pkg => pkg.Write(id), result => {
                if (result.Ok) { ClanNotices.ShowInfo(UI.UiBind.Loc("$clans_chat_joined")); } else { Report(result); }
            });
        }

        private static void Report(NetResult result) {
            if (result.Ok == false) { Error(result.Message()); }
        }

        internal static void Error(string text) => ChatLog.AddSystem("<color=#FF7070>" + text + "</color>");

        // ---------------------------------------------------------------- typing

        // Plain typing goes to the chosen Clans channel instead of vanilla's say. Commands (/...) are left alone.
        [HarmonyPatch(typeof(Chat), "InputText")]
        private static class InputTextPatch {
            private static bool Prefix(Chat __instance) {
                string text = __instance.m_input != null ? __instance.m_input.text : null;
                if (string.IsNullOrEmpty(text)) { return true; }
                // Vanilla's say command does nothing without text; here it switches plain typing back to say.
                // (Vanilla's say, s and w commands themselves are left alone: Chatter patches them.)
                if (text.Trim() == "/say") {
                    SetChannel(ChatChannel.Say);
                    // Chatter keeps its own shout and whisper modes, which its say command ends: let it run.
                    return Compat.ModPresence.Is(Compat.ModPresence.Capability.Chatter);
                }
                if (text[0] == '/') { return true; }
                if (IsClansChannel(Current)) {
                    Send(Current, text);
                    return false;
                }
                return true;
            }
        }

        // ---------------------------------------------------------------- commands

        private static void RegisterCommands() {
            if (commandsRegistered) { return; }
            commandsRegistered = true;
            Channel("g", ChatChannel.Global, "Global chat: /g message, or /g to keep talking there.");
            Channel("c", ChatChannel.Clan, "Clan chat: /c message, or /c to keep talking there.");
            Channel("p", ChatChannel.Party, "Party chat: /p message, or /p to keep talking there.");
            Channel("r", ChatChannel.Raid, "Raid chat: /r message, or /r to keep talking there.");
            _ = new Terminal.ConsoleCommand("pinvite", "Posts an invite to your party: /pinvite [global|clan|say|shout]. Without one, Global chat (Clan chat while you talk there).", args => {
                ChatChannel fallback = Current == ChatChannel.Clan ? ChatChannel.Clan : ChatChannel.Global;
                if (InviteChannel(args.ArgsAll, fallback, out ChatChannel channel)) { PostInvite(ChatLinkKind.PartyInvite, channel); }
            });
            _ = new Terminal.ConsoleCommand("cinvite", "Posts an invite to your clan: /cinvite [global|say|shout]. Without one, Global chat.", args => {
                if (InviteChannel(args.ArgsAll, ChatChannel.Global, out ChatChannel channel)) { PostInvite(ChatLinkKind.ClanInvite, channel); }
            });
            _ = new Terminal.ConsoleCommand("joininvite", "Accepts a party or clan invite posted in chat: /joininvite number.", args => {
                if (long.TryParse((args.ArgsAll ?? "").Trim(), out long id)) { JoinInvite(id); }
            });
        }

        private static bool InviteChannel(string arg, ChatChannel fallback, out ChatChannel channel) {
            switch ((arg ?? "").Trim().ToLowerInvariant()) {
                case "": channel = fallback; return true;
                case "global": case "g": channel = ChatChannel.Global; return true;
                case "clan": case "c": channel = ChatChannel.Clan; return true;
                case "say": channel = ChatChannel.Say; return true;
                case "shout": case "s": channel = ChatChannel.Shout; return true;
                default:
                    channel = fallback;
                    Error(UI.UiBind.Loc("$clans_chat_invite_where"));
                    return false;
            }
        }

        private static void Channel(string command, ChatChannel channel, string help) {
            _ = new Terminal.ConsoleCommand(command, help, args => {
                string text = (args.ArgsAll ?? "").Trim();
                if (text.Length == 0) { SetChannel(channel); } else { Send(channel, text); }
            });
        }
    }
}
