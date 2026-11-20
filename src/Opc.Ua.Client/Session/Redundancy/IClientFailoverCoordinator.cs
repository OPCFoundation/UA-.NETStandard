// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Coordinates backup-client takeover of active-client Subscriptions for OPC 10000-4 §6.6.3 client redundancy.
    /// </summary>
    public interface IClientFailoverCoordinator
    {
        /// <summary>
        /// Discovers subscription ids owned by the active client.
        /// </summary>
        ValueTask<ArrayOf<uint>> DiscoverActiveSubscriptionIdsAsync(
            ISession backupSession,
            ClientRedundancyTransferOptions options,
            CancellationToken ct = default);

        /// <summary>
        /// Transfers active-client subscriptions to the backup client session.
        /// </summary>
        ValueTask<ArrayOf<TransferResult>> TransferActiveSubscriptionsAsync(
            ISession backupSession,
            ClientRedundancyTransferOptions options,
            CancellationToken ct = default);
    }
}
