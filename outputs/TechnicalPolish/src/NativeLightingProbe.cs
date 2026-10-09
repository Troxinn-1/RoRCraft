using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
namespace RoRCraftPolish {
    // Controlled offscreen native-player fixture, not real-stage acceptance.
    internal static class NativeLightingProbe {
        internal static void Run(Shader reference,Shader replacement,List<string> results) {
            const int layer=31;var origin=new Vector3(10000,10000,10000);
            var root=new GameObject("RoRCraft offscreen lighting fixture");
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cameraObject=new GameObject("RoRCraft offscreen lighting camera");var lightObject=new GameObject("RoRCraft offscreen lighting light");
            foreach(var obj in new[]{cube,floor,cameraObject,lightObject}) obj.transform.SetParent(root.transform);
            cube.layer=floor.layer=layer;cube.transform.position=origin;floor.transform.position=origin+new Vector3(0,-.55f,0);floor.transform.localScale=new Vector3(4,.1f,4);
            var renderer=cube.GetComponent<MeshRenderer>();var floorRenderer=floor.GetComponent<MeshRenderer>();
            var native=new Material(reference);var custom=new Material(replacement);
            var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();
            var readback=new Texture2D(256,256,TextureFormat.RGBA32,false,true);
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.renderingPath=RenderingPath.DeferredShading;camera.allowHDR=false;camera.allowMSAA=false;
            camera.cullingMask=1<<layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            camera.transform.position=origin+new Vector3(2,2,-3);camera.transform.LookAt(origin);camera.orthographic=false;camera.fieldOfView=55;camera.aspect=1;camera.nearClipPlane=.1f;camera.farClipPlane=20;camera.targetTexture=target;
            // Cast toward the visible foreground floor; a shadow behind this
            // cube is occluded by the cube itself from the fixture camera.
            var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.cullingMask=1<<layer;light.intensity=1;light.transform.rotation=Quaternion.Euler(50,140,0);
            var previousProbe=RenderSettings.ambientProbe;var previousSun=RenderSettings.sun;var previousShadowQuality=QualitySettings.shadows;var previousShadowDistance=QualitySettings.shadowDistance;var previousCascades=QualitySettings.shadowCascades;
            var active=RenderTexture.active;Mesh ownedMesh=null;Texture2D checker=null;
            try {
                foreach(var material in new[]{native,custom}) {
                    material.SetTexture("_MainTex",Texture2D.whiteTexture);material.SetColor("_Color",Color.white);
                    foreach(var key in new[]{"_SpecularStrength","_Smoothness","_EmPower","_DecalLayer","_NormalStrength"}) if(material.HasProperty(key)) material.SetFloat(key,0);
                    material.SetFloat("_RampInfo",1);if(material.HasProperty("_Fade")) material.SetFloat("_Fade",1);
                }
                ownedMesh=cube.GetComponent<MeshFilter>().mesh;var colors=new Color[ownedMesh.vertexCount];for(int i=0;i<colors.Length;i++) colors[i]=Color.white;ownedMesh.colors=colors;
                var ambient=new SphericalHarmonicsL2();ambient.AddAmbientLight(new Color(.15f,.15f,.15f));RenderSettings.ambientProbe=ambient;
                QualitySettings.shadows=ShadowQuality.All;QualitySettings.shadowDistance=20;QualitySettings.shadowCascades=0;
                light.renderMode=LightRenderMode.ForcePixel;RenderSettings.sun=light;camera.depthTextureMode=DepthTextureMode.Depth;
                results.Add("INFO controlled requested path="+camera.renderingPath+" custom deferred="+GraphicsSettings.GetCustomShader(BuiltinShaderType.DeferredShading));
                string directory=Path.Combine(BepInEx.Paths.ConfigPath,"rorcraft-shader-lighting");Directory.CreateDirectory(directory);
                Color32[] unshadowed=null;
                for(int scenario=0;scenario<3;scenario++) {
                    light.color=scenario==1?new Color(.25f,.65f,1):Color.white;light.shadows=scenario==2?LightShadows.Hard:LightShadows.None;
                    renderer.sharedMaterial=native;floorRenderer.sharedMaterial=native;var baseline=Capture(camera,target,readback,Path.Combine(directory,"native-"+scenario+".png"));
                    if(camera.actualRenderingPath!=RenderingPath.DeferredShading) throw new InvalidOperationException("Controlled camera failed to use deferred rendering; comparison invalid");
                    renderer.sharedMaterial=custom;floorRenderer.sharedMaterial=custom;var candidate=Capture(camera,target,readback,Path.Combine(directory,"custom-"+scenario+".png"));
                    if(scenario==0) unshadowed=baseline;
                    if(scenario==2) {
                        int shadowPixels=0;for(int i=0;i<baseline.Length;i++) if(Math.Max(Math.Abs((int)baseline[i].r-unshadowed[i].r),Math.Max(Math.Abs((int)baseline[i].g-unshadowed[i].g),Math.Abs((int)baseline[i].b-unshadowed[i].b)))>5) shadowPixels++;
                        results.Add("INFO native shadow positive control pixels="+shadowPixels);
                        if(shadowPixels<100) throw new InvalidOperationException("Native shadow positive control absent; caster/receiver comparison invalid");
                        results.Add("PASS native shadow positive control is visible");
                    }
                    double error=0,brightness=0;int count=0;
                    for(int i=0;i<baseline.Length;i++) {
                        if(baseline[i].r+baseline[i].g+baseline[i].b<12 && candidate[i].r+candidate[i].g+candidate[i].b<12) continue;
                        error+=Math.Abs((int)baseline[i].r-candidate[i].r)+Math.Abs((int)baseline[i].g-candidate[i].g)+Math.Abs((int)baseline[i].b-candidate[i].b);
                        brightness+=baseline[i].r+baseline[i].g+baseline[i].b;count++;
                    }
                    double mean=count==0?0:error/(count*3*255d);
                    results.Add("INFO controlled lighting scenario="+scenario+" path="+camera.actualRenderingPath+" occupied="+count+" mean RGB difference="+mean.ToString("F5",System.Globalization.CultureInfo.InvariantCulture));
                    if(count<500 || brightness<5000) throw new InvalidOperationException("Controlled native lighting fixture did not render");
                    if(mean>.035) throw new InvalidOperationException("Replacement differs from native neutral lighting; inspect fixture images, scenario="+scenario);
                    results.Add("PASS controlled native lighting comparison "+scenario);
                }
                checker=new Texture2D(16,16,TextureFormat.RGBA32,false);checker.filterMode=FilterMode.Point;checker.wrapMode=TextureWrapMode.Clamp;
                var pixels=new Color32[256];for(int y=0;y<16;y++) for(int x=0;x<16;x++) pixels[y*16+x]=new Color32(255,255,255,(byte)(((x/4+y/4)%2)==0?255:0));checker.SetPixels32(pixels);checker.Apply();
                foreach(var material in new[]{native,custom}) {material.SetTexture("_MainTex",checker);material.EnableKeyword("CUTOUT");}
                // The floor needs an opaque independent material while the cube
                // exercises cutout albedo and its shadow caster together.
                var floorMaterial=new Material(native);floorMaterial.SetTexture("_MainTex",Texture2D.whiteTexture);floorMaterial.DisableKeyword("CUTOUT");
                try {
                    floorRenderer.sharedMaterial=floorMaterial;renderer.sharedMaterial=native;
                    var baseline=Capture(camera,target,readback,Path.Combine(directory,"native-cutout-shadow.png"));
                    renderer.sharedMaterial=custom;var candidate=Capture(camera,target,readback,Path.Combine(directory,"custom-cutout-shadow.png"));
                    int changed=0;for(int i=0;i<baseline.Length;i++) if(Math.Max(Math.Abs((int)baseline[i].r-candidate[i].r),Math.Max(Math.Abs((int)baseline[i].g-candidate[i].g),Math.Abs((int)baseline[i].b-candidate[i].b)))>5) changed++;
                    results.Add("INFO cutout caster/receiver comparison differing pixels="+changed);
                    if(changed>30) throw new InvalidOperationException("Cutout shadow fixture differs from native; inspect paired images");
                    results.Add("PASS controlled cutout albedo and shadow comparison");
                } finally {UnityEngine.Object.Destroy(floorMaterial);}
                results.Add("Scope: isolated camera, white/blue light, visible native shadows and checker cutout. Real stages, fog, grass contact, skin and performance remain unaccepted.");
            } finally {
                RenderSettings.ambientProbe=previousProbe;RenderSettings.sun=previousSun;QualitySettings.shadows=previousShadowQuality;QualitySettings.shadowDistance=previousShadowDistance;QualitySettings.shadowCascades=previousCascades;
                RenderTexture.active=active;camera.targetTexture=null;target.Release();
                if(ownedMesh!=null) UnityEngine.Object.Destroy(ownedMesh);
                if(checker!=null) UnityEngine.Object.Destroy(checker);
                foreach(var obj in new UnityEngine.Object[]{root,native,custom,target,readback}) UnityEngine.Object.Destroy(obj);
            }
        }
        private static Color32[] Capture(Camera camera,RenderTexture target,Texture2D readback,string path) {
            camera.Render();RenderTexture.active=target;readback.ReadPixels(new Rect(0,0,target.width,target.height),0,0,false);readback.Apply(false,false);
            File.WriteAllBytes(path,readback.EncodeToPNG());return readback.GetPixels32();
        }
    }
}
