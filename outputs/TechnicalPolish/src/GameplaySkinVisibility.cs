using System;
using System.Collections.Generic;
using HarmonyLib;
using RoR2;
using UnityEngine;

namespace RoRCraftPolish {
    // The adapter owns restoration; extend its dictionary when spawn/cosmetic
    // refresh creates or re-enables native renderers after Begin's one-time hide.
    internal static class GameplaySkinVisibility {
        private static Component adapter;
        private static readonly Type adapterType=AccessTools.TypeByName("RoRCraft.RoRCraftPlugin");
        private static readonly System.Reflection.FieldInfo active=AccessTools.Field(adapterType,"active");
        private static readonly System.Reflection.FieldInfo controlled=AccessTools.Field(adapterType,"controlled");
        private static readonly System.Reflection.FieldInfo hidden=AccessTools.Field(adapterType,"hidden");
        private static CharacterBody body;
        private static Renderer[] renderers;
        private static float nextScan;
        internal static void Tick() {
            if(adapter==null && adapterType!=null) adapter=UnityEngine.Object.FindObjectOfType(adapterType) as Component;
            if(adapter==null || active==null || controlled==null || hidden==null || !(bool)active.GetValue(adapter)) {
                body=null;renderers=null;return;
            }
            var next=controlled.GetValue(adapter) as CharacterBody;
            if(next==null || next.modelLocator==null || next.modelLocator.modelTransform==null) return;
            if(next!=body || Time.unscaledTime>=nextScan) {
                body=next;nextScan=Time.unscaledTime+.25f;
                renderers=body.modelLocator.modelTransform.GetComponentsInChildren<Renderer>(true);
            }
            var restore=hidden.GetValue(adapter) as Dictionary<Renderer,bool>;
            if(restore==null || renderers==null) return;
            foreach(var renderer in renderers) if(renderer!=null) {
                if(!restore.ContainsKey(renderer)) restore.Add(renderer,renderer.enabled);
                renderer.enabled=false;
            }
        }
    }
}
