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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Validates and initializes the distributed historian and starts leader election.
    /// </summary>
    public sealed class DistributedHistorianStartupTask : IServerPreStartupTask
    {
        /// <summary>
        /// Creates the startup task.
        /// </summary>
        public DistributedHistorianStartupTask(
            ISharedKeyValueStore store,
            SharedKeyValueHistorianProvider provider,
            IHistorianProvider selectedProvider,
            ILeaderElection election,
            IHistoryContinuationPointStore continuationStore)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_provider = provider ??
                throw new ArgumentNullException(nameof(provider));
            // The host registers the selected provider before this task runs.
            // Retain the constructor parameter for compatibility, including custom providers.
            _ = selectedProvider ??
                throw new ArgumentNullException(nameof(selectedProvider));
            m_election = election ??
                throw new ArgumentNullException(nameof(election));
            m_continuationStore = continuationStore ??
                throw new ArgumentNullException(nameof(continuationStore));
        }

        /// <inheritdoc/>
        public async ValueTask OnServerStartingAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (m_store is not ISharedKeyValueStoreConsistency consistency ||
                !consistency.IsLinearizable("historian/v1/") ||
                consistency.IsProcessLocal("historian/v1/"))
            {
                throw new InvalidOperationException(
                    "The distributed historian requires a cross-process, key-level linearizable shared store.");
            }
            if (m_continuationStore is SharedKeyValueHistoryContinuationStore &&
                (!consistency.IsLinearizable("history-continuation/v1/") ||
                    consistency.IsProcessLocal("history-continuation/v1/")))
            {
                throw new InvalidOperationException(
                    "Portable history continuations require a cross-process linearizable shared store.");
            }

            m_provider.Initialize(server.MessageContext);
            await m_provider.RecoverGarbageCollectionAsync(cancellationToken)
                .ConfigureAwait(false);
            if (m_continuationStore is
                SharedKeyValueHistoryContinuationStore sharedContinuationStore)
            {
                sharedContinuationStore.Initialize(server.MessageContext);
            }
            await m_election.TryAcquireOrRenewAsync(cancellationToken)
                .ConfigureAwait(false);
            m_election.Start();
        }

        private readonly ISharedKeyValueStore m_store;
        private readonly SharedKeyValueHistorianProvider m_provider;
        private readonly ILeaderElection m_election;
        private readonly IHistoryContinuationPointStore m_continuationStore;
    }
}
