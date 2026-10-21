using System.Collections.Generic;

namespace Clans.Net {
    // Where a chat line belongs. Say, Shout, Whisper and System are vanilla's; the rest travel over Clans'
    // own channel. The numbers are the wire format.
    internal enum ChatChannel : byte {
        Say = 0,
        Shout = 1,
        Whisper = 2,
        System = 3,
        Global = 10,
        Clan = 11,
        Party = 12,
        Raid = 13,
    }

    internal enum ChatLinkKind : byte {
        Item = 0,
        PartyInvite = 1,
        ClanInvite = 2,
        // A clanmate's shared Epic Loot bounty: the payload is its position and biome. Made by the server only.
        Bounty = 3,
    }

    // Something clickable inside a chat message. The message text holds "[[n]]" where link n goes; the
    // receiver draws it, so a sender can never inject markup.
    internal sealed class ChatLink {
        internal ChatLinkKind Kind;
        internal string Label = "";
        // Invites: the invite's id on the server.
        internal long Id;
        // Items: the item, as written by ChatItems.Serialize. Bounties: BountyLink.Write.
        internal byte[] Payload = new byte[0];

        internal void Write(ZPackage pkg) {
            pkg.Write((byte)Kind);
            pkg.Write(Label ?? "");
            pkg.Write(Id);
            pkg.Write(Payload ?? new byte[0]);
        }

        internal static ChatLink Read(ZPackage pkg) {
            return new ChatLink {
                Kind = (ChatLinkKind)pkg.ReadByte(),
                Label = pkg.ReadString(),
                Id = pkg.ReadLong(),
                Payload = pkg.ReadByteArray(),
            };
        }
    }

    internal sealed class ChatMessageInfo {
        internal ChatChannel Channel;
        internal string SenderAccount = "";
        internal string SenderName = "";
        internal long SenderClanId;
        internal string Text = "";
        internal readonly List<ChatLink> Links = new List<ChatLink>();

        internal void Write(ZPackage pkg) {
            pkg.Write((byte)Channel);
            pkg.Write(SenderAccount ?? "");
            pkg.Write(SenderName ?? "");
            pkg.Write(SenderClanId);
            pkg.Write(Text ?? "");
            pkg.Write(Links.Count);
            foreach (ChatLink link in Links) { link.Write(pkg); }
        }

        internal static ChatMessageInfo Read(ZPackage pkg) {
            ChatMessageInfo message = new ChatMessageInfo {
                Channel = (ChatChannel)pkg.ReadByte(),
                SenderAccount = pkg.ReadString(),
                SenderName = pkg.ReadString(),
                SenderClanId = pkg.ReadLong(),
                Text = pkg.ReadString(),
            };
            int count = pkg.ReadInt();
            for (int i = 0; i < count && i < ChatLimits.MaxLinks; i++) { message.Links.Add(ChatLink.Read(pkg)); }
            return message;
        }
    }

    // A bounty link's payload: where the bounty's circle is, and its Heightmap.Biome name.
    internal static class BountyLink {
        internal static byte[] Write(UnityEngine.Vector3 position, string biome) {
            ZPackage pkg = new ZPackage();
            pkg.Write(position);
            pkg.Write(biome ?? "");
            return pkg.GetArray();
        }

        internal static bool TryRead(byte[] payload, out UnityEngine.Vector3 position, out string biome) {
            position = UnityEngine.Vector3.zero;
            biome = "";
            if (payload == null || payload.Length == 0) { return false; }
            try {
                ZPackage pkg = new ZPackage(payload);
                position = pkg.ReadVector3();
                biome = pkg.ReadString();
                return true;
            } catch (System.Exception) {
                return false;
            }
        }
    }

    internal static class ChatLimits {
        internal const int MaxLinks = 4;
        internal const int MaxLinkLabel = 80;
        internal const int MaxItemPayload = 6000;
        // A platform user id as vanilla writes it ("Steam_7656...").
        internal const int MaxPlatformId = 96;
    }

    // Links for a vanilla say, shout or whisper, sent ahead of it to everyone who will hear it. Matched to the
    // sender's next line by the sender's platform user id, which vanilla's chat carries too.
    internal static class ChatAttachmentsDto {
        internal static void Write(ZPackage pkg, string senderPlatform, List<ChatLink> links) {
            pkg.Write(senderPlatform ?? "");
            pkg.Write(links.Count);
            foreach (ChatLink link in links) { link.Write(pkg); }
        }

        internal static List<ChatLink> Read(ZPackage pkg, out string senderPlatform) {
            senderPlatform = pkg.ReadString();
            int count = pkg.ReadInt();
            List<ChatLink> links = new List<ChatLink>();
            for (int i = 0; i < count && i < ChatLimits.MaxLinks; i++) { links.Add(ChatLink.Read(pkg)); }
            return links;
        }
    }
}
