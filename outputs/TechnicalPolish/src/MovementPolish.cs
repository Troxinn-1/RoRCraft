using System;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RoRCraft;
using RoR2;

namespace RoRCraftPolish {
    [BepInPlugin("cz.jirka.rorcraft-movement-polish","RoRCraft movement polish","0.1.1")]
    [BepInDependency("cz.jirka.rorcraft-skycraft",BepInDependency.DependencyFlags.HardDependency)]
    public sealed class MovementPolish : BaseUnityPlugin {
        private static MovementPolish instance;
        private Harmony harmony;
        private ConfigEntry<bool> resample;
        private ConfigEntry<bool> softCorrections;
        private long frequency;
        private bool startActive,startSkill,hasPose;
        private int startTeleport,lastPublishedTeleport=-1;
        private double poseX,poseY,poseZ;
        private object adapter;
        private FieldInfo skillField;
        public static long Sampled,Clamped,Rejected,SoftCorrectionsIssued,OwnershipRepairs;
        [DllImport("kernel32.dll")] private static extern bool QueryPerformanceCounter(out long value);
        [DllImport("kernel32.dll")] private static extern bool QueryPerformanceFrequency(out long value);
        private void Awake() {
            instance=this;
            resample=Config.Bind("Movement","PhysicsTickSampling",true,"Sample Minecraft physics on the host frame clock. No smoothing delay or extrapolation. Restart RoR2 after changing.");
            softCorrections=Config.Bind("Movement","SoftCorrections",false,"Requires matching polish Minecraft jar. Apply collision correction displacement without a spawn teleport or readiness hold. Restart RoR2 after changing.");
            if(!QueryPerformanceFrequency(out frequency) || frequency<=0) {Logger.LogError("Shared Windows QPC clock unavailable; sampler disabled.");enabled=false;return;}
            Type memory=null;
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                memory=assembly.GetType("RoRCraft.SkyMemory");if(memory!=null) break;
            }
            var method=memory==null?null:AccessTools.Method(memory,"McState");
            if(method==null) {Logger.LogError("Installed McState method unavailable; sampler disabled.");enabled=false;return;}
            harmony=new Harmony("cz.jirka.rorcraft-movement-polish");
            RenderPacketReuse.Install(harmony,Config.Bind("Performance","ReuseRenderPackets",true,"Reuse distinct exact-size player and scene packet buffers. Requires synchronous baseline renderer. Restart RoR2 after changing.").Value);
            harmony.Patch(method,postfix:new HarmonyMethod(typeof(MovementPolish),"Resample"));
            Type plugin=AccessTools.TypeByName("RoRCraft.RoRCraftPlugin");
            NativeTerrain.Install(harmony,Logger,Config.Bind("Collision","NativeMeshTriangles",true,"Export actual readable native collision meshes and boxes, including ground under overhangs. Unsupported primitives retain baseline coverage. Restart RoR2 after changing.").Value);
            adapter=UnityEngine.Object.FindObjectOfType(plugin);skillField=AccessTools.Field(plugin,"skillMotor");
            harmony.Patch(AccessTools.Method(plugin,"Update"),prefix:new HarmonyMethod(typeof(MovementPolish),"BeforeUpdate"));
            harmony.Patch(AccessTools.Method(memory,"State"),prefix:new HarmonyMethod(typeof(MovementPolish),"BeforeState"));
            Logger.LogInfo("Physics tick sampler loaded. Uses shared QPC timestamp and existing previous/current Minecraft positions; rendered-pose sampling replaced.");
        }
        private static unsafe void Resample(byte[] __result) {
            if(instance==null || !instance.resample.Value || __result==null || __result.Length<0xC8) return;
            fixed(byte* p=__result) {
                int flags=*(int*)(p+4);
                if((flags&35)!=1) return; // Keep menu/death snapshots exactly as published.
                long now;double phase;
                if(!QueryPerformanceCounter(out now) || !TickSampling.Phase(*(long*)(p+0x68),now,instance.frequency,*(float*)(p+0xB8),out phase)) {Rejected++;return;}
                // Reject discontinuous or stale tick pairs around teleports.
                for(int axis=0;axis<3;axis++) {
                    double a=*(double*)(p+0x70+axis*8),b=*(double*)(p+0x88+axis*8),rendered=*(double*)(p+8+axis*8);
                    if(double.IsNaN(a)||double.IsInfinity(a)||double.IsNaN(b)||double.IsInfinity(b)||Math.Abs(b-a)>4 || Math.Abs(b-rendered)>4) {Rejected++;return;}
                }
                for(int axis=0;axis<3;axis++) *(double*)(p+8+axis*8)=TickSampling.Position(*(double*)(p+0x70+axis*8),*(double*)(p+0x88+axis*8),phase);
                float eyeA=*(float*)(p+0xA0),eyeB=*(float*)(p+0xA4);
                if(eyeA>.1f && eyeA<4 && eyeB>.1f && eyeB<4) *(float*)(p+0x28)=(float)TickSampling.Position(eyeA,eyeB,phase);
                Sampled++;if(phase>=1) Clamped++;
                instance.hasPose=true;instance.poseX=*(double*)(p+8);instance.poseY=*(double*)(p+16);instance.poseZ=*(double*)(p+24);
            }
        }
        private static void BeforeUpdate(bool ___active,bool ___skillMotor,int ___teleport,CharacterBody ___controlled) {
            if(instance==null) return;
            instance.startActive=___active;instance.startSkill=___skillMotor;instance.startTeleport=___teleport;instance.hasPose=false;
            // Spawn/body states can re-enable the native motor after takeover.
            // Minecraft owns ordinary movement; native skill motion explicitly
            // transfers ownership via skillMotor and remains untouched here.
            if(___active && !___skillMotor && ___controlled!=null && ___controlled.characterMotor!=null) {
                var motor=___controlled.characterMotor;
                if(motor.enabled || motor.Motor!=null && motor.Motor.enabled) {
                    motor.enabled=false;if(motor.Motor!=null) motor.Motor.enabled=false;
                    OwnershipRepairs++;
                    if(OwnershipRepairs==1) instance.Logger.LogWarning("Native motor was re-enabled during Minecraft movement; restored exclusive Minecraft movement ownership.");
                }
            }
        }
        private static void BeforeState(SkyMemory __instance,int flags,int teleport,double x,double y,double z) {
            if(instance==null || !instance.softCorrections.Value || teleport==instance.lastPublishedTeleport) return;
            instance.lastPublishedTeleport=teleport;
            __instance.I(0x158,0);
            if(!instance.startActive || instance.startSkill || flags!=1 || teleport==instance.startTeleport || !instance.hasPose || (bool)instance.skillField.GetValue(instance.adapter)) return;
            double dx=x-instance.poseX,dy=y-instance.poseY,dz=z-instance.poseZ;
            if(double.IsNaN(dx)||double.IsNaN(dy)||double.IsNaN(dz)||Math.Abs(dx)>4||Math.Abs(dy)>4||Math.Abs(dz)>4) return;
            // Publish before State's release barrier. Client reads these extension
            // fields inside the same SkyState seqlock and matches teleport sequence.
            __instance.D(0x140,dx);__instance.D(0x148,dy);__instance.D(0x150,dz);__instance.I(0x158,teleport);
            SoftCorrectionsIssued++;
        }
        private void OnDestroy() {Logger.LogInfo("Movement totals: ownership repairs "+OwnershipRepairs+", render packets reused "+RenderPacketReuse.ReusedPackets+", reused payload bytes "+RenderPacketReuse.ReusedPayloadBytes+", new pooled payload bytes "+RenderPacketReuse.AllocatedPayloadBytes+", native mesh exports "+NativeTerrain.Exports+", failed native packets "+NativeTerrain.FailedPackets);if(harmony!=null) harmony.UnpatchSelf();RenderPacketReuse.Clear();NativeTerrain.Clear();instance=null;}
    }
}
