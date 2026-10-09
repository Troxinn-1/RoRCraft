using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using RoR2;
using UnityEngine;
using Path=System.IO.Path;
namespace RoRCraftPolish {
    internal static class SurvivorSkinFeatureProbe {
        internal static IEnumerator Verify(List<string> checks) {
            var owner=UnityEngine.Object.FindObjectOfType<LobbySkinPolish>();
            if(owner==null || owner.AccountSkin==null){checks.Add("FAIL skin feature account unavailable");yield break;}
            var local=LocalUserManager.GetFirstLocalUser();
            var user=local==null?null:local.currentNetworkUser;
            if(user==null || PreGameController.instance==null){checks.Add("FAIL skin feature requires ready native lobby");yield break;}
            var original=user.GetSurvivorPreference();var profile=owner.AccountSkin.Account;
            var realStore=owner.SkinStore;
            string path=Path.Combine(Paths.ConfigPath,"SurvivorSkinProbeLibrary");Directory.CreateDirectory(path);
            var isolated=new SurvivorSkinStore(path);
            var variants=new List<SurvivorDef>();foreach(var def in SurvivorCatalog.allSurvivorDefs)
                if(def!=null && !def.hidden && def.bodyPrefab!=null){variants.Add(def);if(variants.Count==2)break;}
            string aFile=Path.Combine(path,"probe-red.png"),bFile=Path.Combine(path,"probe-blue.png");
            Png(aFile,new Color32(225,60,45,255));Png(bFile,new Color32(40,125,235,255));
            string a=isolated.Import(aFile,false),b=isolated.Import(bFile,true);
            bool repeat=Array.IndexOf(Environment.GetCommandLineArgs(),"-rorcraftSkinRestart")>=0;
            if(repeat && (isolated.Assignment(profile,LobbySkinPolish.SurvivorKey(variants[0]))!=a || isolated.Assignment(profile,LobbySkinPolish.SurvivorKey(variants[1]))!=b))
                {checks.Add("FAIL skin assignments did not persist across actual process restart");yield break;}
            if(repeat)checks.Add("PASS survivor skin assignments survived actual RoR2/MC restart");
            isolated.Assign(profile,LobbySkinPolish.SurvivorKey(variants[0]),a);
            isolated.Assign(profile,LobbySkinPolish.SurvivorKey(variants[1]),b);
            if(new SurvivorSkinStore(path).Assignment(profile,LobbySkinPolish.SurvivorKey(variants[0]))!=a)
                {checks.Add("FAIL skin persistence reload");yield break;}
            checks.Add("PASS assignments independently persist per survivor/profile");
            string unusedProfile=Guid.NewGuid().ToString("D");
            if(isolated.Assignment(unusedProfile,LobbySkinPolish.SurvivorKey(variants[0]))!=null){checks.Add("FAIL assignments leak into another profile");yield break;}
            var storeField=typeof(LobbySkinPolish).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic);
            var clear=typeof(LobbySkinPolish).GetMethod("Clear",BindingFlags.Instance|BindingFlags.NonPublic);
            var identity=typeof(LobbySkinPolish).GetField("selectedIdentity",BindingFlags.Instance|BindingFlags.NonPublic);
            var displayField=typeof(RoR2.SurvivorMannequins.SurvivorMannequinSlotController).GetField("mannequinInstanceTransform",BindingFlags.Instance|BindingFlags.NonPublic);
            try {
                storeField.SetValue(owner,isolated);identity.SetValue(owner,null);clear.Invoke(owner,null);
                var menu=typeof(LobbySkinPolish).GetField("skinMenu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner) as SurvivorSkinMenu;
                var eventSystem=UnityEngine.EventSystems.EventSystem.current;
                bool navigation=eventSystem!=null && eventSystem.sendNavigationEvents;
                menu.Open();yield return null;
                if(eventSystem!=null && eventSystem.sendNavigationEvents){checks.Add("FAIL skin menu did not shield native navigation");yield break;}
                menu.Close();yield return null;
                if(eventSystem!=null && eventSystem.sendNavigationEvents!=navigation){checks.Add("FAIL skin menu did not restore navigation");yield break;}
                checks.Add("PASS skin menu opens/closes and restores native navigation");
                using(var music=new WindowsDanceWave()) {
                    if(Mathf.Abs(music.Duration-6)>.01f){checks.Add("FAIL Pigstep duration does not match dance");yield break;}
                    music.Play(0);music.Stop();
                    float volume=MinecraftMenuAudio.MusicVolume();
                    if(volume<0 || volume>1){checks.Add("FAIL native dance music volume range");yield break;}
                    checks.Add("PASS six-second Pigstep PCM/default-device format and native Master/Music gain; silent check only");
                }
                for(int i=0;i<4;i++) {
                    int variant=i%2;user.SetSurvivorPreferenceClient(variants[variant]);
                    yield return new WaitForSecondsRealtime(2);
                    string failure;var chosen=isolated.Resolve(owner.AccountSkin,LobbySkinPolish.SurvivorKey(variants[variant]),out failure);
                    bool visible=false;
                    foreach(var slot in UnityEngine.Object.FindObjectsOfType<RoR2.SurvivorMannequins.SurvivorMannequinSlotController>()) {
                        if(slot.networkUser!=user)continue;var display=displayField.GetValue(slot) as Transform;if(display==null)continue;
                        var avatar=display.Find("RoRCraft lobby avatar");if(avatar==null)continue;
                        var texture=avatar.GetComponentInChildren<MeshRenderer>().sharedMaterial.mainTexture as Texture2D;
                        var color=texture.GetPixel(10,50);var expected=chosen.Pixels;
                        if(Mathf.Abs(color.r*255-expected[0])>2 || Mathf.Abs(color.g*255-expected[1])>2 || Mathf.Abs(color.b*255-expected[2])>2)
                            {checks.Add("FAIL actual lobby custom skin pixels mismatch");yield break;}
                        var arm=avatar.Find("Right arm").GetComponentInChildren<MeshFilter>().sharedMesh.bounds.size.x;
                        if(Mathf.Abs(arm-(chosen.Slim?.1875f:.25f))>.001f){checks.Add("FAIL actual lobby custom model width");yield break;}
                        int roots=0;foreach(Transform child in display)if(child.name=="RoRCraft lobby avatar" && child.gameObject.activeSelf)roots++;
                        if(roots!=1){checks.Add("FAIL duplicate skin models");yield break;}
                        visible=true;
                        var map=typeof(LobbySkinPolish).GetField("avatars",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner) as Dictionary<Transform,LobbySkinAvatar>;
                        var idle=typeof(LobbySkinAvatar).GetField("idle",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(map[display]) as LobbyIdleController;
                        if(i==0 && idle!=null){
                            if(!owner.PreviewDance()){checks.Add("FAIL lobby dance preview action");yield break;}
                            var before=avatar.Find("Right arm").localRotation;
                            for(int frame=0;frame<20;frame++)idle.Advance(.1f);
                            if(Quaternion.Angle(before,avatar.Find("Right arm").localRotation)<5){checks.Add("FAIL dance did not move actual lobby bones");yield break;}
                            checks.Add("PASS actual lobby dance poses Minecraft bones");
                            for(int frame=0;frame<41;frame++)idle.Advance(.1f);
                            if(idle.State!="normal_idle" || Quaternion.Angle(Quaternion.identity,avatar.Find("Right arm").localRotation)>4){checks.Add("FAIL dance did not return to normal idle");yield break;}
                            if(idle.Play("item_inspection")){checks.Add("FAIL inspection activated without held equipment");yield break;}
                            idle.Play("parrot_dance");
                            checks.Add("PASS dance returns to normal idle; inspection requires held item");
                        }
                    }
                    if(!visible){checks.Add("FAIL custom lobby model missing");yield break;}
                    string ackKey=profile+"|"+chosen.Hash.Replace("-","").ToLowerInvariant()+"|"+(chosen.Slim?1:0);
                    yield return new WaitForSecondsRealtime(1);
                    if(!Ack(ackKey)){checks.Add("FAIL Minecraft did not accept the same selected skin/model");yield break;}
                    checks.Add("PASS actual lobby and Minecraft selected skin agree: "+variants[variant].cachedName+" "+(chosen.Slim?"slim":"classic"));
                }
                string problem;var missing=new SurvivorSkinStore(path);
                string wrong=new string('a',64)+":0";missing.Assign(profile,"MissingProbeBody",wrong);
                if(missing.Resolve(owner.AccountSkin,"MissingProbeBody",out problem).Hash!=owner.AccountSkin.Hash || problem==null)
                    {checks.Add("FAIL missing custom skin account fallback");yield break;}
                checks.Add("PASS missing custom skin falls back to authenticated account skin");
                isolated.Assign(profile,LobbySkinPolish.SurvivorKey(variants[1]),null);
                identity.SetValue(owner,null);clear.Invoke(owner,null);yield return new WaitForSecondsRealtime(2);
                if(!Ack(profile+"|"+owner.AccountSkin.Hash.Replace("-","").ToLowerInvariant()+"|"+(owner.AccountSkin.Slim?1:0))){checks.Add("FAIL reset did not restore original account skin in Minecraft");yield break;}
                isolated.Assign(profile,LobbySkinPolish.SurvivorKey(variants[1]),b);
                checks.Add("PASS reset restores original authenticated account skin in Minecraft");
                user.SetSurvivorPreferenceClient(variants[0]);yield return new WaitForSecondsRealtime(2);
                owner.PreviewDance();yield return null;
                HarmonyLib.AccessTools.Method(typeof(PreGameController),"StartRun").Invoke(PreGameController.instance,null);
                float until=Time.realtimeSinceStartup+35;
                while((Run.instance==null || LocalUserManager.GetFirstLocalUser().cachedBody==null)&&Time.realtimeSinceStartup<until)yield return new WaitForSecondsRealtime(.5f);
                if(Run.instance==null || LocalUserManager.GetFirstLocalUser().cachedBody==null){checks.Add("FAIL selected-skin actual run did not start");yield break;}
                yield return new WaitForSecondsRealtime(3);
                if(typeof(LobbySkinPolish).GetField("musicAvatar",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner)!=null){checks.Add("FAIL dance music state survived run start");yield break;}
                checks.Add("PASS dance music state stops on run start");
                var runSkin=isolated.Resolve(owner.AccountSkin,LobbySkinPolish.SurvivorKey(variants[0]),out problem);
                if(!Ack(profile+"|"+runSkin.Hash.Replace("-","").ToLowerInvariant()+"|0")){checks.Add("FAIL selected skin lost at actual run start");yield break;}
                checks.Add("PASS actual RoR2 run retains selected Minecraft skin/model; human F5 appearance pending");
            } finally {
                storeField.SetValue(owner,realStore);identity.SetValue(owner,null);clear.Invoke(owner,null);
                if(user!=null && original!=null)user.SetSurvivorPreferenceClient(original);
            }
        }
        private static bool Ack(string key) {
            var path=Path.Combine(Paths.ConfigPath,"rorcraft-skin-selected.ack");
            return File.Exists(path) && File.ReadAllText(path)==File.ReadAllText(Path.Combine(Paths.ConfigPath,"rorcraft-skin-session.txt")).Trim()+"\n"+key;
        }
        private static void Png(string path,Color32 color) {
            var texture=new Texture2D(64,64,TextureFormat.RGBA32,false);var pixels=new Color32[4096];
            for(int i=0;i<4096;i++)pixels[i]=color;texture.SetPixels32(pixels);texture.Apply();
            File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.Destroy(texture);
        }
    }
}
