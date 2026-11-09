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

namespace UaLens.Plugins.UserManagement;

/// <summary>
/// Workspace persistence for the user-management tool. User accounts,
/// passwords and password restrictions are live server state and are never
/// captured; only a versioned, typed marker is stored so the document can be
/// re-created on restore without a persistence hole.
/// </summary>
internal sealed partial class UserManagementPlugin : IWorkspaceState
{
    /// <summary>
    /// Captures the tool's non-secret configuration as a JSON object. There is
    /// no offline configuration for this tool, so the snapshot is an empty,
    /// versioned envelope.
    /// </summary>
    public JsonElement CaptureState()
        => JsonSerializer.SerializeToElement(
            new UserManagementState(),
            UserManagementStateJsonContext.Default.UserManagementState);

    /// <summary>
    /// Restores configuration only. This tool has none, so restore merely
    /// validates the envelope. Unknown or unsupported state throws.
    /// </summary>
    public Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UserManagementState restored = state.Deserialize(
            UserManagementStateJsonContext.Default.UserManagementState)
            ?? throw new JsonException("User Management state cannot be null.");
        restored.Validate();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Versioned, typed empty snapshot for the user-management tool.
/// </summary>
internal sealed class UserManagementState
{
    /// <summary>
    /// Schema version. Restore rejects any value other than the current one.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Throws when the snapshot version is not understood by this build.
    /// </summary>
    public void Validate()
    {
        if (Version != 1)
        {
            throw new JsonException($"User Management state version '{Version}' is not supported.");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(UserManagementState))]
internal sealed partial class UserManagementStateJsonContext : JsonSerializerContext;
