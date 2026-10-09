using System;
using System.Collections.Generic;
using UnityEngine;
namespace RoRCraftPolish {
    // ParrotModel.PARTY: common cos/sin orbit and sin*.4 head roll.
    // Native Java age advances at 20 ticks/s (period PI/10); local Pigstep
    // repeated spectral attacks fit a .353s subbeat grid. One orbit per
    // subbeat keeps the vanilla path; tempo adaptation is explicit.
    // .25: user-approved motion slowed by ~15%; visuals share .415s, audio unchanged.
    internal static class ParrotDanceMotion {
        internal const float BeatSeconds=.415f,BeatOffset=.008f;
        internal const float CycleSeconds=BeatSeconds;
        internal const float FullLoopSeconds=BeatSeconds*6;
        internal static float Phase(float seconds){return (seconds-BeatOffset)*(2*Mathf.PI/CycleSeconds)-Mathf.PI*.5f;}
        internal static float Beat(float seconds){return Mathf.Sin(Phase(seconds));}
        internal static float Bounce(float seconds){return (1-Beat(seconds))*.5f;}
        internal static float Blend(float seconds){return Mathf.SmoothStep(0,1,Mathf.Clamp01(Mathf.Min(seconds,6-seconds)/.25f));}
        internal static Vector3 Orbit(float seconds){float phase=Phase(seconds);return new Vector3(Mathf.Cos(phase),-Mathf.Sin(phase),0)/16;}
        internal static float NotePulse(float seconds){float t=Mathf.Repeat(seconds-BeatOffset,BeatSeconds);return Mathf.Max(0,1-t/.12f);}
    }
    internal enum LobbyHeldItem {None,Sword,Pickaxe,Shield,Food}
    internal struct LobbyPose {internal Vector3 Torso,Head,RightArm,LeftArm,RightLeg,LeftLeg,Offset,HeadOffset;internal float BodyLift;}
    internal interface ILobbyIdleClip {
        string Name {get;} float Duration {get;}
        bool Available(LobbyHeldItem item);
        LobbyPose Sample(float age,float target,LobbyHeldItem item);
    }
    internal static class LobbyIdleRegistry {
        private static readonly Dictionary<string,ILobbyIdleClip> clips=new Dictionary<string,ILobbyIdleClip>();
        static LobbyIdleRegistry(){Register(new Look());Register(new Dance());Register(new Inspect());}
        internal static void Register(ILobbyIdleClip clip){clips[clip.Name]=clip;}
        internal static ILobbyIdleClip Get(string name){ILobbyIdleClip clip;return clips.TryGetValue(name,out clip)?clip:null;}
        private sealed class Look:ILobbyIdleClip {
            public string Name{get{return "look_around";}}public float Duration{get{return 4;}}
            public bool Available(LobbyHeldItem item){return true;}
            public LobbyPose Sample(float age,float target,LobbyHeldItem item){return new LobbyPose{Head=new Vector3(target*.12f,target,0)*Mathf.Sin(age/Duration*Mathf.PI)};}
        }
        private sealed class Dance:ILobbyIdleClip {
            public string Name{get{return "parrot_dance";}}public float Duration{get{return 6;}}
            public bool Available(LobbyHeldItem item){return true;}
            public LobbyPose Sample(float age,float target,LobbyHeldItem item){
                float fade=ParrotDanceMotion.Blend(age),beat=ParrotDanceMotion.Beat(age);
                float bop=(1-beat)*.5f;
                float side=Mathf.Cos(ParrotDanceMotion.Phase(age));
                // Retarget the bird's shared orbit to the ENTIRE upper body.
                // Arms are folded wings, never independent alternating punches.
                // Opposite hand heights arise from the shoulder rock itself.
                return new LobbyPose{Torso=new Vector3(8,0,-5*beat)*fade,
                    Head=new Vector3(-8,0,-beat*.4f*Mathf.Rad2Deg+5*beat)*fade,
                    RightArm=new Vector3(-15,0,-20)*fade,LeftArm=new Vector3(-15,0,20)*fade,
                    BodyLift=.025f*bop*fade,
                    Offset=new Vector3(.08f*side,-.04f*(beat+1),0)*fade};
            }
        }
        private sealed class Inspect:ILobbyIdleClip {
            public string Name{get{return "item_inspection";}}public float Duration{get{return 3;}}
            public bool Available(LobbyHeldItem item){return item!=LobbyHeldItem.None;}
            public LobbyPose Sample(float age,float target,LobbyHeldItem item){float fade=Mathf.Sin(age/Duration*Mathf.PI);return new LobbyPose{Head=new Vector3(12,-15,0)*fade,RightArm=new Vector3(item==LobbyHeldItem.Shield?-55:-35,0,-12)*fade};}
        }
    }
    internal sealed class LobbyIdleController {
        private readonly Transform torso,head,ra,la,rl,ll;
        private readonly Vector3 tp,hp,rap,lap,rlp,llp;
        private readonly System.Random random;
        private ILobbyIdleClip clip;private float age,clock,lookAt,danceAt,itemAt,target;
        internal Func<LobbyHeldItem> HeldItem {get;set;}
        internal string State{get{return clip==null?"normal_idle":clip.Name;}}
        internal float ClipAge {get{return age;}}
        internal LobbyIdleController(Transform body,Transform face,Transform rightArm,Transform leftArm,Transform rightLeg,Transform leftLeg,int seed){
            torso=body;head=face;ra=rightArm;la=leftArm;rl=rightLeg;ll=leftLeg;
            tp=torso.localPosition;hp=head.localPosition;rap=ra.localPosition;lap=la.localPosition;rlp=rl.localPosition;llp=ll.localPosition;
            random=new System.Random(seed);lookAt=8+Next(8);danceAt=60+Next(90);itemAt=20+Next(20);
        }
        private float Next(float range){return (float)random.NextDouble()*range;}
        internal bool Play(string name){var next=LobbyIdleRegistry.Get(name);if(next==null || !next.Available(HeldItem==null?LobbyHeldItem.None:HeldItem()))return false;clip=next;age=0;target=Next(55)-27.5f;return true;}
        internal void Advance(float delta){
            delta=Mathf.Clamp(delta,0,.1f);clock+=delta;
            if(clip!=null){age+=delta;if(age>=clip.Duration){clip=null;age=0;lookAt=Mathf.Max(lookAt,clock+5);itemAt=Mathf.Max(itemAt,clock+5);}}
            if(clip==null){if(clock>=danceAt){Play("parrot_dance");danceAt=clock+90+Next(120);}else if(clock>=lookAt){Play("look_around");lookAt=clock+8+Next(12);}else if(clock>=itemAt){Play("item_inspection");itemAt=clock+20+Next(20);}}
            var pose=clip==null?new LobbyPose():clip.Sample(age,target,HeldItem==null?LobbyHeldItem.None:HeldItem());bool dancing=State=="parrot_dance";float breath=dancing?0:Mathf.Sin(clock*1.7f);
            var turn=Quaternion.Euler(pose.Torso);
            var lift=Vector3.up*pose.BodyLift;
            torso.localPosition=tp+pose.Offset+lift+Vector3.up*breath*.005f;
            head.localPosition=tp+pose.Offset+lift+(dancing?turn*(hp-tp):hp-tp)+pose.HeadOffset;
            ra.localPosition=tp+pose.Offset+lift+(dancing?turn*(rap-tp):rap-tp);
            la.localPosition=tp+pose.Offset+lift+(dancing?turn*(lap-tp):lap-tp);
            // Keep the feet beneath the swaying hips, with a small residual lean.
            var legShift=dancing?Vector3.right*(pose.Offset.x*.75f):pose.Offset;
            rl.localPosition=rlp+lift+legShift;ll.localPosition=llp+lift+legShift;
            rl.localScale=Vector3.one;ll.localScale=Vector3.one;
            if(dancing) {
                // Follow the lower torso edge instead of pushing the thigh tops
                // into the abdomen. Compress only leg height; feet retain their
                // existing ground/bop height and the torso pose stays unchanged.
                FitLegBelowHip(rl,rlp,turn,pose.Offset+lift,pose.BodyLift,ParrotDanceMotion.Blend(age));
                FitLegBelowHip(ll,llp,turn,pose.Offset+lift,pose.BodyLift,ParrotDanceMotion.Blend(age));
            }
            torso.localRotation=turn;
            head.localRotation=(dancing?turn:Quaternion.identity)*Quaternion.Euler(pose.Head+new Vector3(breath,dancing?0:Mathf.Sin(clock*.43f)*2,0));
            // Rotate the head about its centre like ParrotModel, not about
            // the neck: the hat/eyes no longer swing into the shoulder bird.
            if(dancing)head.localPosition+=Vector3.up*.25f-head.localRotation*(Vector3.up*.25f);
            ra.localRotation=(dancing?turn:Quaternion.identity)*Quaternion.Euler(pose.RightArm+new Vector3(0,0,2+breath));
            la.localRotation=(dancing?turn:Quaternion.identity)*Quaternion.Euler(pose.LeftArm+new Vector3(0,0,-2-breath));
            rl.localRotation=Quaternion.Euler(pose.RightLeg);ll.localRotation=Quaternion.Euler(pose.LeftLeg);
        }
        private void FitLegBelowHip(Transform leg,Vector3 bind,Quaternion turn,Vector3 offset,float lift,float blend) {
            var hip=tp+offset+turn*(bind-tp);
            var position=leg.localPosition;position.y=hip.y-.008f*blend;position.z=hip.z;
            leg.localPosition=position;
            float sole=bind.y-.75f+lift;
            leg.localScale=new Vector3(1,(position.y-sole)/.75f,1);
        }
    }
}
