using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Collections;
using RoR2;
using RoR2.Networking;
using UnityEngine;

namespace RoRCraftPolish {
    internal static class NativeLobbyProbe {
        private static List<string> synchronousChecks;
        private static int synchronousCount;
        private static void AfterRebuild(RoR2.SurvivorMannequins.SurvivorMannequinSlotController __instance) {
            if(synchronousChecks==null) return;
            var user=LocalUserManager.GetFirstLocalUser();
            if(user==null || __instance.networkUser!=user.currentNetworkUser) return;
            var field=HarmonyLib.AccessTools.Field(__instance.GetType(),"mannequinInstanceTransform");
            var display=field.GetValue(__instance) as Transform;
            if(display==null) return;
            var avatar=display.Find("RoRCraft lobby avatar");
            if(avatar==null) {synchronousChecks.Add("FAIL avatar missing when native rebuild returns");return;}
            foreach(var renderer in display.GetComponentsInChildren<Renderer>())
                if(!renderer.transform.IsChildOf(avatar) && renderer.enabled) {synchronousChecks.Add("FAIL native renderer visible when rebuild returns");return;}
            synchronousCount++;
        }
        internal static IEnumerator VerifyActualLobby(List<string> checks) {
            float until=Time.realtimeSinceStartup+40;
            while((NetworkManagerSystem.singleton==null || LocalUserManager.GetFirstLocalUser()==null) && Time.realtimeSinceStartup<until) yield return new WaitForSecondsRealtime(.25f);
            if(NetworkManagerSystem.singleton==null || LocalUserManager.GetFirstLocalUser()==null) {checks.Add("FAIL actual lobby local user unavailable");yield break;}
            if(RoR2.Run.instance!=null) {checks.Add("FAIL refuses to take over existing run");yield break;}
            NetworkManagerSystem.singleton.desiredHost=new HostDescription(new HostDescription.HostingParameters {listen=false,maxPlayers=1});
            until=Time.realtimeSinceStartup+60; // Fresh scene-load budget after host request.
            while(PreGameController.instance==null && Time.realtimeSinceStartup<until) yield return new WaitForSecondsRealtime(.25f);
            if(PreGameController.instance==null) {checks.Add("FAIL actual character-select scene unavailable");yield break;}
            yield return new WaitForSecondsRealtime(2);
            var local=LocalUserManager.GetFirstLocalUser();var user=local==null?null:local.currentNetworkUser;
            if(user==null) {checks.Add("FAIL actual lobby network user unavailable");yield break;}
            var original=user.GetSurvivorPreference();
            var displayField=typeof(RoR2.SurvivorMannequins.SurvivorMannequinSlotController).GetField("mannequinInstanceTransform",BindingFlags.Instance|BindingFlags.NonPublic);
            var timing=new HarmonyLib.Harmony("cz.jirka.rorcraft-lobby-timing-probe");
            synchronousChecks=checks;synchronousCount=0;
            var callback=new HarmonyLib.HarmonyMethod(typeof(NativeLobbyProbe),"AfterRebuild");callback.priority=HarmonyLib.Priority.Last;
            timing.Patch(HarmonyLib.AccessTools.Method(typeof(RoR2.SurvivorMannequins.SurvivorMannequinSlotController),"RebuildMannequinInstance"),postfix:callback);
            try {
                int variants=0,expectedRebuilds=0;
                foreach(var survivor in SurvivorCatalog.allSurvivorDefs) {
                    if(survivor==null || survivor.hidden || survivor.bodyPrefab==null) continue;
                    if(user.GetSurvivorPreference()!=survivor) expectedRebuilds++;
                    user.SetSurvivorPreferenceClient(survivor);
                    yield return new WaitForSecondsRealtime(2);
                    bool found=false;
                    foreach(var slot in UnityEngine.Object.FindObjectsOfType<RoR2.SurvivorMannequins.SurvivorMannequinSlotController>()) {
                        if(slot.networkUser!=user) continue;
                        var display=displayField.GetValue(slot) as Transform;
                        if(display==null) continue;
                        var avatar=display.Find("RoRCraft lobby avatar");if(avatar==null) continue;
                        if(Mathf.Abs(avatar.lossyScale.y*2-2)>.02f) {checks.Add("FAIL Minecraft lobby height depends on survivor");yield break;}
                        foreach(var mesh in avatar.GetComponentsInChildren<MeshRenderer>())
                            if(mesh.sharedMaterial.shader.name!="Hopoo Games/Deferred/Standard" || !mesh.sharedMaterial.IsKeywordEnabled("CUTOUT")) {
                                checks.Add("FAIL lobby material uses incompatible lighting or opaque outer layer");yield break;
                            }
                        int hidden=0;foreach(var renderer in display.GetComponentsInChildren<Renderer>()) if(!renderer.transform.IsChildOf(avatar)) {
                            if(renderer.enabled) {checks.Add("FAIL native lobby model still visible for "+survivor.cachedName);yield break;}hidden++;
                        }
                        if(hidden==0 || avatar.GetComponentsInChildren<MeshRenderer>().Length!=12) continue;
                        var late=GameObject.CreatePrimitive(PrimitiveType.Cube);
                        late.name="Owned probe late native weapon";late.transform.SetParent(display,false);
                        var lateRenderer=late.GetComponent<Renderer>();
                        var lateBehaviour=late.AddComponent<AudioSource>();
                        var particleObject=new GameObject("Owned probe late native VFX");particleObject.transform.SetParent(display,false);
                        var particles=particleObject.AddComponent<ParticleSystem>();particles.Play();
                        try {
                            var before=typeof(LobbySkinPolish).GetMethod("BeforeCamera",BindingFlags.Static|BindingFlags.NonPublic);
                            before.Invoke(null,new object[]{null});
                            if(lateRenderer.enabled || !lateRenderer.forceRenderingOff || lateBehaviour.enabled || particles.isPlaying || particles.particleCount!=0)
                                {checks.Add("FAIL late native weapon/VFX not suppressed before camera");yield break;}
                            lateRenderer.enabled=true;lateRenderer.forceRenderingOff=false;
                            before.Invoke(null,new object[]{null});
                            if(lateRenderer.enabled || !lateRenderer.forceRenderingOff) {checks.Add("FAIL re-enabled native weapon visible");yield break;}
                            checks.Add("PASS late native weapon/script/particles suppressed and renderer re-enable defeated for "+survivor.cachedName);
                        } finally {UnityEngine.Object.DestroyImmediate(late);UnityEngine.Object.DestroyImmediate(particleObject);}
                        found=true;break;
                    }
                    if(!found) {checks.Add("FAIL actual lobby mannequin skin missing for "+survivor.cachedName);yield break;}
                    checks.Add("PASS actual lobby mannequin replaced for "+survivor.cachedName+"; native renderers hidden");
                    checks.Add("PASS native HG skin cutout material and fixed two-unit Minecraft height for "+survivor.cachedName);
                    if(++variants==4) break;
                }
                var owner=UnityEngine.Object.FindObjectOfType<LobbySkinPolish>();
                var map=typeof(LobbySkinPolish).GetField("avatars",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner) as Dictionary<Transform,LobbySkinAvatar>;
                foreach(var entry in map) {
                    var root=entry.Key.Find("RoRCraft lobby avatar");
                    if(root==null)continue;
                    try {DanceSceneProbe.CaptureNormalLobby(checks,root,entry.Value);}catch(Exception error){checks.Add("FAIL normal lobby dance capture: "+error);}
                    break;
                }
                if(variants!=4) checks.Add("FAIL actual lobby did not exercise four survivor selections");
                checks.Add((expectedRebuilds>0 && synchronousCount>=expectedRebuilds?"PASS ":"FAIL ")+"native mannequin already replaced and hidden when rebuild returns; callbacks="+synchronousCount+" expected="+expectedRebuilds);
            } finally {synchronousChecks=null;timing.UnpatchSelf();if(user!=null && original!=null) user.SetSurvivorPreferenceClient(original);}
        }
        internal static int BaselineTextures,BaselineMaterials;
        internal static int CountTextures() {int n=0;foreach(var x in Resources.FindObjectsOfTypeAll<Texture2D>()) if(x.name=="RoRCraft lobby skin texture") n++;return n;}
        internal static int CountMaterials() {int n=0;foreach(var x in Resources.FindObjectsOfTypeAll<Material>()) if(x.name=="RoRCraft lobby skin material") n++;return n;}
        internal static void Run(List<string> checks,string output) {
            BaselineTextures=CountTextures();BaselineMaterials=CountMaterials();
            var type=typeof(LobbySkinPolish).Assembly.GetType("RoRCraftPolish.LobbySkinAvatar",true);
            var dataType=typeof(LobbySkinPolish).Assembly.GetType("RoRCraftPolish.SessionSkinData",true);
            var ctor=type.GetConstructor(new[]{typeof(Transform),dataType});
            var read=dataType.GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic);
            bool live=Array.IndexOf(Environment.GetCommandLineArgs(),"-rorcraftLiveSkin")>=0;
            string session=File.ReadAllText(System.IO.Path.Combine(BepInEx.Paths.ConfigPath,"rorcraft-skin-session.txt")).Trim();
            byte[] skinBytes;
            if(live) skinBytes=File.ReadAllBytes(System.IO.Path.Combine(BepInEx.Paths.ConfigPath,"rorcraft-skin-session.bin"));
            else {
                byte[] pixels=new byte[64*64*4];for(int n=0;n<pixels.Length;n+=4) {pixels[n]=(byte)(n%251);pixels[n+1]=190;pixels[n+2]=60;pixels[n+3]=255;}
                skinBytes=Payload(session,Guid.NewGuid().ToString("D"),false,pixels);
            }
            object skin=read.Invoke(null,new object[]{skinBytes,session});
            Reject(read,skinBytes,Guid.NewGuid().ToString("N"),checks,"other launch/account cache is rejected");
            var corrupt=(byte[])skinBytes.Clone();corrupt[corrupt.Length-1]^=1;Reject(read,corrupt,session,checks,"corrupt skin pixels rejected");
            Reject(read,new byte[120],session,checks,"truncated skin rejected");
            var display=new GameObject("Lobby preview test fixture");display.transform.position=new Vector3(20000,0,0);
            var native=GameObject.CreatePrimitive(PrimitiveType.Cube);native.transform.SetParent(display.transform,false);native.transform.localPosition=Vector3.up;
            native.transform.localScale=new Vector3(.6f,2,.6f);var original=native.GetComponent<Renderer>();
            var collider=native.GetComponent<Collider>();UnityEngine.Object.DestroyImmediate(collider);
            object avatar=null;Camera camera=null;RenderTexture rt=null;Texture2D image=null;GameObject light=null;
            try {
                for(int cycle=0;cycle<12;cycle++) {
                    avatar=ctor.Invoke(new object[]{display.transform,skin});
                    type.GetMethod("Tick").Invoke(avatar,null);
                    if(original.enabled) throw new InvalidOperationException("Native preview renderer was not hidden");
                    if(display.GetComponentsInChildren<Collider>().Length!=0) throw new InvalidOperationException("Lobby avatar added gameplay colliders");
                    var root=display.transform.Find("RoRCraft lobby avatar");
                    if(root==null || root.GetComponentsInChildren<MeshRenderer>().Length!=12) throw new InvalidOperationException("Missing Minecraft body/outer layers");
                    if(cycle==0) {
                        // Fixture pose is independent from actual lobby cameras;
                        // actual front-facing still needs real lobby acceptance.
                        root.localRotation=Quaternion.Euler(0,180,0);
                        foreach(var tr in root.GetComponentsInChildren<Transform>()) tr.gameObject.layer=31;
                        camera=new GameObject("Owned lobby probe camera").AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<31;
                        camera.renderingPath=UnityEngine.RenderingPath.DeferredShading;
                        camera.transform.position=display.transform.position+new Vector3(0,1.1f,-4);camera.transform.LookAt(display.transform.position+Vector3.up);
                        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.magenta;camera.nearClipPlane=.1f;camera.farClipPlane=10;
                        camera.orthographic=true;camera.orthographicSize=1.25f;
                        light=new GameObject("Owned lobby probe light");var lamp=light.AddComponent<Light>();lamp.type=LightType.Directional;lamp.cullingMask=1<<31;
                        light.transform.rotation=Quaternion.Euler(25,0,0);lamp.intensity=1;
                        rt=new RenderTexture(512,512,24);rt.Create();camera.targetTexture=rt;camera.Render();
                        var previous=RenderTexture.active;
                        try {RenderTexture.active=rt;image=new Texture2D(512,512,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();}
                        finally {RenderTexture.active=previous;}
                        int occupied=0;foreach(var pixel in image.GetPixels32()) if(!(pixel.r>245 && pixel.b>245 && pixel.g<10)) occupied++;
                        if(occupied<10000) throw new InvalidOperationException("Lobby avatar render positive control failed: "+occupied);
                        File.WriteAllBytes(output,image.EncodeToPNG());
                        checks.Add("PASS "+(live?"actual Minecraft session":"synthetic protocol")+" skin rendered on native GPU; 12 body/outer meshes, occupied="+occupied);
                    }
                    ((IDisposable)avatar).Dispose();avatar=null;
                    if(!original.enabled) throw new InvalidOperationException("Native preview renderer was not restored");
                }
                checks.Add("PASS 12 construct/Tick/dispose preview cycles without a CharacterBody; native renderers restored");
                checks.Add("PASS lobby avatars add no gameplay colliders");
                byte[] slimPixels=new byte[64*64*4];for(int n=3;n<slimPixels.Length;n+=4) slimPixels[n]=255;
                var slim=read.Invoke(null,new object[]{Payload(session,Guid.NewGuid().ToString("D"),true,slimPixels),session});
                avatar=ctor.Invoke(new object[]{display.transform,slim});
                var avatarRoot=display.GetComponentsInChildren<Transform>();Transform newest=null;
                foreach(var tr in avatarRoot) if(tr.name=="RoRCraft lobby avatar") newest=tr;
                var arm=newest.Find("Right arm").GetComponentInChildren<MeshFilter>().sharedMesh.bounds.size;
                if(Mathf.Abs(arm.x-.1875f)>.0001f) throw new InvalidOperationException("Slim arm width incorrect: "+arm.x);
                ((IDisposable)avatar).Dispose();avatar=null;
                checks.Add("PASS second synthetic profile/slim model produces 3px arm geometry");
                // Deferred Destroy roots must not be mistaken for live avatars.
                checks.Add("INFO final cleanup resource counts are checked after deferred destruction");
            } finally {
                if(avatar!=null) ((IDisposable)avatar).Dispose();
                if(camera!=null) {camera.targetTexture=null;UnityEngine.Object.Destroy(camera.gameObject);}
                if(rt!=null) {rt.Release();UnityEngine.Object.Destroy(rt);}
                if(image!=null) UnityEngine.Object.Destroy(image);
                if(light!=null) UnityEngine.Object.Destroy(light);
                UnityEngine.Object.Destroy(display);
            }
            checks.Add("Scope: native GPU lobby-avatar fixture and lifecycle only; actual character-select layout/selection requires player acceptance.");
        }
        private static byte[] Payload(string session,string account,bool slim,byte[] pixels) {
            using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream)) {
                writer.Write(0x5243534B);writer.Write(1);writer.Write(System.Text.Encoding.ASCII.GetBytes(session));
                writer.Write(System.Text.Encoding.ASCII.GetBytes(account));writer.Write(slim?1:0);writer.Write(64);writer.Write(64);
                using(var sha=System.Security.Cryptography.SHA256.Create()) writer.Write(sha.ComputeHash(pixels));
                writer.Write(pixels);return stream.ToArray();
            }
        }
        private static void Reject(MethodInfo read,byte[] bytes,string session,List<string> checks,string label) {
            try {read.Invoke(null,new object[]{bytes,session});throw new InvalidOperationException("Invalid payload accepted: "+label);}
            catch(TargetInvocationException error) {if(!(error.InnerException is InvalidDataException)) throw;checks.Add("PASS "+label);}
        }
    }
}
