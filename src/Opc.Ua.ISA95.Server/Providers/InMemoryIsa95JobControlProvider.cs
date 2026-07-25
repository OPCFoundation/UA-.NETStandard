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

#pragma warning disable IDE0005 // Imports are required by target frameworks without matching implicit global usings.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;
#pragma warning restore IDE0005

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// A deterministic, thread-safe, non-durable in-memory implementation of the
    /// ISA-95 Job Control provider facets for both V1 and V2. All facets operate
    /// over a single version-neutral store, so V1 and V2 callers observe the same
    /// orders and responses. Expected failures are mapped to precise non-Good
    /// <see cref="ServiceResult"/> values (and the V1 return enumeration) and are
    /// never logged or silently swallowed.
    /// </summary>
    public sealed class InMemoryIsa95JobControlProvider :
        IIsa95JobOrderReceiverV1,
        IIsa95JobResponseProviderV1,
        IIsa95JobResponseReceiverV1,
        IIsa95JobOrderReceiverV2,
        IIsa95JobResponseProviderV2,
        IIsa95JobResponseReceiverV2,
        IIsa95JobStatusSourceV2,
        IIsa95JobExecutionController,
        IIsa95JobOrderCatalog,
        IIsa95JobOrderCatalogChangeSource,
        IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="InMemoryIsa95JobControlProvider"/> class.
        /// </summary>
        /// <param name="options">
        /// The bounded options, or <c>null</c> to use the defaults.
        /// </param>
        /// <param name="timeProvider">
        /// The time provider used for response timestamps and retention, or
        /// <c>null</c> to use <see cref="TimeProvider.System"/>.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when an option value is out of range.
        /// </exception>
        public InMemoryIsa95JobControlProvider(
            Isa95JobControlProviderOptions? options = null,
            TimeProvider? timeProvider = null)
        {
            options ??= new Isa95JobControlProviderOptions();
            options.Validate();
            m_options = new Isa95JobControlProviderOptions
            {
                MaxJobOrders = options.MaxJobOrders,
                MaxJobResponses = options.MaxJobResponses,
                ResponseRetention = options.ResponseRetention
            };
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public ushort MaxDownloadableJobOrders =>
            (ushort)Math.Min(ushort.MaxValue, m_options.MaxJobOrders);

        /// <inheritdoc/>
        public ValueTask<ArrayOf<V1.ISA95JobOrderDataType>> GetJobOrdersV1Async(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                ThrowIfDisposed();
                var orders = new List<V1.ISA95JobOrderDataType>(m_orders.Count);
                foreach (JobEntry entry in m_orders.Values)
                {
                    orders.Add(Isa95JobControlConversions.ToV1Order(entry.Order));
                }
                return new ValueTask<ArrayOf<V1.ISA95JobOrderDataType>>(
                    orders.ToArrayOf());
            }
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<V2.ISA95JobOrderAndStateDataType>> GetJobOrdersV2Async(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                ThrowIfDisposed();
                var orders = new List<V2.ISA95JobOrderAndStateDataType>(m_orders.Count);
                foreach (JobEntry entry in m_orders.Values)
                {
                    orders.Add(new V2.ISA95JobOrderAndStateDataType
                    {
                        JobOrder = Isa95JobControlConversions.ToV2Order(entry.Order),
                        State = Isa95JobControlConversions.ToV2StateArray(entry.State)
                    });
                }
                return new ValueTask<ArrayOf<V2.ISA95JobOrderAndStateDataType>>(
                    orders.ToArrayOf());
            }
        }

        /// <inheritdoc/>
        public ValueTask<Isa95JobOrderReceiptV1> ReceiveJobOrderAsync(
            V1.ISA95JobOrderCommandEnum command,
            V1.ISA95JobOrderDataType jobOrder,
            CancellationToken cancellationToken = default)
        {
            if (jobOrder == null)
            {
                throw new ArgumentNullException(nameof(jobOrder));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            if (!Isa95JobControlConversions.TryMapV1Command(command, out Isa95JobOperation operation))
            {
                return new ValueTask<Isa95JobOrderReceiptV1>(new Isa95JobOrderReceiptV1
                {
                    Result = BusinessFailure(
                        "The job order command is not supported."),
                    ReturnStatus = Isa95JobReturnStatus.InvalidCommand
                });
            }

            EngineResult result = ApplyOperation(operation, Isa95JobControlConversions.FromV1Order(jobOrder), []);
            return new ValueTask<Isa95JobOrderReceiptV1>(new Isa95JobOrderReceiptV1
            {
                Result = result.Result,
                ReturnStatus = ToV1ReturnStatus(result.ReturnStatus)
            });
        }

        /// <inheritdoc/>
        public ValueTask<Isa95JobResponseQueryV1> RequestJobResponseAsync(
            string? jobOrderId,
            V1.ISA95JobOrderStateEnum state,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            bool hasOrderSelector = !string.IsNullOrEmpty(jobOrderId);
            bool hasStateSelector = state != V1.ISA95JobOrderStateEnum.Undefined;

            if (hasStateSelector &&
                ((int)state < (int)V1.ISA95JobOrderStateEnum.Waiting ||
                    (int)state > (int)V1.ISA95JobOrderStateEnum.Error))
            {
                return new ValueTask<Isa95JobResponseQueryV1>(
                    new Isa95JobResponseQueryV1
                    {
                        Result = BusinessFailure("The job-order state is invalid."),
                        ReturnStatus = Isa95JobReturnStatus.InvalidStatus,
                        Responses = []
                    });
            }

            // OPC-10031-4 V1 RequestJobResponse selects responses by exactly one of
            // the job order identifier or the job order state; specifying both or
            // neither is an invalid request.
            if (hasOrderSelector == hasStateSelector)
            {
                return new ValueTask<Isa95JobResponseQueryV1>(
                    new Isa95JobResponseQueryV1
                    {
                        Result = BusinessFailure(
                            "Exactly one of the job order identifier or the job order state must be specified."),
                        ReturnStatus = Isa95JobReturnStatus.InvalidRequest,
                        Responses = []
                    });
            }

            (EngineResult result, List<Isa95JobResponse> responses) = hasOrderSelector
                ? RequestResponses(jobOrderId, filter: null)
                : RequestResponsesByPredicate(
                    Isa95JobControlConversions.FromV1State(state));

            var projected = new List<V1.ISA95JobResponseDataType>(responses.Count);
            foreach (Isa95JobResponse response in responses)
            {
                projected.Add(Isa95JobControlConversions.ToV1Response(response));
            }

            return new ValueTask<Isa95JobResponseQueryV1>(new Isa95JobResponseQueryV1
            {
                Result = result.Result,
                ReturnStatus = result.ReturnStatus,
                Responses = projected.ToArrayOf()
            });
        }

        /// <inheritdoc/>
        public ValueTask<Isa95JobResponseReceiptV1> ReceiveJobResponseAsync(
            V1.ISA95JobResponseDataType response,
            CancellationToken cancellationToken = default)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            if (response.JobState == V1.ISA95JobOrderStateEnum.Undefined)
            {
                return new ValueTask<Isa95JobResponseReceiptV1>(
                    new Isa95JobResponseReceiptV1
                    {
                        Result = BusinessFailure("The job-order state is required."),
                        ReturnStatus = Isa95JobReturnStatus.InvalidStatus
                    });
            }

            EngineResult result = ReceiveResponse(Isa95JobControlConversions.FromV1Response(response));
            return new ValueTask<Isa95JobResponseReceiptV1>(new Isa95JobResponseReceiptV1
            {
                Result = result.Result,
                ReturnStatus = result.ReturnStatus
            });
        }

        /// <inheritdoc/>
        public ValueTask<Isa95JobOrderReceiptV2> ReceiveJobOrderAsync(
            Isa95JobOrderOperationV2 operation,
            V2.ISA95JobOrderDataType jobOrder,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken cancellationToken = default)
        {
            if (jobOrder == null)
            {
                throw new ArgumentNullException(nameof(jobOrder));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            if ((int)operation is < ((int)Isa95JobOrderOperationV2.Store) or
                > ((int)Isa95JobOrderOperationV2.RevokeStart))
            {
                return new ValueTask<Isa95JobOrderReceiptV2>(
                    new Isa95JobOrderReceiptV2
                    {
                        Result = BusinessFailure("The job-order operation is invalid."),
                        ReturnStatus = Isa95JobReturnStatus.InvalidRequest
                    });
            }

            EngineResult result = ApplyOperation(
                Isa95JobControlConversions.MapV2Operation(operation),
                Isa95JobControlConversions.FromV2Order(jobOrder),
                comment);
            return new ValueTask<Isa95JobOrderReceiptV2>(new Isa95JobOrderReceiptV2
            {
                Result = result.Result,
                ReturnStatus = result.ReturnStatus
            });
        }

        /// <inheritdoc/>
        public ValueTask<Isa95JobResponseByIdResultV2> RequestJobResponseByJobOrderIdAsync(
            string jobOrderId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            (EngineResult result, Isa95JobResponse? response) =
                RequestLatestResponse(jobOrderId);
            return new ValueTask<Isa95JobResponseByIdResultV2>(
                new Isa95JobResponseByIdResultV2
                {
                    Result = result.Result,
                    ReturnStatus = result.ReturnStatus,
                    Response = response is null
                        ? null
                        : Isa95JobControlConversions.ToV2Response(response)
                });
        }

        /// <inheritdoc/>
        public ValueTask<Isa95JobResponsesByStateResultV2> RequestJobResponsesByStateAsync(
            ArrayOf<V2.ISA95StateDataType> state,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            if (state.Count == 0)
            {
                return new ValueTask<Isa95JobResponsesByStateResultV2>(
                    new Isa95JobResponsesByStateResultV2
                    {
                        Result = BusinessFailure("A state filter is required."),
                        ReturnStatus = Isa95JobReturnStatus.InvalidStatus,
                        Responses = []
                    });
            }
            if (Isa95V2StateMachine.QueryTopLevelNumber(state) == 0)
            {
                return new ValueTask<Isa95JobResponsesByStateResultV2>(
                    new Isa95JobResponsesByStateResultV2
                    {
                        Result = BusinessFailure("The state filter is invalid."),
                        ReturnStatus = Isa95JobReturnStatus.InvalidStatus,
                        Responses = []
                    });
            }

            (EngineResult result, List<Isa95JobResponse> responses) =
                RequestResponsesByStateQuery(state);
            var projected = new List<V2.ISA95JobResponseDataType>(responses.Count);
            foreach (Isa95JobResponse response in responses)
            {
                projected.Add(Isa95JobControlConversions.ToV2Response(response));
            }

            return new ValueTask<Isa95JobResponsesByStateResultV2>(
                new Isa95JobResponsesByStateResultV2
                {
                    Result = result.Result,
                    ReturnStatus = result.ReturnStatus,
                    Responses = projected.ToArrayOf()
                });
        }

        /// <inheritdoc/>
        public ValueTask<Isa95JobResponseReceiptV2> ReceiveJobResponseAsync(
            V2.ISA95JobResponseDataType response,
            CancellationToken cancellationToken = default)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            if (response.JobState.Count == 0)
            {
                return new ValueTask<Isa95JobResponseReceiptV2>(
                    new Isa95JobResponseReceiptV2
                    {
                        Result = BusinessFailure("The job-order state is required."),
                        ReturnStatus = Isa95JobReturnStatus.InvalidStatus
                    });
            }

            EngineResult result = ReceiveResponse(Isa95JobControlConversions.FromV2Response(response));
            return new ValueTask<Isa95JobResponseReceiptV2>(new Isa95JobResponseReceiptV2
            {
                Result = result.Result,
                ReturnStatus = result.ReturnStatus
            });
        }

        /// <inheritdoc/>
        public ValueTask<Isa95JobOrderReceiptV2> TransitionAsync(
            string jobOrderId,
            Isa95JobExecutionTransition transition,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            EngineResult result = ApplyExecutionTransition(jobOrderId, transition);
            return new ValueTask<Isa95JobOrderReceiptV2>(new Isa95JobOrderReceiptV2
            {
                Result = result.Result,
                ReturnStatus = result.ReturnStatus
            });
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<Isa95JobStatusNotificationV2> SubscribeAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var subscription = new Subscription<Isa95JobStatusNotificationV2>();
            lock (m_lock)
            {
                ThrowIfDisposed();
                m_subscribers.Add(subscription);
            }

            try
            {
                while (true)
                {
                    bool cancelled = false;
                    try
                    {
                        await subscription.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }

                    if (cancelled)
                    {
                        yield break;
                    }

                    if (!subscription.TryDequeue(out Isa95JobStatusNotificationV2? notification))
                    {
                        yield break;
                    }

                    yield return notification;
                }
            }
            finally
            {
                lock (m_lock)
                {
                    m_subscribers.Remove(subscription);
                }
                subscription.Dispose();
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<Isa95JobOrderCatalogChange> SubscribeCatalogChangesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var subscription = new Subscription<Isa95JobOrderCatalogChange>();
            lock (m_lock)
            {
                ThrowIfDisposed();
                m_catalogSubscribers.Add(subscription);
            }

            try
            {
                while (true)
                {
                    bool cancelled = false;
                    try
                    {
                        await subscription.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }

                    if (cancelled)
                    {
                        yield break;
                    }

                    if (!subscription.TryDequeue(out Isa95JobOrderCatalogChange change))
                    {
                        yield break;
                    }

                    yield return change;
                }
            }
            finally
            {
                lock (m_lock)
                {
                    m_catalogSubscribers.Remove(subscription);
                }
                subscription.Dispose();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            List<Subscription<Isa95JobStatusNotificationV2>> subscribers;
            List<Subscription<Isa95JobOrderCatalogChange>> catalogSubscribers;
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                subscribers = [.. m_subscribers];
                catalogSubscribers = [.. m_catalogSubscribers];
                m_subscribers.Clear();
                m_catalogSubscribers.Clear();
                m_orders.Clear();
                m_responses.Clear();
            }

            foreach (Subscription<Isa95JobStatusNotificationV2> subscriber in subscribers)
            {
                subscriber.Complete();
            }
            foreach (Subscription<Isa95JobOrderCatalogChange> subscriber in catalogSubscribers)
            {
                subscriber.Complete();
            }
        }

        private EngineResult ApplyOperation(
            Isa95JobOperation operation,
            Isa95JobOrder order,
            ArrayOf<LocalizedText> comment)
        {
            string id = order.Id;
            if (string.IsNullOrEmpty(id))
            {
                return Failure(
                    Isa95JobReturnStatus.InvalidRequest,
                    "The job order identifier is required.");
            }

            lock (m_lock)
            {
                ThrowIfDisposed();
                bool exists = m_orders.TryGetValue(id, out JobEntry? entry);

                switch (operation)
                {
                    case Isa95JobOperation.Store:
                        return Create(id, order, exists, Isa95JobCanonicalState.NotAllowedToStart, comment);
                    case Isa95JobOperation.StoreAndStart:
                        return Create(id, order, exists, Isa95JobCanonicalState.AllowedToStart, comment);
                    case Isa95JobOperation.Start:
                        return Transition(
                            entry,
                            exists,
                            static state => state == Isa95JobCanonicalState.NotAllowedToStart,
                            Isa95JobCanonicalState.AllowedToStart,
                            comment);
                    case Isa95JobOperation.Update:
                        return Update(entry, exists, order, comment);
                    case Isa95JobOperation.Stop:
                        return Transition(
                            entry,
                            exists,
                            static state => state is Isa95JobCanonicalState.Running
                                or Isa95JobCanonicalState.Held
                                or Isa95JobCanonicalState.Suspended,
                            Isa95JobCanonicalState.Completed,
                            comment);
                    case Isa95JobOperation.StopAndRemove:
                        return StopAndRemoveStarted(id, entry, exists);
                    case Isa95JobOperation.Cancel:
                        return RemoveNotStarted(id, entry, exists);
                    case Isa95JobOperation.Clear:
                        return Clear(id, entry, exists);
                    case Isa95JobOperation.Pause:
                        return Transition(
                            entry,
                            exists,
                            static state => state == Isa95JobCanonicalState.Running,
                            Isa95JobCanonicalState.Suspended,
                            comment);
                    case Isa95JobOperation.Resume:
                        return Transition(
                            entry,
                            exists,
                            static state => state is Isa95JobCanonicalState.Held
                                or Isa95JobCanonicalState.Suspended,
                            Isa95JobCanonicalState.Running,
                            comment);
                    case Isa95JobOperation.Abort:
                        return Transition(
                            entry,
                            exists,
                            static state => !IsTerminal(state),
                            Isa95JobCanonicalState.Aborted,
                            comment);
                    case Isa95JobOperation.RevokeStart:
                        return Transition(
                            entry,
                            exists,
                            static state => state == Isa95JobCanonicalState.AllowedToStart,
                            Isa95JobCanonicalState.NotAllowedToStart,
                            comment);
                    default:
                        return Failure(
                            Isa95JobReturnStatus.InvalidRequest,
                            "The operation is not supported.");
                }
            }
        }

        private EngineResult ApplyExecutionTransition(
            string jobOrderId,
            Isa95JobExecutionTransition transition)
        {
            if (string.IsNullOrEmpty(jobOrderId))
            {
                return Failure(
                    Isa95JobReturnStatus.InvalidRequest,
                    "The job order identifier is required.");
            }
            lock (m_lock)
            {
                ThrowIfDisposed();
                bool exists = m_orders.TryGetValue(jobOrderId, out JobEntry? entry);
                return transition switch
                {
                    Isa95JobExecutionTransition.BeginExecution => Transition(
                        entry,
                        exists,
                        static state => state == Isa95JobCanonicalState.AllowedToStart,
                        Isa95JobCanonicalState.Running,
                        []),
                    Isa95JobExecutionTransition.Hold => Transition(
                        entry,
                        exists,
                        static state => state == Isa95JobCanonicalState.Running,
                        Isa95JobCanonicalState.Held,
                        []),
                    Isa95JobExecutionTransition.Complete => Transition(
                        entry,
                        exists,
                        static state => state is Isa95JobCanonicalState.Running
                            or Isa95JobCanonicalState.Held
                            or Isa95JobCanonicalState.Suspended,
                        Isa95JobCanonicalState.Completed,
                        []),
                    Isa95JobExecutionTransition.Close => Transition(
                        entry,
                        exists,
                        static state => state == Isa95JobCanonicalState.Completed,
                        Isa95JobCanonicalState.Closed,
                        []),
                    _ => Failure(
                        Isa95JobReturnStatus.InvalidRequest,
                        "The execution transition is not supported.")
                };
            }
        }

        private EngineResult Create(
            string id,
            Isa95JobOrder order,
            bool exists,
            Isa95JobCanonicalState state,
            ArrayOf<LocalizedText> comment)
        {
            if (exists)
            {
                return Failure(
                    Isa95JobReturnStatus.UnableToAccept,
                    "A job order with the same identifier exists.");
            }
            if (m_orders.Count >= m_options.MaxJobOrders)
            {
                return Failure(
                    Isa95JobReturnStatus.UnableToAccept,
                    "The job order capacity is exhausted.");
            }

            m_orders[id] = new JobEntry(order with { Comment = comment }, state);
            Emit(id, state);
            return EngineResult.Success;
        }

        private EngineResult Update(
            JobEntry? entry,
            bool exists,
            Isa95JobOrder order,
            ArrayOf<LocalizedText> comment)
        {
            if (!exists || entry is null)
            {
                return NotFound();
            }
            if (entry.State is not (Isa95JobCanonicalState.NotAllowedToStart or Isa95JobCanonicalState.AllowedToStart))
            {
                return InvalidState("The job order cannot be updated after it started.");
            }

            entry.Order = order with { Comment = comment.Count > 0 ? comment : entry.Order.Comment };
            SignalCatalog(
                entry.Order.Id,
                Isa95JobOrderCatalogChangeKind.Updated,
                ProjectOrderAndState(entry));
            // Update is a self-transition with ISA95JobOrderStatusEventType as
            // HasEffect in the standard model.
            Emit(entry.Order.Id, entry.State);
            return EngineResult.Success;
        }

        private EngineResult Clear(string id, JobEntry? entry, bool exists)
        {
            if (!exists || entry is null)
            {
                return NotFound();
            }
            if (!IsTerminal(entry.State))
            {
                return InvalidState("Only terminal job orders can be cleared.");
            }

            m_orders.Remove(id);
            SignalCatalog(id, Isa95JobOrderCatalogChangeKind.Removed, null);
            return EngineResult.Success;
        }

        private EngineResult RemoveNotStarted(string id, JobEntry? entry, bool exists)
        {
            if (!exists || entry is null)
            {
                return NotFound();
            }
            if (entry.State is not (
                Isa95JobCanonicalState.NotAllowedToStart or
                Isa95JobCanonicalState.AllowedToStart))
            {
                return InvalidState("Only a not-started job order can be cancelled.");
            }
            m_orders.Remove(id);
            SignalCatalog(id, Isa95JobOrderCatalogChangeKind.Removed, null);
            return EngineResult.Success;
        }

        private EngineResult StopAndRemoveStarted(string id, JobEntry? entry, bool exists)
        {
            if (!exists || entry is null)
            {
                return NotFound();
            }
            if (entry.State is not (
                Isa95JobCanonicalState.Running or
                Isa95JobCanonicalState.Held or
                Isa95JobCanonicalState.Suspended))
            {
                return InvalidState("Only a started job order can be stopped.");
            }

            // OPC-10031-4 V1 Stop reports on the work done and removes the stored
            // information; the order is not left behind in a terminal state.
            m_orders.Remove(id);
            SignalCatalog(id, Isa95JobOrderCatalogChangeKind.Removed, null);
            return EngineResult.Success;
        }

        private static V2.ISA95JobOrderAndStateDataType ProjectOrderAndState(JobEntry entry)
        {
            return new V2.ISA95JobOrderAndStateDataType
            {
                JobOrder = Isa95JobControlConversions.ToV2Order(entry.Order),
                State = Isa95JobControlConversions.ToV2StateArray(entry.State)
            };
        }

        private EngineResult Transition(
            JobEntry? entry,
            bool exists,
            Func<Isa95JobCanonicalState, bool> allowed,
            Isa95JobCanonicalState target,
            ArrayOf<LocalizedText> comment)
        {
            if (!exists || entry is null)
            {
                return NotFound();
            }
            if (!allowed(entry.State))
            {
                return InvalidState(
                    "The requested transition is not valid from the current state.");
            }

            entry.State = target;
            if (comment.Count > 0)
            {
                entry.Order = entry.Order with { Comment = comment };
            }
            Emit(entry.Order.Id, target);
            return EngineResult.Success;
        }

        private EngineResult ReceiveResponse(Isa95JobResponse response)
        {
            if (string.IsNullOrEmpty(response.Id))
            {
                return Failure(
                    Isa95JobReturnStatus.InvalidRequest,
                    "The job response identifier is required.");
            }
            if (string.IsNullOrEmpty(response.JobOrderId))
            {
                return Failure(
                    Isa95JobReturnStatus.InvalidRequest,
                    "The job order identifier is required.");
            }

            lock (m_lock)
            {
                ThrowIfDisposed();
                DateTimeUtc now = Now();
                PurgeExpired(now);

                if (m_responses.ContainsKey(response.Id))
                {
                    return Failure(
                        Isa95JobReturnStatus.InvalidRequest,
                        "A job response with the same identifier exists.");
                }
                if (m_responses.Count >= m_options.MaxJobResponses)
                {
                    return Failure(
                        Isa95JobReturnStatus.UnableToAccept,
                        "The job response capacity is exhausted.");
                }

                m_responses[response.Id] = response with { ReceivedAt = now };
                return EngineResult.Success;
            }
        }

        private (EngineResult Result, List<Isa95JobResponse> Responses) RequestResponses(
            string? jobOrderId,
            Isa95JobCanonicalState? filter)
        {
            var empty = new List<Isa95JobResponse>();
            if (string.IsNullOrEmpty(jobOrderId))
            {
                return (
                    Failure(
                        Isa95JobReturnStatus.InvalidRequest,
                        "The job order identifier is required."),
                    empty);
            }

            lock (m_lock)
            {
                ThrowIfDisposed();
                PurgeExpired(Now());

                bool known = m_orders.ContainsKey(jobOrderId!);
                var matches = new List<Isa95JobResponse>();
                foreach (Isa95JobResponse response in m_responses.Values)
                {
                    if (!string.Equals(response.JobOrderId, jobOrderId, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    known = true;
                    if (filter is null || response.State == filter.Value)
                    {
                        matches.Add(response);
                    }
                }

                if (!known)
                {
                    return (
                        NotFound(),
                        empty);
                }

                SortResponses(matches);
                return (EngineResult.Success, matches);
            }
        }

        private (EngineResult Result, List<Isa95JobResponse> Responses) RequestResponsesByPredicate(
            Isa95JobCanonicalState state)
        {
            lock (m_lock)
            {
                ThrowIfDisposed();
                PurgeExpired(Now());
                var matches = new List<Isa95JobResponse>();
                foreach (Isa95JobResponse response in m_responses.Values)
                {
                    if (response.State == state)
                    {
                        matches.Add(response);
                    }
                }
                SortResponses(matches);
                return (EngineResult.Success, matches);
            }
        }

        private (EngineResult Result, Isa95JobResponse? Response) RequestLatestResponse(
            string jobOrderId)
        {
            (EngineResult result, List<Isa95JobResponse> responses) =
                RequestResponses(jobOrderId, filter: null);
            if (result.ReturnStatus != Isa95JobReturnStatus.Success)
            {
                return (result, null);
            }
            if (responses.Count == 0)
            {
                return (
                    Failure(
                        Isa95JobReturnStatus.InvalidRequest,
                        "No job response is available for the job order."),
                    null);
            }
            return (EngineResult.Success, responses[^1]);
        }

        private (EngineResult Result, List<Isa95JobResponse> Responses)
            RequestResponsesByStateQuery(ArrayOf<V2.ISA95StateDataType> query)
        {
            lock (m_lock)
            {
                ThrowIfDisposed();
                PurgeExpired(Now());
                var matches = new List<Isa95JobResponse>();
                foreach (Isa95JobResponse response in m_responses.Values)
                {
                    if (Isa95V2StateMachine.Matches(response.State, query))
                    {
                        matches.Add(response);
                    }
                }
                SortResponses(matches);
                return (EngineResult.Success, matches);
            }
        }

        private static void SortResponses(List<Isa95JobResponse> responses)
        {
            responses.Sort(static (left, right) =>
            {
                int byTime = ((long)left.ReceivedAt).CompareTo((long)right.ReceivedAt);
                return byTime != 0 ? byTime : string.CompareOrdinal(left.Id, right.Id);
            });
        }

        private void PurgeExpired(DateTimeUtc now)
        {
            if (m_options.ResponseRetention <= TimeSpan.Zero || m_responses.Count == 0)
            {
                return;
            }

            List<string>? expired = null;
            foreach (KeyValuePair<string, Isa95JobResponse> entry in m_responses)
            {
                if (now - entry.Value.ReceivedAt > m_options.ResponseRetention)
                {
                    (expired ??= []).Add(entry.Key);
                }
            }
            if (expired is null)
            {
                return;
            }
            foreach (string key in expired)
            {
                m_responses.Remove(key);
            }
        }

        private void Emit(string id, Isa95JobCanonicalState state)
        {
            (uint number, string text) = Isa95V2StateMachine.TopLevel(state);
            ArrayOf<V2.ISA95StateDataType> stateArray =
                Isa95JobControlConversions.ToV2StateArray(state);
            JobEntry entry = m_orders[id];
            Isa95JobResponse? latestResponse = null;
            foreach (Isa95JobResponse response in m_responses.Values)
            {
                if (string.Equals(response.JobOrderId, id, StringComparison.Ordinal) &&
                    (latestResponse is null ||
                        (long)response.ReceivedAt >= (long)latestResponse.ReceivedAt))
                {
                    latestResponse = response;
                }
            }

            V2.ISA95JobResponseDataType jobResponse = latestResponse is null
                ? new V2.ISA95JobResponseDataType
                {
                    JobResponseID = string.Empty,
                    JobOrderID = id,
                    JobState = stateArray
                }
                : Isa95JobControlConversions.ToV2Response(latestResponse);

            // The notification advertises the current job order state, so the
            // carried job response must report the same state and never a stale one.
            jobResponse.JobState = stateArray;

            var notification = new Isa95JobStatusNotificationV2
            {
                JobOrderId = id,
                JobOrder = Isa95JobControlConversions.ToV2Order(entry.Order),
                JobResponse = jobResponse,
                Comment = entry.Order.Comment,
                StateNumber = number,
                StateText = new LocalizedText(text),
                State = stateArray,
                Timestamp = Now(),
                SequenceNumber = ++m_sequence
            };

            foreach (Subscription<Isa95JobStatusNotificationV2> subscriber in m_subscribers)
            {
                subscriber.Enqueue(notification);
            }
        }

        private void SignalCatalog(
            string id,
            Isa95JobOrderCatalogChangeKind kind,
            V2.ISA95JobOrderAndStateDataType? order)
        {
            var change = new Isa95JobOrderCatalogChange
            {
                JobOrderId = id,
                Kind = kind,
                Order = order,
                SequenceNumber = ++m_catalogSequence,
                Timestamp = Now()
            };

            foreach (Subscription<Isa95JobOrderCatalogChange> subscriber in m_catalogSubscribers)
            {
                subscriber.Enqueue(change);
            }
        }

        private DateTimeUtc Now()
        {
            return DateTimeUtc.From(m_timeProvider.GetUtcNow());
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(InMemoryIsa95JobControlProvider));
            }
        }

        private static EngineResult NotFound()
        {
            return Failure(
                Isa95JobReturnStatus.UnknownJobOrderId,
                "The job order is unknown.");
        }

        private static EngineResult InvalidState(string message)
        {
            return Failure(Isa95JobReturnStatus.InvalidStatus, message);
        }

        private static EngineResult Failure(ulong returnStatus, string message)
        {
            return new EngineResult(BusinessFailure(message), returnStatus);
        }

        private static ServiceResult BusinessFailure(string message)
        {
            return new ServiceResult(StatusCodes.Uncertain, new LocalizedText(message));
        }

        private static bool IsTerminal(Isa95JobCanonicalState state)
        {
            return state is Isa95JobCanonicalState.Completed
                or Isa95JobCanonicalState.Aborted
                or Isa95JobCanonicalState.Closed;
        }

        private static ulong ToV1ReturnStatus(ulong returnStatus)
        {
            if ((returnStatus & Isa95JobReturnStatus.InvalidStatus) != 0)
            {
                returnStatus &= ~Isa95JobReturnStatus.InvalidStatus;
                returnStatus |= Isa95JobReturnStatus.InvalidCommand;
            }
            return returnStatus;
        }

        private readonly Isa95JobControlProviderOptions m_options;
        private readonly TimeProvider m_timeProvider;
        private readonly Lock m_lock = new();
        private readonly Dictionary<string, JobEntry> m_orders = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Isa95JobResponse> m_responses = new(StringComparer.Ordinal);
        private readonly List<Subscription<Isa95JobStatusNotificationV2>> m_subscribers = [];
        private readonly List<Subscription<Isa95JobOrderCatalogChange>> m_catalogSubscribers = [];
        private ulong m_sequence;
        private ulong m_catalogSequence;
        private bool m_disposed;

        /// <summary>
        /// The outcome of an engine operation.
        /// </summary>
        private readonly record struct EngineResult(ServiceResult Result, ulong ReturnStatus)
        {
            public static EngineResult Success { get; } =
                new EngineResult(
                    ServiceResult.Good,
                    Isa95JobReturnStatus.Success);
        }

        /// <summary>
        /// A mutable store entry pairing a neutral job order with its canonical
        /// state.
        /// </summary>
        private sealed class JobEntry
        {
            public JobEntry(Isa95JobOrder order, Isa95JobCanonicalState state)
            {
                Order = order;
                State = state;
            }

            public Isa95JobOrder Order { get; set; }

            public Isa95JobCanonicalState State { get; set; }
        }

        /// <summary>
        /// An independent subscription backed by a signalled queue so that each
        /// subscriber consumes committed items exactly once.
        /// </summary>
        /// <typeparam name="T">The queued notification type.</typeparam>
        private sealed class Subscription<T> : IDisposable
        {
            public void Enqueue(T item)
            {
                lock (m_gate)
                {
                    if (m_disposed || m_completed)
                    {
                        return;
                    }
                    m_queue.Enqueue(item);
                    m_signal.Release();
                }
            }

            public void Complete()
            {
                lock (m_gate)
                {
                    if (m_disposed || m_completed)
                    {
                        return;
                    }
                    m_completed = true;
                    m_signal.Release();
                }
            }

            public Task WaitAsync(CancellationToken cancellationToken)
            {
                return m_signal.WaitAsync(cancellationToken);
            }

            public bool TryDequeue(out T item)
            {
                lock (m_gate)
                {
                    if (m_queue.Count > 0)
                    {
                        item = m_queue.Dequeue();
                        return true;
                    }
                }
                item = default!;
                return false;
            }

            public void Dispose()
            {
                lock (m_gate)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    m_disposed = true;
                    m_queue.Clear();
                }
                m_signal.Dispose();
            }

            private readonly Lock m_gate = new();
            private readonly Queue<T> m_queue = new();
            private readonly SemaphoreSlim m_signal = new(0);
            private bool m_completed;
            private bool m_disposed;
        }
    }
}
