using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoRCraft;
using UnityEngine;
using Unity.Profiling;
using Stopwatch=System.Diagnostics.Stopwatch;

namespace RoRCraftPolish {
    [BepInPlugin("cz.jirka.rorcraft-polish-diagnostics","RoRCraft movement diagnostics","0.1.0")]
    [BepInDependency("cz.jirka.rorcraft-skycraft", BepInDependency.DependencyFlags.HardDependency)]
    [DefaultExecutionOrder(20000)]
    public sealed class MovementDiagnostics : BaseUnityPlugin {
        private static MovementDiagnostics instance;
        private Harmony harmony;
        private ConfigEntry<bool> enabledCapture;
        private ConfigEntry<string> outputFolder;
        private object adapter;
        private FieldInfo activeField,positionField,mcField,teleportField,bodyField,skillField,rendererField,originField;
        private PropertyInfo sectionCount;
        private CharacterBody cachedBody;
        private Rigidbody cachedRigidbody;
        private readonly Dictionary<MethodBase,int> stages=new Dictionary<MethodBase,int>();
        private readonly double[] ms=new double[9];
        private readonly int[] calls=new int[9];
        private readonly long[] allocatedByStage=new long[9];
        private Func<long> threadAllocationCounter;
        private long previousAllocationCounter=-1;
        private long copyPayloadBytes;
        private long playerReadBytes,sceneReadBytes,textureReadBytes,stateReadBytes,otherReadBytes;
        private int copyReadCalls;
        private FieldInfo pooledPayloadField,pooledArraysField;
        private long previousPooledPayload,previousPooledArrays;
        private struct CallState { public long time,allocated; }
        private readonly float[] frameTimes=new float[1024],sortedTimes=new float[1024];
        private readonly Collider[] nearby=new Collider[64];
        private int frameCount,contacts,contactFrame,lastMc=-1,lastTeleport=-1,capIndex=-1,originalCap,originalVsync;
        private readonly int[] caps={60,120,144,165,-1};
        private Vector3 previousPosition,previousCamera;
        private bool previousActive;
        private Camera sceneCamera;
        private ProfilerRecorder allocations;
        private readonly FrameTiming[] engineTiming=new FrameTiming[1];
        private Sample[] samples;
        private int count;
        private readonly List<Task> pendingWrites=new List<Task>();
        private float nextOverlay,nextSpike;
        private string overlay="",session;
        private long qpcFrequency;
        [DllImport("kernel32.dll")] private static extern bool QueryPerformanceCounter(out long value);
        [DllImport("kernel32.dll")] private static extern bool QueryPerformanceFrequency(out long value);
        private struct Sample {
            public int frame,cap,vsync,fixedTicks,updates,mcFrame,mcFresh,teleports,contacts,grounded,motor,kinematic,skill,sections,gc0,gc1,gc2,rbInterpolation;
            public float time,dt,dx,dy,dz,px,py,pz,cx,cy,cz,camDx,camDy,camDz,inputX,inputY,bx,by,bz,fixedStep,moveX,moveZ,cameraYaw,cameraPitch;
            public long allocated;
            // Unity returns delayed GPU measurements; these are not the same
            // frame as dt/stage timers. Zero/unsupported values stay unavailable.
            public double engineCpuMs,engineGpuMs;
            public long updateBytes,floorBytes,actorsBytes,inventoryBytes,renderBytes,overlayBytes,cameraBytes;
            public long copyPayloadBytes;
            public long playerReadBytes,sceneReadBytes,textureReadBytes,stateReadBytes,otherReadBytes,pooledNewPayloadBytes;
            public int copyReadCalls,mcFlags;
            public float correctionX,correctionY,correctionZ;
            public long tickQpc;
            public float tickAgeMs,tickMs;
            public double prevX,prevY,prevZ,curX,curY,curZ;
            public double updateMs,floorMs,actorsMs,inventoryMs,renderMs,overlayMs,cameraMs,diagnosticMs;
        }
        private void Awake() {
            instance=this;originalCap=Application.targetFrameRate;originalVsync=QualitySettings.vSyncCount;
            QueryPerformanceFrequency(out qpcFrequency);
            try {
                var allocationMethod=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",BindingFlags.Static|BindingFlags.Public);
                if(allocationMethod!=null) {
                    var counter=(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),allocationMethod);
                    long before=counter();var probe=new byte[4096];probe[0]=1;long after=counter();GC.KeepAlive(probe);
                    if(before>=0 && after-before>=4096) threadAllocationCounter=counter;
                    else Logger.LogWarning("Managed allocation counter failed allocation probe; zero/stub readings rejected.");
                }
            } catch(Exception e) {Logger.LogWarning("Managed thread allocation counter unavailable: "+e.Message);}
            enabledCapture=Config.Bind("Developer","Capture",false,"Temporary developer diagnostics. F10 toggles capture and overlay. Disabled for normal play.");
            outputFolder=Config.Bind("Developer","OutputFolder",System.IO.Path.Combine(Paths.PluginPath,"RoRCraftDiagnostics","captures"),"CSV output directory.");
            harmony=new Harmony("cz.jirka.rorcraft-polish-diagnostics");
            // Patch only the deployed adapter; never load or replace the unfinished 0.8 build.
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                var type=assembly.GetType("RoRCraft.RoRCraftPlugin");if(type==null) continue;
                adapter=UnityEngine.Object.FindObjectOfType(type);
                activeField=Field(type,"active");positionField=Field(type,"position");mcField=Field(type,"mc");teleportField=Field(type,"teleport");bodyField=Field(type,"controlled");skillField=Field(type,"skillMotor");rendererField=Field(type,"renderer");
                originField=Field(type,"worldOrigin");
                Hook(type,"Update",0);Hook(type,"FixedUpdate",1);Hook(type,"LateUpdate",2);Hook(type,"ExportFloor",3);Hook(type,"ExportActors",4);Hook(type,"RefreshUpgradeText",5);
                Hook(assembly.GetType("RoRCraft.SkyMemory"),"DrainRender",6);Hook(assembly.GetType("RoRCraft.SkyRenderer"),"Overlay",7);Hook(assembly.GetType("RoRCraft.SkyRenderer"),"Consume",8);
                var copyMethod=assembly.GetType("RoRCraft.SkyMemory").GetMethod("Bytes",new Type[]{typeof(long),typeof(int)});
                harmony.Patch(copyMethod,transpiler:new HarmonyMethod(typeof(MovementDiagnostics),"TracePayloadAllocation"));
                sectionCount=assembly.GetType("RoRCraft.SkyRenderer").GetProperty("SectionCount");
                Logger.LogInfo("Diagnostics attached to deployed adapter "+assembly.GetName().Version+". F10 capture; F11 FPS test cap; F12 save segment. No movement changes.");break;
            }
            if(adapter==null) {Logger.LogError("Deployed RoRCraft adapter not found; diagnostics disabled.");enabled=false;return;}
            var reuse=AccessTools.TypeByName("RoRCraftPolish.RenderPacketReuse");
            if(reuse!=null) {pooledPayloadField=reuse.GetField("AllocatedPayloadBytes");pooledArraysField=reuse.GetField("AllocatedArrays");}
            Logger.LogInfo(threadAllocationCounter!=null?"Allocation source: Mono managed bytes on the main thread (includes diagnostics).":"Allocation source: Unity frame recorder, when available.");
            if(enabledCapture.Value) StartCapture();
        }
        private static FieldInfo Field(Type t,string name) {return t.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);}
        private static void AllocatedPayload(int length,long position,SkyMemory memory) {
            if(instance==null || !instance.enabledCapture.Value) return;
            // This baseline method allocates a new byte[n] on every successful read.
            // Payload excludes array headers and all other allocations; never label it total GC.
            instance.copyPayloadBytes+=length;instance.copyReadCalls++;
            if(position==0x200) instance.stateReadBytes+=length;
            else if(position>=SkyMemory.Render+0x88 && position<=SkyMemory.Size-length) {
                int type=memory.I(position-8);
                if(type==5) instance.playerReadBytes+=length;
                else if(type==6) instance.sceneReadBytes+=length;
                else if(type==1 || type==4) instance.textureReadBytes+=length;
                else instance.otherReadBytes+=length;
            } else instance.otherReadBytes+=length;
        }
        private static IEnumerable<CodeInstruction> TracePayloadAllocation(IEnumerable<CodeInstruction> instructions) {
            foreach(var instruction in instructions) {
                if(instruction.opcode==OpCodes.Newarr && Equals(instruction.operand,typeof(byte))) {
                    var duplicate=new CodeInstruction(OpCodes.Dup);
                    duplicate.labels.AddRange(instruction.labels);instruction.labels.Clear();
                    duplicate.blocks.AddRange(instruction.blocks);instruction.blocks.Clear();
                    yield return duplicate;
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(MovementDiagnostics),"AllocatedPayload"));
                }
                yield return instruction;
            }
        }
        private void Hook(Type type,string name,int stage) {
            if(type==null) return;var method=AccessTools.Method(type,name);if(method==null) return;
            stages[method]=stage;
            harmony.Patch(method,new HarmonyMethod(typeof(MovementDiagnostics),"Before"),new HarmonyMethod(typeof(MovementDiagnostics),"After"));
        }
        private static void Before(out CallState __state) {
            __state=new CallState();
            if(instance!=null && instance.enabledCapture.Value) {__state.time=Stopwatch.GetTimestamp();__state.allocated=instance.threadAllocationCounter==null?-1:instance.threadAllocationCounter();}
        }
        private static void After(MethodBase __originalMethod,CallState __state) {
            if(__state.time==0 || instance==null) return;int stage;
            if(instance.stages.TryGetValue(__originalMethod,out stage)) {
                instance.ms[stage]+=(Stopwatch.GetTimestamp()-__state.time)*1000.0/Stopwatch.Frequency;instance.calls[stage]++;
                if(__state.allocated>=0) instance.allocatedByStage[stage]+=Math.Max(0,instance.threadAllocationCounter()-__state.allocated);
            }
        }
        private void StartCapture() {
            samples=new Sample[131072];count=0;frameCount=0;previousActive=false;session=DateTime.Now.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture);
            previousAllocationCounter=threadAllocationCounter==null?-1:threadAllocationCounter();
            try {allocations=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);} catch(Exception e) {Logger.LogWarning("GC allocation recorder unavailable: "+e.Message);}
            Logger.LogInfo("Movement capture enabled. CSV buffered in RAM; no per-frame disk writes. F12 saves.");
        }
        private void LateUpdate() {
            if(Input.GetKeyDown(KeyCode.F10)) {
                enabledCapture.Value=!enabledCapture.Value;Config.Save();
                if(enabledCapture.Value) StartCapture();else {Save();allocations.Dispose();samples=null;Application.targetFrameRate=originalCap;QualitySettings.vSyncCount=originalVsync;}
            }
            if(!enabledCapture.Value) return;
            if(Input.GetKeyDown(KeyCode.F11)) {
                Save();capIndex=(capIndex+1)%caps.Length;QualitySettings.vSyncCount=0;Application.targetFrameRate=caps[capIndex];
                Logger.LogInfo("FPS test cap "+Application.targetFrameRate+"; VSync disabled for this explicit test. Previous display settings restored when diagnostics ends.");
            }
            if(Input.GetKeyDown(KeyCode.F12)) Save();
            long start=Stopwatch.GetTimestamp();bool active=(bool)activeField.GetValue(adapter);
            if(!active) {previousActive=false;copyPayloadBytes=0;copyReadCalls=0;playerReadBytes=sceneReadBytes=textureReadBytes=stateReadBytes=otherReadBytes=0;Array.Clear(ms,0,ms.Length);Array.Clear(calls,0,calls.Length);Array.Clear(allocatedByStage,0,allocatedByStage.Length);previousAllocationCounter=threadAllocationCounter==null?-1:threadAllocationCounter();if(pooledPayloadField!=null) previousPooledPayload=(long)pooledPayloadField.GetValue(null);if(pooledArraysField!=null) previousPooledArrays=(long)pooledArraysField.GetValue(null);return;}
            var body=bodyField.GetValue(adapter) as CharacterBody;if(body==null) return;
            if(body!=cachedBody) {cachedBody=body;cachedRigidbody=body.GetComponent<Rigidbody>();}
            if(sceneCamera==null) foreach(var rig in CameraRigController.readOnlyInstancesList) if(rig.localUserViewer==LocalUserManager.GetFirstLocalUser()) {sceneCamera=rig.sceneCam;break;}
            var position=(Vector3)positionField.GetValue(adapter);var camera=sceneCamera==null?position:sceneCamera.transform.position;
            byte[] mc=mcField.GetValue(adapter) as byte[];int mcFrame=mc==null?-1:BitConverter.ToInt32(mc,0x38),teleport=(int)teleportField.GetValue(adapter);
            if(!previousActive) {previousPosition=position;previousCamera=camera;lastMc=mcFrame;lastTeleport=teleport;previousActive=true;}
            frameTimes[frameCount++%frameTimes.Length]=Time.unscaledDeltaTime*1000;
            if(Time.frameCount-contactFrame>=15) {contacts=Physics.OverlapCapsuleNonAlloc(position+Vector3.up*.3f,position+Vector3.up*1.5f,.31f,nearby,LayerIndex.world.mask|1,QueryTriggerInteraction.Ignore);contactFrame=Time.frameCount;}
            var delta=position-previousPosition;var cameraDelta=camera-previousCamera;var motor=body.characterMotor;
            var renderer=rendererField.GetValue(adapter);int sections=renderer==null?0:(int)sectionCount.GetValue(renderer,null);
            var sample=new Sample {frame=Time.frameCount,time=Time.unscaledTime,dt=Time.unscaledDeltaTime,cap=Application.targetFrameRate,vsync=QualitySettings.vSyncCount,
                fixedTicks=calls[1],updates=calls[0],mcFrame=mcFrame,mcFresh=mcFrame!=lastMc?1:0,teleports=teleport-lastTeleport,contacts=contacts,grounded=mc!=null && (BitConverter.ToInt32(mc,4)&4)!=0?1:0,
                motor=motor!=null && motor.enabled?1:0,kinematic=motor!=null && motor.Motor!=null && motor.Motor.enabled?1:0,skill=(bool)skillField.GetValue(adapter)?1:0,sections=sections,
                px=position.x,py=position.y,pz=position.z,cx=camera.x,cy=camera.y,cz=camera.z,dx=delta.x,dy=delta.y,dz=delta.z,camDx=cameraDelta.x,camDy=cameraDelta.y,camDz=cameraDelta.z,
                bx=body.footPosition.x,by=body.footPosition.y,bz=body.footPosition.z,fixedStep=Time.fixedDeltaTime,rbInterpolation=cachedRigidbody==null?-1:(int)cachedRigidbody.interpolation,
                moveX=body.inputBank==null?0:body.inputBank.moveVector.x,moveZ=body.inputBank==null?0:body.inputBank.moveVector.z,cameraYaw=sceneCamera==null?0:sceneCamera.transform.eulerAngles.y,cameraPitch=sceneCamera==null?0:sceneCamera.transform.eulerAngles.x,
                inputX=Input.GetAxisRaw("Mouse X"),inputY=Input.GetAxisRaw("Mouse Y"),allocated=allocations.Valid?allocations.LastValue:-1,gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2),
                updateMs=ms[0],cameraMs=ms[2],floorMs=ms[3],actorsMs=ms[4],inventoryMs=ms[5],renderMs=ms[6],overlayMs=ms[7],
                updateBytes=-1,cameraBytes=-1,floorBytes=-1,actorsBytes=-1,inventoryBytes=-1,renderBytes=-1,overlayBytes=-1,
                copyPayloadBytes=copyPayloadBytes,copyReadCalls=copyReadCalls,mcFlags=mc==null?0:BitConverter.ToInt32(mc,4),
                playerReadBytes=playerReadBytes,sceneReadBytes=sceneReadBytes,textureReadBytes=textureReadBytes,stateReadBytes=stateReadBytes,otherReadBytes=otherReadBytes};
            sample.engineCpuMs=sample.engineGpuMs=-1;
            FrameTimingManager.CaptureFrameTimings();
            if(FrameTimingManager.GetLatestTimings(1,engineTiming)>0) {
                if(engineTiming[0].cpuFrameTime>0) sample.engineCpuMs=engineTiming[0].cpuFrameTime;
                if(engineTiming[0].gpuFrameTime>0) sample.engineGpuMs=engineTiming[0].gpuFrameTime;
            }
            if(pooledPayloadField!=null && pooledArraysField!=null) {
                long payload=(long)pooledPayloadField.GetValue(null),arrays=(long)pooledArraysField.GetValue(null);
                sample.pooledNewPayloadBytes=Math.Max(0,payload-previousPooledPayload);sample.copyPayloadBytes+=sample.pooledNewPayloadBytes;
                sample.copyReadCalls+=(int)Math.Max(0,arrays-previousPooledArrays);
                previousPooledPayload=payload;previousPooledArrays=arrays;
            }
            if(threadAllocationCounter!=null) {
                long current=threadAllocationCounter();sample.allocated=previousAllocationCounter<0?-1:Math.Max(0,current-previousAllocationCounter);previousAllocationCounter=current;
                sample.updateBytes=allocatedByStage[0];sample.cameraBytes=allocatedByStage[2];sample.floorBytes=allocatedByStage[3];sample.actorsBytes=allocatedByStage[4];sample.inventoryBytes=allocatedByStage[5];sample.renderBytes=allocatedByStage[6];sample.overlayBytes=allocatedByStage[7];
            }
            if(mc!=null && mc.Length>=0xC8) {
                if(sample.teleports>0 && sample.skill==0) {
                    var origin=(Vector3)originField.GetValue(adapter);
                    sample.correctionX=position.x-(origin.x-(float)BitConverter.ToDouble(mc,8));
                    sample.correctionY=position.y-(origin.y+(float)BitConverter.ToDouble(mc,16));
                    sample.correctionZ=position.z-(origin.z+(float)BitConverter.ToDouble(mc,24));
                }
                long now;sample.tickQpc=BitConverter.ToInt64(mc,0x68);sample.tickMs=BitConverter.ToSingle(mc,0xB8);
                if(qpcFrequency>0 && QueryPerformanceCounter(out now)) sample.tickAgeMs=(float)((now-sample.tickQpc)*1000.0/qpcFrequency);
                sample.prevX=BitConverter.ToDouble(mc,0x70);sample.prevY=BitConverter.ToDouble(mc,0x78);sample.prevZ=BitConverter.ToDouble(mc,0x80);
                sample.curX=BitConverter.ToDouble(mc,0x88);sample.curY=BitConverter.ToDouble(mc,0x90);sample.curZ=BitConverter.ToDouble(mc,0x98);
            }
            if(Time.unscaledTime>=nextOverlay) {
                int n=Math.Min(frameCount,frameTimes.Length);Array.Copy(frameTimes,sortedTimes,n);Array.Sort(sortedTimes,0,n);float sum=0;for(int i=0;i<n;i++) sum+=frameTimes[i];
                float p99=n==0?0:sortedTimes[Math.Min(n-1,(int)(n*.99))];
                overlay=string.Format(CultureInfo.InvariantCulture,"RoRCraft diagnostic capture — F10 off | F11 cap | F12 save\nFPS {0:0} | frame {1:0.00} ms | p99 {2:0.00} ms (~1% low {3:0} FPS) | cap {4}, VSync {5}\nBridge {6} | camera {7} | speed {8:0.00} m/s | grounded {9}\nFixed ticks/frame {10} (~{11:0} Hz) | Update calls {12} | fresh MC {13} | corrections {14}\nNative motor {15}/{16} | skill motor {17} | nearby colliders {18} (overlap query, not actual contacts) | sections {19}\nRoRCraft update {20:0.00} ms: floor {21:0.00}, actors {22:0.00}, inventory {23:0.00}, render {24:0.00}, HUD {25:0.00}\nGC bytes/frame (source in startup log) {26} | collections {27}/{28}/{29}",sum>0?n*1000/sum:0,sample.dt*1000,p99,p99>0?1000/p99:0,sample.cap,sample.vsync,position,camera,delta.magnitude/Mathf.Max(sample.dt,.0001f),sample.grounded,sample.fixedTicks,sample.fixedTicks/Mathf.Max(sample.dt,.0001f),sample.updates,sample.mcFresh,sample.teleports,sample.motor,sample.kinematic,sample.skill,sample.contacts,sample.sections,sample.updateMs,sample.floorMs,sample.actorsMs,sample.inventoryMs,sample.renderMs,sample.overlayMs,sample.allocated,sample.gc0,sample.gc1,sample.gc2);
                nextOverlay=Time.unscaledTime+.25f;
            }
            sample.diagnosticMs=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
            samples[count++]=sample;previousPosition=position;previousCamera=camera;lastMc=mcFrame;lastTeleport=teleport;
            if(sample.dt>.05f && Time.unscaledTime>=nextSpike) {Logger.LogWarning(string.Format(CultureInfo.InvariantCulture,"Frame spike {0:0.0}ms; RoRCraft {1:0.0}ms, floor {2:0.0}, render {3:0.0}, HUD {4:0.0}, GC {5}, MC fresh {6}, corrections {7}",sample.dt*1000,sample.updateMs,sample.floorMs,sample.renderMs,sample.overlayMs,sample.allocated,sample.mcFresh,sample.teleports));nextSpike=Time.unscaledTime+1;}
            Array.Clear(ms,0,ms.Length);Array.Clear(calls,0,calls.Length);
            Array.Clear(allocatedByStage,0,allocatedByStage.Length);
            copyPayloadBytes=0;copyReadCalls=0;
            playerReadBytes=sceneReadBytes=textureReadBytes=stateReadBytes=otherReadBytes=0;
            if(count>=samples.Length) Save();
        }
        private void Save() {
            if(count==0 || samples==null) return;int length=count;var saved=new Sample[length];Array.Copy(samples,saved,length);count=0;
            string path=System.IO.Path.Combine(outputFolder.Value,"movement-"+session+"-"+DateTime.Now.ToString("HHmmssfff",CultureInfo.InvariantCulture)+".csv");
            pendingWrites.RemoveAll(delegate(Task task) {return task.IsCompleted;});
            pendingWrites.Add(Task.Run(delegate {
                try {Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));var fields=typeof(Sample).GetFields(BindingFlags.Public|BindingFlags.Instance);
                    using(var file=new StreamWriter(path+".partial",false,Encoding.UTF8)) {for(int j=0;j<fields.Length;j++) {if(j>0) file.Write(',');file.Write(fields[j].Name);}file.WriteLine();
                        for(int i=0;i<length;i++) {object boxed=saved[i];for(int j=0;j<fields.Length;j++) {if(j>0) file.Write(',');file.Write(Convert.ToString(fields[j].GetValue(boxed),CultureInfo.InvariantCulture));}file.WriteLine();}}
                    File.Move(path+".partial",path);
                    Logger.LogInfo("Movement CSV saved: "+path+" ("+length+" frames)");
                } catch(Exception e) {Logger.LogError("Cannot save movement capture: "+e);}
            }));
        }
        public void SaveSegment() {Save();}
        private void OnGUI() {if(enabledCapture!=null && enabledCapture.Value && previousActive) GUI.Box(new Rect(12,12,Math.Min(Screen.width-24,1150),195),overlay);}
        private void OnDestroy() {Save();if(pendingWrites.Count>0) Task.WaitAll(pendingWrites.ToArray(),3000);if(enabledCapture!=null && enabledCapture.Value) allocations.Dispose();Application.targetFrameRate=originalCap;QualitySettings.vSyncCount=originalVsync;if(harmony!=null) harmony.UnpatchSelf();instance=null;}
    }
}

