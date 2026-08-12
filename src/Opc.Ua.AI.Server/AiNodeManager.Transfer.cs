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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.AI;
using AIRefs = Opc.Ua.AI.ReferenceTypeIds;
using BrowseNames = Opc.Ua.AI.BrowseNames;
using ObjectIds = Opc.Ua.ObjectIds;
using ReferenceTypeIds = Opc.Ua.ReferenceTypeIds;

namespace Opc.Ua.AI.Server
{
    public sealed partial class AINodeManager
    {
        /// <summary>
        /// Opens a chunked exchange for a payload too large to pass inline.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The transfer object is created up front and handed back by NodeId, so the
        /// caller writes into a thing that already exists rather than negotiating
        /// one. Request and Response are Part 5 <c>FileType</c> instances, which is
        /// what lets an existing client library move the bytes without learning
        /// anything new: the specification did not invent a transfer protocol,
        /// because OPC UA already has one.
        /// </para>
        /// <para>
        /// A transfer expires. A caller that opens one and abandons it would
        /// otherwise hold Server memory until restart, and inference payloads are
        /// exactly the size that makes that matter.
        /// </para>
        /// </remarks>
        private async ValueTask<BeginTransferMethodStateResult> BeginTransferAsync(
            NodeId objectId,
            string contentType,
            ulong requestSize,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            DeploymentState? deployment = FindDeployment(objectId);
            if (deployment is null)
            {
                return new BeginTransferMethodStateResult
                {
                    ServiceResult = StatusCodes.BadNodeIdUnknown,
                    Transfer = NodeId.Null,
                    Accepted = false
                };
            }

            if (requestSize > m_options.MaxTransferSize)
            {
                // Refused before a byte is accepted. A Server that takes the whole
                // payload and then declines has already paid the cost it was trying
                // to avoid.
                return new BeginTransferMethodStateResult
                {
                    ServiceResult = StatusCodes.BadRequestTooLarge,
                    Transfer = NodeId.Null,
                    Accepted = false
                };
            }

            await ExpireTransfersAsync(ct).ConfigureAwait(false);

            InferenceTransferState node;

            lock (m_sync)
            {
                if (m_transfers.Count >= m_options.MaxConcurrentTransfers)
                {
                    return new BeginTransferMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadTooManyOperations,
                        Transfer = NodeId.Null,
                        Accepted = false
                    };
                }

                string transferId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

                node = new InferenceTransferState(null);
                node.Create(
                    SystemContext,
                    NodeId.Null,
                    new QualifiedName("Transfer_" + transferId, NamespaceIndex),
                    new LocalizedText("Transfer " + transferId),
                    true);

                Child<PropertyState<string>>(node, BrowseNames.TransferId).Value = transferId;
                Child<PropertyState<TransferStateEnum>>(node, BrowseNames.State).Value =
                    TransferStateEnum.Building;
                Child<PropertyState<string>>(node, BrowseNames.ContentType).Value = contentType;
                Child<PropertyState<DateTimeUtc>>(node, BrowseNames.ExpiresAt).Value =
                    DateTime.UtcNow.Add(m_options.TransferExpiry);

                var entry = new TransferEntry
                {
                    Node = node,
                    DeploymentId = deployment.NodeId,
                    ContentType = contentType,
                    ExpiresAt = DateTime.UtcNow.Add(m_options.TransferExpiry)
                };

                WireTransfer(entry);

                // Every member this transfer will ever carry is materialised now,
                // before the node is indexed. A child created afterwards exists on
                // the NodeState and is invisible over the wire, which reads as a
                // result that silently lost its ModelUsed rather than as an error.
                Child<PropertyState<string>>(node, BrowseNames.ResponseContentType);
                Child<PropertyState<NodeId>>(node, BrowseNames.ModelUsed);
                Child<PropertyState<UsageDataType>>(node, BrowseNames.Usage);
                Child<PropertyState<FinishReasonEnum>>(node, BrowseNames.FinishReason);
                Child<PropertyState<LocalizedText>>(node, BrowseNames.LastError);

                m_transfers[node.NodeId] = entry;
                Child<FolderState>(m_root!, BrowseNames.Jobs).AddChild(node);
                AddPredefinedNodeSynchronously(node);
            }

            return new BeginTransferMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Transfer = node.NodeId,
                Accepted = true
            };
        }

        /// <summary>
        /// Attaches the file handlers and the Execute method to a transfer.
        /// </summary>
        private void WireTransfer(TransferEntry entry)
        {
            var request = Child<FileState>(entry.Node, BrowseNames.Request);
            var response = Child<FileState>(entry.Node, BrowseNames.Response);

            m_files.Attach(request, entry.Request, writable: true);
            m_files.Attach(response, entry.Response, writable: false);
            m_files.Own(entry.Node, request, response);

            Child<ExecuteMethodState>(entry.Node, BrowseNames.Execute).OnCallAsync =
                (context, method, objectId, ct) => ExecuteTransferAsync(objectId, ct);

            // Materialised rather than looked up. FindChild does not create, so an
            // optional Method reached that way is silently absent: the sample would
            // claim to support Abort and publish nothing.
            Child<MethodState>(entry.Node, BrowseNames.Abort).OnCallMethod2Async = async (
                context, method, objectId, inputs, outputs, ct) =>
            {
                await DiscardTransferAsync(objectId, ct).ConfigureAwait(false);
                return ServiceResult.Good;
            };
        }

        /// <summary>
        /// Runs the inference the transfer was opened for.
        /// </summary>
        /// <remarks>
        /// The transfer carries the same outputs the inline call returns, including
        /// <c>ModelUsed</c>. A large payload is a transport concern, so nothing
        /// about the result a caller is entitled to should change because the bytes
        /// arrived in chunks.
        /// </remarks>
        private async ValueTask<ExecuteMethodStateResult> ExecuteTransferAsync(
            NodeId objectId,
            CancellationToken ct)
        {
            TransferEntry? entry;
            DeploymentState? deployment;
            byte[] payload;

            lock (m_sync)
            {
                if (!m_transfers.TryGetValue(objectId, out entry))
                {
                    return new ExecuteMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadNodeIdUnknown,
                        Accepted = false
                    };
                }

                deployment = FindDeployment(entry.DeploymentId);

                if (deployment is null)
                {
                    return new ExecuteMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadNodeIdUnknown,
                        Accepted = false
                    };
                }

                // Snapshotted through the file manager, which is what actually
                // serialises against a concurrent Write. Reading the MemoryStream
                // directly under m_sync would look careful and guarantee nothing:
                // the two locks do not exclude one another.
                payload = m_files.Snapshot(Child<FileState>(entry.Node, BrowseNames.Request));

                SetTransferState(entry, TransferStateEnum.Executing);
            }

            InferenceOutcome outcome = await RunWithFallbackAsync(
                deployment,
                payload,
                entry.ContentType,
                m_options.TransferInferenceTimeout.TotalMilliseconds,
                ct).ConfigureAwait(false);

            lock (m_sync)
            {
                // The inference took a while, and Abort and expiry both remove the
                // entry and dispose its buffers. Writing the result into a transfer
                // that is no longer live would throw ObjectDisposedException out of
                // a Method call - so the answer is dropped instead, which is what a
                // caller that aborted was asking for.
                if (!m_transfers.TryGetValue(objectId, out TransferEntry? live) ||
                    !ReferenceEquals(live, entry))
                {
                    return new ExecuteMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidState,
                        Accepted = false
                    };
                }

                if (!outcome.Result.Ok)
                {
                    Child<PropertyState<LocalizedText>>(entry.Node, BrowseNames.LastError)
                        .Value = new LocalizedText(outcome.Result.Message ?? "Inference failed.");
                    SetTransferState(entry, TransferStateEnum.Failed);

                    return new ExecuteMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Accepted = false
                    };
                }

                m_files.Replace(
                    Child<FileState>(entry.Node, BrowseNames.Response),
                    outcome.Result.Payload.Span);

                Child<PropertyState<string>>(entry.Node, BrowseNames.ResponseContentType)
                    .Value = outcome.Result.ContentType;
                Child<PropertyState<NodeId>>(entry.Node, BrowseNames.ModelUsed).Value =
                    outcome.ModelUsed;
                Child<PropertyState<UsageDataType>>(entry.Node, BrowseNames.Usage).Value =
                    ToUsage(outcome.Result);
                Child<PropertyState<FinishReasonEnum>>(entry.Node, BrowseNames.FinishReason)
                    .Value = ToFinishReason(outcome.Result.Finish);

                SetTransferState(entry, TransferStateEnum.Completed);
            }

            return new ExecuteMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Accepted = true
            };
        }

        private void SetTransferState(TransferEntry entry, TransferStateEnum state)
        {
            var node = Child<PropertyState<TransferStateEnum>>(entry.Node, BrowseNames.State);
            node.Value = state;
            entry.Node.ClearChangeMasks(SystemContext, true);
        }

        /// <summary>
        /// Removes a transfer and the memory it was holding.
        /// </summary>
        /// <remarks>
        /// The dictionary entry goes under the lock and the node goes afterwards.
        /// Removing the entry first is what makes this safe to call twice: a second
        /// caller finds nothing and returns, rather than racing the first one to
        /// delete the same node.
        /// </remarks>
        private async ValueTask DiscardTransferAsync(
            NodeId transferId,
            CancellationToken ct)
        {
            TransferEntry? entry;

            lock (m_sync)
            {
                if (!m_transfers.Remove(transferId, out entry))
                {
                    return;
                }

                m_files.Detach(entry.Node);
            }

            await DeleteNodeAsync(SystemContext, transferId, ct).ConfigureAwait(false);
            entry.Dispose();
        }

        /// <summary>
        /// Drops transfers nobody came back for.
        /// </summary>
        /// <remarks>
        /// Called when a new transfer is opened rather than on a timer. Reclaiming
        /// under the pressure that makes it necessary costs nothing when there is no
        /// pressure, and a timer that ran every minute forever would.
        /// </remarks>
        private async ValueTask ExpireTransfersAsync(CancellationToken ct)
        {
            DateTime now = DateTime.UtcNow;
            List<TransferEntry>? expired = null;

            lock (m_sync)
            {
                foreach (KeyValuePair<NodeId, TransferEntry> pair in m_transfers)
                {
                    if (pair.Value.ExpiresAt <= now)
                    {
                        (expired ??= []).Add(pair.Value);
                    }
                }

                if (expired is null)
                {
                    return;
                }

                foreach (TransferEntry entry in expired)
                {
                    m_transfers.Remove(entry.Node.NodeId);
                    m_files.Detach(entry.Node);
                }
            }

            foreach (TransferEntry entry in expired)
            {
                await DeleteNodeAsync(SystemContext, entry.Node.NodeId, ct)
                    .ConfigureAwait(false);
                entry.Dispose();
            }
        }
    }
}
