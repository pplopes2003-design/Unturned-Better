using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// F6: enumerate installed Steam gun APIs for a future authoritative
    /// repeating-burst adapter. Reflection for signatures ONLY, no invocation.
    /// </summary>
    internal sealed class GunApiDiscovery : MonoBehaviour
    {
        private const int MaxMethods = 90;
        private const int MaxFields = 100;
        private const int MaxEvents = 30;
        private const int MaxActiveGuns = 12;

        private static string TypeName(Type t)
        {
            return t == null ? "(null)" : (t.FullName ?? t.Name);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F6))
                Discover();
        }

        private void Discover()
        {
            try
            {
                var output = new StringBuilder(32768);
                output.AppendLine("Unturned 2 - Steam Gun API inventory (READ ONLY)");
                output.AppendLine("Timestamp UTC: " + DateTime.UtcNow.ToString("o"));
                output.AppendLine("Unity: " + Application.unityVersion);
                var game = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (game == null)
                {
                    ProbeLogger.Log("GUN_API_DISCOVERY_UNAVAILABLE: Assembly-CSharp not loaded.");
                    return;
                }
                string[] requiredTypes = {
                    "SDG.Unturned.UseableGun",
                    "SDG.Unturned.ItemGunAsset",
                    "SDG.Unturned.PlayerEquipment",
                    "SDG.Unturned.PlayerInput",
                    "SDG.Unturned.PlayerAnimator"
                };

                foreach (string typeName in requiredTypes)
                {
                    Type type = game.GetType(typeName, false);
                    output.AppendLine("TYPE " + typeName + " = " + (type == null ? "NOT FOUND" : "FOUND"));
                    if (type == null) continue;
                    BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                         BindingFlags.Instance | BindingFlags.Static |
                                         BindingFlags.DeclaredOnly;
                    try
                    {
                        MethodInfo[] methods = type.GetMethods(flags);
                        int written = 0;
                        foreach (MethodInfo method in methods)
                        {
                            if (written++ >= MaxMethods) break;
                            string signature = method.Name;
                            try
                            {
                                signature = TypeName(method.ReturnType) + " " + method.Name +
                                    "(" + string.Join(", ", method.GetParameters()
                                        .Select(p => TypeName(p.ParameterType)).ToArray()) + ")";
                            }
                            catch { }
                            output.AppendLine("  METHOD " + signature);
                        }
                        output.AppendLine("  METHODS_FOUND " + methods.Length + " MAX_WRITTEN " + MaxMethods);
                    }
                    catch (Exception ex)
                    {
                        output.AppendLine("  METHOD_ERROR " + ex.GetType().Name);
                    }
                    try
                    {
                        FieldInfo[] fields = type.GetFields(flags);
                        for (int i = 0; i < Math.Min(fields.Length, MaxFields); i++)
                            output.AppendLine("  FIELD " + TypeName(fields[i].FieldType) + " " + fields[i].Name);
                        output.AppendLine("  FIELDS_FOUND " + fields.Length);
                    }
                    catch (Exception ex)
                    {
                        output.AppendLine("  FIELD_ERROR " + ex.GetType().Name);
                    }
                    try
                    {
                        EventInfo[] events = type.GetEvents(flags);
                        for (int i = 0; i < Math.Min(events.Length, MaxEvents); i++)
                            output.AppendLine("  EVENT " + events[i].Name + " " +
                                              TypeName(events[i].EventHandlerType));
                    }
                    catch (Exception ex)
                    {
                        output.AppendLine("  EVENT_ERROR " + ex.GetType().Name);
                    }
                }

                // Existing in-world gun components may be absent if none is equipped.
                int active = 0;
                foreach (MonoBehaviour b in FindObjectsOfType<MonoBehaviour>())
                {
                    if (active >= MaxActiveGuns) break;
                    if (b == null || b.GetType().FullName != "SDG.Unturned.UseableGun") continue;
                    output.AppendLine("ACTIVE_GUN component=" + b.GetInstanceID() +
                                      " object=" + b.gameObject.name);
                    active++;
                }
                output.AppendLine("ACTIVE_GUN_COUNT " + active);
                ProbeLogger.SaveDiagnostic("gun-api-discovery.txt", output.ToString());
                ProbeLogger.Log("GUN_API_DISCOVERY_OK: F6 found Steam weapon APIs; read gun-api-discovery.txt");
            }
            catch (Exception ex)
            {
                ProbeLogger.Log("GUN_API_DISCOVERY_FAILED: " + ex);
            }
        }
    }
}
