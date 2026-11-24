using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace RelicExample.Rendering
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    [DefaultExecutionOrder(100)]
    public sealed class GroundFogPatch : MonoBehaviour
    {
        [SerializeField] private Transform player;
        [SerializeField] private ComputeShader solver;
        [SerializeField] private Texture2D restDensity;
        [SerializeField, Min(4f)] private float interactionWindowSize = 16f;
        [SerializeField, Min(0.05f)] private float interactionRadius = 0.76f;
        [SerializeField, Min(0)] private float pushStrength = 9f;
        [SerializeField, Min(0)] private float clearingRate = 27f;
        [SerializeField, Min(0)] private float recoveryRate = 0.7f;

        [Header("Wake and Billows")]
        [SerializeField, Range(0, 1)] private float redistributedDensity = 0.82f;
        [SerializeField, Min(0)] private float liftHeight = 0.36f;
        [SerializeField, Min(0.02f)] private float liftHalfLife = 0.46f;
        [SerializeField, Min(0)] private float vorticityStrength = 3.4f;
        [SerializeField, Min(0)] private float velocityDamping = 0.62f;

        private const int Resolution = 256;
        private const int Groups = Resolution / 8;
        private const int PressureIterations = 18;
        private const int MaxCatchUpSteps = 4;
        private const int WindowShiftCells = 8;
        private const float StepTime = 1f / 24f;
        private static readonly int DensityMap = Shader.PropertyToID("_DensityMap");
        private static readonly int LiveDensity = Shader.PropertyToID("_LiveDensity");
        private static readonly int MotionMap = Shader.PropertyToID("_MotionMap");
        private static readonly int HistoryMap = Shader.PropertyToID("_HistoryMap");
        private static readonly int HistoryArea = Shader.PropertyToID("_HistoryArea");
        private static readonly int SimulationWindow = Shader.PropertyToID("_SimulationWindow");
        private static readonly int DensityPatternSize = Shader.PropertyToID("_DensityPatternSize");
        private static readonly int DetailPattern = Shader.PropertyToID("_Detail");
        private static readonly int LiftExtent = Shader.PropertyToID("_LiftExtent");

        private RenderTexture velocityRead, velocityWrite;
        private RenderTexture densityRead, densityWrite;
        private RenderTexture motionRead, motionWrite;
        private RenderTexture historyRead, historyWrite;
        private RenderTexture historyVelocityRead, historyVelocityWrite;
        private RenderTexture pressureRead, pressureWrite, divergence, curl;
        private int initialize, shiftWindow, advectVelocity, buildCurl, confineVorticity;
        private int buildDivergence, solvePressure, projectVelocity, advectDensity, updateHistory;
        private MeshRenderer fogRenderer;
        private Material fogMaterial;
        private Texture2D densityPattern;
        private Texture3D detailPattern;
        private MaterialPropertyBlock properties, originalProperties;
        private Bounds originalLocalBounds;
        private float appliedLiftExtent;
        private float windowSize;
        private Vector4 historyArea;
        private Vector2 windowCenter, lastFramePosition, lastStepPosition;
        private float accumulatedTime;

        private void OnEnable()
        {
            if (!player || !solver || !SystemInfo.supportsComputeShaders)
            {
                Debug.LogWarning("Ground fog requires a player, a solver and compute shader support.", this);
                return;
            }

            fogRenderer = GetComponent<MeshRenderer>();
            fogMaterial = fogRenderer.sharedMaterial;
            densityPattern = fogMaterial ? fogMaterial.GetTexture(DensityMap) as Texture2D : null;
            if (!densityPattern)
                densityPattern = restDensity;
            detailPattern = fogMaterial ? fogMaterial.GetTexture(DetailPattern) as Texture3D : null;
            if (!fogMaterial || !densityPattern || !detailPattern)
            {
                Debug.LogWarning("Ground fog requires a material with density and volume detail textures.", this);
                return;
            }

            windowSize = Mathf.Max(interactionWindowSize, 4f);
            Bounds worldBounds = fogRenderer.bounds;
            historyArea = new Vector4(worldBounds.center.x, worldBounds.center.z,
                Mathf.Max(windowSize, worldBounds.size.x + 4f), Mathf.Max(windowSize, worldBounds.size.z + 4f));
            lastFramePosition = lastStepPosition = WorldXZ(player.position);
            windowCenter = SnapToCell(lastFramePosition);

            try
            {
                initialize = solver.FindKernel("Initialize");
                shiftWindow = solver.FindKernel("ShiftWindow");
                advectVelocity = solver.FindKernel("AdvectVelocity");
                buildCurl = solver.FindKernel("BuildCurl");
                confineVorticity = solver.FindKernel("ConfineVorticity");
                buildDivergence = solver.FindKernel("BuildDivergence");
                solvePressure = solver.FindKernel("SolvePressure");
                projectVelocity = solver.FindKernel("ProjectVelocity");
                advectDensity = solver.FindKernel("AdvectDensity");
                updateHistory = solver.FindKernel("UpdateHistory");

                velocityRead = Allocate("Fog velocity A", GraphicsFormat.R32G32_SFloat);
                velocityWrite = Allocate("Fog velocity B", GraphicsFormat.R32G32_SFloat);
                densityRead = Allocate("Fog density A", GraphicsFormat.R32_SFloat);
                densityWrite = Allocate("Fog density B", GraphicsFormat.R32_SFloat);
                motionRead = Allocate("Fog motion A", GraphicsFormat.R16G16B16A16_SFloat);
                motionWrite = Allocate("Fog motion B", GraphicsFormat.R16G16B16A16_SFloat);
                historyRead = Allocate("Fog wake history A", GraphicsFormat.R16G16B16A16_SFloat);
                historyWrite = Allocate("Fog wake history B", GraphicsFormat.R16G16B16A16_SFloat);
                historyVelocityRead = Allocate("Fog wake velocity A", GraphicsFormat.R32G32_SFloat);
                historyVelocityWrite = Allocate("Fog wake velocity B", GraphicsFormat.R32G32_SFloat);
                pressureRead = Allocate("Fog pressure A", GraphicsFormat.R32_SFloat);
                pressureWrite = Allocate("Fog pressure B", GraphicsFormat.R32_SFloat);
                divergence = Allocate("Fog divergence", GraphicsFormat.R32_SFloat);
                curl = Allocate("Fog curl", GraphicsFormat.R32_SFloat);

                SetGridParameters();
                solver.SetTexture(initialize, "_VelocityWrite", velocityRead);
                solver.SetTexture(initialize, "_DensityWrite", densityRead);
                solver.SetTexture(initialize, "_MotionWrite", motionRead);
                solver.SetTexture(initialize, "_HistoryWrite", historyRead);
                solver.SetTexture(initialize, "_HistoryVelocityWrite", historyVelocityRead);
                BindDensityPattern(initialize);
                Dispatch(initialize);

                properties = new MaterialPropertyBlock();
                originalProperties = new MaterialPropertyBlock();
                fogRenderer.GetPropertyBlock(originalProperties);
                fogRenderer.GetPropertyBlock(properties);
                originalLocalBounds = fogRenderer.localBounds;
                appliedLiftExtent = 0f;
                accumulatedTime = 0;
                UpdateMaterial();
            }
            catch (Exception error)
            {
                RestoreRenderer();
                ReleaseTextures();
                Debug.LogException(error, this);
            }
        }

        private void LateUpdate()
        {
            if (!densityRead || !player)
                return;

            float elapsed = Mathf.Min(Time.deltaTime, StepTime * MaxCatchUpSteps);
            if (elapsed <= 0)
                return;

            Vector2 position = WorldXZ(player.position);
            float teleportDistance = interactionRadius * 5f;
            if ((position - lastFramePosition).sqrMagnitude > teleportDistance * teleportDistance)
                lastFramePosition = lastStepPosition = position;

            accumulatedTime += elapsed;
            bool advanced = accumulatedTime >= StepTime;
            while (accumulatedTime >= StepTime)
            {
                accumulatedTime -= StepTime;
                Vector2 end = Vector2.Lerp(lastFramePosition, position, 1f - accumulatedTime / elapsed);
                MoveWindow(end);
                Simulate(lastStepPosition, end);
                lastStepPosition = end;
            }

            lastFramePosition = position;
            if (advanced)
                UpdateMaterial();
        }

        private void MoveWindow(Vector2 playerPosition)
        {
            Vector2 difference = playerPosition - windowCenter;
            float scrollDistance = windowSize * WindowShiftCells / Resolution;
            if (Mathf.Max(Mathf.Abs(difference.x), Mathf.Abs(difference.y)) < scrollDistance)
                return;

            Vector2 nextCenter = SnapToCell(playerPosition);
            Vector2 offset = (nextCenter - windowCenter) * (Resolution / windowSize);
            windowCenter = nextCenter;

            SetGridParameters();
            solver.SetInts("_WindowShift", Mathf.RoundToInt(offset.x), Mathf.RoundToInt(offset.y));
            solver.SetTexture(shiftWindow, "_VelocityRead", velocityRead);
            solver.SetTexture(shiftWindow, "_VelocityWrite", velocityWrite);
            solver.SetTexture(shiftWindow, "_DensityRead", densityRead);
            solver.SetTexture(shiftWindow, "_DensityWrite", densityWrite);
            solver.SetTexture(shiftWindow, "_MotionRead", motionRead);
            solver.SetTexture(shiftWindow, "_MotionWrite", motionWrite);
            solver.SetTexture(shiftWindow, "_HistoryRead", historyRead);
            solver.SetTexture(shiftWindow, "_HistoryVelocityRead", historyVelocityRead);
            BindDensityPattern(shiftWindow);
            Dispatch(shiftWindow);
            (velocityRead, velocityWrite) = (velocityWrite, velocityRead);
            (densityRead, densityWrite) = (densityWrite, densityRead);
            (motionRead, motionWrite) = (motionWrite, motionRead);
        }

        private void Simulate(Vector2 start, Vector2 end)
        {
            SetGridParameters();
            solver.SetFloat("_StepTime", StepTime);
            solver.SetFloat("_InteractionRadius", interactionRadius);
            solver.SetFloat("_PushStrength", pushStrength);
            solver.SetFloat("_ClearingRate", clearingRate);
            solver.SetFloat("_RecoveryRate", recoveryRate);
            solver.SetFloat("_RedistributedDensity", redistributedDensity);
            solver.SetFloat("_LiftHalfLife", liftHalfLife);
            solver.SetFloat("_VorticityStrength", vorticityStrength);
            solver.SetFloat("_VelocityDamping", velocityDamping);
            Vector2 localStart = start - windowCenter;
            Vector2 localEnd = end - windowCenter;
            solver.SetVector("_Stroke", new Vector4(localStart.x, localStart.y, localEnd.x, localEnd.y));

            solver.SetTexture(advectVelocity, "_VelocityRead", velocityRead);
            solver.SetTexture(advectVelocity, "_VelocityWrite", velocityWrite);
            Dispatch(advectVelocity);
            (velocityRead, velocityWrite) = (velocityWrite, velocityRead);

            if (vorticityStrength > 0)
            {
                solver.SetTexture(buildCurl, "_VelocityRead", velocityRead);
                solver.SetTexture(buildCurl, "_CurlWrite", curl);
                Dispatch(buildCurl);

                solver.SetTexture(confineVorticity, "_VelocityRead", velocityRead);
                solver.SetTexture(confineVorticity, "_VelocityWrite", velocityWrite);
                solver.SetTexture(confineVorticity, "_CurlRead", curl);
                Dispatch(confineVorticity);
                (velocityRead, velocityWrite) = (velocityWrite, velocityRead);
            }

            solver.SetTexture(buildDivergence, "_VelocityRead", velocityRead);
            solver.SetTexture(buildDivergence, "_DivergenceWrite", divergence);
            solver.SetTexture(buildDivergence, "_PressureWrite", pressureRead);
            Dispatch(buildDivergence);

            solver.SetTexture(solvePressure, "_DivergenceRead", divergence);
            for (int i = 0; i < PressureIterations; i++)
            {
                solver.SetTexture(solvePressure, "_PressureRead", pressureRead);
                solver.SetTexture(solvePressure, "_PressureWrite", pressureWrite);
                Dispatch(solvePressure);
                (pressureRead, pressureWrite) = (pressureWrite, pressureRead);
            }

            solver.SetTexture(projectVelocity, "_PressureRead", pressureRead);
            solver.SetTexture(projectVelocity, "_VelocityRead", velocityRead);
            solver.SetTexture(projectVelocity, "_VelocityWrite", velocityWrite);
            Dispatch(projectVelocity);
            (velocityRead, velocityWrite) = (velocityWrite, velocityRead);

            solver.SetTexture(advectDensity, "_VelocityRead", velocityRead);
            solver.SetTexture(advectDensity, "_DensityRead", densityRead);
            solver.SetTexture(advectDensity, "_DensityWrite", densityWrite);
            solver.SetTexture(advectDensity, "_MotionRead", motionRead);
            solver.SetTexture(advectDensity, "_MotionWrite", motionWrite);
            BindDensityPattern(advectDensity);
            Dispatch(advectDensity);
            (densityRead, densityWrite) = (densityWrite, densityRead);
            (motionRead, motionWrite) = (motionWrite, motionRead);

            solver.SetTexture(updateHistory, "_VelocityRead", velocityRead);
            solver.SetTexture(updateHistory, "_DensityRead", densityRead);
            solver.SetTexture(updateHistory, "_MotionRead", motionRead);
            solver.SetTexture(updateHistory, "_HistoryRead", historyRead);
            solver.SetTexture(updateHistory, "_HistoryWrite", historyWrite);
            solver.SetTexture(updateHistory, "_HistoryVelocityRead", historyVelocityRead);
            solver.SetTexture(updateHistory, "_HistoryVelocityWrite", historyVelocityWrite);
            BindDensityPattern(updateHistory);
            Dispatch(updateHistory);
            (historyRead, historyWrite) = (historyWrite, historyRead);
            (historyVelocityRead, historyVelocityWrite) = (historyVelocityWrite, historyVelocityRead);
        }

        private static Vector2 WorldXZ(Vector3 position) => new Vector2(position.x, position.z);

        private Vector2 SnapToCell(Vector2 position)
        {
            float cellSize = windowSize / Resolution;
            return new Vector2(Mathf.Round(position.x / cellSize), Mathf.Round(position.y / cellSize)) * cellSize;
        }

        private void SetGridParameters()
        {
            solver.SetInt("_Resolution", Resolution);
            solver.SetVector("_FieldSize", new Vector4(windowSize, windowSize, 0, 0));
            solver.SetVector("_FieldCenter", new Vector4(windowCenter.x, windowCenter.y, 0, 0));
            solver.SetVector(HistoryArea, historyArea);
            solver.SetFloat("_PatternTime", Time.time);
        }

        private void BindDensityPattern(int kernel)
        {
            solver.SetFloat("_DensityPatternSize", fogMaterial.GetFloat(DensityPatternSize));
            solver.SetTexture(kernel, "_RestDensity", densityPattern);
            solver.SetTexture(kernel, "_PatternDetail", detailPattern);
        }

        private void Dispatch(int kernel) => solver.Dispatch(kernel, Groups, Groups, 1);

        private void UpdateMaterial()
        {
            float objectSpaceLift = liftHeight / Mathf.Max(Mathf.Abs(transform.lossyScale.y), 0.01f);
            if (!Mathf.Approximately(objectSpaceLift, appliedLiftExtent))
            {
                Bounds bounds = originalLocalBounds;
                bounds.max += Vector3.up * objectSpaceLift;
                fogRenderer.localBounds = bounds;
                appliedLiftExtent = objectSpaceLift;
            }

            properties.SetTexture(DensityMap, densityPattern);
            properties.SetTexture(LiveDensity, densityRead);
            properties.SetTexture(MotionMap, motionRead);
            properties.SetTexture(HistoryMap, historyRead);
            properties.SetVector(HistoryArea, historyArea);
            properties.SetVector(SimulationWindow, new Vector4(windowCenter.x, windowCenter.y, windowSize, 1f / windowSize));
            properties.SetFloat(LiftExtent, objectSpaceLift);
            fogRenderer.SetPropertyBlock(properties);
        }

        private static RenderTexture Allocate(string name, GraphicsFormat format)
        {
            var descriptor = new RenderTextureDescriptor(Resolution, Resolution, format, 0)
            {
                enableRandomWrite = true
            };
            var texture = new RenderTexture(descriptor)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            if (texture.Create())
                return texture;

            Destroy(texture);
            throw new InvalidOperationException($"Could not allocate {name} ({format}).");
        }

        private void OnDisable()
        {
            RestoreRenderer();
            ReleaseTextures();
        }

        private void RestoreRenderer()
        {
            if (!fogRenderer || originalProperties == null)
                return;

            fogRenderer.SetPropertyBlock(originalProperties);
            if (appliedLiftExtent > 0f)
                fogRenderer.localBounds = originalLocalBounds;
            originalProperties = null;
        }

        private void ReleaseTextures()
        {
            Release(ref velocityRead);
            Release(ref velocityWrite);
            Release(ref densityRead);
            Release(ref densityWrite);
            Release(ref motionRead);
            Release(ref motionWrite);
            Release(ref historyRead);
            Release(ref historyWrite);
            Release(ref historyVelocityRead);
            Release(ref historyVelocityWrite);
            Release(ref pressureRead);
            Release(ref pressureWrite);
            Release(ref divergence);
            Release(ref curl);
        }

        private static void Release(ref RenderTexture texture)
        {
            if (!texture)
                return;

            texture.Release();
            Destroy(texture);
            texture = null;
        }
    }
}
