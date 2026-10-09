package dev.skycraft;
import dev.skycraft.link.SkyLink;
import java.lang.foreign.ValueLayout;
import java.lang.invoke.VarHandle;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.damagesource.DamageTypes;
import net.minecraft.world.entity.projectile.arrow.AbstractArrow;
/** Opening-strike bonus from the native inventory; never modifies native proxy damage. */
public final class RoRCrowbar {
    public static int applications;
    public static float lastInput,lastOutput;
    public static int count(){
        var memory=SkyLink.segment();if(memory==null || !SkyLink.active())return 0;
        int sequence=memory.get(ValueLayout.JAVA_INT,0xE0C);
        if((sequence&1)!=0 || memory.get(ValueLayout.JAVA_INT,0xE08)!=0x31425243)return 0;
        VarHandle.loadLoadFence();int count=memory.get(ValueLayout.JAVA_INT,0xE00),token=memory.get(ValueLayout.JAVA_INT,0xE04);VarHandle.loadLoadFence();
        if(sequence!=memory.get(ValueLayout.JAVA_INT,0xE0C) || token!=memory.get(ValueLayout.JAVA_INT,0x940))return 0;
        return Math.max(0,Math.min(1000,count));
    }
    public static float openingDamage(LivingEntity victim,DamageSource source,float amount){
        if(!victim.entityTags().contains(RoRMobs.OWNED) || !(source.getEntity() instanceof ServerPlayer) || !(source.is(DamageTypes.PLAYER_ATTACK) || source.getDirectEntity() instanceof AbstractArrow) || !Float.isFinite(amount) || amount<=0)return amount;
        int count=count();float result=count>0 && victim.getHealth()>=victim.getMaxHealth()*.9f?amount*(1+.75f*count):amount;
        lastInput=amount;lastOutput=result;return result;
    }
}
