using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildRoRCraftShader {
    public static void Build() {
        const string shaderPath="Assets/RoRCraft/BlockDeferred.shader";
        if(Application.unityVersion!="2021.3.33f1") throw new InvalidOperationException("Use the installed game's editor version 2021.3.33f1");
        PlayerSettings.colorSpace=ColorSpace.Linear;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
        AssetDatabase.ImportAsset(shaderPath,ImportAssetOptions.ForceUpdate);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
        if(shader==null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Shader import failed; see editor diagnostics");
        var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../shader-build"));
        Directory.CreateDirectory(directory);
        var manifest=BuildPipeline.BuildAssetBundles(directory,new[]{new AssetBundleBuild {assetBundleName="rorcraft-block-shaders",assetNames=new[]{shaderPath}}},BuildAssetBundleOptions.StrictMode|BuildAssetBundleOptions.ForceRebuildAssetBundle|BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.StandaloneWindows64);
        if(manifest==null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Asset bundle or compiled shader failed");
        var bundle=AssetBundle.LoadFromFile(Path.Combine(directory,"rorcraft-block-shaders"));
        if(bundle==null) throw new InvalidOperationException("Built bundle failed editor reload");
        try {if(bundle.LoadAsset<Shader>(shaderPath)==null) throw new InvalidOperationException("Built bundle lacks the shader asset");}
        finally {bundle.Unload(true);}
        File.WriteAllText(Path.Combine(directory,"BUILD-SCOPE.txt"),"Built with "+Application.unityVersion+" for D3D11. Compilation is not runtime acceptance. Require native MRT color/packing/cutout tests, shadow and stage comparisons before deployment.\n");
        Debug.Log("RoRCraft experimental shader bundle built; NOT deployed.");
    }
}
