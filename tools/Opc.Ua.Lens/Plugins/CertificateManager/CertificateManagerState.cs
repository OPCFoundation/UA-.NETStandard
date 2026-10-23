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
using Opc.Ua;
using UaLens.Storage;

namespace UaLens.Plugins.CertificateManager;

/// <summary>
/// Workspace persistence for the certificate manager. Only offline, non-secret
/// configuration is captured: the user-added directory-store roots and the
/// selected store path. Certificates and private keys are never serialized —
/// they always live in their stores and are re-enumerated on load.
/// </summary>
internal sealed partial class CertificateManagerPlugin : IWorkspaceState
{
    private readonly List<CertificateManagerState.CustomStore> m_restoredCustomStores = new();
    private string? m_restoredSelectedPath;

    /// <summary>
    /// Captures the user-added directory stores and the selected store path as
    /// a versioned JSON object. Certificates are never included.
    /// </summary>
    public JsonElement CaptureState()
    {
        var state = new CertificateManagerState
        {
            SelectedStorePath = SelectedStore?.Identifier.StorePath
        };
        foreach (CertStoreNode node in Stores)
        {
            if (node.Role == CertStoreRole.Custom && !string.IsNullOrEmpty(node.Identifier.StorePath))
            {
                state.CustomStores.Add(new CertificateManagerState.CustomStore
                {
                    Path = node.Identifier.StorePath!,
                    DisplayName = node.DisplayName
                });
            }
        }
        return JsonSerializer.SerializeToElement(
            state, CertificateManagerStateJsonContext.Default.CertificateManagerState);
    }

    /// <summary>
    /// Restores the saved directory-store roots and selection intent without
    /// opening any store or contacting a server. The stores are materialized
    /// later, offline, when <see cref="LoadStoresAsync"/> runs.
    /// </summary>
    public Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CertificateManagerState restored = state.Deserialize(
            CertificateManagerStateJsonContext.Default.CertificateManagerState)
            ?? throw new JsonException("Certificate Manager state cannot be null.");
        restored.Validate();
        m_restoredCustomStores.Clear();
        m_restoredCustomStores.AddRange(restored.CustomStores);
        m_restoredSelectedPath = restored.SelectedStorePath;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Re-adds any restored directory-store roots to the tree. Called by the
    /// store loader after the well-known stores have been enumerated.
    /// </summary>
    private void ApplyRestoredCustomStores()
    {
        foreach (CertificateManagerState.CustomStore entry in m_restoredCustomStores)
        {
            if (string.IsNullOrEmpty(entry.Path) || StoreAlreadyPresent(entry.Path))
            {
                continue;
            }
            var id = new CertificateStoreIdentifier(
                entry.Path, CertificateStoreType.Directory, noPrivateKeys: true);
            string label = string.IsNullOrEmpty(entry.DisplayName) ? entry.Path : entry.DisplayName;
            Stores.Add(new CertStoreNode(CertStoreRole.Custom, label, id));
        }
    }

    private bool StoreAlreadyPresent(string path)
    {
        foreach (CertStoreNode node in Stores)
        {
            if (string.Equals(node.Identifier.StorePath, path, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Selects the previously selected store when it is still present, otherwise
    /// falls back to the first store. Never changes an existing selection.
    /// </summary>
    private void SelectRestoredOrFirstStore()
    {
        if (SelectedStore is not null)
        {
            return;
        }
        if (!string.IsNullOrEmpty(m_restoredSelectedPath))
        {
            foreach (CertStoreNode node in Stores)
            {
                if (string.Equals(node.Identifier.StorePath, m_restoredSelectedPath,
                    System.StringComparison.OrdinalIgnoreCase))
                {
                    SelectedStore = node;
                    return;
                }
            }
        }
        if (Stores.Count > 0)
        {
            SelectedStore = Stores[0];
        }
    }
}

/// <summary>
/// Versioned, typed snapshot of the certificate manager's offline configuration.
/// </summary>
internal sealed class CertificateManagerState
{
    /// <summary>
    /// Schema version. Restore rejects any value other than the current one.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// User-added directory-store roots to re-attach on load.
    /// </summary>
    public List<CustomStore> CustomStores { get; set; } = new();

    /// <summary>
    /// Store path selected when the workspace was saved, if any.
    /// </summary>
    public string? SelectedStorePath { get; set; }

    /// <summary>
    /// Throws when the snapshot version is not understood by this build.
    /// </summary>
    public void Validate()
    {
        if (Version != 1)
        {
            throw new JsonException($"Certificate Manager state version '{Version}' is not supported.");
        }
    }

    /// <summary>
    /// One persisted user-added directory store: its path and display label.
    /// </summary>
    public sealed class CustomStore
    {
        public string Path { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(CertificateManagerState))]
internal sealed partial class CertificateManagerStateJsonContext : JsonSerializerContext;
