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
    /// <see cref="RuntimeNodeSetLifecycleExtensions.AddRuntimeNodeSetAsync(
    /// INodeManagerLifecycle, RuntimeNodeSetOptions, CancellationToken)"/>;
    /// updates use
    /// <see cref="RuntimeNodeSetLifecycleExtensions.ShadowReloadRuntimeNodeSetAsync"/>,
    /// migrating compatible monitored items and invalidating items unavailable
    /// in the replacement. The previous generation is not retained for future
    /// requests or continuations. The stable WoT registry NodeManager is never
    /// touched.
    /// </summary>
    public sealed partial class LifecycleWotProjectionHost
    {
        /// <inheritdoc/>
        public bool SupportsPreparedPublication => m_lifecycle.SupportsPreparedPublication;

        /// <summary>
        /// Gets the atomicity modes this actual source owner can isolate.
        /// </summary>
        /// <inheritdoc/>
        public ArrayOf<WoTAtomicityEnum> SupportedAtomicities =>
            m_lifecycle.SupportsPublicationIsolation
                ? [WoTAtomicityEnum.PerResource, WoTAtomicityEnum.PerGroup,
                    WoTAtomicityEnum.PerClosure, WoTAtomicityEnum.PerRegistry]
                : [];

        /// <summary>
        /// Captures source-owner revisions before input acquisition and conversion.
        /// </summary>
        /// <inheritdoc/>
        public IWotProjectionPublicationCapture CapturePublication()
        {
            return m_lifecycle.SupportsPublicationIsolation
                ? new PublicationCapture(this, m_lifecycle.CapturePublication())
                : throw new NotSupportedException("The lifecycle cannot isolate a publication invocation.");
        }

        /// <inheritdoc/>
        /// <param name="changes">The source projection changes to prepare.</param>
        /// <param name="views">The WoT prepared view publication.</param>
        /// <param name="callerContext">The executing request's context, or <c>null</c>.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        public ValueTask<IWotPreparedProjectionPublication> PrepareAsync(
            ArrayOf<WotProjectionChange> changes,
            IWotPreparedViewPublication? views,
            IOperationContext? callerContext,
            CancellationToken cancellationToken)
        {
            return PrepareCoreAsync(changes, views, null, callerContext, cancellationToken);
        }

        private async ValueTask<IWotPreparedProjectionPublication> PrepareCoreAsync(
            ArrayOf<WotProjectionChange> changes,
            IWotPreparedViewPublication? views,
            INodeManagerPublication? publication,
            IOperationContext? callerContext,
            CancellationToken cancellationToken)
        {
            if (!m_lifecycle.SupportsPreparedPublication)
            {
                throw new NotSupportedException("The lifecycle cannot prepare an aggregate projection publication.");
            }
            PreparedChanges candidate = CreatePreparedChanges(changes, views);
            IPreparedNodeManagerBatch? batch = publication is null
                ? await m_lifecycle.PrepareAsync(candidate.Changes, callerContext, cancellationToken)
                    .ConfigureAwait(false)
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
            if (!publication.IsCurrent)
            {
                throw new InvalidOperationException("A stale publication cannot validate a candidate.");
            }
            PreparedChanges candidate = CreatePreparedChanges(changes, views);
            await publication.ValidateAsync(candidate.Changes, async (batch, token) =>
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
            return new PreparedPublication(
                batch, handles.ToArrayOf(), graph, candidate.RuntimePublications, candidate.SourceImage?.NamespaceUris);
        }

        private PreparedChanges CreatePreparedChanges(
            ArrayOf<WotProjectionChange> changes, IWotPreparedViewPublication? views)
        {
            var lifecycleChanges = new List<NodeManagerBatchChange>();
            var documents = new List<WotProjectionDocument>();
            var runtimePublications = new List<WotProjectionRuntimePublication>();
            var sourceImage = new WotPreparedSourceImage();
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
                    sourceImage.Exclude(current.NodeManager);
                }
                NodeManagerMonitoredItemTransitionMode transitionMode =
                    change.RetirementPolicy == WotProjectionRetirementPolicy.Invalidate
                        ? NodeManagerMonitoredItemTransitionMode.Invalidate
                        : NodeManagerMonitoredItemTransitionMode.Migrate;
                if (change.Document is null)
                {
                    lifecycleChanges.Add(NodeManagerBatchChange.Remove(
                        current ?? throw new ArgumentException("Retirement has no current owner.", nameof(changes))));
                    continue;
                }
                RuntimeNodeSetOptions options = BuildOptions(change.Document);
                runtimePublications.Add(new WotProjectionRuntimePublication(options));
                IAsyncNodeManagerFactory factory = sourceImage.Capture(new RuntimeNodeSetNodeManagerFactory(options));
                lifecycleChanges.Add(current is null
                    ? NodeManagerBatchChange.Add(factory)
                    : NodeManagerBatchChange.Replace(current, factory, transitionMode));
                documents.Add(change.Document);
            }
            if (views is IWotPreparedViewSourceConsumer consumer)
            {
                consumer.BindSourceImage(sourceImage);
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
            /// <summary>
            /// Begins a publication invocation for the executing request on the captured source-owner revisions.
            /// </summary>
            /// <param name="callerContext">The executing request's context, or <c>null</c>.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public async ValueTask<IWotProjectionPublication> BeginAsync(
                IOperationContext? callerContext,
                CancellationToken cancellationToken)
            {
                INodeManagerPublication invocation = await capture.BeginAsync(callerContext, cancellationToken)
                    .ConfigureAwait(false);
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

            /// <summary>
            /// Prepares one unit on the captured publication.
            /// </summary>
            /// <param name="changes">The source projection changes to prepare.</param>
            /// <param name="views">The WoT prepared view publication.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask<IWotPreparedProjectionPublication> PrepareAsync(
                ArrayOf<WotProjectionChange> changes,
                IWotPreparedViewPublication? views = null,
                CancellationToken cancellationToken = default)
            {
                return owner.PrepareCoreAsync(changes, views, publication, null, cancellationToken);
            }

            /// <summary>
            /// Prepares a read-metadata-only unit on the captured publication.
            /// </summary>
            /// <param name="images">The read images to prepare.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public async ValueTask<IWotPreparedProjectionPublication> PrepareReadImagesAsync(
                ArrayOf<INodeManagerReadImage> images,
                CancellationToken cancellationToken = default)
            {
                IPreparedNodeManagerBatch batch = await publication.PrepareReadImagesAsync(images, cancellationToken)
                    .ConfigureAwait(false);
                return new PreparedPublication(batch, [], null, []);
            }

            /// <summary>
            /// Prepares and validates a private candidate, passing it to the inspection callback before release.
            /// </summary>
            /// <param name="changes">The source projection changes to validate.</param>
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
            ArrayOf<WotProjectionRuntimePublication> runtimePublications,
            NamespaceTable? namespaceUris = null)
            : IWotPreparedProjectionPublication, IWotPreparedProjectionNamespaceSource
        {
            /// <inheritdoc/>
            public ArrayOf<string> CapturedNamespaceUris => namespaceUris?.ToArrayOf() ?? [];

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

            /// <summary>
            /// Commits the prepared batch after the caller's decision and publishes the committed state.
            /// </summary>
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
