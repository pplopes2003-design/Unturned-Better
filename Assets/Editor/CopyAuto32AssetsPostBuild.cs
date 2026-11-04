using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

/// <summary>
/// Includes the Unturned Better optional native .dat items alongside the
/// built standalone executable. Unity's managed-code build alone does NOT
/// automatically ship external Bundles/Items content.
/// </summary>
internal static class CopyAuto32AssetsPostBuild
{
    [PostProcessBuild(1000)]
    private static void InstallOptionalContent(BuildTarget target, string outputPath)
    {
        if (target != BuildTarget.StandaloneWindows
            && target != BuildTarget.StandaloneWindows64
            && target != BuildTarget.StandaloneLinux64)
            return;
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string source = Path.Combine(projectRoot,
            "Mods", "UnturnedBetter", "Bundles", "Items");
        if (!Directory.Exists(source))
        {
            Debug.LogWarning("[Auto32Build] Optional item data folder not present: " + source);
            return;
        }
        string playerDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(playerDirectory))
            return;
        string destination = Path.Combine(playerDirectory, "Bundles", "Items");
        string[] relativeFolders =
        {
            Path.Combine("Guns", "Bluntforce_Auto32"),
            Path.Combine("Magazines", "Bluntforce_32_Drum")
        };
        foreach (string relative in relativeFolders)
        {
            string from = Path.Combine(source, relative);
            if (!Directory.Exists(from))
                continue;
            string to = Path.Combine(destination, relative);
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from, "*.dat",
                SearchOption.TopDirectoryOnly))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        }
        Debug.Log("[Auto32Build] Installed custom gun/drum .dat assets alongside player: "
            + destination);
    }
}
