// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using UaLens.Storage;

namespace UaLens.Plugins.RoleManagement;

/// <summary>
/// Workspace persistence for the role-management tool. Roles, identity
/// mappings, applications and endpoints are live server state and are never
/// captured; only a versioned, typed marker is stored so the document can be
/// re-created on restore without a persistence hole.
/// </summary>
internal sealed partial class RoleManagementPlugin : IWorkspaceState
{
    /// <summary>
    /// Captures the tool's non-secret configuration as a JSON object. There is
    /// no offline configuration for this tool, so the snapshot is an empty,
    /// versioned envelope.
    /// </summary>
    public JsonElement CaptureState()
        => JsonSerializer.SerializeToElement(
            new RoleManagementState(),
            RoleManagementStateJsonContext.Default.RoleManagementState);

    /// <summary>
    /// Restores configuration only. This tool has none, so restore merely
    /// validates the envelope. Unknown or unsupported state throws.
    /// </summary>
    public Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RoleManagementState restored = state.Deserialize(
            RoleManagementStateJsonContext.Default.RoleManagementState)
            ?? throw new JsonException("Role Management state cannot be null.");
        restored.Validate();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Versioned, typed empty snapshot for the role-management tool.
/// </summary>
internal sealed class RoleManagementState
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
            throw new JsonException($"Role Management state version '{Version}' is not supported.");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(RoleManagementState))]
internal sealed partial class RoleManagementStateJsonContext : JsonSerializerContext;
