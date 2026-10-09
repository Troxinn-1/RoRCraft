package dev.skycraft.mixin;
import dev.skycraft.RoRMobs;
import net.minecraft.world.entity.Mob;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;
/** Run encounters must survive the mirror's permanent noon without a cosmetic helmet. */
@Mixin(Mob.class)
public abstract class StageMobSunMixin {
    @Inject(method="isSunBurnTick",at=@At("HEAD"),cancellable=true)
    private void rorcraft$stageSun(CallbackInfoReturnable<Boolean> cir) {
        if(((Mob)(Object)this).entityTags().contains(RoRMobs.OWNED))cir.setReturnValue(false);
    }
}
