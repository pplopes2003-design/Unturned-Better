using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Read-only runtime API inventory. F10 enumerates relevant managed
    /// types/method signatures to ground the future NPC adapter in the actual
    /// installed Steam game rather than inventing Unturned APIs.
    /// </summary>
    internal sealed class NpcApiDiscovery : MonoBehaviour
    {
        private const int MaxTypes = 150;
        private const int MaxMethodsPerType = 24;
        private const int MaxComponents = 120;
        private const int MaxOutputChars = 200000;
        private bool _running;
        private static readonly string[] Keywords =
        {
            "npc", "zombie", "animal", "navigation", "pathfinding",
            "levelobject", "door", "barricade"
        };

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10) && !_running)
                Discover();
        }

        private static bool Matches(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            foreach (string term in Keywords)
                if (lower.Contains(term)) return true;
            return false;
        }

        private static string SafeTypeName(Type t)
        {
            return t == null ? "(null)" : (t.FullName ?? t.Name);
        }

        private void Discover()
        {
            _running = true;
            try
            {
                var report = new StringBuilder(32768);
                report.AppendLine("Unturned2 Line2 NPC API discovery");
                report.AppendLine("UTC: " + DateTime.UtcNow.ToString("o"));
                report.AppendLine("Unity: " + Application.unityVersion);
                int typeCount = 0;

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string assemblyName;
                    try { assemblyName = assembly.GetName().Name; }
                    catch { continue; }
                    // Scan game or mod assemblies, not entire .NET/Unity framework.
                    if (assemblyName != "Assembly-CSharp" &&
                        !assemblyName.StartsWith("SDG.", StringComparison.Ordinal) &&
                        !assemblyName.StartsWith("Unturned2.", StringComparison.Ordinal))
                        continue;

                    Type[] types;
                    try { types = assembly.GetTypes(); }
                    catch (ReflectionTypeLoadException ex)
                    {
                        types = ex.Types.Where(x => x != null).ToArray();
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine("ASSEMBLY_ERROR " + assemblyName + ": " + ex.Message);
                        continue;
                    }

                    foreach (Type t in types)
                    {
                        if (typeCount >= MaxTypes || report.Length > MaxOutputChars) break;
                        if (!Matches(t.Name)) continue;
                        typeCount++;
                        report.AppendLine("TYPE " + SafeTypeName(t) + " assembly=" + assemblyName +
                                          " base=" + SafeTypeName(t.BaseType));
                        try
                        {
                            MethodInfo[] methods = t.GetMethods(
                                BindingFlags.Instance | BindingFlags.Static |
                                BindingFlags.Public | BindingFlags.DeclaredOnly);
                            int written = 0;
                            foreach (MethodInfo method in methods)
                            {
                                if (written++ >= MaxMethodsPerType) break;
                                string signature;
                                try
                                {
                                    signature = method.ReturnType.Name + " " + method.Name + "(" +
                                        string.Join(", ", method.GetParameters()
                                            .Select(p => p.ParameterType.Name + " " + p.Name).ToArray()) + ")";
                                }
                                catch { signature = method.Name + "(signature unavailable)"; }
                                report.AppendLine("  METHOD " + signature);
                            }
                        }
                        catch (Exception ex)
                        {
                            report.AppendLine("  METHOD_ERROR " + ex.GetType().Name);
                        }
                    }
                    if (typeCount >= MaxTypes || report.Length > MaxOutputChars) break;
                }

                // Enumerates only matching active MonoBehaviours to help
                // distinguish NPC prefabs from the built-in static quest NPCs.
                int found = 0;
                MonoBehaviour[] components = FindObjectsOfType<MonoBehaviour>();
                foreach (MonoBehaviour component in components)
                {
                    if (found >= MaxComponents || report.Length > MaxOutputChars) break;
                    if (component == null || !Matches(component.GetType().Name)) continue;
                    found++;
                    Transform transform = component.transform;
                    report.AppendLine("INSTANCE " + component.GetType().FullName +
                        " object=" + transform.name + " position=" +
                        transform.position.ToString("F2"));
                }
                report.AppendLine("TypeCount=" + typeCount + " ActiveComponents=" + found);
                ProbeLogger.SaveDiagnostic("npc-api-discovery.txt", report.ToString());
                ProbeLogger.Log("NPC_API_DISCOVERY_OK: types=" + typeCount +
                                " components=" + found +
                                ". Inspect npc-api-discovery.txt; F10 re-scans.");
            }
            catch (Exception ex)
            {
                ProbeLogger.Log("NPC_API_DISCOVERY_FAILED: " + ex);
            }
            finally
            {
                _running = false;
            }
        }
    }
}
