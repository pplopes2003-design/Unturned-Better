// Experimental architectural audit. Editor-only, invoked manually in Play mode.
// Does NOT modify any prefab, collider, navmesh, or runtime NPC path.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SDG.Unturned;
using UnityEditor;
using UnityEngine;

internal static class MobileNPCBuildingAssetAudit
{
    [MenuItem("Unturned Better/Diagnostics/Export Loaded Building Colliders")]
    private static void ExportLoadedBuildings()
    {
        if (!EditorApplication.isPlaying || !Level.isLoaded || LevelObjects.objects == null)
        {
            Debug.LogWarning("[BuildingAssetAudit] Start Play mode and load a map first.");
            return;
        }

        string outputDirectory = Path.Combine(Application.dataPath, "../Logs");
        Directory.CreateDirectory(outputDirectory);
        string output = Path.Combine(outputDirectory, "BuildingAssetAudit.csv");
        int objectCount = 0;
        int colliderCount = 0;
        const int maximumObjects = 350;
        using (var writer = new StreamWriter(output, false, new UTF8Encoding(true)))
        {
            writer.WriteLine("asset_name,asset_id,asset_guid,asset_type,object_x,object_y,object_z," +
                "model_root,nav_active,collider_path,collider_type,layer,layer_name,is_trigger," +
                "bounds_center,bounds_size,mesh_readable,collider_enabled");
            // Distinguish instantiated map objects from asset prefabs:
            // scan actual placed collision hierarchies visible to Unity physics.
            foreach (List<LevelObject> region in LevelObjects.objects)
            {
                if (region == null) continue;
                foreach (LevelObject levelObject in region)
                {
                    if (levelObject == null || levelObject.asset == null ||
                        levelObject.transform == null)
                        continue;
                    EObjectType type = levelObject.asset.type;
                    if (type != EObjectType.LARGE && type != EObjectType.MEDIUM)
                        continue;
                    Transform root = levelObject.transform;
                    Transform nav = root.Find("Nav");
                    string navState = nav == null ? "absent_in_runtime" :
                        nav.gameObject.activeInHierarchy ? "active" : "inactive";
                    Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
                    foreach (Collider col in colliders)
                    {
                        if (col == null) continue;
                        var mesh = col as MeshCollider;
                        writer.WriteLine(string.Join(",", new [] {
                            Csv(levelObject.asset.name),
                            Csv(levelObject.id.ToString()),
                            Csv(levelObject.GUID.ToString("N")),
                            Csv(type.ToString()),
                            Csv(root.position.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)),
                            Csv(root.position.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)),
                            Csv(root.position.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture)),
                            Csv(root.name),
                            Csv(navState),
                            Csv(GetHierarchyPath(root, col.transform)),
                            Csv(col.GetType().Name),
                            Csv(col.gameObject.layer.ToString()),
                            Csv(LayerMask.LayerToName(col.gameObject.layer)),
                            Csv(col.isTrigger.ToString()),
                            Csv(col.bounds.center.ToString("F3")),
                            Csv(col.bounds.size.ToString("F3")),
                            Csv(mesh == null || mesh.sharedMesh == null ? "not_mesh" : mesh.sharedMesh.isReadable.ToString()),
                            Csv(col.enabled.ToString())
                        }));
                        colliderCount++;
                    }
                    objectCount++;
                    if (objectCount >= maximumObjects)
                        break;
                }
                if (objectCount >= maximumObjects) break;
            }
        }

        Debug.Log("[BuildingAssetAudit] Wrote " + objectCount + " placed medium/large objects and " +
            colliderCount + " colliders to " + Path.GetFullPath(output) +
            ". Nav may be absent in non-server editor play; compare with live server.");
        EditorUtility.RevealInFinder(output);
    }

    private static string GetHierarchyPath(Transform root, Transform current)
    {
        var segments = new List<string>();
        while (current != null)
        {
            segments.Add(current.name);
            if (current == root) break;
            current = current.parent;
        }
        segments.Reverse();
        return string.Join("/", segments.ToArray());
    }

    private static string Csv(string value)
    {
        return "\"" + (value ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
    }
}
