using System;
using System.Collections.Generic;
using System.Collections;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace RoRCraftPolish {
    // New event kinds bypass the preserved host's player-only damage handler.
    internal static class MinecraftEnemyCombat {
        private sealed class HealGrant { internal float amount,expires;internal int run,stage; }
        private static readonly Dictionary<int,HealGrant> heals=new Dictionary<int,HealGrant>();
        private static readonly HashSet<int> rewarded=new HashSet<int>();private static int rewardStage=-1;
        private static int observedRun;
        private static object activeAdapter;private static CharacterBody activeBody;
        private static int reverseId=-1;internal static int reversePulses;internal static ulong awardedExperience;
        private sealed class EffectCommand {internal RoRCraft.SkyMemory link;internal int mob,run,stage,effect,duration,interval;internal float damage,radius,secondary;}
        private static readonly Queue<EffectCommand> pending=new Queue<EffectCommand>();
        internal static int queuedPeak;
        internal static void Install(Harmony harmony) {
            SurvivalBalance.Install(harmony);
            GlobalEventManager.onCharacterDeathGlobal+=NativeDeath;
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("RoRCraft.RoRCraftPlugin"),"Update"),postfix:new HarmonyMethod(typeof(MinecraftEnemyCombat),"UpdateBridge"));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("RoRCraft.RoRCraftPlugin"),"ApplyEvent"),prefix:new HarmonyMethod(typeof(MinecraftEnemyCombat),"Apply"));
        }
        private static void UpdateBridge(object __instance,CharacterBody ___controlled){
            activeAdapter=__instance;activeBody=___controlled;
            if(NetworkServer.active && Run.instance!=null && TeamManager.instance!=null){
                var memory=(RoRCraft.SkyMemory)AccessTools.Field(__instance.GetType(),"link").GetValue(__instance);
                if(memory!=null){int token=memory.I(0x940);if(observedRun!=token){observedRun=token;heals.Clear();pending.Clear();rewarded.Clear();rewardStage=-1;}memory.I(0xC18,(int)TeamManager.instance.GetTeamLevel(TeamIndex.Monster));System.Threading.Thread.MemoryBarrier();memory.I(0xC1C,0x314C564C);
                    int sequence=memory.I(0xE0C);memory.I(0xE0C,sequence+1);System.Threading.Thread.MemoryBarrier();
                    memory.I(0xE00,___controlled==null || ___controlled.inventory==null?0:___controlled.inventory.GetItemCountEffective(RoR2Content.Items.Crowbar));memory.I(0xE04,token);memory.I(0xE08,0x31425243);
                    System.Threading.Thread.MemoryBarrier();memory.I(0xE0C,sequence+2);}
            }
            Flush();
        }
        private static void NativeDeath(DamageReport report){
            if(!NetworkServer.active || Run.instance==null || activeAdapter==null || activeBody==null || !activeBody.healthComponent.alive || report.attackerBody!=activeBody || report.victimBody==null || report.victimTeamIndex==activeBody.teamComponent.teamIndex)return;
            int gas=activeBody.inventory.GetItemCountEffective(RoR2Content.Items.IgniteOnKill),wisp=activeBody.inventory.GetItemCountEffective(RoR2Content.Items.ExplodeOnDeath);
            if(gas<=0 && wisp<=0)return;
            var link=(RoRCraft.SkyMemory)AccessTools.Field(activeAdapter.GetType(),"link").GetValue(activeAdapter);
            if(link==null || pending.Count>2045)return;
            var pos=(Vector3)AccessTools.Method(activeAdapter.GetType(),"ToMinecraft").Invoke(activeAdapter,new object[]{report.victimBody.corePosition});
            // Coordinate origin then ordinary effect commands: FIFO keeps each origin before its pulses.
            int id=reverseId--;if(reverseId==int.MinValue)reverseId=-1;
            pending.Enqueue(new EffectCommand{link=link,mob=id,run=link.I(0x940),stage=Run.instance.stageClearCount,effect=5,damage=pos.x,duration=BitConverter.ToInt32(BitConverter.GetBytes(pos.y),0),interval=BitConverter.ToInt32(BitConverter.GetBytes(pos.z),0)});
            if(gas>0){var dot=DotController.GetDotDef(DotController.DotIndex.Burn);float total=(1+gas)*4.5f;int ticks=Math.Max(1,(int)Math.Ceiling(total/(dot.damageCoefficient*6f)));WriteEffect(activeAdapter,id,9,ticks*dot.interval,dot.interval,2,8+4f*gas,total);}
            if(wisp>0)WriteEffect(activeAdapter,id,21*(1+.8f*(wisp-1)),.5f,0,3,12+2.4f*(wisp-1),0);
            reversePulses++;queuedPeak=Math.Max(queuedPeak,pending.Count);Flush();
        }
        private static bool Apply(object __instance,byte[] __0,CharacterBody ___controlled,Dictionary<uint,CharacterBody> ___actors) {
            if(__0==null || __0.Length<32)return true;
            int kind=BitConverter.ToInt32(__0,0);
            if(kind!=0x701 && kind!=0x702 && kind!=0x703 && kind!=0x704)return true;
            if(!NetworkServer.active || Run.instance==null || ___controlled==null || !___controlled.healthComponent.alive)return false;
            var memory=(RoRCraft.SkyMemory)AccessTools.Field(__instance.GetType(),"link").GetValue(__instance);
            int token=memory.I(0x940);if(observedRun!=token){observedRun=token;heals.Clear();pending.Clear();rewarded.Clear();rewardStage=-1;}
            if(kind==0x704){
                int id=BitConverter.ToInt32(__0,4);HealGrant grant;
                if(heals.TryGetValue(id,out grant)){heals.Remove(id);if(grant.run==token && grant.stage==Run.instance.stageClearCount && Time.time<grant.expires)___controlled.healthComponent.Heal(grant.amount,default(ProcChainMask),true);}
                return false;
            }
            if(kind==0x703) {
                float dealt=BitConverter.ToSingle(__0,8);
                if(float.IsNaN(dealt) || float.IsInfinity(dealt) || dealt<=0 || dealt>10000)return false;
                int mob=BitConverter.ToInt32(__0,4);
                if(___controlled.bleedChance>0 && Util.CheckRoll(___controlled.bleedChance,___controlled.master)) {
                    var dot=DotController.GetDotDef(DotController.DotIndex.Bleed);
                    if(dot!=null && dot.interval>0 && dot.damageCoefficient>0)
                        WriteEffect(__instance,mob,dot.damageCoefficient*6f,3f,dot.interval,0);
                }
                // ProcessHitEnemy: Slow60, two seconds per Chronobauble stack, proc coefficient one.
                int slow=___controlled.inventory==null?0:___controlled.inventory.GetItemCountEffective(RoR2Content.Items.SlowOnHit);
                if(slow>0)WriteEffect(__instance,mob,.6f,Math.Min(3600f,2f*slow),0,1);
                return false;
            }
            if(kind==0x702) {
                int id=BitConverter.ToInt32(__0,4);
                var mc=new Vector3(BitConverter.ToSingle(__0,12),BitConverter.ToSingle(__0,16),BitConverter.ToSingle(__0,20));
                if(float.IsNaN(mc.x)||float.IsInfinity(mc.x)||float.IsNaN(mc.y)||float.IsInfinity(mc.y)||float.IsNaN(mc.z)||float.IsInfinity(mc.z))return false;
                if(rewardStage!=Run.instance.stageClearCount){rewardStage=Run.instance.stageClearCount;rewarded.Clear();}
                if(!rewarded.Add(id))return false;
                if(___controlled.master!=null)___controlled.master.GiveMoney((uint)Run.instance.GetDifficultyScaledCost(BitConverter.ToInt32(__0,24)==1?12:8));
                var origin=(Vector3)AccessTools.Method(__instance.GetType(),"ToHost").Invoke(__instance,new object[]{mc});
                if(ExperienceManager.instance!=null){ulong experience=(ulong)Run.instance.GetDifficultyScaledCost(4);ExperienceManager.instance.AwardExperience(origin,___controlled,experience);awardedExperience+=experience;}
                int gas=___controlled.inventory.GetItemCountEffective(RoR2Content.Items.IgniteOnKill);
                if(gas>0){
                    var dot=DotController.GetDotDef(DotController.DotIndex.Burn);float tick=dot.damageCoefficient*6f,total=(1+gas)*.75f*6f;
                    float radius=8+4f*gas;
                    int ticks=Math.Max(1,(int)Math.Ceiling(total/tick));
                    WriteEffect(__instance,id,9f,ticks*dot.interval,dot.interval,2,radius,total);
                    foreach(var burned in new HashSet<CharacterBody>(___actors.Values))if(burned!=null && burned.healthComponent.alive && burned.teamComponent.teamIndex!=___controlled.teamComponent.teamIndex && Vector3.Distance(burned.corePosition,origin)<=radius){
                        var burnInfo=new InflictDotInfo{victimObject=burned.gameObject,attackerObject=___controlled.gameObject,dotIndex=DotController.DotIndex.Burn,damageMultiplier=1,totalDamage=(1+gas)*.75f*___controlled.damage};DotController.InflictDot(ref burnInfo);
                    }
                    Blast(___controlled,origin,radius,___controlled.damage*1.5f);
                }
                int wisp=___controlled.inventory.GetItemCountEffective(RoR2Content.Items.ExplodeOnDeath);
                if(wisp>0){
                    float coefficient=3.5f*(1+.8f*(wisp-1)),radius=12+2.4f*(wisp-1);
                    WriteEffect(__instance,id,6f*coefficient,.5f,0,3,radius,0);
                    ((MonoBehaviour)__instance).StartCoroutine(DelayedBlast(___controlled,origin,radius,Util.OnKillProcDamage(___controlled.damage,coefficient),Run.instance));
                }
                foreach(var key in new List<int>(heals.Keys))if(heals[key].expires<=Time.time)heals.Remove(key);
                int tooth=___controlled.inventory.GetItemCountEffective(RoR2Content.Items.Tooth);
                if(tooth>0 && heals.Count<1024){
                    float amount=8f+.02f*tooth*___controlled.healthComponent.fullHealth;
                    if(WriteEffect(__instance,id,amount,30f,0,4))heals[id]=new HealGrant{amount=amount,expires=Time.time+60,run=token,stage=Run.instance.stageClearCount};
                }
                return false;
            }
            CharacterBody victim;float damage=BitConverter.ToSingle(__0,8);
            if(!___actors.TryGetValue(BitConverter.ToUInt32(__0,4),out victim) || victim==null || victim.healthComponent==null || !victim.healthComponent.alive || float.IsNaN(damage) || float.IsInfinity(damage) || damage<=0 || damage>10000)return false;
            // Ordinary native armor/HP still apply. No player attacker, crit roll or item proc.
            var info=new DamageInfo{damage=damage,attacker=null,inflictor=null,position=victim.corePosition,procCoefficient=0,crit=false};
            victim.healthComponent.TakeDamage(info);
            return false;
        }
        private static IEnumerator DelayedBlast(CharacterBody body,Vector3 origin,float radius,float damage,Run run){
            string scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            yield return new WaitForSeconds(.5f);
            if(Run.instance==run && body!=null && body.healthComponent.alive && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name==scene)Blast(body,origin,radius,damage,BlastAttack.FalloffModel.SweetSpot);
        }
        private static void Blast(CharacterBody body,Vector3 origin,float radius,float damage,BlastAttack.FalloffModel falloff=BlastAttack.FalloffModel.None){
            new BlastAttack{attacker=body.gameObject,teamIndex=body.teamComponent.teamIndex,position=origin,radius=radius,baseDamage=damage,procCoefficient=0,crit=false,falloffModel=falloff}.Fire();
        }
        private static bool WriteEffect(object adapter,int mob,float damage,float seconds,float interval,int effect,float radius=0,float secondary=0) {
            var link=(RoRCraft.SkyMemory)AccessTools.Field(adapter.GetType(),"link").GetValue(adapter);
            if(pending.Count>=2048)return false;
            pending.Enqueue(new EffectCommand{link=link,mob=mob,run=link.I(0x940),stage=Run.instance.stageClearCount,effect=effect,damage=damage,duration=(int)Math.Round(seconds*20),interval=Math.Max(1,(int)Math.Round(interval*20)),radius=radius,secondary=secondary});
            queuedPeak=Math.Max(queuedPeak,pending.Count);Flush();return true;
        }
        private static void Flush(){
            while(pending.Count>0){
                var cmd=pending.Peek();var link=cmd.link;
                if(Run.instance==null || link.I(0x940)!=cmd.run || Run.instance.stageClearCount!=cmd.stage){pending.Dequeue();continue;}
                if(link.I(0xC10)!=0x32435052){link.L(0xC00,0);link.L(0xC08,0);link.I(0xC14,cmd.run);link.I(0xC10,0x32435052);}
                long head=link.L(0xC00),tail=link.L(0xC08);if(head-tail>=15)return;
                pending.Dequeue();link.I(0xC14,cmd.run);long offset=0xC20+(head%15)*32;
                link.I(offset,cmd.mob);link.F(offset+4,cmd.damage);link.I(offset+8,cmd.duration);link.I(offset+12,cmd.interval);
                link.I(offset+16,cmd.run);link.I(offset+20,cmd.effect);link.F(offset+24,cmd.radius);link.F(offset+28,cmd.secondary);
                System.Threading.Thread.MemoryBarrier();link.L(0xC00,head+1);
            }
        }
    }
}
