using System;
using UnityEngine;
using TND.Upscaling.Framework;

namespace TND.Upscaling.FSR2
{
    [Serializable]
    public class FSR2UpscalerSettings: UpscalerSettingsBase
    {
        public Texture transparencyMask;
    }
}
