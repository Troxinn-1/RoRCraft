using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using RoR2;

namespace RoRCraft
{
    public sealed class SkyRenderer : IDisposable
    {
        private readonly Dictionary<int,Material> materials = new Dictionary<int,Material>();
        private readonly Dictionary<string,GameObject> sections = new Dictionary<string,GameObject>();
        private readonly List<GameObject> scene = new List<GameObject>();
        private readonly List<GameObject> player = new List<GameObject>();
        private Vector3 playerPosition;
        private sealed class MeshBuffers {
            public Vector3[] vertices;public Vector2[] uv;public Color32[] colors;
            public readonly List<int> opaque=new List<int>(),transparent=new List<int>();
        }
        private readonly Dictionary<GameObject,MeshBuffers> buffers=new Dictionary<GameObject,MeshBuffers>();
        private readonly GameObject root = new GameObject("RoRCraft Minecraft geometry");
        public Texture2D Hud;
        public bool HudBottomUp;
        public Vector3 WorldOrigin {get {return root.transform.position;} set {root.transform.position=value;}}
        public int SectionCount { get { return sections.Count; } }

        public SkyRenderer() { UnityEngine.Object.DontDestroyOnLoad(root); root.SetActive(false); }
        public void Visible(bool visible) { if(root!=null) root.SetActive(visible); }
        public bool OwnsCollider(Collider collider) {return root!=null && collider!=null && collider.transform.IsChildOf(root.transform);}
        public void ClearBlocks() {foreach(var go in sections.Values) DestroyMesh(go);sections.Clear();}
        public void PlayerPosition(Vector3 feet) {playerPosition=feet;foreach(var go in player) go.transform.localPosition=new Vector3(-feet.x,feet.y,feet.z);}
        private Material MaterialFor(int id,bool translucent=false)
        {
            Material m;
            int key=id*2+(translucent?1:0);
            if (materials.TryGetValue(key,out m)) return m;
            var shader=translucent ? Shader.Find("Sprites/Default") : (Shader.Find("Hopoo Games/Deferred/Standard") ?? Shader.Find("Standard") ?? Shader.Find("Unlit/Transparent Cutout"));
            if (shader==null) throw new InvalidOperationException("No compatible Unity shader available.");
            m=new Material(shader);m.SetInt("_ZTest",4);m.SetInt("_Cull",translucent?0:2);m.SetInt("_ZWrite",translucent?0:1);
            if(m.HasProperty("_Color")) m.SetColor("_Color",Color.white);
            if(m.HasProperty("_EmColor")) m.SetColor("_EmColor",Color.black);
            if(m.HasProperty("_EmPower")) m.SetFloat("_EmPower",0);
            if(m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness",0);
            if(m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness",0);
            if(m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights",0);
            if(m.HasProperty("_GlossyReflections")) m.SetFloat("_GlossyReflections",0);
            if(!translucent) {m.SetFloat("_Mode",1);m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");m.EnableKeyword("_GLOSSYREFLECTIONS_OFF");}
            if(m.HasProperty("_Metallic")) m.SetFloat("_Metallic",0);
            Debug.Log("RoRCraft material "+id+" translucent="+translucent+" shader="+shader.name);
            m.SetFloat("_Cutoff",.5f);m.renderQueue=translucent?3000:2450;
            if(!translucent) {m.EnableKeyword("_ALPHATEST_ON");m.DisableKeyword("_ALPHABLEND_ON");m.SetInt("_SrcBlend",1);m.SetInt("_DstBlend",0);}
            materials[key]=m;if(translucent) m.mainTexture=MaterialFor(id).mainTexture;return m;
        }
        private void Texture(int id,int w,int h,byte[] bytes,int offset)
        {
            if (w<1 || h<1 || w>16384 || h>16384 || (long)w*h*4!=bytes.Length-offset) throw new InvalidDataException("Invalid atlas dimensions.");
            var m=MaterialFor(id); if (m.mainTexture!=null) UnityEngine.Object.Destroy(m.mainTexture);
            var texture=new Texture2D(w,h,TextureFormat.RGBA32,false); texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
            var pixels=new byte[w*h*4];
            // SkyCraft atlas starts with its top row; Unity texture row zero is the bottom.
            for(int row=0;row<h;row++) Buffer.BlockCopy(bytes,offset+row*w*4,pixels,(h-1-row)*w*4,w*4);
            texture.LoadRawTextureData(pixels); texture.Apply(false,false); m.mainTexture=texture;
            Material transparent;if(materials.TryGetValue(id*2+1,out transparent)) transparent.mainTexture=texture;
        }
        private GameObject Build(string name,byte[] bytes,int offset,int count,Vector3 origin,int texture,bool solid=false,GameObject reuse=null)
        {
            if(count<0 || count>2000000 || count%3!=0 || (long)offset+count*32>bytes.Length) throw new InvalidDataException("Invalid vertex count.");
            var go=reuse;
            if(go==null) {
                go=new GameObject(name);go.transform.SetParent(root.transform,false);
                var created=new Mesh();created.indexFormat=IndexFormat.UInt32;if(!solid) created.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh=created;go.AddComponent<MeshRenderer>();buffers[go]=new MeshBuffers();
                if(solid) {go.layer=LayerIndex.world.intVal;go.AddComponent<MeshCollider>();}
            }
            go.SetActive(true);go.transform.localPosition=new Vector3(-origin.x,origin.y,origin.z);
            var mesh=go.GetComponent<MeshFilter>().sharedMesh;var data=buffers[go];
            if(data.vertices==null || data.vertices.Length!=count) {data.vertices=new Vector3[count];data.uv=new Vector2[count];data.colors=new Color32[count];}
            var vertices=data.vertices;var uv=data.uv;var colors=data.colors;
            for(int i=0;i<count;i++) {
                int p=offset+i*32;
                vertices[i]=new Vector3(-BitConverter.ToSingle(bytes,p),BitConverter.ToSingle(bytes,p+4),BitConverter.ToSingle(bytes,p+8));
                uv[i]=new Vector2(BitConverter.ToSingle(bytes,p+12),1-BitConverter.ToSingle(bytes,p+16));
                colors[i]=new Color32(bytes[p+20],bytes[p+21],bytes[p+22],bytes[p+23]);
            }
            // The imported camera/world changes handedness, so reverse the triangle winding.
            var opaque=data.opaque;var transparent=data.transparent;opaque.Clear();transparent.Clear();
            for(int i=0;i<count;i+=3) {
                var target=(BitConverter.ToInt32(bytes,offset+i*32+28)&2)!=0 ? transparent:opaque;
                target.Add(i);target.Add(i+2);target.Add(i+1);
            }
            mesh.Clear();mesh.vertices=vertices;mesh.uv=uv;mesh.colors32=colors;mesh.subMeshCount=2;
            mesh.SetTriangles(opaque,0);mesh.SetTriangles(transparent,1);mesh.RecalculateBounds();mesh.RecalculateNormals();
            // Standard's pass has fixed back-face culling, regardless of a material _Cull value.
            // Keep normals from the original faces, then submit both windings for depth-writing surfaces.
            int faceIndices=opaque.Count;
            for(int i=0;i<faceIndices;i+=3) {opaque.Add(opaque[i]);opaque.Add(opaque[i+2]);opaque.Add(opaque[i+1]);}
            mesh.SetTriangles(opaque,0);
            var meshRenderer=go.GetComponent<MeshRenderer>();var opaqueMaterial=MaterialFor(texture);var blendedMaterial=MaterialFor(texture,true);
            if(meshRenderer.sharedMaterial!=opaqueMaterial) meshRenderer.sharedMaterials=new[]{opaqueMaterial,blendedMaterial};
            if(solid) {var collider=go.GetComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=mesh;}
            return go;
        }
        private void DestroyMesh(GameObject go)
        {
            if(go==null) return;go.SetActive(false);buffers.Remove(go);var filter=go.GetComponent<MeshFilter>();
            if(filter!=null && filter.sharedMesh!=null) UnityEngine.Object.Destroy(filter.sharedMesh);
            UnityEngine.Object.Destroy(go);
        }
        public void Consume(int type,byte[] b)
        {
            if(type==1 && b.Length>=8) Texture(0,BitConverter.ToInt32(b,0),BitConverter.ToInt32(b,4),b,8);
            else if(type==4 && b.Length>=16) Texture(BitConverter.ToInt32(b,0),BitConverter.ToInt32(b,4),BitConverter.ToInt32(b,8),b,16);
            else if(type==2 && b.Length>=16) {
                int x=BitConverter.ToInt32(b,0),y=BitConverter.ToInt32(b,4),z=BitConverter.ToInt32(b,8),n=BitConverter.ToInt32(b,12);
                string key=x+","+y+","+z; GameObject old;
                if(sections.TryGetValue(key,out old)) { DestroyMesh(old); sections.Remove(key); }
                if(n>0) sections[key]=Build("RoRCraft section "+key,b,16,n,new Vector3(x*16,y*16,z*16),0,true);
            } else if(type==3) {
                ClearBlocks();
                foreach(var go in scene) go.SetActive(false);foreach(var go in player) go.SetActive(false);
            } else if((type==6 && b.Length>=32) || (type==5 && b.Length>=8)) {
                var list=type==5?player:scene;int header=type==5?8:32;
                var origin=type==5?playerPosition:new Vector3((float)BitConverter.ToDouble(b,0),(float)BitConverter.ToDouble(b,8),(float)BitConverter.ToDouble(b,16));
                int batches=BitConverter.ToInt32(b,header-8), vertices=BitConverter.ToInt32(b,header-4);
                if(batches<0 || batches>4096 || vertices<0 || header+batches*16L+vertices*32L>b.Length) throw new InvalidDataException("Invalid scene batches.");
                int vertexStart=header+batches*16;
                for(int i=0;i<batches;i++) {
                    int p=header+i*16,texture=BitConverter.ToInt32(b,p),first=BitConverter.ToInt32(b,p+4),count=BitConverter.ToInt32(b,p+8);
                    if(first<0 || count<0 || first+count>vertices) throw new InvalidDataException("Invalid scene range.");
                    GameObject reused=i<list.Count?list[i]:null;
                    if(count>0) {var built=Build("RoRCraft "+type+" batch "+i,b,vertexStart+first*32,count,origin,texture,false,reused);if(i<list.Count) list[i]=built;else list.Add(built);}
                    else if(reused!=null) reused.SetActive(false);
                }
                for(int i=batches;i<list.Count;i++) list[i].SetActive(false);
            }
        }
        public void Overlay(byte[] rgba,int w,int h,bool bottomUp)
        {
            if(Hud==null || Hud.width!=w || Hud.height!=h) {
                if(Hud!=null) UnityEngine.Object.Destroy(Hud);
                Hud=new Texture2D(w,h,TextureFormat.RGBA32,false); Hud.filterMode=FilterMode.Bilinear;
            }
            Hud.LoadRawTextureData(rgba); Hud.Apply(false,false); HudBottomUp=bottomUp;
        }
        public void Dispose()
        {
            foreach(var go in sections.Values) DestroyMesh(go); foreach(var go in scene) DestroyMesh(go);
            foreach(var go in player) DestroyMesh(go);
            var textures=new HashSet<Texture>();foreach(var m in materials.Values) {if(m.mainTexture!=null && textures.Add(m.mainTexture)) UnityEngine.Object.Destroy(m.mainTexture);UnityEngine.Object.Destroy(m);}
            if(Hud!=null) UnityEngine.Object.Destroy(Hud); UnityEngine.Object.Destroy(root);
        }
    }
}
