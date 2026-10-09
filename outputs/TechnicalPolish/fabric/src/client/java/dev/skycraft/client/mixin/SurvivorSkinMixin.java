package dev.skycraft.client.mixin;

import dev.skycraft.client.SurvivorSkinOverride;
import net.minecraft.client.player.AbstractClientPlayer;
import net.minecraft.world.entity.player.PlayerSkin;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(AbstractClientPlayer.class)
public abstract class SurvivorSkinMixin {
    @Inject(method="getSkin",at=@At("RETURN"),cancellable=true)
    private void rorcraft$survivorSkin(CallbackInfoReturnable<PlayerSkin> result) {
        var player=(AbstractClientPlayer)(Object)this;
        result.setReturnValue(SurvivorSkinOverride.resolve(player.getUUID(),result.getReturnValue()));
    }
}
