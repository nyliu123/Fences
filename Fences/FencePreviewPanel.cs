using Microsoft.Win32;
using NoFences.Model;
using NoFences.Win32;
using NoFences.Util;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace NoFences
{
    internal sealed class FencePreviewPanel : Panel
    {
        private sealed class PreviewItem
        {
            public string Name;
            public Bitmap Image;
        }

        private readonly List<PreviewItem> items = new List<PreviewItem>();
        private readonly string title;
        private readonly Size fenceSize;
        private readonly int originalTitleHeight;
        private readonly float originalTitleFontSize;
        private readonly Image wallpaper;
        private readonly Bitmap blurredWallpaper;

        public FenceAppearance Appearance { get; set; }

        public FencePreviewPanel(FenceInfo fence)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(29, 37, 48);
            AccessibleName = "分区外观实时预览";
            title = fence?.Name ?? "常用";
            fenceSize = new Size(Math.Max(180, Math.Min(4096, fence?.Width ?? 300)),
                Math.Max(180, Math.Min(4096, fence?.Height ?? 300)));
            originalTitleHeight = fence?.TitleHeight ?? 35;
            originalTitleFontSize = fence?.Appearance?.TitleFontSize ?? 17f;
            LoadItems(fence);
            wallpaper = LoadWallpaper();
            if (wallpaper != null)
            {
                blurredWallpaper = new Bitmap(Math.Max(1, wallpaper.Width / 24), Math.Max(1, wallpaper.Height / 24));
                using (var graphics = Graphics.FromImage(blurredWallpaper))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(wallpaper, new Rectangle(Point.Empty, blurredWallpaper.Size));
                }
            }
        }

        private void LoadItems(FenceInfo fence)
        {
            if (fence != null)
            {
                foreach (var path in fence.Files)
                {
                    if (items.Count >= 12)
                        break;
                    var entry = FenceEntry.FromPath(path);
                    if (entry == null)
                        continue;
                    Bitmap image = null;
                    try
                    {
                        if (entry.Type == EntryType.Folder)
                            image = IconUtil.FolderLarge.ToBitmap();
                        else if (IsImage(path))
                        {
                            using (var source = Image.FromFile(path))
                            {
                                image = new Bitmap(96, 96);
                                using (var graphics = Graphics.FromImage(image))
                                {
                                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                    DrawImageFit(graphics, source, new Rectangle(0, 0, 96, 96));
                                }
                            }
                        }
                        else
                        {
                            using (var icon = Icon.ExtractAssociatedIcon(path))
                                image = icon?.ToBitmap();
                        }
                    }
                    catch
                    {
                        image?.Dispose();
                        image = null;
                    }
                    items.Add(new PreviewItem
                    {
                        Name = entry.Type == EntryType.File && fence.ShowFileExtensions ? Path.GetFileName(path) : entry.Name,
                        Image = image ?? SystemIcons.Application.ToBitmap()
                    });
                }
            }
            if (items.Count == 0)
            {
                items.Add(new PreviewItem { Name = "文档", Image = IconUtil.FolderLarge.ToBitmap() });
                items.Add(new PreviewItem { Name = "常用应用", Image = SystemIcons.Application.ToBitmap() });
                items.Add(new PreviewItem { Name = "图片", Image = IconUtil.FolderLarge.ToBitmap() });
            }
        }

        private static bool IsImage(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg" ||
                extension == ".bmp" || extension == ".gif" || extension == ".tif" || extension == ".tiff";
        }

        private static Image LoadWallpaper()
        {
            try
            {
                var path = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper", null) as string;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
                if (!File.Exists(path))
                    return null;
                using (var image = Image.FromFile(path))
                    return new Bitmap(image);
            }
            catch
            {
                return null;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(BackColor);
            if (ClientSize.Width < 2 || ClientSize.Height < 2)
                return;

            // Extend a softened wallpaper behind the screenshot instead of stretching
            // the Fence to fill a tall or wide preview card.
            DrawWallpaper(graphics, blurredWallpaper ?? wallpaper, ClientRectangle);
            using (var shade = new SolidBrush(Color.FromArgb(90, BackColor)))
                graphics.FillRectangle(shade, ClientRectangle);

            var settings = Appearance ?? FenceAppearance.CreateDefault();
            var sceneSize = new Size(fenceSize.Width + 48, fenceSize.Height + 48);
            var scale = Math.Min(ClientSize.Width / (float)sceneSize.Width, ClientSize.Height / (float)sceneSize.Height);
            var state = graphics.Save();
            try
            {
                graphics.TranslateTransform((ClientSize.Width - sceneSize.Width * scale) / 2,
                    (ClientSize.Height - sceneSize.Height * scale) / 2);
                graphics.ScaleTransform(scale, scale);
                graphics.SetClip(new Rectangle(Point.Empty, sceneSize));
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                var sceneBounds = new Rectangle(Point.Empty, sceneSize);
                DrawWallpaper(graphics, wallpaper, sceneBounds);
                var fenceBounds = new Rectangle(24, 24, fenceSize.Width, fenceSize.Height);
                using (var outline = WindowGeometry.CreateOutline(fenceBounds, settings.CornerRadius))
                    graphics.SetClip(outline, CombineMode.Intersect);
                var titleHeight = settings.TitleFontSize == originalTitleFontSize ? originalTitleHeight :
                    Math.Max(24, Math.Min(100, (int)Math.Ceiling(settings.TitleFontSize * 2.1f)));
                titleHeight = Math.Max(16, Math.Min(fenceBounds.Height, titleHeight));
                var bodyBounds = new Rectangle(fenceBounds.X, fenceBounds.Y + titleHeight,
                    fenceBounds.Width, fenceBounds.Height - titleHeight);
                var bodyState = graphics.Save();
                graphics.SetClip(bodyBounds, CombineMode.Intersect);
                DrawWallpaper(graphics, blurredWallpaper ?? wallpaper, sceneBounds);
                graphics.Restore(bodyState);
                using (var body = new SolidBrush(Color.FromArgb(
                    (int)Math.Round((100 - settings.BodyTransparency) * 2.55),
                    Color.FromArgb(settings.BodyBackgroundColorArgb))))
                    graphics.FillRectangle(body, bodyBounds);
                var titleBounds = new Rectangle(fenceBounds.X, fenceBounds.Y, fenceBounds.Width, titleHeight);
                using (var background = new SolidBrush(Color.FromArgb(settings.TitleBackgroundColorArgb)))
                    graphics.FillRectangle(background, titleBounds);
                using (var font = CreateFont(settings.TitleFontFamily, settings.TitleFontSize))
                using (var brush = new SolidBrush(Color.FromArgb(settings.TitleTextColorArgb)))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    graphics.DrawString(title, font, brush, titleBounds, format);
                DrawItems(graphics, settings, bodyBounds);
                using (var border = new Pen(Color.FromArgb(72, Color.White)))
                using (var outline = WindowGeometry.CreateOutline(new Rectangle(fenceBounds.X,
                    fenceBounds.Y, fenceBounds.Width - 1, fenceBounds.Height - 1), settings.CornerRadius))
                    graphics.DrawPath(border, outline);
            }
            finally
            {
                graphics.Restore(state);
            }
            base.OnPaint(e);
        }

        private void DrawItems(Graphics graphics, FenceAppearance settings, Rectangle bodyBounds)
        {
            var state = graphics.Save();
            graphics.SetClip(bodyBounds, CombineMode.Intersect);
            using (var font = CreateFont(settings.ItemFontFamily, settings.ItemFontSize))
            using (var text = new SolidBrush(Color.FromArgb(settings.ItemTextColorArgb)))
            using (var shadow = new SolidBrush(Color.FromArgb(180, 15, 15, 15)))
            using (var format = new StringFormat { Alignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.LineLimit, Trimming = StringTrimming.EllipsisCharacter })
            {
                var itemWidth = Math.Max(76, settings.IconSize + 48);
                var textHeight = Math.Max(32, (int)Math.Ceiling(font.GetHeight(graphics) * 2.5f));
                var itemHeight = settings.IconSize + 15 + textHeight;
                var x = bodyBounds.X + 15;
                var y = bodyBounds.Y + 15;
                foreach (var item in items)
                {
                    if (y >= bodyBounds.Bottom)
                        break;
                    DrawImageFit(graphics, item.Image, new Rectangle(x + itemWidth / 2 - settings.IconSize / 2,
                        y, settings.IconSize, settings.IconSize));
                    var textBounds = new RectangleF(x, y + settings.IconSize + 5, itemWidth, textHeight);
                    var shadowBounds = textBounds;
                    shadowBounds.Offset(1.5f, 1.5f);
                    graphics.DrawString(item.Name, font, shadow, shadowBounds, format);
                    graphics.DrawString(item.Name, font, text, textBounds, format);
                    x += itemWidth + settings.IconSpacing;
                    if (x + itemWidth > bodyBounds.Right)
                    {
                        x = bodyBounds.X + 15;
                        y += itemHeight + settings.IconSpacing;
                    }
                }
            }
            graphics.Restore(state);
        }

        private static void DrawWallpaper(Graphics graphics, Image image, Rectangle bounds)
        {
            if (image == null)
            {
                using (var background = new LinearGradientBrush(bounds,
                    Color.FromArgb(63, 85, 110), Color.FromArgb(24, 36, 53), 45f))
                    graphics.FillRectangle(background, bounds);
                return;
            }
            var ratio = Math.Max(bounds.Width / (float)image.Width, bounds.Height / (float)image.Height);
            var width = bounds.Width / ratio;
            var height = bounds.Height / ratio;
            graphics.DrawImage(image, bounds, (image.Width - width) / 2, (image.Height - height) / 2,
                width, height, GraphicsUnit.Pixel);
        }

        private static void DrawImageFit(Graphics graphics, Image image, Rectangle bounds)
        {
            var ratio = Math.Min(bounds.Width / (float)image.Width, bounds.Height / (float)image.Height);
            var width = image.Width * ratio;
            var height = image.Height * ratio;
            graphics.DrawImage(image, bounds.X + (bounds.Width - width) / 2,
                bounds.Y + (bounds.Height - height) / 2, width, height);
        }

        private static Font CreateFont(string family, float size)
        {
            try { return new Font(family, size, FontStyle.Regular, GraphicsUnit.Point); }
            catch { return new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var item in items)
                    item.Image.Dispose();
                wallpaper?.Dispose();
                blurredWallpaper?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
