package dev.skycraft;

import dev.skycraft.link.*;
import java.lang.foreign.ValueLayout;
import java.lang.invoke.VarHandle;
import java.util.*;
import net.fabricmc.fabric.api.entity.event.v1.ServerLivingEntityEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.core.particles.ParticleTypes;
import net.minecraft.world.phys.Vec3;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.resources.Identifier;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.entity.ai.attributes.AttributeModifier;

/** Native item decisions, translated to server-side MC DOT and movement attributes. */
public final class RoRItemProcs {
    private record Dot(LivingEntity victim,ServerPlayer player,float damage,int interval,int ends,int next,boolean burn){}
    private static final List<Dot> dots=new ArrayList<>();
    private record Slow(LivingEntity victim,int ends){}
    private static final Identifier SLOW_ID=Identifier.fromNamespaceAndPath("rorcraft","chronobauble_slow");
    private static final Map<UUID,Slow> slows=new HashMap<>();
    public static int slowApplications;
    private static void clearEffects(){kills.clear();bursts.clear();for(var orb:orbs)orb.discard();orbs.clear();dots.clear();for(var slow:slows.values())removeSlow(slow.victim);slows.clear();}
    private static void removeSlow(LivingEntity victim){var speed=victim.getAttribute(Attributes.MOVEMENT_SPEED);if(speed!=null)speed.removeModifier(SLOW_ID);}
    private record Kill(ServerPlayer player,ServerLevel level,Vec3 pos,int ends){}
    private record Burst(Kill origin,float damage,float radius,int at){}
    private static final Map<Integer,Kill> kills=new HashMap<>();
    private static final List<Burst> bursts=new ArrayList<>();
    private static final List<HealOrb> orbs=new ArrayList<>();
    public static int gasApplications,wispApplications,orbSpawns,orbPickups;
    public static void recordKill(LivingEntity entity,ServerPlayer player){if(kills.size()<1024)kills.put(entity.getId(),new Kill(player,player.level(),entity.position(),player.tickCount+200));}
    public static final String HEAL_TAG="rorcraft_heal_orb";
    public static boolean knownHealOrb(ItemEntity e){return orbs.contains(e);}
    private static final class HealOrb extends ItemEntity{
        final ServerPlayer owner;final int grant,expires;
        HealOrb(Kill kill,int id){super(kill.level,kill.pos.x,kill.pos.y+.3,kill.pos.z,new ItemStack(Items.SLIME_BALL));owner=kill.player;grant=id;expires=owner.tickCount+600;addTag(RoRMobs.OWNED);addTag(HEAL_TAG);setPickUpDelay(10);
            var data=new net.minecraft.nbt.CompoundTag();data.putInt("RoRCraftHealGrant",id);
            getItem().set(net.minecraft.core.component.DataComponents.CUSTOM_DATA,net.minecraft.world.item.component.CustomData.of(data));
            getItem().set(net.minecraft.core.component.DataComponents.CUSTOM_NAME,net.minecraft.network.chat.Component.literal("Healing Orb"));}
        @Override public void playerTouch(Player player){
            if(player!=owner || hasPickUpDelay() || !SkyLink.active() || !isAlive() || owner.getHealth()>=owner.getMaxHealth())return;
            SkyLink.pushEvent(0x704,grant,0,0,0,0,0);orbPickups++;discard();
            owner.level().sendParticles(ParticleTypes.HEART,owner.getX(),owner.getY()+1,owner.getZ(),4,.3,.3,.3,0);
        }
    }
    private static void feedback(Kill k,boolean fire){
        k.level.sendParticles(fire?ParticleTypes.FLAME:ParticleTypes.EXPLOSION,k.pos.x,k.pos.y+.5,k.pos.z,fire?16:4,1,.5,1,.05);
    }
    private static void hurt(Kill k,LivingEntity victim,float damage){
        int cooldown=victim.damageCooldownTime;try{applying=true;victim.damageCooldownTime=0;victim.hurtServer(k.level,k.player.damageSources().indirectMagic(k.player,k.player),damage);}finally{victim.damageCooldownTime=cooldown;applying=false;}
    }
    private static List<LivingEntity> nearby(Kill k,float radius){
        return k.level.getEntitiesOfClass(LivingEntity.class,new net.minecraft.world.phys.AABB(k.pos,k.pos).inflate(radius),e->e.isAlive() && e.entityTags().contains(RoRMobs.OWNED) && e.position().distanceToSqr(k.pos)<=radius*radius);
    }
    private static void area(int kind,int id,float damage,int duration,int interval,float radius,float total,int now){
        areaAt(kind,kills.get(id),id,damage,duration,interval,radius,total,now);
    }
    private static void areaAt(int kind,Kill k,int id,float damage,int duration,int interval,float radius,float total,int now){
        if(k==null || k.player.level()!=k.level || (kind!=4 && (!Float.isFinite(radius) || radius<=0 || radius>256)) || !Float.isFinite(damage) || damage<=0 || damage>100000)return;
        if(kind==2 && duration>0 && duration<=72000 && interval>0 && interval<=duration && Float.isFinite(total) && total>0 && total<=100000){
            gasApplications++;feedback(k,true);int count=Math.max(1,duration/interval);
            for(var victim:nearby(k,radius)){
                hurt(k,victim,damage);
                if(victim.isAlive())dots.add(new Dot(victim,k.player,total/count,interval,now+duration,now+interval,true));
            }
        }else if(kind==3 && duration>0 && duration<=100){wispApplications++;bursts.add(new Burst(k,damage,radius,now+duration));}
        else if(kind==4 && orbs.size()<1024){var orb=new HealOrb(k,id);if(k.level.addFreshEntity(orb)){orbs.add(orb);orbSpawns++;}}
    }
    private static boolean applying;
    private static int run;
    public static int appliedStacks,appliedTicks;
    public static float lastDamage;
    public static void init() {
        ServerLivingEntityEvents.AFTER_DAMAGE.register((mob,source,base,taken,blocked)->{
            if(applying || !SkyLink.active() || !mob.entityTags().contains(RoRMobs.OWNED) || !(source.getEntity() instanceof ServerPlayer) || blocked || taken<=0)return;
            SkyLink.pushEvent(Proto.EV_ROR_MOB_HIT,mob.getId(),taken,0,0,0,0);
        });
        ServerTickEvents.END_SERVER_TICK.register(server->{
            var s=SkyLink.segment();if(s==null || !SkyLink.active() || server.getPlayerList().getPlayers().isEmpty()){clearEffects();return;}
            var player=server.getPlayerList().getPlayers().getFirst();int token=s.get(ValueLayout.JAVA_INT,0x940);
            if(run!=token){run=token;clearEffects();}
            int now=player.tickCount;
            if(s.get(ValueLayout.JAVA_INT,0xC10)==0x32435052 && s.get(ValueLayout.JAVA_INT,0xC14)==token) {
                long head=s.get(ValueLayout.JAVA_LONG,0xC00),tail=s.get(ValueLayout.JAVA_LONG,0xC08);VarHandle.loadLoadFence();
                if(head>=tail && head-tail<=15)while(tail<head) {
                    long offset=0xC20+(tail%15)*32;int id=s.get(ValueLayout.JAVA_INT,offset);
                    float damage=s.get(ValueLayout.JAVA_FLOAT,offset+4);int duration=s.get(ValueLayout.JAVA_INT,offset+8),interval=s.get(ValueLayout.JAVA_INT,offset+12);
                    var e=player.level().getEntity(id);
                    int effect=s.get(ValueLayout.JAVA_INT,offset+20);
                    if(effect==5 && s.get(ValueLayout.JAVA_INT,offset+16)==token){
                        float y=s.get(ValueLayout.JAVA_FLOAT,offset+8),z=s.get(ValueLayout.JAVA_FLOAT,offset+12);
                        if(id<0 && kills.size()<1024 && Float.isFinite(damage) && Float.isFinite(y) && Float.isFinite(z))kills.put(id,new Kill(player,player.level(),new Vec3(damage,y,z),now+200));
                    }else if(effect>=2 && effect<=4 && s.get(ValueLayout.JAVA_INT,offset+16)==token){
                        area(effect,id,damage,duration,interval,s.get(ValueLayout.JAVA_FLOAT,offset+24),s.get(ValueLayout.JAVA_FLOAT,offset+28),now);
                    }else if(s.get(ValueLayout.JAVA_INT,offset+16)==token && e instanceof LivingEntity victim && victim.isAlive() && e.entityTags().contains(RoRMobs.OWNED) && Float.isFinite(damage) && damage>0 && duration>0) {
                        if(effect==0 && damage<1000 && duration<=600 && interval>0 && interval<=duration){
                            dots.add(new Dot(victim,player,damage,interval,now+duration,now+interval,false));appliedStacks++;lastDamage=damage;
                        }else if(effect==1 && damage<1 && duration<=72000){
                            var speed=victim.getAttribute(Attributes.MOVEMENT_SPEED);
                            if(speed!=null){
                                speed.addOrUpdateTransientModifier(new AttributeModifier(SLOW_ID,-damage,AttributeModifier.Operation.ADD_MULTIPLIED_TOTAL));
                                var prior=slows.get(victim.getUUID());
                                slows.put(victim.getUUID(),new Slow(victim,Math.max(now+duration,prior==null?0:prior.ends)));slowApplications++;
                            }
                        }
                    }
                    tail++;
                }
                VarHandle.storeStoreFence();s.set(ValueLayout.JAVA_LONG,0xC08,tail);
            }
            kills.values().removeIf(k->now>=k.ends || k.player.level()!=k.level);
            orbs.removeIf(orb->{if(now>=orb.expires || orb.level()!=player.level())orb.discard();return orb.isRemoved();});
            for(var it=bursts.iterator();it.hasNext();){var burst=it.next();if(burst.origin.level!=player.level()){it.remove();continue;}if(now>=burst.at){feedback(burst.origin,false);for(var victim:nearby(burst.origin,burst.radius))hurt(burst.origin,victim,burst.damage*(victim.position().distanceToSqr(burst.origin.pos)>burst.radius*burst.radius*.25?.25f:1f));it.remove();}}
            for(var it=slows.values().iterator();it.hasNext();){
                var slow=it.next();if(!slow.victim.isAlive() || slow.victim.isRemoved() || slow.victim.level()!=player.level() || now>=slow.ends){removeSlow(slow.victim);it.remove();}
            }
            for(var it=dots.listIterator();it.hasNext();) {
                var dot=it.next();if(!dot.victim.isAlive() || dot.victim.isRemoved() || dot.victim.level()!=player.level() || now>dot.ends){it.remove();continue;}
                if(now>=dot.next) {
                    if(dot.burn)player.level().sendParticles(ParticleTypes.FLAME,dot.victim.getX(),dot.victim.getY()+.6,dot.victim.getZ(),4,.35,.5,.35,.01);
                    int cooldown=dot.victim.damageCooldownTime;
                    try{applying=true;dot.victim.damageCooldownTime=0;dot.victim.hurtServer(player.level(),player.damageSources().indirectMagic(dot.player,dot.player),dot.damage);appliedTicks++;}
                    finally{dot.victim.damageCooldownTime=cooldown;applying=false;}
                    it.set(new Dot(dot.victim,dot.player,dot.damage,dot.interval,dot.ends,dot.next+dot.interval,dot.burn));
                }
            }
        });
    }
}
