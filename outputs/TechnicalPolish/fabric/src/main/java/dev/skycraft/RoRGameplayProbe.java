package dev.skycraft;

import java.nio.file.*;
import java.util.*;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.inventory.*;
import net.minecraft.world.item.*;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.phys.AABB;

/** Explicit developer flag + separate world only. No input injection or normal-world tests. */
public final class RoRGameplayProbe {
    private static boolean done;
    public static void init() {
        boolean live=Boolean.getBoolean("skycraft.gameplayLiveProbe");
        if(!Boolean.getBoolean("skycraft.gameplayProbe") && !live)return;
        String report=Path.of(new String(Base64.getDecoder().decode(System.getProperty("skycraft.skinMailbox")),java.nio.charset.StandardCharsets.UTF_8)).getParent().resolve(live?"rorcraft-gameplay-live-probe.txt":"rorcraft-gameplay-probe.txt").toString();
        if(!SkyCraft.WORLD_NAME.equals("SkyCraft-GameplayTest"))throw new IllegalStateException("Gameplay probe requires isolated world");
        ServerTickEvents.END_SERVER_TICK.register(server->{
            if(done || server.getPlayerList().getPlayers().isEmpty())return;
            var player=server.getPlayerList().getPlayers().getFirst();
            if(player.tickCount<40)return;
            if(live && RoRGameplay.livePlacements.size()!=4)return;
            done=true;var checks=new ArrayList<String>();
            try {if(live)liveCheck(player,checks);else run(player,checks);}catch(Throwable error){checks.add("FAIL "+error);SkyCraft.LOG.error("Gameplay probe failed",error);}
            try {Files.write(Path.of(report),checks);}catch(Exception error){throw new RuntimeException(error);}
        });
    }
    private static void liveCheck(ServerPlayer p,List<String> checks) {
        require(count(p,Items.WOODEN_PICKAXE)==1 && count(p,Items.DIAMOND_PICKAXE)==0,"Live starter inventory wrong");
        for(var entry:RoRGameplay.livePlacements.entrySet()) {
            var pos=entry.getValue();require(!p.level().isEmptyBlock(pos),"Live resource disappeared");
            var ground=dev.skycraft.world.SkyClip.cast(new net.minecraft.world.phys.Vec3(pos.getX()+.5,pos.getY()+2,pos.getZ()+.5),new net.minecraft.world.phys.Vec3(pos.getX()+.5,pos.getY()-2,pos.getZ()+.5));
            require(ground!=null && ground.ny()>=.85,"Resource has no exported native terrain support");
            checks.add("PASS resource "+entry.getKey()+" at "+pos+" on actual RoR2 terrain y="+ground.y());
        }
        checks.add("PASS live RoR2 takeover generated four resource patches and survival inventory through production server tick");
    }
    private static void require(boolean ok,String message){if(!ok)throw new IllegalStateException(message);}
    private static void run(ServerPlayer p,List<String> checks) {
        p.setNoGravity(true);p.getAbilities().invulnerable=true;
        p.setPos(0,65,0);
        // Purge only this explicitly isolated fixture region from earlier test runs.
        for(int x=-2;x<24;x++)for(int z=-2;z<8;z++)for(int y=63;y<72;y++)p.level().setBlock(new BlockPos(x,y,z),(y==63?Blocks.BEDROCK:Blocks.AIR).defaultBlockState(),3);
        for(var entity:p.level().getEntitiesOfClass(ItemEntity.class,new AABB(-3,60,-3,25,75,9)))entity.discard();
        int token=(int)(System.nanoTime()&0x7fffffff);if(token==0)token=1;
        p.getInventory().add(new ItemStack(Items.DIAMOND,64));
        p.setItemSlot(EquipmentSlot.HEAD,new ItemStack(Items.DIAMOND_HELMET));
        require(RoRGameplay.beginRun(p,token),"Fresh run not initialized");
        require(count(p,Items.DIAMOND)==0 && p.getItemBySlot(EquipmentSlot.HEAD).isEmpty(),"Old inventory/armor leaked");
        require(count(p,Items.WOODEN_PICKAXE)==1 && count(p,Items.WOODEN_SWORD)==1 && count(p,Items.COOKED_BEEF)==8,"Starter loadout missing");
        p.getInventory().add(new ItemStack(Items.EMERALD,3));
        require(!RoRGameplay.beginRun(p,token) && count(p,Items.EMERALD)==3,"Same run/refreshed player inventory was reset");
        checks.add("PASS fresh survival inventory clears old equipment; repeated same-run call preserves acquired items");
        for(int node=0;node<4;node++)require(RoRGameplay.placePatch(p,new BlockPos(2+node*5,64,2),node),"Resource patch failed "+node);
        require(!RoRGameplay.placePatch(p,new BlockPos(2,64,2),0),"Resource patch overwrites existing blocks");
        p.getInventory().setSelectedSlot(0);
        mine(p,new BlockPos(2,64,2));
        require(count(p,Items.OAK_LOG)>0,"Mined log not picked up");
        craft(p,p.inventoryMenu,new Item[]{Items.OAK_LOG},Items.OAK_PLANKS);
        require(count(p,Items.OAK_PLANKS)==4,"Log recipe did not produce four planks");
        craft(p,p.inventoryMenu,new Item[]{Items.OAK_PLANKS,Items.OAK_PLANKS,Items.OAK_PLANKS,Items.OAK_PLANKS},Items.CRAFTING_TABLE);
        mine(p,new BlockPos(2,65,2));
        craft(p,p.inventoryMenu,new Item[]{Items.OAK_LOG},Items.OAK_PLANKS);
        craft(p,p.inventoryMenu,new Item[]{Items.OAK_PLANKS,null,Items.OAK_PLANKS},Items.STICK);
        p.getInventory().setSelectedSlot(1);
        for(int i=0;i<16;i++)mine(p,new BlockPos(7+i/8,64+i%4,2+(i/4)%2));
        require(count(p,Items.COBBLESTONE)==16,"Stone deposit must cover tools and furnace");
        var table=new BlockPos(0,64,2);p.level().setBlock(table,Blocks.CRAFTING_TABLE.defaultBlockState(),3);
        var menu=new CraftingMenu(42,p.getInventory(),ContainerLevelAccess.create(p.level(),table));p.containerMenu=menu;
        craft(p,menu,new Item[]{Items.COBBLESTONE,Items.COBBLESTONE,Items.COBBLESTONE,null,Items.STICK,null,null,Items.STICK},Items.STONE_PICKAXE);
        craft(p,menu,new Item[]{Items.COBBLESTONE,null,null,Items.COBBLESTONE,null,null,Items.STICK},Items.STONE_SWORD);
        craft(p,menu,new Item[]{Items.COBBLESTONE,Items.COBBLESTONE,Items.COBBLESTONE,Items.COBBLESTONE,null,Items.COBBLESTONE,Items.COBBLESTONE,Items.COBBLESTONE,Items.COBBLESTONE},Items.FURNACE);
        require(count(p,Items.FURNACE)==1 && count(p,Items.COBBLESTONE)==3,"Deposit budget or furnace recipe failed");
        require(count(p,Items.STONE_SWORD)==1 && count(p,Items.STONE_PICKAXE)==1,"Crafted tools absent");
        checks.add("PASS vanilla mining drops/pickup -> 2x2 planks/sticks/table -> 3x3 stone pickaxe, sword and furnace; ingredients consumed, three cobblestone spare");
        // A wooden pick must not provide iron; a crafted stone pick must.
        p.getInventory().setSelectedSlot(1);mine(p,new BlockPos(17,64,2));require(count(p,Items.RAW_IRON)==0,"Wood pick bypassed iron tool requirement");
        for(int i=0;i<36;i++)if(p.getInventory().getItem(i).is(Items.STONE_PICKAXE)){var item=p.getInventory().getItem(i);p.getInventory().setItem(i,p.getInventory().getItem(1));p.getInventory().setItem(1,item);break;}
        mine(p,new BlockPos(18,64,2));require(count(p,Items.RAW_IRON)==1,"Stone pick failed iron drop");
        mine(p,new BlockPos(12,64,2));require(count(p,Items.COAL)==1,"Coal loot failed");
        require(p.getMainHandItem().getDamageValue()>0,"Mining did not consume tool durability");
        checks.add("PASS vanilla pickaxe tier gates iron; coal/iron drops and crafted tool durability");
        require(!RoRGameplay.beginRun(p,token) && count(p,Items.STONE_SWORD)==1,"Stage-equivalent same run lost crafted sword");
        p.containerMenu.setCarried(new ItemStack(Items.DIAMOND,2));
        require(RoRGameplay.beginRun(p,token+1) && count(p,Items.STONE_SWORD)==0 && count(p,Items.DIAMOND)==0,"New-run inventory/cursor reset failed");
        checks.add("PASS next run clears crafted loot and carried cursor; same run keeps progression");
        checks.add("Scope: real Minecraft integrated server inventory/mining/crafting. Native RoR2 terrain placement, stage transition and proc effects require separate host verification.");
    }
    static int count(ServerPlayer p,Item item){int n=0;for(int i=0;i<36;i++)if(p.getInventory().getItem(i).is(item))n+=p.getInventory().getItem(i).getCount();return n;}
    static void mine(ServerPlayer p,BlockPos pos) {
        require(p.gameMode.destroyBlock(pos),"Mining refused "+pos);
        for(var drop:p.level().getEntitiesOfClass(ItemEntity.class,new AABB(pos).inflate(2))){drop.setNoPickUpDelay();drop.playerTouch(p);}
    }
    static void craft(ServerPlayer p,AbstractCraftingMenu menu,Item[] recipe,Item expected) {
        var slots=menu.getInputGridSlots();
        for(int i=0;i<slots.size();i++) {
            Item item=i<recipe.length?recipe[i]:null;ItemStack input=ItemStack.EMPTY;
            if(item!=null)for(int j=0;j<36;j++)if(p.getInventory().getItem(j).is(item)){input=p.getInventory().removeItem(j,1);break;}
            require(item==null || !input.isEmpty(),"Missing recipe ingredient "+item);
            slots.get(i).set(input);
        }
        menu.slotsChanged(slots.getFirst().container);
        require(menu.getResultSlot().getItem().is(expected),"Vanilla recipe output mismatch: "+expected+" got "+menu.getResultSlot().getItem());
        menu.quickMoveStack(p,0);
        require(slots.stream().allMatch(slot->slot.getItem().isEmpty()),"Recipe did not consume ingredients");
    }
}
