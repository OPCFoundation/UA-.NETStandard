// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    public partial class Session
    {
        /// <inheritdoc/>
        /// <param name="ct">The cancellation token supplied by the caller.</param>
        public override ValueTask<ISessionClient> CreateBindingAsync(CancellationToken ct = default)
        {
            return CreateBindingAsync(static () => true, ct);
        }

        /// <summary>
        /// Captures session dispatch with an additional managed-owner validity check.
        /// </summary>
        /// <param name="ownerCurrent">The additional owner-validity check.</param>
        /// <param name="ct">The cancellation token supplied by the caller.</param>
        internal ValueTask<ISessionClient> CreateBindingAsync(Func<bool> ownerCurrent, CancellationToken ct)
        {
            NamespaceTable namespaces = NamespaceUris;
            StringTable servers = ServerUris;
            return CreateBindingCoreAsync(
                namespaces, servers, m_telemetry,
                () => !Reconnecting &&
                    ReferenceEquals(NamespaceUris, namespaces) &&
                    ReferenceEquals(ServerUris, servers) &&
                    ownerCurrent(),
                ct);
        }
    }
}
