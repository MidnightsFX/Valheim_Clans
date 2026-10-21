namespace Clans.Core {
    // A clan emblem: a background shape and a symbol on top of it, each from the emblem catalog
    // (emblems.yaml) and each tinted with its own colour. Colours are "#RRGGBB".
    public class EmblemData {
        public string BackId { get; set; } = "";
        public string BackColor { get; set; } = "";
        public string FrontId { get; set; } = "";
        public string FrontColor { get; set; } = "";

        public EmblemData Clone() {
            return new EmblemData { BackId = BackId, BackColor = BackColor, FrontId = FrontId, FrontColor = FrontColor };
        }

        public void Write(ZPackage pkg) {
            pkg.Write(BackId ?? "");
            pkg.Write(BackColor ?? "");
            pkg.Write(FrontId ?? "");
            pkg.Write(FrontColor ?? "");
        }

        public static EmblemData Read(ZPackage pkg) {
            return new EmblemData {
                BackId = pkg.ReadString(),
                BackColor = pkg.ReadString(),
                FrontId = pkg.ReadString(),
                FrontColor = pkg.ReadString(),
            };
        }

        public bool SameAs(EmblemData other) {
            return other != null && BackId == other.BackId && BackColor == other.BackColor
                && FrontId == other.FrontId && FrontColor == other.FrontColor;
        }
    }
}
