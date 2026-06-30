using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using TND.Upscaling.Framework;
using FSRUP = TND.Upscaling.FSR2.FSR2UnityPlugin;

namespace TND.Upscaling.FSR2
{
    public class FSR2Upscaler : UpscalerBase<FSR2UpscalerSettings>
    {
        public static readonly string DisplayName = "FSR 2.2 (Evaluation)";

        public FSR2Upscaler(FSR2UpscalerSettings settings)
            : base(settings)
        {
        }

        private FSRUP.FSR2Context _fsrContext;
        private UpscalerInitParams _initParams;
        private RenderTexture _intermediateOutput;
        
        public override bool Initialize(CommandBuffer commandBuffer, in UpscalerInitParams initParams)
        {
            _initParams = initParams;

            if (!FSRUP.IsInitialized && !FSRUP.InitializePlugin())
            {
                return false;
            }
            
            FSRUP.UpscaleFlags flags = FSRUP.UpscaleFlags.AutoExposure | FSRUP.UpscaleFlags.DynamicResolution;
            if (initParams.enableHDR) flags |= FSRUP.UpscaleFlags.HighDynamicRange;
            if (initParams.invertedDepth) flags |= FSRUP.UpscaleFlags.DepthInverted;
            if (initParams.highResMotionVectors) flags |= FSRUP.UpscaleFlags.DisplayResolutionMotionVectors;
            if (initParams.jitteredMotionVectors) flags |= FSRUP.UpscaleFlags.MotionVectorsJitterCancellation;

            FSRUP.InitializationData initData = new()
            {
                maxRenderSizeWidth = (uint)initParams.maxRenderSize.x,
                maxRenderSizeHeight = (uint)initParams.maxRenderSize.y,
                displaySizeWidth = (uint)initParams.upscaleSize.x,
                displaySizeHeight = (uint)initParams.upscaleSize.y,
                flags = flags,
            };
            
            _fsrContext = FSRUP.CreateFeature(commandBuffer, ref initData);
            return _fsrContext != null;
        }

        public override void Destroy(CommandBuffer commandBuffer)
        {
            if (_fsrContext != null)
            {
                FSRUP.DestroyFeature(commandBuffer, _fsrContext);
                _fsrContext = null;
            }
            
            UpscalerUtils.DestroyRenderTexture(ref _intermediateOutput);

            base.Destroy(commandBuffer);
        }

        public override void Dispatch(CommandBuffer commandBuffer, in UpscalerDispatchParams dispatchParams)
        {
            if (_fsrContext == null)
            {
                return;
            }

            commandBuffer.BeginSample(DisplayName);

            bool requireIntermediate = !_initParams.enableHDR && GraphicsFormatUtility.IsSRGBFormat(dispatchParams.outputColor.GraphicsFormat) && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12;
            if (requireIntermediate && _intermediateOutput == null)
            {
                // FSR2 has an issue with writing to sRGB output textures in non-HDR rendering, causing incorrect image brightness.
                // We work around this by having FSR2 output to a linear format texture, then blitting to the sRGB output texture afterward.
                GraphicsFormat format = GraphicsFormatUtility.GetLinearFormat(dispatchParams.outputColor.GraphicsFormat);
                _intermediateOutput = _initParams.CreateMatchingRenderTexture("_FSR2Output", _initParams.upscaleSize, format, true);
            }
            else if (!requireIntermediate && _intermediateOutput != null)
            {
                UpscalerUtils.DestroyRenderTexture(ref _intermediateOutput);
            }

            FSRUP.ExecutionData execData = new()
            {
                jitterOffset = dispatchParams.jitterOffset,
                mvScale = dispatchParams.motionVectorScale,
                renderSizeWidth = (uint)dispatchParams.renderSize.x,
                renderSizeHeight = (uint)dispatchParams.renderSize.y,
                enableSharpening = dispatchParams.enableSharpening ? 1 : 0,
                sharpness = dispatchParams.sharpness,
                frameTimeDelta = Time.unscaledDeltaTime * 1000.0f,
                preExposure = dispatchParams.preExposure,
                reset = dispatchParams.resetHistory ? 1 : 0,
            };

            Camera camera = _initParams.camera;
            if (camera != null)
            {
                execData.cameraNear = camera.nearClipPlane;
                execData.cameraFar = camera.farClipPlane;
                execData.cameraFovAngleVertical = camera.fieldOfView * Mathf.Deg2Rad;
            }
            else
            {
                execData.cameraNear = 0.3f;
                execData.cameraFar = 1000f;
                execData.cameraFovAngleVertical = 60f * Mathf.Deg2Rad;
            }
            
            FSRUP.TextureTable textureTable = new()
            {
                inputColor = dispatchParams.inputColor.GetTexture(commandBuffer),
                inputDepth = dispatchParams.inputDepth.GetTexture(commandBuffer),
                inputMotionVectors = dispatchParams.inputMotionVectors.GetTexture(commandBuffer),
                inputExposure = dispatchParams.inputExposure.GetTexture(commandBuffer),
                inputReactiveMask = dispatchParams.inputReactiveMask.GetTexture(commandBuffer),
                inputTransparencyMask = Settings.transparencyMask,
                outputColor = requireIntermediate ? _intermediateOutput : dispatchParams.outputColor.GetTexture(commandBuffer),
            };
            
            FSRUP.ExecuteFeature(commandBuffer, _fsrContext, ref execData, textureTable);

            if (requireIntermediate)
            {
                commandBuffer.Blit(_intermediateOutput, dispatchParams.outputColor.GetRenderTargetIdentifier());
            }
            
            commandBuffer.EndSample(DisplayName);
        }
    }
}
