using UnityEngine;
using UnityEditor;
using RelicExample.Rendering;

namespace RelicExample.EditorTools
{
    [CustomEditor(typeof(GlobalOutlineSettings))]
    public sealed class GlobalOutlineSettingsEditor : Editor
    {
        bool advanced;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("每个物体拥有独立轮廓身份，描边贴合它本身的形状；相邻物体不会合成一整片。内部纹理和面片不生成线条。", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            Field("outlineEnabled", "启用全局描边");
            Field("thickness", "描边粗细（像素）");
            Field("outlineColor", "暗边颜色");
            Field("opacity", "轮廓暗边强度");
            Field("rimHighlight", "外沿微弱高光");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("自动覆盖的物体", EditorStyles.boldLabel);
            Field("includeTreeCanopies", "树冠");
            Field("includeBushes", "灌木丛");
            Field("includeTrunks", "树干");
            Field("includeBuildings", "建筑与灯笼");
            Field("includeCandles", "蜡烛");
            Field("includeRocks", "石头与岩壁");
            Field("includeMasks", "傩戏面具");
            if (GUILayout.Button("打开面具三渲二材质")) MaskToonShaderGUI.Open();
            advanced = EditorGUILayout.Foldout(advanced, "高级：外轮廓平滑", true);
            if (advanced)
            {
                Field("silhouetteSmoothing", "细缝修补（贴合建议 0～1）");
                Field("suppressCanopyHoles", "忽略树冠内部碎孔");
                using (new EditorGUI.DisabledScope(true)) Field("outlineMaterial", "共享描边材质");
            }
            if (EditorGUI.EndChangeCheck())
            {
                var profile = (GlobalOutlineSettings)target;
                if (profile.OutlineMaterial) Undo.RecordObject(profile.OutlineMaterial, "Adjust global outline");
                serializedObject.ApplyModifiedProperties();
                profile.Apply();
                GlobalOutlineTargets.Refresh();
                EditorUtility.SetDirty(profile);
                if (profile.OutlineMaterial) EditorUtility.SetDirty(profile.OutlineMaterial);
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
            }
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("粗细为 0 时关闭，正数按整数原始像素向上取整。平滑建议 0～1，以贴合模型形状。勾选“忽略树冠内部碎孔”可清除叶片内部碎线。火苗、萤火虫和光束不参与描边。", MessageType.None);
            if (GUILayout.Button("保存描边设置"))
            {
                var profile = (GlobalOutlineSettings)target;
                AssetDatabase.SaveAssetIfDirty(profile);
                if (profile.OutlineMaterial) AssetDatabase.SaveAssetIfDirty(profile.OutlineMaterial);
            }
        }
        void Field(string property, string label) => EditorGUILayout.PropertyField(serializedObject.FindProperty(property), new GUIContent(label));
    }

    [InitializeOnLoad]
    public static class GlobalOutlineEditorTargets
    {
        static double nextUpdate;
        static GlobalOutlineEditorTargets()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += GlobalOutlineTargets.Restore;
        }
        static void Update()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < nextUpdate) return;
            nextUpdate = EditorApplication.timeSinceStartup + 0.8;
            GlobalOutlineTargets.Refresh();
        }
    }

    public sealed class GlobalOutlineWindow : EditorWindow
    {
        Editor inspector;
        [MenuItem("Tools/Rendering/全局描边设置")]
        public static void Open() => GetWindow<GlobalOutlineWindow>("全局描边").Show();
        void OnGUI()
        {
            var profile = GlobalOutlineSettings.Instance;
            if (!profile) { EditorGUILayout.HelpBox("未找到 Resources/GlobalOutlineSettings.asset。", MessageType.Warning); return; }
            Editor.CreateCachedEditor(profile, null, ref inspector);
            inspector.OnInspectorGUI();
        }
        void OnDisable() { if (inspector) DestroyImmediate(inspector); }
    }
}
