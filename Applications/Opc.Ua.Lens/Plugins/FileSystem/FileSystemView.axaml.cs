/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace UaLens.Plugins.FileSystem;

/// <summary>
/// Avalonia view for <see cref="FileSystemPlugin"/>. Layout: toolbar on
/// top, left-hand TreeView of FileSystem roots, right-hand details
/// ListBox of the selected directory's children. Wires the inbound
/// drop-from-OS-Explorer behaviour and the per-row context menu in
/// code-behind so the templates stay free of binding boilerplate.
/// </summary>
internal sealed partial class FileSystemView : UserControl
{
    public FileSystemView()
    {
        InitializeComponent();

        // Allow files to be dragged onto the tree/details from the OS.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Right-click on a tree row sets <see cref="FileSystemPlugin.SelectedNode"/>
    /// to the clicked node before showing the context menu, so commands
    /// always act on the row that was actually clicked.
    /// </summary>
    private void OnTreeRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not FileSystemPlugin vm)
        {
            return;
        }
        if (sender is not Control row || row.DataContext is not FsNode node)
        {
            return;
        }
        var props = e.GetCurrentPoint(row).Properties;
        if (props.IsRightButtonPressed || props.IsLeftButtonPressed)
        {
            vm.SelectedNode = node;
        }
        if (props.IsRightButtonPressed)
        {
            row.ContextMenu = BuildContextMenu(vm, node);
        }
    }

    private void OnDetailsRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not FileSystemPlugin vm)
        {
            return;
        }
        if (sender is not Control row || row.DataContext is not FsNode node)
        {
            return;
        }
        var props = e.GetCurrentPoint(row).Properties;
        if (props.IsLeftButtonPressed && e.ClickCount == 2 && node.IsDirectory)
        {
            // Double-click directory in details list → drill into it.
            vm.SelectedNode = node;
            node.IsExpanded = true;
            return;
        }
        if (props.IsRightButtonPressed)
        {
            vm.SelectedNode = node;
            row.ContextMenu = BuildContextMenu(vm, node);
        }
    }

    private static ContextMenu BuildContextMenu(FileSystemPlugin vm, FsNode node)
    {
        var menu = new ContextMenu();
        if (node.IsDirectory)
        {
            var newFolder = new MenuItem { Header = "_New Folder…" };
            newFolder.Click += async (_, _) => await vm.NewFolderAsync().ConfigureAwait(true);
            menu.Items.Add(newFolder);

            var add = new MenuItem { Header = "_Add File…" };
            add.Click += async (_, _) => await vm.AddFileAsync().ConfigureAwait(true);
            menu.Items.Add(add);

            menu.Items.Add(new Separator());

            var refresh = new MenuItem { Header = "_Refresh" };
            refresh.Click += async (_, _) => await vm.RefreshAsync().ConfigureAwait(true);
            menu.Items.Add(refresh);
        }
        else
        {
            var export = new MenuItem { Header = "_Export to file…" };
            export.Click += async (_, _) => await vm.ExportFileAsync().ConfigureAwait(true);
            menu.Items.Add(export);
        }
        if (!node.IsRoot)
        {
            menu.Items.Add(new Separator());
            var rename = new MenuItem { Header = "Re_name…" };
            rename.Click += async (_, _) => await vm.RenameAsync().ConfigureAwait(true);
            menu.Items.Add(rename);

            var delete = new MenuItem { Header = "_Delete" };
            delete.Click += async (_, _) => await vm.DeleteAsync().ConfigureAwait(true);
            menu.Items.Add(delete);
        }
        return menu;
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        // Accept only file-list drops; anything else falls through.
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not FileSystemPlugin vm)
        {
            return;
        }
        if (!e.Data.Contains(DataFormats.Files))
        {
            return;
        }
        IEnumerable<IStorageItem>? items = e.Data.GetFiles();
        if (items is null)
        {
            return;
        }
        var paths = new List<string>();
        foreach (IStorageItem si in items)
        {
            if (si is IStorageFile sf)
            {
                string? p = sf.TryGetLocalPath();
                if (!string.IsNullOrEmpty(p))
                {
                    paths.Add(p!);
                }
            }
        }
        if (paths.Count == 0)
        {
            return;
        }
        // Target the row under the cursor when one is reachable;
        // otherwise fall back to the currently selected directory.
        FsNode? target = ResolveDropTarget(e) ?? vm.SelectedNode;
        if (target is null)
        {
            return;
        }
        _ = vm.ImportFilesAsync(target, paths);
        e.Handled = true;
    }

    private static FsNode? ResolveDropTarget(DragEventArgs e)
    {
        if (e.Source is StyledElement el && el.DataContext is FsNode target)
        {
            return target;
        }
        return null;
    }
}
