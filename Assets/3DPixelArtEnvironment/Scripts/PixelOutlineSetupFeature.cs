namespace Environment
{
    using UnityEngine.Rendering;
    using UnityEngine.Rendering.Universal;
#if UNITY_6000_0_OR_NEWER
    using UnityEngine.Rendering.RenderGraphModule;
#endif

    /// <summary>
    /// This render feature creates depth normals and colors after rendering opaques.
    /// </summary>
    public class PixelOutlineSetupFeature : ScriptableRendererFeature
    {
        public class EmptyPass : ScriptableRenderPass
        {
            [System.Obsolete("Used by URP Compatibility Mode.")]
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }

#if UNITY_6000_0_OR_NEWER
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                // ConfigureInput requests the textures from URP before graph recording.
                // This setup pass does not draw anything in either rendering path.
            }
#endif
        }

        EmptyPass emptyPass;

        public override void Create()
        {
            this.emptyPass = new EmptyPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            this.emptyPass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Color);
            renderer.EnqueuePass(this.emptyPass);
        }
    }
}
