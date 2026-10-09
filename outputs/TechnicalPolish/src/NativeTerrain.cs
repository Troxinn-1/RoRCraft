using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using RoRCraft;
using RoR2;
using UnityEngine;

namespace RoRCraftPolish {
    // Preserve the baseline readiness/voxel packets, then replace its height-field
    // triangles with the actual nearby readable native collision meshes and boxes.
    // A downward height field cannot represent ground beneath a bridge or a ceiling.
    internal static class NativeTerrain {
        private sealed class Geometry {
            public Mesh mesh; public Matrix4x4 matrix; public Vector3[] world;
            public int[] indices;
        }
        private sealed class Region {
            public readonly List<Vector3> points=new List<Vector3>();
            public byte[] packet;
        }
        private static readonly Collider[] nearby=new Collider[512];
        private static readonly Dictionary<Collider,Geometry> cache=new Dictionary<Collider,Geometry>();
        private static readonly Dictionary<Vector3Int,Region> regions=new Dictionary<Vector3Int,Region>();
        private static readonly HashSet<Vector3Int> unsupported=new HashSet<Vector3Int>();
        private static readonly List<Vector3Int> prune=new List<Vector3Int>();
        private static readonly Dictionary<Vector3Int,byte[]> fallback=new Dictionary<Vector3Int,byte[]>();
        [ThreadStatic] private static bool capturingBaseline;
        private static readonly int[] boxIndices={0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5};
        private static ManualLogSource log;
        private static int previousEpoch=-1;
        private static Vector3Int low,high;
        private static bool reported,overflowReported;
        public static long Exports,Triangles,FailedPackets;
        public static void Install(Harmony harmony,ManualLogSource logger,bool enabled) {
            if(!enabled) return;log=logger;
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("RoRCraft.RoRCraftPlugin"),"ExportFloor"),prefix:new HarmonyMethod(typeof(NativeTerrain),"Begin"),postfix:new HarmonyMethod(typeof(NativeTerrain),"Export"),finalizer:new HarmonyMethod(typeof(NativeTerrain),"Finish"));
            harmony.Patch(AccessTools.Method(typeof(SkyMemory),"CollisionMessage"),prefix:new HarmonyMethod(typeof(NativeTerrain),"CaptureBaseline"));
        }
        private static void Begin() {capturingBaseline=true;fallback.Clear();}
        private static void Finish() {capturingBaseline=false;}
        private static bool CaptureBaseline(int type,byte[] data,ref bool __result) {
            if(!capturingBaseline || type!=3 || data==null || data.Length<32) return true;
            var key=new Vector3Int(BitConverter.ToInt32(data,0)/8,BitConverter.ToInt32(data,4)/8,BitConverter.ToInt32(data,8)/8);
            fallback[key]=data;__result=true;return false;
        }
        private static void PublishFallback(SkyMemory link) {foreach(var packet in fallback.Values) if(!link.CollisionMessage(3,packet)) FailedPackets++;}
        public static void Clear() {cache.Clear();regions.Clear();unsupported.Clear();previousEpoch=-1;reported=false;overflowReported=false;}
        private static int Cell(float v) {return (int)Math.Floor(v/8.0);}
        private static Vector3 Mc(Vector3 world,Vector3 origin) {return new Vector3(origin.x-world.x,world.y-origin.y,world.z-origin.z);}
        private static Region Get(Vector3Int key) {Region r;if(!regions.TryGetValue(key,out r)) {r=new Region();regions.Add(key,r);}return r;}
        private static Geometry Read(Collider collider) {
            var matrix=collider.transform.localToWorldMatrix;var meshCollider=collider as MeshCollider;var box=collider as BoxCollider;
            var capsule=collider as CapsuleCollider;var sphere=collider as SphereCollider;
            Mesh mesh=meshCollider==null?null:meshCollider.sharedMesh;
            if(meshCollider!=null && (mesh==null || !mesh.isReadable)) return null;
            Geometry g;
            if(cache.TryGetValue(collider,out g) && g.mesh==mesh && g.matrix==matrix && box==null) return g;
            Vector3[] local;bool worldSpace=false;
            if(meshCollider!=null) {local=mesh.vertices;g=new Geometry {indices=mesh.triangles};}
            else if(box!=null) {
                var a=box.center-box.size*.5f;var b=box.center+box.size*.5f;
                local=new[]{new Vector3(a.x,a.y,a.z),new Vector3(b.x,a.y,a.z),new Vector3(b.x,b.y,a.z),new Vector3(a.x,b.y,a.z),new Vector3(a.x,a.y,b.z),new Vector3(b.x,a.y,b.z),new Vector3(b.x,b.y,b.z),new Vector3(a.x,b.y,b.z)};
                g=new Geometry {indices=boxIndices};
            } else if(capsule!=null || sphere!=null) {
                const int sides=32,hemisphereSteps=8;
                var scale=collider.transform.lossyScale;scale=new Vector3(Math.Abs(scale.x),Math.Abs(scale.y),Math.Abs(scale.z));
                int direction=capsule==null?1:capsule.direction;
                var axis=collider.transform.rotation*(direction==0?Vector3.right:direction==1?Vector3.up:Vector3.forward);
                var u=collider.transform.rotation*(direction==0?Vector3.up:Vector3.right);var v=Vector3.Cross(axis,u);
                float radius=capsule==null?sphere.radius*Math.Max(scale.x,Math.Max(scale.y,scale.z)):capsule.radius*(direction==0?Math.Max(scale.y,scale.z):direction==1?Math.Max(scale.x,scale.z):Math.Max(scale.x,scale.y));
                float height=capsule==null?2*radius:capsule.height*(direction==0?scale.x:direction==1?scale.y:scale.z);
                float half=Math.Max(0,height*.5f-radius);
                var center=matrix.MultiplyPoint3x4(capsule==null?sphere.center:capsule.center);
                int rings=2*(hemisphereSteps+1);local=new Vector3[rings*sides];
                var indices=new int[(rings-1)*sides*6];int index=0;
                for(int ring=0;ring<rings;ring++) {
                    double theta=ring<=hemisphereSteps?-Math.PI*.5+ring*Math.PI*.5/hemisphereSteps:(ring-hemisphereSteps-1)*Math.PI*.5/hemisphereSteps;
                    float y=(float)Math.Sin(theta)*radius+(ring<=hemisphereSteps?-half:half);
                    float r=(float)Math.Cos(theta)*radius;
                    for(int side=0;side<sides;side++) {
                        double angle=side*2*Math.PI/sides;
                        local[ring*sides+side]=center+axis*y+u*((float)Math.Cos(angle)*r)+v*((float)Math.Sin(angle)*r);
                        if(ring==rings-1) continue;
                        int a=ring*sides+side,b=ring*sides+(side+1)%sides,c=b+sides,d=a+sides;
                        indices[index++]=a;indices[index++]=b;indices[index++]=c;indices[index++]=a;indices[index++]=c;indices[index++]=d;
                    }
                }
                g=new Geometry {indices=indices};worldSpace=true;
            } else return null;
            g.mesh=mesh;g.matrix=matrix;g.world=new Vector3[local.Length];
            for(int i=0;i<local.Length;i++) g.world[i]=worldSpace?local[i]:matrix.MultiplyPoint3x4(local[i]);
            cache[collider]=g;return g;
        }
        private static void Bounds(Vector3 a,Vector3 b,Vector3 c,out Vector3Int lo,out Vector3Int hi) {
            var min=Vector3.Min(a,Vector3.Min(b,c));var max=Vector3.Max(a,Vector3.Max(b,c));
            lo=new Vector3Int(Math.Max(low.x,Cell(min.x-.001f)),Math.Max(low.y,Cell(min.y-.001f)),Math.Max(low.z,Cell(min.z-.001f)));
            hi=new Vector3Int(Math.Min(high.x,Cell(max.x+.001f)),Math.Min(high.y,Cell(max.y+.001f)),Math.Min(high.z,Cell(max.z+.001f)));
        }
        private static unsafe void Export(Vector3 ___position,Vector3 ___worldOrigin,int ___epoch,SkyRenderer ___renderer,SkyMemory ___link,CharacterBody ___controlled) {
            capturingBaseline=false;
            if(___link==null || ___renderer==null) return;
            if(previousEpoch!=___epoch) {Clear();previousEpoch=___epoch;}
            foreach(var r in regions.Values) r.points.Clear();unsupported.Clear();
            var center=Mc(___position,___worldOrigin);
            low=new Vector3Int(Cell(center.x-12),Cell(center.y-12),Cell(center.z-12));
            high=new Vector3Int(Cell(center.x+12),Cell(center.y+12),Cell(center.z+12));
            if(regions.Count>1024) {
                prune.Clear();foreach(var key in regions.Keys) if(key.x<low.x || key.x>high.x || key.y<low.y || key.y>high.y || key.z<low.z || key.z>high.z) prune.Add(key);
                foreach(var key in prune) regions.Remove(key);prune.Clear();
            }
            int found=Physics.OverlapBoxNonAlloc(___position,new Vector3(13,13,13),nearby,Quaternion.identity,LayerIndex.world.mask|1,QueryTriggerInteraction.Ignore);
            if(found==nearby.Length) {if(!overflowReported) {log.LogWarning("Native collider query full; keeping baseline triangle stream rather than publishing incomplete geometry.");overflowReported=true;}PublishFallback(___link);return;}
            int meshes=0,triangles=0;
            for(int n=0;n<found;n++) {
                var collider=nearby[n];
                if(collider==null || !collider.enabled || ___renderer.OwnsCollider(collider) || collider.GetComponentInParent<CharacterBody>()!=null || ___controlled!=null && collider.transform.IsChildOf(___controlled.transform)) continue;
                var g=Read(collider);
                if(g==null) {
                    if(!reported) log.LogInfo("Unsupported native collider: "+collider.GetType().Name+" "+collider.name+" parent="+(collider.transform.parent==null?"none":collider.transform.parent.name)+" bounds="+collider.bounds);
                    // Retain baseline coverage for unsupported native primitives.
                    var a=Mc(collider.bounds.min,___worldOrigin);var b=Mc(collider.bounds.max,___worldOrigin);Vector3Int lo,hi;
                    Bounds(a,b,a,out lo,out hi);
                    for(int x=lo.x;x<=hi.x;x++) for(int y=lo.y;y<=hi.y;y++) for(int z=lo.z;z<=hi.z;z++) unsupported.Add(new Vector3Int(x,y,z));
                    continue;
                }
                meshes++;
                for(int i=0;i+2<g.indices.Length;i+=3) {
                    var a=Mc(g.world[g.indices[i]],___worldOrigin);
                    // Reflecting X changes handedness, so reverse triangle winding.
                    var b=Mc(g.world[g.indices[i+2]],___worldOrigin);var c=Mc(g.world[g.indices[i+1]],___worldOrigin);
                    Vector3Int lo,hi;Bounds(a,b,c,out lo,out hi);
                    if(lo.x>hi.x || lo.y>hi.y || lo.z>hi.z || Vector3.Cross(b-a,c-a).sqrMagnitude<1e-10f) continue;
                    if(!reported) {
                        float ux=b.x-a.x,uz=b.z-a.z,vx=c.x-a.x,vz=c.z-a.z;
                        float determinant=ux*vz-uz*vx;
                        if(Math.Abs(determinant)>1e-5f) {
                            float px=center.x-a.x,pz=center.z-1.5f-a.z;
                            float u=(px*vz-pz*vx)/determinant,v=(ux*pz-uz*px)/determinant;
                            if(u>=0 && v>=0 && u+v<=1) log.LogInfo("Native surface at test column: "+collider.name+" MC y="+(a.y+u*(b.y-a.y)+v*(c.y-a.y))+" normal="+Vector3.Cross(b-a,c-a).normalized);
                        }
                    }
                    triangles++;
                    for(int x=lo.x;x<=hi.x;x++) for(int y=lo.y;y<=hi.y;y++) for(int z=lo.z;z<=hi.z;z++) {
                        var points=Get(new Vector3Int(x,y,z)).points;points.Add(a);points.Add(b);points.Add(c);
                    }
                }
            }
            if(meshes==0) {PublishFallback(___link);return;}
            foreach(var key in unsupported) {byte[] packet;if(fallback.TryGetValue(key,out packet) && !___link.CollisionMessage(3,packet)) FailedPackets++;}
            // Empty regions explicitly replace stale height-field triangles too.
            for(int x=low.x;x<=high.x;x++) for(int y=low.y;y<=high.y;y++) for(int z=low.z;z<=high.z;z++) {
                var key=new Vector3Int(x,y,z);if(unsupported.Contains(key)) continue;
                var r=Get(key);int length=32+r.points.Count/3*40;
                if(r.packet==null || r.packet.Length!=length) r.packet=new byte[length];
                fixed(byte* p=r.packet) {
                    *(int*)p=x*8;*(int*)(p+4)=y*8;*(int*)(p+8)=z*8;
                    *(int*)(p+12)=x*8+7;*(int*)(p+16)=y*8+7;*(int*)(p+20)=z*8+7;*(int*)(p+24)=___epoch;*(int*)(p+28)=r.points.Count/3;
                    int offset=32;
                    for(int i=0;i<r.points.Count;i+=3) {
                        for(int j=0;j<3;j++) {var v=r.points[i+j];*(float*)(p+offset+j*12)=v.x;*(float*)(p+offset+j*12+4)=v.y;*(float*)(p+offset+j*12+8)=v.z;}
                        *(int*)(p+offset+36)=0;offset+=40;
                    }
                }
                if(!___link.CollisionMessage(3,r.packet)) FailedPackets++;
            }
            Exports++;Triangles=triangles;
            if(!reported) {log.LogInfo("Native terrain export: "+meshes+" readable meshes/boxes, "+triangles+" actual triangles; "+unsupported.Count+" regions retain primitive fallback. Readiness packets preserved.");reported=true;}
        }
    }
}
