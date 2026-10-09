using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoRCraftPolish {
    // A scoped menu reached through the game's existing settings navigation.
    // No gameplay hotkey, diagnostics, or per-frame work while closed.
    internal sealed class MinecraftSettingsScreen : IDisposable {
        private readonly MashupSettings settings;
        private readonly Dictionary<char,int> glyphs=new Dictionary<char,int>();
        private readonly Dictionary<char,int> widths=new Dictionary<char,int>();
        private readonly Dictionary<string,Texture2D> textTextures=new Dictionary<string,Texture2D>();
        private Texture2D button,hover,background,font,slider,handle,pointer;
        private GameObject inputShield,previousSelection;
        private bool navigation,oldCursorVisible;
        private CursorLockMode oldLock;
        private GUIStyle trackStyle,thumbStyle;
        private int selected;
        public bool Visible {get;private set;}
        private readonly MashupSettings.FrameLimit[] limits={MashupSettings.FrameLimit.GameDefault,MashupSettings.FrameLimit.FPS30,MashupSettings.FrameLimit.FPS60,MashupSettings.FrameLimit.FPS90,MashupSettings.FrameLimit.FPS120,MashupSettings.FrameLimit.FPS144,MashupSettings.FrameLimit.FPS165,MashupSettings.FrameLimit.FPS240,MashupSettings.FrameLimit.Unlimited};
        public MinecraftSettingsScreen(MashupSettings owner) {settings=owner;}
        private static Texture2D Load(string name) {
            using(var stream=typeof(MinecraftSettingsScreen).Assembly.GetManifestResourceStream("MinecraftUi."+name+".png")) {
                if(stream==null) throw new InvalidOperationException("Missing Minecraft menu asset: "+name);
                using(var bytes=new MemoryStream()) {
                    stream.CopyTo(bytes);var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                    texture.name="Minecraft menu "+name;
                    if(!ImageConversion.LoadImage(texture,bytes.ToArray(),false)) throw new InvalidOperationException("Invalid Minecraft GUI texture: "+name);
                    texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;return texture;
                }
            }
        }
        internal void Assets() {
            if(button!=null) return;
            button=Load("button");hover=Load("hover");background=Load("background");font=Load("ascii");slider=Load("slider");handle=Load("handle");background.wrapMode=TextureWrapMode.Repeat;
            for(int i=0;i<MinecraftGlyphs.Characters.Length;i++) {
                char c=MinecraftGlyphs.Characters[i];if(c=='\0') continue;
                glyphs[c]=i;int width=0;
                for(int x=0;x<8;x++) for(int y=0;y<8;y++) if(font.GetPixel((i%16)*8+x,font.height-1-((i/16)*8+y)).a>.05f) width=Math.Max(width,x+1);
                widths[c]=c==' ' ? 4:Math.Max(1,width+1);
            }
            trackStyle=new GUIStyle();thumbStyle=new GUIStyle();thumbStyle.fixedWidth=8;thumbStyle.fixedHeight=24;
        }
        public void MaintainCursor() {if(Visible && CursorSafety.OwnsFocus) {CursorSafety.SetLock(CursorLockMode.None);CursorSafety.SetVisible(false);}}
        internal void Pointer(Vector2 mouse) {
            if(pointer==null) {
                pointer=new Texture2D(12,18,TextureFormat.RGBA32,false);
                var pixels=new Color32[12*18];
                for(int y=0;y<16;y++) for(int x=0;x<11;x++) {
                    bool arrow=y<12 && x<=y/2;
                    bool stem=y>=9 && y<16 && x>=3 && x<=5;
                    if(arrow || stem) {
                        bool inside=(arrow && x>0 && x<y/2 && y<11) || (stem && x==4 && y<15);
                        pixels[(17-y)*12+x]=inside?new Color32(255,255,255,255):new Color32(0,0,0,255);
                    }
                }
                pointer.SetPixels32(pixels);pointer.Apply(false,true);pointer.filterMode=FilterMode.Point;
            }
            GUI.color=Color.white;GUI.DrawTexture(new Rect(mouse.x,mouse.y,12,18),pointer);
        }
        public void Open() {
            if(Visible) return;Assets();selected=0;if(settings!=null) settings.MenuHover(0);
            oldLock=Cursor.lockState;oldCursorVisible=Cursor.visible;
            var system=EventSystem.current;
            if(system!=null) {previousSelection=system.currentSelectedGameObject;navigation=system.sendNavigationEvents;system.SetSelectedGameObject(null);system.sendNavigationEvents=false;}
            // Prevent the native options behind this screen receiving mouse clicks.
            inputShield=new GameObject("Minecraft settings modal",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
            var canvas=inputShield.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=32760;
            var shield=new GameObject("Input shield",typeof(RectTransform),typeof(Image));shield.transform.SetParent(inputShield.transform,false);
            var rect=shield.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            shield.GetComponent<Image>().color=Color.clear;shield.GetComponent<Image>().raycastTarget=true;
            Visible=true;MaintainCursor();
        }
        public void Close() {
            if(!Visible) return;Visible=false;
            if(inputShield!=null) UnityEngine.Object.Destroy(inputShield);
            var system=EventSystem.current;
            if(system!=null) {system.sendNavigationEvents=navigation;if(previousSelection!=null && previousSelection.activeInHierarchy) system.SetSelectedGameObject(previousSelection);}
            CursorSafety.SetLock(oldLock);CursorSafety.SetVisible(oldCursorVisible);
            if(settings!=null) {settings.MenuHover(0);settings.Config.Save();MaterialPolish.Instance.Config.Save();}
        }
        private float TextWidth(string value,int scale) {int width=0;foreach(char c in value) {int w;width+=widths.TryGetValue(c,out w)?w:6;}return width*scale;}
        internal void Text(string value,float x,float y,int scale,Color color,bool centered) {
            Texture2D texture;
            if(!textTextures.TryGetValue(value,out texture)) {
                int width=Math.Max(1,(int)TextWidth(value,1)+1);
                var pixels=new Color32[width*9];
                // Compose a whole bitmap label once. This avoids per-glyph GUI
                // UV draw state being affected by native slider/button painting.
                for(int pass=0;pass<2;pass++) {
                    int px=pass==0?1:0;
                    foreach(char character in value) {
                        char c=glyphs.ContainsKey(character)?character:'?';int index,w;
                        if(glyphs.TryGetValue(c,out index)) {
                            for(int gy=0;gy<8;gy++) for(int gx=0;gx<8 && px+gx<width;gx++) {
                                var source=font.GetPixel((index%16)*8+gx,font.height-1-((index/16)*8+gy));
                                if(source.a>.05f) {int py=8-gy-(pass==0?1:0);byte shade=(byte)(pass==0?64:255);pixels[py*width+px+gx]=new Color32(shade,shade,shade,(byte)(source.a*255));}
                            }
                        }
                        px+=widths.TryGetValue(c,out w)?w:6;
                    }
                }
                texture=new Texture2D(width,9,TextureFormat.RGBA32,false);texture.name="Minecraft menu label "+value;
                texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
                texture.SetPixels32(pixels);texture.Apply(false,true);textTextures[value]=texture;
            }
            if(centered) x-=TextWidth(value,scale)/2;
            var old=GUI.color;GUI.color=color;
            GUI.DrawTexture(new Rect(x,y,texture.width*scale,texture.height*scale),texture);
            GUI.color=old;
        }
        private static void Slice(Rect rect,Texture2D texture) {
            const float border=3;
            for(int y=0;y<3;y++) for(int x=0;x<3;x++) {
                float px=x==0?rect.x:x==1?rect.x+border:rect.xMax-border;
                float py=y==0?rect.y:y==1?rect.y+border:rect.yMax-border;
                float w=x==1?rect.width-border*2:border,h=y==1?rect.height-border*2:border;
                float u=x==0?0:x==1?border/texture.width:1-border/texture.width;
                float v=y==0?1-border/texture.height:y==1?border/texture.height:0;
                float uw=x==1?1-border*2/texture.width:border/texture.width,vh=y==1?1-border*2/texture.height:border/texture.height;
                GUI.DrawTextureWithTexCoords(new Rect(px,py,w,h),texture,new Rect(u,v,uw,vh));
            }
        }
        internal bool Button(Rect rect,string label,int index,Vector2 mouse) {
            bool highlighted=rect.Contains(mouse) || selected==index;
            bool clicked=GUI.Button(rect,GUIContent.none,trackStyle);
            if(Event.current.type==EventType.Repaint) {Slice(rect,highlighted?hover:button);Text(label,rect.center.x,rect.y+8,1,Color.white,true);}
            if(Event.current.type==EventType.MouseDown && rect.Contains(mouse)) selected=index;
            return clicked;
        }
        private string LimitLabel() {var limit=settings.FrameCap.Value;return "Max Framerate: "+(limit==MashupSettings.FrameLimit.GameDefault?"Game Default":limit==MashupSettings.FrameLimit.Unlimited?"Unlimited":((int)limit).ToString()+" fps");}
        private void Activate(int index,int direction) {
            if(index==0) {int current=Array.IndexOf(limits,settings.FrameCap.Value);settings.FrameCap.Value=limits[(current+direction+limits.Length)%limits.Length];}
            else if(index==1) {int current=(int)settings.VSync.Value+1;settings.VSync.Value=(MashupSettings.SyncMode)((current+direction+3)%3-1);}
            else if(index==2) MaterialPolish.Instance.NativeLighting.Value=!MaterialPolish.Instance.NativeLighting.Value;
            else if(index==3) MaterialPolish.Instance.BlockShadows.Value=!MaterialPolish.Instance.BlockShadows.Value;
            else if(index==4) {int step=Mathf.RoundToInt(MaterialPolish.Instance.EnvironmentColorInfluence.Value*4);MaterialPolish.Instance.EnvironmentColorInfluence.Value=((step-direction+5)%5)*.25f;}
            else if(index==5) MaterialPolish.Instance.DistantFiltering.Value=!MaterialPolish.Instance.DistantFiltering.Value;
            else Close();
        }
        public void Draw() {
            if(!Visible) return;
            var savedMatrix=GUI.matrix;var savedColor=GUI.color;int depth=GUI.depth;
            float scale=Math.Min(Screen.width/854f,Screen.height/480f);
            float offsetX=(Screen.width-854*scale)/2,offsetY=(Screen.height-480*scale)/2;
            Vector2 mouse=new Vector2((Input.mousePosition.x-offsetX)/scale,(Screen.height-Input.mousePosition.y-offsetY)/scale);
            try {
                GUI.depth=-10000;GUI.color=Color.white;
                if(Event.current.type==EventType.Repaint) {
                    // Modern Minecraft's menu tile itself is translucent: it
                    // needs an opaque base when replacing another game's page.
                    GUI.color=new Color(.12f,.12f,.12f,1);
                    GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);
                    GUI.color=new Color(.28f,.28f,.28f,1);
                    GUI.DrawTextureWithTexCoords(new Rect(0,0,Screen.width,Screen.height),background,new Rect(0,0,Screen.width/(32*scale),Screen.height/(32*scale)));
                }
                GUI.color=Color.white;
                GUI.matrix=Matrix4x4.TRS(new Vector3(offsetX,offsetY,0),Quaternion.identity,new Vector3(scale,scale,1));
                var e=Event.current;
                if(e.type==EventType.KeyDown) {
                    if(e.keyCode==KeyCode.Escape) {Close();e.Use();return;}
                    if(e.keyCode==KeyCode.Tab || e.keyCode==KeyCode.DownArrow || e.keyCode==KeyCode.UpArrow) {selected=(selected+(e.keyCode==KeyCode.UpArrow || e.shift?6:1))%7;e.Use();}
                    else if(e.keyCode==KeyCode.Return || e.keyCode==KeyCode.Space || e.keyCode==KeyCode.RightArrow || e.keyCode==KeyCode.LeftArrow) {Activate(selected,e.keyCode==KeyCode.LeftArrow?-1:1);e.Use();}
                }
                if(Event.current.type==EventType.Repaint) {Text("Minecraft Mashup",427,65,2,Color.white,true);Text("Video Settings",427,110,1,Color.white,true);}
                var fps=new Rect(95,165,320,24);
                if(Button(fps,LimitLabel(),0,mouse)) Activate(0,1);
                string sync=settings.VSync.Value==MashupSettings.SyncMode.GameDefault?"Game Default":settings.VSync.Value==MashupSettings.SyncMode.On?"ON":"OFF";
                if(Button(new Rect(439,165,320,24),"VSync: "+sync,1,mouse)) Activate(1,1);
                if(Button(new Rect(95,203,320,24),"RoR2 Lighting: "+(MaterialPolish.Instance.NativeLighting.Value?"ON":"OFF"),2,mouse)) Activate(2,1);
                if(Button(new Rect(439,203,320,24),"Minecraft Shadows: "+(MaterialPolish.Instance.BlockShadows.Value?"ON":"OFF"),3,mouse)) Activate(3,1);
                if(Event.current.type==EventType.Repaint) {Text("Changes are saved automatically.",427,292,1,new Color(.75f,.75f,.75f),true);Text("VSync may limit the selected framerate.",427,312,1,new Color(.75f,.75f,.75f),true);}
                if(Button(new Rect(95,241,320,24),"Environment Color: "+Mathf.RoundToInt(MaterialPolish.Instance.EnvironmentColorInfluence.Value*100)+"%",4,mouse)) Activate(4,1);
                if(Button(new Rect(439,241,320,24),"Distant Textures: "+(MaterialPolish.Instance.DistantFiltering.Value?"ON":"OFF"),5,mouse)) Activate(5,1);
                if(Button(new Rect(327,373,200,24),"Done",6,mouse)) Close();
                if(Visible && Event.current.type==EventType.Repaint) {
                    int hovered=0;
                    if(new Rect(95,165,320,24).Contains(mouse)) hovered=1;
                    else if(new Rect(439,165,320,24).Contains(mouse)) hovered=2;
                    else if(new Rect(95,203,320,24).Contains(mouse)) hovered=3;
                    else if(new Rect(439,203,320,24).Contains(mouse)) hovered=4;
                    else if(new Rect(95,241,320,24).Contains(mouse)) hovered=5;
                    else if(new Rect(439,241,320,24).Contains(mouse)) hovered=6;
                    else if(new Rect(327,373,200,24).Contains(mouse)) hovered=7;
                    settings.MenuHover(hovered);Pointer(mouse);
                }
            } finally {GUI.matrix=savedMatrix;GUI.color=savedColor;GUI.depth=depth;}
        }
        public void Dispose() {
            Close();foreach(var texture in textTextures.Values) UnityEngine.Object.Destroy(texture);textTextures.Clear();
            foreach(var texture in new[]{button,hover,background,font,slider,handle,pointer}) if(texture!=null) UnityEngine.Object.Destroy(texture);
        }
    }
}
