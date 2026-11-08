using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using RelicExample.Rendering;
public static class InstallMaskToon
{
    const string Folder="Assets/SceneEffects/MaskToon";
    public static object Install()
    {
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(Folder+"/MaskToon.shader");
        if(!shader||ShaderUtil.ShaderHasError(shader))throw new Exception("Mask toon shader failed to compile");
        if(!AssetDatabase.IsValidFolder(Folder+"/OriginalMaterials"))AssetDatabase.CreateFolder(Folder,"OriginalMaterials");
        var materials=UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None)
            .Where(r=>{var f=r.GetComponent<MeshFilter>();string path=f&&f.sharedMesh?AssetDatabase.GetAssetPath(f.sharedMesh):"";return path.Contains("/mianju/")||path.Contains("魔鬼面具");})
            .SelectMany(r=>r.sharedMaterials).Where(m=>m&&AssetDatabase.Contains(m)).Distinct().ToArray();
        if(materials.Length==0)throw new Exception("No mask materials found in the active scene");
        foreach(var material in materials)
        {
            if(material.shader==shader)continue;
            string backup=Folder+"/OriginalMaterials/"+material.name+"_OriginalLit.mat";
            if(!AssetDatabase.LoadAssetAtPath<Material>(backup)&&!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(material),backup))throw new Exception("Could not back up original material");
            Undo.RecordObject(material,"面具三渲二材质");
            var map=material.HasProperty("_BaseMap")?material.GetTexture("_BaseMap"):null;
            Color color=material.HasProperty("_BaseColor")?material.GetColor("_BaseColor"):Color.white;
            int queue=material.renderQueue;float cull=material.HasProperty("_Cull")?material.GetFloat("_Cull"):2;
            material.shader=shader;material.shaderKeywords=Array.Empty<string>();
            if(map)material.SetTexture("_BaseMap",map);material.SetColor("_BaseColor",color);
            material.SetFloat("_Cull",cull);material.renderQueue=queue;
            material.SetFloat("_ToonEnabled",1);material.SetFloat("_UseMidtone",0);
            material.SetFloat("_ShadowThreshold",0.52f);material.SetFloat("_HighlightThreshold",0.78f);
            material.SetFloat("_LightLevel",1.15f);material.SetFloat("_MidLevel",0.64f);material.SetFloat("_ShadowLevel",0.28f);
            material.SetFloat("_BandSoftness",0.004f);material.SetFloat("_MaskToonMarker",1);
            EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
        }
        GlobalOutlineTargets.Refresh();
        Selection.activeObject=materials[0];EditorGUIUtility.PingObject(materials[0]);
        SceneView.RepaintAll();EditorApplication.QueuePlayerLoopUpdate();
        return new{materials=materials.Select(m=>AssetDatabase.GetAssetPath(m)).ToArray(),shaderMessages=ShaderUtil.GetShaderMessages(shader).Select(m=>new{m.message,severity=m.severity.ToString()}).ToArray(),playing=EditorApplication.isPlaying};
    }
}
