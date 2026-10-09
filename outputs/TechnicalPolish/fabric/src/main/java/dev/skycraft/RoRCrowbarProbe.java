package dev.skycraft;
import java.nio.file.*;
import java.util.*;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.*;
import net.minecraft.world.entity.monster.zombie.Zombie;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.item.*;
import net.minecraft.world.phys.Vec3;
public final class RoRCrowbarProbe {
    private static final String[] CASES={"zero","one","two"};
    private static final float[] HEALTH={100,90,89};
    private static int index,phase,step,at,applications;
    private static Zombie target;private static final List<String> checks=new ArrayList<>();
    private static void equip(ServerPlayer p,boolean bow){for(int i=0;i<36;i++)if(bow?p.getInventory().getItem(i)==RoRMobProbe.craftedBow:p.getInventory().getItem(i).is(Items.STONE_SWORD)){var stack=p.getInventory().getItem(i);p.getInventory().setItem(i,p.getInventory().getItem(0));p.getInventory().setItem(0,stack);break;}p.getInventory().setSelectedSlot(0);}
    public static void init(){if(!Boolean.getBoolean("skycraft.crowbarProbe"))return;
        var folder=Path.of(new String(Base64.getDecoder().decode(System.getProperty("skycraft.skinMailbox")),java.nio.charset.StandardCharsets.UTF_8)).getParent();
        ServerTickEvents.END_SERVER_TICK.register(server->{if(index>=CASES.length || server.getPlayerList().getPlayers().isEmpty())return;var p=server.getPlayerList().getPlayers().getFirst();String mode=CASES[index];
            try{
                if(phase==0 && Files.exists(folder.resolve("rorcraft-crowbar-"+mode+"-request.txt"))){
                    if(RoRCrowbar.count()!=index)return;
                    var pos=p.position().add(0,3,0);target=EntityTypes.ZOMBIE.create(p.level(),EntitySpawnReason.EVENT);target.addTag(RoRMobs.OWNED);target.setNoAi(true);target.setNoGravity(true);
                    target.getAttribute(Attributes.MAX_HEALTH).setBaseValue(100);target.getAttribute(Attributes.ARMOR).setBaseValue(0);target.setHealth(100);target.setPos(pos);p.level().addFreshEntity(target);
                    p.setNoGravity(true);p.setPos(pos.x,pos.y,pos.z-1.2);equip(p,false);applications=RoRCrowbar.applications;checks.clear();step=0;at=p.tickCount+30;phase=1;
                }else if(phase==1 && p.tickCount>=at){
                    target.setHealth(HEALTH[step]);target.damageCooldownTime=0;p.setOnGround(true);p.setSprinting(false);p.resetAttackStrengthTicker();at=p.tickCount+30;phase=2;
                }else if(phase==2 && p.tickCount>=at){
                    p.setOnGround(true);p.setSprinting(false);p.attack(target);
                    float expected=5*(step<2?1+.75f*index:1),lost=HEALTH[step]-target.getHealth();
                    if(Math.abs(lost-expected)>.02)throw new IllegalStateException(mode+" sword atHP"+HEALTH[step]+" expected="+expected+" actual="+lost);
                    checks.add("PASS "+mode+" actual crafted sword at "+HEALTH[step]+"% HP: damage="+lost);step++;
                    if(step<3){at=p.tickCount+20;phase=1;}else{at=p.tickCount+25;phase=3;}
                }else if(phase==3 && p.tickCount>=at){
                    target.setHealth(100);target.damageCooldownTime=0;target.hurtServer(p.level(),p.damageSources().indirectMagic(p,p),5);
                    if(Math.abs(target.getHealth()-95)>.01)throw new IllegalStateException("Proc/magic received opening bonus");
                    target.setHealth(100);target.damageCooldownTime=0;target.hurtServer(p.level(),p.damageSources().generic(),5);
                    if(Math.abs(target.getHealth()-95)>.01)throw new IllegalStateException("Unattributed damage received opening bonus");
                    checks.add("PASS "+mode+" player magic/proc and environment damage excluded; no repeated multiplier below90%");
                    if(index==0){finish(folder,mode);return;}
                    target.setHealth(100);target.damageCooldownTime=0;equip(p,true);p.setPos(target.getX(),target.getY(),target.getZ()-4);
                    var d=target.getBoundingBox().getCenter().subtract(p.getEyePosition());p.setYRot((float)Math.toDegrees(Math.atan2(-d.x,d.z)));p.setXRot((float)-Math.toDegrees(Math.atan2(d.y,Math.sqrt(d.x*d.x+d.z*d.z))));
                    var bow=p.getMainHandItem();if(bow!=RoRMobProbe.craftedBow)throw new IllegalStateException("Actual crafted bow missing");int arrows=RoRGameplayProbe.count(p,Items.ARROW);
                    p.startUsingItem(net.minecraft.world.InteractionHand.MAIN_HAND);((BowItem)bow.getItem()).releaseUsing(bow,p.level(),p,bow.getUseDuration(p)-20);p.stopUsingItem();
                    if(RoRGameplayProbe.count(p,Items.ARROW)!=arrows-1)throw new IllegalStateException("Real bow arrow consumption missing");RoRCombatProbe.spentAdditionalArrow();at=p.tickCount+80;phase=4;
                }else if(phase==4 && target.getHealth()<100){
                    float expected=RoRCrowbar.lastInput*(1+.75f*index),lost=100-target.getHealth();
                    if(Math.abs(lost-expected)>.03 || Math.abs(RoRCrowbar.lastOutput-expected)>.03)throw new IllegalStateException("Real bow Crowbar damage mismatch input="+RoRCrowbar.lastInput+" output="+RoRCrowbar.lastOutput+" lost="+lost);
                    checks.add("PASS "+mode+" actual crafted bow projectile: input="+RoRCrowbar.lastInput+" damage="+lost+" multiplier="+(1+.75f*index));finish(folder,mode);
                }else if(phase==4 && p.tickCount>=at)throw new IllegalStateException("Crowbar test arrow missed actual zombie");
            }catch(Throwable error){if(target!=null)target.discard();index=CASES.length;try{Files.writeString(folder.resolve("rorcraft-combat-failure.txt"),"FAIL Crowbar "+error);}catch(Exception ignored){}}
        });
    }
    private static void finish(Path folder,String mode)throws Exception{
        if(RoRCrowbar.applications-applications!=(index==0?0:3))throw new IllegalStateException("Unexpected successful bonus-hit count "+(RoRCrowbar.applications-applications));
        checks.add("PASS "+mode+" exact bonus count, native inventory snapshot="+RoRCrowbar.count());Files.write(folder.resolve("rorcraft-crowbar-"+mode+"-done.txt"),checks);target.discard();index++;phase=0;
    }
}
