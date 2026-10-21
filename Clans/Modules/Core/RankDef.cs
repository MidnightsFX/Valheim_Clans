namespace Clans.Core {
    // One rank in a clan. A clan's ranks are an ordered list: index 0 is the leader rank, the last entry is
    // the rank new members join at. Both of those are Fixed (cannot be removed or moved); the leader can add,
    // rename, reorder and remove the ones in between, up to the server's MaxRanks.
    //
    // Id is stable for the clan's lifetime, so a member keeps pointing at the right rank when ranks are
    // reordered or renamed. Name is free text; an empty name falls back to the localized default for the
    // rank's position.
    public class RankDef {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public ClanPermission Permissions { get; set; }
        public bool Fixed { get; set; }

        public RankDef Clone() {
            return new RankDef { Id = Id, Name = Name, Permissions = Permissions, Fixed = Fixed };
        }

        public void Write(ZPackage pkg) {
            pkg.Write(Id);
            pkg.Write(Name ?? "");
            pkg.Write((int)Permissions);
            pkg.Write(Fixed);
        }

        public static RankDef Read(ZPackage pkg) {
            return new RankDef {
                Id = pkg.ReadInt(),
                Name = pkg.ReadString(),
                Permissions = (ClanPermission)(uint)pkg.ReadInt(),
                Fixed = pkg.ReadBool(),
            };
        }
    }
}
