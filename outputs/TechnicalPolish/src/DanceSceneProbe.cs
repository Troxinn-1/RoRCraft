using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using BepInEx;
namespace RoRCraftPolish {
    internal static class DanceSceneProbe {
        internal static void CaptureNormalLobby(List<string> checks,Transform root,LobbySkinAvatar avatar) {
            var idle=typeof(LobbySkinAvatar).GetField("idle",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(avatar) as LobbyIdleController;
            Camera actual=null;float size=0;
            foreach(var c in UnityEngine.Object.FindObjectsOfType<Camera>()) {
                if(!c.enabled || c.orthographic || (c.cullingMask&(1<<root.gameObject.layer))==0)continue;
                var feet=c.WorldToViewportPoint(root.position);var top=c.WorldToViewportPoint(root.position+Vector3.up*2);
                float h=Mathf.Abs(top.y-feet.y);
                if(feet.z>0 && top.x>0 && top.x<1 && top.y>0 && top.y<1 && feet.y>0 && feet.y<1 && h>.08f && h<.9f && h>size){actual=c;size=h;}
            }
            if(actual==null)throw new InvalidOperationException("Normal lobby camera unavailable; refusing substitute close-up");
            var copy=new GameObject("Owned normal-distance dance capture").AddComponent<Camera>();copy.enabled=false;copy.CopyFrom(actual);copy.enabled=false;
            copy.transform.SetPositionAndRotation(actual.transform.position,actual.transform.rotation);
            var rt=new RenderTexture(1280,720,24);rt.Create();var image=new Texture2D(1280,720,TextureFormat.RGBA32,false);
            var directory=Path.Combine(Paths.ConfigPath,"RoRCraftDanceCapture");Directory.CreateDirectory(directory);
            try {
                copy.targetTexture=rt;avatar.PreviewDance();int eyeSamples=0;
                var playerHead=root.Find("Head");var bird=root.Find("Torso/Minecraft shoulder parrot");
                // Props are created lazily on first update.
                avatar.UpdateDanceScene();bird=root.Find("Torso/Minecraft shoulder parrot");
                var props=root.Find("RoRCraft Parrot Dance scene");
                for(int i=0;i<96;i++) {
                    float sample=ParrotDanceMotion.BeatOffset+ParrotDanceMotion.BeatSeconds*6+i*(ParrotDanceMotion.FullLoopSeconds/96);while(idle.ClipAge+.0001f<sample)idle.Advance(Mathf.Min(.025f,sample-idle.ClipAge));
                    avatar.UpdateDanceScene();copy.Render();var old=RenderTexture.active;
                    try {RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();}finally{RenderTexture.active=old;}
                    File.WriteAllBytes(Path.Combine(directory,"lobby-distance-"+i.ToString("00")+".png"),image.EncodeToPNG());
                    var withProps=image.GetPixels32();bird.gameObject.SetActive(false);props.gameObject.SetActive(false);copy.Render();Read(rt,image);var noProps=image.GetPixels32();
                    for(int y=0;y<4;y++)for(int x=1;x<7;x++) {
                        var point=playerHead.TransformPoint(new Vector3(-.2625f+(x+.5f)*.065625f,.25f-.2625f+(y+.5f)*.065625f,.263f));
                        var screen=copy.WorldToScreenPoint(point);int px=Mathf.RoundToInt(screen.x),py=Mathf.RoundToInt(screen.y);
                        if(px<0 || px>=1280 || py<0 || py>=720)continue;
                        var on=withProps[py*1280+px];var off=noProps[py*1280+px];eyeSamples++;
                        if(on.g-on.r>off.g-off.r+20 && on.g-on.b>off.g-off.b+20)throw new InvalidOperationException("Green eye contamination in normal lobby phase "+i);
                    }
                    bird.gameObject.SetActive(true);props.gameObject.SetActive(true);
                }
                while(idle.State=="parrot_dance")idle.Advance(.05f);avatar.UpdateDanceScene();
                if(eyeSamples<2000)throw new InvalidOperationException("Incomplete normal-lobby face/eye phase coverage: "+eyeSamples);
                checks.Add("PASS native normal-distance green face/eye comparison: "+eyeSamples+" prop on/off samples");
                checks.Add("PASS complete 2.490s loop at normal lobby camera (96 frames): original camera="+actual.name+" distance="+Vector3.Distance(actual.transform.position,root.position).ToString("F2")+"m FOV="+actual.fieldOfView+" model height="+(720*size).ToString("F0")+"px/720px; no zoom adjustment");
            } finally {copy.targetTexture=null;UnityEngine.Object.Destroy(copy.gameObject);rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);}
        }
        private static void Read(RenderTexture rt,Texture2D image){var old=RenderTexture.active;try{RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();}finally{RenderTexture.active=old;}}
        internal static void Run(List<string> checks) {
            string session=File.ReadAllText(Path.Combine(Paths.ConfigPath,"rorcraft-skin-session.txt")).Trim();
            var skin=SessionSkinData.Read(File.ReadAllBytes(Path.Combine(Paths.ConfigPath,"rorcraft-skin-session.bin")),session);
            var display=new GameObject("Owned dance scene GPU fixture");display.transform.position=new Vector3(30000,0,0);
            LobbySkinAvatar avatar=null;Camera camera=null;GameObject light=null;RenderTexture rt=null;Texture2D capture=null,sheet=null;
            try {
                avatar=new LobbySkinAvatar(display.transform,skin);
                var root=display.transform.Find("RoRCraft lobby avatar");root.localRotation=Quaternion.identity;
                if(!avatar.PreviewDance())throw new InvalidOperationException("Dance preview refused");
                var idle=typeof(LobbySkinAvatar).GetField("idle",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(avatar) as LobbyIdleController;
                avatar.UpdateDanceScene();
                var bird=root.Find("Torso/Minecraft shoulder parrot");var box=root.Find("RoRCraft Parrot Dance scene/Minecraft jukebox");
                if(bird==null || box==null || !bird.gameObject.activeInHierarchy || !box.gameObject.activeInHierarchy)throw new InvalidOperationException("Dance props missing");
                if(bird.parent!=root.Find("Torso") || bird.GetComponentsInChildren<MeshRenderer>().Length!=11)throw new InvalidOperationException("Incorrect parrot model/shoulder attachment");
                if(bird.Find("head").GetComponentInChildren<MeshRenderer>().sharedMaterial.mainTexture.name!="RoRCraft dance texture parrot_green.png")throw new InvalidOperationException("GIF green parrot texture missing");
                if(root.GetComponentsInChildren<Collider>(true).Length!=0)throw new InvalidOperationException("Dance props added gameplay collision");
                foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
                camera=new GameObject("Owned dance camera").AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<31;camera.renderingPath=UnityEngine.RenderingPath.DeferredShading;
                camera.transform.position=display.transform.position+new Vector3(.3f,1.4f,4.5f);camera.transform.LookAt(display.transform.position+new Vector3(.15f,1.1f,0));
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.14f,.18f);camera.nearClipPlane=.1f;camera.farClipPlane=12;camera.orthographic=true;camera.orthographicSize=1.35f;
                light=new GameObject("Owned dance light");var lamp=light.AddComponent<Light>();lamp.type=LightType.Directional;lamp.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(35,180,0);lamp.intensity=1.3f;
                rt=new RenderTexture(640,640,24);rt.Create();camera.targetTexture=rt;capture=new Texture2D(640,640,TextureFormat.RGBA32,false);sheet=new Texture2D(2560,1280,TextureFormat.RGBA32,false);
                var directory=Path.Combine(Paths.ConfigPath,"RoRCraftDanceCapture");Directory.CreateDirectory(directory);
                float lowest=100,highest=-100,leftmost=100,rightmost=-100,lowestFoot=100,highestFoot=-100;
                int eyeSamples=0,penetrations=0;var seenNoteColors=new HashSet<Color>();
                float start=ParrotDanceMotion.BeatOffset+ParrotDanceMotion.BeatSeconds*6;
                var playerHead=root.Find("Head");var legs=root.Find("Right leg").localPosition;
                var loopPositions=new Dictionary<Transform,Vector3>();var loopRotations=new Dictionary<Transform,Quaternion>();var loopScales=new Dictionary<Transform,Vector3>();
                for(int frame=0;frame<96;frame++) {
                    float sample=start+frame*(ParrotDanceMotion.FullLoopSeconds/96);
                    while(idle.ClipAge+.0001f<sample)idle.Advance(Mathf.Min(.01f,sample-idle.ClipAge));
                    avatar.UpdateDanceScene();
                    int visibleNotes=0;
                    foreach(var renderer in box.parent.GetComponentsInChildren<MeshRenderer>())if(renderer.gameObject.name=="Minecraft note") {visibleNotes++;seenNoteColors.Add(renderer.sharedMaterial.GetColor("_Color"));}
                    if(visibleNotes!=1 || box.localScale!=Vector3.one)throw new InvalidOperationException("Expected one note and a non-pulsing jukebox");
                    var head=bird.Find("head");float angle=frame*2*Mathf.PI/16-Mathf.PI/2;
                    float expectedY=(24-15.69f-Mathf.Sin(angle))/16;
                    if(Mathf.Abs(head.localPosition.y-expectedY)>.001f || Mathf.Abs(head.localPosition.x-Mathf.Cos(angle)/16)>.001f)throw new InvalidOperationException("Parrot common orbit differs from original PARTY equations");
                    if(Quaternion.Angle(head.localRotation,Quaternion.Euler(0,0,-Mathf.Sin(angle)*.4f*Mathf.Rad2Deg))>.1f)throw new InvalidOperationException("Original parrot head roll mismatch");
                    var torso=root.Find("Torso");lowest=Mathf.Min(lowest,torso.localPosition.y);highest=Mathf.Max(highest,torso.localPosition.y);leftmost=Mathf.Min(leftmost,torso.localPosition.x);rightmost=Mathf.Max(rightmost,torso.localPosition.x);
                    var rightLeg=root.Find("Right leg");var leftLeg=root.Find("Left leg");
                    var foot=rightLeg.localPosition;float lift=foot.y-.75f*rightLeg.localScale.y;
                    lowestFoot=Mathf.Min(lowestFoot,lift);highestFoot=Mathf.Max(highestFoot,lift);
                    if(Mathf.Abs(foot.x-legs.x)>.061f || lift<-.001f || lift>.026f)throw new InvalidOperationException("Feet shift beyond support or ground/bop height");
                    var feetCenter=(foot+leftLeg.localPosition)*.5f;
                    if(Mathf.Abs(feetCenter.x-torso.localPosition.x)>.021f)throw new InvalidOperationException("Feet no longer support the torso");
                    if(Mathf.Abs(leftLeg.localPosition.y-.75f*leftLeg.localScale.y-lift)>.0001f)throw new InvalidOperationException("Foot soles no longer share ground height");
                    foreach(var leg in new[]{rightLeg,leftLeg}) {
                        float side=leg==rightLeg?-.125f:.125f;
                        var hip=torso.localPosition+torso.localRotation*new Vector3(side,-.375f,0);
                        if(leg.localPosition.y>hip.y+.001f || hip.y-leg.localPosition.y>.01f || leg.localScale.y<.8f || leg.localScale.y>1.05f)throw new InvalidOperationException("Leg top obscures lower torso or hip gap/leg compression is excessive");
                    }
                    foreach(var mesh in bird.GetComponentsInChildren<MeshFilter>())foreach(var vertex in mesh.sharedMesh.vertices) {
                        var local=playerHead.InverseTransformPoint(mesh.transform.TransformPoint(vertex));
                        if(Mathf.Abs(local.x)<.261f && Mathf.Abs(local.y-.25f)<.261f && Mathf.Abs(local.z)<.261f)penetrations++;
                    }
                    if(frame==0)foreach(var t in root.GetComponentsInChildren<Transform>(true)){loopPositions[t]=t.localPosition;loopRotations[t]=t.localRotation;loopScales[t]=t.localScale;}
                    camera.Render();Read(rt,capture);var withProps=capture.GetPixels32();
                    File.WriteAllBytes(Path.Combine(directory,"dance-"+frame.ToString("00")+".png"),capture.EncodeToPNG());
                    if(frame<16 && frame%2==0)sheet.SetPixels((frame/2%4)*640,(1-frame/2/4)*640,640,640,capture.GetPixels());
                    // Render the SAME pose without props: a green eye must not
                    // be caused by clipping, emission or material contamination.
                    bird.gameObject.SetActive(false);box.parent.gameObject.SetActive(false);camera.Render();Read(rt,capture);var noProps=capture.GetPixels32();
                    for(int y=0;y<4;y++)for(int x=1;x<7;x++) {
                        var point=playerHead.TransformPoint(new Vector3(-.2625f+(x+.5f)*.065625f,.25f-.2625f+(y+.5f)*.065625f,.263f));
                        var screen=camera.WorldToScreenPoint(point);int px=Mathf.RoundToInt(screen.x),py=Mathf.RoundToInt(screen.y);
                        if(px<0 || px>=640 || py<0 || py>=640)continue;
                        var on=withProps[py*640+px];var off=noProps[py*640+px];eyeSamples++;
                        if(on.g-on.r>off.g-off.r+20 && on.g-on.b>off.g-off.b+20)throw new InvalidOperationException("Green face/eye contamination at phase "+frame+" sample "+x+","+y);
                    }
                    bird.gameObject.SetActive(true);box.parent.gameObject.SetActive(true);
                }
                if(penetrations!=0)throw new InvalidOperationException("Parrot intersects player head: "+penetrations+" vertices");
                if(highest-lowest<.10f || highest-lowest>.11f || rightmost-leftmost<.15f || rightmost-leftmost>.17f || highestFoot-lowestFoot<.024f || highestFoot-lowestFoot>.026f)throw new InvalidOperationException("Gentle .22-based motion range mismatch");
                if(seenNoteColors.Count!=3)throw new InvalidOperationException("Single note did not cycle all three colours");
                checks.Add("PASS restored .22 side motion, gentle 2.5cm body lift, one note at a time in three alternating colours, stationary jukebox");
                float end=start+ParrotDanceMotion.FullLoopSeconds;while(idle.ClipAge+.0001f<end)idle.Advance(Mathf.Min(.01f,end-idle.ClipAge));avatar.UpdateDanceScene();
                foreach(var t in loopPositions.Keys)if((loopPositions[t]-t.localPosition).magnitude>.001f || Quaternion.Angle(loopRotations[t],t.localRotation)>.1f || (loopScales[t]-t.localScale).magnitude>.001f)throw new InvalidOperationException("Whole-loop seam on "+t.name);
                var clip=LobbyIdleRegistry.Get("parrot_dance");var finish=clip.Sample(5.999f,0,LobbyHeldItem.None);
                if(finish.Offset.magnitude>.001f || finish.BodyLift>.001f || finish.RightArm.magnitude>.01f)throw new InvalidOperationException("Idle transition snaps");
                checks.Add("PASS source-derived common orbit/head roll, gentle whole-body lift/folded arms; complete player/parrot/notes 2.490s loop and eased idle transition");
                checks.Add("PASS no bird vertices penetrate head; green eye/face comparison with props on/off at "+eyeSamples+" samples across 96 phases");
                sheet.Apply();File.WriteAllBytes(Path.Combine(directory,"dance-contact-sheet.png"),sheet.EncodeToPNG());
                while(idle.State=="parrot_dance")idle.Advance(.05f);avatar.UpdateDanceScene();
                if(bird.gameObject.activeSelf || box.parent.gameObject.activeSelf)throw new InvalidOperationException("Dance props did not disappear at idle");
                avatar.PreviewDance();avatar.UpdateDanceScene();avatar.Dispose();avatar=null;
                if(bird.gameObject.activeSelf || box.parent.gameObject.activeSelf)throw new InvalidOperationException("Dance props remained active after dispose");
                checks.Add("PASS authentic 11-cube MC parrot + shoulder parent + jukebox; PARTY equations/roll across 96 full-loop phases, no colliders");
                checks.Add("PASS dance props hide at idle and dispose; 96 native GPU frames/full-loop contact sheet captured for agent visual inspection");
            } finally {
                if(avatar!=null)avatar.Dispose();if(camera!=null){camera.targetTexture=null;UnityEngine.Object.Destroy(camera.gameObject);}if(light!=null)UnityEngine.Object.Destroy(light);
                if(rt!=null){rt.Release();UnityEngine.Object.Destroy(rt);}if(capture!=null)UnityEngine.Object.Destroy(capture);if(sheet!=null)UnityEngine.Object.Destroy(sheet);UnityEngine.Object.Destroy(display);
            }
        }
    }
}
