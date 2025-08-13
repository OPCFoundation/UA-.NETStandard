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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Manages a session with a server.
    /// Contains the async versions of the public session api.
    /// </summary>
    public partial class Session : SessionClientBatched, ISession
    {
        /// <inheritdoc/>
        public Task OpenAsync(string sessionName, IUserIdentity identity, CancellationToken ct)
        {
            return OpenAsync(sessionName, 0, identity, null, ct);
        }

        /// <inheritdoc/>
        public Task OpenAsync(
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            CancellationToken ct)
        {
            return OpenAsync(sessionName, sessionTimeout, identity, preferredLocales, true, ct);
        }

        /// <inheritdoc/>
        public Task OpenAsync(
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            bool checkDomain,
            CancellationToken ct)
        {
            return OpenAsync(
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales,
                checkDomain,
                true,
                ct);
        }

        /// <inheritdoc/>
        public async Task OpenAsync(
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            bool checkDomain,
            bool closeChannel,
            CancellationToken ct)
        {
            OpenValidateIdentity(
                ref identity,
                out UserIdentityToken identityToken,
                out UserTokenPolicy identityPolicy,
                out string securityPolicyUri,
                out bool requireEncryption);

            // validate the server certificate /certificate chain.
            X509Certificate2 serverCertificate = null;
            byte[] certificateData = m_endpoint.Description.ServerCertificate;

            if (certificateData != null && certificateData.Length > 0)
            {
                X509Certificate2Collection serverCertificateChain = Utils.ParseCertificateChainBlob(
                    certificateData);

                if (serverCertificateChain.Count > 0)
                {
                    serverCertificate = serverCertificateChain[0];
                }

                if (requireEncryption)
                {
                    // validation skipped until IOP isses are resolved.
                    // ValidateServerCertificateApplicationUri(serverCertificate);
                    if (checkDomain)
                    {
                        await m_configuration
                            .CertificateValidator.ValidateAsync(
                                serverCertificateChain,
                                m_endpoint,
                                ct)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await m_configuration
                            .CertificateValidator.ValidateAsync(serverCertificateChain, ct)
                            .ConfigureAwait(false);
                    }
                    // save for reconnect
                    m_checkDomain = checkDomain;
                }
            }

            // create a nonce.
            uint length = (uint)m_configuration.SecurityConfiguration.NonceLength;
            byte[] clientNonce = Nonce.CreateRandomNonceData(length);

            // send the application instance certificate for the client.
            BuildCertificateData(
                out byte[] clientCertificateData,
                out byte[] clientCertificateChainData);

            var clientDescription = new ApplicationDescription
            {
                ApplicationUri = m_configuration.ApplicationUri,
                ApplicationName = m_configuration.ApplicationName,
                ApplicationType = ApplicationType.Client,
                ProductUri = m_configuration.ProductUri
            };

            if (sessionTimeout == 0)
            {
                sessionTimeout = (uint)m_configuration.ClientConfiguration.DefaultSessionTimeout;
            }

            // select the security policy for the user token.
            RequestHeader requestHeader = CreateRequestHeaderPerUserTokenPolicy(
                identityPolicy.SecurityPolicyUri,
                m_endpoint.Description.SecurityPolicyUri);

            bool successCreateSession = false;
            CreateSessionResponse response = null;

            //if security none, first try to connect without certificate
            if (m_endpoint.Description.SecurityPolicyUri == SecurityPolicies.None)
            {
                //first try to connect with client certificate NULL
                try
                {
                    response = await base.CreateSessionAsync(
                            null,
                            clientDescription,
                            m_endpoint.Description.Server.ApplicationUri,
                            m_endpoint.EndpointUrl.ToString(),
                            sessionName,
                            clientNonce,
                            null,
                            sessionTimeout,
                            (uint)MessageContext.MaxMessageSize,
                            ct)
                        .ConfigureAwait(false);

                    successCreateSession = true;
                }
                catch (Exception ex)
                {
                    Utils.LogInfo(
                        "Create session failed with client certificate NULL. " + ex.Message);
                    successCreateSession = false;
                }
            }

            if (!successCreateSession)
            {
                response = await base.CreateSessionAsync(
                        requestHeader,
                        clientDescription,
                        m_endpoint.Description.Server.ApplicationUri,
                        m_endpoint.EndpointUrl.ToString(),
                        sessionName,
                        clientNonce,
                        clientCertificateChainData ?? clientCertificateData,
                        sessionTimeout,
                        (uint)MessageContext.MaxMessageSize,
                        ct)
                    .ConfigureAwait(false);
            }

            NodeId sessionId = response.SessionId;
            NodeId sessionCookie = response.AuthenticationToken;
            byte[] serverNonce = response.ServerNonce;
            byte[] serverCertificateData = response.ServerCertificate;
            SignatureData serverSignature = response.ServerSignature;
            EndpointDescriptionCollection serverEndpoints = response.ServerEndpoints;
            SignedSoftwareCertificateCollection serverSoftwareCertificates = response
                .ServerSoftwareCertificates;

            m_sessionTimeout = response.RevisedSessionTimeout;
            m_maxRequestMessageSize = response.MaxRequestMessageSize;

            // save session id.
            lock (SyncRoot)
            {
                // save session id and cookie in base
                base.SessionCreated(sessionId, sessionCookie);
            }

            Utils.LogInfo("Revised session timeout value: {0}. ", m_sessionTimeout);
            Utils.LogInfo(
                "Max response message size value: {0}. Max request message size: {1} ",
                MessageContext.MaxMessageSize,
                m_maxRequestMessageSize);

            //we need to call CloseSession if CreateSession was successful but some other exception is thrown
            try
            {
                // verify that the server returned the same instance certificate.
                ValidateServerCertificateData(serverCertificateData);

                ValidateServerEndpoints(serverEndpoints);

                ValidateServerSignature(
                    serverCertificate,
                    serverSignature,
                    clientCertificateData,
                    clientCertificateChainData,
                    clientNonce);

                HandleSignedSoftwareCertificates(serverSoftwareCertificates);

                //  process additional header
                ProcessResponseAdditionalHeader(response.ResponseHeader, serverCertificate);

                // create the client signature.
                byte[] dataToSign = Utils.Append(serverCertificate?.RawData, serverNonce);
                SignatureData clientSignature = SecurityPolicies.Sign(
                    m_instanceCertificate,
                    securityPolicyUri,
                    dataToSign);

                // select the security policy for the user token.
                string tokenSecurityPolicyUri = identityPolicy.SecurityPolicyUri;

                if (string.IsNullOrEmpty(tokenSecurityPolicyUri))
                {
                    tokenSecurityPolicyUri = m_endpoint.Description.SecurityPolicyUri;
                }

                // save previous nonce
                byte[] previousServerNonce = GetCurrentTokenServerNonce();

                // validate server nonce and security parameters for user identity.
                ValidateServerNonce(
                    identity,
                    serverNonce,
                    tokenSecurityPolicyUri,
                    previousServerNonce,
                    m_endpoint.Description.SecurityMode);

                // sign data with user token.
                SignatureData userTokenSignature = identityToken.Sign(
                    dataToSign,
                    tokenSecurityPolicyUri);

                // encrypt token.
                identityToken.Encrypt(
                    serverCertificate,
                    serverNonce,
                    m_userTokenSecurityPolicyUri,
                    m_eccServerEphemeralKey,
                    m_instanceCertificate,
                    m_instanceCertificateChain,
                    m_endpoint.Description.SecurityMode != MessageSecurityMode.None);

                // send the software certificates assigned to the client.
                SignedSoftwareCertificateCollection clientSoftwareCertificates
                    = GetSoftwareCertificates();

                // copy the preferred locales if provided.
                if (preferredLocales != null && preferredLocales.Count > 0)
                {
                    m_preferredLocales = [.. preferredLocales];
                }

                // activate session.
                ActivateSessionResponse activateResponse = await ActivateSessionAsync(
                        null,
                        clientSignature,
                        clientSoftwareCertificates,
                        m_preferredLocales,
                        new ExtensionObject(identityToken),
                        userTokenSignature,
                        ct)
                    .ConfigureAwait(false);

                //  process additional header
                ProcessResponseAdditionalHeader(activateResponse.ResponseHeader, serverCertificate);

                serverNonce = activateResponse.ServerNonce;
                StatusCodeCollection certificateResults = activateResponse.Results;
                DiagnosticInfoCollection certificateDiagnosticInfos = activateResponse
                    .DiagnosticInfos;

                if (certificateResults != null)
                {
                    for (int i = 0; i < certificateResults.Count; i++)
                    {
                        Utils.LogInfo(
                            "ActivateSession result[{0}] = {1}",
                            i,
                            certificateResults[i]);
                    }
                }

                if (clientSoftwareCertificates?.Count > 0 &&
                    (certificateResults == null || certificateResults.Count == 0))
                {
                    Utils.LogInfo("Empty results were received for the ActivateSession call.");
                }

                // fetch namespaces.
                await FetchNamespaceTablesAsync(ct).ConfigureAwait(false);

                lock (SyncRoot)
                {
                    // save nonces.
                    m_sessionName = sessionName;
                    m_identity = identity;
                    m_previousServerNonce = previousServerNonce;
                    m_serverNonce = serverNonce;
                    m_serverCertificate = serverCertificate;

                    // update system context.
                    m_systemContext.PreferredLocales = m_preferredLocales;
                    m_systemContext.SessionId = SessionId;
                    m_systemContext.UserIdentity = identity;
                }

                // fetch operation limits
                await FetchOperationLimitsAsync(ct).ConfigureAwait(false);

                // start keep alive thread.
                StartKeepAliveTimer();

                // raise event that session configuration changed.
                IndicateSessionConfigurationChanged();

                // call session created callback, which was already set in base class only.
                SessionCreated(sessionId, sessionCookie);
            }
            catch (Exception)
            {
                try
                {
                    await base.CloseSessionAsync(null, false, CancellationToken.None)
                        .ConfigureAwait(false);
                    if (closeChannel)
                    {
                        await CloseChannelAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    Utils.LogError(
                        "Cleanup: CloseSessionAsync() or CloseChannelAsync() raised exception. " +
                        e.Message);
                }
                finally
                {
                    SessionCreated(null, null);
                }

                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionAsync(
            Subscription subscription,
            CancellationToken ct = default)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }

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

            m_SubscriptionsChanged?.Invoke(this, null);

            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionsAsync(
            IEnumerable<Subscription> subscriptions,
            CancellationToken ct = default)
        {
            if (subscriptions == null)
            {
                throw new ArgumentNullException(nameof(subscriptions));
            }

            var subscriptionsToDelete = new List<Subscription>();

            bool removed = PrepareSubscriptionsToDelete(subscriptions, subscriptionsToDelete);

            foreach (Subscription subscription in subscriptionsToDelete)
            {
                await subscription.DeleteAsync(true, ct).ConfigureAwait(false);
            }

            if (removed)
            {
                m_SubscriptionsChanged?.Invoke(this, null);
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
                bool reconnecting = false;
                await m_reconnectLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    reconnecting = m_reconnecting;
                    m_reconnecting = true;

                    for (int ii = 0; ii < subscriptions.Count; ii++)
                    {
                        if (!await subscriptions[ii]
                                .TransferAsync(this, subscriptionIds[ii], [], ct)
                                .ConfigureAwait(false))
                        {
                            Utils.LogError(
                                "SubscriptionId {0} failed to reactivate.",
                                subscriptionIds[ii]);
                            failedSubscriptions++;
                        }
                    }

                    if (sendInitialValues)
                    {
                        (bool success, IList<ServiceResult> resendResults) = await ResendDataAsync(
                            subscriptions,
                            ct)
                            .ConfigureAwait(false);
                        if (!success)
                        {
                            Utils.LogError("Failed to call resend data for subscriptions.");
                        }
                        else if (resendResults != null)
                        {
                            for (int ii = 0; ii < resendResults.Count; ii++)
                            {
                                // no need to try for subscriptions which do not exist
                                if (StatusCode.IsNotGood(resendResults[ii].StatusCode))
                                {
                                    Utils.LogError(
                                        "SubscriptionId {0} failed to resend data.",
                                        subscriptionIds[ii]);
                                }
                            }
                        }
                    }

                    Utils.LogInfo(
                        "Session REACTIVATE of {0} subscriptions completed. {1} failed.",
                        subscriptions.Count,
                        failedSubscriptions);
                }
                finally
                {
                    m_reconnecting = reconnecting;
                    m_reconnectLock.Release();
                }

                StartPublishing(OperationTimeout, true);
            }
            else
            {
                Utils.LogInfo("No subscriptions. TransferSubscription skipped.");
            }

            return failedSubscriptions == 0;
        }

        /// <inheritdoc/>
        public async Task<(bool, IList<ServiceResult>)> ResendDataAsync(
            IEnumerable<Subscription> subscriptions,
            CancellationToken ct)
        {
            CallMethodRequestCollection requests = CreateCallRequestsForResendData(subscriptions);

            var errors = new List<ServiceResult>(requests.Count);
            try
            {
                CallResponse response = await CallAsync(null, requests, ct).ConfigureAwait(false);
                CallMethodResultCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;
                ResponseHeader responseHeader = response.ResponseHeader;
                ValidateResponse(results, requests);
                ValidateDiagnosticInfos(diagnosticInfos, requests);

                int ii = 0;
                foreach (CallMethodResult value in results)
                {
                    ServiceResult result = ServiceResult.Good;
                    if (StatusCode.IsNotGood(value.StatusCode))
                    {
                        result = GetResult(value.StatusCode, ii, diagnosticInfos, responseHeader);
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
                bool reconnecting = false;
                await m_reconnectLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    reconnecting = m_reconnecting;
                    m_reconnecting = true;

                    TransferSubscriptionsResponse response = await base.TransferSubscriptionsAsync(
                            null,
                            subscriptionIds,
                            sendInitialValues,
                            ct)
                        .ConfigureAwait(false);
                    TransferResultCollection results = response.Results;
                    DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;
                    ResponseHeader responseHeader = response.ResponseHeader;

                    if (!StatusCode.IsGood(responseHeader.ServiceResult))
                    {
                        Utils.LogError(
                            "TransferSubscription failed: {0}",
                            responseHeader.ServiceResult);
                        return false;
                    }

                    ValidateResponse(results, subscriptionIds);
                    ValidateDiagnosticInfos(diagnosticInfos, subscriptionIds);

                    for (int ii = 0; ii < subscriptions.Count; ii++)
                    {
                        if (StatusCode.IsGood(results[ii].StatusCode))
                        {
                            if (await subscriptions[ii]
                                    .TransferAsync(
                                        this,
                                        subscriptionIds[ii],
                                        results[ii].AvailableSequenceNumbers,
                                        ct)
                                    .ConfigureAwait(false))
                            {
                                lock (m_acknowledgementsToSendLock)
                                {
                                    // create ack for available sequence numbers
                                    foreach (uint sequenceNumber in results[ii]
                                        .AvailableSequenceNumbers)
                                    {
                                        AddAcknowledgementToSend(
                                            m_acknowledgementsToSend,
                                            subscriptionIds[ii],
                                            sequenceNumber);
                                    }
                                }
                            }
                        }
                        else if (results[ii].StatusCode == StatusCodes.BadNothingToDo)
                        {
                            Utils.LogInfo(
                                "SubscriptionId {0} is already member of the session.",
                                subscriptionIds[ii]);
                            failedSubscriptions++;
                        }
                        else
                        {
                            Utils.LogError(
                                "SubscriptionId {0} failed to transfer, StatusCode={1}",
                                subscriptionIds[ii],
                                results[ii].StatusCode);
                            failedSubscriptions++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Utils.LogError(
                        "Session TRANSFER ASYNC of {0} subscriptions Failed due to unexpected Exception {1}",
                        subscriptions.Count,
                        ex.Message);
                    failedSubscriptions++;
                }
                finally
                {
                    m_reconnecting = reconnecting;
                    m_reconnectLock.Release();
                }

                StartPublishing(OperationTimeout, false);
            }
            else
            {
                Utils.LogInfo("No subscriptions. TransferSubscription skipped.");
            }

            return failedSubscriptions == 0;
        }

        /// <inheritdoc/>
        public async Task FetchNamespaceTablesAsync(CancellationToken ct = default)
        {
            ReadValueIdCollection nodesToRead = PrepareNamespaceTableNodesToRead();

            // read from server.
            ReadResponse response = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection values = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;
            ResponseHeader responseHeader = response.ResponseHeader;

            ValidateResponse(values, nodesToRead);
            ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            UpdateNamespaceTable(values, diagnosticInfos, responseHeader);
        }

        /// <inheritdoc/>
        public async Task FetchTypeTreeAsync(ExpandedNodeId typeId, CancellationToken ct = default)
        {
            if (await NodeCache.FindAsync(typeId, ct).ConfigureAwait(false) is Node node)
            {
                var subTypes = new ExpandedNodeIdCollection();
                foreach (IReference reference in node.Find(ReferenceTypeIds.HasSubtype, false))
                {
                    subTypes.Add(reference.TargetId);
                }
                if (subTypes.Count > 0)
                {
                    await FetchTypeTreeAsync(subTypes, ct).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc/>
        public async Task FetchTypeTreeAsync(
            ExpandedNodeIdCollection typeIds,
            CancellationToken ct = default)
        {
            var referenceTypeIds = new NodeIdCollection { ReferenceTypeIds.HasSubtype };
            IList<INode> nodes = await NodeCache
                .FindReferencesAsync(typeIds, referenceTypeIds, false, false, ct)
                .ConfigureAwait(false);
            var subTypes = new ExpandedNodeIdCollection();
            foreach (INode inode in nodes)
            {
                if (inode is Node node)
                {
                    foreach (IReference reference in node.Find(ReferenceTypeIds.HasSubtype, false))
                    {
                        if (!typeIds.Contains(reference.TargetId))
                        {
                            subTypes.Add(reference.TargetId);
                        }
                    }
                }
            }
            if (subTypes.Count > 0)
            {
                await FetchTypeTreeAsync(subTypes, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Fetch the operation limits of the server.
        /// </summary>
        public async Task FetchOperationLimitsAsync(CancellationToken ct)
        {
            try
            {
                var operationLimitsProperties = typeof(OperationLimits).GetProperties()
                    .Select(p => p.Name)
                    .ToList();

                var nodeIds = new NodeIdCollection(
                    operationLimitsProperties.Select(name =>
                        (NodeId)
                            typeof(VariableIds)
                                .GetField(
                                    "Server_ServerCapabilities_OperationLimits_" + name,
                                    BindingFlags.Public | BindingFlags.Static)
                                .GetValue(null)))
                {
                    // add the server capability MaxContinuationPointPerBrowse and MaxByteStringLength
                    VariableIds.Server_ServerCapabilities_MaxBrowseContinuationPoints
                };
                int maxBrowseContinuationPointIndex = nodeIds.Count - 1;

                nodeIds.Add(VariableIds.Server_ServerCapabilities_MaxByteStringLength);
                int maxByteStringLengthIndex = nodeIds.Count - 1;

                (DataValueCollection values, IList<ServiceResult> errors) = await ReadValuesAsync(
                    nodeIds,
                    ct)
                    .ConfigureAwait(false);

                OperationLimits configOperationLimits =
                    m_configuration?.ClientConfiguration?.OperationLimits ?? new OperationLimits();
                var operationLimits = new OperationLimits();

                for (int ii = 0; ii < operationLimitsProperties.Count; ii++)
                {
                    PropertyInfo property = typeof(OperationLimits).GetProperty(
                        operationLimitsProperties[ii]);
                    uint value = (uint)property.GetValue(configOperationLimits);
                    if (values[ii] != null &&
                        ServiceResult.IsNotBad(errors[ii]) &&
                        values[ii].Value is uint serverValue &&
                        serverValue > 0 &&
                        (value == 0 || serverValue < value))
                    {
                        value = serverValue;
                    }
                    property.SetValue(operationLimits, value);
                }
                OperationLimits = operationLimits;

                if (values[maxBrowseContinuationPointIndex]
                        .Value is ushort serverMaxContinuationPointsPerBrowse &&
                    ServiceResult.IsNotBad(errors[maxBrowseContinuationPointIndex]))
                {
                    ServerMaxContinuationPointsPerBrowse = serverMaxContinuationPointsPerBrowse;
                }

                if (values[maxByteStringLengthIndex].Value is uint serverMaxByteStringLength &&
                    ServiceResult.IsNotBad(errors[maxByteStringLengthIndex]))
                {
                    ServerMaxByteStringLength = serverMaxByteStringLength;
                }
            }
            catch (Exception ex)
            {
                Utils.LogError(
                    ex,
                    "Failed to read operation limits from server. Using configuration defaults.");
                OperationLimits operationLimits = m_configuration?.ClientConfiguration?
                    .OperationLimits;
                if (operationLimits != null)
                {
                    OperationLimits = operationLimits;
                }
            }
        }

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
                nodeIds,
                nodeClass,
                attributesToRead,
                attributesPerNodeId,
                nodeCollection,
                optionalAttributes);

            ReadResponse readResponse = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                attributesToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ValidateResponse(values, attributesToRead);
            ValidateDiagnosticInfos(diagnosticInfos, attributesToRead);

            List<ServiceResult> serviceResults = new ServiceResult[nodeIds.Count].ToList();
            ProcessAttributesReadNodesResponse(
                readResponse.ResponseHeader,
                attributesToRead,
                attributesPerNodeId,
                values,
                diagnosticInfos,
                nodeCollection,
                serviceResults);

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
            itemsToRead =
            [
                .. nodeIds.Select(nodeId => new ReadValueId {
                    NodeId = nodeId,
                    AttributeId = Attributes.NodeClass })
            ];

            ReadResponse readResponse = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                itemsToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection nodeClassValues = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ValidateResponse(nodeClassValues, itemsToRead);
            ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            // second determine attributes to read per nodeclass
            var attributesPerNodeId = new List<IDictionary<uint, DataValue>>(nodeIds.Count);
            var serviceResults = new List<ServiceResult>(nodeIds.Count);
            var attributesToRead = new ReadValueIdCollection();

            CreateAttributesReadNodesRequest(
                readResponse.ResponseHeader,
                itemsToRead,
                nodeClassValues,
                diagnosticInfos,
                attributesToRead,
                attributesPerNodeId,
                nodeCollection,
                serviceResults,
                optionalAttributes);

            if (attributesToRead.Count > 0)
            {
                readResponse = await ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    attributesToRead,
                    ct)
                    .ConfigureAwait(false);

                DataValueCollection values = readResponse.Results;
                diagnosticInfos = readResponse.DiagnosticInfos;

                ValidateResponse(values, attributesToRead);
                ValidateDiagnosticInfos(diagnosticInfos, attributesToRead);

                ProcessAttributesReadNodesResponse(
                    readResponse.ResponseHeader,
                    attributesToRead,
                    attributesPerNodeId,
                    values,
                    diagnosticInfos,
                    nodeCollection,
                    serviceResults);
            }

            return (nodeCollection, serviceResults);
        }

        /// <inheritdoc/>
        public Task<Node> ReadNodeAsync(NodeId nodeId, CancellationToken ct = default)
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
            IDictionary<uint, DataValue> attributes = CreateAttributes(
                nodeClass,
                optionalAttributes);

            // build list of values to read.
            var itemsToRead = new ReadValueIdCollection();
            foreach (uint attributeId in attributes.Keys)
            {
                var itemToRead = new ReadValueId { NodeId = nodeId, AttributeId = attributeId };
                itemsToRead.Add(itemToRead);
            }

            // read from server.
            ReadResponse readResponse = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                itemsToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ValidateResponse(values, itemsToRead);
            ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            return ProcessReadResponse(
                readResponse.ResponseHeader,
                attributes,
                itemsToRead,
                values,
                diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<DataValue> ReadValueAsync(NodeId nodeId, CancellationToken ct = default)
        {
            var itemToRead = new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value };

            var itemsToRead = new ReadValueIdCollection { itemToRead };

            // read from server.
            ReadResponse readResponse = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                itemsToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ValidateResponse(values, itemsToRead);
            ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            if (StatusCode.IsBad(values[0].StatusCode))
            {
                ServiceResult result = GetResult(
                    values[0].StatusCode,
                    0,
                    diagnosticInfos,
                    readResponse.ResponseHeader);
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
                nodeIds.Select(
                    nodeId => new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }));

            // read from server.
            var errors = new List<ServiceResult>(itemsToRead.Count);

            ReadResponse readResponse = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                itemsToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ValidateResponse(values, itemsToRead);
            ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            foreach (DataValue value in values)
            {
                ServiceResult result = ServiceResult.Good;
                if (StatusCode.IsBad(value.StatusCode))
                {
                    result = GetResult(
                        values[0].StatusCode,
                        0,
                        diagnosticInfos,
                        readResponse.ResponseHeader);
                }
                errors.Add(result);
            }

            return (values, errors);
        }

        /// <inheritdoc/>
        public async Task<byte[]> ReadByteStringInChunksAsync(NodeId nodeId, CancellationToken ct)
        {
            int count = (int)ServerMaxByteStringLength;

            int maxByteStringLength = m_configuration.TransportQuotas.MaxByteStringLength;
            if (maxByteStringLength > 0)
            {
                count =
                    ServerMaxByteStringLength > maxByteStringLength
                        ? maxByteStringLength
                        : (int)ServerMaxByteStringLength;
            }

            if (count <= 1)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIndexRangeNoData,
                    "The MaxByteStringLength is not known or too small for reading data in chunks.");
            }

            int offset = 0;
            using var bytes = new MemoryStream();
            while (true)
            {
                var valueToRead = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    IndexRange = new NumericRange(offset, offset + count - 1).ToString(),
                    DataEncoding = null
                };
                var readValueIds = new ReadValueIdCollection { valueToRead };

                ReadResponse result = await ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    readValueIds,
                    ct)
                    .ConfigureAwait(false);

                ResponseHeader responseHeader = result.ResponseHeader;
                DataValueCollection results = result.Results;
                DiagnosticInfoCollection diagnosticInfos = result.DiagnosticInfos;
                ValidateResponse(results, readValueIds);
                ValidateDiagnosticInfos(diagnosticInfos, readValueIds);

                if (offset == 0)
                {
                    Variant wrappedValue = results[0].WrappedValue;
                    if (wrappedValue.TypeInfo.BuiltInType != BuiltInType.ByteString ||
                        wrappedValue.TypeInfo.ValueRank != ValueRanks.Scalar)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTypeMismatch,
                            "Value is not a ByteString scalar.");
                    }
                }

                if (StatusCode.IsBad(results[0].StatusCode))
                {
                    if (results[0].StatusCode == StatusCodes.BadIndexRangeNoData)
                    {
                        // this happens when the previous read has fetched all remaining data
                        break;
                    }
                    ServiceResult serviceResult = GetResult(
                        results[0].StatusCode,
                        0,
                        diagnosticInfos,
                        responseHeader);
                    throw new ServiceResultException(serviceResult);
                }

                if (results[0].Value is not byte[] chunk || chunk.Length == 0)
                {
                    break;
                }

                bytes.Write(chunk, 0, chunk.Length);
                if (chunk.Length < count)
                {
                    break;
                }
                offset += count;
            }
            return bytes.ToArray();
        }

        /// <inheritdoc/>
        public async Task<(
            ResponseHeader responseHeader,
            ByteStringCollection continuationPoints,
            IList<ReferenceDescriptionCollection> referencesList,
            IList<ServiceResult> errors
        )> BrowseAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            IList<NodeId> nodesToBrowse,
            uint maxResultsToReturn,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            CancellationToken ct = default)
        {
            var browseDescriptions = new BrowseDescriptionCollection();
            foreach (NodeId nodeToBrowse in nodesToBrowse)
            {
                var description = new BrowseDescription
                {
                    NodeId = nodeToBrowse,
                    BrowseDirection = browseDirection,
                    ReferenceTypeId = referenceTypeId,
                    IncludeSubtypes = includeSubtypes,
                    NodeClassMask = nodeClassMask,
                    ResultMask = (uint)BrowseResultMask.All
                };

                browseDescriptions.Add(description);
            }

            BrowseResponse browseResponse = await BrowseAsync(
                    requestHeader,
                    view,
                    maxResultsToReturn,
                    browseDescriptions,
                    ct)
                .ConfigureAwait(false);

            ValidateResponse(browseResponse.ResponseHeader);
            BrowseResultCollection results = browseResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = browseResponse.DiagnosticInfos;

            ValidateResponse(results, browseDescriptions);
            ValidateDiagnosticInfos(diagnosticInfos, browseDescriptions);

            int ii = 0;
            var errors = new List<ServiceResult>();
            var continuationPoints = new ByteStringCollection();
            var referencesList = new List<ReferenceDescriptionCollection>();
            foreach (BrowseResult result in results)
            {
                if (StatusCode.IsBad(result.StatusCode))
                {
                    errors.Add(
                        new ServiceResult(
                            result.StatusCode,
                            ii,
                            diagnosticInfos,
                            browseResponse.ResponseHeader.StringTable));
                }
                else
                {
                    errors.Add(ServiceResult.Good);
                }
                continuationPoints.Add(result.ContinuationPoint);
                referencesList.Add(result.References);
                ii++;
            }

            return (browseResponse.ResponseHeader, continuationPoints, referencesList, errors);
        }

        /// <inheritdoc/>
        public async Task<(
            ResponseHeader responseHeader,
            ByteStringCollection revisedContinuationPoints,
            IList<ReferenceDescriptionCollection> referencesList,
            IList<ServiceResult> errors
        )> BrowseNextAsync(
            RequestHeader requestHeader,
            ByteStringCollection continuationPoints,
            bool releaseContinuationPoint,
            CancellationToken ct = default)
        {
            BrowseNextResponse response = await base.BrowseNextAsync(
                    requestHeader,
                    releaseContinuationPoint,
                    continuationPoints,
                    ct)
                .ConfigureAwait(false);

            ValidateResponse(response.ResponseHeader);

            BrowseResultCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

            ValidateResponse(results, continuationPoints);
            ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);

            int ii = 0;
            var errors = new List<ServiceResult>();
            var revisedContinuationPoints = new ByteStringCollection();
            var referencesList = new List<ReferenceDescriptionCollection>();
            foreach (BrowseResult result in results)
            {
                if (StatusCode.IsBad(result.StatusCode))
                {
                    errors.Add(
                        new ServiceResult(
                            result.StatusCode,
                            ii,
                            diagnosticInfos,
                            response.ResponseHeader.StringTable));
                }
                else
                {
                    errors.Add(ServiceResult.Good);
                }
                revisedContinuationPoints.Add(result.ContinuationPoint);
                referencesList.Add(result.References);
                ii++;
            }

            return (response.ResponseHeader, revisedContinuationPoints, referencesList, errors);
        }

        /// <inheritdoc/>
        public async Task<(IList<ReferenceDescriptionCollection>, IList<ServiceResult>)> ManagedBrowseAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            IList<NodeId> nodesToBrowse,
            uint maxResultsToReturn,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            CancellationToken ct = default)
        {
            int count = nodesToBrowse.Count;
            var result = new List<ReferenceDescriptionCollection>(count);
            var errors = new List<ServiceResult>(count);

            // first attempt for implementation: create the references for the output in advance.
            // optimize later, when everything works fine.
            for (int i = 0; i < nodesToBrowse.Count; i++)
            {
                result.Add([]);
                errors.Add(new ServiceResult(StatusCodes.Good));
            }

            try
            {
                // in the first pass, we browse all nodes from the input.
                // Some nodes may need to be browsed again, these are then fed into the next pass.
                var nodesToBrowseForPass = new List<NodeId>(count);
                nodesToBrowseForPass.AddRange(nodesToBrowse);

                var resultForPass = new List<ReferenceDescriptionCollection>(count);
                resultForPass.AddRange(result);

                var errorsForPass = new List<ServiceResult>(count);
                errorsForPass.AddRange(errors);

                int passCount = 0;

                do
                {
                    int badNoCPErrorsPerPass = 0;
                    int badCPInvalidErrorsPerPass = 0;
                    int otherErrorsPerPass = 0;
                    uint maxNodesPerBrowse = OperationLimits.MaxNodesPerBrowse;

                    if (ContinuationPointPolicy == ContinuationPointPolicy.Balanced &&
                        ServerMaxContinuationPointsPerBrowse > 0)
                    {
                        maxNodesPerBrowse =
                            ServerMaxContinuationPointsPerBrowse < maxNodesPerBrowse
                                ? ServerMaxContinuationPointsPerBrowse
                                : maxNodesPerBrowse;
                    }

                    // split input into batches
                    int batchOffset = 0;

                    var nodesToBrowseForNextPass = new List<NodeId>();
                    var referenceDescriptionsForNextPass
                        = new List<ReferenceDescriptionCollection>();
                    var errorsForNextPass = new List<ServiceResult>();

                    // loop over the batches
                    foreach (
                        List<NodeId> nodesToBrowseBatch in nodesToBrowseForPass
                            .Batch<NodeId, List<NodeId>>(
                                maxNodesPerBrowse))
                    {
                        int nodesToBrowseBatchCount = nodesToBrowseBatch.Count;

                        (IList<ReferenceDescriptionCollection> resultForBatch, IList<ServiceResult> errorsForBatch) =
                            await BrowseWithBrowseNextAsync(
                                    requestHeader,
                                    view,
                                    nodesToBrowseBatch,
                                    maxResultsToReturn,
                                    browseDirection,
                                    referenceTypeId,
                                    includeSubtypes,
                                    nodeClassMask,
                                    ct)
                                .ConfigureAwait(false);

                        int resultOffset = batchOffset;
                        for (int ii = 0; ii < nodesToBrowseBatchCount; ii++)
                        {
                            StatusCode statusCode = errorsForBatch[ii].StatusCode;
                            if (StatusCode.IsBad(statusCode))
                            {
                                bool addToNextPass = false;
                                if (statusCode == StatusCodes.BadNoContinuationPoints)
                                {
                                    addToNextPass = true;
                                    badNoCPErrorsPerPass++;
                                }
                                else if (statusCode == StatusCodes.BadContinuationPointInvalid)
                                {
                                    addToNextPass = true;
                                    badCPInvalidErrorsPerPass++;
                                }
                                else
                                {
                                    otherErrorsPerPass++;
                                }

                                if (addToNextPass)
                                {
                                    nodesToBrowseForNextPass.Add(
                                        nodesToBrowseForPass[resultOffset]);
                                    referenceDescriptionsForNextPass.Add(
                                        resultForPass[resultOffset]);
                                    errorsForNextPass.Add(errorsForPass[resultOffset]);
                                }
                            }

                            resultForPass[resultOffset].Clear();
                            resultForPass[resultOffset].AddRange(resultForBatch[ii]);
                            errorsForPass[resultOffset] = errorsForBatch[ii];
                            resultOffset++;
                        }

                        batchOffset += nodesToBrowseBatchCount;
                    }

                    resultForPass = referenceDescriptionsForNextPass;
                    referenceDescriptionsForNextPass = [];

                    errorsForPass = errorsForNextPass;
                    errorsForNextPass = [];

                    nodesToBrowseForPass = nodesToBrowseForNextPass;
                    nodesToBrowseForNextPass = [];

                    const string aggregatedErrorMessage =
                        "ManagedBrowse: in pass {0}, {1} {2} occured with a status code {3}.";

                    if (badCPInvalidErrorsPerPass > 0)
                    {
                        Utils.LogDebug(
                            aggregatedErrorMessage,
                            passCount,
                            badCPInvalidErrorsPerPass,
                            badCPInvalidErrorsPerPass == 1 ? "error" : "errors",
                            nameof(StatusCodes.BadContinuationPointInvalid));
                    }
                    if (badNoCPErrorsPerPass > 0)
                    {
                        Utils.LogDebug(
                            aggregatedErrorMessage,
                            passCount,
                            badNoCPErrorsPerPass,
                            badNoCPErrorsPerPass == 1 ? "error" : "errors",
                            nameof(StatusCodes.BadNoContinuationPoints));
                    }
                    if (otherErrorsPerPass > 0)
                    {
                        Utils.LogDebug(
                            aggregatedErrorMessage,
                            passCount,
                            otherErrorsPerPass,
                            otherErrorsPerPass == 1 ? "error" : "errors",
                            $"different from {nameof(StatusCodes.BadNoContinuationPoints)} or {nameof(StatusCodes.BadContinuationPointInvalid)}");
                    }
                    if (otherErrorsPerPass == 0 &&
                        badCPInvalidErrorsPerPass == 0 &&
                        badNoCPErrorsPerPass == 0)
                    {
                        Utils.LogTrace("ManagedBrowse completed with no errors.");
                    }

                    passCount++;
                } while (nodesToBrowseForPass.Count > 0);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "ManagedBrowse failed");
            }

            return (result, errors);
        }

        /// <summary>
        /// Used to pass on references to the Service results in the loop in ManagedBrowseAsync.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private class ReferenceWrapper<T>
        {
            public T Reference { get; set; }
        }

        /// <summary>
        /// Call the browse service asynchronously and call browse next,
        /// if applicable, immediately afterwards. Observe proper treatment
        /// of specific service results, specifically
        /// BadNoContinuationPoint and BadContinuationPointInvalid
        /// </summary>
        private async Task<(IList<ReferenceDescriptionCollection>, IList<ServiceResult>)> BrowseWithBrowseNextAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            List<NodeId> nodeIds,
            uint maxResultsToReturn,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            CancellationToken ct = default)
        {
            if (requestHeader != null)
            {
                requestHeader.RequestHandle = 0;
            }

            var result = new List<ReferenceDescriptionCollection>(nodeIds.Count);

            (
                _,
                ByteStringCollection continuationPoints,
                IList<ReferenceDescriptionCollection> referenceDescriptions,
                IList<ServiceResult> errors
            ) = await BrowseAsync(
                    requestHeader,
                    view,
                    nodeIds,
                    maxResultsToReturn,
                    browseDirection,
                    referenceTypeId,
                    includeSubtypes,
                    nodeClassMask,
                    ct)
                .ConfigureAwait(false);

            result.AddRange(referenceDescriptions);

            // process any continuation point.
            List<ReferenceDescriptionCollection> previousResults = result;
            var errorAnchors = new List<ReferenceWrapper<ServiceResult>>();
            var previousErrors = new List<ReferenceWrapper<ServiceResult>>();
            foreach (ServiceResult error in errors)
            {
                previousErrors.Add(new ReferenceWrapper<ServiceResult> { Reference = error });
                errorAnchors.Add(previousErrors[^1]);
            }

            var nextContinuationPoints = new ByteStringCollection();
            var nextResults = new List<ReferenceDescriptionCollection>();
            var nextErrors = new List<ReferenceWrapper<ServiceResult>>();

            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                if (continuationPoints[ii] != null &&
                    !StatusCode.IsBad(previousErrors[ii].Reference.StatusCode))
                {
                    nextContinuationPoints.Add(continuationPoints[ii]);
                    nextResults.Add(previousResults[ii]);
                    nextErrors.Add(previousErrors[ii]);
                }
            }
            while (nextContinuationPoints.Count > 0)
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                (
                    _,
                    ByteStringCollection revisedContinuationPoints,
                    IList<ReferenceDescriptionCollection> browseNextResults,
                    IList<ServiceResult> browseNextErrors
                ) = await BrowseNextAsync(requestHeader, nextContinuationPoints, false, ct)
                    .ConfigureAwait(false);

                for (int ii = 0; ii < browseNextResults.Count; ii++)
                {
                    nextResults[ii].AddRange(browseNextResults[ii]);
                    nextErrors[ii].Reference = browseNextErrors[ii];
                }

                previousResults = nextResults;
                previousErrors = nextErrors;

                nextResults = [];
                nextErrors = [];
                nextContinuationPoints = [];

                for (int ii = 0; ii < revisedContinuationPoints.Count; ii++)
                {
                    if (revisedContinuationPoints[ii] != null &&
                        !StatusCode.IsBad(browseNextErrors[ii].StatusCode))
                    {
                        nextContinuationPoints.Add(revisedContinuationPoints[ii]);
                        nextResults.Add(previousResults[ii]);
                        nextErrors.Add(previousErrors[ii]);
                    }
                }
            }
            var finalErrors = new List<ServiceResult>(errorAnchors.Count);
            foreach (ReferenceWrapper<ServiceResult> errorReference in errorAnchors)
            {
                finalErrors.Add(errorReference.Reference);
            }

            return (result, finalErrors);
        }

        /// <inheritdoc/>
        public async Task<IList<object>> CallAsync(
            NodeId objectId,
            NodeId methodId,
            CancellationToken ct = default,
            params object[] args)
        {
            var inputArguments = new VariantCollection();

            if (args != null)
            {
                for (int ii = 0; ii < args.Length; ii++)
                {
                    inputArguments.Add(new Variant(args[ii]));
                }
            }

            var request = new CallMethodRequest
            {
                ObjectId = objectId,
                MethodId = methodId,
                InputArguments = inputArguments
            };

            var requests = new CallMethodRequestCollection { request };

            CallMethodResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            CallResponse response = await base.CallAsync(null, requests, ct).ConfigureAwait(false);

            results = response.Results;
            diagnosticInfos = response.DiagnosticInfos;

            ValidateResponse(results, requests);
            ValidateDiagnosticInfos(diagnosticInfos, requests);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw ServiceResultException.Create(
                    results[0].StatusCode,
                    0,
                    diagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            var outputArguments = new List<object>();

            foreach (Variant arg in results[0].OutputArguments)
            {
                outputArguments.Add(arg.Value);
            }

            return outputArguments;
        }

        /// <inheritdoc/>
        public async Task<ReferenceDescriptionCollection> FetchReferencesAsync(
            NodeId nodeId,
            CancellationToken ct = default)
        {
            (IList<ReferenceDescriptionCollection> descriptions, _) = await ManagedBrowseAsync(
                    null,
                    null,
                    [nodeId],
                    0,
                    BrowseDirection.Both,
                    null,
                    true,
                    0,
                    ct)
                .ConfigureAwait(false);
            return descriptions[0];
        }

        /// <inheritdoc/>
        public Task<(IList<ReferenceDescriptionCollection>, IList<ServiceResult>)> FetchReferencesAsync(
            IList<NodeId> nodeIds,
            CancellationToken ct = default)
        {
            return ManagedBrowseAsync(
                null,
                null,
                nodeIds,
                0,
                BrowseDirection.Both,
                null,
                true,
                0,
                ct);
        }

        /// <summary>
        /// Recreates a session based on a specified template.
        /// </summary>
        /// <param name="sessionTemplate">The Session object to use as template</param>
        /// <param name="ct">Cancellation Token to cancel operation with</param>
        /// <returns>The new session object.</returns>
        public static async Task<Session> RecreateAsync(
            Session sessionTemplate,
            CancellationToken ct = default)
        {
            ServiceMessageContext messageContext = sessionTemplate.m_configuration
                .CreateMessageContext();
            messageContext.Factory = sessionTemplate.Factory;

            // create the channel object used to connect to the server.
            ITransportChannel channel = SessionChannel.Create(
                sessionTemplate.m_configuration,
                sessionTemplate.ConfiguredEndpoint.Description,
                sessionTemplate.ConfiguredEndpoint.Configuration,
                sessionTemplate.m_instanceCertificate,
                sessionTemplate.m_configuration.SecurityConfiguration.SendCertificateChain
                    ? sessionTemplate.m_instanceCertificateChain
                    : null,
                messageContext);

            // create the session object.
            Session session = sessionTemplate.CloneSession(channel, true);

            try
            {
                session.RecreateRenewUserIdentity();
                // open the session.
                await session
                    .OpenAsync(
                        sessionTemplate.SessionName,
                        (uint)sessionTemplate.SessionTimeout,
                        session.Identity,
                        sessionTemplate.PreferredLocales,
                        sessionTemplate.m_checkDomain,
                        ct)
                    .ConfigureAwait(false);

                await session.RecreateSubscriptionsAsync(sessionTemplate.Subscriptions, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                session.Dispose();
                ThrowCouldNotRecreateSessionException(e, sessionTemplate.m_sessionName);
            }

            return session;
        }

        /// <summary>
        /// Recreates a session based on a specified template.
        /// </summary>
        /// <param name="sessionTemplate">The Session object to use as template</param>
        /// <param name="connection">The waiting reverse connection.</param>
        /// <param name="ct">Cancelation token to cancel operation with</param>
        /// <returns>The new session object.</returns>
        public static async Task<Session> RecreateAsync(
            Session sessionTemplate,
            ITransportWaitingConnection connection,
            CancellationToken ct = default)
        {
            ServiceMessageContext messageContext = sessionTemplate.m_configuration
                .CreateMessageContext();
            messageContext.Factory = sessionTemplate.Factory;

            // create the channel object used to connect to the server.
            ITransportChannel channel = SessionChannel.Create(
                sessionTemplate.m_configuration,
                connection,
                sessionTemplate.m_endpoint.Description,
                sessionTemplate.m_endpoint.Configuration,
                sessionTemplate.m_instanceCertificate,
                sessionTemplate.m_configuration.SecurityConfiguration.SendCertificateChain
                    ? sessionTemplate.m_instanceCertificateChain
                    : null,
                messageContext);

            // create the session object.
            Session session = sessionTemplate.CloneSession(channel, true);

            try
            {
                session.RecreateRenewUserIdentity();
                // open the session.
                await session
                    .OpenAsync(
                        sessionTemplate.m_sessionName,
                        (uint)sessionTemplate.m_sessionTimeout,
                        session.Identity,
                        sessionTemplate.m_preferredLocales,
                        sessionTemplate.m_checkDomain,
                        ct)
                    .ConfigureAwait(false);

                await session.RecreateSubscriptionsAsync(sessionTemplate.Subscriptions, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                session.Dispose();
                ThrowCouldNotRecreateSessionException(e, sessionTemplate.m_sessionName);
            }

            return session;
        }

        /// <summary>
        /// Recreates a session based on a specified template using the provided channel.
        /// </summary>
        /// <param name="sessionTemplate">The Session object to use as template</param>
        /// <param name="transportChannel">The waiting reverse connection.</param>
        /// <param name="ct">Cancellation token to cancel the operation with</param>
        /// <returns>The new session object.</returns>
        public static async Task<Session> RecreateAsync(
            Session sessionTemplate,
            ITransportChannel transportChannel,
            CancellationToken ct = default)
        {
            if (transportChannel == null)
            {
                return await RecreateAsync(sessionTemplate, ct).ConfigureAwait(false);
            }

            ServiceMessageContext messageContext = sessionTemplate.m_configuration
                .CreateMessageContext();
            messageContext.Factory = sessionTemplate.Factory;

            // create the session object.
            Session session = sessionTemplate.CloneSession(transportChannel, true);

            try
            {
                session.RecreateRenewUserIdentity();
                // open the session.
                await session
                    .OpenAsync(
                        sessionTemplate.m_sessionName,
                        (uint)sessionTemplate.m_sessionTimeout,
                        session.Identity,
                        sessionTemplate.m_preferredLocales,
                        sessionTemplate.m_checkDomain,
                        false,
                        ct)
                    .ConfigureAwait(false);

                // create the subscriptions.
                foreach (Subscription subscription in session.Subscriptions)
                {
                    await subscription.CreateAsync(ct).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                session.Dispose();
                ThrowCouldNotRecreateSessionException(e, sessionTemplate.m_sessionName);
            }

            return session;
        }

        /// <inheritdoc/>
        public override Task<StatusCode> CloseAsync(CancellationToken ct = default)
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
        {
            return CloseAsync(timeout, true, ct);
        }

        /// <inheritdoc/>
        public virtual async Task<StatusCode> CloseAsync(
            int timeout,
            bool closeChannel,
            CancellationToken ct = default)
        {
            // check if already called.
            if (Disposed)
            {
                return StatusCodes.Good;
            }

            StatusCode result = StatusCodes.Good;

            // stop the keep alive timer.
            StopKeepAliveTimer();

            // check if correctly connected.
            bool connected = Connected;

            // halt all background threads.
            if (connected && m_SessionClosing != null)
            {
                try
                {
                    m_SessionClosing(this, null);
                }
                catch (Exception e)
                {
                    Utils.LogError(e, "Session: Unexpected error raising SessionClosing event.");
                }
            }

            // close the session with the server.
            if (connected && !KeepAliveStopped)
            {
                try
                {
                    // close the session and delete all subscriptions if specified.
                    var requestHeader = new RequestHeader
                    {
                        TimeoutHint = timeout > 0
                            ? (uint)timeout
                            : (uint)(OperationTimeout > 0 ? OperationTimeout : 0)
                    };
                    CloseSessionResponse response = await base.CloseSessionAsync(
                            requestHeader,
                            DeleteSubscriptionsOnClose,
                            ct)
                        .ConfigureAwait(false);

                    if (closeChannel)
                    {
                        await CloseChannelAsync(ct).ConfigureAwait(false);
                    }

                    // raised notification indicating the session is closed.
                    SessionCreated(null, null);
                }
                // don't throw errors on disconnect, but return them
                // so the caller can log the error.
                catch (ServiceResultException sre)
                {
                    result = sre.StatusCode;
                }
                catch (Exception)
                {
                    result = StatusCodes.Bad;
                }
            }

            // clean up.
            if (closeChannel)
            {
                Dispose();
            }

            return result;
        }

        /// <inheritdoc/>
        public Task ReconnectAsync(CancellationToken ct)
        {
            return ReconnectAsync(null, null, ct);
        }

        /// <inheritdoc/>
        public Task ReconnectAsync(ITransportWaitingConnection connection, CancellationToken ct)
        {
            return ReconnectAsync(connection, null, ct);
        }

        /// <inheritdoc/>
        public Task ReconnectAsync(ITransportChannel channel, CancellationToken ct)
        {
            return ReconnectAsync(null, channel, ct);
        }

        /// <inheritdoc/>
        public async Task ReloadInstanceCertificateAsync(CancellationToken ct = default)
        {
            await m_reconnectLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await LoadInstanceCertificateAsync(null).ConfigureAwait(false);
            }
            finally
            {
                m_reconnectLock.Release();
            }
        }

        /// <summary>
        /// Reconnects to the server after a network failure using a waiting connection.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async Task ReconnectAsync(
            ITransportWaitingConnection connection,
            ITransportChannel transportChannel,
            CancellationToken ct)
        {
            bool resetReconnect = false;
            await m_reconnectLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                bool reconnecting = m_reconnecting;
                m_reconnecting = true;
                resetReconnect = true;
                m_reconnectLock.Release();

                // check if already connecting.
                if (reconnecting)
                {
                    Utils.LogWarning("Session is already attempting to reconnect.");

                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "Session is already attempting to reconnect.");
                }

                StopKeepAliveTimer();

                IAsyncResult result = PrepareReconnectBeginActivate(connection, transportChannel);

                const string timeoutMessage = "ACTIVATE SESSION ASYNC timed out. {0}/{1}";
                if (result is ChannelAsyncOperation<int> operation)
                {
                    try
                    {
                        _ = await operation.EndAsync(kReconnectTimeout / 2, true, ct)
                            .ConfigureAwait(false);
                    }
                    catch (ServiceResultException sre)
                    {
                        if (sre.StatusCode == StatusCodes.BadRequestInterrupted)
                        {
                            var error = ServiceResult.Create(
                                StatusCodes.BadRequestTimeout,
                                timeoutMessage,
                                GoodPublishRequestCount,
                                OutstandingRequestCount);
                            Utils.LogWarning("WARNING: {0}", error.ToString());
                            operation.Fault(false, error);
                        }
                    }
                }
                else if (!result.AsyncWaitHandle.WaitOne(kReconnectTimeout / 2))
                {
                    Utils.LogWarning(
                        timeoutMessage,
                        GoodPublishRequestCount,
                        OutstandingRequestCount);
                }

                // reactivate session.

                EndActivateSession(
                    result,
                    out byte[] serverNonce,
                    out StatusCodeCollection certificateResults,
                    out DiagnosticInfoCollection certificateDiagnosticInfos);

                Utils.LogInfo("Session RECONNECT {0} completed successfully.", SessionId);

                lock (SyncRoot)
                {
                    m_previousServerNonce = m_serverNonce;
                    m_serverNonce = serverNonce;
                }

                await m_reconnectLock.WaitAsync(ct).ConfigureAwait(false);
                m_reconnecting = false;
                resetReconnect = false;
                m_reconnectLock.Release();

                StartPublishing(OperationTimeout, true);

                StartKeepAliveTimer();

                IndicateSessionConfigurationChanged();
            }
            finally
            {
                if (resetReconnect)
                {
                    await m_reconnectLock.WaitAsync(ct).ConfigureAwait(false);
                    m_reconnecting = false;
                    m_reconnectLock.Release();
                }
            }
        }

        /// <inheritdoc/>
        public async Task<(bool, ServiceResult)> RepublishAsync(
            uint subscriptionId,
            uint sequenceNumber,
            CancellationToken ct)
        {
            // send republish request.
            var requestHeader = new RequestHeader
            {
                TimeoutHint = (uint)OperationTimeout,
                ReturnDiagnostics = (uint)(int)ReturnDiagnostics,
                RequestHandle = Utils.IncrementIdentifier(ref m_publishCounter)
            };

            try
            {
                Utils.LogInfo(
                    "Requesting RepublishAsync for {0}-{1}",
                    subscriptionId,
                    sequenceNumber);

                // request republish.
                RepublishResponse response = await RepublishAsync(
                    requestHeader,
                    subscriptionId,
                    sequenceNumber,
                    ct)
                    .ConfigureAwait(false);
                ResponseHeader responseHeader = response.ResponseHeader;
                NotificationMessage notificationMessage = response.NotificationMessage;

                Utils.LogInfo(
                    "Received RepublishAsync for {0}-{1}-{2}",
                    subscriptionId,
                    sequenceNumber,
                    responseHeader.ServiceResult);

                // process response.
                ProcessPublishResponse(
                    responseHeader,
                    subscriptionId,
                    null,
                    false,
                    notificationMessage);

                return (true, ServiceResult.Good);
            }
            catch (Exception e)
            {
                return ProcessRepublishResponseError(e, subscriptionId, sequenceNumber);
            }
        }

        /// <summary>
        /// Recreate the subscriptions in a reconnected session.
        /// Uses Transfer service if <see cref="TransferSubscriptionsOnReconnect"/> is set to <c>true</c>.
        /// </summary>
        /// <param name="subscriptionsTemplate">The template for the subscriptions.</param>
        /// <param name="ct">Cancelation token to cancel operation with</param>
        private async Task RecreateSubscriptionsAsync(
            IEnumerable<Subscription> subscriptionsTemplate,
            CancellationToken ct)
        {
            bool transferred = false;
            if (TransferSubscriptionsOnReconnect)
            {
                try
                {
                    transferred = await TransferSubscriptionsAsync(
                        [.. subscriptionsTemplate],
                        false,
                        ct)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    if (sre.StatusCode == StatusCodes.BadServiceUnsupported)
                    {
                        TransferSubscriptionsOnReconnect = false;
                        Utils.LogWarning(
                            "Transfer subscription unsupported, TransferSubscriptionsOnReconnect set to false.");
                    }
                    else
                    {
                        Utils.LogError(sre, "Transfer subscriptions failed.");
                    }
                }
                catch (Exception ex)
                {
                    Utils.LogError(ex, "Unexpected Transfer subscriptions error.");
                }
            }

            if (!transferred)
            {
                // Create the subscriptions which were not transferred.
                foreach (Subscription subscription in Subscriptions)
                {
                    if (!subscription.Created)
                    {
                        await subscription.CreateAsync(ct).ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
#endif
