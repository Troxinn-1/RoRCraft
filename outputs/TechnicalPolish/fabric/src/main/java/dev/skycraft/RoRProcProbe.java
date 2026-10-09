package dev.skycraft;

import java.nio.file.*;
import java.util.*;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.minecraft.world.entity.monster.zombie.Zombie;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.item.Items;

/** Actual vanilla player hit, native item roll, returned DOT; isolated opt-in fixture. */
public final class RoRProcProbe {
    private static int phase,at,stacks,ticks;
    private static float health;
    private static double speed;
    private static int slowCount,firstTick;
    private static Zombie target;
    public static void init() {
        if(!Boolean.getBoolean("skycraft.mobProcProbe"))return;
        if(!SkyCraft.WORLD_NAME.equals("SkyCraft-GameplayTest"))throw new IllegalStateException("Proc test needs isolated world");
        var folder=Path.of(new String(Base64.getDecoder().decode(System.getProperty("skycraft.skinMailbox")),java.nio.charset.StandardCharsets.UTF_8)).getParent();
        ServerTickEvents.END_SERVER_TICK.register(server->{
            if(phase==4 || server.getPlayerList().getPlayers().isEmpty())return;
            var p=server.getPlayerList().getPlayers().getFirst();
            try {
                if(phase==0 && Files.exists(folder.resolve("rorcraft-proc-request.txt"))) {
                    for(var e:p.level().getAllEntities())if(e instanceof Zombie z && e.entityTags().contains(RoRMobs.OWNED)){target=z;break;}
                    if(target==null)throw new IllegalStateException("Production zombie absent");
                    target.getAttribute(Attributes.MAX_HEALTH).setBaseValue(100);target.setHealth(100); // test survival for full DOT duration
                    p.setPos(target.getX(),target.getY(),target.getZ()-1.5);
                    for(int i=0;i<36;i++)if(p.getInventory().getItem(i).is(Items.STONE_SWORD)){var item=p.getInventory().getItem(i);p.getInventory().setItem(i,p.getInventory().getItem(0));p.getInventory().setItem(0,item);break;}
                    p.getInventory().setSelectedSlot(0);p.resetAttackStrengthTicker();at=p.tickCount+30;phase=1;
                } else if(phase==1 && p.tickCount>=at) {
                    stacks=RoRItemProcs.appliedStacks;ticks=RoRItemProcs.appliedTicks;
                    p.attack(target);health=target.getHealth();at=p.tickCount+80;phase=2;
                } else if(phase==2 && p.tickCount>=at) {
                    if(RoRItemProcs.appliedStacks-stacks!=1 || RoRItemProcs.appliedTicks-ticks!=12 || Math.abs((health-target.getHealth())-12*RoRItemProcs.lastDamage)>.1f)throw new IllegalStateException("Native Bleed->MC DOT mismatch: stacks="+(RoRItemProcs.appliedStacks-stacks)+" ticks="+(RoRItemProcs.appliedTicks-ticks)+" HP="+health+"->"+target.getHealth());
                    Files.writeString(folder.resolve("rorcraft-proc-positive.txt"),"PASS actual crafted sword hit on owned zombie activated native Bleed chance; one returned stack, twelve DOT ticks over3s, MC health "+health+" -> "+target.getHealth());
                    stacks=RoRItemProcs.appliedStacks;phase=3;
                } else if(phase==3 && Files.exists(folder.resolve("rorcraft-proc-zero.txt"))) {
                    p.resetAttackStrengthTicker();p.attack(target);at=p.tickCount+20;phase=5;
                } else if(phase==5 && p.tickCount>=at) {
                    if(RoRItemProcs.appliedStacks!=stacks)throw new IllegalStateException("No-item hit activated extra Bleed");
                    Files.writeString(folder.resolve("rorcraft-proc-negative.txt"),"PASS no native Bleed chance: second vanilla sword hit created no additional DOT; DOT ticks did not recursively proc");phase=6;
                } else if(phase==6 && Files.exists(folder.resolve("rorcraft-slow-request.txt"))) {
                    speed=target.getAttributeValue(Attributes.MOVEMENT_SPEED);slowCount=RoRItemProcs.slowApplications;
                    target.damageCooldownTime=0;p.resetAttackStrengthTicker();p.attack(target);at=p.tickCount+20;phase=7;
                } else if(phase==7 && RoRItemProcs.slowApplications>slowCount) {
                    if(RoRItemProcs.slowApplications!=slowCount+1 || Math.abs(target.getAttributeValue(Attributes.MOVEMENT_SPEED)-speed*.4)>1e-6)throw new IllegalStateException("Chronobauble initial speed mismatch");
                    firstTick=p.tickCount;at=firstTick+40;phase=8;
                } else if(phase==7 && p.tickCount>=at)throw new IllegalStateException("Chronobauble command timeout");
                else if(phase==8 && p.tickCount>=at){
                    target.damageCooldownTime=0;p.resetAttackStrengthTicker();p.attack(target);at=firstTick+85;phase=9;
                } else if(phase==9 && p.tickCount>=at){
                    if(RoRItemProcs.slowApplications!=slowCount+2 || Math.abs(target.getAttributeValue(Attributes.MOVEMENT_SPEED)-speed*.4)>1e-6)throw new IllegalStateException("Repeated hit failed refresh or multiplied slow strength");
                    at=firstTick+130;phase=10;
                } else if(phase==10 && p.tickCount>=at){
                    if(Math.abs(target.getAttributeValue(Attributes.MOVEMENT_SPEED)-speed)>1e-6)throw new IllegalStateException("Slow expiry did not restore original speed");
                    Files.writeString(folder.resolve("rorcraft-slow-positive.txt"),"PASS two native Chronobauble: actual sword hits, MC speed "+speed+" -> "+speed*.4+"; duration4s; refresh survives original deadline without stacking strength; expiry restores original attribute");phase=11;
                } else if(phase==11 && Files.exists(folder.resolve("rorcraft-slow-zero.txt"))){
                    slowCount=RoRItemProcs.slowApplications;target.damageCooldownTime=0;p.resetAttackStrengthTicker();p.attack(target);at=p.tickCount+20;phase=12;
                } else if(phase==12 && p.tickCount>=at){
                    if(RoRItemProcs.slowApplications!=slowCount || Math.abs(target.getAttributeValue(Attributes.MOVEMENT_SPEED)-speed)>1e-6)throw new IllegalStateException("No-item hit slowed mob");
                    Files.writeString(folder.resolve("rorcraft-slow-negative.txt"),"PASS native Chronobauble removed: actual hit creates no slow, original movement speed preserved");phase=4;
                }
            }catch(Throwable error){phase=4;try{Files.writeString(folder.resolve("rorcraft-combat-failure.txt"),"FAIL "+error);}catch(Exception ignored){}}
        });
    }
}
