using System;
using System.Collections.Generic;
using System.IO;
using RoR2;
using UnityEngine;

namespace RoRCraft
{
    // A cosmetic avatar on the real RoR2 body. Skills, damage, inventory,
    // interactions and physics remain owned by the selected survivor.
    public sealed class BlockSurvivor : IDisposable
    {
        private readonly CharacterBody body;
        private readonly GameObject root;
        private readonly Material material;
        private readonly Texture2D skin;
        private readonly Dictionary<Renderer,bool> original=new Dictionary<Renderer,bool>();
        private readonly List<Mesh> meshes=new List<Mesh>();
        private Transform head,rightArm,leftArm,rightLeg,leftLeg;
        private Vector3 last;
        private float phase,swing;
        public BlockSurvivor(CharacterBody target,string path) : this(target,null,path) {}
        public BlockSurvivor(Transform display,string path) : this(null,display,path) {}
        private BlockSurvivor(CharacterBody target,Transform display,string path)
        {
            body=target;
            skin=new Texture2D(64,64,TextureFormat.RGBA32,false);
            if(!ImageConversion.LoadImage(skin,File.ReadAllBytes(path))) throw new InvalidDataException("Cannot load Minecraft skin.");
            skin.filterMode=FilterMode.Point;skin.wrapMode=TextureWrapMode.Clamp;
            var shader=Shader.Find("Standard") ?? Shader.Find("Unlit/Transparent Cutout");
            if(shader==null) throw new InvalidOperationException("No avatar shader available.");
            material=new Material(shader);material.mainTexture=skin;material.SetInt("_Cull",0);material.SetFloat("_Cutoff",.1f);
            material.SetFloat("_Mode",1);material.EnableKeyword("_ALPHATEST_ON");material.SetFloat("_Glossiness",0);material.SetFloat("_SpecularHighlights",0);material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");material.renderQueue=2450;
            root=new GameObject("RoRCraft player skin");
            if(display!=null) root.transform.SetParent(display,false);else UnityEngine.Object.DontDestroyOnLoad(root);
            // Keep the original bones/animator and item display system alive.
            var model=body==null || body.modelLocator==null || body.modelLocator.modelTransform==null ? null : body.modelLocator.modelTransform.GetComponent<CharacterModel>();
            if(model!=null) { foreach(var info in model.baseRendererInfos) if(info.renderer!=null) original[info.renderer]=info.renderer.enabled; }
            else if(body!=null && body.modelLocator!=null && body.modelLocator.modelTransform!=null)
                foreach(var r in body.modelLocator.modelTransform.GetComponentsInChildren<SkinnedMeshRenderer>()) original[r]=r.enabled;
            else if(display!=null) foreach(var r in display.GetComponentsInChildren<Renderer>()) original[r]=r.enabled;
            Part("Torso",new Vector3(0,1.125f,0),new Vector3(.5f,.75f,.25f),Vector3.zero,16,16,8,12,4,16,32);
            head=Part("Head",new Vector3(0,1.5f,0),new Vector3(.5f,.5f,.5f),new Vector3(0,.25f,0),0,0,8,8,8,32,0);
            rightArm=Part("Right arm",new Vector3(-.375f,1.5f,0),new Vector3(.25f,.75f,.25f),new Vector3(0,-.375f,0),40,16,4,12,4,40,32);
            leftArm=Part("Left arm",new Vector3(.375f,1.5f,0),new Vector3(.25f,.75f,.25f),new Vector3(0,-.375f,0),32,48,4,12,4,48,48);
            rightLeg=Part("Right leg",new Vector3(-.125f,.75f,0),new Vector3(.25f,.75f,.25f),new Vector3(0,-.375f,0),0,16,4,12,4,0,32);
            leftLeg=Part("Left leg",new Vector3(.125f,.75f,0),new Vector3(.25f,.75f,.25f),new Vector3(0,-.375f,0),16,48,4,12,4,0,48);
            last=body.footPosition;
        }
        private Transform Part(string name,Vector3 pivot,Vector3 size,Vector3 center,int u,int v,int w,int h,int d,int outerU,int outerV)
        {
            var part=new GameObject(name);part.transform.SetParent(root.transform,false);part.transform.localPosition=pivot;
            Box(part.transform,size,center,u,v,w,h,d);
            if(skin.height>=64) Box(part.transform,size+Vector3.one*.025f,center,outerU,outerV,w,h,d);
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
            int n=verts.Count;verts.Add(c+a);verts.Add(c+b);verts.Add(c+e);verts.Add(c+f);
            float left=u/(float)skin.width,right=(u+w)/(float)skin.width,top=1-v/(float)skin.height,bottom=1-(v+h)/(float)skin.height;
            uv.Add(new Vector2(left,bottom));uv.Add(new Vector2(right,bottom));uv.Add(new Vector2(right,top));uv.Add(new Vector2(left,top));
            tri.Add(n);tri.Add(n+1);tri.Add(n+2);tri.Add(n);tri.Add(n+2);tri.Add(n+3);
        }
        public void Tick()
        {
            foreach(var item in original) if(item.Key!=null) item.Key.enabled=false;
            if(body==null) return;
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
        public void Dispose()
        {
            foreach(var item in original) if(item.Key!=null) item.Key.enabled=item.Value;
            UnityEngine.Object.Destroy(root);foreach(var mesh in meshes) UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(material);UnityEngine.Object.Destroy(skin);
        }
    }
}
