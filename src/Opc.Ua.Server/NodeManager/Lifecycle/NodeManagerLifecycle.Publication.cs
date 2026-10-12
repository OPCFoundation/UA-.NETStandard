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

namespace Opc.Ua.Server
{
    public sealed partial class NodeManagerLifecycle
    {
        /// <inheritdoc/>
        public bool SupportsPublicationIsolation => true;

        /// <inheritdoc/>
        public INodeManagerPublicationCapture CapturePublication()
        {
            (IServerInternal server, IDynamicNodeManagerHost host) = GetRunningServer(allowRequestCallback: true);
            if (host is not IDynamicNodeManagerBatchHost batchHost || server.Factory is not EncodeableFactory factory)
            {
                throw new NotSupportedException("The server cannot capture publication revisions.");
            }
            using IDisposable routing = batchHost.UseLiveRouting();
            _ = server.TypeTree.CaptureSnapshot(out TypeTable types, out long typeRevision);
            _ = factory.CaptureSnapshot(out EncodeableFactory originalFactory, out long factoryRevision);
            return new PublicationCapture(
                this, server, host, batchHost, batchHost.RoutingRevision, types, typeRevision,
                originalFactory, factoryRevision);
        }

        /// <summary>
        /// Begins a publication invocation for a capture if no other lifecycle operation owns admission and the
        /// captured revisions are still current.
        /// </summary>
        /// <param name="capture">The capture the invocation belongs to.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>The publication invocation.</returns>
        private PublicationInvocation BeginPublication(PublicationCapture capture, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_operationLifetimeLock)
            {
                int ancestors = 0;
                for (OperationLifetime? operation = m_currentOperation.Value; operation is not null;
                    operation = operation.Parent)
                {
                    if (operation.IsOwnedBy(this))
                    {
                        ancestors++;
                    }
                }
                if (m_publicationCapture is not null || m_activeLifecycleOperations != ancestors)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadServerTooBusy, "Another lifecycle operation owns publication admission.");
                }
                EnsureSameRunningServer(capture.Server, capture.OwnerHost, allowRequestCallback: true);
                using IDisposable routing = capture.Host.UseLiveRouting();
                _ = ((EncodeableFactory)capture.Server.Factory).CaptureSnapshot(
                    out EncodeableFactory factory, out long factoryRevision);
                bool current = ReferenceEquals(capture.Host.RoutingRevision, capture.RoutingRevision) &&
                    capture.Server.TypeTree.IsCurrentSnapshot(capture.Types, capture.TypeRevision) &&
                    ReferenceEquals(factory, capture.Factory) && factoryRevision == capture.FactoryRevision;
                var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_publicationCapture = capture;
                m_publicationFinished = finished;
                try
                {
                    OperationLifetime operation = EnterLifecycleOperation(capture);
                    return new PublicationInvocation(this, capture, current, finished, operation);
                }
                catch
                {
                    m_publicationCapture = null;
                    m_publicationFinished = null;
                    throw;
                }
            }
        }

        /// <summary>
        /// Enters shutdown once no publication invocation owns admission.
        /// </summary>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>A task that completes when the active operations have drained.</returns>
        private async ValueTask<Task> EnterShutdownAfterPublicationAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                Task finished;
                lock (m_operationLifetimeLock)
                {
                    if (m_publicationCapture is null)
                    {
                        return EnterShutdownMethod();
                    }
                    finished = m_publicationFinished!.Task;
                }
                await finished.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Immutable record of the routing, type and factory revisions captured for a publication.
        /// </summary>
        /// <param name="Owner">The lifecycle that created the capture.</param>
        /// <param name="Server">The running server.</param>
        /// <param name="OwnerHost">The dynamic node manager host.</param>
        /// <param name="Host">The batch host of the node manager.</param>
        /// <param name="RoutingRevision">The captured routing revision.</param>
        /// <param name="Types">The captured type tree.</param>
        /// <param name="TypeRevision">The revision of the captured type tree.</param>
        /// <param name="Factory">The captured encodeable factory.</param>
        /// <param name="FactoryRevision">The revision of the captured factory.</param>
        private sealed record PublicationCapture(
            NodeManagerLifecycle Owner,
            IServerInternal Server,
            IDynamicNodeManagerHost OwnerHost,
            IDynamicNodeManagerBatchHost Host,
            NodeManagerRoutingTable.RoutingSnapshot RoutingRevision,
            TypeTable Types,
            long TypeRevision,
            EncodeableFactory Factory,
            long FactoryRevision) : INodeManagerPublicationCapture
        {
            /// <inheritdoc/>
            public ValueTask<INodeManagerPublication> BeginAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask<INodeManagerPublication>(Owner.BeginPublication(this, cancellationToken));
            }
        }

        /// <summary>
        /// Admitted publication that prepares and owns batches until it is disposed.
        /// </summary>
        /// <param name="owner">The lifecycle that admitted the publication.</param>
        /// <param name="capture">The capture the publication belongs to.</param>
        /// <param name="current">Whether the captured revisions were still current at admission.</param>
        /// <param name="finished">The completion source set when the publication ends.</param>
        /// <param name="operation">The lifecycle operation held for the publication.</param>
        private sealed class PublicationInvocation(
            NodeManagerLifecycle owner,
            PublicationCapture capture,
            bool current,
            TaskCompletionSource<bool> finished,
            OperationLifetime operation) : INodeManagerValidationPublication
        {
            /// <summary>
            /// Gets whether the captured revisions were current when the publication was admitted.
            /// </summary>
            public bool IsCurrent { get; } = current;
            /// <summary>
            /// Gets the capture the publication belongs to.
            /// </summary>
            public PublicationCapture Capture { get; } = capture;
            /// <summary>
            /// Gets a task that completes when the publication ends.
            /// </summary>
            public Task Finished => m_finished.Task;

            /// <inheritdoc/>
            public ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
                ArrayOf<NodeManagerBatchChange> changes, CancellationToken cancellationToken = default)
            {
                return PrepareCoreAsync(changes, default, cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask<IPreparedNodeManagerBatch> PrepareReadImagesAsync(
                ArrayOf<INodeManagerReadImage> images, CancellationToken cancellationToken = default)
            {
                if (images.Count == 0)
                {
                    throw new ArgumentException("A read publication must contain at least one image.", nameof(images));
                }
                return PrepareCoreAsync([], images, cancellationToken);
            }

            /// <inheritdoc/>
            public async ValueTask ValidateAsync(
                ArrayOf<NodeManagerBatchChange> changes,
                Func<IPreparedNodeManagerBatch, CancellationToken, ValueTask> inspectAsync,
                CancellationToken cancellationToken = default)
            {
                _ = inspectAsync ?? throw new ArgumentNullException(nameof(inspectAsync));
                using IDisposable namespaces = Capture.Server.NamespaceUris.UsePrivateCopy();
                using IDisposable servers = Capture.Server.ServerUris.UsePrivateCopy();
                IPreparedNodeManagerBatch batch = await PrepareCoreAsync(
                    changes, default, cancellationToken, validationOnly: true)
                    .ConfigureAwait(false);
                await using var lifetime = batch.ConfigureAwait(false);
                if (batch is not PreparedBatch candidate)
                {
                    throw new InvalidOperationException("Validation requires the captured lifecycle batch owner.");
                }
                using (((IDynamicNodeManagerBatchHost)candidate.Host).UseTypeImage(
                    candidate.TypeTree, candidate.Factory))
                {
                    await inspectAsync(batch, cancellationToken).ConfigureAwait(false);
                }
                await candidate.ValidateAsync(cancellationToken).ConfigureAwait(false);
            }

            /// <summary>
            /// Prepares a batch while ensuring that the invocation is current and prepares sequentially.
            /// </summary>
            /// <param name="changes">The changes of the unit.</param>
            /// <param name="images">The read images of the unit.</param>
            /// <param name="cancellationToken">The token used to cancel the operation.</param>
            /// <param name="validationOnly">Whether the batch is a validation candidate.</param>
            /// <returns>The prepared batch.</returns>
            private async ValueTask<IPreparedNodeManagerBatch> PrepareCoreAsync(
                ArrayOf<NodeManagerBatchChange> changes,
                ArrayOf<INodeManagerReadImage> images,
                CancellationToken cancellationToken,
                bool validationOnly = false)
            {
                lock (owner.m_operationLifetimeLock)
                {
                    if (m_closing || !ReferenceEquals(owner.m_publicationCapture, Capture) || !IsCurrent)
                    {
                        throw new InvalidOperationException("The publication invocation is stale or closed.");
                    }
                    if (m_preparing)
                    {
                        throw new InvalidOperationException("Prepare invocation units sequentially.");
                    }
                    m_preparing = true;
                    m_prepared = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                try
                {
                    IPreparedNodeManagerBatch batch = await owner.PrepareBatchAsync(
                        changes, this, cancellationToken, images, validationOnly).ConfigureAwait(false);
                    lock (owner.m_operationLifetimeLock)
                    {
                        m_batches.Add(batch);
                    }
                    return batch;
                }
                finally
                {
                    lock (owner.m_operationLifetimeLock)
                    {
                        m_preparing = false;
                        m_prepared.TrySetResult(true);
                    }
                }
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                Task? prepared;
                bool ownsDisposal;
                lock (owner.m_operationLifetimeLock)
                {
                    ownsDisposal = !m_closing;
                    m_closing = true;
                    prepared = m_preparing ? m_prepared.Task : null;
                }
                if (!ownsDisposal)
                {
                    await Finished.ConfigureAwait(false);
                    return;
                }
                try
                {
                    if (prepared is not null)
                    {
                        await prepared.ConfigureAwait(false);
                    }
                    var failures = new List<Exception>();
                    foreach (IPreparedNodeManagerBatch batch in m_batches)
                    {
                        try
                        {
                            await batch.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception failure) when (failure is not OutOfMemoryException)
                        {
                            failures.Add(failure);
                        }
                    }
                    if (failures.Count != 0)
                    {
                        throw new AggregateException("Publication invocation cleanup failed.", failures);
                    }
                }
                finally
                {
                    lock (owner.m_operationLifetimeLock)
                    {
                        owner.m_publicationCapture = null;
                        owner.m_publicationFinished = null;
                    }
                    m_operation.Dispose();
                    m_finished.TrySetResult(true);
                    owner.ScheduleRetiredGenerationDrainCleanup();
                }
            }

            private readonly List<IPreparedNodeManagerBatch> m_batches = [];
            private readonly OperationLifetime m_operation = operation;
            private readonly TaskCompletionSource<bool> m_finished = finished;
            private TaskCompletionSource<bool> m_prepared =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private bool m_preparing;
            private bool m_closing;
        }

        private readonly AsyncLocal<OperationLifetime?> m_currentOperation = new();
        private PublicationCapture? m_publicationCapture;
        private TaskCompletionSource<bool>? m_publicationFinished;
    }
}
