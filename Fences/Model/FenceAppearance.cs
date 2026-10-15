using System;
using System.Drawing;

namespace NoFences.Model
{
    public sealed class FenceAppearance
    {
        public string TitleFontFamily { get; set; } = "Segoe UI";

        public float TitleFontSize { get; set; } = 17f;

        public int TitleTextColorArgb { get; set; } = Color.White.ToArgb();

        public int TitleBackgroundColorArgb { get; set; } = Color.FromArgb(1, 1, 1).ToArgb();

        public int BodyBackgroundColorArgb { get; set; } = Color.FromArgb(36, 36, 36).ToArgb();

        public int BodyTransparency { get; set; } = 36;

        public int IconSize { get; set; } = 48;

        public int IconSpacing { get; set; } = 15;

        public int CornerRadius { get; set; } = 0;

        public string ItemFontFamily { get; set; } = "Segoe UI";

        public float ItemFontSize { get; set; } = 9f;

        public int ItemTextColorArgb { get; set; } = Color.White.ToArgb();

        public static FenceAppearance CreateDefault()
        {
            return new FenceAppearance();
        }

        public FenceAppearance Clone()
        {
            return new FenceAppearance
            {
                TitleFontFamily = TitleFontFamily,
                TitleFontSize = TitleFontSize,
                TitleTextColorArgb = TitleTextColorArgb,
                TitleBackgroundColorArgb = TitleBackgroundColorArgb,
                BodyBackgroundColorArgb = BodyBackgroundColorArgb,
                BodyTransparency = BodyTransparency,
                IconSize = IconSize,
                IconSpacing = IconSpacing,
                CornerRadius = CornerRadius,
                ItemFontFamily = ItemFontFamily,
                ItemFontSize = ItemFontSize,
                ItemTextColorArgb = ItemTextColorArgb
            };
        }

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(TitleFontFamily))
                TitleFontFamily = "Segoe UI";
            if (string.IsNullOrWhiteSpace(ItemFontFamily))
                ItemFontFamily = "Segoe UI";

            TitleFontSize = Clamp(TitleFontSize, 8f, 36f);
            ItemFontSize = Clamp(ItemFontSize, 7f, 24f);
            BodyTransparency = Math.Max(0, Math.Min(90, BodyTransparency));
            IconSize = Math.Max(24, Math.Min(96, IconSize));
            IconSpacing = Math.Max(0, Math.Min(64, IconSpacing));
            CornerRadius = Math.Max(0, Math.Min(48, CornerRadius));
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
