// Checked-in native NetInvokable bridge; generated-method conventions
// follow Assets/Runtime/Assembly-CSharp/NetGen/NetInvokable/*.cs.
// Do not manually edit RPC argument lists without updating this file.
using SDG.NetPak;

namespace SDG.Unturned
{
    [NetInvokableGeneratedClass(typeof(MobileNPCNetworkReplica))]
    public static class MobileNPCNetworkReplica_NetMethods
    {
        [NetInvokableGeneratedMethod(nameof(MobileNPCNetworkReplica.ReceiveNpcSnapshot),
            ENetInvokableGeneratedMethodPurpose.Read)]
        public static void ReceiveNpcSnapshot_Read(in ClientInvocationContext context)
        {
            NetPakReader reader = context.reader;
            int instanceId;
            if (!reader.ReadInt32(out instanceId))
            {
                context.ReadParameterFailed(nameof(instanceId));
                return;
            }
            UnityEngine.Vector3 position;
            if (!reader.ReadClampedVector3(out position))
            {
                context.ReadParameterFailed(nameof(position));
                return;
            }
            byte yaw;
            if (!reader.ReadUInt8(out yaw))
            {
                context.ReadParameterFailed(nameof(yaw));
                return;
            }
            ushort gunId;
            if (!reader.ReadUInt16(out gunId))
            {
                context.ReadParameterFailed(nameof(gunId));
                return;
            }
            byte motionFlags;
            if (!reader.ReadUInt8(out motionFlags))
            {
                context.ReadParameterFailed(nameof(motionFlags));
                return;
            }
            byte headPitch;
            if (!reader.ReadUInt8(out headPitch))
            {
                context.ReadParameterFailed(nameof(headPitch));
                return;
            }
            MobileNPCNetworkReplica.ReceiveNpcSnapshot(instanceId, position, yaw, gunId, motionFlags, headPitch);
        }

        [NetInvokableGeneratedMethod(nameof(MobileNPCNetworkReplica.ReceiveNpcSnapshot),
            ENetInvokableGeneratedMethodPurpose.Write)]
        public static void ReceiveNpcSnapshot_Write(NetPakWriter writer,
            int instanceId, UnityEngine.Vector3 position, byte yaw, ushort gunId, byte motionFlags, byte headPitch)
        {
            writer.WriteInt32(instanceId);
            writer.WriteClampedVector3(position);
            writer.WriteUInt8(yaw);
            writer.WriteUInt16(gunId);
            writer.WriteUInt8(motionFlags);
            writer.WriteUInt8(headPitch);
        }

        [NetInvokableGeneratedMethod(nameof(MobileNPCNetworkReplica.ReceiveNpcIdentity),
            ENetInvokableGeneratedMethodPurpose.Read)]
        public static void ReceiveNpcIdentity_Read(in ClientInvocationContext context)
        {
            NetPakReader reader = context.reader;
            int id;
            if (!reader.ReadInt32(out id))
            {
                context.ReadParameterFailed(nameof(id));
                return;
            }
            string name;
            if (!reader.ReadString(out name))
            {
                context.ReadParameterFailed(nameof(name));
                return;
            }
            int seed;
            if (!reader.ReadInt32(out seed))
            {
                context.ReadParameterFailed(nameof(seed));
                return;
            }
            MobileNPCNetworkReplica.ReceiveNpcIdentity(id, name, seed);
        }

        [NetInvokableGeneratedMethod(nameof(MobileNPCNetworkReplica.ReceiveNpcIdentity),
            ENetInvokableGeneratedMethodPurpose.Write)]
        public static void ReceiveNpcIdentity_Write(NetPakWriter writer,
            int id, string name, int seed)
        {
            writer.WriteInt32(id);
            writer.WriteString(name);
            writer.WriteInt32(seed);
        }

        [NetInvokableGeneratedMethod(nameof(MobileNPCNetworkReplica.ReceiveNpcShot),
            ENetInvokableGeneratedMethodPurpose.Read)]
        public static void ReceiveNpcShot_Read(in ClientInvocationContext context)
        {
            NetPakReader reader = context.reader;
            int id;
            if (!reader.ReadInt32(out id))
            {
                context.ReadParameterFailed(nameof(id));
                return;
            }
            UnityEngine.Vector3 position;
            if (!reader.ReadClampedVector3(out position))
            {
                context.ReadParameterFailed(nameof(position));
                return;
            }
            ushort gunId;
            if (!reader.ReadUInt16(out gunId))
            {
                context.ReadParameterFailed(nameof(gunId));
                return;
            }
            MobileNPCNetworkReplica.ReceiveNpcShot(id, position, gunId);
        }

        [NetInvokableGeneratedMethod(nameof(MobileNPCNetworkReplica.ReceiveNpcShot),
            ENetInvokableGeneratedMethodPurpose.Write)]
        public static void ReceiveNpcShot_Write(NetPakWriter writer,
            int id, UnityEngine.Vector3 position, ushort gunId)
        {
            writer.WriteInt32(id);
            writer.WriteClampedVector3(position);
            writer.WriteUInt16(gunId);
        }

        [NetInvokableGeneratedMethod(nameof(MobileNPCNetworkReplica.ReceiveNpcReload),
            ENetInvokableGeneratedMethodPurpose.Read)]
        public static void ReceiveNpcReload_Read(in ClientInvocationContext context)
        {
            NetPakReader reader = context.reader;
            int id;
            if (!reader.ReadInt32(out id))
            {
                context.ReadParameterFailed(nameof(id));
                return;
            }
            ushort gunId;
            if (!reader.ReadUInt16(out gunId))
            {
                context.ReadParameterFailed(nameof(gunId));
                return;
            }
            MobileNPCNetworkReplica.ReceiveNpcReload(id, gunId);
        }

        [NetInvokableGeneratedMethod(nameof(MobileNPCNetworkReplica.ReceiveNpcReload),
            ENetInvokableGeneratedMethodPurpose.Write)]
        public static void ReceiveNpcReload_Write(NetPakWriter writer,
            int id, ushort gunId)
        {
            writer.WriteInt32(id);
            writer.WriteUInt16(gunId);
        }

        [NetInvokableGeneratedMethod(nameof(MobileNPCNetworkReplica.ReceiveNpcDeath),
            ENetInvokableGeneratedMethodPurpose.Read)]
        public static void ReceiveNpcDeath_Read(in ClientInvocationContext context)
        {
            NetPakReader reader = context.reader;
            int id;
            if (!reader.ReadInt32(out id))
            {
                context.ReadParameterFailed(nameof(id));
                return;
            }
            UnityEngine.Vector3 position;
            if (!reader.ReadClampedVector3(out position))
            {
                context.ReadParameterFailed(nameof(position));
                return;
            }
            MobileNPCNetworkReplica.ReceiveNpcDeath(id, position);
        }

        [NetInvokableGeneratedMethod(nameof(MobileNPCNetworkReplica.ReceiveNpcDeath),
            ENetInvokableGeneratedMethodPurpose.Write)]
        public static void ReceiveNpcDeath_Write(NetPakWriter writer,
            int id, UnityEngine.Vector3 position)
        {
            writer.WriteInt32(id);
            writer.WriteClampedVector3(position);
        }

        [NetInvokableGeneratedMethod(nameof(MobileNPCNetworkReplica.ReceiveNpcGrenade),
            ENetInvokableGeneratedMethodPurpose.Read)]
        public static void ReceiveNpcGrenade_Read(in ClientInvocationContext context)
        {
            NetPakReader reader = context.reader;
            System.Int32 npcId;
            if (!reader.ReadInt32(out npcId))
            {
                context.ReadParameterFailed(nameof(npcId));
                return;
            }
            System.UInt16 throwableId;
            if (!reader.ReadUInt16(out throwableId))
            {
                context.ReadParameterFailed(nameof(throwableId));
                return;
            }
            UnityEngine.Vector3 origin;
            if (!reader.ReadClampedVector3(out origin))
            {
                context.ReadParameterFailed(nameof(origin));
                return;
            }
            UnityEngine.Vector3 velocity;
            if (!reader.ReadClampedVector3(out velocity))
            {
                context.ReadParameterFailed(nameof(velocity));
                return;
            }
            System.Single fuseSeconds;
            if (!reader.ReadFloat(out fuseSeconds))
            {
                context.ReadParameterFailed(nameof(fuseSeconds));
                return;
            }
            MobileNPCNetworkReplica.ReceiveNpcGrenade(npcId, throwableId, origin, velocity, fuseSeconds);
        }

        [NetInvokableGeneratedMethod(nameof(MobileNPCNetworkReplica.ReceiveNpcGrenade),
            ENetInvokableGeneratedMethodPurpose.Write)]
        public static void ReceiveNpcGrenade_Write(NetPakWriter writer,
            System.Int32 npcId, System.UInt16 throwableId, UnityEngine.Vector3 origin, UnityEngine.Vector3 velocity, System.Single fuseSeconds)
        {
            writer.WriteInt32(npcId);
            writer.WriteUInt16(throwableId);
            writer.WriteClampedVector3(origin);
            writer.WriteClampedVector3(velocity);
            writer.WriteFloat(fuseSeconds);
        }

    }
}
