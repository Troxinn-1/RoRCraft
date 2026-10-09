package dev.skycraft.mixin;

import dev.skycraft.RoRCombatProbe;
import dev.skycraft.combat.SkyrimActorEntity;
import net.minecraft.world.entity.projectile.arrow.AbstractArrow;
import net.minecraft.world.phys.EntityHitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Read-only observer for the explicitly enabled isolated bow test; never generates a hit. */
@Mixin(AbstractArrow.class)
public abstract class ArrowProbeMixin {
    @Inject(method="onHitEntity",at=@At("RETURN"))
    private void rorcraft$observeArrow(EntityHitResult hit,CallbackInfo ci) {
        if(Boolean.getBoolean("skycraft.gameplayCombatProbe") && hit.getEntity() instanceof SkyrimActorEntity proxy)RoRCombatProbe.recordArrowCollision(proxy);
    }
}
