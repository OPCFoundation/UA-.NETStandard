/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using UaLens.Storage;

namespace UaLens.Plugins.FileSystem;

/// <summary>
/// Workspace persistence for the file-system tool. Only offline configuration is
/// captured: the root type filter and the node ids of user-picked roots. File
/// contents, live client handles and transfer results are never serialized; the
/// roots are re-attached against a live session when the connection is available.
/// </summary>
internal sealed partial class FileSystemPlugin : IWorkspaceState
{
    private readonly List<FileSystemState.RootSpec> m_userRoots = new();
    private readonly List<FileSystemState.RootSpec> m_pendingRoots = new();

    /// <summary>
    /// Captures the root filter and the user-picked root node ids as a versioned
    /// JSON object. File content is never included.
    /// </summary>
    public JsonElement CaptureState()
    {
        var state = new FileSystemState
        {
            AllowFileSystem = m_filter.AllowFileSystem,
            AllowDirectory = m_filter.AllowDirectory,
            AllowFile = m_filter.AllowFile
        };
        state.Roots.AddRange(m_userRoots);
        return JsonSerializer.SerializeToElement(state, FileSystemStateJsonContext.Default.FileSystemState);
    }

    /// <summary>
    /// Restores the filter and the intent to re-attach the saved roots. No session
    /// is contacted here; the roots are attached later, when connected, by
    /// <see cref="OnConnectionStateChangedAsync"/>.
    /// </summary>
    public Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileSystemState restored = state.Deserialize(FileSystemStateJsonContext.Default.FileSystemState)
            ?? throw new JsonException("File System state cannot be null.");
        restored.Validate();
        m_filter = new FileSystemRootFilter(
            restored.AllowFileSystem, restored.AllowDirectory, restored.AllowFile);
        m_pendingRoots.Clear();
        m_pendingRoots.AddRange(restored.Roots);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Records a successfully attached user-picked root so it can be re-attached on
    /// a later restore. The auto-discovered <c>Server.FileSystem</c> root is not
    /// tracked because it is re-attached automatically on connect.
    /// </summary>
    private void TrackUserRoot(string nodeId, string displayName)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            return;
        }
        foreach (FileSystemState.RootSpec existing in m_userRoots)
        {
            if (existing.NodeId == nodeId)
            {
                return;
            }
        }
        m_userRoots.Add(new FileSystemState.RootSpec { NodeId = nodeId, DisplayName = displayName });
    }
}

/// <summary>
/// Versioned, typed snapshot of the file-system tool's offline configuration.
/// </summary>
internal sealed class FileSystemState
{
    /// <summary>
    /// Schema version. Restore rejects any value other than the current one.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Whether the root picker accepts a <c>FileSystem</c> object.
    /// </summary>
    public bool AllowFileSystem { get; set; } = true;

    /// <summary>
    /// Whether the root picker accepts a <c>FileDirectoryType</c> object.
    /// </summary>
    public bool AllowDirectory { get; set; } = true;

    /// <summary>
    /// Whether the root picker accepts a <c>FileType</c> object.
    /// </summary>
    public bool AllowFile { get; set; }

    /// <summary>
    /// User-picked roots to re-attach on connect.
    /// </summary>
    public List<RootSpec> Roots { get; set; } = new();

    /// <summary>
    /// Throws when the snapshot version is not understood by this build.
    /// </summary>
    public void Validate()
    {
        if (Version != 1)
        {
            throw new JsonException($"File System state version '{Version}' is not supported.");
        }
    }

    /// <summary>
    /// One persisted user-picked root: its node id and display label.
    /// </summary>
    public sealed class RootSpec
    {
        public string NodeId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(FileSystemState))]
internal sealed partial class FileSystemStateJsonContext : JsonSerializerContext;
