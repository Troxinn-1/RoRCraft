// Runs without Unity or a game window. Uses the production guard and real Win32
// foreground ownership; the cursor sink is fake so no desktop pointer is changed.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoRCraftPolish;
namespace UnityEngine {
    public enum CursorLockMode {None,Locked,Confined}
    public enum RuntimePlatform {WindowsPlayer,LinuxPlayer}
    public static class Application {public static bool isFocused;public static RuntimePlatform platform;}
    public static class Cursor {
        public static int Writes;private static CursorLockMode state;private static bool shown;
        public static CursorLockMode lockState {get{return state;}set{state=value;Writes++;}}
        public static bool visible {get{return shown;}set{shown=value;Writes++;}}
    }
}
class CursorSafetyChecks {
    static void Check(bool value,string name) {if(!value) throw new Exception(name);Console.WriteLine("PASS "+name);}
    static void Main() {
        UnityEngine.Application.platform=UnityEngine.RuntimePlatform.WindowsPlayer;
        UnityEngine.Application.isFocused=false;
        CursorSafety.SetLock(UnityEngine.CursorLockMode.Locked);CursorSafety.SetVisible(false);
        Check(UnityEngine.Cursor.Writes==0,"unfocused game never writes cursor state");
        UnityEngine.Application.isFocused=true;
        // No window is created or activated by this console test. Foreground is another process.
        Check(!CursorSafety.OwnsFocus,"stale/simulated Unity focus rejected by real foreground PID");
        CursorSafety.SetLock(UnityEngine.CursorLockMode.Locked);CursorSafety.SetVisible(false);
        Check(UnityEngine.Cursor.Writes==0,"foreign foreground process retains pointer ownership");
        CursorSafety.BackgroundTest=true;
        CursorSafety.SetLock(UnityEngine.CursorLockMode.Locked);CursorSafety.SetVisible(false);
        Check(UnityEngine.Cursor.Writes==0,"developer test cannot write another application's cursor");
        // Non-Windows fallback exercises the positive setter behavior with a fake sink.
        UnityEngine.Application.platform=UnityEngine.RuntimePlatform.LinuxPlayer;
        CursorSafety.SetLock(UnityEngine.CursorLockMode.Locked);CursorSafety.SetVisible(false);
        Check(UnityEngine.Cursor.lockState==UnityEngine.CursorLockMode.None && UnityEngine.Cursor.visible,"test mode keeps focused game's pointer free");
        CursorSafety.BackgroundTest=false;
        CursorSafety.SetLock(UnityEngine.CursorLockMode.Locked);CursorSafety.SetVisible(false);
        Check(UnityEngine.Cursor.lockState==UnityEngine.CursorLockMode.Locked && !UnityEngine.Cursor.visible,"normal focused gameplay retains capture semantics");
        var input=new[]{new CodeInstruction(OpCodes.Nop),new CodeInstruction(OpCodes.Call,AccessTools.PropertySetter(typeof(UnityEngine.Cursor),"lockState")),new CodeInstruction(OpCodes.Call,AccessTools.PropertySetter(typeof(UnityEngine.Cursor),"visible"))};
        var transform=typeof(CursorSafety).GetMethod("GuardSetters",BindingFlags.NonPublic|BindingFlags.Static);
        var output=new List<CodeInstruction>((IEnumerable<CodeInstruction>)transform.Invoke(null,new object[]{input}));
        Check(output.Count==3 && output[0].opcode==OpCodes.Nop && output[1].Calls(AccessTools.Method(typeof(CursorSafety),"SetLock")) && output[2].Calls(AccessTools.Method(typeof(CursorSafety),"SetVisible")),"transpiler preserves unrelated instructions and guards both setters");
        Console.WriteLine("Fake cursor sink only; actual RoR2 Alt-Tab and GPU integration acceptance still pending.");
    }
}
