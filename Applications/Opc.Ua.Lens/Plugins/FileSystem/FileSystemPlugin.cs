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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.FileSystem;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Plugins.FileSystem;

/// <summary>
/// Three-way toggle that picks which node classes the
/// <see cref="BrowsePickerDialog"/> behind "Pick root…" will accept.
/// </summary>
internal sealed record FileSystemRootFilter(bool AllowFileSystem, bool AllowDirectory, bool AllowFile)
{
    /// <summary>Defaults: accept the standard <c>Server.FileSystem</c> plus any FileSystem-typed object.</summary>
    public static FileSystemRootFilter Default { get; } = new(true, true, false);

    /// <summary>Returns true if at least one node class is allowed.</summary>
    public bool AcceptsAnything => AllowFileSystem || AllowDirectory || AllowFile;
}

/// <summary>
/// Windows-Explorer-style FileSystem tab. The left pane shows a tree of
/// every <c>FileDirectoryType</c> / <c>FileSystem</c> root discovered on
/// the connected server (auto-discovers <c>Server.FileSystem</c>; users
/// add more with the "Pick root…" button). The right pane lists the
/// currently selected directory's children in a flat ListBox.
/// </summary>
/// <remarks>
/// <para>
/// Built on the <see cref="FileSystemClient"/> SDK that landed in
/// <c>Libraries/Opc.Ua.Client/FileSystem</c>. One client is created per
/// added root; the tab disposes none of them explicitly because
/// <see cref="FileSystemClient"/> has no IDisposable surface — all
/// session-bound state is owned by the underlying <see cref="ISession"/>.
/// </para>
/// <para>
/// Toolbar commands: PickRoot, Filter, AddFile, ExportFile, NewFolder,
/// Rename, Delete, Refresh. Drag-and-drop and context-menu wiring lives
/// in <see cref="FileSystemView"/>; this view-model exposes the same
/// operations as public methods (<see cref="ImportFilesAsync"/>,
/// <see cref="ExportFileAsync"/>, etc.) so the view can call them
/// directly from drop handlers without dispatching through a command.
/// </para>
/// </remarks>
internal sealed partial class FileSystemPlugin : ObservableObject, IPlugin
{
    private static int s_nextNumber;

    private readonly PluginHost m_host;
    private readonly ILogger m_log;
    private FileSystemView? m_view;
    private FileSystemRootFilter m_filter = FileSystemRootFilter.Default;
    private bool m_disposed;

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private FsNode? m_selectedNode;

    [ObservableProperty]
    private string m_status = "● 0 roots";

    /// <summary>Tree-bound root rows; one per <see cref="FileSystemClient"/> attached to the tab.</summary>
    public ObservableCollection<FsNode> Roots { get; } = new();

    /// <summary>
    /// Right-pane "details" list: snapshot of the selected directory's
    /// immediate children. Updated whenever <see cref="SelectedNode"/>
    /// changes or its <c>Children</c> collection mutates.
    /// </summary>
    public ObservableCollection<FsNode> SelectedChildren { get; } = new();

    public FileSystemPlugin(PluginHost host)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_log = host.Log;
        int n = Interlocked.Increment(ref s_nextNumber);
        m_title = $"File System {n}";

        // Best-effort: try the standard Server.FileSystem root on open.
        // It throws DirectoryNotFoundException on first op if the server
        // doesn't expose it — discovered lazily on expansion.
        if (m_host.Connection.Session is { } session)
        {
            TryAttachServerFileSystem(session);
        }
        else
        {
            m_log.LogWarning("File System opened without an active session.");
        }
    }

    // ----- IPlugin members -----

    public PluginKind Kind => PluginKind.FileSystem;

    Control? IPlugin.View => m_view ??= new FileSystemView { DataContext = this };
    Control? IPlugin.HeaderToolbar => null;

    public bool SupportsDuplicate => true;

    public void OnActivated() { }
    public void OnDeactivated() { }

    public IReadOnlyList<MenuItem> ContributeMenuItems()
    {
        var pickRoot = new MenuItem { Header = "_Pick Root\u2026" };
        var filter = new MenuItem { Header = "_Filter\u2026" };
        var refresh = new MenuItem { Header = "_Refresh" };
        var addFile = new MenuItem { Header = "_Add File\u2026" };
        var exportFile = new MenuItem { Header = "_Export File\u2026" };
        var newFolder = new MenuItem { Header = "_New Folder\u2026" };
        var rename = new MenuItem { Header = "Re_name\u2026" };
        var delete = new MenuItem { Header = "_Delete" };

        pickRoot.Click += async (_, _) => await PickRootAsync().ConfigureAwait(true);
        filter.Click += async (_, _) => await EditFilterAsync().ConfigureAwait(true);
        refresh.Click += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        addFile.Click += async (_, _) => await AddFileAsync().ConfigureAwait(true);
        exportFile.Click += async (_, _) => await ExportFileAsync().ConfigureAwait(true);
        newFolder.Click += async (_, _) => await NewFolderAsync().ConfigureAwait(true);
        rename.Click += async (_, _) => await RenameAsync().ConfigureAwait(true);
        delete.Click += async (_, _) => await DeleteAsync().ConfigureAwait(true);

        return [pickRoot, filter, refresh, addFile, exportFile, newFolder, rename, delete];
    }

    public ValueTask DisposeAsync()
    {
        m_disposed = true;
        // FileSystemClient has no IDisposable surface — session-bound
        // state is owned by the underlying ISession. Nothing to clean.
        return ValueTask.CompletedTask;
    }

    // ----- Property changed hooks -----

    partial void OnSelectedNodeChanged(FsNode? value)
    {
        RebuildSelectedChildren();
        UpdateStatus();
    }

    // ----- Commands -----

    /// <summary>Opens <see cref="BrowsePickerDialog"/> filtered to FileSystem / Directory / File types.</summary>
    [RelayCommand]
    public async Task PickRootAsync()
    {
        if (m_host.Connection.Session is not { } session)
        {
            return;
        }
        Window? owner = GetOwnerWindow();
        var picker = new BrowsePickerDialog(new BrowsePickerDialog.Options(
            Session: session,
            Root: ObjectIds.ObjectsFolder,
            Title: "Pick FileSystem root",
            AcceptedClasses: NodeClass.Object,
            AcceptPredicate: (id, cls) => MatchesFilterAsync(session, id, cls),
            Header: BuildFilterHeader(m_filter)));
        NodeId? pickedId = owner is null
            ? await picker.ShowDialog<NodeId?>(new Window()).ConfigureAwait(true)
            : await picker.ShowDialog<NodeId?>(owner).ConfigureAwait(true);
        if (!pickedId.HasValue || pickedId.Value.IsNull)
        {
            return;
        }
        await AttachRootAsync(session, pickedId.Value, picker.PickedDisplay).ConfigureAwait(true);
    }

    /// <summary>Edits the type filter used by the next <see cref="PickRootAsync"/>.</summary>
    [RelayCommand]
    public async Task EditFilterAsync()
    {
        Window? owner = GetOwnerWindow();
        var dlg = new FilterDialog(m_filter);
        FileSystemRootFilter? result;
        if (owner is not null)
        {
            result = await dlg.ShowDialog<FileSystemRootFilter?>(owner).ConfigureAwait(true);
        }
        else
        {
            dlg.Show();
            return;
        }
        if (result is null)
        {
            return;
        }
        m_filter = result;
        m_log.LogInformation("File System filter: FileSystem={Fs} Directory={Dir} File={File}",
            result.AllowFileSystem, result.AllowDirectory, result.AllowFile);
    }

    /// <summary>Re-enumerates the currently selected directory (or all roots when none is selected).</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (SelectedNode is { IsDirectory: true } dir)
        {
            await dir.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            RebuildSelectedChildren();
            return;
        }
        foreach (FsNode root in Roots)
        {
            await root.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }

    /// <summary>OS-Open-File-Picker → CreateFile in the selected directory → stream-copy contents.</summary>
    [RelayCommand]
    public async Task AddFileAsync()
    {
        FsNode? target = SelectedDirectory();
        if (target is null || target.AsDirectory is not { } dir)
        {
            return;
        }
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }
        var opts = new FilePickerOpenOptions
        {
            Title = "Add file to " + target.FullPath,
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("All files") { Patterns = new[] { "*" } }
            }
        };
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider
            .OpenFilePickerAsync(opts).ConfigureAwait(true);
        if (files.Count == 0)
        {
            return;
        }
        var paths = new List<string>(files.Count);
        foreach (IStorageFile f in files)
        {
            string? p = f.TryGetLocalPath();
            if (!string.IsNullOrEmpty(p))
            {
                paths.Add(p!);
            }
        }
        await ImportFilesAsync(target, paths).ConfigureAwait(true);
    }

    /// <summary>OpenRead on the selected file → OS-Save-File-Picker → stream-copy contents.</summary>
    [RelayCommand]
    public async Task ExportFileAsync()
    {
        if (SelectedNode is not { IsDirectory: false } node || node.AsFile is not { } file)
        {
            return;
        }
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }
        var opts = new FilePickerSaveOptions
        {
            Title = "Export " + node.Name,
            SuggestedFileName = node.Name,
            ShowOverwritePrompt = true
        };
        IStorageFile? target = await owner.StorageProvider
            .SaveFilePickerAsync(opts).ConfigureAwait(true);
        if (target is null)
        {
            return;
        }
        await ExportFileToAsync(file, target).ConfigureAwait(true);
    }

    /// <summary>Prompts for a name, then creates a new sub-directory under the selected directory.</summary>
    [RelayCommand]
    public async Task NewFolderAsync()
    {
        FsNode? parent = SelectedDirectory();
        if (parent?.AsDirectory is not { } dir)
        {
            return;
        }
        string? name = await PromptForNameAsync("New folder", "Enter folder name:", "New folder")
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        try
        {
            await dir.CreateSubdirectoryAsync(name, CancellationToken.None).ConfigureAwait(true);
            await parent.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            RebuildSelectedChildren();
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Create directory '{Name}' in '{Path}' failed.", name, parent.FullPath);
            Status = $"● Create folder failed: {ex.Message}";
        }
    }

    /// <summary>Prompts for a new name and moves the selected node within its parent directory.</summary>
    [RelayCommand]
    public async Task RenameAsync()
    {
        if (SelectedNode is not { Info: { } info } node || node.IsRoot)
        {
            return;
        }
        string? name = await PromptForNameAsync("Rename", "Enter new name:", info.Name).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, info.Name, StringComparison.Ordinal))
        {
            return;
        }
        try
        {
            if (info.Parent is { } parent)
            {
                await info.MoveToAsync(parent, name, CancellationToken.None).ConfigureAwait(true);
                FsNode? parentRow = FindNode(node.Root!, parent.NodeId);
                if (parentRow is not null)
                {
                    await parentRow.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
                    RebuildSelectedChildren();
                }
            }
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Rename '{Path}' → '{New}' failed.", info.FullPath, name);
            Status = $"● Rename failed: {ex.Message}";
        }
    }

    /// <summary>Deletes the selected file or directory (recursive for non-empty directories).</summary>
    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (SelectedNode is not { } node || node.IsRoot || node.Info is null)
        {
            return;
        }
        try
        {
            if (node.AsDirectory is { } dir)
            {
                await dir.DeleteAsync(recursive: true, CancellationToken.None).ConfigureAwait(true);
            }
            else
            {
                await node.Info.DeleteAsync(CancellationToken.None).ConfigureAwait(true);
            }
            FsNode? parent = FindParent(node.Root!, node.Info.NodeId);
            if (parent is not null)
            {
                await parent.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
                SelectedNode = parent;
            }
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Delete '{Path}' failed.", node.FullPath);
            Status = $"● Delete failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Drop-target entry point used by the view: copies every path in
    /// <paramref name="localFiles"/> into <paramref name="target"/>.
    /// </summary>
    public async Task ImportFilesAsync(FsNode target, IReadOnlyList<string> localFiles)
    {
        if (target.AsDirectory is not { } dir || localFiles.Count == 0)
        {
            return;
        }
        int ok = 0;
        foreach (string local in localFiles)
        {
            try
            {
                string name = Path.GetFileName(local);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                UaFileInfo created = await dir
                    .CreateFileAsync(name, CancellationToken.None).ConfigureAwait(true);
                UaFileStream writer = await created
                    .OpenWriteAsync(CancellationToken.None).ConfigureAwait(true);
                await using (writer.ConfigureAwait(false))
                {
                    FileStream src = File.OpenRead(local);
                    await using (src.ConfigureAwait(false))
                    {
                        await src.CopyToAsync(writer, 64 * 1024, CancellationToken.None).ConfigureAwait(true);
                    }
                }
                ok++;
            }
            catch (Exception ex)
            {
                m_log.LogWarning(ex, "Import '{Local}' to '{Path}' failed.", local, target.FullPath);
            }
        }
        await target.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        RebuildSelectedChildren();
        Status = string.Format(CultureInfo.InvariantCulture,
            "● Imported {0} of {1} file{2}", ok, localFiles.Count, localFiles.Count == 1 ? "" : "s");
    }

    /// <summary>
    /// Stream the contents of <paramref name="file"/> into the
    /// already-opened destination <see cref="IStorageFile"/>.
    /// </summary>
    public async Task ExportFileToAsync(UaFileInfo file, IStorageFile target)
    {
        try
        {
            Stream dst = await target.OpenWriteAsync().ConfigureAwait(true);
            await using (dst.ConfigureAwait(false))
            {
                UaFileStream src = await file.OpenReadAsync(CancellationToken.None).ConfigureAwait(true);
                await using (src.ConfigureAwait(false))
                {
                    await src.CopyToAsync(dst, 64 * 1024, CancellationToken.None).ConfigureAwait(true);
                }
            }
            Status = $"● Exported {file.Name}";
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Export '{Path}' failed.", file.FullPath);
            Status = $"● Export failed: {ex.Message}";
        }
    }

    // ----- Internals -----

    /// <summary>Best-effort attach of the standard <c>Server.FileSystem</c> root.</summary>
    private void TryAttachServerFileSystem(ISession session)
    {
        try
        {
            FileSystemClient client = FileSystemClient.OpenServerFileSystem(session);
            var root = new FsNode(client, "Server.FileSystem", m_log);
            Roots.Add(root);
            SelectedNode = root;
            UpdateStatus();
        }
        catch (Exception ex)
        {
            m_log.LogDebug(ex, "OpenServerFileSystem threw (root will not be added).");
        }
    }

    /// <summary>Attaches a user-picked <see cref="FileSystemClient"/> root.</summary>
    private async Task AttachRootAsync(ISession session, NodeId rootId, string displayName)
    {
        try
        {
            FileSystemClient client;
            string label = string.IsNullOrEmpty(displayName) ? rootId.ToString()! : displayName;
            // When the user picked a FileType node, root at its parent
            // directory and select the file in the right pane. The SDK
            // ctor itself requires a FileDirectoryType.
            NodeId typeDef = await ReadTypeDefinitionAsync(session, rootId).ConfigureAwait(true);
            if (typeDef == ObjectTypeIds.FileType)
            {
                NodeId? parentId = await GetContainingDirectoryAsync(session, rootId).ConfigureAwait(true);
                if (parentId is null || parentId.Value.IsNull)
                {
                    m_log.LogWarning(
                        "Picked file '{Id}' has no containing FileDirectoryType — root not attached.",
                        rootId);
                    return;
                }
                client = new FileSystemClient(session, parentId.Value);
            }
            else
            {
                client = new FileSystemClient(session, rootId);
            }
            var root = new FsNode(client, label, m_log);
            Roots.Add(root);
            SelectedNode = root;
            UpdateStatus();
            m_log.LogInformation("File System root attached: {Name} ({Id})", label, rootId);
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Attach FileSystem root '{Id}' failed.", rootId);
            Status = $"● Attach root failed: {ex.Message}";
        }
    }

    /// <summary>Predicate handed to <see cref="BrowsePickerDialog"/> to enforce <see cref="m_filter"/>.</summary>
    private async Task<bool> MatchesFilterAsync(ISession session, NodeId nodeId, NodeClass nodeClass)
    {
        if (nodeClass != NodeClass.Object || nodeId.IsNull)
        {
            return false;
        }
        if (m_filter.AllowFileSystem && nodeId == ObjectIds.FileSystem)
        {
            return true;
        }
        NodeId typeDef = await ReadTypeDefinitionAsync(session, nodeId).ConfigureAwait(true);
        if (typeDef.IsNull)
        {
            return false;
        }
        if (m_filter.AllowFile && typeDef == ObjectTypeIds.FileType)
        {
            return true;
        }
        if (m_filter.AllowDirectory && typeDef == ObjectTypeIds.FileDirectoryType)
        {
            return true;
        }
        if (m_filter.AllowFileSystem
            && await IsSubtypeOfAsync(session, typeDef, ObjectTypeIds.FileDirectoryType).ConfigureAwait(true))
        {
            // Most servers expose FileSystems as FileDirectoryType (or a
            // subtype thereof) under Server / Objects.
            return true;
        }
        return false;
    }

    private static async Task<NodeId> ReadTypeDefinitionAsync(ISession session, NodeId nodeId)
    {
        ArrayOf<BrowseDescription> browse = new BrowseDescription[]
        {
            new BrowseDescription
            {
                NodeId = nodeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition,
                IncludeSubtypes = false,
                NodeClassMask = (uint)NodeClass.ObjectType,
                ResultMask = (uint)BrowseResultMask.None
            }
        };
        try
        {
            BrowseResponse resp = await session
                .BrowseAsync(null, null, 0, browse, CancellationToken.None).ConfigureAwait(false);
            if (resp.Results.Count == 0 || StatusCode.IsBad(resp.Results[0].StatusCode))
            {
                return NodeId.Null;
            }
            foreach (ReferenceDescription r in resp.Results[0].References)
            {
                return ExpandedNodeId.ToNodeId(r.NodeId, session.NamespaceUris);
            }
        }
        catch
        {
            // Treat browse failure as "unknown type" — caller decides.
        }
        return NodeId.Null;
    }

    private static async Task<bool> IsSubtypeOfAsync(ISession session, NodeId typeId, NodeId parentType)
    {
        if (typeId.IsNull || parentType.IsNull)
        {
            return false;
        }
        NodeId current = typeId;
        for (int depth = 0; depth < 16; depth++)
        {
            if (current == parentType)
            {
                return true;
            }
            ArrayOf<BrowseDescription> browse = new BrowseDescription[]
            {
                new BrowseDescription
                {
                    NodeId = current,
                    BrowseDirection = BrowseDirection.Inverse,
                    ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                    IncludeSubtypes = false,
                    NodeClassMask = (uint)NodeClass.ObjectType,
                    ResultMask = (uint)BrowseResultMask.None
                }
            };
            BrowseResponse resp;
            try
            {
                resp = await session
                    .BrowseAsync(null, null, 0, browse, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
            if (resp.Results.Count == 0 || StatusCode.IsBad(resp.Results[0].StatusCode))
            {
                return false;
            }
            NodeId next = NodeId.Null;
            foreach (ReferenceDescription r in resp.Results[0].References)
            {
                next = ExpandedNodeId.ToNodeId(r.NodeId, session.NamespaceUris);
                break;
            }
            if (next.IsNull)
            {
                return false;
            }
            current = next;
        }
        return false;
    }

    private static async Task<NodeId?> GetContainingDirectoryAsync(ISession session, NodeId fileId)
    {
        ArrayOf<BrowseDescription> browse = new BrowseDescription[]
        {
            new BrowseDescription
            {
                NodeId = fileId,
                BrowseDirection = BrowseDirection.Inverse,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.NodeClass
            }
        };
        try
        {
            BrowseResponse resp = await session
                .BrowseAsync(null, null, 0, browse, CancellationToken.None).ConfigureAwait(false);
            if (resp.Results.Count == 0 || StatusCode.IsBad(resp.Results[0].StatusCode))
            {
                return null;
            }
            // Materialise before awaiting — References' enumerator is a
            // ref-struct that cannot cross an await boundary.
            var candidates = new List<NodeId>();
            foreach (ReferenceDescription r in resp.Results[0].References)
            {
                candidates.Add(ExpandedNodeId.ToNodeId(r.NodeId, session.NamespaceUris));
            }
            foreach (NodeId candidate in candidates)
            {
                NodeId td = await ReadTypeDefinitionAsync(session, candidate).ConfigureAwait(false);
                if (td == ObjectTypeIds.FileDirectoryType
                    || await IsSubtypeOfAsync(session, td, ObjectTypeIds.FileDirectoryType).ConfigureAwait(false))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            // Best-effort.
        }
        return null;
    }

    private static string BuildFilterHeader(FileSystemRootFilter filter)
    {
        var parts = new List<string>(3);
        if (filter.AllowFileSystem)
        {
            parts.Add("FileSystem");
        }
        if (filter.AllowDirectory)
        {
            parts.Add("Directory");
        }
        if (filter.AllowFile)
        {
            parts.Add("File");
        }
        return parts.Count == 0
            ? "No classes accepted — adjust 'Filter…' first."
            : "Accept: " + string.Join(" / ", parts);
    }

    /// <summary>Walks <paramref name="root"/> looking for a node whose Info matches <paramref name="nodeId"/>.</summary>
    private static FsNode? FindNode(FsNode root, NodeId nodeId)
    {
        if (root.Info?.NodeId == nodeId)
        {
            return root;
        }
        foreach (FsNode child in root.Children)
        {
            if (child.IsPlaceholder)
            {
                continue;
            }
            FsNode? hit = FindNode(child, nodeId);
            if (hit is not null)
            {
                return hit;
            }
        }
        return null;
    }

    /// <summary>Walks <paramref name="root"/> looking for the parent row of <paramref name="childId"/>.</summary>
    private static FsNode? FindParent(FsNode root, NodeId childId)
    {
        foreach (FsNode child in root.Children)
        {
            if (child.IsPlaceholder)
            {
                continue;
            }
            if (child.Info?.NodeId == childId)
            {
                return root;
            }
            FsNode? hit = FindParent(child, childId);
            if (hit is not null)
            {
                return hit;
            }
        }
        return null;
    }

    /// <summary>The selected node if it's a directory; falls back to its parent for files.</summary>
    private FsNode? SelectedDirectory()
    {
        if (SelectedNode is { IsDirectory: true } dir)
        {
            return dir;
        }
        if (SelectedNode is { Info: { Parent: { } parentInfo }, Root: { } root })
        {
            return FindNode(root, parentInfo.NodeId);
        }
        return SelectedNode?.Root;
    }

    private void RebuildSelectedChildren()
    {
        SelectedChildren.Clear();
        if (SelectedNode is { IsDirectory: true } dir)
        {
            foreach (FsNode c in dir.Children)
            {
                if (!c.IsPlaceholder)
                {
                    SelectedChildren.Add(c);
                }
            }
            if (!dir.ChildrenLoaded)
            {
                _ = dir.LoadChildrenAsync(CancellationToken.None);
            }
        }
    }

    private void UpdateStatus()
    {
        if (Roots.Count == 0)
        {
            Status = "● No FileSystem roots — try Pick Root…";
            return;
        }
        string sel = SelectedNode is { } s ? s.FullPath : "/";
        Status = string.Format(CultureInfo.InvariantCulture,
            "● {0} root{1} · {2}", Roots.Count, Roots.Count == 1 ? "" : "s", sel);
    }

    private static Window? GetOwnerWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desk)
        {
            return desk.MainWindow;
        }
        return null;
    }

    private async Task<string?> PromptForNameAsync(string title, string prompt, string defaultValue)
    {
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return null;
        }
        var dlg = new NameInputDialog(title, prompt, defaultValue);
        if (m_disposed)
        {
            return null;
        }
        return await dlg.ShowDialog<string?>(owner).ConfigureAwait(true);
    }
}
