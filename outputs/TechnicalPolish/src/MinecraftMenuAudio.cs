using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Networking;

namespace RoRCraftPolish {
    // Reuse the EventSystem's hit testing so overlapping or disabled controls
    // do not produce hover sounds. No scene scans or asset reads per frame.
    internal sealed class MinecraftMenuAudio : IDisposable {
        private readonly MashupSettings owner;
        private readonly AudioSource source;
        private readonly WindowsMenuWave wave;
        private readonly HarmonyLib.Harmony hoverHarmony;
        private static MinecraftMenuAudio instance;
        private readonly List<RaycastResult> hits=new List<RaycastResult>();
        private AudioClip click;
        private PointerEventData pointer;
        private EventSystem pointerSystem;
        private int hovered;
        private float lastPlay=-1;
        private bool disposed;
        private bool warned;
        private static readonly System.Reflection.FieldInfo master=HarmonyLib.AccessTools.Field(typeof(RoR2.AudioManager),"cvVolumeMaster"),sfx=HarmonyLib.AccessTools.Field(typeof(RoR2.AudioManager),"cvVolumeSfx");
        private static float GameVolume() {
            var masterValue=master==null?null:master.GetValue(null) as RoR2.ConVar.BaseConVar;
            var effectValue=sfx==null?null:sfx.GetValue(null) as RoR2.ConVar.BaseConVar;
            float a,b;
            if(masterValue==null || effectValue==null || !float.TryParse(masterValue.GetString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out a) || !float.TryParse(effectValue.GetString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out b)) throw new InvalidOperationException("Native master/SFX volume unavailable");
            if(float.IsNaN(a) || float.IsInfinity(a) || float.IsNaN(b) || float.IsInfinity(b)) throw new InvalidOperationException("Invalid native audio volume");
            return Mathf.Clamp01(a/100)*Mathf.Clamp01(b/100);
        }
        internal static float MusicVolume() {
            float product=1;
            foreach(var name in new[]{"cvVolumeMaster","cvVolumeMsx"}) {
                var field=HarmonyLib.AccessTools.Field(typeof(RoR2.AudioManager),name);
                var value=field==null?null:field.GetValue(null) as RoR2.ConVar.BaseConVar;
                float gain;if(value==null || !float.TryParse(value.GetString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out gain) || float.IsNaN(gain) || float.IsInfinity(gain))
                    throw new InvalidOperationException("Native Master/Music volume unavailable");
                product*=Mathf.Clamp01(gain/100);
            }
            return product;
        }
        internal static string DeveloperAudioAudit() {return "Windows PCM ready="+(instance!=null && instance.wave!=null)+" native master/SFX gain="+GameVolume().ToString(System.Globalization.CultureInfo.InvariantCulture)+"; no playback requested";}
        public MinecraftMenuAudio(MashupSettings plugin) {
            owner=plugin;instance=this;
            if(Application.platform==RuntimePlatform.WindowsPlayer) {
                try {wave=new WindowsMenuWave();owner.MenuAudioNativeReady();}
                catch(Exception error) {owner.MenuAudioFailed(error.Message);}
            } else {
                source=plugin.gameObject.AddComponent<AudioSource>();
                source.playOnAwake=false;source.spatialBlend=0;source.volume=.3f;source.ignoreListenerPause=true;
                plugin.StartCoroutine(Load());
            }
            hoverHarmony=new HarmonyLib.Harmony(MashupSettings.Guid+".menu-hover");
            hoverHarmony.Patch(HarmonyLib.AccessTools.Method(typeof(Selectable),"OnPointerEnter"),postfix:new HarmonyLib.HarmonyMethod(typeof(MinecraftMenuAudio),"PointerEnter"));
            hoverHarmony.Patch(HarmonyLib.AccessTools.Method(typeof(Selectable),"OnPointerExit"),postfix:new HarmonyLib.HarmonyMethod(typeof(MinecraftMenuAudio),"PointerExit"));
        }
        private static void PointerEnter(Selectable __instance) {if(instance!=null && __instance.isActiveAndEnabled && __instance.IsInteractable()) instance.Hover(__instance.GetInstanceID());}
        private static void PointerExit(Selectable __instance) {if(instance!=null && instance.hovered==__instance.GetInstanceID()) instance.Hover(0);}
        private IEnumerator Load() {
            string path=Path.Combine(BepInEx.Paths.CachePath,"rorcraft-menu-click.ogg");
            using(var input=typeof(MinecraftMenuAudio).Assembly.GetManifestResourceStream("MinecraftUi.click.ogg"))
            using(var output=File.Create(path)) input.CopyTo(output);
            using(var request=UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri,AudioType.OGGVORBIS)) {
                yield return request.SendWebRequest();
                if(request.result==UnityWebRequest.Result.Success) {
                    var loaded=DownloadHandlerAudioClip.GetContent(request);
                    if(disposed) UnityEngine.Object.Destroy(loaded);
                    else {click=loaded;owner.MenuAudioLoaded();}
                } else owner.MenuAudioFailed(request.error);
            }
        }
        public void Hover(int target) {
            if(target==hovered) return;
            hovered=target;
            if(target!=0 && CursorSafety.OwnsFocus && Time.unscaledTime-lastPlay>=.065f) {
                if(wave!=null) {
                    try {if(!wave.Play(GameVolume())) throw new InvalidOperationException("Windows PCM playback failed");}
                    catch(Exception error) {if(!warned) {owner.MenuAudioFailed(error.Message);warned=true;}}
                }
                else if(source!=null && click!=null) source.PlayOneShot(click);
                lastPlay=Time.unscaledTime;
            }
        }
        public void Update(bool customMenu) {
            if(customMenu) return;
            var system=EventSystem.current;
            if(system==null || Cursor.lockState==CursorLockMode.Locked || !Application.isFocused) {Hover(0);return;}
            if(pointerSystem!=system) {pointerSystem=system;pointer=new PointerEventData(system);}
            pointer.Reset();pointer.position=Input.mousePosition;hits.Clear();system.RaycastAll(pointer,hits);
            int target=0;
            if(hits.Count>0) {
                var control=hits[0].gameObject.GetComponentInParent<Selectable>();
                if(control!=null && control.isActiveAndEnabled && control.IsInteractable()) target=control.GetInstanceID();
            }
            Hover(target);
        }
        public void Dispose() {
            disposed=true;if(source!=null) UnityEngine.Object.Destroy(source);
            if(wave!=null) wave.Dispose();if(hoverHarmony!=null) hoverHarmony.UnpatchSelf();if(instance==this) instance=null;
            if(click!=null) UnityEngine.Object.Destroy(click);hits.Clear();
        }
    }
}
