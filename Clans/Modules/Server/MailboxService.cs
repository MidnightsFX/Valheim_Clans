using Clans.Net;
using Clans.Server.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Clans.Server {
    // Coins and items the server owes an account: activity rewards, treasury withdrawals, a kicked member's
    // personal storage. Kept in the save file until the player's client confirms it has them, and re-sent
    // every time they come online, so a disconnect at the wrong moment never loses anything.
    //
    // The client records which deliveries it has applied in the character's own save data, so a re-send of
    // something it already has is acknowledged without being applied twice.
    internal static class MailboxService {
        internal static void Register() {
            ClanNet.RegisterServer(Op.AckDelivery, OnAck);
        }

        // Random rather than counted: a character remembers the ids it has applied, and a counter starting
        // over (the clans data wiped, or an older backup restored) would hand out ids it already holds, whose
        // deliveries would then be acknowledged without being applied.
        private static long NewId(ClansWorldState state) {
            while (true) {
                long id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0) & long.MaxValue;
                if (id != 0 && state.Mailbox.Values.Any(box => box.Any(d => d.Id == id)) == false) { return id; }
            }
        }

        internal static void Add(string account, Delivery delivery) {
            ClansWorldState state = ClanStore.State;
            if (state == null || string.IsNullOrEmpty(account) || delivery == null) { return; }
            delivery.Id = NewId(state);
            delivery.CreatedUtc = ClanStore.Now;
            if (state.Mailbox.TryGetValue(account, out List<Delivery> box) == false) {
                box = new List<Delivery>();
                state.Mailbox[account] = box;
            }
            box.Add(delivery);
            ClanStore.MarkDirtySoon();
            ClanSender sender = Sessions.Get(account);
            if (sender != null) { Send(sender, delivery); }
        }

        internal static void SendPending(ClanSender sender) {
            ClansWorldState state = ClanStore.State;
            if (state == null || state.Mailbox.TryGetValue(sender.Account, out List<Delivery> box) == false) { return; }
            foreach (Delivery delivery in box) { Send(sender, delivery); }
        }

        internal static int CountFor(string account) {
            ClansWorldState state = ClanStore.State;
            return state != null && state.Mailbox.TryGetValue(account, out List<Delivery> box) ? box.Count : 0;
        }

        private static void Send(ClanSender sender, Delivery delivery) {
            ClanNet.Send(sender, Op.RewardGrant, 0, pkg => DeliveryCodec.Write(pkg, delivery));
        }

        private static void OnAck(ServerRequest request) {
            long id = request.Body.ReadLong();
            ClansWorldState state = ClanStore.State;
            if (state != null && state.Mailbox.TryGetValue(request.Sender.Account, out List<Delivery> box)) {
                if (box.RemoveAll(d => d.Id == id) > 0) {
                    if (box.Count == 0) { state.Mailbox.Remove(request.Sender.Account); }
                    ClanStore.MarkDirtySoon();
                }
            }
        }
    }

    internal static class DeliveryCodec {
        internal static void Write(ZPackage pkg, Delivery delivery) {
            pkg.Write(delivery.Id);
            pkg.Write(delivery.Reason ?? "");
            pkg.Write(delivery.Coins);
            pkg.Write(delivery.Items.Count);
            foreach (DeliveryItem item in delivery.Items) {
                pkg.Write(item.Prefab ?? "");
                pkg.Write(item.Stack);
                pkg.Write(item.Quality);
                pkg.Write(item.Magic);
                pkg.Write(item.Rarity);
            }
            pkg.Write(delivery.Inventories.Count);
            foreach (string inventory in delivery.Inventories) { pkg.Write(inventory ?? ""); }
        }

        internal static Delivery Read(ZPackage pkg) {
            Delivery delivery = new Delivery {
                Id = pkg.ReadLong(),
                Reason = pkg.ReadString(),
                Coins = pkg.ReadInt(),
            };
            int items = pkg.ReadInt();
            for (int i = 0; i < items; i++) {
                delivery.Items.Add(new DeliveryItem {
                    Prefab = pkg.ReadString(),
                    Stack = pkg.ReadInt(),
                    Quality = pkg.ReadInt(),
                    Magic = pkg.ReadBool(),
                    Rarity = pkg.ReadInt(),
                });
            }
            int inventories = pkg.ReadInt();
            for (int i = 0; i < inventories; i++) { delivery.Inventories.Add(pkg.ReadString()); }
            return delivery;
        }
    }
}
