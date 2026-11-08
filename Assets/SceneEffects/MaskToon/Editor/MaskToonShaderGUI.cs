using System.Linq;
using UnityEngine;
using UnityEditor;
namespace RelicExample.EditorTools
{
    public sealed class MaskToonShaderGUI : ShaderGUI
    {
        bool advanced;
        public override void OnGUI(MaterialEditor editor,MaterialProperty[] properties)
        {
            EditorGUILayout.HelpBox("面具三渲二：贴图颜色乘以分档光照。关闭灰阶为亮、暗二分；开启后可分别调整亮、灰、暗三档。外沿继续使用全局描边。",MessageType.Info);
            var texture=FindProperty("_BaseMap",properties);var tint=FindProperty("_BaseColor",properties);
            editor.TexturePropertySingleLine(new GUIContent("面具贴图与颜色"),texture,tint);
            Draw(editor,properties,"_ToonEnabled","启用三渲二");
            Draw(editor,properties,"_UseMidtone","启用灰阶（三档）");
            EditorGUILayout.Space();EditorGUILayout.LabelField("亮 / 灰 / 暗",EditorStyles.boldLabel);
            Draw(editor,properties,"_LightLevel","亮部亮度");
            using(new EditorGUI.DisabledScope(FindProperty("_UseMidtone",properties).floatValue<0.5f))
                Draw(editor,properties,"_MidLevel","灰部亮度");
            Draw(editor,properties,"_ShadowLevel","暗部亮度");
            Draw(editor,properties,"_ShadowThreshold","明暗分界位置");
            using(new EditorGUI.DisabledScope(FindProperty("_UseMidtone",properties).floatValue<0.5f))
                Draw(editor,properties,"_HighlightThreshold","灰亮分界位置");
            Draw(editor,properties,"_BandSoftness","分界柔和度（0 为硬边）");
            Draw(editor,properties,"_Exposure","整体曝光");
            Draw(editor,properties,"_Saturation","贴图饱和度");
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button("明显二分"))Preset(editor,false);
            if(GUILayout.Button("亮灰暗三档"))Preset(editor,true);
            EditorGUILayout.EndHorizontal();
            advanced=EditorGUILayout.Foldout(advanced,"高级：光向与色调",true);
            if(advanced)
            {
                Draw(editor,properties,"_UseSceneLight","跟随场景主光方向");
                using(new EditorGUI.DisabledScope(FindProperty("_UseSceneLight",properties).floatValue>0.5f))
                {Draw(editor,properties,"_LightYaw","自定义光水平角");Draw(editor,properties,"_LightPitch","自定义光俯仰角");}
                Draw(editor,properties,"_SceneColorInfluence","场景灯光染色强度");
                Draw(editor,properties,"_ReceiveSceneShadows","接收场景投影");
                Draw(editor,properties,"_LightTint","亮部色调");
                Draw(editor,properties,"_MidTint","灰部色调");
                Draw(editor,properties,"_ShadowTint","暗部色调");
            }
            EditorGUILayout.Space();
            if(GUILayout.Button("保存面具材质"))foreach(Material material in editor.targets)AssetDatabase.SaveAssetIfDirty(material);
        }
        static void Draw(MaterialEditor editor,MaterialProperty[] properties,string property,string label)=>editor.ShaderProperty(FindProperty(property,properties),label);
        static void Preset(MaterialEditor editor,bool mid)
        {
            Undo.RecordObjects(editor.targets,"调整面具三渲二");
            foreach(Material material in editor.targets)
            {
                material.SetFloat("_ToonEnabled",1);material.SetFloat("_UseMidtone",mid?1:0);
                material.SetFloat("_ShadowThreshold",0.52f);material.SetFloat("_HighlightThreshold",0.78f);
                material.SetFloat("_LightLevel",1.15f);material.SetFloat("_MidLevel",0.64f);material.SetFloat("_ShadowLevel",0.28f);
                material.SetFloat("_BandSoftness",0.004f);EditorUtility.SetDirty(material);
            }
            editor.PropertiesChanged();SceneView.RepaintAll();EditorApplication.QueuePlayerLoopUpdate();
        }
        [MenuItem("Tools/Rendering/面具三渲二设置")]
        public static void Open()
        {
            var material=Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .SelectMany(r=>r.sharedMaterials).FirstOrDefault(m=>m&&m.shader&&m.shader.name=="RelicExample/Mask Toon");
            if(!material){Debug.LogWarning("当前场景没有使用面具三渲二材质的物体。");return;}
            Selection.activeObject=material;EditorGUIUtility.PingObject(material);
        }
    }
}
