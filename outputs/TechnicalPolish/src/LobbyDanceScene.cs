using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace RoRCraftPolish {
    // Minecraft 26.3 ParrotModel body layer + PARTY pose, converted with
    // C(x,y,z)=(x,-y,-z)/16. No entity/collider or gameplay inventory changes.
    internal sealed class LobbyDanceScene : IDisposable {
        private GameObject scene,bird,box;
        private Transform[] notes=new Transform[1];
        private Material noteTint;
        private readonly Color[] noteColors={new Color(1,.75f,.12f),new Color(.15f,.8f,1),new Color(1,.25f,.65f)};
        private Vector3 birdAt;
        private readonly List<Mesh> meshes=new List<Mesh>();
        private readonly List<Material> materials=new List<Material>();
        private readonly List<Texture2D> textures=new List<Texture2D>();
        private Transform head,body,tail,leftWing,rightWing,leftLeg,rightLeg;
        private Vector3 headAt,bodyAt,tailAt,leftWingAt,rightWingAt;
        internal bool Visible {get{return scene!=null && scene.activeSelf;}}
        internal LobbyDanceScene(Transform avatar,Transform torso,Material shader) {
            try {
                scene=new GameObject("RoRCraft Parrot Dance scene");scene.transform.SetParent(avatar,false);
                bird=new GameObject("Minecraft shoulder parrot");bird.transform.SetParent(torso,false);
                // Feet rest on the outside of the shoulder, not inside head/hat.
                bird.transform.localPosition=new Vector3(-.46f,.375f,.025f);
                birdAt=bird.transform.localPosition;bird.transform.localScale=Vector3.one*.60f;
                var feather=Asset("parrot_green.png",shader);
                body=Part(bird.transform,"body",new Vector3(0,16.5f,-3),new Vector3(.4937f,0,0),new Vector3(-1.5f,0,-1.5f),new Vector3(3,6,3),2,8,feather,true);
                tail=Part(bird.transform,"tail",new Vector3(0,21.07f,1.16f),new Vector3(1.015f,0,0),new Vector3(-1.5f,-1,-1),new Vector3(3,4,1),22,1,feather,true);
                leftWing=Part(bird.transform,"left_wing",new Vector3(1.5f,16.94f,-2.76f),new Vector3(-.6981f,-Mathf.PI,0),new Vector3(-.5f,0,-1.5f),new Vector3(1,5,3),19,8,feather,true);
                rightWing=Part(bird.transform,"right_wing",new Vector3(-1.5f,16.94f,-2.76f),new Vector3(-.6981f,-Mathf.PI,0),new Vector3(-.5f,0,-1.5f),new Vector3(1,5,3),19,8,feather,true);
                head=Part(bird.transform,"head",new Vector3(0,15.69f,-2.76f),Vector3.zero,new Vector3(-1,-1.5f,-1),new Vector3(2,3,2),2,2,feather,true);
                Part(head,"head2",new Vector3(0,-2,-1),Vector3.zero,new Vector3(-1,-.5f,-2),new Vector3(2,1,4),10,0,feather,false);
                Part(head,"beak1",new Vector3(0,-.5f,-1.5f),Vector3.zero,new Vector3(-.5f,-1,-.5f),new Vector3(1,2,1),11,7,feather,false);
                Part(head,"beak2",new Vector3(0,-1.75f,-2.45f),Vector3.zero,new Vector3(-.5f,0,-.5f),new Vector3(1,2,1),16,7,feather,false);
                Part(head,"feather",new Vector3(0,-2.15f,.15f),new Vector3(-.2214f,0,0),new Vector3(0,-4,-2),new Vector3(0,5,4),2,18,feather,false);
                leftLeg=Part(bird.transform,"left_leg",new Vector3(1,22,-1.05f),new Vector3(-.0299f,0,0),new Vector3(-.5f,0,-.5f),new Vector3(1,2,1),14,18,feather,true);
                rightLeg=Part(bird.transform,"right_leg",new Vector3(-1,22,-1.05f),new Vector3(-.0299f,0,0),new Vector3(-.5f,0,-.5f),new Vector3(1,2,1),14,18,feather,true);
                headAt=head.localPosition;bodyAt=body.localPosition;tailAt=tail.localPosition;leftWingAt=leftWing.localPosition;rightWingAt=rightWing.localPosition;
                box=new GameObject("Minecraft jukebox");box.transform.SetParent(scene.transform,false);
                box.transform.localPosition=new Vector3(.62f,.5f,-1.0f);
                var sides=Asset("jukebox_side.png",shader);var top=Asset("jukebox_top.png",shader);
                var points=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();
                Quad(points,uv,tri,new Vector3(-.5f,-.5f,.5f),new Vector3(.5f,-.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,.5f,.5f),0,0,16,16,sides.mainTexture as Texture2D);
                Quad(points,uv,tri,new Vector3(.5f,-.5f,-.5f),new Vector3(-.5f,-.5f,-.5f),new Vector3(-.5f,.5f,-.5f),new Vector3(.5f,.5f,-.5f),0,0,16,16,sides.mainTexture as Texture2D);
                Quad(points,uv,tri,new Vector3(-.5f,-.5f,-.5f),new Vector3(-.5f,-.5f,.5f),new Vector3(-.5f,.5f,.5f),new Vector3(-.5f,.5f,-.5f),0,0,16,16,sides.mainTexture as Texture2D);
                Quad(points,uv,tri,new Vector3(.5f,-.5f,.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(.5f,.5f,.5f),0,0,16,16,sides.mainTexture as Texture2D);
                Quad(points,uv,tri,new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,-.5f,.5f),new Vector3(-.5f,-.5f,.5f),0,0,16,16,sides.mainTexture as Texture2D);
                Mesh(box.transform,"Jukebox sides",points,uv,tri,sides);
                points.Clear();uv.Clear();tri.Clear();
                Quad(points,uv,tri,new Vector3(-.5f,.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(.5f,.5f,-.5f),new Vector3(-.5f,.5f,-.5f),0,0,16,16,top.mainTexture as Texture2D);
                Mesh(box.transform,"Jukebox top",points,uv,tri,top);
                var noteMaterial=Asset("note.png",shader);var noteTexture=noteMaterial.mainTexture as Texture2D;
                // Use the game's already-loaded HG emission path: stripped builds
                // do not contain Unity's optional Unlit/Color shader.
                noteMaterial.SetTexture("_EmTex",Texture2D.whiteTexture);
                noteMaterial.SetFloat("_EmPower",.12f);
                for(int i=0;i<notes.Length;i++) {
                    var colored=noteMaterial;noteTint=colored;
                    colored.SetColor("_Color",noteColors[i]);colored.SetColor("_EmColor",noteColors[i]);
                    var note=new GameObject("Beat note "+i);note.transform.SetParent(scene.transform,false);notes[i]=note.transform;
                    points.Clear();uv.Clear();tri.Clear();
                    float half=.11f;
                    Face(points,uv,tri,new Vector3(-half,0,0),new Vector3(half,0,0),new Vector3(half,2*half,0),new Vector3(-half,2*half,0),0,0,noteTexture.width,noteTexture.height,noteTexture);
                    Face(points,uv,tri,new Vector3(half,0,0),new Vector3(-half,0,0),new Vector3(-half,2*half,0),new Vector3(half,2*half,0),0,0,noteTexture.width,noteTexture.height,noteTexture);
                    Mesh(note.transform,"Minecraft note",points,uv,tri,colored);
                }
                SetLayer(scene.transform,avatar.gameObject.layer);SetLayer(bird.transform,avatar.gameObject.layer);
                scene.SetActive(false);bird.SetActive(false);
            } catch {Dispose();throw;}
        }
        private Material Asset(string name,Material shader) {
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);textures.Add(texture);
            using(var input=typeof(LobbyDanceScene).Assembly.GetManifestResourceStream("MinecraftUi."+name)) {
                if(input==null)throw new InvalidDataException("Dance texture missing: "+name);
                using(var copy=new MemoryStream()){input.CopyTo(copy);if(!ImageConversion.LoadImage(texture,copy.ToArray()))throw new InvalidDataException("Dance texture decode: "+name);}
            }
            texture.name="RoRCraft dance texture "+name;texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
            var material=new Material(shader);materials.Add(material);material.mainTexture=texture;material.name="RoRCraft dance material "+name;
            // Minecraft RenderPipelines.ENTITY_CUTOUT uses withCull(false).
            // Leg texture has transparent front faces and pink rear/sole:
            // backface culling made the feet vanish and the bird look afloat.
            if(name.StartsWith("parrot_"))material.SetInt("_Cull",2);
            return material;
        }
        private static Vector3 Convert(Vector3 v){return new Vector3(v.x,-v.y,-v.z)/16;}
        // A 180deg rotation about X maps MC basis to Unity, preserving handedness.
        private static Quaternion Rotation(float x,float y,float z){return Quaternion.AngleAxis(-z*Mathf.Rad2Deg,Vector3.forward)*Quaternion.AngleAxis(-y*Mathf.Rad2Deg,Vector3.up)*Quaternion.AngleAxis(x*Mathf.Rad2Deg,Vector3.right);}
        private Transform Part(Transform parent,string name,Vector3 pivot,Vector3 angle,Vector3 start,Vector3 dimensions,int u,int v,Material material,bool rootPart) {
            var part=new GameObject(name);part.transform.SetParent(parent,false);part.transform.localPosition=Convert(pivot)+(rootPart?Vector3.up*1.5f:Vector3.zero);part.transform.localRotation=Rotation(angle.x,angle.y,angle.z);
            var size=new Vector3(dimensions.x,dimensions.y,dimensions.z)/16;
            var center=Convert(start+dimensions*.5f);float x=size.x*.5f,y=size.y*.5f,z=size.z*.5f;
            int w=(int)dimensions.x,h=(int)dimensions.y,d=(int)dimensions.z;
            var p=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();var tex=material.mainTexture as Texture2D;
            // UV face order matches MC Cube: -Z front maps to Unity +Z.
            Face(p,uv,tri,center+new Vector3(-x,-y,z),center+new Vector3(x,-y,z),center+new Vector3(x,y,z),center+new Vector3(-x,y,z),u+d,v+d,w,h,tex);
            Face(p,uv,tri,center+new Vector3(x,-y,-z),center+new Vector3(-x,-y,-z),center+new Vector3(-x,y,-z),center+new Vector3(x,y,-z),u+2*d+w,v+d,w,h,tex);
            Face(p,uv,tri,center+new Vector3(-x,-y,-z),center+new Vector3(-x,-y,z),center+new Vector3(-x,y,z),center+new Vector3(-x,y,-z),u,v+d,d,h,tex);
            Face(p,uv,tri,center+new Vector3(x,-y,z),center+new Vector3(x,-y,-z),center+new Vector3(x,y,-z),center+new Vector3(x,y,z),u+d+w,v+d,d,h,tex);
            Face(p,uv,tri,center+new Vector3(-x,y,z),center+new Vector3(x,y,z),center+new Vector3(x,y,-z),center+new Vector3(-x,y,-z),u+d,v,w,d,tex);
            Face(p,uv,tri,center+new Vector3(-x,-y,-z),center+new Vector3(x,-y,-z),center+new Vector3(x,-y,z),center+new Vector3(-x,-y,z),u+d+w,v,w,d,tex,true);
            Mesh(part.transform,"Minecraft model cube",p,uv,tri,material);return part.transform;
        }
        private void Face(List<Vector3> p,List<Vector2> uv,List<int> tri,Vector3 a,Vector3 b,Vector3 c,Vector3 d,int u,int v,int w,int h,Texture2D tex,bool flipV=false) {
            // Geometry alpha mask avoids black transparent feather/beak texels
            // in HG deferred passes, including both sides of the zero-width crest.
            for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
                int row=flipV?y:h-y-1;
                if(tex.GetPixel(u+x,tex.height-v-row-1).a<.1f)continue;
                var horizontal=b-a;var vertical=d-a;var corner=a+horizontal*(x/(float)w)+vertical*(y/(float)h);
                Quad(p,uv,tri,corner,corner+horizontal/w,corner+horizontal/w+vertical/h,corner+vertical/h,u+x,v+row,1,1,tex);
            }
        }
        private static void Quad(List<Vector3> p,List<Vector2> uv,List<int> tri,Vector3 a,Vector3 b,Vector3 c,Vector3 d,int u,int v,int w,int h,Texture2D tex) {
            int n=p.Count;p.Add(a);p.Add(b);p.Add(c);p.Add(d);
            float l=u/(float)tex.width,r=(u+w)/(float)tex.width,t=1-v/(float)tex.height,bottom=1-(v+h)/(float)tex.height;
            uv.Add(new Vector2(l,bottom));uv.Add(new Vector2(r,bottom));uv.Add(new Vector2(r,t));uv.Add(new Vector2(l,t));
            tri.Add(n);tri.Add(n+1);tri.Add(n+2);tri.Add(n);tri.Add(n+2);tri.Add(n+3);
        }
        private void Mesh(Transform parent,string name,List<Vector3> p,List<Vector2> uv,List<int> tri,Material material) {
            var mesh=new Mesh();meshes.Add(mesh);mesh.name="RoRCraft dance mesh";mesh.vertices=p.ToArray();mesh.uv=uv.ToArray();mesh.triangles=tri.ToArray();mesh.RecalculateNormals();
            if(name=="Minecraft model cube") {
                // HG passes may hard-code CullBack, ignoring _Cull. Explicit
                // reversed winding reproduces MC's two-sided entity cutout.
                // Preserve face normals before adding back sides (no cancelling normals).
                int n=tri.Count;var both=new int[n*2];tri.CopyTo(both);
                for(int i=0;i<n;i+=3){both[n+i]=tri[i+2];both[n+i+1]=tri[i+1];both[n+i+2]=tri[i];}
                mesh.triangles=both;
            }
            mesh.RecalculateBounds();
            var child=new GameObject(name);child.transform.SetParent(parent,false);child.AddComponent<MeshFilter>().sharedMesh=mesh;child.AddComponent<MeshRenderer>().sharedMaterial=material;
        }
        private static void SetLayer(Transform t,int layer){foreach(var child in t.GetComponentsInChildren<Transform>(true))child.gameObject.layer=layer;}
        internal void Update(bool dancing,float seconds) {
            if(scene==null || bird==null)return;
            if(scene.activeSelf!=dancing)scene.SetActive(dancing);if(bird.activeSelf!=dancing)bird.SetActive(dancing);
            if(!dancing)return;
            // Grounded vanilla PARTY pose; no flying flap or unrelated body rock.
            float blend=ParrotDanceMotion.Blend(seconds),bounce=ParrotDanceMotion.Bounce(seconds);
            bird.transform.localPosition=birdAt;
            bird.transform.localRotation=Quaternion.identity;
            var offset=ParrotDanceMotion.Orbit(seconds)*blend;
            // One quiet note at a time; change colour only when the next note starts.
            float noteAge=Mathf.Max(0,seconds-ParrotDanceMotion.BeatOffset);
            float interval=ParrotDanceMotion.BeatSeconds*2;
            float t=Mathf.Repeat(noteAge,interval)/interval;
            int colorIndex=Mathf.FloorToInt(noteAge/interval)%noteColors.Length;
            noteTint.SetColor("_Color",noteColors[colorIndex]);noteTint.SetColor("_EmColor",noteColors[colorIndex]);
            notes[0].localPosition=box.transform.localPosition+new Vector3(0,.6f+t*.45f,.12f);
            notes[0].localScale=Vector3.one*(Mathf.Min(1,t*8)*Mathf.Min(1,(1-t)*4)*blend);
            box.transform.localScale=Vector3.one;
            head.localPosition=headAt+offset;body.localPosition=bodyAt+offset;tail.localPosition=tailAt+offset;
            leftWing.localPosition=leftWingAt+offset;rightWing.localPosition=rightWingAt+offset;
            head.localRotation=Rotation(0,0,ParrotDanceMotion.Beat(seconds)*.4f*blend);
            leftWing.localRotation=Rotation(-.6981f,-Mathf.PI,-.0873f);
            rightWing.localRotation=Rotation(-.6981f,-Mathf.PI,.0873f);
            leftLeg.localRotation=Rotation(-.0299f,0,-.34906584f);rightLeg.localRotation=Rotation(-.0299f,0,.34906584f);
        }
        public void Dispose(){if(scene!=null){scene.SetActive(false);UnityEngine.Object.Destroy(scene);}if(bird!=null){bird.SetActive(false);UnityEngine.Object.Destroy(bird);}foreach(var mesh in meshes)UnityEngine.Object.Destroy(mesh);foreach(var material in materials)UnityEngine.Object.Destroy(material);foreach(var texture in textures)UnityEngine.Object.Destroy(texture);meshes.Clear();materials.Clear();textures.Clear();}
    }
}
