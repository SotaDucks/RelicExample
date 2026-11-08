using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RelicExample.Rendering
{
    /// <summary>Assigns one persistent frame-to-frame ID per scenery object, retaining original renderer state.</summary>
    public static class GlobalOutlineTargets
    {
        public const uint OutlineLayer=1u<<30;
        sealed class State
        {
            public bool originalBit;
            public MaterialPropertyBlock common;
            public MaterialPropertyBlock[] slots;
        }
        static readonly Dictionary<MeshRenderer,State> states=new Dictionary<MeshRenderer,State>();
        static readonly Dictionary<Transform,int> ids=new Dictionary<Transform,int>();
        static readonly MaterialPropertyBlock block=new MaterialPropertyBlock();
        static int nextId=1;
        static double nextRefresh;
        public static int LastTargetCount{get;private set;}
        public static int LastObjectCount{get;private set;}

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Initialize()
        {
            Restore();nextRefresh=0;
            RenderPipelineManager.beginContextRendering-=BeforeRendering;
            RenderPipelineManager.beginContextRendering+=BeforeRendering;
            SceneManager.sceneLoaded-=OnLoaded;SceneManager.sceneLoaded+=OnLoaded;
        }
        static void OnLoaded(Scene s,LoadSceneMode mode)=>Refresh();
        static void BeforeRendering(ScriptableRenderContext context,List<Camera> cameras)
        {if(Time.realtimeSinceStartupAsDouble>=nextRefresh)Refresh();}

        public static int Refresh()
        {
            nextRefresh=Time.realtimeSinceStartupAsDouble+0.8;
            var settings=GlobalOutlineSettings.Instance;
            if(!settings||!settings.IsEnabled){Restore();return 0;}
            int count=0;var objects=new HashSet<int>();
            foreach(var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if(!renderer.gameObject.scene.IsValid()||!renderer.gameObject.scene.isLoaded)continue;
                #if UNITY_EDITOR
                if(UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(renderer.gameObject.scene))continue;
                #endif
                var materials=renderer.sharedMaterials;
                var selectedSlots=new bool[materials.Length];
                bool selected=false;
                for(int i=0;i<materials.Length;i++){selectedSlots[i]=Eligible(renderer,materials,i,settings);selected|=selectedSlots[i];}
                if(!states.TryGetValue(renderer,out var state)){state=new State{originalBit=(renderer.renderingLayerMask&OutlineLayer)!=0};states[renderer]=state;}
                renderer.renderingLayerMask=selected?renderer.renderingLayerMask|OutlineLayer:renderer.renderingLayerMask&~OutlineLayer;
                if(!selected){RestoreBlocks(renderer,state);continue;}
                var owner=Owner(renderer.transform);
                if(!ids.TryGetValue(owner,out int id)){id=nextId++;ids[owner]=id;}
                objects.Add(id);count++;
                if(state.common==null)
                {
                    state.common=new MaterialPropertyBlock();renderer.GetPropertyBlock(state.common);
                    state.slots=new MaterialPropertyBlock[materials.Length];
                    for(int i=0;i<materials.Length;i++){state.slots[i]=new MaterialPropertyBlock();renderer.GetPropertyBlock(state.slots[i],i);}
                }
                for(int i=0;i<materials.Length;i++)
                {
                    var material=materials[i];if(!material)continue;
                    if(!selectedSlots[i])
                    {
                        // A tree can have leaves and trunk in different submeshes of one renderer.
                        // Restore its original material properties, then exclude just this submesh.
                        var original=state.slots[i].isEmpty?state.common:state.slots[i];
                        renderer.SetPropertyBlock(original.isEmpty?null:original,i);
                        renderer.GetPropertyBlock(block,i);if(block.isEmpty)renderer.GetPropertyBlock(block);
                        block.SetFloat("_GlobalOutlineObjectId",0);renderer.SetPropertyBlock(block,i);continue;
                    }
                    renderer.GetPropertyBlock(block,i);if(block.isEmpty)renderer.GetPropertyBlock(block);
                    block.SetFloat("_GlobalOutlineObjectId",id);
                    string textureName=material.HasProperty("_Base_Map")?"_Base_Map":material.HasProperty("_BaseMap")?"_BaseMap":material.HasProperty("_MainTex")?"_MainTex":null;
                    Texture texture=textureName!=null?material.GetTexture(textureName):null;
                    block.SetTexture("_SourceAlphaMap",texture?texture:Texture2D.whiteTexture);
                    Vector2 scale=textureName!=null?material.GetTextureScale(textureName):Vector2.one;
                    Vector2 offset=textureName!=null?material.GetTextureOffset(textureName):Vector2.zero;
                    block.SetVector("_SourceAlphaMap_ST",new Vector4(scale.x,scale.y,offset.x,offset.y));
                    bool alpha=(material.HasProperty("_AlphaClip")&&material.GetFloat("_AlphaClip")>0.5f)||material.HasProperty("_Alpha_Clip")||material.renderQueue==2450;
                    block.SetFloat("_OutlineCanopyMask",alpha&&(Has(renderer.name,"Tree","Bush","树")||Has(material.shader.name,"SG_Plant","Leaf"))?1:0);
                    float cutoff=material.HasProperty("_Alpha_Clip")?material.GetFloat("_Alpha_Clip"):material.HasProperty("_Cutoff")?material.GetFloat("_Cutoff"):0.5f;
                    block.SetFloat("_SourceAlphaClip",alpha?1:0);block.SetFloat("_SourceCutoff",cutoff);
                    bool plantWind=material.shader.name.Contains("SG_Plant");
                    bool grassWind=material.shader.name.Contains("SG_Grass");
                    block.SetFloat("_SourceAlphaRed",plantWind&&material.HasProperty("_Greyscale")&&material.GetFloat("_Greyscale")>0.5f?1:0);
                    block.SetFloat("_OutlinePlantWind",grassWind?2:plantWind?1:0);
                    block.SetVector("_OutlineWind",plantWind||grassWind?new Vector4(material.GetFloat("_Wind_Strength"),material.GetFloat("_Wind_Density"),material.GetFloat("_Wind_Speed"),material.GetFloat("_UV1")):Vector4.zero);
                    block.SetFloat("_OutlineWindFalloff",plantWind&&material.HasProperty("_UV1_Falloff")?material.GetFloat("_UV1_Falloff"):0);
                    block.SetFloat("_OutlineDepthTolerance",0.025f);
                    if(material.HasProperty("_DepthEdgeStrength"))block.SetFloat("_DepthEdgeStrength",0);
                    if(material.HasProperty("_NormalEdgeStrength"))block.SetFloat("_NormalEdgeStrength",0);
                    renderer.SetPropertyBlock(block,i);
                }
            }
            LastTargetCount=count;LastObjectCount=objects.Count;return count;
        }
        static Transform Owner(Transform t)
        {
            var explicitOwner=t.GetComponentInParent<GlobalOutlineObject>();if(explicitOwner)return explicitOwner.transform;
            var lod=t.GetComponentInParent<LODGroup>();if(lod)return lod.transform;
            var owner=t;
            for(var p=t.parent;p;p=p.parent)
            {
                string name=p.name;
                if(name=="树"||Has(name,"景观","林木","与","草丛","石景","Floor","Terrain"))break;
                if(Has(name,"SM_Tree","SM_Bush","SM_Shrub","Bush_","Shrub_","SM_Roof","Building","House","Candle","Rock","Cliff","亭子","房屋","蜡烛","石头"))owner=p;
            }
            return owner;
        }
        static void RestoreBlocks(MeshRenderer r,State s)
        {
            if(s.common==null)return;
            r.SetPropertyBlock(s.common.isEmpty?null:s.common);
            for(int i=0;i<Math.Min(s.slots.Length,r.sharedMaterials.Length);i++)r.SetPropertyBlock(s.slots[i].isEmpty?null:s.slots[i],i);
            s.common=null;s.slots=null;
        }
        public static void Restore()
        {
            foreach(var e in states)if(e.Key){RestoreBlocks(e.Key,e.Value);e.Key.renderingLayerMask=e.Value.originalBit?e.Key.renderingLayerMask|OutlineLayer:e.Key.renderingLayerMask&~OutlineLayer;}
            states.Clear();ids.Clear();nextId=1;LastTargetCount=0;LastObjectCount=0;
        }
        static bool Has(string value,params string[] words)
        {foreach(var word in words)if(value.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0)return true;return false;}
        enum VegetationPart { None,Canopy,Bush,Trunk }
        static bool IsWood(Material material)=>material&&Has(material.name,"Trunk","Branch","Bark","树干","树皮","枝干");
        static bool IsFoliage(Material material)=>material&&material.shader&&(Has(material.shader.name,"SG_Plant","Leaf","Foliage")||Has(material.name,"Leaf","Leaves","Foliage","树叶","树冠"));
        static VegetationPart Part(MeshRenderer renderer,Material[] materials,int slot)
        {
            string label=renderer.name+"/"+Owner(renderer.transform).name;
            if(Has(label,"Bush","Shrub","灌木"))return VegetationPart.Bush;
            var material=materials[slot];
            if(IsFoliage(material)||Has(renderer.name,"Leaf","Foliage","树冠","树叶"))return VegetationPart.Canopy;
            if(IsWood(material))return VegetationPart.Trunk;
            // Match tree model names rather than the substring in 'street' or 'treeslot'.
            bool tree=Has(label,"Tree_","SM_Tree/","Trunk","Branch","树","枝");
            if(!tree)return VegetationPart.None;
            foreach(var other in materials)if(IsWood(other))return VegetationPart.Canopy;
            foreach(var other in materials)if(IsFoliage(other))return VegetationPart.Trunk;
            return materials.Length>1&&slot>0?VegetationPart.Canopy:VegetationPart.Trunk;
        }
        static bool Eligible(MeshRenderer renderer,Material[] materials,int slot,GlobalOutlineSettings settings)
        {
            string path=renderer.name;for(var p=renderer.transform.parent;p;p=p.parent)path=p.name+"/"+path;
            if(Has(path,"Kaoru","主角","角色","小怪","Enemy","Canvas","DirtCell"))return false;
            var material=materials[slot];
            if(!material||!material.shader||material.renderQueue>2500)return false;
            var part=Part(renderer,materials,slot);
            if(Has(material.shader.name,"Upscale","Water","Terrain"))return false;
            if(part!=VegetationPart.Bush&&(Has(material.shader.name,"SG_Grass")||Has(material.name,"草")||Has(path,"Grass")))return false;
            if(part==VegetationPart.Canopy)return settings.IncludeTreeCanopies;
            if(part==VegetationPart.Bush)return settings.IncludeBushes;
            if(part==VegetationPart.Trunk)return settings.IncludeTrunks;
            return(settings.IncludeBuildings&&Has(path,"Roof","Building","House","Wall","Stair","Gate","Pillar","Lantern","Lamp","Statue","亭","房","柱","门","灯笼"))
                ||(settings.IncludeCandles&&Has(path,"Candle","蜡烛","烛"))
                ||(settings.IncludeRocks&&Has(path,"Rock","Cliff","Stone","Boulder","石","岩"));
        }
    }
}
