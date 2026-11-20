// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UaLens.Storage
{
    /// <summary>
    /// Opt-in document configuration. Implementations use source-generated typed
    /// metadata, exclude secrets/live handles, and never start workloads while restoring.
    /// </summary>
    internal interface IWorkspaceState
    {
        JsonElement CaptureState();

        Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default);
    }
}
