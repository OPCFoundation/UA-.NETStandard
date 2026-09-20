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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.WotCon.Server.Materialization
{
    public sealed partial class LifecycleWotProjectionHost
    {
        /// <inheritdoc/>
        public bool SupportsPreparedPublication => m_lifecycle is INodeManagerBatchLifecycle;

        /// <inheritdoc/>
        public async ValueTask<IWotPreparedProjectionPublication> PrepareAsync(
            ArrayOf<WotProjectionChange> changes,
            IWotPreparedViewPublication? views = null,
            CancellationToken cancellationToken = default)
        {
            if (m_lifecycle is not INodeManagerBatchLifecycle lifecycle)
            {
                throw new NotSupportedException("The lifecycle cannot prepare an aggregate projection publication.");
            }
            var lifecycleChanges = new List<NodeManagerBatchChange>();
            var documents = new List<WotProjectionDocument>();
            foreach (WotProjectionChange change in changes)
            {
                _ = change ?? throw new ArgumentException("A projection change is null.", nameof(changes));
                NodeManagerRegistration? current = null;
                if (change.Current is not null)
                {
                    if (change.Current.Registration is not NodeManagerProjectionRegistration registration)
                    {
                        throw new ArgumentException("A projection belongs to another host.", nameof(changes));
                    }
                    current = registration.Registration;
                }
                bool immediate = change.RetirementPolicy == WotProjectionRetirementPolicy.Immediate;
                if (change.Document is null)
                {
                    lifecycleChanges.Add(NodeManagerBatchChange.Remove(
                        current ?? throw new ArgumentException("Retirement has no current owner.", nameof(changes)),
                        immediate));
                    continue;
                }
                var factory = new RuntimeNodeSetNodeManagerFactory(BuildOptions(change.Document));
                lifecycleChanges.Add(current is null
                    ? NodeManagerBatchChange.Add(factory)
                    : NodeManagerBatchChange.Replace(current, factory, immediate));
                documents.Add(change.Document);
            }
            int sourceCount = documents.Count;
            if (views is not null)
            {
                foreach (NodeManagerBatchChange change in views.Changes)
                {
                    lifecycleChanges.Add(change);
                }
            }
            IPreparedNodeManagerBatch? batch = await lifecycle.PrepareAsync(
                lifecycleChanges.ToArrayOf(), cancellationToken).ConfigureAwait(false);
            try
            {
                var handles = new List<WotProjectionHandle>(sourceCount);
                for (int i = 0; i < sourceCount; i++)
                {
                    NodeManagerRegistration registration = batch.Registrations[i];
                    handles.Add(new WotProjectionHandle(
                        documents[i].ClosureKey,
                        registration.Generation,
                        new NodeManagerProjectionRegistration(registration),
                        [],
                        0));
                }
                WotPreparedViewGraphState? graph = null;
                if (views is not null)
                {
                    var registrations = new List<NodeManagerRegistration>();
                    for (int i = sourceCount; i < batch.Registrations.Count; i++)
                    {
                        registrations.Add(batch.Registrations[i]);
                    }
                    graph = views.BindPreparedRegistrations(registrations.ToArrayOf());
                }
                var publication = new PreparedPublication(batch, handles.ToArrayOf(), graph);
                batch = null;
                return publication;
            }
            finally
            {
                if (batch is not null)
                {
                    await batch.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private sealed class PreparedPublication(
            IPreparedNodeManagerBatch batch,
            ArrayOf<WotProjectionHandle> projections,
            WotPreparedViewGraphState? graph) : IWotPreparedProjectionPublication
        {
            public ArrayOf<WotProjectionHandle> Projections { get; } = projections;
            public WotPreparedViewGraphState? ViewGraph { get; } = graph;
            public bool IsCommitted => batch.IsCommitted;
            public Exception? CleanupFailure { get; private set; }

            public async ValueTask CommitAsync(
                Func<CancellationToken, ValueTask> decideAsync,
                Action publishCommittedState,
                CancellationToken cancellationToken = default)
            {
                NodeManagerBatchResult result = await batch.CommitAsync(
                    decideAsync, publishCommittedState, cancellationToken).ConfigureAwait(false);
                CleanupFailure = result.CleanupFailure;
            }

            public ValueTask DisposeAsync()
            {
                return batch.DisposeAsync();
            }
        }
    }
}
