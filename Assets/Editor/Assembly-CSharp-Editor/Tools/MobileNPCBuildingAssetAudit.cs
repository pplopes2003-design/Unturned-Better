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

        ExportSourcePrefabs(outputDirectory);

        Debug.Log("[BuildingAssetAudit] Wrote " + objectCount + " placed medium/large objects and " +
            colliderCount + " colliders to " + Path.GetFullPath(output) +
            ". Nav may be absent in non-server editor play; compare with live server.");
        EditorUtility.RevealInFinder(output);
    }

    /// <summary>
    /// Source prefabs are not the same as spawned scene instances: this captures
    /// the precise authoring representation of Object / Clip / Nav separately.
    /// </summary>
    private static void ExportSourcePrefabs(string outputDirectory)
    {
        string filePath = Path.Combine(outputDirectory, "BuildingPrefabAudit.csv");
        var examinedAssets = new HashSet<Guid>();
        int assetCount = 0;
        const int assetLimit = 120;
        using (var writer = new StreamWriter(filePath, false, new UTF8Encoding(true)))
        {
            writer.WriteLine("asset_name,asset_id,asset_guid,asset_type,prefab_variant," +
                "prefab_path,component_type,layer_name,layer_id,is_trigger,enabled," +
                "bounds_size,mesh_vertices,mesh_triangles");
            foreach (List<LevelObject> region in LevelObjects.objects)
            {
                if (region == null) continue;
                foreach (LevelObject levelObject in region)
                {
                    if (levelObject == null || levelObject.asset == null) continue;
                    ObjectAsset asset = levelObject.asset;
                    if (asset.type != EObjectType.LARGE && asset.type != EObjectType.MEDIUM
                        || !examinedAssets.Add(levelObject.GUID))
                        continue;
                    assetCount++;

                    GameObject[] prefabs = {
                        asset.DebugGetVisualPrefabForBuildingAudit(),
                        asset.DebugGetClipPrefabForBuildingAudit(),
                        asset.DebugGetNavPrefabForBuildingAudit()
                    };
                    string[] names = { "Object", "Clip", "Nav" };
                    for (int i = 0; i < prefabs.Length; ++i)
                    {
                        GameObject prefab = prefabs[i];
                        if (prefab == null)
                        {
                            WritePrefabRow(writer, levelObject, names[i], "-", "MISSING",
                                "-", -1, "-", "-", "-", 0, 0);
                            continue;
                        }
                        Transform root = prefab.transform;
                        var colliders = prefab.GetComponentsInChildren<Collider>(true);
                        foreach (Collider collider in colliders)
                        {
                            MeshCollider mesh = collider as MeshCollider;
                            int vertices = mesh != null && mesh.sharedMesh != null ?
                                mesh.sharedMesh.vertexCount : 0;
                            int triangles = mesh != null && mesh.sharedMesh != null ?
                                (int)(mesh.sharedMesh.GetIndexCount(0) / 3) : 0;
                            WritePrefabRow(writer, levelObject, names[i],
                                GetHierarchyPath(root, collider.transform),
                                collider.GetType().Name,
                                LayerMask.LayerToName(collider.gameObject.layer),
                                collider.gameObject.layer,
                                collider.isTrigger.ToString(), collider.enabled.ToString(),
                                collider.bounds.size.ToString("F3"), vertices, triangles);
                        }
                        // Nav can be an authored mesh rather than colliders.
                        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                        {
                            if (filter.sharedMesh == null) continue;
                            WritePrefabRow(writer, levelObject, names[i],
                                GetHierarchyPath(root, filter.transform), "MeshFilter",
                                LayerMask.LayerToName(filter.gameObject.layer),
                                filter.gameObject.layer, "-", "-",
                                "-", filter.sharedMesh.vertexCount,
                                (int)(filter.sharedMesh.GetIndexCount(0) / 3));
                        }
                        if (colliders.Length == 0
                            && prefab.GetComponentsInChildren<MeshFilter>(true).Length == 0)
                        {
                            WritePrefabRow(writer, levelObject, names[i], root.name,
                                "EMPTY", LayerMask.LayerToName(root.gameObject.layer),
                                root.gameObject.layer, "-", "-", "-", 0, 0);
                        }
                    }
                    if (assetCount >= assetLimit) break;
                }
                if (assetCount >= assetLimit) break;
            }
        }
        Debug.Log("[BuildingAssetAudit] Compared Object, Clip and Nav source prefabs for " +
            assetCount + " unique building assets: " + Path.GetFullPath(filePath));
    }

    private static void WritePrefabRow(StreamWriter writer, LevelObject item,
        string variant, string path, string type, string layer, int layerId,
        string trigger, string enabled, string bounds, int vertices, int triangles)
    {
        writer.WriteLine(string.Join(",", new [] {
            Csv(item.asset.name), Csv(item.id.ToString()),
            Csv(item.GUID.ToString("N")), Csv(item.asset.type.ToString()),
            Csv(variant), Csv(path), Csv(type), Csv(layer), Csv(layerId.ToString()),
            Csv(trigger), Csv(enabled), Csv(bounds), Csv(vertices.ToString()),
            Csv(triangles.ToString())
        }));
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
