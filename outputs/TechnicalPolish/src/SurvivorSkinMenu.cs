using System;
using UnityEngine;
using RoR2;

namespace RoRCraftPolish {
    internal sealed class SurvivorSkinMenu : IDisposable {
        private readonly LobbySkinPolish owner;
        private readonly MinecraftSettingsScreen widgets=new MinecraftSettingsScreen(null);
        private bool opened,slim;
        private string path="",message="";
        private string[] available=new string[0];
        private Vector2 scroll;
        internal SurvivorSkinMenu(LobbySkinPolish source) {owner=source;}
        internal void Open() {if(opened)return;opened=true;widgets.Open();available=owner.SkinStore.Available();}
        internal void Close() {if(!opened)return;opened=false;widgets.Close();}
        private bool Button(Rect rect,string text,int id) {return widgets.Button(rect,text,id,Event.current.mousePosition);}
        internal void Draw() {
            if(!CursorSafety.OwnsFocus) return;
            widgets.Assets();
            if(!opened) {
                if(Button(new Rect(Screen.width*.5f-120,Screen.height-108,240,35),"Preview Parrot Dance",105)) owner.PreviewDance();
                if(Button(new Rect(Screen.width*.5f-120,Screen.height-65,240,35),"Minecraft Skins",100)) {
                    Open();
                }return;
            }
            widgets.MaintainCursor();
            var saved=GUI.matrix;int oldDepth=GUI.depth;GUI.depth=-250;
            try {
                GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.blackTexture);
                float left=Screen.width*.5f-320;
                var survivor=owner.SelectedSurvivor;
                widgets.Text("Minecraft Skin: "+(survivor==null?"Player":Language.GetString(survivor.displayNameToken)),Screen.width*.5f,50,2,Color.white,true);
                string profile=owner.AccountSkin==null?"":owner.AccountSkin.Account;
                string assigned=owner.SkinStore.Assignment(profile,LobbySkinPolish.SurvivorKey(survivor));
                widgets.Text(assigned==null?"Current: Minecraft account skin":"Current: "+owner.SkinStore.Label(assigned),left,90,1,Color.white,false);
                GUI.enabled=owner.AccountSkin!=null;
                if(Button(new Rect(left,115,640,30),"Use Minecraft Account Skin",101)) {owner.AssignSkin(null);message="Account skin restored for this survivor.";}
                widgets.Text("Import 64x64 PNG (paste full file path):",left,160,1,Color.white,false);
                path=GUI.TextField(new Rect(left,181,640,28),path);
                if(Button(new Rect(left,219,310,30),slim?"Model: Slim (Alex)":"Model: Classic (Steve)",102)) slim=!slim;
                if(Button(new Rect(left+330,219,310,30),"Import and Assign",103)) {
                    try {var id=owner.SkinStore.Import(path.Trim().Trim('"'),slim);owner.AssignSkin(id);available=owner.SkinStore.Available();message="Skin saved for this survivor.";}
                    catch(Exception error) {message=error.Message;}
                }
                widgets.Text("Saved skins (selection is remembered per survivor):",left,272,1,Color.white,false);
                scroll=GUI.BeginScrollView(new Rect(left,294,640,Mathf.Max(60,Screen.height-420)),scroll,new Rect(0,0,610,available.Length*36));
                for(int i=0;i<available.Length;i++) if(Button(new Rect(0,i*36,610,30),owner.SkinStore.Label(available[i]),110+i)) {owner.AssignSkin(available[i]);message="Skin assigned.";}
                GUI.EndScrollView();
                widgets.Text(message,left,Screen.height-110,1,Color.white,false);
                GUI.enabled=true;
                if(Button(new Rect(left+170,Screen.height-70,300,32),"Done",104)) Close();
                if(Event.current.type==EventType.KeyDown && Event.current.keyCode==KeyCode.Escape) {Close();Event.current.Use();}
                if(Event.current.type==EventType.Repaint) widgets.Pointer(Event.current.mousePosition);
            } finally {GUI.matrix=saved;GUI.depth=oldDepth;GUI.enabled=true;}
        }
        public void Dispose() {Close();widgets.Dispose();}
    }
}
