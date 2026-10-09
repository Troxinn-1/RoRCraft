package dev.skycraft.mixin;
import dev.skycraft.RoRCrowbar;
import com.llamalad7.mixinextras.injector.wrapmethod.WrapMethod;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.core.particles.ParticleTypes;
import org.spongepowered.asm.mixin.Mixin;
@Mixin(LivingEntity.class)
public abstract class RoRCrowbarMixin {
    @WrapMethod(method="hurtServer")
    private boolean rorcraft$openingStrike(ServerLevel level,DamageSource source,float amount,Operation<Boolean> original){
        LivingEntity self=(LivingEntity)(Object)this;float boosted=RoRCrowbar.openingDamage(self,source,amount);
        boolean hit=original.call(level,source,boosted);
        if(hit && boosted>amount){RoRCrowbar.applications++;level.sendParticles(ParticleTypes.DAMAGE_INDICATOR,self.getX(),self.getY()+self.getBbHeight()*.6,self.getZ(),6,.25,.3,.25,.05);}
        return hit;
    }
}
