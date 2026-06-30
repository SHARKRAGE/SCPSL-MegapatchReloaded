using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace TND.Upscaling.FSR2
{
    public static class FSR2UnityPlugin
    {
        private const string LibraryName = "FSR2UnityPlugin";

        public class FSR2Context: IDisposable
        {
            public readonly uint featureSlot;
            public readonly NativeStruct<InitializationData> initData = new();
            public readonly NativeStruct<ExecutionData> execData = new();

            public FSR2Context(uint featureSlot)
            {
                this.featureSlot = featureSlot;
            }

            public void Dispose()
            {
                initData.Dispose();
                execData.Dispose();
            }
        }

        private static int s_baseEventId = -1;

        public static bool IsInitialized => s_baseEventId >= 0;
        
        public static bool IsSupported()
        {
            try
            {
                if (!IsInitialized && !InitializePluginInternal())
                {
                    return false;
                }

                return SystemInfo.operatingSystemFamily is OperatingSystemFamily.Windows && 
                       SystemInfo.graphicsDeviceType is GraphicsDeviceType.Direct3D11 or GraphicsDeviceType.Direct3D12 or GraphicsDeviceType.Vulkan;
            }
            catch (DllNotFoundException)
            {
                // This is a valid case on non-Windows platforms, so don't log an error here
                return false;
            }
        }

        public static bool InitializePlugin()
        {
            if (IsInitialized)
                return true;
            
            try
            {
                return InitializePluginInternal();
            }
            catch (DllNotFoundException ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }
        
        private static bool InitializePluginInternal()
        {
            if (!Initialize())
                return false;

            s_baseEventId = GetBaseEventId();
            return true;
        }

        public static void ShutdownPlugin()
        {
            if (!IsInitialized)
                return;
            
            s_baseEventId = -1;
            
            try
            {
                Shutdown();
            }
            catch (DllNotFoundException)
            {
                // In case someone tries to shut down the plugin after initialization fails due to a missing plugin.
                // We don't need to see the same error twice.
            }
        }

        public static FSR2Context CreateFeature(CommandBuffer cmd, ref InitializationData initData)
        {
            if (!IsInitialized)
            {
                Debug.LogWarning("FSR2 plugin not initialized yet!");
                return null;
            }

            uint featureSlot = CreateFeatureSlot();
            if (featureSlot == uint.MaxValue)
                return null;

            FSR2Context fsrContext = new(featureSlot);
            initData.featureSlot = featureSlot;
            fsrContext.initData.SetData(ref initData);
            cmd.IssuePluginEventAndData(GetRenderEventCallback(), s_baseEventId + (int)PluginEvent.InitFeature, fsrContext.initData.GetPointer());
            
            return fsrContext;
        }

        public static void ExecuteFeature(CommandBuffer cmd, FSR2Context fsrContext, ref ExecutionData execData, in TextureTable textures)
        {
            if (!IsInitialized)
            {
                Debug.LogWarning("FSR2 plugin not initialized yet!");
                return;
            }

            uint featureSlot = fsrContext.featureSlot;
            if (featureSlot == uint.MaxValue)
            {
                Debug.LogWarning($"Invalid FSR2 feature slot: {featureSlot}");
                return;
            }

            IntPtr textureCallback = GetSetTextureEventCallback();
            SetTexture(cmd, textureCallback, featureSlot, TextureSlot.ColorInput, textures.inputColor, true);
            SetTexture(cmd, textureCallback, featureSlot, TextureSlot.ColorOutput, textures.outputColor);
            SetTexture(cmd, textureCallback, featureSlot, TextureSlot.Depth, textures.inputDepth);
            SetTexture(cmd, textureCallback, featureSlot, TextureSlot.MotionVectors, textures.inputMotionVectors);
            SetTexture(cmd, textureCallback, featureSlot, TextureSlot.TransparencyMask, textures.inputTransparencyMask);
            SetTexture(cmd, textureCallback, featureSlot, TextureSlot.ExposureTexture, textures.inputExposure);
            SetTexture(cmd, textureCallback, featureSlot, TextureSlot.ReactiveMask, textures.inputReactiveMask);
            SetTexture(cmd, textureCallback, featureSlot, TextureSlot.BiasColorMask, textures.inputReactiveMask);
            
            execData.featureSlot = featureSlot;
            fsrContext.execData.SetData(ref execData);
            cmd.IssuePluginEventAndData(GetRenderEventCallback(), s_baseEventId + (int)PluginEvent.Execute, fsrContext.execData.GetPointer());
        }

        public static void DestroyFeature(CommandBuffer cmd, FSR2Context fsrContext)
        {
            if (!IsInitialized)
            {
                Debug.LogWarning("FSR2 plugin not initialized yet!");
                return;
            }

            uint featureSlot = fsrContext.featureSlot;
            if (featureSlot == uint.MaxValue)
            {
                Debug.LogWarning($"Invalid FSR2 feature slot: {featureSlot}");
                return;
            }
            
            cmd.IssuePluginEventAndData(GetRenderEventCallback(), s_baseEventId + (int)PluginEvent.DestroyFeature, (IntPtr)featureSlot);

            fsrContext.Dispose();
        }

        private static void SetTexture(CommandBuffer cmd, IntPtr callback, uint featureSlot, TextureSlot textureSlot, Texture texture, bool clearTextureTable = false)
        {
            if (texture == null)
                return;
            
            uint userData = ((featureSlot & 0xFFFF) << 16) | (((uint)textureSlot & 0xFFFF) << 1) | (clearTextureTable ? 1u : 0u);
            cmd.IssuePluginCustomTextureUpdateV2(callback, texture, userData);
        }

        [DllImport(LibraryName, EntryPoint = "AMDUP_InitApi")]
        private static extern bool Initialize();

        [DllImport(LibraryName, EntryPoint = "AMDUP_ShutdownApi")]
        private static extern void Shutdown();

        [DllImport(LibraryName, EntryPoint = "AMDUP_GetRenderEventCallback")]
        private static extern IntPtr GetRenderEventCallback();

        [DllImport(LibraryName, EntryPoint = "AMDUP_GetSetTextureEventCallback")]
        private static extern IntPtr GetSetTextureEventCallback();

        [DllImport(LibraryName, EntryPoint = "AMDUP_CreateFeatureSlot")]
        private static extern uint CreateFeatureSlot();

        [DllImport(LibraryName, EntryPoint = "AMDUP_GetBaseEventId")]
        private static extern int GetBaseEventId();

        private enum PluginEvent
        {
            DestroyFeature,
            Execute,
            PostExecute,
            InitFeature,
        }

        [Flags]
        public enum UpscaleFlags
        {
            None = 0,
            HighDynamicRange = 1 << 0,
            DisplayResolutionMotionVectors = 1 << 1,
            MotionVectorsJitterCancellation = 1 << 2,
            DepthInverted = 1 << 3,
            DepthInfinite = 1 << 4,
            AutoExposure = 1 << 5,
            DynamicResolution = 1 << 6,
            Texture1DUsage = 1 << 7,
            DebugChecking = 1 << 8,
        }

        private enum TextureSlot
        {
            ColorInput,
            ColorOutput,
            Depth,
            MotionVectors,
            TransparencyMask,
            ExposureTexture,
            ReactiveMask,
            BiasColorMask,
        }

        [Serializable, StructLayout(LayoutKind.Sequential)]
        public struct InitializationData
        {
            public uint maxRenderSizeWidth;
            public uint maxRenderSizeHeight;
            public uint displaySizeWidth;
            public uint displaySizeHeight;
            public UpscaleFlags flags;

            public uint featureSlot;
        }

        [Serializable, StructLayout(LayoutKind.Sequential)]
        public struct ExecutionData
        {
            public Vector2 jitterOffset;
            public Vector2 mvScale;
            public uint renderSizeWidth;
            public uint renderSizeHeight;
            public int enableSharpening;
            public float sharpness;
            public float frameTimeDelta;
            public float preExposure;
            public int reset;
            public float cameraNear;
            public float cameraFar;
            public float cameraFovAngleVertical;
            
            public uint featureSlot;
        }

        public struct TextureTable
        {
            public Texture inputColor;
            public Texture inputDepth;
            public Texture inputMotionVectors;
            public Texture inputExposure;
            public Texture inputReactiveMask;
            public Texture inputTransparencyMask;
            public Texture outputColor;
        }
    }
}
