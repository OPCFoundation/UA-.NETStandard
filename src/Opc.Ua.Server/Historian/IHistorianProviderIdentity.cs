// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Stable identity for a historian provider replicated across server instances.
    /// </summary>
    public interface IHistorianProviderIdentity
    {
        /// <summary>
        /// Provider identity, identical on every replica serving the same archive.
        /// </summary>
        string ProviderId { get; }
    }
}
