using System;
using System.Reflection;
using UnityEngine;
namespace RoRCraftPolish {
    internal static class MinecraftBlockShader {
        private static AssetBundle bundle;
        private static Shader shader;
        private static bool attempted;
        internal static Shader Get(Action<string> log) {
            if(attempted) return shader;
            attempted=true;
            try {
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("MinecraftWorld.BlockShaders")) {
                    if(stream==null) throw new InvalidOperationException("Embedded block shader resource missing");
                    var bytes=new byte[checked((int)stream.Length)];int read=0;
                    while(read<bytes.Length) {int count=stream.Read(bytes,read,bytes.Length-read);if(count==0) throw new InvalidOperationException("Truncated shader resource");read+=count;}
                    bundle=AssetBundle.LoadFromMemory(bytes);
                }
                if(bundle==null) throw new InvalidOperationException("Block shader asset bundle failed to load");
                shader=bundle.LoadAsset<Shader>("Assets/RoRCraft/BlockDeferred.shader");
                if(shader==null || !shader.isSupported) throw new InvalidOperationException("Block shader unsupported on this graphics backend");
                log("Loaded embedded Minecraft block shader: "+shader.name+"; world tint/AO only, player skins retain the native material.");
            } catch(Exception error) {
                shader=null;if(bundle!=null) {bundle.Unload(true);bundle=null;}
                log("Minecraft block shader unavailable; native fallback retained: "+error.Message);
            }
            return shader;
        }
        // Renderer/material destruction order during player shutdown is not
        // guaranteed. Release the bundle container while leaving referenced
        // shaders alive for Unity's normal asset/scene lifetime management.
        internal static void Dispose() {shader=null;if(bundle!=null) bundle.Unload(false);bundle=null;attempted=false;}
    }
}
