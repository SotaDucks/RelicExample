using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace RelicExample.Rendering
{
    public sealed class GlobalOutlineRendererFeature : ScriptableRendererFeature
    {
        public GlobalOutlineSettings settings;
        public Shader morphologyShader;
        public Shader objectIdShader;
        Material morphologyMaterial;
        Material objectIdMaterial;
        OutlinePass pass;
        public override void Create()
        {
            CoreUtils.Destroy(morphologyMaterial);
            CoreUtils.Destroy(objectIdMaterial);
            if(morphologyShader)morphologyMaterial=CoreUtils.CreateEngineMaterial(morphologyShader);
            if(objectIdShader)objectIdMaterial=CoreUtils.CreateEngineMaterial(objectIdShader);
            pass=new OutlinePass();
        }
        protected override void Dispose(bool disposing){CoreUtils.Destroy(morphologyMaterial);CoreUtils.Destroy(objectIdMaterial);}
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData)
        {
            if (!settings || !settings.IsEnabled || !settings.OutlineMaterial || !morphologyMaterial || !objectIdMaterial) return;
            var camera=renderingData.cameraData.camera;
            if (camera.cameraType==CameraType.Preview || camera.cameraType==CameraType.Reflection) return;
            // The view camera only enlarges an already outlined render texture.
            if (camera.GetComponent<PixelCamera.CanvasViewCamera>()) return;
            pass.material=settings.OutlineMaterial;
            pass.morphology=morphologyMaterial;
            pass.objectIds=objectIdMaterial;
            pass.smoothing=settings.SilhouetteSmoothing;
            renderer.EnqueuePass(pass);
        }
        sealed class OutlinePass : ScriptableRenderPass
        {
            public Material material;
            public Material morphology;
            public Material objectIds;
            public int smoothing;
            static readonly List<ShaderTagId> Tags=new List<ShaderTagId>{new ShaderTagId("UniversalForward"),new ShaderTagId("UniversalForwardOnly"),new ShaderTagId("SRPDefaultUnlit")};
            static readonly MaterialPropertyBlock Properties=new MaterialPropertyBlock();
            sealed class MaskData { public RendererListHandle renderers; }
            sealed class CompositeData
            {
                public TextureHandle source,mask,depth;
                public Material material;
                public Vector4 texelSize;
            }
            sealed class MorphData
            {
                public TextureHandle source;
                public Material material;
                public Vector4 direction;
                public int radius,pass;
                public bool inputNormals;
            }
            public OutlinePass()
            {
                renderPassEvent=RenderPassEvent.BeforeRenderingTransparents;
                ConfigureInput(ScriptableRenderPassInput.Depth);
                requiresIntermediateTexture=true;
            }
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                var resources=frame.Get<UniversalResourceData>();
                var camera=frame.Get<UniversalCameraData>();
                var rendering=frame.Get<UniversalRenderingData>();
                var lights=frame.Get<UniversalLightData>();
                if(resources.isActiveTargetBackBuffer || !resources.activeColorTexture.IsValid() || !resources.activeDepthTexture.IsValid() || !resources.cameraDepthTexture.IsValid())return;

                var source=resources.activeColorTexture;
                var descriptor=graph.GetTextureDesc(source);
                var maskDescriptor=descriptor;
                maskDescriptor.name="GlobalOutline Selected Scenery Normals";
                maskDescriptor.format=GraphicsFormat.R32_SFloat;
                maskDescriptor.depthBufferBits=DepthBits.None;
                maskDescriptor.msaaSamples=MSAASamples.None;
                maskDescriptor.bindTextureMS=false;
                maskDescriptor.clearBuffer=true;maskDescriptor.clearColor=Color.clear;
                maskDescriptor.filterMode=FilterMode.Point;
                var mask=graph.CreateTexture(maskDescriptor);

                var drawing=RenderingUtils.CreateDrawingSettings(Tags,rendering,camera,lights,camera.defaultOpaqueSortFlags);
                drawing.perObjectData=PerObjectData.None;
                drawing.overrideMaterial=objectIds;drawing.overrideMaterialPassIndex=0;drawing.enableInstancing=false;drawing.enableDynamicBatching=false;
                var filtering=new FilteringSettings(RenderQueueRange.opaque,-1,GlobalOutlineTargets.OutlineLayer);
                // Source alpha texture/cutoff and the object's stable ID are supplied per material slot.
                // Visibility is compared with the sampled scene depth; the camera depth is never written.
                var tagValues=new NativeArray<ShaderTagId>(1,Allocator.Temp);tagValues[0]=ShaderTagId.none;
                var stateBlocks=new NativeArray<RenderStateBlock>(1,Allocator.Temp);
                stateBlocks[0]=new RenderStateBlock(RenderStateMask.Depth){depthState=new DepthState(false,CompareFunction.Always)};
                var listParameters=new RendererListParams(rendering.cullResults,drawing,filtering){tagValues=tagValues,stateBlocks=stateBlocks,isPassTagName=false};
                var rendererList=graph.CreateRendererList(listParameters);
                using(var builder=graph.AddRasterRenderPass<MaskData>("GlobalOutline Object Mask",out var data))
                {
                    data.renderers=rendererList;
                    builder.UseRendererList(data.renderers);
                    builder.SetRenderAttachment(mask,0,AccessFlags.Write);
                    builder.UseTexture(resources.cameraDepthTexture,AccessFlags.Read);
                    builder.SetRenderFunc(static (MaskData d,RasterGraphContext context)=>context.cmd.DrawRendererList(d.renderers));
                }
                var texelSize=new Vector4(1f/camera.cameraTargetDescriptor.width,1f/camera.cameraTargetDescriptor.height,camera.cameraTargetDescriptor.width,camera.cameraTargetDescriptor.height);
                // Minimal label-preserving closing fills tiny gaps while retaining each object's ID.
                var closed=Morph(graph,mask,descriptor,texelSize,true,false,0);
                closed=Morph(graph,closed,descriptor,texelSize,false,true,0);
                closed=Morph(graph,closed,descriptor,texelSize,false,false,1);
                closed=Morph(graph,closed,descriptor,texelSize,false,true,1);
                descriptor.name="GlobalOutline Composite";descriptor.clearBuffer=false;descriptor.depthBufferBits=DepthBits.None;
                var output=graph.CreateTexture(descriptor);
                using(var builder=graph.AddRasterRenderPass<CompositeData>("GlobalOutline Composite",out var data))
                {
                    data.source=source;data.mask=closed;data.depth=resources.cameraDepthTexture;data.material=material;data.texelSize=texelSize;
                    builder.UseTexture(data.source,AccessFlags.Read);builder.UseTexture(data.mask,AccessFlags.Read);builder.UseTexture(data.depth,AccessFlags.Read);
                    builder.SetRenderAttachment(output,0,AccessFlags.Write);
                    builder.SetRenderFunc(static (CompositeData d,RasterGraphContext context)=>
                    {
                        Properties.Clear();
                        Properties.SetTexture("_BlitTexture",(RTHandle)d.source);
                        Properties.SetTexture("_GlobalOutlineMask",(RTHandle)d.mask);
                        Properties.SetTexture("_CameraDepthTexture",(RTHandle)d.depth);
                        Properties.SetVector("_BlitScaleBias",new Vector4(1,1,0,0));
                        Properties.SetVector("_BlitTexture_TexelSize",d.texelSize);
                        Properties.SetVector("_CameraDepthTexture_TexelSize",d.texelSize);
                        context.cmd.DrawProcedural(Matrix4x4.identity,d.material,0,MeshTopology.Triangles,3,1,Properties);
                    });
                }
                resources.cameraColor=output;
            }
            TextureHandle Morph(RenderGraph graph,TextureHandle source,TextureDesc descriptor,Vector4 texelSize,bool normals,bool vertical,int shaderPass)
            {
                descriptor.name=shaderPass==0?"GlobalOutline Close Dilate":"GlobalOutline Close Erode";
                descriptor.format=GraphicsFormat.R32_SFloat;descriptor.depthBufferBits=DepthBits.None;
                descriptor.msaaSamples=MSAASamples.None;descriptor.bindTextureMS=false;descriptor.clearBuffer=false;descriptor.filterMode=FilterMode.Point;
                var output=graph.CreateTexture(descriptor);
                using(var builder=graph.AddRasterRenderPass<MorphData>(descriptor.name,out var data))
                {
                    data.source=source;data.material=morphology;
                    data.direction=vertical?new Vector4(0,texelSize.y,0,0):new Vector4(texelSize.x,0,0,0);
                    data.radius=smoothing;data.pass=shaderPass;data.inputNormals=normals;
                    builder.UseTexture(data.source,AccessFlags.Read);builder.SetRenderAttachment(output,0,AccessFlags.Write);
                    builder.SetRenderFunc(static (MorphData d,RasterGraphContext context)=>
                    {
                        Properties.Clear();Properties.SetTexture("_BlitTexture",(RTHandle)d.source);Properties.SetVector("_BlitScaleBias",new Vector4(1,1,0,0));
                        Properties.SetVector("_MaskDirection",d.direction);Properties.SetFloat("_MaskRadius",d.radius);Properties.SetFloat("_InputNormals",d.inputNormals?1:0);
                        context.cmd.DrawProcedural(Matrix4x4.identity,d.material,d.pass,MeshTopology.Triangles,3,1,Properties);
                    });
                }
                return output;
            }
        }
    }
}
