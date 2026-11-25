// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Asynchronously lists the registered SecurityGroups of an SKS.
    /// </summary>
    public interface IAsyncPubSubSecurityGroupCatalog
    {
        /// <summary>
        /// Gets the registered SecurityGroup identifiers from their authoritative store.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<ArrayOf<string>> GetSecurityGroupIdsAsync(CancellationToken cancellationToken = default);
    }
}
