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
            public ValueTask<INodeManagerPublication> BeginAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask<INodeManagerPublication>(Owner.BeginPublication(this, cancellationToken));
            }
        }

        private sealed class PublicationInvocation(
            NodeManagerLifecycle owner,
            PublicationCapture capture,
            bool current,
            TaskCompletionSource<bool> finished,
            OperationLifetime operation) : INodeManagerPublication
        {
            public bool IsCurrent { get; } = current;
            public PublicationCapture Capture { get; } = capture;
            public Task Finished => m_finished.Task;

            public ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
                ArrayOf<NodeManagerBatchChange> changes, CancellationToken cancellationToken = default)
            {
                return PrepareCoreAsync(changes, default, cancellationToken);
            }

            public ValueTask<IPreparedNodeManagerBatch> PrepareReadImagesAsync(
                ArrayOf<INodeManagerReadImage> images, CancellationToken cancellationToken = default)
            {
                if (images.Count == 0)
                {
                    throw new ArgumentException("A read publication must contain at least one image.", nameof(images));
                }
                return PrepareCoreAsync([], images, cancellationToken);
            }

            private async ValueTask<IPreparedNodeManagerBatch> PrepareCoreAsync(
                ArrayOf<NodeManagerBatchChange> changes,
                ArrayOf<INodeManagerReadImage> images,
                CancellationToken cancellationToken)
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
                        changes, this, cancellationToken, images).ConfigureAwait(false);
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
