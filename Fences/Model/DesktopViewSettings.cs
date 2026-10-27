using NoFences.Win32;
using System;
using System.Collections.Generic;

namespace NoFences.Model
{
    public sealed class DesktopViewSettings
    {
        public bool AutoArrange { get; set; } = true;
        public bool AlignToGrid { get; set; } = true;
        public bool ShowIcons { get; set; } = true;
        public int IconSize { get; set; } = 48;
        public Guid SortFormat { get; set; } = new Guid("b725f130-47ef-101a-a5f1-02608c9eebac");
        public uint SortProperty { get; set; } = 10;
        public int SortDirection { get; set; } = 1;
        public List<DesktopIconPosition> Positions { get; set; } = new List<DesktopIconPosition>();

        internal DesktopPreferences ToNative()
        {
            var result = DesktopPreferences.Empty;
            result.Flags = (AutoArrange ? 1u : 0u) | (AlignToGrid ? 4u : 0u) | (ShowIcons ? 0u : 0x1000u);
            result.IconSize = Math.Max(16, Math.Min(256, IconSize));
            result.SortFormat = SortFormat;
            result.SortProperty = SortProperty;
            result.SortDirection = SortDirection == -1 ? -1 : 1;
            return result;
        }

        internal static DesktopViewSettings FromNative(DesktopPreferences value) => new DesktopViewSettings
        {
            AutoArrange = (value.Flags & 1) != 0,
            AlignToGrid = (value.Flags & 4) != 0,
            ShowIcons = (value.Flags & 0x1000) == 0,
            IconSize = value.IconSize,
            SortFormat = value.SortFormat,
            SortProperty = value.SortProperty,
            SortDirection = value.SortDirection
        };
    }

    public sealed class DesktopIconPosition
    {
        public string Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    public sealed class DesktopViewSession
    {
        public int OwnerProcessId { get; set; }
        public long OwnerStartTime { get; set; }
        public long OriginalWindow { get; set; }
        public long OriginalParent { get; set; }
        public int ShellProcessId { get; set; }
        public long ShellStartTime { get; set; }
        public bool OriginallyVisible { get; set; }
    }
}
