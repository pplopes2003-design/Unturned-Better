using UnityEditor;
using UnityEngine;

/// <summary>
/// Developer-only command to build the matching Unturned Better dedicated
/// server executable without switching to an unrelated Steam U3DS binary.
/// </summary>
public partial class BuildMethods
{
    [MenuItem("Unturned Better/Build Matching Dedicated Server (Windows x64)")]
    public static void BuildUnturnedBetterDedicatedServerMenu()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
        {
            Debug.LogError("[UnturnedBetterHost] Switch the Unity active build target to Windows x64 before building.");
            return;
        }

        bool success = buildWindows64DedicatedServer();
        if (success)
        {
            Debug.Log("[UnturnedBetterHost] Compatible dedicated server built in " +
                "Builds/Windows64_Headless. Check the UnturnedBetterDedicated.marker " +
                "and launch it using Play > Host Friends (LAN) in the client.");
        }
        else
        {
            Debug.LogError("[UnturnedBetterHost] Dedicated-server build failed. " +
                "Inspect Unity Console; hosting cannot use vanilla U3DS as a fallback.");
        }
    }
}
