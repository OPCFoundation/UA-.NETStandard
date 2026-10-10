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

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using UaLens.Storage;

namespace UaLens.Plugins.GdsManagement;

/// <summary>
/// Workspace persistence for the GDS Management tool. Only the offline target
/// configuration is captured — the GDS endpoint URL and the application filter.
/// Registered applications, certificates, live sessions and issued-certificate
/// results are never serialized, and restoring never connects or registers.
/// </summary>
internal sealed partial class GdsManagementPlugin : IWorkspaceState
{
    /// <summary>
    /// Captures the GDS endpoint URL and the app filter text as a versioned JSON
    /// object. No live session or registered-application result is included.
    /// </summary>
    public JsonElement CaptureState()
        => JsonSerializer.SerializeToElement(
            new GdsManagementState
            {
                EndpointUrl = EndpointUrl ?? string.Empty,
                FilterText = FilterText ?? string.Empty
            },
            GdsManagementStateJsonContext.Default.GdsManagementState);

    /// <summary>
    /// Restores the GDS endpoint URL and filter offline. No session is
    /// established. Unknown or unsupported state throws.
    /// </summary>
    public Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GdsManagementState restored = state.Deserialize(
            GdsManagementStateJsonContext.Default.GdsManagementState)
            ?? throw new JsonException("GDS Management state cannot be null.");
        restored.Validate();
        if (!string.IsNullOrEmpty(restored.EndpointUrl))
        {
            EndpointUrl = restored.EndpointUrl;
        }
        FilterText = restored.FilterText;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Versioned, typed snapshot of the GDS Management tool's offline target
/// configuration.
/// </summary>
internal sealed class GdsManagementState
{
    /// <summary>
    /// Schema version. Restore rejects any value other than the current one.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// The Global Discovery Server endpoint URL to target.
    /// </summary>
    public string EndpointUrl { get; set; } = string.Empty;

    /// <summary>
    /// The application list filter text.
    /// </summary>
    public string FilterText { get; set; } = string.Empty;

    /// <summary>
    /// Throws when the snapshot version is not understood by this build.
    /// </summary>
    public void Validate()
    {
        if (Version != 1)
        {
            throw new JsonException($"GDS Management state version '{Version}' is not supported.");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(GdsManagementState))]
internal sealed partial class GdsManagementStateJsonContext : JsonSerializerContext;
