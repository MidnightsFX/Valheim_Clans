using System.IO;
using UnityEditor;
using UnityEngine;

// Builds the "clans" asset bundle from everything under Assets/Clans/Bundle and copies it into the mod
// project (Clans/Assets/clans), where the build embeds it.
//
// From the editor: menu Clans/Build Asset Bundle.
// Headless, with the editor closed:
//   Unity.exe -batchmode -quit -projectPath ClansUnity -executeMethod ClansBundleBuilder.BuildFromCommandLine -logFile build.log
public static class ClansBundleBuilder {
    private const string BundleName = "clans";
    private const string BundleFolder = "Assets/Clans/Bundle";

    [MenuItem("Clans/Build Asset Bundle")]
    public static void Build() {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string stage = Path.Combine(projectRoot, "AssetBundles");
        Directory.CreateDirectory(stage);

        // The folder carries the bundle name, so every asset under it is included without tagging each one.
        AssetImporter folder = AssetImporter.GetAtPath(BundleFolder);
        if (folder == null) {
            Debug.LogError($"[Clans] {BundleFolder} does not exist; nothing to build.");
            return;
        }
        if (folder.assetBundleName != BundleName) {
            folder.assetBundleName = BundleName;
            folder.SaveAndReimport();
        }

        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(stage, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows);
        if (manifest == null) {
            Debug.LogError("[Clans] The asset bundle build failed; see the errors above.");
            return;
        }

        string built = Path.Combine(stage, BundleName);
        if (File.Exists(built) == false) {
            Debug.LogError($"[Clans] The build finished but {built} was not written.");
            return;
        }

        // Prove the bundle loads before handing it to the mod: a bundle that fails here fails in game too.
        AssetBundle check = AssetBundle.LoadFromFile(built);
        if (check == null) {
            Debug.LogError($"[Clans] {built} was written but does not load; not copied.");
            return;
        }
        int assets = check.GetAllAssetNames().Length;
        check.Unload(true);

        string modAssets = Path.GetFullPath(Path.Combine(projectRoot, "..", "Clans", "Assets"));
        Directory.CreateDirectory(modAssets);
        string target = Path.Combine(modAssets, BundleName);
        File.Copy(built, target, true);
        Debug.Log($"[Clans] Built '{BundleName}' ({assets} assets, {new FileInfo(target).Length / 1024} KB) and copied it to {target}. Rebuild the mod to embed it.");
    }

    public static void BuildFromCommandLine() {
        Build();
    }
}
