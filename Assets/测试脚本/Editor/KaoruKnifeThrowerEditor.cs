using UnityEditor;
using UnityEngine;

/// <summary>Shows the assigned knife prefab's flight settings inside the Knife Thrower inspector.</summary>
[CustomEditor(typeof(KaoruKnifeThrower))]
[CanEditMultipleObjects]
public sealed class KaoruKnifeThrowerEditor : Editor
{
    private const string ExpandedKey = "Kaoru.KnifeThrower.ShowKnifeSettings";
    private Editor knifeEditor;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        KaoruThrowingKnife knife = ((KaoruKnifeThrower)target).knifePrefab;
        if (knife == null)
        {
            EditorGUILayout.HelpBox("指定 Knife Prefab 后，这里会显示飞刀的物理参数。", MessageType.Info);
            return;
        }
        foreach (Object other in targets)
            if (((KaoruKnifeThrower)other).knifePrefab != knife)
            {
                EditorGUILayout.HelpBox("所选角色使用了不同的飞刀 Prefab，无法同时编辑。", MessageType.Info);
                return;
            }

        bool expanded = EditorPrefs.GetBool(ExpandedKey, true);
        expanded = EditorGUILayout.BeginFoldoutHeaderGroup(expanded, "飞刀物理参数（" + knife.gameObject.name + " Prefab）");
        EditorPrefs.SetBool(ExpandedKey, expanded);
        if (expanded)
        {
            // Edits write straight to the prefab asset, so every thrower using it shares them,
            // and changes made in Play mode are kept.
            EditorGUILayout.HelpBox("这里修改的是飞刀 Prefab 本身：所有使用它的角色共享，Play 模式中的修改也会保留，对之后投出的飞刀生效。",
                MessageType.None);
            CreateCachedEditor(knife, null, ref knifeEditor);
            using (new EditorGUI.IndentLevelScope())
                knifeEditor.OnInspectorGUI();
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    private void OnDisable()
    {
        if (knifeEditor != null)
            DestroyImmediate(knifeEditor);
    }
}
