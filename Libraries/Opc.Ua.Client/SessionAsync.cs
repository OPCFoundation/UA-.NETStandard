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
        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionAsync(Subscription subscription, CancellationToken ct = default)
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription));

            if (subscription.Created)
            {
                await subscription.DeleteAsync(false, ct).ConfigureAwait(false);
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

        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionsAsync(IEnumerable<Subscription> subscriptions, CancellationToken ct = default)
        {
            if (subscriptions == null) throw new ArgumentNullException(nameof(subscriptions));

            List<Subscription> subscriptionsToDelete = new List<Subscription>();

            bool removed = PrepareSubscriptionsToDelete(subscriptions, subscriptionsToDelete);

            foreach (Subscription subscription in subscriptionsToDelete)
            {
                await subscription.DeleteAsync(true, ct).ConfigureAwait(false);
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

        /// <inheritdoc/>
        public async Task<bool> ReactivateSubscriptionsAsync(
            SubscriptionCollection subscriptions,
            bool sendInitialValues,
            CancellationToken ct = default)
        {
            UInt32Collection subscriptionIds = CreateSubscriptionIdsForTransfer(subscriptions);
            int failedSubscriptions = 0;

            if (subscriptionIds.Count > 0)
            {
                try
                {
                    await m_reconnectLock.WaitAsync().ConfigureAwait(false);
                    m_reconnecting = true;

                    IList<ServiceResult> resendResults= null;
                    if (sendInitialValues)
                    {
                        bool success;
                        (success, resendResults) = await ResendDataAsync(subscriptions, ct).ConfigureAwait(false);
                        if (!success)
                        {
                            Utils.LogError("Failed to call resend data for subscriptions.");
                        }
                    }

                    for (int ii = 0; ii < subscriptions.Count; ii++)
                    {
                        // no need to try for subscriptions which do not exist
                        if (resendResults != null &&
                            resendResults.Count > ii &&
                            StatusCode.IsNotGood(resendResults[ii].StatusCode))
                        {
                            Utils.LogError("SubscriptionId {0} failed to resend data and is not activated.", subscriptionIds[ii]);
                            failedSubscriptions++;
                            continue;
                        }

                        if (!await subscriptions[ii].TransferAsync(this, subscriptionIds[ii], new UInt32Collection(), ct).ConfigureAwait(false))
                        {
                            Utils.LogError("SubscriptionId {0} failed to reactivate.", subscriptionIds[ii]);
                            failedSubscriptions++;
                        }
                    }

                    Utils.LogInfo("Session REACTIVATE of {0} subscriptions completed. {1} failed.", subscriptions.Count, failedSubscriptions);
                }
                finally
                {
                    m_reconnecting = false;
                    m_reconnectLock.Release();
                }

                RestartPublishing();
            }
            else
            {
                Utils.LogInfo("No subscriptions. Transfersubscription skipped.");
            }

            return failedSubscriptions == 0;
        }

        /// <inheritdoc/>
        public async Task<(bool, IList<ServiceResult>)> ResendDataAsync(IEnumerable<Subscription> subscriptions, CancellationToken ct)
        {
            CallMethodRequestCollection requests = CreateCallRequestsForResendData(subscriptions);

            IList<ServiceResult> errors = new List<ServiceResult>(requests.Count);
            try
            {
                CallResponse response = await CallAsync(null, requests, ct).ConfigureAwait(false);
                CallMethodResultCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;
                ResponseHeader responseHeader = response.ResponseHeader;
                ClientBase.ValidateResponse(results, requests);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, requests);

                int ii = 0;
                foreach (var value in results)
                {
                    ServiceResult result = ServiceResult.Good;
                    if (StatusCode.IsNotGood(value.StatusCode))
                    {
                        result = ClientBase.GetResult(value.StatusCode, ii, diagnosticInfos, responseHeader);
                    }
                    errors.Add(result);
                    ii++;
                }

                return (true, errors);
            }
            catch (ServiceResultException sre)
            {
                Utils.LogError(sre, "Failed to call ResendData on server.");
            }

            return (false, errors);
        }

        /// <inheritdoc/>
        public async Task<bool> TransferSubscriptionsAsync(
            SubscriptionCollection subscriptions,
            bool sendInitialValues,
            CancellationToken ct)
        {
            UInt32Collection subscriptionIds = CreateSubscriptionIdsForTransfer(subscriptions);
            int failedSubscriptions = 0;

            if (subscriptionIds.Count > 0)
            {
                try
                {
                    await m_reconnectLock.WaitAsync().ConfigureAwait(false);
                    m_reconnecting = true;

                    TransferSubscriptionsResponse response = await base.TransferSubscriptionsAsync(null, subscriptionIds, sendInitialValues, ct).ConfigureAwait(false);
                    TransferResultCollection results = response.Results;
                    DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;
                    ResponseHeader responseHeader = response.ResponseHeader;

                    if (!StatusCode.IsGood(responseHeader.ServiceResult))
                    {
                        Utils.LogError("TransferSubscription failed: {0}", responseHeader.ServiceResult);
                        return false;
                    }

                    ClientBase.ValidateResponse(results, subscriptionIds);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, subscriptionIds);

                    for (int ii = 0; ii < subscriptions.Count; ii++)
                    {
                        if (StatusCode.IsGood(results[ii].StatusCode))
                        {
                            if (await subscriptions[ii].TransferAsync(this, subscriptionIds[ii], results[ii].AvailableSequenceNumbers).ConfigureAwait(false))
                            {
                                lock (SyncRoot)
                                {
                                    // create ack for available sequence numbers
                                    foreach (var sequenceNumber in results[ii].AvailableSequenceNumbers)
                                    {
                                        var ack = new SubscriptionAcknowledgement() {
                                            SubscriptionId = subscriptionIds[ii],
                                            SequenceNumber = sequenceNumber
                                        };
                                        m_acknowledgementsToSend.Add(ack);
                                    }
                                }
                            }
                        }
                        else if (results[ii].StatusCode == StatusCodes.BadNothingToDo)
                        {
                            Utils.LogInfo("SubscriptionId {0} is already member of the session.", subscriptionIds[ii]);
                            failedSubscriptions++;
                        }
                        else
                        {
                            Utils.LogError("SubscriptionId {0} failed to transfer, StatusCode={1}", subscriptionIds[ii], results[ii].StatusCode);
                            failedSubscriptions++;
                        }
                    }

                    Utils.LogInfo("Session TRANSFER ASYNC of {0} subscriptions completed. {1} failed.", subscriptions.Count, failedSubscriptions);
                }
                finally
                {
                    m_reconnecting = false;
                    m_reconnectLock.Release();
                }

                RestartPublishing();
            }
            else
            {
                Utils.LogInfo("No subscriptions. Transfersubscription skipped.");
            }

            return failedSubscriptions == 0;
        }
        #endregion

        #region ReadNode Async Methods
        /// <inheritdoc/>
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

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public Task<Node> ReadNodeAsync(
            NodeId nodeId,
            CancellationToken ct = default)
        {
            return ReadNodeAsync(nodeId, NodeClass.Unspecified, true, ct);
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
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


        /// <inheritdoc/>
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

        #region Call Methods
        /// <inheritdoc/>
        public async Task<IList<object>> CallAsync(NodeId objectId, NodeId methodId, CancellationToken ct = default, params object[] args)
        {
            VariantCollection inputArguments = new VariantCollection();

            if (args != null)
            {
                for (int ii = 0; ii < args.Length; ii++)
                {
                    inputArguments.Add(new Variant(args[ii]));
                }
            }

            CallMethodRequest request = new CallMethodRequest();

            request.ObjectId = objectId;
            request.MethodId = methodId;
            request.InputArguments = inputArguments;

            CallMethodRequestCollection requests = new CallMethodRequestCollection();
            requests.Add(request);

            CallMethodResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            CallResponse response = await base.CallAsync(null, requests, ct).ConfigureAwait(false);

            results = response.Results;
            diagnosticInfos = response.DiagnosticInfos;

            ClientBase.ValidateResponse(results, requests);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, requests);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw ServiceResultException.Create(results[0].StatusCode, 0, diagnosticInfos, response.ResponseHeader.StringTable);
            }

            List<object> outputArguments = new List<object>();

            foreach (Variant arg in results[0].OutputArguments)
            {
                outputArguments.Add(arg.Value);
            }

            return outputArguments;
        }
        #endregion

        #region Close Async Methods
        /// <inheritdoc/>
        public Task<StatusCode> CloseAsync(CancellationToken ct = default)
        {
            return CloseAsync(m_keepAliveInterval, true, ct);
        }

        /// <inheritdoc/>
        public Task<StatusCode> CloseAsync(bool closeChannel, CancellationToken ct = default)
        {
            return CloseAsync(m_keepAliveInterval, closeChannel, ct);
        }

        /// <inheritdoc/>
        public Task<StatusCode> CloseAsync(int timeout, CancellationToken ct = default)
            => CloseAsync(timeout, true, ct);

        /// <inheritdoc/>
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
                finally
                {
                    this.OperationTimeout = existingTimeout;
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
