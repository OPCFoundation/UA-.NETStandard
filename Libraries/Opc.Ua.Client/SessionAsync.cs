/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

#if CLIENT_ASYNC

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Manages a session with a server.
    /// Contains the async versions of the public session api.
    /// </summary>
    public partial class Session : SessionClientBatched, ISession, IDisposable
    {
        #region Subscription Async Methods
        /// <summary>
        /// Removes a subscription from the session.
        /// </summary>
        /// <param name="subscription">The subscription to remove.</param>
        public async Task<bool> RemoveSubscriptionAsync(Subscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription));

            if (subscription.Created)
            {
                await subscription.DeleteAsync(false).ConfigureAwait(false);
            }

            lock (SyncRoot)
            {
                if (!m_subscriptions.Remove(subscription))
                {
                    return false;
                }

                subscription.Session = null;
            }

            if (m_SubscriptionsChanged != null)
            {
                m_SubscriptionsChanged(this, null);
            }

            return true;
        }

        /// <summary>
        /// Removes a list of subscriptions from the sessiont.
        /// </summary>
        /// <param name="subscriptions">The list of subscriptions to remove.</param>
        public async Task<bool> RemoveSubscriptionsAsync(IEnumerable<Subscription> subscriptions)
        {
            if (subscriptions == null) throw new ArgumentNullException(nameof(subscriptions));

            List<Subscription> subscriptionsToDelete = new List<Subscription>();

            bool removed = PrepareSubscriptionsToDelete(subscriptions, subscriptionsToDelete);

            foreach (Subscription subscription in subscriptionsToDelete)
            {
                await subscription.DeleteAsync(true).ConfigureAwait(false);
            }

            if (removed)
            {
                if (m_SubscriptionsChanged != null)
                {
                    m_SubscriptionsChanged(this, null);
                }
            }

            return removed;
        }
        #endregion

        #region ReadNode Async Methods
        /// <summary>
        /// Reads the values for the node attributes and returns a node object collection.
        /// </summary>
        /// <remarks>
        /// If the nodeclass for the nodes in nodeIdCollection is already known
        /// and passed as nodeClass, reads only values of required attributes.
        /// Otherwise NodeClass.Unspecified should be used.
        /// </remarks>
        /// <param name="nodeIds">The nodeId collection to read.</param>
        /// <param name="nodeClass">The nodeClass of all nodes in the collection. Set to <c>NodeClass.Unspecified</c> if the nodeclass is unknown.</param>
        /// <param name="optionalAttributes">Set to <c>true</c> if optional attributes should not be omitted.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The node collection and associated errors.</returns>
        public async Task<(IList<Node>, IList<ServiceResult>)> ReadNodesAsync(
            IList<NodeId> nodeIds,
            NodeClass nodeClass,
            bool optionalAttributes = false,
            CancellationToken ct = default)
        {
            if (nodeIds.Count == 0)
            {
                return (new List<Node>(), new List<ServiceResult>());
            }

            if (nodeClass == NodeClass.Unspecified)
            {
                return await ReadNodesAsync(nodeIds, optionalAttributes, ct).ConfigureAwait(false);
            }

            var nodeCollection = new NodeCollection(nodeIds.Count);

            // determine attributes to read for nodeclass
            var attributesPerNodeId = new List<IDictionary<uint, DataValue>>(nodeIds.Count);
            var attributesToRead = new ReadValueIdCollection();

            CreateNodeClassAttributesReadNodesRequest(
                nodeIds, nodeClass,
                attributesToRead, attributesPerNodeId,
                nodeCollection,
                optionalAttributes);

            ReadResponse readResponse = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                attributesToRead,
                ct).ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ClientBase.ValidateResponse(values, attributesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, attributesToRead);

            var serviceResults = new ServiceResult[nodeIds.Count].ToList();
            ProcessAttributesReadNodesResponse(
                readResponse.ResponseHeader,
                attributesToRead, attributesPerNodeId,
                values, diagnosticInfos,
                nodeCollection, serviceResults);

            return (nodeCollection, serviceResults);
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object collection.
        /// Reads the nodeclass of the nodeIds, then reads
        /// the values for the node attributes and returns a node collection.
        /// </summary>
        /// <param name="nodeIds">The nodeId collection.</param>
        /// <param name="optionalAttributes">If optional attributes to read.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<(IList<Node>, IList<ServiceResult>)> ReadNodesAsync(
            IList<NodeId> nodeIds,
            bool optionalAttributes = false,
            CancellationToken ct = default)
        {
            if (nodeIds.Count == 0)
            {
                return (new List<Node>(), new List<ServiceResult>());
            }

            var nodeCollection = new NodeCollection(nodeIds.Count);
            var itemsToRead = new ReadValueIdCollection(nodeIds.Count);

            // first read only nodeclasses for nodes from server.
            itemsToRead = new ReadValueIdCollection(
                nodeIds.Select(nodeId =>
                    new ReadValueId {
                        NodeId = nodeId,
                        AttributeId = Attributes.NodeClass
                    }));

            ReadResponse readResponse = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                itemsToRead,
                ct).ConfigureAwait(false);

            DataValueCollection nodeClassValues = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ClientBase.ValidateResponse(nodeClassValues, itemsToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            // second determine attributes to read per nodeclass
            var attributesPerNodeId = new List<IDictionary<uint, DataValue>>(nodeIds.Count);
            var serviceResults = new List<ServiceResult>(nodeIds.Count);
            var attributesToRead = new ReadValueIdCollection();

            CreateAttributesReadNodesRequest(
                readResponse.ResponseHeader,
                itemsToRead, nodeClassValues, diagnosticInfos,
                attributesToRead, attributesPerNodeId, nodeCollection, serviceResults,
                optionalAttributes);

            if (attributesToRead.Count > 0)
            {
                readResponse = await ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    attributesToRead, ct).ConfigureAwait(false);

                DataValueCollection values = readResponse.Results;
                diagnosticInfos = readResponse.DiagnosticInfos;

                ClientBase.ValidateResponse(values, attributesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, attributesToRead);

                ProcessAttributesReadNodesResponse(
                    readResponse.ResponseHeader,
                    attributesToRead, attributesPerNodeId,
                    values, diagnosticInfos,
                    nodeCollection, serviceResults);
            }

            return (nodeCollection, serviceResults);
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object.
        /// </summary>
        /// <param name="nodeId">The nodeId.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        public Task<Node> ReadNodeAsync(
            NodeId nodeId,
            CancellationToken ct = default)
        {
            return ReadNodeAsync(nodeId, NodeClass.Unspecified, true, ct);
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object.
        /// </summary>
        /// <remarks>
        /// If the nodeclass is known, only the supported attribute values are read.
        /// </remarks>
        /// <param name="nodeId">The nodeId.</param>
        /// <param name="nodeClass">The nodeclass of the node to read.</param>
        /// <param name="optionalAttributes">Read optional attributes.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        public async Task<Node> ReadNodeAsync(
            NodeId nodeId,
            NodeClass nodeClass,
            bool optionalAttributes = true,
            CancellationToken ct = default)
        {
            // build list of attributes.
            var attributes = CreateAttributes(nodeClass, optionalAttributes);

            // build list of values to read.
            ReadValueIdCollection itemsToRead = new ReadValueIdCollection();
            foreach (uint attributeId in attributes.Keys)
            {
                ReadValueId itemToRead = new ReadValueId {
                    NodeId = nodeId,
                    AttributeId = attributeId
                };
                itemsToRead.Add(itemToRead);
            }

            // read from server.
            ReadResponse readResponse = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                itemsToRead, ct).ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ClientBase.ValidateResponse(values, itemsToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            return ProcessReadResponse(readResponse.ResponseHeader, attributes, itemsToRead, values, diagnosticInfos);
        }

        /// <summary>
        /// Reads the value for a node.
        /// </summary>
        /// <param name="nodeId">The node Id.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        public async Task<DataValue> ReadValueAsync(
            NodeId nodeId,
            CancellationToken ct = default)
        {
            ReadValueId itemToRead = new ReadValueId {
                NodeId = nodeId,
                AttributeId = Attributes.Value
            };

            ReadValueIdCollection itemsToRead = new ReadValueIdCollection {
                itemToRead
            };

            // read from server.
            ReadResponse readResponse = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                itemsToRead,
                ct).ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ClientBase.ValidateResponse(values, itemsToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            if (StatusCode.IsBad(values[0].StatusCode))
            {
                ServiceResult result = ClientBase.GetResult(values[0].StatusCode, 0, diagnosticInfos, readResponse.ResponseHeader);
                throw new ServiceResultException(result);
            }

            return values[0];
        }


        /// <summary>
        /// Reads the values for a node collection. Returns diagnostic errors.
        /// </summary>
        /// <param name="nodeIds">The node Id.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        public async Task<(DataValueCollection, IList<ServiceResult>)> ReadValuesAsync(
            IList<NodeId> nodeIds,
            CancellationToken ct = default)
        {
            if (nodeIds.Count == 0)
            {
                return (new DataValueCollection(), new List<ServiceResult>());
            }

            // read all values from server.
            var itemsToRead = new ReadValueIdCollection(
                nodeIds.Select(nodeId =>
                    new ReadValueId {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }));

            // read from server.
            var errors = new List<ServiceResult>(itemsToRead.Count);

            ReadResponse readResponse = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                itemsToRead, ct).ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ClientBase.ValidateResponse(values, itemsToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            foreach (var value in values)
            {
                ServiceResult result = ServiceResult.Good;
                if (StatusCode.IsBad(value.StatusCode))
                {
                    result = ClientBase.GetResult(values[0].StatusCode, 0, diagnosticInfos, readResponse.ResponseHeader);
                }
                errors.Add(result);
            }

            return (values, errors);
        }
        #endregion

        #region Close Async Methods
        /// <summary>
        /// Disconnects from the server and frees any network resources.
        /// </summary>
        public Task<StatusCode> CloseAsync(CancellationToken ct = default)
        {
            return CloseAsync(m_keepAliveInterval, true, ct);
        }

        /// <summary>
        /// Close the session with the server and optionally closes the channel.
        /// </summary>
        /// <param name="closeChannel"></param>
        /// <param name="ct">The cancellation token.</param>
        public Task<StatusCode> CloseAsync(bool closeChannel, CancellationToken ct = default)
        {
            return CloseAsync(m_keepAliveInterval, closeChannel, ct);
        }

        /// <summary>
        /// Disconnects from the server and frees any network resources with the specified timeout.
        /// </summary>
        public Task<StatusCode> CloseAsync(int timeout, CancellationToken ct = default)
            => CloseAsync(timeout, true, ct);

        /// <summary>
        /// Disconnects from the server and frees any network resources with the specified timeout.
        /// </summary>
        public virtual async Task<StatusCode> CloseAsync(int timeout, bool closeChannel, CancellationToken ct = default)
        {
            // check if already called.
            if (Disposed)
            {
                return StatusCodes.Good;
            }

            StatusCode result = StatusCodes.Good;

            // stop the keep alive timer.
            Utils.SilentDispose(m_keepAliveTimer);
            m_keepAliveTimer = null;

            // check if currectly connected.
            bool connected = Connected;

            // halt all background threads.
            if (connected)
            {
                if (m_SessionClosing != null)
                {
                    try
                    {
                        m_SessionClosing(this, null);
                    }
                    catch (Exception e)
                    {
                        Utils.LogError(e, "Session: Unexpected eror raising SessionClosing event.");
                    }
                }
            }

            // close the session with the server.
            if (connected && !KeepAliveStopped)
            {
                int existingTimeout = this.OperationTimeout;

                try
                {
                    // close the session and delete all subscriptions if specified.
                    this.OperationTimeout = timeout;
                    var response = await base.CloseSessionAsync(null, m_deleteSubscriptionsOnClose, ct).ConfigureAwait(false);
                    this.OperationTimeout = existingTimeout;

                    if (closeChannel)
                    {
                        CloseChannel();
                    }

                    // raised notification indicating the session is closed.
                    SessionCreated(null, null);
                }
                catch (Exception e)
                {
                    // dont throw errors on disconnect, but return them
                    // so the caller can log the error.
                    if (e is ServiceResultException)
                    {
                        result = ((ServiceResultException)e).StatusCode;
                    }
                    else
                    {
                        result = StatusCodes.Bad;
                    }

                    Utils.LogError("Session close error: " + result);
                }
            }

            // clean up.
            if (closeChannel)
            {
                Dispose();
            }

            return result;
        }
        #endregion
    }
}
#endif
