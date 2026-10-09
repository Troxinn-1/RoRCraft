using System;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using RoRCraft;

namespace RoRCraftPolish {
    internal static class RenderPacketReuse {
        private static bool enabled;
        private static int depth;
        private static SkyMemory owner;
        private static IntPtr pointer;
        private static RenderPacketBuffers buffers;
        private static FieldInfo pointerField;
        public static long ReusedPayloadBytes,ReusedPackets,AllocatedPayloadBytes,AllocatedArrays;
        public static void Install(Harmony harmony,bool useBuffers) {
            enabled=useBuffers;
            if(!enabled) return;
            pointerField=AccessTools.Field(typeof(SkyMemory),"ptr");
            if(pointerField==null) throw new MissingFieldException("SkyMemory.ptr");
            harmony.Patch(AccessTools.Method(typeof(SkyMemory),"DrainRender"),
                prefix:new HarmonyMethod(typeof(RenderPacketReuse),"BeginDrain"),
                finalizer:new HarmonyMethod(typeof(RenderPacketReuse),"EndDrain"));
            harmony.Patch(typeof(SkyMemory).GetMethod("Bytes",new Type[]{typeof(long),typeof(int)}),
                prefix:new HarmonyMethod(typeof(RenderPacketReuse),"Read"));
            harmony.Patch(AccessTools.Method(typeof(SkyMemory),"Dispose"),
                prefix:new HarmonyMethod(typeof(RenderPacketReuse),"Disposed"));
        }
        private static void BeginDrain() {depth++;}
        private static void EndDrain() {depth=Math.Max(0,depth-1);}
        private static unsafe bool Read(SkyMemory __instance,long p,int n,ref byte[] __result) {
            if(!enabled || depth==0 || p<SkyMemory.Render+0x88 || n<=0 || n>8*1024*1024 || p>SkyMemory.Size-n) return true;
            int type=__instance.I(p-8);
            if((type!=5 && type!=6 && type!=7) || __instance.I(p-4)!=n) return true;
            if(!ReferenceEquals(owner,__instance)) {
                pointer=(IntPtr)Pointer.Unbox(pointerField.GetValue(__instance));
                owner=__instance;buffers=new RenderPacketBuffers();
            }
            if(pointer==IntPtr.Zero) return true;
            long oldBytes=buffers.AllocatedPayloadBytes,oldArrays=buffers.AllocatedArrays;
            var result=buffers.Get(type,n);
            AllocatedPayloadBytes+=buffers.AllocatedPayloadBytes-oldBytes;AllocatedArrays+=buffers.AllocatedArrays-oldArrays;
            Marshal.Copy(new IntPtr(pointer.ToInt64()+p),result,0,n);
            __result=result;ReusedPackets++;ReusedPayloadBytes+=n;
            return false;
        }
        private static void Disposed(SkyMemory __instance) {if(ReferenceEquals(owner,__instance)) Clear();}
        public static void Clear() {owner=null;pointer=IntPtr.Zero;buffers=null;depth=0;}
    }
}
