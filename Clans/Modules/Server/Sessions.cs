using Clans.Net;
using System.Collections.Generic;

namespace Clans.Server {
    // Who is online right now, by account and by peer. Filled when a client says Hello (and refreshed by
    // every request), emptied on disconnect. Not saved.
    internal static class Sessions {
        private static readonly Dictionary<string, ClanSender> byAccount = new Dictionary<string, ClanSender>();
        private static readonly Dictionary<long, string> accountByPeer = new Dictionary<long, string>();

        internal static IEnumerable<ClanSender> All => byAccount.Values;
        internal static int Count => byAccount.Count;

        // Returns true when this is a new session (the account was not online before).
        internal static bool Touch(ClanSender sender) {
            if (sender == null || string.IsNullOrEmpty(sender.Account)) { return false; }
            bool isNew = byAccount.ContainsKey(sender.Account) == false;
            // A second login on the same account (another machine) replaces the first session.
            if (byAccount.TryGetValue(sender.Account, out ClanSender previous) && previous.PeerUid != sender.PeerUid) {
                accountByPeer.Remove(previous.PeerUid);
            }
            byAccount[sender.Account] = sender;
            accountByPeer[sender.PeerUid] = sender.Account;
            return isNew;
        }

        internal static string RemovePeer(long peerUid) {
            if (accountByPeer.TryGetValue(peerUid, out string account) == false) { return null; }
            accountByPeer.Remove(peerUid);
            if (byAccount.TryGetValue(account, out ClanSender sender) && sender.PeerUid == peerUid) {
                byAccount.Remove(account);
                return account;
            }
            return null;
        }

        internal static bool IsOnline(string account) => account != null && byAccount.ContainsKey(account);

        internal static ClanSender Get(string account) {
            if (account == null) { return null; }
            byAccount.TryGetValue(account, out ClanSender sender);
            return sender;
        }

        internal static string AccountOfPeer(long peerUid) {
            accountByPeer.TryGetValue(peerUid, out string account);
            return account;
        }

        internal static void Clear() {
            byAccount.Clear();
            accountByPeer.Clear();
        }
    }
}
