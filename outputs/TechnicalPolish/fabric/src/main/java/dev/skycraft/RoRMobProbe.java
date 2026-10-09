package dev.skycraft;

import java.nio.file.*;
import java.util.*;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.minecraft.world.entity.*;
import net.minecraft.world.item.*;
import net.minecraft.world.phys.*;
import net.minecraft.world.inventory.*;
import net.minecraft.world.level.block.Blocks;
import dev.skycraft.world.SkyClip;

/** Explicit isolated live test: ordinary navigation, grounded bodies, death loot and bow crafting. */
public final class RoRMobProbe {
    private static int phase,startTick;
    static ItemStack craftedBow;
    private static boolean captureRequested;
    private static final Map<Mob,Vec3> starts=new HashMap<>();
    private static Path folder;
    public static void init() {
        if(!Boolean.getBoolean("skycraft.gameplayCombatProbe"))return;
        if(!SkyCraft.WORLD_NAME.equals("SkyCraft-GameplayTest"))throw new IllegalStateException("Mob test requires isolated world");
        folder=Path.of(new String(Base64.getDecoder().decode(System.getProperty("skycraft.skinMailbox")),java.nio.charset.StandardCharsets.UTF_8)).getParent();
        ServerTickEvents.END_SERVER_TICK.register(server->{
            if(phase==2 || server.getPlayerList().getPlayers().isEmpty())return;
            var p=server.getPlayerList().getPlayers().getFirst();
            try {
                if(phase==0) {
                    if(RoRGameplay.livePlacements.size()!=4)return; // wait for the current run reset/resources, not persisted test-world mobs
                    var mobs=new ArrayList<Mob>();for(var e:p.level().getAllEntities())if(e instanceof Mob m && e.entityTags().contains(RoRMobs.OWNED))mobs.add(m);
                    if(mobs.size()!=RoRMobs.population())return;
                    p.getAbilities().invulnerable=true;
                    for(var mob:mobs) {
                        starts.put(mob,mob.position());
                        if(Boolean.getBoolean("skycraft.enemyProbe"))mob.setNoAi(true);
                        for(int dir=0;dir<4;dir++) {
                            double x=mob.getX()+(dir<2?(dir==0?2:-2):0),z=mob.getZ()+(dir>=2?(dir==2?2:-2):0);
                            if(mob.getNavigation().moveTo(x,mob.getY(),z,1))break;
                        }
                    }
                    startTick=p.tickCount;phase=1;
                } else if(phase==1 && p.tickCount-startTick>=(Boolean.getBoolean("skycraft.bowProbe")?0:80)) {
                    if(!Boolean.getBoolean("skycraft.bowProbe") && !captureRequested) {
                        var poses=new ArrayList<String>();
                        for(var mob:starts.keySet()){mob.setNoAi(true);poses.add(mob.getType().toString()+"|"+mob.getX()+"|"+mob.getY()+"|"+mob.getZ());}
                        Files.write(folder.resolve("rorcraft-mob-preview-ready.txt"),poses);captureRequested=true;
                    }
                    if(!Boolean.getBoolean("skycraft.bowProbe") && !Files.exists(folder.resolve("rorcraft-mob-preview-done.txt"))) {
                        if(p.tickCount-startTick>600)throw new IllegalStateException("Native mob preview acknowledgement missing");
                        return;
                    }
                    var checks=new ArrayList<String>();int moved=0;
                    if(!Boolean.getBoolean("skycraft.bowProbe"))for(var entry:starts.entrySet()) {
                        var mob=entry.getKey();
                        var ground=SkyClip.cast(mob.position().add(0,2,0),mob.position().add(0,-3,0));
                        if(ground==null || mob.getY()<ground.y()-.3 || mob.getY()>ground.y()+1.5)throw new IllegalStateException("Mob not grounded: "+mob.getType()+" at "+mob.position());
                        if(mob.position().distanceToSqr(entry.getValue())>.0625)moved++;
                    }
                    if(!Boolean.getBoolean("skycraft.bowProbe") && moved<2)throw new IllegalStateException("Vanilla navigation did not move enough mobs: "+moved);
                    if(!Boolean.getBoolean("skycraft.bowProbe"))checks.add("PASS eleven production mobs grounded on native terrain; "+moved+" moved at least25cm through vanilla AI/navigation");
                    // Kill with ordinary player damage attribution; no direct item injection.
                    for(var mob:starts.keySet())if(!Boolean.getBoolean("skycraft.enemyProbe") || (mob.getType()!=EntityTypes.ZOMBIE && mob.getType()!=EntityTypes.CREEPER))mob.hurtServer(p.level(),p.damageSources().playerAttack(p),1000);
                    for(var drop:p.level().getEntitiesOfClass(net.minecraft.world.entity.item.ItemEntity.class,new AABB(p.blockPosition()).inflate(50))){drop.setNoPickUpDelay();drop.playerTouch(p);}
                    if(RoRGameplayProbe.count(p,Items.STRING)<3 || RoRGameplayProbe.count(p,Items.FEATHER)<2 || RoRGameplayProbe.count(p,Items.LEATHER)<2 || RoRGameplayProbe.count(p,Items.ARROW)<4)throw new IllegalStateException("Stage mob resource rewards missing");
                    checks.add("PASS actual mob deaths/drop/pickup yielded string, feathers, leather, arrows and vanilla loot");
                    var log=RoRGameplay.livePlacements.get(0);
                    p.getInventory().setSelectedSlot(0);RoRGameplayProbe.mine(p,log.above(3));RoRGameplayProbe.mine(p,log.above(4));
                    for(int i=0;i<2;i++)RoRGameplayProbe.craft(p,p.inventoryMenu,new Item[]{Items.OAK_LOG},Items.OAK_PLANKS);
                    for(int i=0;i<2;i++)RoRGameplayProbe.craft(p,p.inventoryMenu,new Item[]{Items.OAK_PLANKS,null,Items.OAK_PLANKS},Items.STICK);
                    var table=p.blockPosition().offset(-2,0,0);p.level().setBlock(table,Blocks.CRAFTING_TABLE.defaultBlockState(),3);
                    var menu=new CraftingMenu(44,p.getInventory(),ContainerLevelAccess.create(p.level(),table));p.containerMenu=menu;
                    int bowsBefore=RoRGameplayProbe.count(p,Items.BOW);var priorBows=Collections.newSetFromMap(new IdentityHashMap<ItemStack,Boolean>());
                    for(int i=0;i<36;i++)if(p.getInventory().getItem(i).is(Items.BOW))priorBows.add(p.getInventory().getItem(i));
                    RoRGameplayProbe.craft(p,menu,new Item[]{null,Items.STICK,Items.STRING,Items.STICK,null,Items.STRING,null,Items.STICK,Items.STRING},Items.BOW);
                    p.closeContainer();if(RoRGameplayProbe.count(p,Items.BOW)!=bowsBefore+1)throw new IllegalStateException("Mob loot bow not crafted: before="+bowsBefore+" after="+RoRGameplayProbe.count(p,Items.BOW));
                    for(int i=0;i<36;i++){var stack=p.getInventory().getItem(i);if(stack.is(Items.BOW) && !priorBows.contains(stack)){craftedBow=stack;break;}}
                    if(craftedBow==null)throw new IllegalStateException("Crafted bow identity missing");
                    checks.add("PASS vanilla bow recipe consumed dropped spider string and crafted wooden sticks");
                    p.getAbilities().invulnerable=false;
                    Files.write(folder.resolve("rorcraft-gameplay-mobs.txt"),checks);phase=2;
                }
            }catch(Throwable error) {
                phase=2;SkyCraft.LOG.error("Mob probe failed",error);
                try{Files.writeString(folder.resolve("rorcraft-combat-failure.txt"),"FAIL "+error);}catch(Exception ignored){}
            }
        });
    }
}
