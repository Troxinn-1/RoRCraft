using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoRCraftPolish {
    // Render actual native shader albedo to offscreen G-buffers. No player input,
    // scene lights or normal game materials are changed by this reference check.
    internal static class NativeTintProbe {
        internal static void Run(List<string> results) {
            Shader shader=null;
            foreach(var candidate in Resources.FindObjectsOfTypeAll<Shader>()) if(candidate.name=="Hopoo Games/Deferred/Standard") {shader=candidate;break;}
            if(shader==null) shader=UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<Shader>("RoR2/Base/Shaders/HGStandard.shader").WaitForCompletion();
            if(shader==null) throw new InvalidOperationException("Native HG reference shader unavailable");
            var material=new Material(shader);
            var mesh=new Mesh();
            var cameraObject=new GameObject("RoRCraft offscreen tint reference");
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
            camera.transform.position=new Vector3(0,0,-2);camera.orthographic=true;camera.orthographicSize=1;camera.aspect=1;camera.nearClipPlane=.1f;camera.farClipPlane=10;
            var targets=new RenderTexture[4];var ids=new RenderTargetIdentifier[4];
            var readback=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);
            var command=new CommandBuffer();command.name="RoRCraft native tint reference";
            var active=RenderTexture.active;
            try {
                mesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(-1,1,0),new Vector3(1,1,0),new Vector3(1,-1,0)};
                mesh.uv=new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)};
                mesh.normals=new[]{Vector3.back,Vector3.back,Vector3.back,Vector3.back};
                mesh.tangents=new[]{new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1)};
                mesh.triangles=new[]{0,1,2,0,2,3};
                for(int i=0;i<4;i++) {targets[i]=new RenderTexture(32,32,i==0?24:0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);targets[i].name="RoRCraft tint probe target";targets[i].Create();ids[i]=new RenderTargetIdentifier(targets[i]);}
                material.SetTexture("_MainTex",Texture2D.whiteTexture);material.SetColor("_Color",Color.white);
                material.SetFloat("_Cull",0);material.SetFloat("_Fade",1);material.SetFloat("_NormalStrength",0);material.SetFloat("_EmPower",0);
                material.SetFloat("_SpecularStrength",0);material.SetFloat("_SpecularExponent",1);material.SetFloat("_Smoothness",0);material.SetFloat("_RampInfo",1);material.SetFloat("_DecalLayer",0);
                int pass=material.FindPass("DEFERRED");if(pass<0) throw new InvalidOperationException("Deferred reference pass unavailable");
                var white=Draw(mesh,material,camera,command,targets,ids,readback,pass,Color.white,false);
                var green=Draw(mesh,material,camera,command,targets,ids,readback,pass,new Color(.2f,.7f,.3f,1),true);
                results.Add("INFO native offscreen albedo white="+white+" vertexTint="+green);
                if(white.r<.9f || white.g<.9f || white.b<.9f) throw new InvalidOperationException("Reference quad failed to produce white albedo");
                if(Math.Abs(white.r-green.r)>.01f || Math.Abs(white.g-green.g)>.01f || Math.Abs(white.b-green.b)>.01f) throw new InvalidOperationException("GPU disagrees with selected-variant inspection; revise tint diagnosis");
                results.Add("PASS GPU confirms USE_VERTEX_COLORS alone leaves native deferred albedo unchanged");
                Near(Read(targets[1],readback),new Color(0,.05f,1f/16f,0),"native specular/ramp packing control");
                Near(Read(targets[2],readback),new Color(.5f,.5f,0,0),"native normal/decal packing control");
                results.Add("PASS native parameter table agrees with actual GPU MRT packing");
                mesh.colors=new[]{Color.white,Color.white,Color.white,Color.white};
                material.DisableKeyword("USE_VERTEX_COLORS");material.SetColor("_Color",new Color(.25f,.5f,.75f,1));
                var uniform=Draw(mesh,material,camera,command,targets,ids,readback,pass,Color.white,false);
                results.Add("INFO native uniform tint control="+uniform);
                if(uniform.r>=white.r-.1f || uniform.g>=white.g-.1f) throw new InvalidOperationException("Uniform tint positive control failed");
                results.Add("PASS uniform material tint positive control changes actual GPU albedo");
                byte[] image=readback.EncodeToPNG();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(BepInEx.Paths.ConfigPath,"rorcraft-native-tint-reference.png"),image);
                CheckExperimental(shader,results,mesh,camera,command,targets,ids,readback);
            } finally {
                RenderTexture.active=active;command.Release();
                foreach(var target in targets) if(target!=null) {target.Release();UnityEngine.Object.Destroy(target);}
                UnityEngine.Object.Destroy(readback);UnityEngine.Object.Destroy(mesh);UnityEngine.Object.Destroy(material);UnityEngine.Object.Destroy(cameraObject);
            }
        }
        private static void CheckExperimental(Shader reference,List<string> results,Mesh mesh,Camera camera,CommandBuffer command,RenderTexture[] targets,RenderTargetIdentifier[] ids,Texture2D readback) {
            var path=System.IO.Path.Combine(BepInEx.Paths.ConfigPath,"rorcraft-experimental-block-shaders");
            AssetBundle bundle=null;Shader shader=null;
            if(System.IO.File.Exists(path)) {
                bundle=AssetBundle.LoadFromFile(path);
                if(bundle==null) throw new InvalidOperationException("Experimental shader bundle could not load");
                shader=bundle.LoadAsset<Shader>("Assets/RoRCraft/BlockDeferred.shader");
            } else {
                var embedded=typeof(MaterialPolish).Assembly.GetType("RoRCraftPolish.MinecraftBlockShader");
                if(embedded==null) {results.Add("INFO experimental/embedded shader absent; replacement shader NOT tested");return;}
                shader=(Shader)embedded.GetMethod("Get",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,new object[]{(Action<string>)results.Add});
                results.Add("INFO checking block shader embedded in the installed unified mod");
            }
            Material material=null;
            try {
                if(shader==null || !shader.isSupported) throw new InvalidOperationException("Experimental shader missing or unsupported");
                material=new Material(shader);material.SetTexture("_MainTex",Texture2D.whiteTexture);material.SetColor("_Color",Color.white);material.SetFloat("_Cull",0);
                int pass=material.FindPass("DEFERRED");if(pass<0) throw new InvalidOperationException("Experimental DEFERRED pass absent");
                Color white=Draw(mesh,material,camera,command,targets,ids,readback,pass,Color.white,false);
                Near(white,new Color(1,1,1,1),"experimental neutral albedo");results.Add("PASS experimental neutral albedo");
                Color tint=new Color(.2f,.7f,.3f,1);
                Color actual=Draw(mesh,material,camera,command,targets,ids,readback,pass,tint,false);
                Near(actual,new Color(Mathf.GammaToLinearSpace(tint.r),Mathf.GammaToLinearSpace(tint.g),Mathf.GammaToLinearSpace(tint.b),1),"experimental vertex tint in linear space");
                results.Add("PASS experimental vertex tint changes actual GPU albedo");
                material.SetFloat("_VertexTint",0);
                Near(Draw(mesh,material,camera,command,targets,ids,readback,pass,tint,false),white,"experimental tint disable control");results.Add("PASS experimental tint disable control");
                material.SetFloat("_VertexTint",1);material.SetFloat("_DecalLayer",0);material.SetFloat("_RampInfo",1);material.SetFloat("_SpecularExponent",1);
                Draw(mesh,material,camera,command,targets,ids,readback,pass,Color.white,false);
                Near(Read(targets[1],readback),new Color(0,.05f,1f/16f,0),"experimental specular and ramp packing");
                Near(Read(targets[2],readback),new Color(.5f,.5f,0,0),"experimental world normal and neutral decal packing");results.Add("PASS experimental specular decal normal and ramp MRT packing");
                material.SetFloat("_SpecularStrength",.2f);material.SetFloat("_SpecularMask",1);
                Draw(mesh,material,camera,command,targets,ids,readback,pass,Color.white,false);
                Near(Read(targets[1],readback),new Color(.2f,.05f,1f/16f,0),"opaque metal independent specular mask");
                results.Add("PASS opaque metal reflects without changing texture transparency");material.SetFloat("_SpecularMask",0);material.SetFloat("_SpecularStrength",0);
                material.SetTexture("_EmTex",Texture2D.whiteTexture);material.SetFloat("_EmPower",1);material.EnableKeyword("UNITY_HDR_ON");
                Draw(mesh,material,camera,command,targets,ids,readback,pass,Color.white,false);
                var hdr=Read(targets[3],readback);
                material.DisableKeyword("UNITY_HDR_ON");
                Draw(mesh,material,camera,command,targets,ids,readback,pass,Color.white,false);
                Near(Read(targets[3],readback),new Color(Mathf.Pow(2,-hdr.r),Mathf.Pow(2,-hdr.g),Mathf.Pow(2,-hdr.b),1),"experimental HDR/LDR emission encoding");results.Add("PASS experimental HDR and LDR emission variants agree");
                material.EnableKeyword("CUTOUT");material.SetColor("_Color",new Color(1,1,1,0));
                Near(Draw(mesh,material,camera,command,targets,ids,readback,pass,Color.white,false),Color.clear,"experimental cutout discards albedo");results.Add("PASS experimental transparent texel discarded");
                material.SetColor("_Color",Color.white);
                Near(Draw(mesh,material,camera,command,targets,ids,readback,pass,Color.white,false),white,"experimental opaque cutout texel");results.Add("PASS experimental opaque cutout texel retained");
                NativeLightingProbe.Run(reference,shader,results);
                results.Add("Scope: replacement MRT output only; real scene shadows, fog, ambient balance and performance remain unaccepted.");
            } finally {if(material!=null) UnityEngine.Object.Destroy(material);if(bundle!=null) bundle.Unload(true);}
        }
        private static Color Read(RenderTexture target,Texture2D readback) {RenderTexture.active=target;readback.ReadPixels(new Rect(0,0,32,32),0,0,false);readback.Apply(false,false);return readback.GetPixel(16,16);}
        private static void Near(Color actual,Color expected,string label) {
            if(Math.Abs(actual.r-expected.r)>.01f || Math.Abs(actual.g-expected.g)>.01f || Math.Abs(actual.b-expected.b)>.01f || Math.Abs(actual.a-expected.a)>.01f) throw new InvalidOperationException(label+": got "+actual+" expected "+expected);
        }
        private static Color Draw(Mesh mesh,Material material,Camera camera,CommandBuffer command,RenderTexture[] targets,RenderTargetIdentifier[] ids,Texture2D readback,int pass,Color color,bool keyword) {
            mesh.colors=new[]{color,color,color,color};
            if(keyword) material.EnableKeyword("USE_VERTEX_COLORS");else material.DisableKeyword("USE_VERTEX_COLORS");
            command.Clear();command.SetRenderTarget(ids,new RenderTargetIdentifier(targets[0]));command.ClearRenderTarget(true,true,Color.clear);
            command.SetViewProjectionMatrices(camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(camera.projectionMatrix,true));
            command.DrawMesh(mesh,Matrix4x4.identity,material,0,pass);Graphics.ExecuteCommandBuffer(command);
            RenderTexture.active=targets[0];readback.ReadPixels(new Rect(0,0,32,32),0,0,false);readback.Apply(false,false);
            return readback.GetPixel(16,16);
        }
    }
}
