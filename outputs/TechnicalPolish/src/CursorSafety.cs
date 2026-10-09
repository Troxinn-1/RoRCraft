using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;

namespace RoRCraftPolish {
    // Simulated focus is never permission to capture the desktop pointer.
    public static class CursorSafety {
        public static bool BackgroundTest;
        private static readonly uint processId=(uint)Process.GetCurrentProcess().Id;
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        public static bool OwnsFocus {
            get {
                if(!Application.isFocused) return false;
                if(Application.platform!=RuntimePlatform.WindowsPlayer) return true;
                uint pid;GetWindowThreadProcessId(GetForegroundWindow(),out pid);
                return pid==processId;
            }
        }
        public static void SetLock(CursorLockMode value) {
            if(OwnsFocus) Cursor.lockState=BackgroundTest?CursorLockMode.None:value;
        }
        public static void SetVisible(bool value) {
            if(OwnsFocus) Cursor.visible=BackgroundTest || value;
        }
        public static void Install(Harmony harmony) {
            foreach(var target in new[]{
                AccessTools.Method("RoRCraft.RoRCraftPlugin:InputToMinecraft"),
                AccessTools.Method("RoRCraft.RoRCraftPlugin:End"),
                AccessTools.Method("RoR2.RoR2Application:UpdateCursorState"),
                AccessTools.Method("RoR2.MPEventSystemManager:Update")}) {
                if(target==null) throw new MissingMethodException("Required cursor owner method unavailable.");
                harmony.Patch(target,transpiler:new HarmonyMethod(typeof(CursorSafety),"GuardSetters"));
            }
        }
        private static IEnumerable<CodeInstruction> GuardSetters(IEnumerable<CodeInstruction> instructions) {
            var lockSetter=AccessTools.PropertySetter(typeof(Cursor),"lockState");
            var visibleSetter=AccessTools.PropertySetter(typeof(Cursor),"visible");
            foreach(var instruction in instructions) {
                if(instruction.Calls(lockSetter)) {instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(CursorSafety),"SetLock");}
                else if(instruction.Calls(visibleSetter)) {instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(CursorSafety),"SetVisible");}
                yield return instruction;
            }
        }
    }
}
