using System;
using HarmonyLib;
using RoR2;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace RoRCraftPolish {
    // Only the linked player's native enemy hits: no global enemy/item modifications.
    internal static class SurvivalBalance {
        internal static void Install(Harmony harmony) {
            // A20..BFF is HUD text; C00 onwards belongs to gameplay effects/monster
            // scaling. The preserved host allowed 1024 bytes, overlapping those fields.
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("RoRCraft.RoRCraftPlugin"),"RefreshUpgradeText"),
                transpiler:new HarmonyMethod(typeof(SurvivalBalance),"BoundHudText"));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("RoRCraft.RoRCraftPlugin"),"OnIncomingDamageServer"),
                postfix:new HarmonyMethod(typeof(SurvivalBalance),"Incoming"));
        }
        private static IEnumerable<CodeInstruction> BoundHudText(IEnumerable<CodeInstruction> code) {
            foreach(var instruction in code) {
                if(instruction.opcode==OpCodes.Ldc_I4 && (int)instruction.operand==1024)instruction.operand=480;
                yield return instruction;
            }
        }
        internal static float EquipmentMultiplier(float damage,float maximum,float armor,float toughness) {
            float mcDamage=damage*20/Mathf.Max(1,maximum);
            float protection=Mathf.Clamp(armor-mcDamage/(2+toughness/4),armor*.2f,20);
            return 1-protection/25;
        }
        private static void Incoming(object __instance,DamageInfo __0,bool ___active,CharacterBody ___controlled) {
            if(!___active || ___controlled==null || __0==null || __0.rejected || __0.damage<=0 || __0.attacker==null)return;
            // Hazards/void/fall and damage over time retain their original rules.
            if(__0.dotIndex!=DotController.DotIndex.None || (__0.damageType&(DamageType.BypassArmor|DamageType.BypassBlock|DamageType.FallDamage|DamageType.VoidDeath|DamageType.OutOfBounds))!=0)return;
            var attacker=__0.attacker.GetComponent<CharacterBody>();
            if(attacker==null || attacker.teamComponent==null || ___controlled.teamComponent==null || attacker.teamComponent.teamIndex==___controlled.teamComponent.teamIndex)return;
            var memory=(RoRCraft.SkyMemory)AccessTools.Field(__instance.GetType(),"link").GetValue(__instance);
            if(memory==null)return;
            int sequence=memory.I(0x980);
            if((sequence&1)!=0 || memory.I(0x984)!=0x31524F52 || memory.I(0x9AC)!=0x31465244)return;
            System.Threading.Thread.MemoryBarrier();float armor=memory.F(0x9A4),toughness=memory.F(0x9A8);
            System.Threading.Thread.MemoryBarrier();if(sequence!=memory.I(0x980) || float.IsNaN(armor) || float.IsNaN(toughness))return;
            // First-person melee needs time to close distance. Retain native difficulty
            // scaling, armor, items, crits and lethal damage; this is not an HP floor.
            float damage=__0.damage*.65f;
            __0.damage=damage*EquipmentMultiplier(damage,___controlled.maxHealth,Mathf.Clamp(armor,0,30),Mathf.Clamp(toughness,0,20));
        }
    }
}
