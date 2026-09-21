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
using System.Linq;
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
        public ArrayOf<WoTAtomicityEnum> SupportedAtomicities =>
            m_lifecycle is INodeManagerPublicationLifecycle { SupportsPublicationIsolation: true }
                ? [WoTAtomicityEnum.PerResource, WoTAtomicityEnum.PerGroup,
                    WoTAtomicityEnum.PerClosure, WoTAtomicityEnum.PerRegistry]
                : [];

        /// <inheritdoc/>
        public IWotProjectionPublicationCapture CapturePublication()
        {
            return m_lifecycle is INodeManagerPublicationLifecycle { SupportsPublicationIsolation: true } lifecycle
                ? new PublicationCapture(this, lifecycle.CapturePublication())
                : throw new NotSupportedException("The lifecycle cannot isolate a publication invocation.");
        }

        /// <inheritdoc/>
        public ValueTask<IWotPreparedProjectionPublication> PrepareAsync(
            ArrayOf<WotProjectionChange> changes,
            IWotPreparedViewPublication? views = null,
            CancellationToken cancellationToken = default)
        {
            return PrepareCoreAsync(changes, views, null, cancellationToken);
        }

        private async ValueTask<IWotPreparedProjectionPublication> PrepareCoreAsync(
            ArrayOf<WotProjectionChange> changes,
            IWotPreparedViewPublication? views,
            INodeManagerPublication? publication,
            CancellationToken cancellationToken)
        {
            if (m_lifecycle is not INodeManagerBatchLifecycle lifecycle)
            {
                throw new NotSupportedException("The lifecycle cannot prepare an aggregate projection publication.");
            }
            var lifecycleChanges = new List<NodeManagerBatchChange>();
            var documents = new List<WotProjectionDocument>();
            var runtimePublications = new List<WotProjectionRuntimePublication>();
            WotPreparedSourceImage? sourceImage = views is IWotPreparedViewSourceConsumer ? new() : null;
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
                    sourceImage?.Exclude(current.NodeManager);
                }
                bool immediate = change.RetirementPolicy == WotProjectionRetirementPolicy.Immediate;
                if (change.Document is null)
                {
                    lifecycleChanges.Add(NodeManagerBatchChange.Remove(
                        current ?? throw new ArgumentException("Retirement has no current owner.", nameof(changes)),
                        immediate));
                    continue;
                }
                RuntimeNodeSetOptions options = BuildOptions(change.Document);
                runtimePublications.Add(new WotProjectionRuntimePublication(options));
                IAsyncNodeManagerFactory factory = new RuntimeNodeSetNodeManagerFactory(options);
                if (sourceImage is not null)
                {
                    factory = sourceImage.Capture(factory);
                }
                lifecycleChanges.Add(current is null
                    ? NodeManagerBatchChange.Add(factory)
                    : NodeManagerBatchChange.Replace(current, factory, immediate));
                documents.Add(change.Document);
            }
            int sourceCount = documents.Count;
            if (views is IWotPreparedViewSourceConsumer consumer)
            {
                consumer.BindSourceImage(sourceImage!);
            }
            if (views is not null)
            {
                foreach (NodeManagerBatchChange change in views.Changes)
                {
                    lifecycleChanges.Add(change);
                }
            }
            IPreparedNodeManagerBatch? batch = publication is null
                ? await lifecycle.PrepareAsync(lifecycleChanges.ToArrayOf(), cancellationToken).ConfigureAwait(false)
                : await publication.PrepareAsync(lifecycleChanges.ToArrayOf(), cancellationToken).ConfigureAwait(false);
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
                sourceImage?.BindRegistrations(batch.Registrations.ToList().Take(sourceCount).ToArrayOf());
                WotPreparedViewGraphState? graph = null;
                if (views is not null)
                {
                    var registrations = new List<NodeManagerRegistration>();
                    for (int i = sourceCount; i < batch.Registrations.Count; i++)
                    {
                        NodeManagerRegistration registration = batch.Registrations[i];
                        if (registration.NodeManager is not IWotCanonicalViewReadImage)
                        {
                            throw new NotSupportedException(
                                "A prepared View owner must retain captured membership metadata.");
                        }
                        registrations.Add(registration);
                    }
                    graph = views.BindPreparedRegistrations(registrations.ToArrayOf());
                }
                var preparedPublication = new PreparedPublication(
                    batch, handles.ToArrayOf(), graph, runtimePublications.ToArrayOf());
                batch = null;
                return preparedPublication;
            }
            finally
            {
                if (batch is not null)
                {
                    await batch.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private sealed class PublicationCapture(
            LifecycleWotProjectionHost owner, INodeManagerPublicationCapture capture) : IWotProjectionPublicationCapture
        {
            public async ValueTask<IWotProjectionPublication> BeginAsync(
                CancellationToken cancellationToken = default)
            {
                INodeManagerPublication invocation = await capture.BeginAsync(cancellationToken).ConfigureAwait(false);
                return new PublicationInvocation(owner, invocation);
            }
        }

        private sealed class PublicationInvocation(
            LifecycleWotProjectionHost owner, INodeManagerPublication publication) : IWotProjectionPublication
        {
            public bool IsCurrent => publication.IsCurrent;

            public ValueTask<IWotPreparedProjectionPublication> PrepareAsync(
                ArrayOf<WotProjectionChange> changes,
                IWotPreparedViewPublication? views = null,
                CancellationToken cancellationToken = default)
            {
                return owner.PrepareCoreAsync(changes, views, publication, cancellationToken);
            }

            public ValueTask DisposeAsync()
            {
                return publication.DisposeAsync();
            }
        }

        private sealed class PreparedPublication(
            IPreparedNodeManagerBatch batch,
            ArrayOf<WotProjectionHandle> projections,
            WotPreparedViewGraphState? graph,
            ArrayOf<WotProjectionRuntimePublication> runtimePublications) : IWotPreparedProjectionPublication
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
                _ = publishCommittedState ?? throw new ArgumentNullException(nameof(publishCommittedState));
                NodeManagerBatchResult result = await batch.CommitAsync(
                    decideAsync,
                    () =>
                    {
                        for (int i = 0; i < runtimePublications.Count; i++)
                        {
                            runtimePublications[i].Publish(Projections[i].Generation);
                        }
                        publishCommittedState();
                    },
                    cancellationToken).ConfigureAwait(false);
                CleanupFailure = result.CleanupFailure;
            }

            public ValueTask DisposeAsync()
            {
                return batch.DisposeAsync();
            }
        }
    }
}
