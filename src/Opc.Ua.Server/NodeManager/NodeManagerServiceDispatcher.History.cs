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
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server
{
    internal sealed partial class NodeManagerServiceDispatcher
    {
        internal async ValueTask<(ArrayOf<HistoryReadResult> Values, ArrayOf<DiagnosticInfo> Diagnostics)>
            HistoryReadAsync(
            OperationContext context,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            ArrayOf<HistoryReadValueId> nodesToRead,
            CancellationToken cancellationToken = default)
        {
            var results = new List<HistoryReadResult>(nodesToRead.Count);
            try
            {
                return await HistoryReadCoreAsync(
                    context, historyReadDetails, timestampsToReturn, releaseContinuationPoints, nodesToRead,
                    results, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                ReleaseFailedHistoryResults(context, results, failure);
                throw;
            }
        }

        internal async ValueTask<ServiceResult> CaptureHistoryContinuationAsync(
            HistorianContinuationState state,
            CancellationToken cancellationToken)
        {
            if (state.Ownership.HasCapturedDependencies)
            {
                return ServiceResult.Good;
            }
            using NodeManagerRoutingTable.ReadScope routing = m_nodeManagers.Capture();
            (object? handle, IAsyncNodeManager? source) = await m_owner.GetManagerHandleAsync(
                state.OriginNodeId, cancellationToken).ConfigureAwait(false);
            if (handle is null || source is null)
            {
                return StatusCodes.BadNodeIdUnknown;
            }
            ArrayOf<NodeId> dependencies = [];
            if (state.Provider is IHistorianContinuationDependencies declaration)
            {
                if (!declaration.TryGetContinuationDependencies(state.NodeId, state.ResumeToken, out dependencies))
                {
                    return ServiceResult.Create(StatusCodes.BadNotSupported,
                        "The historian cannot declare its complete continuation dependencies.");
                }
            }
            else if (m_owner.RequiresHistoryContinuationOwnership(source))
            {
                return ServiceResult.Create(StatusCodes.BadNotSupported,
                    "A dynamic historian source requires complete continuation dependencies.");
            }
            else
            {
                return ServiceResult.Good;
            }

            var owners = new List<IAsyncNodeManager> { source };
            HashSet<NodeId> nodeIds = [state.NodeId, .. dependencies];
            foreach (NodeId nodeId in nodeIds)
            {
                if (nodeId.IsNull)
                {
                    return ServiceResult.Create(StatusCodes.BadNotSupported,
                        "A history continuation dependency must identify a local Node.");
                }
                (object? target, IAsyncNodeManager? manager) = await m_owner.GetManagerHandleAsync(
                    nodeId, cancellationToken).ConfigureAwait(false);
                if (target is null || manager is null)
                {
                    return ServiceResult.Create(StatusCodes.BadNodeIdUnknown,
                        "A history continuation dependency has no captured owner.");
                }
                if (!owners.Exists(owner => ReferenceEquals(owner, manager)))
                {
                    owners.Add(manager);
                }
            }
            if (!owners.Exists(m_owner.RequiresHistoryContinuationOwnership))
            {
                return ServiceResult.Good;
            }
            ArrayOf<IAsyncNodeManager> retained = [.. owners];
            state.Ownership.Manager = source;
            state.Ownership.SetDependencyOwners(retained);
            state.Ownership.RoutingSnapshot = m_nodeManagers.CapturedRevision.ForContinuation(retained);
            return ServiceResult.Good;
        }

        private async ValueTask<(HistoryReadResult Result, ServiceResult Error)> ReadHistoryContinuationAsync(
            OperationContext context,
            SessionContinuationPoints holder,
            HistoryReadValueId node,
            HistoryReadDetails details,
            TimestampsToReturn timestampsToReturn,
            bool release,
            int index,
            int nodeCount,
            CancellationToken cancellationToken)
        {
            if (!release && details is ReadProcessedDetails processed)
            {
                if (processed.AggregateType.Count != nodeCount)
                {
                    throw new ServiceResultException(StatusCodes.BadAggregateListMismatch);
                }
                var single = (ReadProcessedDetails)processed.Clone();
                single.AggregateType = [processed.AggregateType[index]];
                details = single;
            }
            using SessionContinuationPoints.HistoryReadScope use = holder.BeginHistoryRead(node.ContinuationPoint);
            var result = new HistoryReadResult();
            if (use.Point is null ||
                (use.Point is HistorianContinuationState saved &&
                    (saved.OriginNodeId.IsNull ? saved.NodeId : saved.OriginNodeId) != node.NodeId))
            {
                return (result, StatusCodes.BadContinuationPointInvalid);
            }
            var state = use.Point as HistorianContinuationState;
            using NodeManagerRoutingTable.ReadScope routing = m_nodeManagers.Capture(state?.Ownership.RoutingSnapshot);
            ServiceResult? error = await ValidateHistoryReadRequestAsync(context, node, cancellationToken)
                .ConfigureAwait(false);
            if (ServiceResult.IsBad(error))
            {
                return (result, error!);
            }

            node.Processed = false;
            var results = new List<HistoryReadResult> { result };
            var errors = new List<ServiceResult> { ServiceResult.Good };
            try
            {
                if (state?.Ownership.RoutingSnapshot is not null)
                {
                    await state.Ownership.Manager.HistoryReadAsync(
                        context, details, timestampsToReturn, release, [node], results, errors, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    foreach (IAsyncNodeManager manager in m_nodeManagers)
                    {
                        await manager.HistoryReadAsync(
                            context, details, timestampsToReturn, release, [node], results, errors, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                return (results[0] ?? result, node.Processed ? errors[0] : StatusCodes.BadNodeIdUnknown);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                ReleaseFailedHistoryResults(context, results, failure);
                throw;
            }
        }

        private static void ReleaseFailedHistoryResults(
            OperationContext context,
            List<HistoryReadResult> results,
            Exception failure)
        {
            List<Exception>? failures = null;
            foreach (HistoryReadResult? result in results)
            {
                if (result is null || result.ContinuationPoint.IsEmpty)
                {
                    continue;
                }
                try
                {
                    context.Session?.ContinuationPoints.RestoreHistory(result.ContinuationPoint)?.Dispose();
                }
                catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                {
                    (failures ??= [failure]).Add(cleanupFailure);
                }
            }
            if (failures is not null)
            {
                throw new AggregateException("HistoryRead and continuation cleanup failed.", failures);
            }
        }
    }
}
