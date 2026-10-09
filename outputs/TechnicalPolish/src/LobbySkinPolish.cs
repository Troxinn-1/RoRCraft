using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using RoR2;
using UnityEngine;
using RoR2.SurvivorMannequins;

namespace RoRCraftPolish {
    // Cosmetic preview only: never replaces SurvivorDef, bodyPrefab or loadout.
    [BepInPlugin("cz.jirka.rorcraft-lobby-skin","RoRCraft lobby skin","0.2.0")]
    [BepInDependency("cz.jirka.rorcraft-material-polish",BepInDependency.DependencyFlags.HardDependency)]
    [DefaultExecutionOrder(20000)]
    public sealed class LobbySkinPolish : BaseUnityPlugin {
        private readonly Dictionary<Transform,LobbySkinAvatar> avatars=new Dictionary<Transform,LobbySkinAvatar>();
        private readonly HashSet<Transform> failed=new HashSet<Transform>();
        private readonly List<Transform> remove=new List<Transform>();
        private float nextScan;
        private string session;
        private SessionSkinData skin;
        private long fileStamp;
        private string warning;
        private static LobbySkinPolish instance;
        private HarmonyLib.Harmony hooks;
        private static int nativeVisualScope;
        private SurvivorSkinStore store;
        private SurvivorSkinMenu skinMenu;
        private string selectedIdentity;
        private float nextSkinPoll;
        private WindowsDanceWave danceMusic;
        private bool danceMusicLatched,musicWarning;
        private LobbySkinAvatar musicAvatar;
        internal SessionSkinData AccountSkin {get {return skin;}}
        internal SurvivorSkinStore SkinStore {get {return store;}}
        internal SurvivorDef SelectedSurvivor {
            get {var user=LocalUserManager.GetFirstLocalUser();return user==null || user.currentNetworkUser==null?null:user.currentNetworkUser.GetSurvivorPreference();}
        }
        internal static string SurvivorKey(SurvivorDef survivor) {return survivor==null || survivor.bodyPrefab==null?"":survivor.bodyPrefab.name;}
        private static readonly System.Reflection.FieldInfo displayField=HarmonyLib.AccessTools.Field(typeof(SurvivorMannequinSlotController),"mannequinInstanceTransform");
        private void Awake() {
            instance=this;
            store=new SurvivorSkinStore(System.IO.Path.Combine(Paths.ConfigPath,"RoRCraftSkins"));
            skinMenu=new SurvivorSkinMenu(this);
            hooks=new HarmonyLib.Harmony("cz.jirka.rorcraft-lobby-skin-events");
            foreach(var name in new[]{"RebuildMannequinInstance","ApplyLoadoutToMannequinInstance","Update"}) {
                var method=HarmonyLib.AccessTools.Method(typeof(SurvivorMannequinSlotController),name);
                if(method==null) throw new MissingMethodException("Required lobby lifecycle hook: "+name);
                bool creation=name!="Update";
                hooks.Patch(method,prefix:creation?new HarmonyLib.HarmonyMethod(typeof(LobbySkinPolish),"VisualScopeBegin"):null,
                    postfix:new HarmonyLib.HarmonyMethod(typeof(LobbySkinPolish),"SlotChanged"),
                    finalizer:creation?new HarmonyLib.HarmonyMethod(typeof(LobbySkinPolish),"VisualScopeEnd"):null);
            }
            foreach(var method in typeof(EffectManager).GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static))
                if(method.Name=="SpawnEffect") hooks.Patch(method,prefix:new HarmonyLib.HarmonyMethod(typeof(LobbySkinPolish),"AllowNativeEffect"));
            Camera.onPreCull+=BeforeCamera;
            hooks.Patch(HarmonyLib.AccessTools.Method(typeof(SurvivorMannequinSlotController),"ClearMannequinInstance"),prefix:new HarmonyLib.HarmonyMethod(typeof(LobbySkinPolish),"SlotClearing"));
            var token=System.IO.Path.Combine(Paths.ConfigPath,"rorcraft-skin-session.txt");
            if(File.Exists(token)) session=File.ReadAllText(token).Trim();
            Logger.LogInfo("Lobby awaits current Minecraft profile skin; no packaged player skin fallback.");
        }
        private static void VisualScopeBegin(SurvivorMannequinSlotController __instance,out bool __state) {
            var user=LocalUserManager.GetFirstLocalUser();
            __state=instance!=null && instance.skin!=null && Run.instance==null && __instance!=null && user!=null && __instance.networkUser==user.currentNetworkUser;
            if(__state) nativeVisualScope++;
        }
        private static Exception VisualScopeEnd(Exception __exception,bool __state) {if(__state) nativeVisualScope--;return __exception;}
        private static bool AllowNativeEffect() {return nativeVisualScope==0;}
        private static void BeforeCamera(Camera camera) {
            if(instance==null || Run.instance!=null) return;
            foreach(var pair in instance.avatars) if(pair.Key!=null) {pair.Value.RefreshNativeRenderers(pair.Key);pair.Value.Tick();}
        }
        private static void SlotChanged(SurvivorMannequinSlotController __instance,System.Reflection.MethodBase __originalMethod) {
            if(instance!=null && Run.instance==null) instance.Attach(__instance,__originalMethod.Name!="Update");
        }
        private static void SlotClearing(SurvivorMannequinSlotController __instance) {
            if(instance==null || displayField==null) return;
            var display=displayField.GetValue(__instance) as Transform;
            if(display==null) return;
            LobbySkinAvatar avatar;
            if(instance.avatars.TryGetValue(display,out avatar)) {
                avatar.Dispose(false);instance.avatars.Remove(display);
            }
            instance.failed.Remove(display);
        }
        private void Attach(SurvivorMannequinSlotController slot,bool refresh=false) {
            if(skin==null || slot==null) return;
            var user=LocalUserManager.GetFirstLocalUser();
            if(user==null || slot.networkUser==null || slot.networkUser!=user.currentNetworkUser) return;
            var display=displayField==null?null:displayField.GetValue(slot) as Transform;
            if(display==null || !display.gameObject.activeInHierarchy || failed.Contains(display)) return;
            LobbySkinAvatar existing;
            if(avatars.TryGetValue(display,out existing)) {if(refresh) existing.RefreshNativeRenderers(display);existing.Tick();return;}
            try {
                string failure;
                var resolved=store.Resolve(skin,SurvivorKey(slot.currentSurvivorDef),out failure);
                var avatar=new LobbySkinAvatar(display,resolved);
                avatars.Add(display,avatar);
                avatar.RefreshNativeRenderers(display);
                avatar.Tick(); // Before the creating native callback returns/rendering.
                Logger.LogInfo("Minecraft skin replaces lobby mannequin synchronously: "+display.gameObject.name);
            } catch(Exception error) {
                failed.Add(display);
                Logger.LogWarning("Lobby skin unavailable; native preview retained: "+error.Message);
            }
        }
        internal void AssignSkin(string id) {
            if(skin==null || SelectedSurvivor==null) return;
            store.Assign(skin.Account,SurvivorKey(SelectedSurvivor),id);
            selectedIdentity=null;Clear();UpdateVisualChoice();
        }
        internal bool PreviewDance() {
            foreach(var avatar in avatars.Values)if(avatar.PreviewDance()){StopDanceMusic();return true;}
            return false;
        }
        private void StopDanceMusic(){if(danceMusic!=null)danceMusic.Stop();musicAvatar=null;danceMusicLatched=false;}
        private void UpdateDanceMusic() {
            LobbySkinAvatar dancing=null;
            foreach(var avatar in avatars.Values)if(avatar.IsDancing){dancing=avatar;break;}
            if(dancing==null){StopDanceMusic();return;}
            if(dancing!=musicAvatar){StopDanceMusic();musicAvatar=dancing;}
            // Never emit audio during background tests or after Alt-Tab.
            if(Application.isBatchMode || CursorSafety.BackgroundTest || !CursorSafety.OwnsFocus){if(danceMusic!=null)danceMusic.Stop();danceMusicLatched=true;return;}
            if(danceMusicLatched)return;danceMusicLatched=true;
            try {
                if(danceMusic==null)danceMusic=new WindowsDanceWave();
                danceMusic.Play(MinecraftMenuAudio.MusicVolume());
            } catch(Exception error){if(!musicWarning){musicWarning=true;Logger.LogWarning("Dance continues without Pigstep: "+error.Message);}}
        }
        private void UpdateVisualChoice() {
            if(skin==null || SelectedSurvivor==null) return;
            string failure;var chosen=store.Resolve(skin,SurvivorKey(SelectedSurvivor),out failure);
            string identity=chosen.Account+"|"+chosen.Hash+"|"+chosen.Slim;
            if(identity==selectedIdentity) return;
            if(failure!=null) Logger.LogWarning("Custom skin unavailable; account skin used: "+failure);
            SurvivorSkinStore.Atomic(System.IO.Path.Combine(Paths.ConfigPath,"rorcraft-skin-selected.bin"),chosen.Encode(session));
            selectedIdentity=identity;
            Logger.LogInfo("Survivor skin resolved for lobby/gameplay: "+SurvivorKey(SelectedSurvivor)+" model="+(chosen.Slim?"slim":"classic"));
        }
        private void ReadSkin() {
            string path=System.IO.Path.Combine(Paths.ConfigPath,"rorcraft-skin-session.bin");
            if(!File.Exists(path) || session==null) return;
            long stamp=File.GetLastWriteTimeUtc(path).Ticks;if(stamp==fileStamp) return;
            try {
                if(new FileInfo(path).Length!=120+64*64*4) throw new InvalidDataException("Invalid profile skin size");
                var next=SessionSkinData.Read(File.ReadAllBytes(path),session);
                fileStamp=stamp;warning=null;
                if(skin!=null && skin.Account==next.Account && skin.Slim==next.Slim && skin.Hash==next.Hash) return;
                Clear();skin=next;selectedIdentity=null;
                Logger.LogInfo("Current Minecraft profile skin applied; model="+(skin.Slim?"slim":"classic"));
            } catch(Exception error) {
                if(skin!=null) {Clear();skin=null;}
                var selected=System.IO.Path.Combine(Paths.ConfigPath,"rorcraft-skin-selected.bin");
                if(File.Exists(selected)) File.Delete(selected);selectedIdentity=null;
                if(warning!=error.Message) {warning=error.Message;Logger.LogWarning("Native lobby retained: "+warning);}
            }
        }
        private void LateUpdate() {
            GameplaySkinVisibility.Tick();
            if(Time.unscaledTime>=nextSkinPoll) {nextSkinPoll=Time.unscaledTime+.25f;ReadSkin();UpdateVisualChoice();}
            if(Run.instance!=null) {StopDanceMusic();if(avatars.Count!=0 || failed.Count!=0) Clear();return;}
            if(Time.unscaledTime>=nextScan) {
                nextScan=Time.unscaledTime+.25f;
                ReadSkin();
                if(skin==null) return;
                remove.Clear();
                foreach(var pair in avatars) if(pair.Key==null || !pair.Key.gameObject.activeInHierarchy) remove.Add(pair.Key);
                foreach(var key in remove) {avatars[key].Dispose();avatars.Remove(key);}
                failed.RemoveWhere(IsGone);
                var user=LocalUserManager.GetFirstLocalUser();
                foreach(var slot in UnityEngine.Object.FindObjectsOfType<SurvivorMannequinSlotController>()) {
                    Attach(slot);
                    var display=displayField==null?null:displayField.GetValue(slot) as Transform;
                    LobbySkinAvatar existing;
                    if(display!=null && avatars.TryGetValue(display,out existing)) existing.RefreshNativeRenderers(display);
                }
            }
            foreach(var avatar in avatars.Values) avatar.Tick();
            UpdateDanceMusic();
        }
        private static bool IsGone(Transform value) {return value==null || !value.gameObject.activeInHierarchy;}
        private void OnGUI() {if(skinMenu!=null && Run.instance==null && PreGameController.instance!=null) skinMenu.Draw();else if(skinMenu!=null) skinMenu.Close();}
        private void Clear() {
            StopDanceMusic();
            // Restoring native preview components can run OnEnable intro effects
            // during a skin swap. This is still an owned lobby-only operation.
            nativeVisualScope++;
            try {foreach(var avatar in avatars.Values) avatar.Dispose();avatars.Clear();failed.Clear();}
            finally {nativeVisualScope--;}
        }
        private void OnDestroy() {if(skinMenu!=null) skinMenu.Dispose();Camera.onPreCull-=BeforeCamera;if(hooks!=null) hooks.UnpatchSelf();Clear();if(danceMusic!=null)danceMusic.Dispose();if(instance==this) instance=null;}
    }
}
