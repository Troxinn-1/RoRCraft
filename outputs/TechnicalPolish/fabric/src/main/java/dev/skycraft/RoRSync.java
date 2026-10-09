package dev.skycraft;

import dev.skycraft.link.SkyLink;
import java.lang.foreign.MemorySegment;
import java.lang.foreign.ValueLayout;
import java.lang.invoke.VarHandle;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.entity.ai.attributes.AttributeModifier;
import net.minecraft.resources.Identifier;

/** RoR2 stat extension in the otherwise unused v10 header area. No chat input or commands. */
public final class RoRSync {
    private static final int MAGIC=0x31524F52;
    private static final long BASE=0x900, REPLY=0x980;
    private static ServerPlayer previous;
    private static float appliedHealth;
    private static boolean hasHealth;
    private static double healthDelta;
    private static int replySequence,lastLog;
    private static int generation=-1;
    private static float appliedShieldDamage;
    private RoRSync() {}
    public static void init() { ServerTickEvents.END_SERVER_TICK.register(RoRSync::tick); }
    private static void tick(MinecraftServer server) {
        MemorySegment s=SkyLink.segment();
        if(s==null || !SkyLink.active() || s.get(ValueLayout.JAVA_INT,BASE+4)!=MAGIC) {hasHealth=false;return;}
        var stats=RoRStats.read(s);if(stats==null) {hasHealth=false;return;}
        float maximum=stats.maximum(),health=stats.health(),speed=stats.speed(),attack=stats.attack(),shield=stats.shield();
        var players=server.getPlayerList().getPlayers();if(players.isEmpty()) return;
        ServerPlayer player=players.getFirst();
        if(player!=previous || generation!=SkyLink.generation()) {previous=player;generation=SkyLink.generation();hasHealth=false;healthDelta=0;replySequence=0;appliedShieldDamage=s.get(ValueLayout.JAVA_FLOAT,0x924);}
        if(!player.isAlive()) {hasHealth=false;return;}
        // Natural MC healing or environmental damage returns to the real RoR2 body.
        if(hasHealth) {
            float change=player.getHealth()-appliedHealth;
            if(Float.isFinite(change) && Math.abs(change)>.001f) healthDelta+=change;
        }
        var hp=player.getAttribute(Attributes.MAX_HEALTH);if(hp!=null) hp.setBaseValue(maximum);
        var movement=player.getAttribute(Attributes.MOVEMENT_SPEED);if(movement!=null) movement.setBaseValue(Math.clamp(speed,.01f,1f));
        var attacks=player.getAttribute(Attributes.ATTACK_SPEED);
        if(attacks!=null) attacks.addOrUpdateTransientModifier(new AttributeModifier(Identifier.fromNamespaceAndPath("rorcraft","attack_speed"),Math.clamp(attack,.1f,16f)-1,AttributeModifier.Operation.ADD_MULTIPLIED_TOTAL));
        // Host health is already reduced by RoR2 attacks, armor and item procs.
        // Never apply that damage a second time to Minecraft's smaller health pool.
        appliedHealth=Math.clamp(health,.01f,maximum);player.setHealth(appliedHealth);
        player.setAbsorptionAmount(Float.isFinite(shield)?Math.clamp(shield,0f,1024f):0);
        hasHealth=true;
        float blocked=s.get(ValueLayout.JAVA_FLOAT,0x924),wear=blocked-appliedShieldDamage;appliedShieldDamage=blocked;
        if(Float.isFinite(wear) && wear>0 && wear<100000 && player.isBlocking()) {
            player.getUseItem().hurtAndBreak(1+(int)Math.floor(wear*20/maximum),player,player.getUsedItemHand()==net.minecraft.world.InteractionHand.MAIN_HAND?net.minecraft.world.entity.EquipmentSlot.MAINHAND:net.minecraft.world.entity.EquipmentSlot.OFFHAND);
            player.level().playSound(null,player.blockPosition(),net.minecraft.sounds.SoundEvents.SHIELD_BLOCK.value(),net.minecraft.sounds.SoundSource.PLAYERS,1f,1f);
        }
        s.set(ValueLayout.JAVA_INT,REPLY,++replySequence*2-1);VarHandle.storeStoreFence();
        s.set(ValueLayout.JAVA_DOUBLE,REPLY+8,healthDelta);
        s.set(ValueLayout.JAVA_FLOAT,REPLY+16,player.getHealth());
        s.set(ValueLayout.JAVA_FLOAT,REPLY+20,player.getMaxHealth());
        s.set(ValueLayout.JAVA_FLOAT,REPLY+24,(float)player.getAttributeValue(Attributes.MOVEMENT_SPEED));
        s.set(ValueLayout.JAVA_FLOAT,REPLY+28,(float)player.getAttributeValue(Attributes.ATTACK_SPEED));
        s.set(ValueLayout.JAVA_INT,0x9A0,player.isBlocking()?1:0);
        // Part of the same seqlock as health and blocking; native hits use MC equipment.
        s.set(ValueLayout.JAVA_FLOAT,0x9A4,(float)player.getAttributeValue(Attributes.ARMOR));
        s.set(ValueLayout.JAVA_FLOAT,0x9A8,(float)player.getAttributeValue(Attributes.ARMOR_TOUGHNESS));
        s.set(ValueLayout.JAVA_INT,0x9AC,0x31465244);
        s.set(ValueLayout.JAVA_INT,REPLY+4,MAGIC);VarHandle.storeStoreFence();s.set(ValueLayout.JAVA_INT,REPLY,replySequence*2);
        int signature=Float.floatToIntBits(maximum)^Float.floatToIntBits(speed)^Float.floatToIntBits(attack);
        if(signature!=lastLog) {lastLog=signature;SkyCraft.LOG.info("RoRCraft direct stats: maxHP {}, health {}, speed {}, attack {}",maximum,appliedHealth,speed,attack);}
    }
}
