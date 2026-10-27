using NoFences.Model;
using NoFences.Util;
using NoFences.Win32;
using Peter;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using static NoFences.Win32.WindowUtil;

namespace NoFences
{
    public partial class FenceWindow : Form
    {
        private int logicalTitleHeight;
        private int titleHeight;
        private int itemIconSize;
        private int itemWidth;
        private int itemHeight;
        private int itemSpacing;
        private int textHeight;
        private const int itemPadding = 15;
        private const float shadowDist = 1.5f;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_NCRBUTTONDOWN = 0x00A4;
        private const int WM_MOVING = 0x0216;
        private static readonly Color acrylicBorder = Color.FromArgb(72, 255, 255, 255);

        private readonly FenceInfo fenceInfo;
        private bool initializing = true;

        private Font titleFont;
        private Font iconFont;

        private string selectedItem;
        private string hoveringItem;
        private bool shouldRunDoubleClick;
        private bool hasHoverUpdated;
        private bool isMinified;
        private int prevHeight;
        private Point hoveringItemOrigin;
        private Rectangle hoveringItemIconBounds;
        private Rectangle hoveringItemTextBounds;
        private Point titleMouseDown;
        private Point titleWindowLocation;
        private bool titleDragDetected;
        private bool isTitleClickPending;
        private OpaqueTitleWindow titleLayer;
        private OpaqueItemsWindow itemLayer;
        private ItemEditorWindow itemEditor;
        private string editingItemPath;
        private Point dragStartPoint;
        private string draggedItem;
        private bool isDraggingItem;
        private string activeDragItem;
        private Point dragGhostCursorOffset;
        private DragGhostWindow dragGhost;
        private DateTime dragCompletedAtUtc;

        private int scrollHeight;
        private int scrollOffset;

        private readonly ThrottledExecution throttledMove = new ThrottledExecution(TimeSpan.FromSeconds(4));
        private readonly ThrottledExecution throttledResize = new ThrottledExecution(TimeSpan.FromSeconds(4));

        private readonly ShellContextMenu shellContextMenu = new ShellContextMenu();

        private readonly ThumbnailProvider thumbnailProvider = new ThumbnailProvider();

        private sealed class PendingDesktopImport
        {
            public string OriginalPath { get; }
            public string ImportedPath { get; }

            public PendingDesktopImport(string originalPath, string importedPath)
            {
                OriginalPath = originalPath;
                ImportedPath = importedPath;
            }
        }

        private sealed class DuplicateDesktopImport
        {
            public string OriginalPath { get; }
            public string ImportedPath { get; }

            public DuplicateDesktopImport(string originalPath, string importedPath)
            {
                OriginalPath = originalPath;
                ImportedPath = importedPath;
            }
        }

        private void ReloadFonts()
        {
            var previousTitleFont = titleFont;
            var previousIconFont = iconFont;
            var appearance = GetAppearance();
            titleFont = CreateFont(appearance.TitleFontFamily, appearance.TitleFontSize);
            // WinForms ignores assigning an equal Font, leaving controls referencing
            // the original instance. Keep that instance alive when only colors/radius change.
            if (previousTitleFont != null && previousTitleFont.Equals(titleFont))
            {
                titleFont.Dispose();
                titleFont = previousTitleFont;
                previousTitleFont = null;
            }
            iconFont = CreateFont(appearance.ItemFontFamily, appearance.ItemFontSize);
            itemIconSize = appearance.IconSize;
            itemSpacing = appearance.IconSpacing;
            itemWidth = Math.Max(76, itemIconSize + 48);
            textHeight = Math.Max(32, (int)Math.Ceiling(iconFont.GetHeight() * 2.5f));
            itemHeight = itemIconSize + itemPadding + textHeight;
            // Existing title controls still reference the old font until synchronized.
            SyncTitleLayer();
            previousTitleFont?.Dispose();
            previousIconFont?.Dispose();
        }

        public FenceWindow(FenceInfo fenceInfo)
        {
            InitializeComponent();
            this.fenceInfo = fenceInfo;
            GetAppearance().Normalize();
            appContextMenu.Items.Remove(renameToolStripMenuItem);
            appContextMenu.Items.Remove(newFenceToolStripMenuItem);
            deleteItemToolStripMenuItem.Text = "移回桌面";
            titleSizeToolStripMenuItem.Text = "个性化...";
            Opacity = 1;
            WindowUtil.HideFromAltTab(Handle);
            DesktopWindowHost.Attach(this);
            ApplyWindowMaterial();
            //DesktopUtil.PreventMinimize(Handle);
            logicalTitleHeight = (fenceInfo.TitleHeight < 16 || fenceInfo.TitleHeight > 100) ? 35 : fenceInfo.TitleHeight;
            titleHeight = LogicalToDeviceUnits(logicalTitleHeight);
            
            this.MouseWheel += FenceWindow_MouseWheel;
            this.MouseUp += (sender, e) => { if (e.Button == MouseButtons.Left) StopScrollbarDrag(); };
            this.MouseCaptureChanged += (sender, e) => { if (!Capture) StopScrollbarDrag(); };
            this.DragLeave += FenceWindow_DragLeave;
            InitializeItemReordering();
            InitializeAdministratorMenu();
            InitializeShortcutLocationMenu();
            InitializeItemDeleteMenu();
            this.GiveFeedback += FenceWindow_GiveFeedback;
            this.QueryContinueDrag += TrackOriginalDrag;
            thumbnailProvider.IconThumbnailLoaded += ThumbnailProvider_IconThumbnailLoaded;

            ReloadFonts();

            AllowDrop = true;

            Text = fenceInfo.Name;
            var savedBounds = new Rectangle(fenceInfo.PosX, fenceInfo.PosY, fenceInfo.Width, fenceInfo.Height);
            Bounds = DesktopWindowHost.FromScreen(WindowGeometry.FitToWorkingArea(
                savedBounds, Screen.FromRectangle(savedBounds).WorkingArea));

            prevHeight = Height;
            lockedToolStripMenuItem.Checked = fenceInfo.Locked;
            minifyToolStripMenuItem.Checked = fenceInfo.CanMinify;
            showFileExtensionsToolStripMenuItem.Checked = fenceInfo.ShowFileExtensions;
            Minify();
            initializing = false;
            UpdateWindowRegion();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        }

        private void UpdateWindowRegion()
        {
            if (fenceInfo == null || ClientSize.Width < 1 || ClientSize.Height < 1)
                return;
            var oldRegion = Region;
            using (var outline = WindowGeometry.CreateOutline(ClientRectangle, GetAppearance().CornerRadius))
                Region = new Region(outline);
            oldRegion?.Dispose();
        }

        internal void EnsureOnScreen()
        {
            if (IsDisposed || Disposing || initializing)
                return;
            var screenBounds = RectangleToScreen(ClientRectangle);
            var fitted = WindowGeometry.FitToWorkingArea(screenBounds, Screen.FromRectangle(screenBounds).WorkingArea);
            Bounds = DesktopWindowHost.FromScreen(fitted);
            if (isMinified)
                prevHeight = Math.Min(prevHeight, Screen.FromRectangle(fitted).WorkingArea.Height);
            Refresh();
        }

        private void DisplaySettingsChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated || IsDisposed || Disposing)
                return;
            try
            {
                BeginInvoke((Action)(() => EnsureOnScreen()));
            }
            catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated)
            {
                // The window closed while the display notification was being queued.
            }
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            EnsureOnScreen();
        }

        protected override void WndProc(ref Message m)
        {
            //Console.WriteLine(m.Msg.ToString("X4"));

            if (m.Msg == 0x007B)
            {
                var screenPoint = m.LParam == new IntPtr(-1) ? MousePosition : new Point(unchecked((int)m.LParam.ToInt64()));
                ShowFenceContextMenu(PointToClient(screenPoint));
                return;
            }

            if (m.Msg == WM_RBUTTONDOWN || m.Msg == WM_NCRBUTTONDOWN)
                ConfirmTitleEditBeforeContextMenu();

            if (m.Msg == WM_MOVING && isTitleClickPending)
                titleDragDetected = true;

            var isTitleMouseDownMessage = m.Msg == WM_NCLBUTTONDOWN && m.WParam.ToInt32() == HTCAPTION;
            if (isTitleMouseDownMessage)
            {
                titleMouseDown = MousePosition;
                titleWindowLocation = Location;
                titleDragDetected = false;
                isTitleClickPending = true;
            }

            // Remove border
            if (m.Msg == 0x0083)
            {
                m.Result = IntPtr.Zero;
                return;
            }

            // Mouse leave
            var myrect = RectangleToScreen(ClientRectangle);
            if (m.Msg == 0x02a2 && !myrect.IntersectsWith(new Rectangle(MousePosition, new Size(1, 1))))
            {
                FenceWindow_MouseLeave(this, EventArgs.Empty);
            }

            // Prevent maximize
            if ((m.Msg == WM_SYSCOMMAND) && m.WParam.ToInt32() == 0xF032)
            {
                m.Result = IntPtr.Zero;
                return;
            }

            // Other messages
            base.WndProc(ref m);

            if (isTitleMouseDownMessage && isTitleClickPending)
            {
                isTitleClickPending = false;
                var dragSize = SystemInformation.DragSize;
                if (!titleDragDetected && Location == titleWindowLocation &&
                    Math.Abs(MousePosition.X - titleMouseDown.X) <= dragSize.Width / 2 &&
                    Math.Abs(MousePosition.Y - titleMouseDown.Y) <= dragSize.Height / 2)
                    BeginTitleEdit();
            }

            if (m.Msg == WM_NCHITTEST && ClientRectangle.Contains(PointToClient(new Point(unchecked((int)m.LParam.ToInt64())))))
                FenceWindow_MouseEnter(this, EventArgs.Empty);

            // If not locked and using the left mouse button
            if (MouseButtons == MouseButtons.Right || lockedToolStripMenuItem.Checked)
                return;

            // Then, allow dragging and resizing
            if (m.Msg == WM_NCHITTEST)
            {

                var pt = PointToClient(new Point(unchecked((int)m.LParam.ToInt64())));
                if (IsScrollbarPoint(pt)) { m.Result = new IntPtr(HTCLIENT); return; }

                if ((int)m.Result == HTCLIENT && pt.Y < titleHeight)     // drag the form
                {
                    m.Result = (IntPtr)HTCAPTION;
                }

                if (pt.X < 10 && pt.Y < 10)
                    m.Result = new IntPtr(HTTOPLEFT);
                else if (pt.X > (Width - 10) && pt.Y < 10)
                    m.Result = new IntPtr(HTTOPRIGHT);
                else if (pt.X < 10 && pt.Y > (Height - 10))
                    m.Result = new IntPtr(HTBOTTOMLEFT);
                else if (pt.X > (Width - 10) && pt.Y > (Height - 10))
                    m.Result = new IntPtr(HTBOTTOMRIGHT);
                else if (pt.Y > (Height - 10))
                    m.Result = new IntPtr(HTBOTTOM);
                else if (pt.X < 10)
                    m.Result = new IntPtr(HTLEFT);
                else if (pt.X > (Width - 10))
                    m.Result = new IntPtr(HTRIGHT);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= 0x00080000; // WS_EX_LAYERED
                return parameters;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The layered bitmap supplies the translucent background.
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible || Disposing || IsDisposed || iconFont == null)
                return;
            EnsureItemLayer();
            EnsureTitleLayer();
            RefreshBackgroundLayer();
        }

        private void EnsureItemLayer()
        {
            if (itemLayer != null || IsDisposed)
                return;

            itemLayer = new OpaqueItemsWindow(DrawOpaqueItems);
            DesktopWindowHost.Attach(itemLayer, Handle);
            Controls.Add(itemLayer);
            SyncItemLayer();
        }

        private void SyncItemLayer()
        {
            if (itemLayer == null || itemLayer.IsDisposed)
                return;

            if (isMinified)
            {
                if (itemLayer.Visible)
                    itemLayer.Hide();
                return;
            }

            itemLayer.Bounds = ClientRectangle;
            if (!itemLayer.Visible)
                itemLayer.Show();
            itemLayer.RefreshItems();
        }

        private void EnsureTitleLayer()
        {
            if (titleLayer != null || IsDisposed)
                return;

            var appearance = GetAppearance();
            titleLayer = new OpaqueTitleWindow(
                Text,
                titleFont,
                Color.FromArgb(appearance.TitleTextColorArgb),
                Color.FromArgb(appearance.TitleBackgroundColorArgb));
            titleLayer.CommitRequested += TitleEditor_CommitRequested;
            titleLayer.CancelRequested += TitleEditor_CancelRequested;
            titleLayer.ContextMenuRequested += TitleLayer_ContextMenuRequested;
            DesktopWindowHost.Attach(titleLayer, Handle);
            Controls.Add(titleLayer);
            SyncTitleLayer();
            titleLayer.Show();
            titleLayer.BringToFront();
        }

        private void SyncTitleLayer()
        {
            if (titleLayer == null || titleLayer.IsDisposed)
                return;

            titleLayer.Bounds = new Rectangle(0, 0, Width, titleHeight);
            var appearance = GetAppearance();
            titleLayer.UpdateTitle(
                Text,
                titleFont,
                Color.FromArgb(appearance.TitleTextColorArgb),
                Color.FromArgb(appearance.TitleBackgroundColorArgb));
        }

        private void ApplyWindowMaterial()
        {
            var appearance = GetAppearance();
            var bodyColor = Color.FromArgb(appearance.BodyBackgroundColorArgb);
            BackColor = bodyColor;
            Opacity = 1;
            RefreshBackgroundLayer();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "确定要移除这个桌面分区吗？其中的项目会移回桌面。", "移除桌面分区", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try
                {
                    RemoveFenceAndClose();
                }
                catch (Exception exception)
                {
                    MessageBox.Show(exception.Message, "无法移除桌面分区", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        internal void RemoveFenceAndClose()
        {
            Close();
            if (!IsDisposed)
                return;
            FenceShutdown.WaitForWindowUpdates();
            try
            {
                FenceManager.Instance.RemoveFence(fenceInfo);
            }
            catch
            {
                // Successfully released items stay on the desktop; reopen only the
                // remaining membership so a failed removal can be retried safely.
                new FenceWindow(fenceInfo).Show();
                throw;
            }
        }

        private void deleteItemToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                FenceManager.Instance.ReleaseItem(fenceInfo, contextMenuItem);
                hoveringItem = null;
                Save();
                Refresh();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "无法将项目移回桌面", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void contextMenuStrip1_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ConfirmTitleEditBeforeContextMenu();
            UpdateItemReorderMenu();
            runAsAdministratorMenuItem.Visible = ExecutableLauncher.CanRunAsAdministrator(contextMenuItem);
            UpdateShortcutLocationMenu();

            var onItem = contextMenuItem != null;
            deleteItemToolStripMenuItem.Visible = onItem;
            recycleItemMenuItem.Visible = onItem;
            recycleItemSeparator.Visible = onItem;
            showFileExtensionsToolStripMenuItem.Visible = !onItem;
            lockedToolStripMenuItem.Visible = !onItem;
            minifyToolStripMenuItem.Visible = !onItem;
            titleSizeToolStripMenuItem.Visible = !onItem;
            exitToolStripMenuItem.Visible = !onItem;
            toolStripSeparator1.Visible = !onItem;
        }

        private void FenceWindow_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.None;
            if (lockedToolStripMenuItem.Checked)
                return;

            if (IsLocalItemDrag(e.Data))
            {
                UpdateItemInsertionTarget(e);
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = ImportEffect(e.AllowedEffect);
            }
        }

        private void FenceWindow_DragLeave(object sender, EventArgs e)
        {
            ClearItemInsertionTarget();
        }

        private void FenceWindow_DragDrop(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.None;
            if (lockedToolStripMenuItem.Checked || (ImportEffect(e.AllowedEffect) == DragDropEffects.None && !IsLocalItemDrag(e.Data)))
                return;
            if (IsLocalItemDrag(e.Data))
            {
                CommitItemReorder(e);
                return;
            }
            var dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (dropped == null)
                return;
            if (FenceManager.Instance.UsesOriginalPaths(fenceInfo))
            {
                DropOriginalItems(e, dropped);
                return;
            }
            var imports = new List<PendingDesktopImport>();
            var addedPaths = new List<string>();
            var duplicateImports = new List<DuplicateDesktopImport>();
            var importSucceeded = false;
            try
            {
                foreach (var file in dropped)
                {
                    var importedPath = GetImportedPath(imports, file);
                    if (importedPath == null)
                    {
                        if (!ItemExists(file))
                            continue;

                        importedPath = FenceManager.Instance.ImportItem(fenceInfo, file);
                        if (!PathsEqual(file, importedPath))
                            imports.Add(new PendingDesktopImport(file, importedPath));
                    }

                    var equivalentPath = FenceManager.Instance.FindEquivalentImportedItem(fenceInfo, file, importedPath);
                    if (equivalentPath != null)
                    {
                        duplicateImports.Add(new DuplicateDesktopImport(file, importedPath));
                        continue;
                    }

                    if (ContainsPath(fenceInfo.Files, importedPath))
                        continue;

                    fenceInfo.Files.Add(importedPath);
                    addedPaths.Add(importedPath);
                }

                Save();
                e.Effect = DragDropEffects.Move;
                importSucceeded = true;
            }
            catch (Exception exception)
            {
                foreach (var addedPath in addedPaths)
                    fenceInfo.Files.Remove(addedPath);
                var displayedException = exception;
                try
                {
                    RollbackDesktopImports(imports);
                }
                catch (Exception rollbackException)
                {
                    displayedException = new AggregateException("导入失败，并且无法完全恢复原状态。", exception, rollbackException);
                }
                e.Effect = DragDropEffects.None;
                ShowDragDropError(displayedException.Message, "无法添加项目");
            }

            if (importSucceeded)
            {
                foreach (var duplicateImport in duplicateImports)
                {
                    try
                    {
                        FenceManager.Instance.CompleteDuplicateImport(duplicateImport.ImportedPath);
                    }
                    catch (Exception exception)
                    {
                        try
                        {
                            FenceManager.Instance.CancelImport(duplicateImport.OriginalPath, duplicateImport.ImportedPath);
                        }
                        catch (Exception rollbackException)
                        {
                            exception = new AggregateException("重复项目清理失败，并且无法恢复到桌面。", exception, rollbackException);
                        }

                        ShowDragDropError(exception.Message, "无法清理重复项目");
                    }
                }
            }
            DesktopUtil.FlushDesktopChanges(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            Refresh();
        }

        private void RollbackDesktopImports(IList<PendingDesktopImport> imports)
        {
            Exception firstError = null;
            for (var index = imports.Count - 1; index >= 0; index--)
            {
                try
                {
                    FenceManager.Instance.CancelImport(imports[index].OriginalPath, imports[index].ImportedPath);
                }
                catch (Exception exception)
                {
                    if (firstError == null)
                        firstError = exception;
                }
            }

            if (firstError != null)
                throw new IOException("一个或多个桌面项目无法恢复。", firstError);
        }

        private string GetImportedPath(IList<PendingDesktopImport> imports, string originalPath)
        {
            foreach (var import in imports)
                if (PathsEqual(import.OriginalPath, originalPath))
                    return import.ImportedPath;

            return null;
        }

        private bool ContainsPath(IList<string> paths, string itemPath)
        {
            foreach (var path in paths)
                if (PathsEqual(path, itemPath))
                    return true;

            return false;
        }

        private bool PathsEqual(string first, string second)
        {
            return string.Equals(
                Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private void FenceWindow_Resize(object sender, EventArgs e)
        {
            if (initializing || IsDisposed || Disposing)
                return;
            UpdateWindowRegion();
            RefreshBackgroundLayer();
            SyncItemLayer();
            SyncTitleLayer();

            throttledResize.Run(() =>
            {
                if (IsDisposed || Disposing)
                    return;
                fenceInfo.Width = Width;
                fenceInfo.Height = isMinified ? prevHeight : Height;
                Save();
            });

            Refresh();
        }

        private void FenceWindow_MouseMove(object sender, MouseEventArgs e)
        {
            if (ScrollbarMouseMove(e)) return;
            if (e.Button == MouseButtons.Left && !isDraggingItem && !string.IsNullOrEmpty(draggedItem))
            {
                var dragSize = SystemInformation.DragSize;
                var dragBounds = new Rectangle(
                    dragStartPoint.X - dragSize.Width / 2,
                    dragStartPoint.Y - dragSize.Height / 2,
                    dragSize.Width,
                    dragSize.Height);
                if (!dragBounds.Contains(e.Location))
                {
                    var itemPath = draggedItem;
                    if (!ItemExists(itemPath))
                        return;

                    try
                    {
                        isDraggingItem = true;
                        activeDragItem = itemPath;
                        var entry = FenceEntry.FromPath(itemPath);
                        if (entry != null)
                        {
                            dragGhost = new DragGhostWindow(CreateDragGhostBitmap(entry), dragGhostCursorOffset);
                            dragGhost.FollowCursor(MousePosition);
                            dragGhost.Show();
                        }
                        Refresh();
                        var data = new DataObject();
                        data.SetData(DataFormats.FileDrop, true, new[] { itemPath });
                        originalDragWasDropped = originalDragWasCancelled = false;
                        originalDragModifiers = Keys.None;
                        var effect = DoDragDrop(data, FenceManager.Instance.UsesOriginalPaths(fenceInfo)
                            ? DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link : DragDropEffects.Move);
                        // Hit-test the real destination, not our top-level drag image.
                        dragGhost?.Hide();
                        if (ItemExists(itemPath)) CompleteOriginalDesktopDrop(itemPath, effect);

                        if (!ItemExists(itemPath))
                        {
                            FenceManager.Instance.CompleteExternalMove(fenceInfo, itemPath);
                            selectedItem = null;
                            hoveringItem = null;
                        }
                    }
                    catch (Exception exception)
                    {
                        MessageBox.Show(this, exception.Message, "无法移动项目", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    finally
                    {
                        if (dragGhost != null)
                        {
                            dragGhost.Close();
                            dragGhost.Dispose();
                            dragGhost = null;
                        }
                        draggedItem = null;
                        activeDragItem = null;
                        isDraggingItem = false;
                        ClearItemInsertionTarget();
                        dragCompletedAtUtc = DateTime.UtcNow;
                        Refresh();
                    }
                    return;
                }
            }

            Refresh();
        }

        private void FenceWindow_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            if (itemEditor != null)
                CommitItemRename();
            if (titleLayer != null && titleLayer.IsEditing)
                CommitTitleEdit();

            if (ScrollbarMouseDown(e)) return;

            draggedItem = hoveringItem;
            dragStartPoint = e.Location;
            dragGhostCursorOffset = new Point(
                e.X - hoveringItemOrigin.X + 8,
                e.Y - hoveringItemOrigin.Y + 4);
        }

        private void FenceWindow_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            if (dragGhost != null)
                dragGhost.FollowCursor(MousePosition);
            UpdateOriginalDragFeedback(e, MousePosition, ModifierKeys);
        }

        private void FenceWindow_MouseEnter(object sender, EventArgs e)
        {
            if (minifyToolStripMenuItem.Checked && isMinified)
            {
                isMinified = false;
                Height = prevHeight;
                EnsureOnScreen();
                SyncItemLayer();
            }
        }

        private void FenceWindow_MouseLeave(object sender, EventArgs e)
        {
            var clientPoint = PointToClient(MousePosition);
            if (hoveringItem != null)
                ClearItemHover();
            if (ClientRectangle.Contains(clientPoint) || appContextMenu.Visible || isDraggingItem ||
                itemEditor != null || (titleLayer != null && titleLayer.IsEditing))
                return;

            Minify();
            Refresh();
        }

        private void Minify()
        {
            if (minifyToolStripMenuItem.Checked && !isMinified)
            {
                isMinified = true;
                prevHeight = Height;
                Height = titleHeight;
                SyncItemLayer();
                Refresh();
            }
        }

        private void minifyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetCanMinify(minifyToolStripMenuItem.Checked);
        }

        private void showFileExtensionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetShowFileExtensions(showFileExtensionsToolStripMenuItem.Checked);
        }

        private void FenceWindow_DoubleClick(object sender, EventArgs e)
        {
            shouldRunDoubleClick = true;
            Refresh();
        }

        private void FenceWindow_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clip = new Region(ClientRectangle);
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using (var borderPen = new Pen(acrylicBorder))
                e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

            // Items
            var x = itemPadding;
            var y = itemPadding;
            FenceEntry selectedEntry = null;
            var selectedEntryLocation = Point.Empty;
            var entries = UpdateScrollLayout();
            e.Graphics.Clip = new Region(new Rectangle(0, titleHeight, Width, Height - titleHeight));
            foreach (var entry in entries)
            {

                var entryLocation = new Point(x, y + titleHeight - scrollOffset);
                var wasSelected = string.Equals(selectedItem, entry.Path, StringComparison.OrdinalIgnoreCase);
                if (!wasSelected)
                    RenderEntry(e.Graphics, entry, entryLocation.X, entryLocation.Y);

                if (wasSelected || string.Equals(selectedItem, entry.Path, StringComparison.OrdinalIgnoreCase))
                {
                    selectedEntry = entry;
                    selectedEntryLocation = entryLocation;
                }

                x += itemWidth + itemSpacing;
                if (x + itemWidth > itemLayoutWidth)
                {
                    x = itemPadding;
                    y += itemHeight + itemSpacing;
                }
            }

            if (selectedEntry != null)
                RenderEntry(e.Graphics, selectedEntry, selectedEntryLocation.X, selectedEntryLocation.Y);

            DrawScrollbar(e.Graphics);



            // Click handlers
            if (!hasHoverUpdated)
                hoveringItem = null;

            shouldRunDoubleClick = false;
            hasHoverUpdated = false;
            RefreshBackgroundLayer();
            itemLayer?.RefreshItems();
        }

        private void RenderEntry(Graphics g, FenceEntry entry, int x, int y)
        {
            var mousePos = PointToClient(MousePosition);
            var baseItemRect = new Rectangle(x, y, itemWidth, itemHeight);
            var mouseOver = !draggingScrollThumb && !IsScrollbarPoint(mousePos) && ClientRectangle.Contains(mousePos) && mousePos.Y >= titleHeight && baseItemRect.Contains(mousePos);

            if (mouseOver)
            {
                hoveringItem = entry.Path;
                hoveringItemOrigin = new Point(x, y);
                hoveringItemIconBounds = new Rectangle(
                    x + itemWidth / 2 - itemIconSize / 2,
                    y,
                    itemIconSize,
                    itemIconSize);
                hasHoverUpdated = true;
            }

            if (mouseOver && shouldRunDoubleClick)
            {
                shouldRunDoubleClick = false;
                selectedItem = null;
                entry.Open();
            }

            var showFullText = string.Equals(selectedItem, entry.Path, StringComparison.OrdinalIgnoreCase);
            using (var stringFormat = new StringFormat { Alignment = StringAlignment.Center })
            {
                if (!showFullText)
                {
                    stringFormat.FormatFlags = StringFormatFlags.LineLimit;
                    stringFormat.Trimming = StringTrimming.EllipsisCharacter;
                }

                var textPosition = new PointF(x, y + itemIconSize + 5);
                var visibleTextHeight = MeasureItemVisibleTextHeight(g, entry, showFullText, stringFormat);
                var textMaxSize = new SizeF(itemWidth, visibleTextHeight);
                if (mouseOver)
                    hoveringItemTextBounds = Rectangle.Round(new RectangleF(textPosition, textMaxSize));
                var outlineRect = new Rectangle(x - 2, y - 2, itemWidth + 2, itemIconSize + visibleTextHeight + 7);
                var outlineRectInner = outlineRect.Shrink(1);

                var editing = string.Equals(editingItemPath, entry.Path, StringComparison.OrdinalIgnoreCase);
                if (showFullText && !editing)
                {
                    using (var borderPen = new Pen(Color.FromArgb(120, SystemColors.ActiveBorder)))
                    using (var selectionBrush = new SolidBrush(Color.FromArgb(
                        mouseOver ? 100 : 80,
                        mouseOver ? SystemColors.GradientActiveCaption : SystemColors.GradientInactiveCaption)))
                    {
                        g.DrawRectangle(borderPen, outlineRectInner);
                        g.FillRectangle(selectionBrush, outlineRect);
                    }
                }
                else if (mouseOver && !editing)
                {
                    using (var borderPen = new Pen(Color.FromArgb(120, SystemColors.ActiveBorder)))
                    using (var hoverBrush = new SolidBrush(Color.FromArgb(80, SystemColors.ActiveCaption)))
                    {
                        g.DrawRectangle(borderPen, outlineRectInner);
                        g.FillRectangle(hoverBrush, outlineRect);
                    }
                }

            }
        }

        private void DrawOpaqueItems(Graphics graphics)
        {
            graphics.SetClip(new Rectangle(0, titleHeight, Width, Math.Max(0, Height - titleHeight)));
            var x = itemPadding;
            var y = itemPadding;
            FenceEntry selectedEntry = null;
            var selectedLocation = Point.Empty;
            foreach (var file in fenceInfo.Files)
            {
                var entry = FenceEntry.FromPath(file);
                if (entry == null)
                    continue;

                var location = new Point(x, y + titleHeight - scrollOffset);
                var selected = string.Equals(selectedItem, entry.Path, StringComparison.OrdinalIgnoreCase);
                var dragging = string.Equals(activeDragItem, entry.Path, StringComparison.OrdinalIgnoreCase);
                if (selected)
                {
                    selectedEntry = entry;
                    selectedLocation = location;
                }
                else if (!dragging)
                {
                    DrawEntryContent(
                        graphics,
                        entry,
                        location.X,
                        location.Y,
                        false,
                        !string.Equals(editingItemPath, entry.Path, StringComparison.OrdinalIgnoreCase));
                }

                x += itemWidth + itemSpacing;
                if (x + itemWidth > itemLayoutWidth)
                {
                    x = itemPadding;
                    y += itemHeight + itemSpacing;
                }
            }

            if (selectedEntry != null &&
                !string.Equals(activeDragItem, selectedEntry.Path, StringComparison.OrdinalIgnoreCase))
                DrawEntryContent(graphics, selectedEntry, selectedLocation.X, selectedLocation.Y, true);
            DrawItemInsertionMarker(graphics);
        }

        private void DrawEntryContent(
            Graphics graphics,
            FenceEntry entry,
            int x,
            int y,
            bool showFullText,
            bool drawText = true)
        {
            var name = GetDisplayName(entry);
            using (var format = new StringFormat { Alignment = StringAlignment.Center })
            {
                if (!showFullText)
                {
                    format.FormatFlags = StringFormatFlags.LineLimit;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                }

                var textPosition = new PointF(x, y + itemIconSize + 5);
                var measuredText = graphics.MeasureString(
                    name,
                    iconFont,
                    new SizeF(itemWidth, showFullText ? 10000f : textHeight),
                    format);
                var visibleTextHeight = showFullText
                    ? Math.Max(textHeight, (int)Math.Ceiling(measuredText.Height))
                    : Math.Min(textHeight, (int)Math.Ceiling(measuredText.Height));
                var textSize = new SizeF(itemWidth, visibleTextHeight);
                var icon = entry.ExtractIcon(thumbnailProvider);
                graphics.DrawIcon(icon, new Rectangle(
                    x + itemWidth / 2 - itemIconSize / 2,
                    y,
                    itemIconSize,
                    itemIconSize));
                if (!drawText)
                    return;
                using (var shadowBrush = new SolidBrush(Color.FromArgb(180, 15, 15, 15)))
                    graphics.DrawString(name, iconFont, shadowBrush, new RectangleF(textPosition.Move(shadowDist, shadowDist), textSize), format);
                using (var textBrush = new SolidBrush(Color.FromArgb(GetAppearance().ItemTextColorArgb)))
                    graphics.DrawString(name, iconFont, textBrush, new RectangleF(textPosition, textSize), format);
            }
        }

        private Bitmap CreateDragGhostBitmap(FenceEntry entry)
        {
            var bitmap = new Bitmap(itemWidth + 16, itemHeight + 8, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                DrawEntryContent(graphics, entry, 8, 4, false);
            }
            return bitmap;
        }

        private void renameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            BeginItemRename();
        }

        private void BeginItemRename()
        {
            var itemPath = hoveringItem;
            if (string.IsNullOrEmpty(itemPath) || !ItemExists(itemPath))
                return;

            if (itemEditor != null)
                CommitItemRename();

            editingItemPath = itemPath;
            selectedItem = null;
            Refresh();

            var oldName = Path.GetFileName(itemPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var editorWidth = itemWidth;
            var editorBounds = new Rectangle(
                hoveringItemOrigin.X + (itemWidth - editorWidth) / 2,
                hoveringItemOrigin.Y + itemIconSize + 5,
                editorWidth,
                1);
            var editor = new ItemEditorWindow(
                oldName,
                File.Exists(itemPath),
                iconFont,
                RectangleToScreen(editorBounds), true);
            itemEditor = editor;
            editor.CommitRequested += ItemEditor_CommitRequested;
            editor.CancelRequested += ItemEditor_CancelRequested;
            editor.Show();
        }

        private void ItemEditor_CommitRequested(object sender, EventArgs e)
        {
            CommitItemRename();
        }

        private void ItemEditor_CancelRequested(object sender, EventArgs e)
        {
            CancelItemRename();
        }

        private void CommitItemRename()
        {
            if (itemEditor == null)
                return;

            var itemPath = editingItemPath;
            var newName = itemEditor.EditedName;
            CloseItemEditor(false);
            try
            {
                FenceManager.Instance.RenameItem(fenceInfo, itemPath, newName);
                selectedItem = null;
                hoveringItem = null;
            }
            catch (Exception exception)
            {
                Refresh();
                MessageBox.Show(this, exception.Message, "无法重命名项目", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Refresh(); }
        }

        private void CancelItemRename()
        {
            if (itemEditor != null)
                CloseItemEditor();
        }

        private void CloseItemEditor(bool refresh = true)
        {
            var editor = itemEditor;
            itemEditor = null;
            hoveringItem = null;
            editingItemPath = null;
            editor.CommitRequested -= ItemEditor_CommitRequested;
            editor.CancelRequested -= ItemEditor_CancelRequested;
            editor.Close();
            editor.Dispose();
            if (refresh) Refresh();
        }

        private void BeginTitleEdit()
        {
            if (IsDisposed || MouseButtons == MouseButtons.Right || appContextMenu.Visible)
                return;

            EnsureTitleLayer();
            titleLayer.BeginEdit();
        }

        private void CancelPendingTitleEdit()
        {
            isTitleClickPending = false;
        }

        private void ConfirmTitleEditBeforeContextMenu()
        {
            if (titleLayer != null && titleLayer.IsEditing)
                CommitTitleEdit();

            if (titleLayer != null)
                titleLayer.EndEdit(Text);

            CancelPendingTitleEdit();
        }

        private void TitleEditor_CommitRequested(object sender, EventArgs e)
        {
            CommitTitleEdit();
        }

        private void TitleEditor_CancelRequested(object sender, EventArgs e)
        {
            CancelTitleEdit();
        }

        private void CommitTitleEdit()
        {
            CancelPendingTitleEdit();
            if (titleLayer == null || !titleLayer.IsEditing)
                return;

            var newTitle = titleLayer.EditedTitle.Trim();
            if (newTitle.Length == 0 || newTitle == Text)
            {
                titleLayer.EndEdit(Text);
                return;
            }

            Text = newTitle;
            fenceInfo.Name = newTitle;
            titleLayer.EndEdit(newTitle);
            SyncTitleLayer();
            Refresh();
            Save();
        }

        private void CancelTitleEdit()
        {
            CancelPendingTitleEdit();
            if (titleLayer != null && titleLayer.IsEditing)
                titleLayer.EndEdit(Text);
        }

        private void FenceWindow_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (itemEditor != null)
                CancelItemRename();

            if (titleLayer != null)
            {
                titleLayer.CommitRequested -= TitleEditor_CommitRequested;
                titleLayer.CancelRequested -= TitleEditor_CancelRequested;
                titleLayer.ContextMenuRequested -= TitleLayer_ContextMenuRequested;
                titleLayer.Close();
                titleLayer.Dispose();
                titleLayer = null;
            }

            if (itemLayer != null)
            {
                itemLayer.Close();
                itemLayer.Dispose();
                itemLayer = null;
            }

            if (dragGhost != null)
            {
                dragGhost.Close();
                dragGhost.Dispose();
                dragGhost = null;
            }

            titleFont?.Dispose();
            titleFont = null;
            iconFont?.Dispose();
            iconFont = null;

        }

        private readonly object saveLock = new object();
        private void Save()
        {
            lock (saveLock)
            {
                FenceManager.Instance.UpdateFence(fenceInfo);
            }
        }

        private void FenceWindow_LocationChanged(object sender, EventArgs e)
        {
            if (initializing || IsDisposed || Disposing)
                return;
            if (isTitleClickPending && Location != titleWindowLocation)
                titleDragDetected = true;

            SyncTitleLayer();
            SyncItemLayer();
            throttledMove.Run(() =>
            {
                if (IsDisposed || Disposing)
                    return;
                var screenLocation = PointToScreen(Point.Empty);
                fenceInfo.PosX = screenLocation.X;
                fenceInfo.PosY = screenLocation.Y;
                Save();
            });
        }

        private void lockedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetLocked(lockedToolStripMenuItem.Checked);
        }

        private void FenceWindow_Load(object sender, EventArgs e)
        {
            ApplyWindowMaterial();
        }

        private void titleSizeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var dialog = CreatePersonalizationDialog(false))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    ApplyPersonalization(dialog.Appearance);
            }
        }

        internal bool IsLocked => fenceInfo.Locked;

        internal FenceInfo Info => fenceInfo;

        internal void PrepareDesktopPathChange()
        {
            if (itemEditor != null)
                CancelItemRename();
            selectedItem = hoveringItem = draggedItem = null;
        }

        internal bool CanMinify => fenceInfo.CanMinify;

        internal bool ShowFileExtensions => fenceInfo.ShowFileExtensions;

        internal int TitleHeight => fenceInfo.TitleHeight;

        internal FenceAppearance Appearance => GetAppearance().Clone();

        internal PersonalizationDialog CreatePersonalizationDialog(bool batchMode)
        {
            return new PersonalizationDialog(GetAppearance(), batchMode, fenceInfo);
        }

        internal void ClearItemSelectionIfOutside(Point screenPoint)
        {
            if (selectedItem == null || RectangleToScreen(ClientRectangle).Contains(screenPoint))
                return;

            selectedItem = null;
            Refresh();
        }

        internal void CommitRenameIfOutside(Point screenPoint)
        {
            if (IsDisposed || RectangleToScreen(ClientRectangle).Contains(screenPoint))
                return;

            var commitTitle = titleLayer != null && titleLayer.IsEditing &&
                !titleLayer.EditorContains(screenPoint);
            var commitItem = itemEditor != null && !itemEditor.Bounds.Contains(screenPoint);
            if (!commitTitle && !commitItem)
                return;

            BeginInvoke((Action)(() =>
            {
                if (IsDisposed)
                    return;
                if (commitTitle && titleLayer != null && titleLayer.IsEditing)
                    CommitTitleEdit();
                if (commitItem && itemEditor != null)
                    CommitItemRename();
            }));
        }

        internal void SetLocked(bool value)
        {
            lockedToolStripMenuItem.Checked = value;
            fenceInfo.Locked = value;
            Save();
        }

        internal void SetCanMinify(bool value)
        {
            minifyToolStripMenuItem.Checked = value;
            fenceInfo.CanMinify = value;
            if (value)
                Minify();
            else if (isMinified)
            {
                isMinified = false;
                Height = prevHeight;
                EnsureOnScreen();
                SyncItemLayer();
                Refresh();
            }
            Save();
        }

        internal void SetShowFileExtensions(bool value)
        {
            showFileExtensionsToolStripMenuItem.Checked = value;
            fenceInfo.ShowFileExtensions = value;
            Save();
            Refresh();
        }

        internal void SetTitleHeight(int value)
        {
            fenceInfo.TitleHeight = value;
            logicalTitleHeight = value;
            titleHeight = LogicalToDeviceUnits(logicalTitleHeight);
            ReloadFonts();
            Minify();
            if (isMinified)
                Height = titleHeight;
            SyncTitleLayer();
            SyncItemLayer();
            Refresh();
            Save();
        }

        internal void ApplyPersonalization(FenceAppearance value)
        {
            if (value == null)
                return;

            fenceInfo.Appearance = value.Clone();
            fenceInfo.Appearance.Normalize();
            fenceInfo.TitleHeight = Math.Max(24, Math.Min(100,
                (int)Math.Ceiling(fenceInfo.Appearance.TitleFontSize * 2.1f)));
            logicalTitleHeight = fenceInfo.TitleHeight;
            titleHeight = LogicalToDeviceUnits(logicalTitleHeight);
            ReloadFonts();
            UpdateWindowRegion();
            ApplyWindowMaterial();
            if (isMinified)
                Height = titleHeight;
            SyncTitleLayer();
            SyncItemLayer();
            Refresh();
            Save();
        }

        private void FenceWindow_MouseClick(object sender, MouseEventArgs e)
        {
            draggedItem = null;
            if ((DateTime.UtcNow - dragCompletedAtUtc).TotalMilliseconds < 250)
                return;

            if (e.Button == MouseButtons.Left)
            {
                if (hoveringItem != null && hoveringItemIconBounds.Contains(e.Location))
                {
                    selectedItem = string.Equals(selectedItem, hoveringItem, StringComparison.OrdinalIgnoreCase)
                        ? null
                        : hoveringItem;
                    Refresh();
                }
                else if (hoveringItem != null && hoveringItemTextBounds.Contains(e.Location))
                    BeginItemRename();
                else
                {
                    selectedItem = null;
                    Refresh();
                }
                return;
            }

            if (e.Button != MouseButtons.Right)
                return;

            // WM_CONTEXTMENU handles client, title, and keyboard requests using a fresh hit test.
        }

        private void FenceWindow_MouseWheel(object sender, MouseEventArgs e)
        {
            if (scrollHeight < 1)
                return;

            SetScrollOffset(scrollOffset - Math.Sign(e.Delta) * 10);
        }

        private void ThumbnailProvider_IconThumbnailLoaded(object sender, EventArgs e)
        {
            if (!IsHandleCreated || IsDisposed || Disposing)
                return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed && !Disposing)
                        Refresh();
                }));
            }
            catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated)
            {
                // A thumbnail can finish after its fence has closed.
            }
        }

        private bool ItemExists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        private string GetDisplayName(FenceEntry entry)
        {
            return entry.Type == EntryType.File && showFileExtensionsToolStripMenuItem.Checked
                ? Path.GetFileName(entry.Path)
                : entry.Name;
        }

        private FenceAppearance GetAppearance()
        {
            if (fenceInfo.Appearance == null)
                fenceInfo.Appearance = FenceAppearance.CreateDefault();
            fenceInfo.Appearance.Normalize();
            return fenceInfo.Appearance;
        }

        private static Font CreateFont(string familyName, float size)
        {
            try
            {
                return new Font(familyName, size, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch
            {
                return new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point);
            }
        }

    }

}
