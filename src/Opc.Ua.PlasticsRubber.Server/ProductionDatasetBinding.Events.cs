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
using System.Threading;
using System.Threading.Tasks;
using static Opc.Ua.PlasticsRubber.Server.ProductionDatasetValidation;

namespace Opc.Ua.PlasticsRubber.Server
{
    public sealed partial class ProductionDatasetBinding
    {
        /// <summary>
        /// Requests the client's matching dataset list through the standard list-request event.
        /// </summary>
        public async ValueTask RequestListAsync(
            ISystemContext context,
            string nameFilter = "",
            string mouldId = "",
            CancellationToken cancellationToken = default)
        {
            if (Node.ProductionDatasetLists is not { } lists)
            {
                throw Error(StatusCodes.BadNotSupported, "Dataset lists are not enabled.");
            }
            ServiceResult result = await InvokeAsync(
                context, lists.NodeId, lists, lists, async (request, token) =>
                {
                    Text(nameFilter, m_options);
                    Text(mouldId, m_options);
                    var notification = new RequestProductionDatasetListEventState(null);
                    InitializeRequest(
                        notification, request.SystemContext, lists, m_listRequestType,
                        "Production dataset list requested.");
                    notification.NameFilter!.Value = nameFilter;
                    notification.MouldId!.Value = mouldId;
                    await lists.ReportEventAsync(request.SystemContext, notification, token).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
            ThrowIfBad(result);
        }

        /// <summary>
        /// Requests a client download from a single source using the standard read-request event.
        /// </summary>
        public async ValueTask RequestReadAsync(
            ISystemContext context,
            ProductionDatasetReadOptionsType options,
            CancellationToken cancellationToken = default)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            TemporaryFileTransferState transfer = Node.ProductionDatasetTransfer!;
            ServiceResult result = await InvokeAsync(
                context, transfer.NodeId, transfer, transfer, async (request, token) =>
                {
                    Read(options, m_options);
                    var notification = new RequestProductionDatasetReadEventState(null);
                    InitializeRequest(
                        notification, request.SystemContext, transfer, m_readRequestType,
                        "Production dataset read requested.");
                    notification.Storage!.Value = options.Storage;
                    notification.Name!.Value = options.Name ?? string.Empty;
                    await transfer.ReportEventAsync(request.SystemContext, notification, token).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
            ThrowIfBad(result);
        }

        /// <summary>
        /// Requests a client upload, preserving the destination mask and component list.
        /// </summary>
        public async ValueTask RequestWriteAsync(
            ISystemContext context,
            ProductionDatasetWriteOptionsType options,
            CancellationToken cancellationToken = default)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            TemporaryFileTransferState transfer = Node.ProductionDatasetTransfer!;
            ServiceResult result = await InvokeAsync(
                context, transfer.NodeId, transfer, transfer, async (request, token) =>
                {
                    Write(options, m_options);
                    await EnsureMutableAsync(request, options.Storage, token).ConfigureAwait(false);
                    var notification = new RequestProductionDatasetWriteEventState(null);
                    InitializeRequest(
                        notification, request.SystemContext, transfer, m_writeRequestType,
                        "Production dataset write requested.");
                    notification.Storage!.Value = options.Storage;
                    notification.Name!.Value = options.Name ?? string.Empty;
                    notification.Components!.Value = options.Components.ToArray().ToArrayOf();
                    await transfer.ReportEventAsync(request.SystemContext, notification, token).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
            ThrowIfBad(result);
        }

        /// <summary>
        /// Creates concrete application event subtypes rather than emitting instances of abstract model types.
        /// </summary>
        private async ValueTask CreateRequestEventTypesAsync(CancellationToken ct)
        {
            if (Has(ProductionDatasetFeatures.Lists))
            {
                m_listRequestType = await CreateEventTypeAsync(
                    ObjectTypes.RequestProductionDatasetListEventType, "DatasetListRequestedEventType", ct)
                    .ConfigureAwait(false);
            }
            m_readRequestType = await CreateEventTypeAsync(
                ObjectTypes.RequestProductionDatasetReadEventType, "DatasetReadRequestedEventType", ct)
                .ConfigureAwait(false);
            m_writeRequestType = await CreateEventTypeAsync(
                ObjectTypes.RequestProductionDatasetWriteEventType, "DatasetWriteRequestedEventType", ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Registers a concrete subtype inheriting all generated standard event fields.
        /// </summary>
        private async ValueTask<NodeId> CreateEventTypeAsync(uint baseType, string name, CancellationToken ct)
        {
            NodeId superType = NodeId.Create(baseType, Namespaces.PlasticsRubber, m_owner.SystemContext.NamespaceUris);
            var type = new BaseObjectTypeState
            {
                NodeId = new NodeId(
                    Node.NodeId.WithNamespaceIndex(0).ToString() + "/EventTypes/" + name,
                    Node.NodeId.NamespaceIndex),
                BrowseName = new QualifiedName(name, Node.NodeId.NamespaceIndex),
                DisplayName = new LocalizedText(name),
                SuperTypeId = superType,
                IsAbstract = false
            };
            type.AddReference(ReferenceTypeIds.HasSubtype, true, superType);
            if (m_owner.FindPredefinedNode<NodeState>(type.NodeId) != null)
            {
                throw Error(StatusCodes.BadNodeIdExists, "The dataset event type identity is already owned.");
            }
            m_eventTypes.Add(type.NodeId);
            await m_owner.AddPredefinedNodeAsync(type, ct).ConfigureAwait(false);
            return type.NodeId;
        }

        /// <summary>
        /// Initializes standard event fields with a concrete type and the actual source object.
        /// </summary>
        private void InitializeRequest(
            BaseEventState notification, ISystemContext context, NodeState source, NodeId typeId, string message)
        {
            notification.Create(m_owner.SystemContext, NodeId.Null, QualifiedName.Null, LocalizedText.Null, true);
            notification.Initialize(context, source, EventSeverity.Low, new LocalizedText(message));
            notification.TypeDefinitionId = typeId;
            notification.EventType!.Value = typeId;
            notification.Time!.Value = Now;
            notification.ReceiveTime!.Value = Now;
        }

        /// <summary>
        /// The concrete list-request event subtype.
        /// </summary>
        private NodeId m_listRequestType;

        /// <summary>
        /// The concrete read-request event subtype.
        /// </summary>
        private NodeId m_readRequestType;

        /// <summary>
        /// The concrete write-request event subtype.
        /// </summary>
        private NodeId m_writeRequestType;
    }
}
