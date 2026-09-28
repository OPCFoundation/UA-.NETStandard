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
using Microsoft.Extensions.Logging;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Server;

namespace Opc.Ua.Machinery.Server.Results
{
    /// <summary>
    /// Binds the optional methods of one OPC 40001-101
    /// <c>ResultManagementType</c> instance to a result store, and raises the
    /// <c>ResultReadyEventType</c> for newly published results.
    /// </summary>
    /// <remarks>
    /// The same binder serves the <c>ResultManagement</c> object below a
    /// machine and the one a stand-alone
    /// <see cref="MachineryResultNodeManager"/> publishes, so both answer a
    /// client identically — the result part of OPC 40001 needs only UA core and
    /// carries no machine model of its own.
    /// </remarks>
    internal sealed class MachineryResultManagementBinder
    {
        public MachineryResultManagementBinder(
            ResultManagementState state,
            ISystemContext context,
            MachineryServerOptions? options = null,
            TimeProvider? timeProvider = null)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_options = options ?? new MachineryServerOptions();
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        public ResultManagementState State { get; }

        public void BindMethods(IMachineryResultStore store)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            // The timeout argument of the three getters is, per OPC 40001-101
            // §6.4 and §7.1.3, the client's hint for how long it will need the
            // data afterwards - "merely a hint allowing the Server to optimize
            // its resource management". It is not a deadline for the call, so
            // the store reads run under the service call's own cancellation.
            State.AddGetLatestResult(m_context);
            State.GetLatestResult!.OnCallAsync = async (context, _, _, _, ct) =>
            {
                MachineryResult? result = await store
                    .GetLatestResultAsync(ct)
                    .ConfigureAwait(false);
                if (result == null)
                {
                    return new GetLatestResultMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        ResultHandle = 0,
                        Result = new ResultDataType(),
                        Error = unchecked((int)StatusCodes.BadNoDataAvailable.Code)
                    };
                }
                uint? handle = TryPin(context, result.ResultId);
                return new GetLatestResultMethodStateResult
                {
                    ServiceResult = handle == null ? PinnedHandleLimitReached() : ServiceResult.Good,
                    ResultHandle = handle ?? 0,
                    Result = result.Data,
                    Error = handle == null
                        ? unchecked((int)StatusCodes.BadTooManyOperations.Code)
                        : 0
                };
            };

            State.AddGetResultById(m_context);
            State.GetResultById!.OnCallAsync = async (context, _, _, resultId, _, ct) =>
            {
                MachineryResult? result = await store
                    .GetResultByIdAsync(resultId, ct)
                    .ConfigureAwait(false);
                if (result == null)
                {
                    return new GetResultByIdMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        ResultHandle = 0,
                        Result = new ResultDataType(),
                        Error = unchecked((int)StatusCodes.BadNotFound.Code)
                    };
                }
                uint? handle = TryPin(context, result.ResultId);
                return new GetResultByIdMethodStateResult
                {
                    ServiceResult = handle == null ? PinnedHandleLimitReached() : ServiceResult.Good,
                    ResultHandle = handle ?? 0,
                    Result = result.Data,
                    Error = handle == null
                        ? unchecked((int)StatusCodes.BadTooManyOperations.Code)
                        : 0
                };
            };

            State.AddGetResultIdListFiltered(m_context);
            State.GetResultIdListFiltered!.OnCallAsync = async (
                context, _, _, filter, orderedBy, maxResults, _, ct) =>
            {
                ArrayOf<string> ids;
                if (MachineryResultQuery.IsUnrestricted(filter, orderedBy))
                {
                    // Neither a filter nor an order: OPC 40001-101 §7.1.3
                    // executes no ordering, so the store's own listing is
                    // the answer.
                    ids = await store
                        .GetResultIdsAsync(maxResults, ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    var filterContext = new FilterContext(
                        context.NamespaceUris,
                        context.TypeTable,
                        context as IOperationContext,
                        context.Telemetry);
                    ServiceResult invalid = MachineryResultQuery.Validate(
                        filter,
                        orderedBy,
                        filterContext);
                    if (ServiceResult.IsBad(invalid))
                    {
                        return new GetResultIdListFilteredMethodStateResult
                        {
                            ServiceResult = invalid,
                            ResultHandle = 0,
                            ResultIdList = ArrayOf<string>.Empty,
                            Error = unchecked((int)invalid.StatusCode.Code)
                        };
                    }
                    ids = await MachineryResultQuery.ExecuteAsync(
                        store,
                        filter,
                        orderedBy,
                        maxResults,
                        filterContext,
                        ct).ConfigureAwait(false);
                }
                uint? handle = ids.Count == 0 ? 0 : TryPin(context, ids);
                return new GetResultIdListFilteredMethodStateResult
                {
                    ServiceResult = handle == null ? PinnedHandleLimitReached() : ServiceResult.Good,
                    ResultHandle = handle ?? 0,
                    ResultIdList = ids,
                    Error = handle == null
                        ? unchecked((int)StatusCodes.BadTooManyOperations.Code)
                        : 0
                };
            };

            State.AddAcknowledgeResults(m_context);
            State.AcknowledgeResults!.OnCallAsync = async (_, _, _, resultIds, ct) =>
            {
                ArrayOf<int> errors = await store
                    .AcknowledgeResultsAsync(resultIds, ct)
                    .ConfigureAwait(false);
                return new AcknowledgeResultsMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    ErrorPerResultId = errors
                };
            };

            // ReleaseResultHandle is mandatory for a server that hands out
            // handles: OPC 40001-101 lets the client tell the server it is done
            // with the results a Get* call pinned. Handles also expire on their
            // own, so an abandoned client cannot pin a result for ever.
            State.AddReleaseResultHandle(m_context);
            State.ReleaseResultHandle!.OnCallAsync = (context, _, _, resultHandle, ct) =>
            {
                _ = ct;
                return new ValueTask<ReleaseResultHandleMethodStateResult>(
                    new ReleaseResultHandleMethodStateResult
                    {
                        ServiceResult = Release(context, resultHandle)
                    });
            };
        }

        /// <summary>
        /// Gets the result identifiers a client has pinned with a handle.
        /// </summary>
        /// <param name="resultHandle">The handle to resolve.</param>
        public ArrayOf<string> GetPinnedResults(uint resultHandle)
        {
            lock (m_handleLock)
            {
                return m_handles.TryGetValue(resultHandle, out HandleEntry entry)
                    ? entry.ResultIds
                    : ArrayOf<string>.Empty;
            }
        }

        private static ServiceResult PinnedHandleLimitReached()
        {
            return ServiceResult.Create(
                StatusCodes.BadTooManyOperations,
                "The maximum number of pinned result handles has been reached; " +
                "release an existing handle with ReleaseResultHandle first.");
        }

        private uint? TryPin(ISystemContext context, string resultId)
        {
            return TryPin(context, new[] { resultId }.ToArrayOf());
        }

        /// <summary>
        /// Pins a result identifier list behind a fresh handle, refusing when
        /// the configured cap is already reached. Expired handles are swept
        /// first, so the cap is measured against handles a client is still
        /// plausibly using — the same shape
        /// <see cref="MachineryResultTransferManager"/> uses for its own
        /// concurrent-handle limit.
        /// </summary>
        private uint? TryPin(ISystemContext context, ArrayOf<string> resultIds)
        {
            NodeId? sessionId = (context as ISessionSystemContext)?.SessionId;
            DateTimeOffset now = m_timeProvider.GetUtcNow();
            lock (m_handleLock)
            {
                ReclaimExpiredHandlesNoLock(now);
                if (m_handles.Count >= m_options.MaxPinnedResultHandles)
                {
                    return null;
                }
                uint handle = ++m_nextHandle;
                m_handles[handle] = new HandleEntry(sessionId, resultIds, now);
                return handle;
            }
        }

        /// <summary>
        /// Drops handles that have seen no <c>ReleaseResultHandle</c> call for
        /// longer than <see cref="MachineryServerOptions.PinnedResultHandleTimeout"/>.
        /// Must be called with <see cref="m_handleLock"/> already held.
        /// </summary>
        private void ReclaimExpiredHandlesNoLock(DateTimeOffset now)
        {
            List<uint>? expired = null;
            foreach (KeyValuePair<uint, HandleEntry> entry in m_handles)
            {
                if (now - entry.Value.PinnedAt > m_options.PinnedResultHandleTimeout)
                {
                    (expired ??= []).Add(entry.Key);
                }
            }
            if (expired == null)
            {
                return;
            }
            for (int ii = 0; ii < expired.Count; ii++)
            {
                m_handles.Remove(expired[ii]);
            }
        }

        private ServiceResult Release(ISystemContext context, uint resultHandle)
        {
            NodeId? sessionId = (context as ISessionSystemContext)?.SessionId;
            lock (m_handleLock)
            {
                if (!m_handles.TryGetValue(resultHandle, out HandleEntry entry))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidArgument,
                        "Unknown result handle.");
                }
                if (entry.OwnerSessionId != null &&
                    sessionId != null &&
                    entry.OwnerSessionId != sessionId)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadUserAccessDenied,
                        "The result handle is owned by another session.");
                }
                m_handles.Remove(resultHandle);
            }
            return ServiceResult.Good;
        }

        public MachineryResultTransferManager? BindTransfer(
            AsyncCustomNodeManager manager,
            IMachineryResultStore store,
            MachineryServerOptions options,
            ILogger logger)
        {
            if (State.ResultTransfer == null)
            {
                return null;
            }

            var transfer = new MachineryResultTransferManager(
                State.ResultTransfer,
                manager,
                store,
                options,
                logger);

            return transfer;
        }

        /// <summary>
        /// Mints the concrete subtype of the abstract
        /// <c>ResultReadyEventType</c> this binder reports events with.
        /// </summary>
        /// <remarks>
        /// OPC 40001-101 declares <c>ResultReadyEventType</c> abstract, so the
        /// event cannot carry it as its type definition. Without this a client
        /// filtering on a concrete event type never sees the event.
        /// </remarks>
        /// <param name="manager">The manager that owns the address space.</param>
        /// <param name="instanceNamespaceIndex">
        /// The application-owned namespace the subtype is minted into.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask BindEventTypeAsync(
            AsyncCustomNodeManager manager,
            ushort instanceNamespaceIndex,
            CancellationToken cancellationToken)
        {
            m_eventTypeId = await MachineryConcreteEventTypes.EnsureAsync(
                manager,
                instanceNamespaceIndex,
                NodeId.Create(
                    Opc.Ua.Machinery.Result.ObjectTypes.ResultReadyEventType,
                    Opc.Ua.Machinery.Result.Namespaces.MachineryResult,
                    m_context.NamespaceUris),
                MachineryConcreteEventTypes.ResultReadyEventInstanceType,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the concrete event type the binder reports with, or
        /// <see cref="NodeId.Null"/> when <see cref="BindEventTypeAsync"/> has
        /// not run.
        /// </summary>
        public NodeId EventTypeId => m_eventTypeId;

        public void RaiseResultReady(MachineryResult result)
        {
            if (m_eventTypeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The concrete ResultReadyEventType subtype has not been registered; " +
                    "OPC UA forbids reporting an event with an abstract type definition.");
            }

            ResultReadyEventState eventState =
                m_context.CreateInstanceOfResultReadyEventType(State, default);
            eventState.Initialize(
                m_context,
                State,
                EventSeverity.Medium,
                new LocalizedText($"Result '{result.ResultId}' is ready."));
            eventState.CreateOrReplaceResult(
                m_context,
                m_context.CreateInstanceOfResultType(eventState, default));
            eventState.Result!.WrappedValue = Variant.FromStructure(result.Data);

            // Both halves matter: TypeDefinitionId is what the event carries in
            // the address space, EventType is the field a client's event filter
            // actually selects on.
            eventState.TypeDefinitionId = m_eventTypeId;
            eventState.EventType!.Value = m_eventTypeId;
            State.ReportEvent(m_context, eventState);
        }

        private readonly ISystemContext m_context;
        private readonly MachineryServerOptions m_options;
        private readonly TimeProvider m_timeProvider;
        private NodeId m_eventTypeId = NodeId.Null;
        private readonly Dictionary<uint, HandleEntry> m_handles = [];
        private readonly Lock m_handleLock = new();
        private uint m_nextHandle;

        private readonly record struct HandleEntry(
            NodeId? OwnerSessionId,
            ArrayOf<string> ResultIds,
            DateTimeOffset PinnedAt);
    }
}
