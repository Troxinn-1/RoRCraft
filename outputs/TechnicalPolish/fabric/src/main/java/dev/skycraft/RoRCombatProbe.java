package dev.skycraft;

import dev.skycraft.combat.SkyCombat;
import dev.skycraft.link.SkyLink;
import java.lang.foreign.ValueLayout;
import java.nio.file.*;
import java.util.*;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.minecraft.core.BlockPos;
import net.minecraft.world.inventory.*;
import net.minecraft.world.item.*;
import net.minecraft.world.level.block.Blocks;

/** Real vanilla attack -> normal combat ring -> native victim. Explicit isolated test only. */
public final class RoRCombatProbe {
    private static int phase,readyTick,token,arrowsAfterShot,bowsAfterShot;
    static void spentAdditionalArrow(){arrowsAfterShot--;}
    private static Path folder;
    private static net.minecraft.world.entity.monster.Creeper creeper;
    private static net.minecraft.core.BlockPos blastBlock;
    private static int enemyStart;
    public static void init() {
        if(!Boolean.getBoolean("skycraft.gameplayCombatProbe"))return;
        if(!SkyCraft.WORLD_NAME.equals("SkyCraft-GameplayTest"))throw new IllegalStateException("Combat test requires isolated world");
        folder=Path.of(new String(Base64.getDecoder().decode(System.getProperty("skycraft.skinMailbox")),java.nio.charset.StandardCharsets.UTF_8)).getParent();
        ServerTickEvents.END_SERVER_TICK.register(server->{
            if(phase==5 || server.getPlayerList().getPlayers().isEmpty())return;
            var p=server.getPlayerList().getPlayers().getFirst();
            try {
                if(phase==0) {
                    var request=folder.resolve("rorcraft-combat-target.txt");
                    if(RoRGameplay.livePlacements.size()!=4 || !Files.exists(request))return;
                    int id=Integer.parseUnsignedInt(Files.readString(request).trim());
                    var target=SkyCombat.proxy(id);if(target==null)return;
                    token=SkyLink.segment().get(ValueLayout.JAVA_INT,0x940);
                    p.setNoGravity(true);
                    var log=RoRGameplay.livePlacements.get(0);var stone=RoRGameplay.livePlacements.get(1);
                    p.getInventory().setSelectedSlot(0);
                    RoRGameplayProbe.mine(p,log);RoRGameplayProbe.mine(p,log.above());
                    RoRGameplayProbe.craft(p,p.inventoryMenu,new Item[]{Items.OAK_LOG},Items.OAK_PLANKS);
                    RoRGameplayProbe.craft(p,p.inventoryMenu,new Item[]{Items.OAK_PLANKS,Items.OAK_PLANKS,Items.OAK_PLANKS,Items.OAK_PLANKS},Items.CRAFTING_TABLE);
                    RoRGameplayProbe.craft(p,p.inventoryMenu,new Item[]{Items.OAK_LOG},Items.OAK_PLANKS);
                    RoRGameplayProbe.craft(p,p.inventoryMenu,new Item[]{Items.OAK_PLANKS,null,Items.OAK_PLANKS},Items.STICK);
                    p.getInventory().setSelectedSlot(1);
                    int mined=0;
                    for(int dx=0;dx<2 && mined<5;dx++)for(int dz=0;dz<2 && mined<5;dz++)for(int y=-3;y<7 && mined<5;y++) {
                        var pos=stone.offset(dx,y,dz);if(p.level().getBlockState(pos).is(Blocks.STONE)){RoRGameplayProbe.mine(p,pos);mined++;}
                    }
                    if(mined!=5)throw new IllegalStateException("Live terrain deposit yielded fewer than five stones");
                    var table=p.blockPosition().offset(2,0,0);
                    if(!p.level().isEmptyBlock(table))throw new IllegalStateException("Test crafting table cell occupied");
                    p.level().setBlock(table,Blocks.CRAFTING_TABLE.defaultBlockState(),3);
                    var menu=new CraftingMenu(43,p.getInventory(),ContainerLevelAccess.create(p.level(),table));p.containerMenu=menu;
                    RoRGameplayProbe.craft(p,menu,new Item[]{Items.COBBLESTONE,Items.COBBLESTONE,Items.COBBLESTONE,null,Items.STICK,null,null,Items.STICK},Items.STONE_PICKAXE);
                    RoRGameplayProbe.craft(p,menu,new Item[]{Items.COBBLESTONE,null,null,Items.COBBLESTONE,null,null,Items.STICK},Items.STONE_SWORD);
                    p.closeContainer();
                    for(int i=0;i<36;i++)if(p.getInventory().getItem(i).is(Items.STONE_SWORD)){var item=p.getInventory().getItem(i);p.getInventory().setItem(i,p.getInventory().getItem(0));p.getInventory().setItem(0,item);break;}
                    p.getInventory().setSelectedSlot(0);
                    p.setPos(target.getX(),target.getY(),target.getZ()-1.5);
                    p.resetAttackStrengthTicker();readyTick=p.tickCount+30;phase=1;
                } else if(phase==1 && p.tickCount>=readyTick) {
                    int id=Integer.parseUnsignedInt(Files.readString(folder.resolve("rorcraft-combat-target.txt")).trim());
                    var target=SkyCombat.proxy(id);if(target==null)return;
                    p.setOnGround(true);p.setSprinting(false);
                    p.attack(target); // No manually fabricated hit event or native damage call.
                    var field=target.getClass().getDeclaredField("pendingDamage");field.setAccessible(true);
                    float damage=field.getFloat(target);
                    if(damage<=0 || !p.getMainHandItem().is(Items.STONE_SWORD))throw new IllegalStateException("Crafted sword did not produce vanilla melee damage");
                    Files.write(folder.resolve("rorcraft-combat-mc.txt"),List.of("PASS crafted stone sword from live terrain resources attacked exported native proxy with vanilla Player.attack",Float.toString(damage)));
                    phase=2;
                } else if(phase==2 && Files.exists(folder.resolve("rorcraft-arrow-request.txt"))) {
                    int id=Integer.parseUnsignedInt(Files.readString(folder.resolve("rorcraft-combat-target.txt")).trim());
                    var target=SkyCombat.proxy(id);if(target==null)return;
                    for(int i=0;i<36;i++)if(p.getInventory().getItem(i)==RoRMobProbe.craftedBow){var item=p.getInventory().getItem(i);p.getInventory().setItem(i,p.getInventory().getItem(0));p.getInventory().setItem(0,item);break;}
                    p.getInventory().setSelectedSlot(0);
                    p.setPos(target.getX(),target.getY(),target.getZ()-4);
                    var delta=target.getBoundingBox().getCenter().subtract(p.getEyePosition());
                    p.setYRot((float)Math.toDegrees(Math.atan2(-delta.x,delta.z)));
                    p.setXRot((float)-Math.toDegrees(Math.atan2(delta.y,Math.sqrt(delta.x*delta.x+delta.z*delta.z))));
                    var bow=p.getMainHandItem();if(!bow.is(Items.BOW))throw new IllegalStateException("Crafted bow missing");
                    int arrows=RoRGameplayProbe.count(p,Items.ARROW);
                    p.startUsingItem(net.minecraft.world.InteractionHand.MAIN_HAND);
                    ((BowItem)bow.getItem()).releaseUsing(bow,p.level(),p,bow.getUseDuration(p)-20);
                    p.stopUsingItem();
                    if(RoRGameplayProbe.count(p,Items.ARROW)!=arrows-1)throw new IllegalStateException("Vanilla bow did not consume one loot arrow");
                    bowsAfterShot=RoRGameplayProbe.count(p,Items.BOW);arrowsAfterShot=arrows-1;readyTick=p.tickCount+100;phase=3;
                } else if(phase==3 && p.tickCount>readyTick) {
                    throw new IllegalStateException("Vanilla arrow missed exported enemy");
                } else if(phase==4 && Boolean.getBoolean("skycraft.enemyProbe") && Files.exists(folder.resolve("rorcraft-enemy-request.txt"))) {
                    net.minecraft.world.entity.monster.zombie.Zombie zombie=null;
                    for(var e:p.level().getAllEntities())if(e!=null && e.entityTags().contains(RoRMobs.OWNED)) {
                        if(e instanceof net.minecraft.world.entity.monster.Creeper c)creeper=c;
                        if(e instanceof net.minecraft.world.entity.monster.zombie.Zombie z)zombie=z;
                    }
                    if(creeper==null || zombie==null)throw new IllegalStateException("Production zombie/creeper missing");
                    var target=SkyCombat.proxy(Integer.parseUnsignedInt(Files.readString(folder.resolve("rorcraft-combat-target.txt")).trim()));
                    p.setPos(target.getX(),target.getY(),target.getZ()-1.5);
                    zombie.snapTo(p.getX()+1,p.getY(),p.getZ(),0,0);
                    float hp=p.getHealth();zombie.doHurtTarget(p.level(),p);
                    if(p.getHealth()>=hp)throw new IllegalStateException("Vanilla zombie melee did not hurt MC player");
                    creeper.snapTo(target.getX()+1,target.getY(),target.getZ(),0,0);
                    blastBlock=null;
                    for(var offset:new int[][]{{0,1,1},{0,1,-1},{1,1,0},{-1,1,0},{0,2,0}}){var cell=creeper.blockPosition().offset(offset[0],offset[1],offset[2]);if(p.level().isEmptyBlock(cell)){blastBlock=cell;break;}}
                    if(blastBlock==null)throw new IllegalStateException("No empty nearby explosion fixture cell; preserved existing blocks");
                    p.level().setBlock(blastBlock,Blocks.DIRT.defaultBlockState(),3);
                    Files.write(folder.resolve("rorcraft-enemy-ready.txt"),List.of(zombie.getType()+"|"+zombie.getX()+"|"+zombie.getY()+"|"+zombie.getZ(),creeper.getType()+"|"+creeper.getX()+"|"+creeper.getY()+"|"+creeper.getZ()));
                    enemyStart=p.tickCount;phase=6;
                } else if(phase==6 && Files.exists(folder.resolve("rorcraft-enemy-done.txt"))) {
                    creeper.ignite();enemyStart=p.tickCount;phase=7;
                } else if(phase==7 && p.tickCount-enemyStart>45) {
                    if(!creeper.isRemoved() || !p.level().isEmptyBlock(blastBlock))throw new IllegalStateException("Actual creeper explosion did not destroy fixture dirt block");
                    Files.writeString(folder.resolve("rorcraft-enemy-mc.txt"),"PASS production zombie vanilla melee damaged MC player; actual creeper fuse/explosion removed owned creeper and destroyed placed dirt");
                    phase=4;Files.deleteIfExists(folder.resolve("rorcraft-enemy-request.txt"));
                } else if(phase==4 && RoRGameplay.liveStage>1 && RoRGameplay.livePlacements.size()==4) {
                    if(SkyLink.segment().get(ValueLayout.JAVA_INT,0x940)!=token)throw new IllegalStateException("Stage advance changed run token");
                    if(RoRGameplayProbe.count(p,Items.STONE_SWORD)!=1 || RoRGameplayProbe.count(p,Items.STONE_PICKAXE)!=1)throw new IllegalStateException("Stage advance lost crafted gear");
                    if(RoRGameplayProbe.count(p,Items.BOW)!=bowsAfterShot || RoRGameplayProbe.count(p,Items.ARROW)!=arrowsAfterShot)throw new IllegalStateException("Stage advance lost bow or changed arrow inventory");
                    Files.write(folder.resolve("rorcraft-gameplay-stage.txt"),List.of("PASS actual stage "+RoRGameplay.liveStage+" generated four new deposits and kept crafted sword/pickaxe/bow, remaining arrows and run token"));
                    phase=5;
                }
            }catch(Throwable error) {
                phase=5;SkyCraft.LOG.error("Combat/stage probe failed",error);
                try{Files.writeString(folder.resolve("rorcraft-combat-failure.txt"),"FAIL "+error);}catch(Exception ignored){}
            }
        });
    }
    public static void recordArrowCollision(dev.skycraft.combat.SkyrimActorEntity entity) {
        if(phase!=3 || folder==null)return;
        try {
            int id=Integer.parseUnsignedInt(Files.readString(folder.resolve("rorcraft-combat-target.txt")).trim());
            if(entity.formId()!=id)return;
            var field=entity.getClass().getDeclaredField("pendingDamage");field.setAccessible(true);
            float damage=field.getFloat(entity);
            var flags=entity.getClass().getDeclaredField("pendingFlags");flags.setAccessible(true);
            if(damage<=0 || (flags.getInt(entity)&dev.skycraft.link.Proto.HIT_PROJECTILE)==0)return;
            Files.write(folder.resolve("rorcraft-arrow-mc.txt"),List.of("PASS crafted vanilla bow released loot arrow; actual projectile collision reached exported native enemy",Float.toString(damage)));
            phase=4;
        }catch(Throwable error){phase=5;try{Files.writeString(folder.resolve("rorcraft-combat-failure.txt"),"FAIL "+error);}catch(Exception ignored){}}
    }

}
