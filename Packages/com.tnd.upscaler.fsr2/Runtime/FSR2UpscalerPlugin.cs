using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting;
using TND.Upscaling.Framework;

[assembly: AlwaysLinkAssembly]

namespace TND.Upscaling.FSR2
{
    public class FSR2UpscalerPlugin: UpscalerPlugin<FSR2Upscaler, FSR2UpscalerSettings>
    {
        public override UpscalerName Name => UpscalerName.FSR2;
        public override string DisplayName => FSR2Upscaler.DisplayName;
        public override int Priority => (int)UpscalerName.FSR2 + 22;
#if UNITY_STANDALONE_WIN
        public override bool IsSupported => FSR2UnityPlugin.IsSupported();
#else
        public override bool IsSupported => false;
#endif
        public override bool IsTemporalUpscaler => true;
        public override bool SupportsDynamicResolution => true;
        public override bool UsesMachineLearning => false;
        public override bool IncludesAlphaUpscale => false;
        public override bool AcceptsReactiveMask => true;

        protected override bool TryCreateUpscaler(CommandBuffer commandBuffer, FSR2UpscalerSettings settings, in UpscalerInitParams initParams, out FSR2Upscaler upscaler)
        {
            upscaler = new FSR2Upscaler(settings);
            return upscaler.Initialize(commandBuffer, initParams);
        }

        public override void Cleanup()
        {
            FSR2UnityPlugin.ShutdownPlugin();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        private static void RegisterUpscalerPlugin()
        {
            RegisterUpscalerPlugin<FSR2UpscalerPlugin>();
        }
    }
}
