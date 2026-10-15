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

namespace UaLens.Plugins.GdsDiscovery;

/// <summary>
/// Workspace persistence for the discovery tool. Only offline configuration is
/// captured: the LDS host, the manually added custom discovery URLs, and the
/// global-discovery query filter. Favourites live in the shared favourites store,
/// and no discovered server or live client handle is ever serialized.
/// </summary>
internal sealed partial class GdsDiscoveryPlugin : IWorkspaceState
{
    private readonly List<string> m_restoredCustomUrls = new();

    /// <summary>
    /// Captures the LDS host, manual custom URLs and query filter as a versioned
    /// JSON object. Discovered servers and favourites are not included.
    /// </summary>
    public JsonElement CaptureState()
    {
        var state = new GdsDiscoveryState
        {
            LocalMachineUrl = LocalMachineUrl ?? string.Empty,
            Filter = new GdsDiscoveryState.QueryFilter
            {
                ApplicationName = m_gdsFilter.ApplicationName,
                ApplicationUri = m_gdsFilter.ApplicationUri,
                ProductUri = m_gdsFilter.ProductUri
            }
        };
        state.Filter.ServerCapabilities.AddRange(m_gdsFilter.ServerCapabilities);
        DiscoveryNode? custom = FindRoot(DiscoveryRootKind.CustomDiscovery);
        if (custom is not null)
        {
            foreach (DiscoveryNode child in custom.Children)
            {
                if (!child.IsFavorite && child.Endpoint is not null
                    && !string.IsNullOrEmpty(child.EndpointUrl))
                {
                    state.CustomUrls.Add(child.EndpointUrl);
                }
            }
        }
        return JsonSerializer.SerializeToElement(state, GdsDiscoveryStateJsonContext.Default.GdsDiscoveryState);
    }

    /// <summary>
    /// Restores the LDS host, query filter and custom URLs without contacting any
    /// server. The custom URLs are materialized offline on the first connection
    /// notification, alongside the favourites.
    /// </summary>
    public Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GdsDiscoveryState restored = state.Deserialize(GdsDiscoveryStateJsonContext.Default.GdsDiscoveryState)
            ?? throw new JsonException("Discovery state cannot be null.");
        restored.Validate();
        if (!string.IsNullOrEmpty(restored.LocalMachineUrl))
        {
            LocalMachineUrl = restored.LocalMachineUrl;
        }
        var filter = new QueryServersFilter
        {
            ApplicationName = restored.Filter.ApplicationName,
            ApplicationUri = restored.Filter.ApplicationUri,
            ProductUri = restored.Filter.ProductUri
        };
        filter.ServerCapabilities.AddRange(restored.Filter.ServerCapabilities);
        m_gdsFilter = filter;
        m_restoredCustomUrls.Clear();
        m_restoredCustomUrls.AddRange(restored.CustomUrls);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds the restored custom discovery URLs under the Custom Discovery root.
    /// Offline only; never contacts a server.
    /// </summary>
    private void ApplyRestoredCustomUrls()
    {
        if (m_restoredCustomUrls.Count == 0)
        {
            return;
        }
        DiscoveryNode? custom = FindRoot(DiscoveryRootKind.CustomDiscovery);
        if (custom is null)
        {
            return;
        }
        foreach (string url in m_restoredCustomUrls)
        {
            if (string.IsNullOrEmpty(url))
            {
                continue;
            }
            custom.Children.Add(new DiscoveryNode
            {
                Display = url,
                Glyph = DiscoveryGlyphs.CustomUrl,
                Endpoint = new EndpointDescription(url)
            });
        }
        m_restoredCustomUrls.Clear();
    }
}

/// <summary>
/// Versioned, typed snapshot of the discovery tool's offline configuration.
/// </summary>
internal sealed class GdsDiscoveryState
{
    /// <summary>
    /// Schema version. Restore rejects any value other than the current one.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// The Local Discovery Server host URL entered in the toolbar.
    /// </summary>
    public string LocalMachineUrl { get; set; } = string.Empty;

    /// <summary>
    /// Manually added custom discovery URLs (favourites are stored separately).
    /// </summary>
    public List<string> CustomUrls { get; set; } = new();

    /// <summary>
    /// The global-discovery QueryServers filter.
    /// </summary>
    public QueryFilter Filter { get; set; } = new();

    /// <summary>
    /// Throws when the snapshot version is not understood by this build.
    /// </summary>
    public void Validate()
    {
        if (Version != 1)
        {
            throw new JsonException($"Discovery state version '{Version}' is not supported.");
        }
    }

    /// <summary>
    /// Persisted global-discovery QueryServers filter.
    /// </summary>
    public sealed class QueryFilter
    {
        public string? ApplicationName { get; set; }

        public string? ApplicationUri { get; set; }

        public string? ProductUri { get; set; }

        public List<string> ServerCapabilities { get; set; } = new();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(GdsDiscoveryState))]
internal sealed partial class GdsDiscoveryStateJsonContext : JsonSerializerContext;
