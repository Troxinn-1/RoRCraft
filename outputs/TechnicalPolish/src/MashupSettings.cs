using BepInEx;
using BepInEx.Configuration;
using RiskOfOptions;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;
using UnityEngine;

namespace RoRCraftPolish {
    [BepInPlugin(Guid,"Minecraft Mashup settings","0.1.5")]
    [BepInDependency("com.rune580.riskofoptions",BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("cz.jirka.rorcraft-material-polish",BepInDependency.DependencyFlags.HardDependency)]
    public sealed class MashupSettings : BaseUnityPlugin {
        public const string Guid="cz.jirka.rorcraft-mashup-settings";
        private const string Title="Minecraft Mashup";
        public enum FrameLimit {GameDefault=0,FPS30=30,FPS60=60,FPS90=90,FPS120=120,FPS144=144,FPS165=165,FPS240=240,Unlimited=-1}
        public enum SyncMode {GameDefault=-1,Off=0,On=1}
        private ConfigEntry<FrameLimit> cap;
        private ConfigEntry<SyncMode> sync;
        private bool pending;
        private int defaultCap,defaultSync;
        private FrameLimit previousCap;
        private SyncMode previousSync;
        public static MashupSettings Instance;
        public ConfigEntry<FrameLimit> FrameCap {get {return cap;}}
        public ConfigEntry<SyncMode> VSync {get {return sync;}}
        private MinecraftSettingsScreen screen;
        private MinecraftMenuAudio menuAudio;
        private HarmonyLib.Harmony cursorHarmony;
        internal void MenuHover(int index) {if(menuAudio!=null) menuAudio.Hover(index);}
        internal void MenuAudioLoaded() {Logger.LogInfo("Original Minecraft UI click decoded. Audible playback is not yet verified.");}
        internal void MenuAudioNativeReady() {Logger.LogInfo("Minecraft UI PCM backend ready on default Windows device; Unity audio is disabled by RoR2. Audible playback is not yet verified.");}
        internal void MenuAudioFailed(string error) {Logger.LogWarning("Minecraft UI sound could not load: "+error);}
        private ConfigEntry<bool> preview;
        private bool previewOpened,previewCaptured;
        private float readyTime=-1,previewTime;
        private int previewFrame;
        private void Awake() {
            Instance=this;
            cursorHarmony=new HarmonyLib.Harmony(Guid+".cursor-focus");
            CursorSafety.Install(cursorHarmony);
            defaultCap=Application.targetFrameRate;defaultSync=QualitySettings.vSyncCount;
            cap=Config.Bind("Graphics","FPS Limit",FrameLimit.GameDefault,"GameDefault leaves the game's frame limit unchanged. VSync can take precedence over a selected limit. Changes apply immediately and persist.");
            sync=Config.Bind("Graphics","VSync",SyncMode.GameDefault,"GameDefault leaves the native graphics setting in control. On synchronizes to the display; Off allows the selected frame limit.");
            cap.SettingChanged+=Changed;sync.SettingChanged+=Changed;
            previousCap=cap.Value;previousSync=sync.Value;
            RoR2.RoR2Application.onLoad+=Loaded;
            ModSettingsManager.SetModDescription("Graphics controls for Minecraft × Risk of Rain 2. Resolution, display mode and native post-processing remain in the game's Graphics menu. More Minecraft visual controls are in development.",Guid,Title);
            menuAudio=new MinecraftMenuAudio(this);
            screen=new MinecraftSettingsScreen(this);
            preview=Config.Bind("Developer","PreviewMenu",false,"Developer-only menu screenshot run. Normally closes after capture. Disabled for regular play.");
            ModSettingsManager.AddOption(new GenericButtonOption("Minecraft Settings","Settings","Open the Minecraft-style graphics settings page.","Open Settings",screen.Open),Guid,Title);
            var materials=MaterialPolish.Instance;
            if(materials==null) throw new System.InvalidOperationException("Minecraft material settings unavailable.");
            pending=cap.Value!=FrameLimit.GameDefault || sync.Value!=SyncMode.GameDefault;
            Logger.LogInfo("Minecraft-style settings page registered in Mod Options; four existing persistent controls retained.");
        }
        private void Loaded() {
            readyTime=Time.unscaledTime;
            defaultCap=Application.targetFrameRate;defaultSync=QualitySettings.vSyncCount;
            pending=cap.Value!=FrameLimit.GameDefault || sync.Value!=SyncMode.GameDefault;
        }
        private void Changed(object sender,System.EventArgs args) {
            if(sender==cap && previousCap==FrameLimit.GameDefault) defaultCap=Application.targetFrameRate;
            if(sender==sync && previousSync==SyncMode.GameDefault) defaultSync=QualitySettings.vSyncCount;
            previousCap=cap.Value;previousSync=sync.Value;pending=true;
        }
        private void Update() {
            if(menuAudio!=null) menuAudio.Update(screen!=null && screen.Visible);
            if(pending) {pending=false;Apply();Config.Save();}
            if(preview.Value && readyTime>=0 && Time.unscaledTime-readyTime>10) {
                if(!previewOpened) {screen.Open();previewOpened=true;previewFrame=Time.frameCount;previewTime=Time.unscaledTime;}
                else if(!previewCaptured && Time.frameCount-previewFrame>6) {
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.ConfigPath,"minecraft-settings-preview.png"));previewCaptured=true;
                    Logger.LogInfo("Minecraft menu preview capture requested.");
                } else if(previewCaptured && Time.unscaledTime-previewTime>5) {
                    screen.Close();preview.Value=false;Config.Save();Application.Quit();
                }
            }
        }
        private void Apply() {
            // A graphics change must not overwrite an unrelated native setting.
            if(cap.Value!=FrameLimit.GameDefault) Application.targetFrameRate=(int)cap.Value;
            else if(lastAppliedCap!=FrameLimit.GameDefault) Application.targetFrameRate=defaultCap;
            if(sync.Value!=SyncMode.GameDefault) QualitySettings.vSyncCount=(int)sync.Value;
            else if(lastAppliedSync!=SyncMode.GameDefault) QualitySettings.vSyncCount=defaultSync;
            lastAppliedCap=cap.Value;lastAppliedSync=sync.Value;
        }
        private FrameLimit lastAppliedCap;
        private SyncMode lastAppliedSync=SyncMode.GameDefault;
        private void LateUpdate() {if(screen!=null) screen.MaintainCursor();}
        private void OnGUI() {if(screen!=null) screen.Draw();}
        private void OnDestroy() {if(screen!=null) screen.Dispose();if(menuAudio!=null) menuAudio.Dispose();if(cursorHarmony!=null) cursorHarmony.UnpatchSelf();RoR2.RoR2Application.onLoad-=Loaded;cap.SettingChanged-=Changed;sync.SettingChanged-=Changed;Instance=null;}
    }
}
