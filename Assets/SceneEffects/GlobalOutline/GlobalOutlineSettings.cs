using UnityEngine;
using UnityEngine.Serialization;

namespace RelicExample.Rendering
{
    /// <summary>One shared profile controls the outline material used by the URP fullscreen pass.</summary>
    [CreateAssetMenu(fileName = "GlobalOutlineSettings", menuName = "Rendering/Global Outline Settings")]
    public sealed class GlobalOutlineSettings : ScriptableObject
    {
        [SerializeField] bool outlineEnabled = true;
        [SerializeField, Range(0, 6)] float thickness = 2;
        [SerializeField] Color outlineColor = new Color(0.005f, 0.006f, 0.008f, 1);
        [SerializeField, Range(0, 1)] float opacity = 1f;
        [SerializeField, Range(0, 0.3f)] float rimHighlight = 0.12f;
        [SerializeField, Range(0, 8)] int silhouetteSmoothing = 1;
        [SerializeField] bool suppressCanopyHoles = true;
        [SerializeField, FormerlySerializedAs("includeTrees")] bool includeTreeCanopies = true;
        [SerializeField] bool includeBushes = true;
        [SerializeField] bool includeTrunks = true;
        [SerializeField, HideInInspector] int vegetationSettingsVersion;
        [SerializeField] bool includeBuildings = true;
        [SerializeField] bool includeCandles = true;
        [SerializeField] bool includeRocks = true;
        [SerializeField] Material outlineMaterial;

        static GlobalOutlineSettings instance;
        public static GlobalOutlineSettings Instance => instance ? instance : (instance = Resources.Load<GlobalOutlineSettings>("GlobalOutlineSettings"));
        public float Thickness => thickness;
        public bool IsEnabled => outlineEnabled && thickness > 0;
        public int SilhouetteSmoothing => Mathf.Clamp(silhouetteSmoothing,0,8);
        public bool IncludeTreeCanopies => includeTreeCanopies;
        public bool IncludeBushes => includeBushes;
        public bool IncludeTrunks => includeTrunks;
        public bool IncludeTrees => includeTreeCanopies || includeBushes || includeTrunks;
        public bool IncludeBuildings => includeBuildings;
        public bool IncludeCandles => includeCandles;
        public bool IncludeRocks => includeRocks;
        public Material OutlineMaterial => outlineMaterial;

        public void SetThickness(float renderPixels) { thickness = Mathf.Clamp(renderPixels, 0, 6); Apply(); }
        public void SetEnabled(bool value) { outlineEnabled = value; Apply(); }
        public void SetColor(Color value) { outlineColor = value; Apply(); }
        public void BindMaterial(Material material) { outlineMaterial = material; Apply(); }

        public void Apply()
        {
            if (!outlineMaterial) return;
            outlineMaterial.SetFloat("_OutlineEnabled", outlineEnabled ? 1 : 0);
            outlineMaterial.SetFloat("_OutlineWidth", Mathf.Clamp(thickness, 0, 6));
            outlineMaterial.SetColor("_OutlineColor", outlineColor);
            outlineMaterial.SetFloat("_OutlineOpacity", Mathf.Clamp01(opacity));
            outlineMaterial.SetFloat("_RimHighlight", Mathf.Clamp(rimHighlight,0,0.3f));
            outlineMaterial.SetFloat("_SuppressCanopyHoles",suppressCanopyHoles?1:0);
        }

        void UpgradeVegetationSettings()
        {
            if (vegetationSettingsVersion >= 1) return;
            // Preserve the old combined checkbox when loading an existing profile.
            includeBushes = includeTrunks = includeTreeCanopies;
            vegetationSettingsVersion = 1;
        }
        void OnEnable() { UpgradeVegetationSettings(); Apply(); }
        void OnValidate()
        {
            UpgradeVegetationSettings();
            #if UNITY_EDITOR
            // Validation may happen during loading. Defer Unity material calls to the editor thread.
            UnityEditor.EditorApplication.delayCall += ApplyInEditor;
            #else
            Apply();
            #endif
        }
        #if UNITY_EDITOR
        void ApplyInEditor()
        {
            if (!this) return;
            Apply();
            if (outlineMaterial) UnityEditor.EditorUtility.SetDirty(outlineMaterial);
            UnityEditor.SceneView.RepaintAll();
        }
        #endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyAtStartup() { if (Instance) Instance.Apply(); }
    }
}
