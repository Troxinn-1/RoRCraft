using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using RoR2;

namespace RoRCraftPolish {
    [BepInPlugin("cz.jirka.rorcraft-settings-probe","Minecraft settings developer probe","0.1.0")]
    [BepInDependency(MashupSettings.Guid,BepInDependency.DependencyFlags.HardDependency)]
    public sealed class SettingsProbe : BaseUnityPlugin {
        private IEnumerator Start() {
            if(!Config.Bind("Developer","Enabled",false,"Controlled developer settings verification. Disabled for normal play.").Value) yield break;
            var settings=MashupSettings.Instance;var material=MaterialPolish.Instance;
            float deadline=Time.unscaledTime+150;
            Dictionary<int,Material> materials=null;
            GameObject root=null;
            var adapter=BepInEx.Bootstrap.Chainloader.PluginInfos["cz.jirka.rorcraft-skycraft"].Instance;
            var rendererField=AccessTools.Field(adapter.GetType(),"renderer");
            while(Time.unscaledTime<deadline) {
                if(adapter!=null && Run.instance!=null) {
                    var renderer=rendererField.GetValue(adapter);
                    if(renderer!=null) {
                        materials=(Dictionary<int,Material>)AccessTools.Field(renderer.GetType(),"materials").GetValue(renderer);
                        root=(GameObject)AccessTools.Field(renderer.GetType(),"root").GetValue(renderer);
                    }
                    if(materials!=null && materials.Count>0 && root!=null && root.GetComponentsInChildren<MeshRenderer>(true).Length>0) break;
                }
                yield return new WaitForSecondsRealtime(1);
            }
            string output=System.IO.Path.Combine(Paths.ConfigPath,"rorcraft-settings-probe.txt");
            if(materials==null || materials.Count==0) {File.WriteAllText(output,"FAIL: test materials unavailable");yield break;}
            // The developer Minecraft world can replace its renderer during
            // initial stage handoff. Verify the live root, not the discarded one.
            yield return new WaitForSecondsRealtime(5);
            var currentRenderer=rendererField.GetValue(adapter);
            materials=(Dictionary<int,Material>)AccessTools.Field(currentRenderer.GetType(),"materials").GetValue(currentRenderer);
            root=(GameObject)AccessTools.Field(currentRenderer.GetType(),"root").GetValue(currentRenderer);
            var cap=settings.FrameCap.Value;var sync=settings.VSync.Value;
            bool lighting=material.NativeLighting.Value,shadows=material.BlockShadows.Value;
            bool distant=material.DistantFiltering.Value;float influence=material.EnvironmentColorInfluence.Value;
            var checks=new List<string>();
            try {
                foreach(MashupSettings.FrameLimit value in Enum.GetValues(typeof(MashupSettings.FrameLimit))) {
                    if(value==MashupSettings.FrameLimit.GameDefault) continue;
                    settings.FrameCap.Value=value;yield return null;yield return null;
                    checks.Add((Application.targetFrameRate==(int)value ? "PASS":"FAIL")+": FPS "+value+" actual "+Application.targetFrameRate);
                }
                settings.VSync.Value=MashupSettings.SyncMode.Off;yield return null;yield return null;
                checks.Add((QualitySettings.vSyncCount==0 ? "PASS":"FAIL")+": VSync off");
                settings.VSync.Value=MashupSettings.SyncMode.On;yield return null;yield return null;
                checks.Add((QualitySettings.vSyncCount==1 ? "PASS":"FAIL")+": VSync on");
                material.NativeLighting.Value=false;material.BlockShadows.Value=false;
                yield return null;yield return null;
                checks.Add(CheckMaterials(materials,"Standard") ? "PASS: live lit fallback":"FAIL: live lit fallback");
                checks.Add(CheckShadows(root,false,checks) ? "PASS: shadows off":"FAIL: shadows off");
                material.NativeLighting.Value=true;material.BlockShadows.Value=true;
                yield return null;yield return null;
                checks.Add(CheckMaterials(materials,"Hopoo Games/Deferred/Standard") ? "PASS: live native lit shader":"FAIL: live native lit shader");
                checks.Add(CheckShadows(root,true,checks) ? "PASS: shadows on":"FAIL: shadows on");
                material.DistantFiltering.Value=false;yield return null;yield return null;
                var close=materials[0].mainTexture as Texture2D;
                checks.Add(close!=null && close.mipmapCount==1 ? "PASS: distant filtering off uses full resolution":"FAIL: distant filtering off");
                material.DistantFiltering.Value=true;yield return null;yield return null;
                var distantTexture=materials[0].mainTexture as Texture2D;
                checks.Add(distantTexture!=null && distantTexture.mipmapCount==3 ? "PASS: distant filtering on uses two isolated reductions":"FAIL: distant filtering on");
                checks.Add(MaterialPolish.DeveloperAtlasAudit());
                foreach(float value in new[]{0f,.5f,1f}) {material.EnvironmentColorInfluence.Value=value;yield return null;yield return null;checks.Add(material.EnvironmentColorInfluence.Value==value ? "PASS: environment influence "+value:"FAIL: environment influence");}
            } finally {
                settings.FrameCap.Value=cap;settings.VSync.Value=sync;
                material.NativeLighting.Value=lighting;material.BlockShadows.Value=shadows;
                material.DistantFiltering.Value=distant;material.EnvironmentColorInfluence.Value=influence;
            }
            yield return null;yield return null;
            settings.Config.Reload();material.Config.Reload();
            checks.Add(settings.FrameCap.Value==cap && settings.VSync.Value==sync && material.NativeLighting.Value==lighting && material.BlockShadows.Value==shadows && material.DistantFiltering.Value==distant && material.EnvironmentColorInfluence.Value==influence ? "PASS: disk reload restored all six settings":"FAIL: disk reload");
            File.WriteAllLines(output,checks.ToArray());
            Logger.LogInfo("Settings runtime checks saved: "+output+" (restore completed)");
            if(Config.Bind("Developer","QuitWhenDone",false,"Normally exit this controlled developer run after settings checks and restoration.").Value) Application.Quit();
        }
        private static bool CheckMaterials(Dictionary<int,Material> materials,string shader) {
            int count=0;foreach(var entry in materials) if(entry.Key%2==0 && entry.Value!=null) {count++;if(entry.Value.shader.name!=shader || entry.Value.mainTexture==null) return false;}return count>0;
        }
        private static bool CheckShadows(GameObject root,bool enabled,List<string> checks) {
            int count=0;foreach(var mesh in root.GetComponentsInChildren<MeshRenderer>(true)) {
                count++;if(mesh.receiveShadows!=enabled || (mesh.shadowCastingMode!=UnityEngine.Rendering.ShadowCastingMode.Off)!=enabled) {
                    var tracked=(HashSet<MeshRenderer>)AccessTools.Field(typeof(MaterialPolish),"meshes").GetValue(null);
                    checks.Add("DETAIL: "+mesh.name+" receive="+mesh.receiveShadows+" cast="+mesh.shadowCastingMode+" tracked="+tracked.Contains(mesh)+" tracked count="+tracked.Count);
                    return false;
                }
            } return count>0;
        }
    }
}
