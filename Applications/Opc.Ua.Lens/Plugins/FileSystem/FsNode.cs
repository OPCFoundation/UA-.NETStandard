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
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client.FileSystem;

namespace UaLens.Plugins.FileSystem;

/// <summary>
/// Tree-view row for a single file-system entry (a directory or file)
/// rendered in <see cref="FileSystemPlugin"/>.
/// </summary>
/// <remarks>
/// <para>
/// Directories carry a <see cref="UaDirectoryInfo"/> handle and a lazily
/// populated <see cref="Children"/> collection: a sentinel placeholder
/// is seeded on construction so the TreeView shows an expand chevron;
/// the first transition of <see cref="IsExpanded"/> to <c>true</c>
/// triggers <see cref="LoadChildrenAsync"/> which replaces the
/// placeholder with the directory's actual contents. Files carry a
/// <see cref="UaFileInfo"/> handle and an empty children collection.
/// </para>
/// <para>
/// Equivalent to <c>BrowsePickerNode</c> in spirit but specialised on
/// the strongly-typed <c>Opc.Ua.Client.FileSystem</c> SDK.
/// </para>
/// </remarks>
internal sealed partial class FsNode : ObservableObject
{
    /// <summary>Sentinel "Loading…" row shared by every unexpanded directory.</summary>
    private static readonly FsNode s_placeholder = new();

    private readonly ILogger? m_log;
    private bool m_loadStarted;

    /// <summary>The underlying SDK info; <c>null</c> only for the placeholder.</summary>
    public UaFileSystemInfo? Info { get; }

    /// <summary>Convenience cast; non-null exactly when <see cref="IsDirectory"/> is true.</summary>
    public UaDirectoryInfo? AsDirectory => Info as UaDirectoryInfo;

    /// <summary>Convenience cast; non-null exactly when <see cref="IsDirectory"/> is false.</summary>
    public UaFileInfo? AsFile => Info as UaFileInfo;

    /// <summary>True for directories and FileSystem roots; false for plain files.</summary>
    public bool IsDirectory { get; }

    /// <summary>Glyph used in the tree / details rows (📁 / 📄 / …).</summary>
    public string Glyph
    {
        get
        {
            if (IsPlaceholder)
            {
                return "…";
            }
            if (IsRoot)
            {
                return "💾";
            }
            return IsDirectory ? "📁" : "📄";
        }
    }

    /// <summary>Human-friendly file size; blank for directories.</summary>
    public string DisplaySize
    {
        get
        {
            if (IsDirectory)
            {
                return string.Empty;
            }
            ulong size = Size;
            if (size < 1024UL)
            {
                return $"{size} B";
            }
            if (size < 1024UL * 1024UL)
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0.0} KiB", size / 1024.0);
            }
            if (size < 1024UL * 1024UL * 1024UL)
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0.0} MiB", size / (1024.0 * 1024.0));
            }
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:0.0} GiB", size / (1024.0 * 1024.0 * 1024.0));
        }
    }

    /// <summary>ISO-8601 UTC timestamp for the details list; blank when unknown.</summary>
    public string DisplayLastModified
    {
        get
        {
            DateTime? lm = LastModified;
            if (lm is null)
            {
                return string.Empty;
            }
            return lm.Value.ToUniversalTime()
                .ToString("yyyy-MM-dd HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Display name used as the tree row label.</summary>
    public string Name { get; }

    /// <summary>UA path relative to the owning <c>FileSystemClient</c> root.</summary>
    public string FullPath { get; }

    /// <summary>Owning root row (the FileSystem itself); never null for real nodes.</summary>
    public FsNode? Root { get; }

    /// <summary>Owning <see cref="FileSystemClient"/>; never null for real nodes.</summary>
    public FileSystemClient? Client { get; }

    /// <summary>Tree-bound TwoWay expansion flag.</summary>
    [ObservableProperty]
    private bool m_isExpanded;

    /// <summary>Cached file size (in bytes); only meaningful for files after Refresh.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySize))]
    private ulong m_size;

    /// <summary>Cached <see cref="UaFileInfo.LastModifiedTime"/>; <c>null</c> if unknown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLastModified))]
    private DateTime? m_lastModified;

    /// <summary>True when this row is the loading-placeholder sentinel.</summary>
    public bool IsPlaceholder => Info is null && !IsRoot;

    /// <summary>True for the per-FileSystem root rows (vs. directory or file children).</summary>
    public bool IsRoot { get; }

    /// <summary>
    /// Lazy children. Files have an empty collection. Directories start
    /// with the placeholder; the first IsExpanded → true swaps it for
    /// the enumerated content.
    /// </summary>
    public ObservableCollection<FsNode> Children { get; } = new();

    /// <summary>Returns true once <see cref="LoadChildrenAsync"/> has populated <see cref="Children"/>.</summary>
    public bool ChildrenLoaded { get; private set; }

    /// <summary>Private constructor for the shared placeholder sentinel.</summary>
    private FsNode()
    {
        Name = "…";
        FullPath = string.Empty;
        IsDirectory = false;
        IsRoot = false;
    }

    /// <summary>Constructs a directory or file row from an SDK info object.</summary>
    public FsNode(UaFileSystemInfo info, FsNode root, FileSystemClient client, ILogger? log)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Client = client ?? throw new ArgumentNullException(nameof(client));
        m_log = log;
        IsDirectory = info.IsDirectory;
        Name = info.Name;
        FullPath = info.FullPath;
        IsRoot = false;
        if (IsDirectory)
        {
            Children.Add(s_placeholder);
        }
    }

    /// <summary>Constructs a per-FileSystem root row (no parent path).</summary>
    public FsNode(FileSystemClient client, string rootName, ILogger? log)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        m_log = log;
        IsDirectory = true;
        IsRoot = true;
        Name = rootName;
        Info = client.Root;
        FullPath = "/";
        Root = this;
        Children.Add(s_placeholder);
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && IsDirectory && !ChildrenLoaded && !m_loadStarted)
        {
            m_loadStarted = true;
            _ = LoadChildrenAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Populates <see cref="Children"/> from the underlying directory.
    /// Safe to call multiple times — re-entry while a load is already
    /// in flight is a no-op; subsequent calls after the first complete
    /// load short-circuit to <see cref="RefreshAsync"/>.
    /// </summary>
    public async Task LoadChildrenAsync(CancellationToken ct)
    {
        if (ChildrenLoaded)
        {
            await RefreshAsync(ct).ConfigureAwait(true);
            return;
        }
        if (AsDirectory is not { } dir || Root is null || Client is null)
        {
            return;
        }
        try
        {
            var loaded = new System.Collections.Generic.List<FsNode>();
            await foreach (UaFileSystemInfo child in dir.EnumerateAsync(ct).ConfigureAwait(false))
            {
                loaded.Add(new FsNode(child, Root, Client, m_log));
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Children.Clear();
                foreach (FsNode n in loaded)
                {
                    Children.Add(n);
                }
                ChildrenLoaded = true;
            }).GetTask().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            m_log?.LogWarning(ex, "FileSystem: enumerate '{Path}' failed.", FullPath);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Children.Clear();
                ChildrenLoaded = true;
            }).GetTask().ConfigureAwait(true);
        }
        finally
        {
            m_loadStarted = false;
        }
    }

    /// <summary>
    /// Re-enumerates this directory (replacing children in-place) or
    /// re-reads metadata for a file.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        if (IsDirectory)
        {
            ChildrenLoaded = false;
            m_loadStarted = false;
            await LoadChildrenAsync(ct).ConfigureAwait(true);
            return;
        }
        if (AsFile is { } file)
        {
            try
            {
                await file.RefreshAsync(ct).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Size = file.Size;
                    LastModified = file.LastModifiedTime;
                }).GetTask().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                m_log?.LogDebug(ex, "FileSystem: refresh '{Path}' failed.", FullPath);
            }
        }
    }
}
