// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Resource-server store for credentials pushed through OPC 10000-12 §8.
    /// </summary>
    public interface IKeyCredentialStore
    {
        /// <summary>
        /// Retrieves a stored credential, or <see langword="null"/> when unknown.
        /// </summary>
        Task<KeyCredential?> GetAsync(string credentialId, CancellationToken ct);

        /// <summary>
        /// Adds or replaces a pushed credential.
        /// </summary>
        Task UpdateAsync(string credentialId, KeyCredential credential, CancellationToken ct);

        /// <summary>
        /// Deletes a pushed credential if present.
        /// </summary>
        Task DeleteAsync(string credentialId, CancellationToken ct);

        /// <summary>
        /// Lists stored credential identifiers for browse projection.
        /// </summary>
        Task<IReadOnlyList<string>> ListAsync(CancellationToken ct);
    }
}
