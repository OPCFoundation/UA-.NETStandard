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
    /// <summary>
    /// The production <see cref="IWotProjectionHost"/> that projects WoT closures
    /// onto the live server through the public NodeManager lifecycle. First
    /// activation uses
    /// <see cref="RuntimeNodeSetLifecycleExtensions.AddRuntimeNodeSetAsync"/>;
    /// updates use
    /// <see cref="RuntimeNodeSetLifecycleExtensions.ShadowReloadRuntimeNodeSetAsync"/>,
    /// so the previous generation keeps serving its existing monitored items
    /// until they drain. The stable WoT registry NodeManager is never touched.
    /// </summary>
    public sealed partial class LifecycleWotProjectionHost
    {
        /// <inheritdoc/>
        public bool SupportsPreparedPublication => m_lifecycle is INodeManagerBatchLifecycle;

        /// <summary>
        /// Gets the atomicity modes this actual source owner can isolate.
        /// </summary>
        /// <inheritdoc/>
        public ArrayOf<WoTAtomicityEnum> SupportedAtomicities =>
            m_lifecycle is INodeManagerPublicationLifecycle { SupportsPublicationIsolation: true }
                ? [WoTAtomicityEnum.PerResource, WoTAtomicityEnum.PerGroup,
                    WoTAtomicityEnum.PerClosure, WoTAtomicityEnum.PerRegistry]
                : [];

        /// <summary>
        /// Captures source-owner revisions before input acquisition and conversion.
        /// </summary>
        /// <inheritdoc/>
        public IWotProjectionPublicationCapture CapturePublication()
        {
            return m_lifecycle is INodeManagerPublicationLifecycle { SupportsPublicationIsolation: true } lifecycle
                ? new PublicationCapture(this, lifecycle.CapturePublication())
                : throw new NotSupportedException("The lifecycle cannot isolate a publication invocation.");
        }

        /// <inheritdoc/>
        /// <param name="views">The WoT prepared view publication.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
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
            PreparedChanges candidate = CreatePreparedChanges(changes, views);
            IPreparedNodeManagerBatch? batch = publication is null
                ? await lifecycle.PrepareAsync(candidate.Changes, cancellationToken).ConfigureAwait(false)
                : await publication.PrepareAsync(candidate.Changes, cancellationToken).ConfigureAwait(false);
            try
            {
                PreparedPublication preparedPublication = BindPreparedPublication(candidate, views, batch);
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

        private async ValueTask ValidateCoreAsync(
            ArrayOf<WotProjectionChange> changes,
            Func<IWotPreparedProjectionPublication, CancellationToken, ValueTask> inspectAsync,
            IWotPreparedViewPublication? views,
            INodeManagerPublication publication,
            CancellationToken cancellationToken)
        {
            _ = inspectAsync ?? throw new ArgumentNullException(nameof(inspectAsync));
            if (publication is not INodeManagerValidationPublication validation)
            {
                throw new NotSupportedException("The lifecycle cannot privately validate a publication candidate.");
            }
            PreparedChanges candidate = CreatePreparedChanges(changes, views);
            await validation.ValidateAsync(candidate.Changes, async (batch, token) =>
            {
                PreparedPublication prepared = BindPreparedPublication(candidate, views, batch);
                await inspectAsync(prepared, token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        private static PreparedPublication BindPreparedPublication(
            PreparedChanges candidate, IWotPreparedViewPublication? views, IPreparedNodeManagerBatch batch)
        {
            var handles = new List<WotProjectionHandle>(candidate.Documents.Count);
            for (int i = 0; i < candidate.Documents.Count; i++)
            {
                NodeManagerRegistration registration = batch.Registrations[i];
                handles.Add(new WotProjectionHandle(
                    candidate.Documents[i].ClosureKey, registration.Generation,
                    new NodeManagerProjectionRegistration(registration), [], 0));
            }
            WotPreparedViewGraphState? graph = BindPreparedViews(candidate, views, batch.Registrations);
            return new PreparedPublication(batch, handles.ToArrayOf(), graph, candidate.RuntimePublications);
        }

        private PreparedChanges CreatePreparedChanges(
            ArrayOf<WotProjectionChange> changes, IWotPreparedViewPublication? views)
        {
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
            return new PreparedChanges(
                lifecycleChanges.ToArrayOf(), documents.ToArrayOf(), runtimePublications.ToArrayOf(), sourceImage);
        }

        private static WotPreparedViewGraphState? BindPreparedViews(
            PreparedChanges candidate,
            IWotPreparedViewPublication? views,
            ArrayOf<NodeManagerRegistration> registrations)
        {
            int sourceCount = candidate.Documents.Count;
            candidate.SourceImage?.BindRegistrations(registrations.ToList().Take(sourceCount).ToArrayOf());
            if (views is null)
            {
                return null;
            }
            var viewRegistrations = new List<NodeManagerRegistration>();
            for (int i = sourceCount; i < registrations.Count; i++)
            {
                NodeManagerRegistration registration = registrations[i];
                if (registration.NodeManager is not IWotCanonicalViewReadImage)
                {
                    throw new NotSupportedException(
                        "A prepared View owner must retain captured membership metadata.");
                }
                viewRegistrations.Add(registration);
            }
            return views.BindPreparedRegistrations(viewRegistrations.ToArrayOf());
        }

        private sealed record PreparedChanges(
            ArrayOf<NodeManagerBatchChange> Changes,
            ArrayOf<WotProjectionDocument> Documents,
            ArrayOf<WotProjectionRuntimePublication> RuntimePublications,
            WotPreparedSourceImage? SourceImage);

        private sealed class PublicationCapture(
            LifecycleWotProjectionHost owner, INodeManagerPublicationCapture capture) : IWotProjectionPublicationCapture
        {
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public async ValueTask<IWotProjectionPublication> BeginAsync(
                CancellationToken cancellationToken = default)
            {
                INodeManagerPublication invocation = await capture.BeginAsync(cancellationToken).ConfigureAwait(false);
                return new PublicationInvocation(owner, invocation);
            }
        }

        private sealed class PublicationInvocation(
            LifecycleWotProjectionHost owner, INodeManagerPublication publication) : IWotProjectionValidationPublication
        {
            /// <summary>
            /// Gets the is current for <c>PublicationInvocation</c>.
            /// </summary>
            public bool IsCurrent => publication.IsCurrent;

            /// <param name="views">The WoT prepared view publication.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask<IWotPreparedProjectionPublication> PrepareAsync(
                ArrayOf<WotProjectionChange> changes,
                IWotPreparedViewPublication? views = null,
                CancellationToken cancellationToken = default)
            {
                return owner.PrepareCoreAsync(changes, views, publication, cancellationToken);
            }

            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public async ValueTask<IWotPreparedProjectionPublication> PrepareReadImagesAsync(
                ArrayOf<INodeManagerReadImage> images,
                CancellationToken cancellationToken = default)
            {
                IPreparedNodeManagerBatch batch = await publication.PrepareReadImagesAsync(images, cancellationToken)
                    .ConfigureAwait(false);
                return new PreparedPublication(batch, [], null, []);
            }

            /// <param name="inspectAsync">The inspect async.</param>
            /// <param name="views">The WoT prepared view publication.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask ValidateAsync(
                ArrayOf<WotProjectionChange> changes,
                Func<IWotPreparedProjectionPublication, CancellationToken, ValueTask> inspectAsync,
                IWotPreparedViewPublication? views = null,
                CancellationToken cancellationToken = default)
            {
                return owner.ValidateCoreAsync(changes, inspectAsync, views, publication, cancellationToken);
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
            /// <summary>
            /// Gets the projections for <c>PreparedPublication</c>.
            /// </summary>
            public ArrayOf<WotProjectionHandle> Projections { get; } = projections;
            /// <summary>
            /// Gets the view graph for <c>PreparedPublication</c>.
            /// </summary>
            public WotPreparedViewGraphState? ViewGraph { get; } = graph;
            /// <summary>
            /// Gets the is committed for <c>PreparedPublication</c>.
            /// </summary>
            public bool IsCommitted => batch.IsCommitted;
            /// <summary>
            /// Gets or sets the cleanup failure for <c>PreparedPublication</c>.
            /// </summary>
            public Exception? CleanupFailure { get; private set; }

            public void BindReadImages(ArrayOf<INodeManagerReadImage> images)
            {
                batch.BindReadImages(images);
            }

            /// <param name="decideAsync">The decide async.</param>
            /// <param name="publishCommittedState">The publish committed state.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
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
