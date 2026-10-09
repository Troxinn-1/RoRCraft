using System;
using System.Collections.Generic;
using System.IO;
using RoR2;
using UnityEngine;

namespace RoRCraftPolish
{
    // Lobby-only cosmetic fork. Never instantiate a CharacterBody or touch stats.
    internal sealed class LobbySkinAvatar : IDisposable
    {
        private readonly CharacterBody body;
        private readonly GameObject root;
        private readonly Material material;
        private readonly Texture2D skin;
        private Color32[] skinPixels;
        private bool maskFace;
        private bool previewFacing;
        private readonly Dictionary<Renderer,bool> original=new Dictionary<Renderer,bool>();
        private readonly Dictionary<Renderer,bool> originalForced=new Dictionary<Renderer,bool>();
        private readonly Dictionary<Behaviour,bool> nativeBehaviours=new Dictionary<Behaviour,bool>();
        private readonly List<Renderer> rendererScan=new List<Renderer>();
        private readonly List<Behaviour> behaviourScan=new List<Behaviour>();
        private readonly List<ParticleSystem> particleScan=new List<ParticleSystem>();
        private readonly List<Mesh> meshes=new List<Mesh>();
        private Transform torso,head,rightArm,leftArm,rightLeg,leftLeg;
        private LobbyIdleController idle;
        private LobbyDanceScene danceScene;
        private int idleFrame=-1;
        private Vector3 last;
        private float phase,swing;
        private bool disposed;
        public LobbySkinAvatar(CharacterBody target,string path) : this(target,null,path) {}
        public LobbySkinAvatar(Transform display,string path) : this(null,display,path) {}
        public LobbySkinAvatar(Transform display,SessionSkinData data) : this(null,display,null,data) {}
        private LobbySkinAvatar(CharacterBody target,Transform display,string path) : this(target,display,path,null) {}
        private LobbySkinAvatar(CharacterBody target,Transform display,string path,SessionSkinData data)
        {
            body=target;
            try {
            skin=new Texture2D(64,64,TextureFormat.RGBA32,false);
            if(data==null) {
                if(!ImageConversion.LoadImage(skin,File.ReadAllBytes(path))) throw new InvalidDataException("Cannot load Minecraft skin.");
            } else {
                var pixels=new byte[data.Pixels.Length];
                for(int row=0;row<64;row++) Buffer.BlockCopy(data.Pixels,row*64*4,pixels,(63-row)*64*4,64*4);
                skin.LoadRawTextureData(pixels);skin.Apply(false,false);
            }
            if(skin.width!=64 || skin.height!=64) {UnityEngine.Object.Destroy(skin);throw new InvalidDataException("Lobby skin must be a 64x64 Minecraft skin.");}
            skin.name="RoRCraft lobby skin texture";skin.filterMode=FilterMode.Point;skin.wrapMode=TextureWrapMode.Clamp;
            skinPixels=skin.GetPixels32();
            // RoR2's deferred lighting uses HG G-buffer packing, not Unity Standard.
            Shader shader=null;
            foreach(var loaded in Resources.FindObjectsOfTypeAll<Shader>())
                if(loaded!=null && loaded.isSupported && loaded.name=="Hopoo Games/Deferred/Standard") {shader=loaded;break;}
            if(shader==null) shader=Shader.Find("Hopoo Games/Deferred/Standard");
            if(shader==null) throw new InvalidOperationException("Native lobby skin shader not loaded; keep survivor preview.");
            if(shader==null) throw new InvalidOperationException("No avatar shader available.");
            material=new Material(shader);material.mainTexture=skin;material.SetInt("_Cull",2);material.SetFloat("_Cutoff",.1f);
            material.SetInt("_ZWrite",1);material.SetInt("_ZTest",4);
            material.SetFloat("_Mode",1);material.EnableKeyword("_ALPHATEST_ON");material.SetFloat("_Glossiness",0);material.SetFloat("_SpecularHighlights",0);material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");material.renderQueue=2450;
            material.EnableKeyword("CUTOUT");
            Set("_EnableCutout",1);Set("_NormalStrength",0);Set("_SpecularStrength",0);
            Set("_EmPower",0);Set("_Fade",1);Set("_RampInfo",1);Set("_DecalLayer",0);
            Set("_ColorsOn",0);Set("_DoubleColorsOn",0);Set("_Smoothness",0);
            if(material.HasProperty("_Color")) material.SetColor("_Color",Color.white);
            material.name="RoRCraft lobby skin material";root=new GameObject("RoRCraft lobby avatar");
            if(display!=null) {
                root.transform.SetParent(display,false);
                // Minecraft front is +Z; native mannequin displays face -Z.
                root.transform.localRotation=Quaternion.Euler(0,180,0);
            } else UnityEngine.Object.DontDestroyOnLoad(root);
            // Keep the original bones/animator and item display system alive.
            var model=body==null || body.modelLocator==null || body.modelLocator.modelTransform==null ? null : body.modelLocator.modelTransform.GetComponent<CharacterModel>();
            if(model!=null) { foreach(var info in model.baseRendererInfos) if(info.renderer!=null) original[info.renderer]=info.renderer.enabled; }
            else if(body!=null && body.modelLocator!=null && body.modelLocator.modelTransform!=null)
                foreach(var r in body.modelLocator.modelTransform.GetComponentsInChildren<SkinnedMeshRenderer>()) original[r]=r.enabled;
            else if(display!=null) foreach(var r in display.GetComponentsInChildren<Renderer>()) original[r]=r.enabled;
            torso=Part("Torso",new Vector3(0,1.125f,0),new Vector3(.5f,.75f,.25f),Vector3.zero,16,16,8,12,4,16,32);
            head=Part("Head",new Vector3(0,1.5f,0),new Vector3(.5f,.5f,.5f),new Vector3(0,.25f,0),0,0,8,8,8,32,0);
            int armWidth=data!=null && data.Slim?3:4;float armSize=armWidth/16f,armPivot=.25f+armSize*.5f;
            rightArm=Part("Right arm",new Vector3(-armPivot,1.5f,0),new Vector3(armSize,.75f,.25f),new Vector3(0,-.375f,0),40,16,armWidth,12,4,40,32);
            leftArm=Part("Left arm",new Vector3(armPivot,1.5f,0),new Vector3(armSize,.75f,.25f),new Vector3(0,-.375f,0),32,48,armWidth,12,4,48,48);
            rightLeg=Part("Right leg",new Vector3(-.125f,.75f,0),new Vector3(.25f,.75f,.25f),new Vector3(0,-.375f,0),0,16,4,12,4,0,32);
            leftLeg=Part("Left leg",new Vector3(.125f,.75f,0),new Vector3(.25f,.75f,.25f),new Vector3(0,-.375f,0),16,48,4,12,4,0,48);
            last=body==null?Vector3.zero:body.footPosition;
            if(display!=null) FitPreview(display);
            if(display!=null) idle=new LobbyIdleController(torso,head,rightArm,leftArm,rightLeg,leftLeg,display.GetInstanceID());
            } catch {Dispose();throw;}
        }
        private void Set(string property,float value) {if(material.HasProperty(property)) material.SetFloat(property,value);}
        private void FitPreview(Transform display) {
            Bounds bounds=new Bounds();bool found=false;
            foreach(var pair in original) if(pair.Key is SkinnedMeshRenderer && pair.Value) {
                if(!found) {bounds=pair.Key.bounds;found=true;}else bounds.Encapsulate(pair.Key.bounds);
            }
            if(!found) foreach(var pair in original) if(pair.Key!=null && pair.Value) {
                if(!found) {bounds=pair.Key.bounds;found=true;}else bounds.Encapsulate(pair.Key.bounds);
            }
            var anchor=display.position;
            if(found && bounds.size.y>=.1f) anchor.y=bounds.min.y;
            root.transform.position=anchor;
            // Minecraft identity keeps a two-unit height across survivors. Native
            // weapons/oversized bodies must not change the Minecraft proportions.
            var scale=display.lossyScale;
            root.transform.localScale=new Vector3(1/Mathf.Max(.001f,Mathf.Abs(scale.x)),1/Mathf.Max(.001f,Mathf.Abs(scale.y)),1/Mathf.Max(.001f,Mathf.Abs(scale.z)));
            FacePreviewCamera();
        }
        private void FacePreviewCamera() {
            if(previewFacing || body!=null) return;
            Camera selected=null;
            foreach(var camera in Camera.allCameras) {
                if(camera==null || !camera.enabled || camera.orthographic || camera.targetTexture!=null || (camera.cullingMask&(1<<root.layer))==0) continue;
                if(selected==null || camera==Camera.main || camera.depth>selected.depth && selected!=Camera.main) selected=camera;
            }
            if(selected==null) return;
            var toward=selected.transform.position-root.transform.position;toward.y=0;
            if(toward.sqrMagnitude<.01f) return;
            root.transform.rotation=Quaternion.LookRotation(toward);
            previewFacing=true;
        }
        internal void RefreshNativeRenderers(Transform display) {
            if(disposed || root==null || display==null)return;
            FacePreviewCamera();
            display.GetComponentsInChildren<Renderer>(true,rendererScan);
            foreach(var renderer in rendererScan) if(!renderer.transform.IsChildOf(root.transform)) {
                if(!original.ContainsKey(renderer)) original.Add(renderer,renderer.enabled);
                if(!originalForced.ContainsKey(renderer)) originalForced.Add(renderer,renderer.forceRenderingOff);
                renderer.forceRenderingOff=true;renderer.enabled=false;
            }
            // Display-only animators/scripts/lights can re-enable meshes or emit
            // survivor intro VFX. Keep them dormant while this preview is replaced.
            display.GetComponentsInChildren<Behaviour>(true,behaviourScan);
            foreach(var behaviour in behaviourScan) if(!behaviour.transform.IsChildOf(root.transform)) {
                if(!nativeBehaviours.ContainsKey(behaviour)) nativeBehaviours.Add(behaviour,behaviour.enabled);
                var script=behaviour as MonoBehaviour;if(script!=null && script.enabled)script.StopAllCoroutines();
                behaviour.enabled=false;
            }
            display.GetComponentsInChildren<ParticleSystem>(true,particleScan);
            foreach(var particles in particleScan) if(!particles.transform.IsChildOf(root.transform))
                particles.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        private Transform Part(string name,Vector3 pivot,Vector3 size,Vector3 center,int u,int v,int w,int h,int d,int outerU,int outerV)
        {
            var part=new GameObject(name);part.transform.SetParent(root.transform,false);part.transform.localPosition=pivot;
            Box(part.transform,size,center,u,v,w,h,d);
            if(skin.height>=64) {
                // Emit only occupied outer-layer texels. Transparent hat/jacket
                // regions cannot become black occluders in another shader pass.
                maskFace=true;
                try {Box(part.transform,size+Vector3.one*.025f,center,outerU,outerV,w,h,d);}
                finally {maskFace=false;}
            }
            return part.transform;
        }
        private void Box(Transform parent,Vector3 size,Vector3 center,int u,int v,int w,int h,int d)
        {
            var x=size.x*.5f;var y=size.y*.5f;var z=size.z*.5f;
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
            Face(vertices,uv,indices,center,new Vector3(-x,-y,z),new Vector3(x,-y,z),new Vector3(x,y,z),new Vector3(-x,y,z),u+d,v+d,w,h);
            Face(vertices,uv,indices,center,new Vector3(x,-y,-z),new Vector3(-x,-y,-z),new Vector3(-x,y,-z),new Vector3(x,y,-z),u+2*d+w,v+d,w,h);
            Face(vertices,uv,indices,center,new Vector3(-x,-y,-z),new Vector3(-x,-y,z),new Vector3(-x,y,z),new Vector3(-x,y,-z),u,v+d,d,h);
            Face(vertices,uv,indices,center,new Vector3(x,-y,z),new Vector3(x,-y,-z),new Vector3(x,y,-z),new Vector3(x,y,z),u+d+w,v+d,d,h);
            Face(vertices,uv,indices,center,new Vector3(-x,y,z),new Vector3(x,y,z),new Vector3(x,y,-z),new Vector3(-x,y,-z),u+d,v,w,d);
            Face(vertices,uv,indices,center,new Vector3(-x,-y,-z),new Vector3(x,-y,-z),new Vector3(x,-y,z),new Vector3(-x,-y,z),u+d+w,v,w,d);
            var mesh=new Mesh();mesh.vertices=vertices.ToArray();mesh.uv=uv.ToArray();mesh.triangles=indices.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();meshes.Add(mesh);
            var go=new GameObject("Skin layer");go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
        }
        private void Face(List<Vector3> verts,List<Vector2> uv,List<int> tri,Vector3 c,Vector3 a,Vector3 b,Vector3 e,Vector3 f,int u,int v,int w,int h)
        {
            if(maskFace) {
                for(int y=0;y<h;y++) for(int x=0;x<w;x++) {
                    int px=u+x,py=skin.height-v-h+y;
                    if(skinPixels[py*skin.width+px].a<26) continue;
                    var horizontal=b-a;var vertical=f-a;
                    var corner=a+horizontal*(x/(float)w)+vertical*(y/(float)h);
                    Quad(verts,uv,tri,c,corner,corner+horizontal/w,corner+horizontal/w+vertical/h,corner+vertical/h,u+x,v+h-y-1,1,1);
                }
                return;
            }
            Quad(verts,uv,tri,c,a,b,e,f,u,v,w,h);
        }
        private void Quad(List<Vector3> verts,List<Vector2> uv,List<int> tri,Vector3 c,Vector3 a,Vector3 b,Vector3 e,Vector3 f,int u,int v,int w,int h) {
            int n=verts.Count;verts.Add(c+a);verts.Add(c+b);verts.Add(c+e);verts.Add(c+f);
            float left=u/(float)skin.width,right=(u+w)/(float)skin.width,top=1-v/(float)skin.height,bottom=1-(v+h)/(float)skin.height;
            uv.Add(new Vector2(left,bottom));uv.Add(new Vector2(right,bottom));uv.Add(new Vector2(right,top));uv.Add(new Vector2(left,top));
            tri.Add(n);tri.Add(n+1);tri.Add(n+2);tri.Add(n);tri.Add(n+2);tri.Add(n+3);
        }
        public void Tick()
        {
            if(disposed || root==null || !root.activeInHierarchy)return;
            foreach(var item in originalForced) if(item.Key!=null) item.Key.forceRenderingOff=true;
            foreach(var item in nativeBehaviours) if(item.Key!=null) item.Key.enabled=false;
            foreach(var item in original) if(item.Key!=null) item.Key.enabled=false;
            if(body==null) {if(idle!=null && idleFrame!=Time.frameCount){idleFrame=Time.frameCount;idle.Advance(Time.unscaledDeltaTime);}UpdateDanceScene();return;}
            var at=body.footPosition;var delta=at-last;delta.y=0;last=at;
            phase+=delta.magnitude*7;float walk=Mathf.Sin(phase)*Mathf.Clamp(delta.magnitude/Mathf.Max(Time.deltaTime,.001f),0,6)*7;
            root.transform.position=at;
            var source=body.modelLocator==null ? null : body.modelLocator.modelTransform;
            if(source!=null) root.transform.rotation=Quaternion.Euler(0,source.eulerAngles.y,0);
            else root.transform.rotation=Quaternion.Euler(0,body.transform.eulerAngles.y,0);
            if(Input.GetMouseButton(0)) swing=1;else swing=Mathf.Max(0,swing-Time.deltaTime*4);
            rightLeg.localRotation=Quaternion.Euler(walk,0,0);leftLeg.localRotation=Quaternion.Euler(-walk,0,0);
            rightArm.localRotation=Quaternion.Euler(-walk-swing*65,0,0);leftArm.localRotation=Quaternion.Euler(walk-swing*45,0,0);
            var bank=body.inputBank;
            if(bank!=null && bank.aimDirection.sqrMagnitude>.01f) {
                var look=root.transform.InverseTransformDirection(bank.aimDirection.normalized);
                head.localRotation=Quaternion.Euler(-Mathf.Asin(Mathf.Clamp(look.y,-1,1))*Mathf.Rad2Deg,Mathf.Clamp(Mathf.Atan2(look.x,look.z)*Mathf.Rad2Deg,-75,75),0);
            }
            root.SetActive(body.healthComponent!=null && body.healthComponent.alive);
        }
        internal bool IsDancing {get{return !disposed && root!=null && root.activeInHierarchy && idle!=null && idle.State=="parrot_dance";}}
        internal bool PreviewDance(){return !disposed && root!=null && root.activeInHierarchy && idle!=null && idle.Play("parrot_dance");}
        internal void UpdateDanceScene(){if(IsDancing && danceScene==null)danceScene=new LobbyDanceScene(root.transform,torso,material);if(danceScene!=null)danceScene.Update(IsDancing,idle==null?0:idle.ClipAge);}
        public void Dispose()
        {
            Dispose(true);
        }
        internal void Dispose(bool restoreNative)
        {
            if(disposed) return;disposed=true;
            if(danceScene!=null)danceScene.Dispose();
            if(restoreNative) foreach(var item in original) if(item.Key!=null) item.Key.enabled=item.Value;
            if(restoreNative) foreach(var item in originalForced) if(item.Key!=null) item.Key.forceRenderingOff=item.Value;
            if(restoreNative) foreach(var item in nativeBehaviours) if(item.Key!=null) item.Key.enabled=item.Value;
            if(root!=null) root.SetActive(false);
            UnityEngine.Object.Destroy(root);foreach(var mesh in meshes) UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(material);UnityEngine.Object.Destroy(skin);
        }
    }
}
