using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Networking;
using RoRCraft;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoRCraftPolish {
    [BepInPlugin("cz.jirka.rorcraft-polish-harness","RoRCraft developer movement test","0.1.0")]
    [BepInDependency("cz.jirka.rorcraft-skycraft",BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("cz.jirka.rorcraft-polish-diagnostics",BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("cz.jirka.rorcraft-mashup-settings",BepInDependency.DependencyFlags.HardDependency)]
    [DefaultExecutionOrder(9000)]
    public sealed class PolishTestHarness : BaseUnityPlugin {
        private static PolishTestHarness instance;
        private ConfigEntry<bool> autoStart;
        private ConfigEntry<int> seconds;
        private ConfigEntry<string> folder;
        private ConfigEntry<bool> quitOnFinish;
        private ConfigEntry<ulong> testSeed;
        private ConfigEntry<bool> freezeSpawns,materialReferences,worldShaderReferences;
        private ConfigEntry<int> firstSurface,surfaceCount;
        private readonly Dictionary<CombatDirector,bool> directorStates=new Dictionary<CombatDirector,bool>();
        private readonly List<GameObject> pausedMonsters=new List<GameObject>();
        private Harmony harmony;
        private SkyMemory link;
        private object adapter;
        private FieldInfo activeField,bodyField,positionField,originField,teleportField;
        private MovementDiagnostics diagnostic;
        private CharacterBody testBody;
        private bool originalGodMode,originalBackground,startedHost,startedRun,running,caseStarted,fixtureSpawned;
        private int originalCap,originalVsync,index=-1,token;
        private float startup,runRequested,warmup,caseStart,caseElapsed;
        private float scriptedPath;
        private Vector3 lastPhysics;
        private double originX,originY,originZ;
        private float nextWaitStatus,lastMcStateTime;
        private readonly int[] caps={60,120,144,165,-1};
        private string events;
        private bool Testing {get {return autoStart!=null && autoStart.Value;}}
        private void Awake() {
            instance=this;startup=Time.unscaledTime;originalCap=Application.targetFrameRate;originalVsync=QualitySettings.vSyncCount;originalBackground=Application.runInBackground;
            autoStart=Config.Bind("Developer","AutoStart",false,"Developer-only local run and scripted Minecraft input. F9 aborts. Always disabled at completion.");
            seconds=Config.Bind("Developer","SecondsPerCap",70,"Seconds per FPS cap on each surface. 70 gives five minutes of scripted walking plus five 10-second camera rotations per surface; shorter runs are partial tests.");
            folder=Config.Bind("Developer","OutputFolder",Paths.PluginPath,"Test event output directory.");
            quitOnFinish=Config.Bind("Developer","QuitOnFinish",false,"Normally quit the developer-created local run after testing. Minecraft saves and exits through quitWithSkyrim. Does not take over an existing run.");
            testSeed=Config.Bind("Developer","TestSeed",20261005UL,"Fixed local developer run seed for reproducible map/spawn comparisons.");
            freezeSpawns=Config.Bind("Developer","FreezeCombatSpawns",false,"Disable further combat director spawns after warmup, only in the developer-created run. Restored on stop. Focused movement benchmark, not combat stress validation.");
            materialReferences=Config.Bind("Developer","MaterialReferences",false,"Compare only the matte material native ramps/layers. Disabled in normal play.");
            worldShaderReferences=Config.Bind("Developer","WorldShaderReferences",false,"Native versus embedded Minecraft shader and F5 image captures in an isolated developer world.");
            firstSurface=Config.Bind("Developer","FirstSurface",0,"0 native terrain, 1 flat Minecraft platform, 2 terrain/slab/block transitions.");
            surfaceCount=Config.Bind("Developer","SurfaceCount",2,"Number of consecutive developer surfaces to test.");
            if(firstSurface.Value<0 || surfaceCount.Value<1 || firstSurface.Value+surfaceCount.Value>3) throw new InvalidOperationException("Invalid developer surface range.");
            index=firstSurface.Value*5-1;
            if(!Testing) return;
            SetBackgroundCursorTest(true);
            var type=AccessTools.TypeByName("RoRCraft.RoRCraftPlugin");adapter=UnityEngine.Object.FindObjectOfType(type);
            activeField=AccessTools.Field(type,"active");bodyField=AccessTools.Field(type,"controlled");link=(SkyMemory)AccessTools.Field(type,"link").GetValue(adapter);
            positionField=AccessTools.Field(type,"position");originField=AccessTools.Field(type,"worldOrigin");teleportField=AccessTools.Field(type,"teleport");
            diagnostic=UnityEngine.Object.FindObjectOfType<MovementDiagnostics>();
            if(adapter==null || link==null || diagnostic==null) {Logger.LogError("Test dependencies unavailable.");Stop("dependency failure");return;}
            harmony=new Harmony("cz.jirka.rorcraft-polish-harness");
            harmony.Patch(AccessTools.Method(type,"Update"),transpiler:new HarmonyMethod(typeof(PolishTestHarness),"FocusForTest"));
            harmony.Patch(AccessTools.Method(type,"InputToMinecraft"),prefix:new HarmonyMethod(typeof(PolishTestHarness),"ScriptLook"));
            harmony.Patch(AccessTools.Method(type,"UpdateSkills"),prefix:new HarmonyMethod(typeof(PolishTestHarness),"ScriptMove"));
            Application.runInBackground=true;
            Directory.CreateDirectory(folder.Value);events=System.IO.Path.Combine(folder.Value,"automated-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-events.csv");
            File.WriteAllText(events,"event,scenario,cap,host_time,duration\n");
            Logger.LogInfo("Developer movement run requested. F9 aborts; native terrain and flat Minecraft platform, five caps each.");
        }
        public static bool AcceptFocus() {return Application.isFocused || instance!=null && instance.Testing;}
        private static void SetBackgroundCursorTest(bool enabled) {
            var type=AccessTools.TypeByName("RoRCraftPolish.CursorSafety");
            if(type==null && enabled) throw new InvalidOperationException("Background cursor protection missing; refusing automatic test.");
            if(type!=null) AccessTools.Field(type,"BackgroundTest").SetValue(null,enabled);
        }
        private static IEnumerable<CodeInstruction> FocusForTest(IEnumerable<CodeInstruction> instructions) {
            var original=AccessTools.PropertyGetter(typeof(Application),"isFocused");var replacement=AccessTools.Method(typeof(PolishTestHarness),"AcceptFocus");
            foreach(var instruction in instructions) {if(instruction.Calls(original)) {instruction.opcode=OpCodes.Call;instruction.operand=replacement;}yield return instruction;}
        }
        private static bool ScriptLook(ref float ___yaw,ref float ___pitch) {
            if(instance==null || !instance.Testing) return true;
            float turn=Math.Max(0,instance.caseElapsed-Math.Max(0,instance.seconds.Value-10));
            ___yaw=instance.materialReferences.Value || instance.worldShaderReferences.Value?0:turn>0?turn*36:0;___pitch=instance.materialReferences.Value || instance.worldShaderReferences.Value?5:15;return false;
        }
        private static bool ScriptMove(CharacterBody ___controlled) {
            if(instance==null || !instance.Testing) return true;
            if(___controlled==null || ___controlled.inputBank==null) return false;
            bool moving=instance.caseStarted && instance.caseElapsed<Math.Max(1,instance.seconds.Value-10);
            float direction=instance.link.F(0x9F8);
            ___controlled.inputBank.moveVector=moving?Vector3.forward*direction:Vector3.zero;
            return false;
        }
        private void Update() {
            if(!Testing) return;
            if(Input.GetKeyDown(KeyCode.F9)) {Stop("aborted by player");return;}
            try {
                if(Time.unscaledTime-startup>180 && !running) {Stop("startup timeout");return;}
                if(!startedHost) {
                    if(NetworkManagerSystem.singleton==null || LocalUserManager.GetFirstLocalUser()==null || Time.unscaledTime-startup<4) return;
                    if(Run.instance!=null) {Stop("existing run; refusing automatic takeover");return;}
                    NetworkManagerSystem.singleton.desiredHost=new HostDescription(new HostDescription.HostingParameters {listen=false,maxPlayers=1});
                    startedHost=true;Logger.LogInfo("Developer local host requested.");return;
                }
                if(!startedRun) {
                    var user=LocalUserManager.GetFirstLocalUser();
                    if(PreGameController.instance==null || user==null || user.currentNetworkUser==null) return;
                    if(runRequested==0) {runRequested=Time.unscaledTime;return;}
                    if(Time.unscaledTime-runRequested<3) return;
                    AccessTools.Field(typeof(PreGameController),"runSeed").SetValue(PreGameController.instance,testSeed.Value);
                    AccessTools.Method(typeof(PreGameController),"StartRun").Invoke(PreGameController.instance,null);startedRun=true;Logger.LogInfo("Developer run started through normal pregame method.");return;
                }
                bool active=(bool)activeField.GetValue(adapter);
                var body=bodyField.GetValue(adapter) as CharacterBody;
                var localUser=LocalUserManager.GetFirstLocalUser();
                var nativeBody=localUser==null || localUser.currentNetworkUser==null?null:localUser.currentNetworkUser.GetCurrentBody();
                if(nativeBody!=null && nativeBody.healthComponent!=null && nativeBody!=testBody) {
                    testBody=nativeBody;originalGodMode=nativeBody.healthComponent.godMode;nativeBody.healthComponent.godMode=true;
                }
                if(!active && nativeBody!=null && nativeBody.currentVehicle!=null && Time.unscaledTime-runRequested>12) {
                    nativeBody.currentVehicle.EjectPassenger();Logger.LogInfo("Developer run exited its spawn pod through VehicleSeat.EjectPassenger.");
                }
                if(!active && Time.unscaledTime>=nextWaitStatus) {
                    var waiting=link.McState();
                    Logger.LogInfo("Developer waiting: body="+(nativeBody==null?"none":nativeBody.name)+", vehicle="+(nativeBody!=null && nativeBody.currentVehicle!=null)+", MC flags="+(waiting==null?0:BitConverter.ToInt32(waiting,4))+", reset="+link.I(0x9B0)+"/"+link.I(0x940));
                    nextWaitStatus=Time.unscaledTime+15;
                }
                if(body!=null && body.healthComponent!=null && body!=testBody) {testBody=body;originalGodMode=body.healthComponent.godMode;body.healthComponent.godMode=true;}
                if(!active || body==null) {if(running && Time.unscaledTime-warmup>30) Stop("integration lost during test");return;}
                var state=link.McState();
                // A missed publication is normal at high host frame rates.
                // Never decode a missing snapshot or call it player death.
                if(state==null) {if(running && Time.unscaledTime-lastMcStateTime>10) Stop("Minecraft snapshot timeout");return;}
                lastMcStateTime=Time.unscaledTime;
                if(running && state!=null && (BitConverter.ToInt32(state,4)&32)!=0) {Stop("Minecraft player died; movement cases invalidated");return;}
                if(!running) {
                    if(warmup==0) {warmup=Time.unscaledTime;return;}
                    if(Time.unscaledTime-warmup<8) return;
                    var pose=link.McState();originX=BitConverter.ToDouble(pose,8);originY=BitConverter.ToDouble(pose,16);originZ=BitConverter.ToDouble(pose,24);
                    AuditColliders(body);
                    if(freezeSpawns.Value) foreach(var director in UnityEngine.Object.FindObjectsOfType<CombatDirector>()) {
                        directorStates[director]=director.enabled;director.enabled=false;
                    }
                    if(freezeSpawns.Value) {
                        foreach(var enemy in CharacterBody.readOnlyInstancesList) if(enemy!=null && enemy.teamComponent!=null && enemy.teamComponent.teamIndex==TeamIndex.Monster && enemy.gameObject.activeSelf) pausedMonsters.Add(enemy.gameObject);
                        foreach(var enemy in pausedMonsters) enemy.SetActive(false);
                    }
                    Logger.LogInfo("Developer benchmark seed "+Run.instance.seed+"; further combat spawns frozen "+freezeSpawns.Value);
                    running=true;NextCase();return;
                }
                if(!caseStarted) {
                    if(link.I(0x9D8)!=token) {if(Time.unscaledTime-warmup>30) Stop("Minecraft fixture timeout");return;}
                    if(!fixtureSpawned) {
                        // Fixtures are spawns, not a 6m fall through the native
                        // floor guard. Use the adapter's existing hard-teleport
                        // protocol and update both authoritative host positions.
                        var origin=(Vector3)originField.GetValue(adapter);
                        var feet=new Vector3(origin.x-(float)link.D(0xA00),origin.y+(float)link.D(0xA08),origin.z+(float)link.D(0xA10));
                        positionField.SetValue(adapter,feet);
                        var target=body.transform.position+feet-body.footPosition;
                        if(body.characterMotor!=null && body.characterMotor.Motor!=null) body.characterMotor.Motor.SetPosition(target);
                        else body.transform.position=target;
                        int seq=(int)teleportField.GetValue(adapter)+1;teleportField.SetValue(adapter,seq);link.I(0x928,seq);
                        fixtureSpawned=true;
                    }
                    if(caseStart==0) {caseStart=Time.unscaledTime;return;}
                    if(Time.unscaledTime-caseStart<3) return;
                    if(materialReferences.Value) {var materialType=AccessTools.TypeByName("RoRCraftPolish.MaterialPolish");AccessTools.Method(materialType,"DeveloperReference").Invoke(null,new object[]{index%5});}
                    CaptureWorld(System.IO.Path.Combine(folder.Value,"scenario-"+(index/5)+"-cap-"+caps[index%5]+".png"));
                    diagnostic.SaveSegment();caseStart=Time.unscaledTime;caseElapsed=0;caseStarted=true;
                    lastPhysics=new Vector3((float)BitConverter.ToDouble(state,0x88),(float)BitConverter.ToDouble(state,0x90),(float)BitConverter.ToDouble(state,0x98));scriptedPath=0;
                    Event("begin");Logger.LogInfo("Movement test case "+index+" surface "+(index/5)+" FPS cap "+caps[index%5]);
                }
                caseElapsed=Time.unscaledTime-caseStart;link.F(0x9DC,caseElapsed);
                var physics=new Vector3((float)BitConverter.ToDouble(state,0x88),(float)BitConverter.ToDouble(state,0x90),(float)BitConverter.ToDouble(state,0x98));
                scriptedPath+=new Vector2(physics.x-lastPhysics.x,physics.z-lastPhysics.z).magnitude;lastPhysics=physics;
                if(caseElapsed>5 && caseElapsed<seconds.Value-10 && scriptedPath<.25f) {Stop("scripted fixture blocked; invalid movement case");return;}
                if(physics.y<originY-24) {Stop("fell below developer fixture; invalid collision case");return;}
                if(caseElapsed>=seconds.Value) {
                    Event("end");diagnostic.SaveSegment();
                    if(index==(firstSurface.Value+surfaceCount.Value)*5-1) {if(index/5==2) AuditMaterials();Stop("completed captures; analysis and visual validation required");return;}
                    NextCase();
                }
            } catch(Exception e) {Logger.LogError(e);Stop("test exception");}
        }
        private void NextCase() {
            index++;token++;caseStarted=false;fixtureSpawned=false;caseStart=0;caseElapsed=-3;warmup=Time.unscaledTime;
            QualitySettings.vSyncCount=0;Application.targetFrameRate=caps[index%5];
            if(worldShaderReferences.Value) {
                var materialType=AccessTools.TypeByName("RoRCraftPolish.MaterialPolish");
                AccessTools.Method(materialType,"DeveloperBlockShaderReference").Invoke(null,new object[]{index%5==0});
                // Cycle Minecraft's actual perspective through its normal input
                // bridge: first person, then rear F5, then front F5, then first.
                if(index%5>=2) {link.Input(1,62,1,0,0);link.Input(1,62,0,0,0);}
            }
            link.I(0x9D0,0);link.D(0x9E0,originX);link.D(0x9E8,originY);link.D(0x9F0,originZ);
            link.I(0x9D4,index/5);link.I(0x9FC,token);link.F(0x9DC,-3);System.Threading.Thread.MemoryBarrier();link.I(0x9D0,0x31545354);
        }
        private void CaptureWorld(string path) {
            if(!Application.isBatchMode) {ScreenCapture.CaptureScreenshot(path);return;}
            var rig=AccessTools.Field(adapter.GetType(),"rig").GetValue(adapter) as CameraRigController;
            var camera=rig==null?Camera.main:rig.sceneCam;
            if(camera==null) throw new InvalidOperationException("Native scene camera unavailable for headless capture");
            var target=new RenderTexture(1920,1080,24,RenderTextureFormat.DefaultHDR);target.Create();
            var display=new RenderTexture(1920,1080,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);display.Create();
            var pixels=new Texture2D(1920,1080,TextureFormat.RGBA32,false);var activeTarget=RenderTexture.active;bool oldSrgb=GL.sRGBWrite;
            // Never redirect the live scene camera: its native scaler and
            // command buffers retain render targets across frames. Own every
            // target and callback in the offscreen capture instead.
            var copyObject=new GameObject("RoRCraft developer stage capture");var copy=copyObject.AddComponent<Camera>();
            var cleanTarget=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);cleanTarget.Create();
            var cleanPixels=new Texture2D(1920,1080,TextureFormat.RGBA32,false);
            UnityEngine.Rendering.PostProcessing.PostProcessLayer layer=null;
            try {
                copy.CopyFrom(camera);copy.enabled=false;copy.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation);
                copy.targetTexture=cleanTarget;copy.allowHDR=false;copy.rect=new Rect(0,0,1,1);copy.aspect=1920f/1080;copy.ResetProjectionMatrix();copy.ResetCullingMatrix();
                copy.Render();RenderTexture.active=cleanTarget;cleanPixels.ReadPixels(new Rect(0,0,1920,1080),0,0,false);cleanPixels.Apply();
                File.WriteAllBytes(path+".geometry.png",cleanPixels.EncodeToPNG());
                var originalLayer=camera.GetComponent<UnityEngine.Rendering.PostProcessing.PostProcessLayer>();
                if(originalLayer==null) throw new InvalidOperationException("Stage postprocessing layer unavailable");
                copyObject.SetActive(false);layer=copyObject.AddComponent<UnityEngine.Rendering.PostProcessing.PostProcessLayer>();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(originalLayer),layer);layer.volumeTrigger=copy.transform;
                var resources=(UnityEngine.Rendering.PostProcessing.PostProcessResources)AccessTools.Field(originalLayer.GetType(),"m_Resources").GetValue(originalLayer);
                if(resources==null) throw new InvalidOperationException("Native postprocess resources unavailable");
                layer.Init(resources);copy.targetTexture=target;copy.allowHDR=camera.allowHDR;copyObject.SetActive(true);layer.ResetHistory();
                copy.Render();GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;Graphics.Blit(target,display);RenderTexture.active=display;
                pixels.ReadPixels(new Rect(0,0,1920,1080),0,0,false);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());
                var colors=new HashSet<int>();var values=pixels.GetPixels32();for(int i=0;i<values.Length;i+=8) colors.Add((values[i].r<<16)|(values[i].g<<8)|values[i].b);
                if(colors.Count<256) throw new InvalidOperationException("Stage capture lacks textured positive control; distinct colors="+colors.Count);
                Logger.LogInfo("Owned stage camera path="+copy.actualRenderingPath+" color space="+QualitySettings.activeColorSpace+" textured colors="+colors.Count+" case="+index%5);
            } finally {
                if(layer!=null) layer.enabled=false;copy.targetTexture=null;copyObject.SetActive(false);GL.sRGBWrite=oldSrgb;RenderTexture.active=activeTarget;
                cleanTarget.Release();target.Release();display.Release();UnityEngine.Object.Destroy(copyObject);UnityEngine.Object.Destroy(cleanTarget);UnityEngine.Object.Destroy(cleanPixels);UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(display);UnityEngine.Object.Destroy(pixels);
            }
            var state=link.McState();
            int perspective=state==null?-1:BitConverter.ToInt32(state,0xC0);
            if(worldShaderReferences.Value && perspective!=(index%5==2?1:index%5==3?2:0)) throw new InvalidOperationException("F5 perspective did not reach the requested native MC state: "+perspective);
            File.WriteAllText(path+".scope.txt","Owned camera on actual stage, native postprocess volume/resources; HUD, Sobel/vision callbacks excluded. MC perspective="+perspective+"; atlas="+AccessTools.Method(AccessTools.TypeByName("RoRCraftPolish.MaterialPolish"),"DeveloperAtlasAudit").Invoke(null,null));
        }
        private void Event(string kind) {if(events!=null) File.AppendAllText(events,string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0},{1},{2},{3},{4}\n",kind,index/5,index<0?originalCap:caps[index%5],Time.unscaledTime,caseElapsed));}
        private void AuditColliders(CharacterBody body) {
            string path=System.IO.Path.Combine(folder.Value,"nearby-collider-audit.txt");
            using(var file=new StreamWriter(path)) {
                file.WriteLine("Developer audit at "+body.footPosition+"; colliders within 24 metres. Captured before timed cases.");
                foreach(var collider in UnityEngine.Object.FindObjectsOfType<Collider>()) {
                    if(Vector3.Distance(collider.bounds.ClosestPoint(body.footPosition),body.footPosition)>24) continue;
                    file.Write(collider.GetType().Name+" name="+collider.name+" layer="+collider.gameObject.layer+" trigger="+collider.isTrigger+" enabled="+collider.enabled+" static="+collider.gameObject.isStatic+" bounds="+collider.bounds+" scale="+collider.transform.lossyScale);
                    var meshCollider=collider as MeshCollider;
                    if(meshCollider!=null && meshCollider.sharedMesh!=null) file.Write(" mesh="+meshCollider.sharedMesh.name+" vertices="+meshCollider.sharedMesh.vertexCount+" readable="+meshCollider.sharedMesh.isReadable+" convex="+meshCollider.convex);
                    file.WriteLine();
                }
            }
        }
        private void AuditMaterials() {
            string path=System.IO.Path.Combine(folder.Value,"material-audit-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt");
            using(var file=new StreamWriter(path)) {
                file.WriteLine("Read-only material audit after developer terrain/block transition cases.");
                file.WriteLine("Scene="+UnityEngine.SceneManagement.SceneManager.GetActiveScene().name+" seed="+Run.instance.seed);
                file.WriteLine("Ambient mode="+RenderSettings.ambientMode+" intensity="+RenderSettings.ambientIntensity+" color="+RenderSettings.ambientLight+" sky="+RenderSettings.ambientSkyColor+" ground="+RenderSettings.ambientGroundColor);
                file.WriteLine("Native HG shader currently available="+Shader.Find("Hopoo Games/Deferred/Standard"));
                var shaderNames=new HashSet<string>();
                foreach(var native in UnityEngine.Object.FindObjectsOfType<MeshRenderer>()) {
                    if(native.name.StartsWith("RoRCraft")) continue;
                    foreach(var nativeMaterial in native.sharedMaterials) {
                        if(nativeMaterial==null || nativeMaterial.shader==null || !shaderNames.Add(nativeMaterial.shader.name)) continue;
                        file.WriteLine("Native shader="+nativeMaterial.shader.name+" example="+native.name);
                        for(int i=0;i<nativeMaterial.shader.GetPropertyCount();i++) file.WriteLine("  property="+nativeMaterial.shader.GetPropertyName(i));
                    }
                }
                var renderer=AccessTools.Field(adapter.GetType(),"renderer").GetValue(adapter);
                var materials=(Dictionary<int,Material>)AccessTools.Field(renderer.GetType(),"materials").GetValue(renderer);
                foreach(var entry in materials) {
                    var material=entry.Value;var shader=material.shader;
                    file.WriteLine("\nMinecraft material key="+entry.Key+" shader="+shader.name+" queue="+material.renderQueue+" keywords="+string.Join(",",material.shaderKeywords));
                    var texture=material.mainTexture as Texture2D;
                    if(texture!=null && entry.Key==0) File.WriteAllBytes(System.IO.Path.Combine(folder.Value,"minecraft-atlas-reference.png"),texture.EncodeToPNG());
                    if(texture!=null) file.WriteLine("Texture="+texture.name+" dimensions="+texture.width+"x"+texture.height+" format="+texture.format+" mipmaps="+texture.mipmapCount+" filtering="+texture.filterMode+" anisotropy="+texture.anisoLevel+" wrap="+texture.wrapMode+" mipBias="+texture.mipMapBias);
                    for(int i=0;i<shader.GetPropertyCount();i++) {
                        string name=shader.GetPropertyName(i);var type=shader.GetPropertyType(i);
                        if(type==ShaderPropertyType.Float || type==ShaderPropertyType.Range) file.WriteLine(name+"="+material.GetFloat(name));
                        else if(type==ShaderPropertyType.Color) file.WriteLine(name+"="+material.GetColor(name));
                        else if(type==ShaderPropertyType.Texture) file.WriteLine(name+"="+material.GetTexture(name));
                    }
                }
                var seenCategories=new HashSet<Material>();
                foreach(var owned in UnityEngine.Object.FindObjectsOfType<MeshRenderer>()) {
                    if(!owned.name.StartsWith("RoRCraft section")) continue;
                    foreach(var category in owned.sharedMaterials) {
                        if(category==null || !category.name.StartsWith("Minecraft surface category") || !seenCategories.Add(category)) continue;
                        file.WriteLine("CATEGORY "+category.name+" shader="+category.shader.name+" queue="+category.renderQueue+" texture="+category.mainTexture+" keywords="+string.Join(",",category.shaderKeywords));
                        foreach(string prop in new[]{"_Glossiness","_Smoothness","_SpecularStrength","_Metallic","_EmPower","_ZWrite","_SrcBlend","_DstBlend"}) if(category.HasProperty(prop)) file.WriteLine("  "+prop+"="+category.GetFloat(prop));
                    }
                }
                var auditType=AccessTools.TypeByName("RoRCraftPolish.MaterialPolish");
                var atlasAudit=auditType==null?null:AccessTools.Method(auditType,"DeveloperAtlasAudit");
                if(atlasAudit!=null) file.WriteLine(atlasAudit.Invoke(null,null));
                foreach(var light in UnityEngine.Object.FindObjectsOfType<Light>()) if(light.isActiveAndEnabled && light.type==LightType.Directional)
                    file.WriteLine("Directional light="+light.name+" color="+light.color+" intensity="+light.intensity+" shadows="+light.shadows+" shadowStrength="+light.shadowStrength);
            }
            Logger.LogInfo("Material audit saved: "+path);
        }
        private void Stop(string reason) {
            bool willQuit=quitOnFinish!=null && quitOnFinish.Value && startedHost && startedRun && reason!="aborted by player" && reason!="shutdown";
            SetBackgroundCursorTest(false);
            Logger.LogInfo("Developer test stopped: "+reason);Event("stop");
            if(diagnostic!=null) diagnostic.SaveSegment();if(link!=null) link.I(0x9D0,0);
            if(testBody!=null && testBody.healthComponent!=null) testBody.healthComponent.godMode=originalGodMode;
            if(!willQuit) {
                foreach(var entry in directorStates) if(entry.Key!=null) entry.Key.enabled=entry.Value;
                foreach(var enemy in pausedMonsters) if(enemy!=null) enemy.SetActive(true);
            }
            directorStates.Clear();pausedMonsters.Clear();
            Application.targetFrameRate=originalCap;QualitySettings.vSyncCount=originalVsync;Application.runInBackground=originalBackground;
            if(autoStart!=null) {autoStart.Value=false;Config.Save();}
            if(willQuit) StartCoroutine(QuitOwnedRun());
        }
        private System.Collections.IEnumerator QuitOwnedRun() {
            // Dispose this test's network/run objects through the normal host
            // lifecycle before terminating the Unity player. Never stop an
            // existing player-owned session (guarded by startedHost/startRun).
            Application.runInBackground=true;
            if(NetworkManagerSystem.singleton!=null) NetworkManagerSystem.singleton.StopHost();
            yield return new WaitForSecondsRealtime(.5f);
            Application.Quit();
        }
        private void OnDestroy() {if(Testing) Stop("shutdown");if(harmony!=null) harmony.UnpatchSelf();instance=null;}
    }
}
