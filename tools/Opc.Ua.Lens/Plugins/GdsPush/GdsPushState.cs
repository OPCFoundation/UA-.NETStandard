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

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Storage;

namespace UaLens.Plugins.GdsPush;

/// <summary>
/// Workspace persistence for the GDS Push tool. Only the offline view
/// configuration is captured — the active trust-list bucket and the trust-list
/// mask. Certificates, private keys, live sessions and endpoints are never
/// serialized, and restoring never establishes a session or pushes anything.
/// </summary>
internal sealed partial class GdsPushPlugin : IWorkspaceState
{
    /// <summary>
    /// Captures the active bucket and trust-list mask as a versioned JSON object.
    /// No certificate material or live session state is included.
    /// </summary>
    public JsonElement CaptureState()
        => JsonSerializer.SerializeToElement(
            new GdsPushState
            {
                ActiveBucket = this.ActiveBucket.ToString(),
                TrustListMasks = this.TrustListMasks.ToString()
            },
            GdsPushStateJsonContext.Default.GdsPushState);

    /// <summary>
    /// Restores the offline view configuration only. No session is established and
    /// nothing is pushed. Unknown or unsupported state throws.
    /// </summary>
    public Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GdsPushState restored = state.Deserialize(GdsPushStateJsonContext.Default.GdsPushState)
            ?? throw new JsonException("GDS Push state cannot be null.");
        restored.Validate();
        if (Enum.TryParse(restored.ActiveBucket, out TrustListBucket bucket) && Enum.IsDefined(bucket))
        {
            ActiveBucket = bucket;
        }
        if (Enum.TryParse(restored.TrustListMasks, out Opc.Ua.TrustListMasks masks))
        {
            TrustListMasks = masks;
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Versioned, typed snapshot of the GDS Push tool's offline view configuration.
/// </summary>
internal sealed class GdsPushState
{
    /// <summary>
    /// Schema version. Restore rejects any value other than the current one.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// The active trust-list bucket (Trusted, Issuer, Rejected).
    /// </summary>
    public string ActiveBucket { get; set; } = nameof(TrustListBucket.Trusted);

    /// <summary>
    /// The trust-list categories the refresh command pulls.
    /// </summary>
    public string TrustListMasks { get; set; } = "All";

    /// <summary>
    /// Throws when the snapshot version is not understood by this build.
    /// </summary>
    public void Validate()
    {
        if (Version != 1)
        {
            throw new JsonException($"GDS Push state version '{Version}' is not supported.");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(GdsPushState))]
internal sealed partial class GdsPushStateJsonContext : JsonSerializerContext;
