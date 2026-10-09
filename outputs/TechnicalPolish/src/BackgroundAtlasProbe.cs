using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace RoRCraftPolish {
    [BepInPlugin("cz.jirka.rorcraft-background-atlas-probe","RoRCraft headless atlas verification","0.1.0")]
    [BepInDependency(MashupSettings.Guid,BepInDependency.DependencyFlags.HardDependency)]
    public sealed class BackgroundAtlasProbe:BaseUnityPlugin {
        private readonly List<string> checks=new List<string>();
        private object atlas;
        private Texture2D source;
        private Type atlasType;
        private bool requested;
        private const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private IEnumerator Start() {
            requested=Array.IndexOf(Environment.GetCommandLineArgs(),"-rorcraftBackgroundProbe")>=0;
            if(!requested) yield break;
            if(!Application.isBatchMode) {Logger.LogError("Headless verification refused: missing batch mode.");yield break;}
            Application.runInBackground=true;Application.targetFrameRate=30;
            yield return new WaitForSecondsRealtime(2);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-rorcraftLoaderProbe")>=0) {
                foreach(var name in new[]{"RoRCraft.RoRCraftPlugin","RoRCraftPolish.MaterialPolish","RoRCraftPolish.MovementPolish","RoRCraftPolish.MashupSettings","RoRCraftPolish.LobbySkinPolish"}) {
                    var type=HarmonyLib.AccessTools.TypeByName(name);
                    checks.Add((type!=null && UnityEngine.Object.FindObjectOfType(type)!=null?"PASS ":"FAIL ")+"installed loader initialized "+name);
                }
                File.WriteAllLines(Path.Combine(Paths.ConfigPath,"rorcraft-background-atlas-probe.txt"),checks.ToArray());
                yield return new WaitForSecondsRealtime(2);Application.Quit();yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-rorcraftGameplayLiveProbe")>=0) {
                yield return GameplayLiveProbe.Run(checks);
                File.WriteAllLines(Path.Combine(Paths.ConfigPath,"rorcraft-background-atlas-probe.txt"),checks.ToArray());
                // StopHost queues scene/network teardown. Quitting in that same
                // frame races the native scene jobs; let normal teardown settle.
                yield return new WaitForSecondsRealtime(5);
                Application.Quit();yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-rorcraftGameplayProbe")>=0) {
                string receipt=Path.Combine(Paths.ConfigPath,"rorcraft-gameplay-probe.txt");
                float deadline=Time.realtimeSinceStartup+110;
                while(!File.Exists(receipt) && Time.realtimeSinceStartup<deadline)yield return new WaitForSecondsRealtime(.25f);
                File.WriteAllLines(Path.Combine(Paths.ConfigPath,"rorcraft-background-atlas-probe.txt"),File.Exists(receipt)?File.ReadAllLines(receipt):new[]{"FAIL no Minecraft gameplay probe receipt"});
                Application.Quit();yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-rorcraftLiveSkin")>=0) {
                var skinPath=Path.Combine(Paths.ConfigPath,"rorcraft-skin-session.bin");
                float until=Time.realtimeSinceStartup+75;
                while(!LiveSkinReady(skinPath) && Time.realtimeSinceStartup<until) yield return new WaitForSecondsRealtime(.25f);
                // Allow async default -> account skin update before taking evidence.
                yield return new WaitForSecondsRealtime(3);
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-rorcraftIsolatedDancePreview")<0)yield return NativeLobbyProbe.VerifyActualLobby(checks);
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-rorcraftSurvivorSkinsProbe")>=0) {
                    var feature=SurvivorSkinFeatureProbe.Verify(checks);
                    while(true) {
                        bool more;object current;
                        try {more=feature.MoveNext();current=more?feature.Current:null;}
                        catch(Exception error) {checks.Add("FAIL survivor skin feature: "+error);break;}
                        if(!more)break;yield return current;
                    }
                }
                // Let the feature probe's restored/disposed lobby models finish
                // deferred destruction before measuring the fixture baseline.
                yield return null;yield return null;yield return null;
            }
            bool lobby=Array.IndexOf(Environment.GetCommandLineArgs(),"-rorcraftLobbyProbe")>=0;
            if(lobby) {
                try {DanceSceneProbe.Run(checks);}catch(Exception error){checks.Add("FAIL dance GPU capture: "+error);}
                // The capture fixture owns a skin too: drain its deferred
                // resources before taking the separate lifecycle baseline.
                yield return null;yield return null;
            }
            try {if(lobby) NativeLobbyProbe.Run(checks,Path.Combine(Paths.ConfigPath,"rorcraft-lobby-avatar.png"));else RunChecks();} catch(Exception error) {checks.Add("FAIL "+error);}
            finally {Cleanup();}
            yield return null;yield return null;
            if(lobby) {
                int textures=NativeLobbyProbe.CountTextures(),materials=NativeLobbyProbe.CountMaterials();
                checks.Add((textures==NativeLobbyProbe.BaselineTextures?"PASS ":"FAIL ")+"lobby skin textures return to baseline after deferred destruction; actual="+textures+" baseline="+NativeLobbyProbe.BaselineTextures);
                checks.Add((materials==NativeLobbyProbe.BaselineMaterials?"PASS ":"FAIL ")+"lobby materials return to baseline after deferred destruction; actual="+materials+" baseline="+NativeLobbyProbe.BaselineMaterials);
                int props=0;foreach(var asset in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())if(asset!=null && asset.name.StartsWith("RoRCraft dance "))props++;
                checks.Add((props==0?"PASS ":"FAIL ")+"dance prop meshes/materials/textures released after deferred destruction; retained="+props);
            }
            int retained=0;
            foreach(var texture in Resources.FindObjectsOfTypeAll<Texture2D>()) if(texture!=null && (texture.name=="RoRCraft headless probe source" || texture.name=="Minecraft sprite-safe atlas" || texture.name=="Minecraft animated atlas staging")) retained++;
            if(!lobby) checks.Add((retained==0?"PASS":"FAIL")+" probe source, derived and staging textures released after deferred destruction, retained="+retained);
            checks.Add("Scope: actual Unity texture API and module load; no stage visual, gameplay, frame-pacing or human Alt-Tab acceptance.");
            string path=System.IO.Path.Combine(Paths.ConfigPath,"rorcraft-background-atlas-probe.txt");
            File.WriteAllLines(path,checks.ToArray());foreach(string result in checks) Logger.LogInfo(result);
            if(RoR2.Networking.NetworkManagerSystem.singleton!=null) RoR2.Networking.NetworkManagerSystem.singleton.StopHost();
            yield return new WaitForSecondsRealtime(.5f);
            Application.Quit();
        }
        private static bool LiveSkinReady(string path) {
            try {
                var type=typeof(LobbySkinPolish).Assembly.GetType("RoRCraftPolish.SessionSkinData",true);
                string session=File.ReadAllText(Path.Combine(Paths.ConfigPath,"rorcraft-skin-session.txt")).Trim();
                type.GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{File.ReadAllBytes(path),session});
                return true;
            } catch {return false;}
        }
        private void Check(bool value,string label) {checks.Add((value?"PASS ":"FAIL ")+label);if(!value) throw new InvalidOperationException(label);}
        private object Call(string name,params object[] arguments) {return atlasType.GetMethod(name,Members).Invoke(atlas,arguments);}
        private Texture2D Texture(string name) {return (Texture2D)atlasType.GetField(name,Members).GetValue(atlas);}
        private static byte[] Descriptor() {using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream)) {foreach(int value in new[]{0x50494d53,8,4,2,1,0,0,4,4,0,4,0,4,4,0}) writer.Write(value);return stream.ToArray();}}
        private static byte[] Region(int x,int w,int h,byte red) {using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream)) {foreach(int value in new[]{x,0,w,h}) writer.Write(value);for(int i=0;i<w*h;i++) {writer.Write(red);writer.Write((byte)0);writer.Write((byte)0);writer.Write((byte)255);}return stream.ToArray();}}
        private void RunChecks() {
            NativeTintProbe.Run(checks);
            Check(!CursorSafety.OwnsFocus,"headless player does not own desktop focus");
            checks.Add("INFO GPU="+SystemInfo.graphicsDeviceType+" copies="+SystemInfo.copyTextureSupport+" batch="+Application.isBatchMode);
            var assembly=typeof(MashupSettings).Assembly;
            var audioType=assembly.GetType("RoRCraftPolish.MinecraftMenuAudio",true);
            string audio=(string)audioType.GetMethod("DeveloperAudioAudit",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            checks.Add("INFO "+audio);
            Check(audio.Contains("Windows PCM ready=True"),"native Windows menu PCM initialized; format/volume query only, no audible playback");
            var layoutType=assembly.GetType("RoRCraftPolish.AtlasTiles",true);
            atlasType=assembly.GetType("RoRCraftPolish.AtlasTexture",true);
            var layout=Activator.CreateInstance(layoutType,Members,null,new object[]{Descriptor()},null);
            source=new Texture2D(8,4,TextureFormat.RGBA32,false,false);source.name="RoRCraft headless probe source";
            source.SetPixelData<byte>(new byte[8*4*4],0,0);source.Apply(false,false);
            atlas=Activator.CreateInstance(atlasType,Members,null,new object[]{layout,source,true},null);
            Call("Region",Region(0,4,4,255));Call("Region",Region(4,1,2,111));Call("Flush");
            var pixels=source.GetRawTextureData();
            Check(pixels[(3*8+4)*4]==111,"native item atlas receives animation patches");
            Check(Texture("Texture").GetRawTextureData()[(3*8+4)*4]==111,"native block and item base texels agree");
            Check(Texture("Texture").mipmapCount==3,"native block atlas has three isolated levels");
            long uploads=(long)atlasType.GetField("Uploads",Members).GetValue(atlas);Call("Flush");
            Check((long)atlasType.GetField("Uploads",Members).GetValue(atlas)==uploads,"idle native atlas does not reupload");
            UnityEngine.Object.Destroy((Texture2D)Call("SelectMipmaps",false));
            Check(Texture("Texture").mipmapCount==1 && Texture("Texture").GetRawTextureData()[(3*8+4)*4]==111,"native quality toggle retains animated base");
            UnityEngine.Object.Destroy((Texture2D)Call("SelectMipmaps",true));
            Check(Texture("Texture").mipmapCount==3,"native mipmaps can be restored");
            // Exercise the production descriptor receiver, including repeated metadata.
            var shader=Shader.Find("Standard");if(shader==null) shader=Shader.Find("Sprites/Default");
            Check(shader!=null,"probe material shader available");
            var material=new Material(shader);material.mainTexture=source;
            var materials=new Dictionary<int,Material>{{0,material}};
            var receiver=typeof(MaterialPolish).GetMethod("AtlasPacket",BindingFlags.Static|BindingFlags.NonPublic);
            Call("Dispose",false);atlas=null;
            try {
                receiver.Invoke(null,new object[]{20,Descriptor(),materials});
                var first=material.mainTexture;
                receiver.Invoke(null,new object[]{20,Descriptor(),materials});
                Check(material.mainTexture!=first && ((Texture2D)material.mainTexture).mipmapCount==3,"production receiver accepts repeated descriptor without invalid raw-mip length");
            } finally {
                typeof(MaterialPolish).GetMethod("Disposed",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                UnityEngine.Object.Destroy(material);source=null;
            }
        }
        private void Cleanup() {if(atlas!=null) {Call("Dispose",true);atlas=null;source=null;}if(source!=null) {UnityEngine.Object.Destroy(source);source=null;}}
        private void OnDestroy() {if(requested) Cleanup();}
    }
}
