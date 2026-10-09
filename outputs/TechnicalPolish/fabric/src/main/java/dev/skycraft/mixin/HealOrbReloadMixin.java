package dev.skycraft.mixin;
import dev.skycraft.RoRItemProcs;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.entity.player.Player;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
/** Old saved temporary heal entities cannot turn into ordinary inventory slime balls. */
@Mixin(ItemEntity.class)
public abstract class HealOrbReloadMixin {
    @Inject(method="playerTouch",at=@At("HEAD"),cancellable=true)
    private void rorcraft$temporaryHeal(Player player,CallbackInfo ci){
        var item=(ItemEntity)(Object)this;
        if(item.entityTags().contains(RoRItemProcs.HEAL_TAG) && !RoRItemProcs.knownHealOrb(item)){item.discard();ci.cancel();}
    }
}
