/* ========================================================================
 * Copyright (c) 2005-2023 The OPC Foundation, Inc. All rights reserved.
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

#if  NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Manages a session with a server.
    /// </summary>
    public class SessionActivitySource : ISession
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance of the <see cref="Session"/> class.
        /// </summary>
        /// <param name="session"></param>
        public SessionActivitySource(ISession session)
        {
            m_session = session;
        }
        #endregion

        /// <summary>
        /// Activity Source Name
        /// </summary>
        public const string OpcUaClientActivitySourceName = "Opc.Ua.Client-ActivitySource";

        private ISession m_session;

        /// <inheritdoc/>
        public ISession Session => m_session;

        /// <inheritdoc/>
        public event KeepAliveEventHandler KeepAlive
        {
            add => m_session.KeepAlive += value;
            remove => m_session.KeepAlive -= value;
        }

        /// <inheritdoc/>
        public event NotificationEventHandler Notification
        {
            add => m_session.Notification += value;
            remove => m_session.Notification -= value;
        }

        /// <inheritdoc/>
        public event PublishErrorEventHandler PublishError
        {
            add => m_session.PublishError += value;
            remove => m_session.PublishError -= value;
        }

        /// <inheritdoc/>
        public event PublishSequenceNumbersToAcknowledgeEventHandler PublishSequenceNumbersToAcknowledge
        {
            add => m_session.PublishSequenceNumbersToAcknowledge += value;
            remove => m_session.PublishSequenceNumbersToAcknowledge -= value;
        }

        /// <inheritdoc/>
        public event EventHandler SubscriptionsChanged
        {
            add => m_session.SubscriptionsChanged += value;
            remove => m_session.SubscriptionsChanged -= value;
        }

        /// <inheritdoc/>
        public event EventHandler SessionClosing
        {
            add => m_session.SessionClosing += value;
            remove => m_session.SessionClosing -= value;
        }

        /// <inheritdoc/>
        public event RenewUserIdentityEventHandler RenewUserIdentity
        {
            add => m_session.RenewUserIdentity += value;
            remove => m_session.RenewUserIdentity -= value;
        }

        /// <summary>
        /// Activity Source
        /// </summary>
        ///
        public static ActivitySource ActivitySrc { get; } = new ActivitySource(OpcUaClientActivitySourceName);

        /// <inheritdoc/>
        public ISessionFactory SessionFactory = new DefaultActivitySessionFactory();

        /// <inheritdoc/>
        public ConfiguredEndpoint ConfiguredEndpoint => m_session.ConfiguredEndpoint;

        /// <inheritdoc/>
        public string SessionName => m_session.SessionName;

        /// <inheritdoc/>
        public double SessionTimeout => m_session.SessionTimeout;

        /// <inheritdoc/>
        public object Handle => m_session.Handle;

        /// <inheritdoc/>
        public IUserIdentity Identity => m_session.Identity;

        /// <inheritdoc/>
        public IEnumerable<IUserIdentity> IdentityHistory => m_session.IdentityHistory;

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris => m_session.NamespaceUris;

        /// <inheritdoc/>
        public StringTable ServerUris => m_session.ServerUris;

        /// <inheritdoc/>
        public ISystemContext SystemContext => m_session.SystemContext;

        /// <inheritdoc/>
        public IEncodeableFactory Factory => m_session.Factory;

        /// <inheritdoc/>
        public ITypeTable TypeTree => m_session.TypeTree;

        /// <inheritdoc/>
        public INodeCache NodeCache => m_session.NodeCache;

        /// <inheritdoc/>
        public FilterContext FilterContext => m_session.FilterContext;

        /// <inheritdoc/>
        public StringCollection PreferredLocales => m_session.PreferredLocales;

        /// <inheritdoc/>
        public IReadOnlyDictionary<NodeId, DataDictionary> DataTypeSystem => m_session.DataTypeSystem;

        /// <inheritdoc/>
        public IEnumerable<Subscription> Subscriptions => m_session.Subscriptions;

        /// <inheritdoc/>
        public int SubscriptionCount => m_session.SubscriptionCount;

        /// <inheritdoc/>
        public bool DeleteSubscriptionsOnClose
        {
            get => m_session.DeleteSubscriptionsOnClose;
            set => m_session.DeleteSubscriptionsOnClose = value;
        }

        /// <inheritdoc/>
        public Subscription DefaultSubscription
        {
            get => m_session.DefaultSubscription;
            set => m_session.DefaultSubscription = value;
        }

        /// <inheritdoc/>
        public int KeepAliveInterval
        {
            get => m_session.KeepAliveInterval;
            set => m_session.KeepAliveInterval = value;
        }

        /// <inheritdoc/>
        public bool KeepAliveStopped => m_session.KeepAliveStopped;

        /// <inheritdoc/>
        public DateTime LastKeepAliveTime => m_session.LastKeepAliveTime;

        /// <inheritdoc/>
        public int OutstandingRequestCount => m_session.OutstandingRequestCount;

        /// <inheritdoc/>
        public int DefunctRequestCount => m_session.DefunctRequestCount;

        /// <inheritdoc/>
        public int GoodPublishRequestCount => m_session.GoodPublishRequestCount;

        /// <inheritdoc/>
        public int MinPublishRequestCount
        {
            get => m_session.MinPublishRequestCount;
            set => m_session.MinPublishRequestCount = value;
        }

        /// <inheritdoc/>
        public OperationLimits OperationLimits => m_session.OperationLimits;

        /// <inheritdoc/>
        public bool TransferSubscriptionsOnReconnect
        {
            get => m_session.TransferSubscriptionsOnReconnect;
            set => m_session.TransferSubscriptionsOnReconnect = value;
        }

        /// <inheritdoc/>
        public NodeId SessionId => m_session.SessionId;

        /// <inheritdoc/>
        public bool Connected => m_session.Connected;

        /// <inheritdoc/>
        public EndpointDescription Endpoint => m_session.Endpoint;

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration => m_session.EndpointConfiguration;

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext => m_session.MessageContext;

        /// <inheritdoc/>
        public ITransportChannel TransportChannel => m_session.TransportChannel;

        /// <inheritdoc/>
        public DiagnosticsMasks ReturnDiagnostics
        {
            get => m_session.ReturnDiagnostics;
            set => m_session.ReturnDiagnostics = value;
        }

        /// <inheritdoc/>
        public int OperationTimeout
        {
            get => m_session.OperationTimeout;
            set => m_session.OperationTimeout = value;
        }

        /// <inheritdoc/>
        public bool Disposed => m_session.Disposed;

        ISessionFactory ISession.SessionFactory => new DefaultActivitySessionFactory();

        /// <inheritdoc/>
        public void Reconnect()
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Reconnect)))
            {
                m_session.Reconnect();
            }
        }

        /// <inheritdoc/>
        public void Reconnect(ITransportWaitingConnection connection)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Reconnect)))
            {
                m_session.Reconnect(connection);
            }
        }

        /// <inheritdoc/>
        public void Reconnect(ITransportChannel channel)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Reconnect)))
            {
                m_session.Reconnect(channel);
            }
        }

        /// <inheritdoc/>
        public void Save(string filePath)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Save)))
            {
                m_session.Save(filePath);
            }
        }

        /// <inheritdoc/>
        public void Save(Stream stream, IEnumerable<Subscription> subscriptions)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Save)))
            {
                m_session.Save(stream, subscriptions);
            }
        }

        /// <inheritdoc/>
        public void Save(string filePath, IEnumerable<Subscription> subscriptions)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Save)))
            {
                m_session.Save(filePath, subscriptions);
            }
        }

        /// <inheritdoc/>
        public IEnumerable<Subscription> Load(Stream stream, bool transferSubscriptions = false)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Load)))
            {
                return m_session.Load(stream, transferSubscriptions);
            }
        }

        /// <inheritdoc/>
        public IEnumerable<Subscription> Load(string filePath, bool transferSubscriptions = false)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Load)))
            {
                return m_session.Load(filePath, transferSubscriptions);
            }
        }

        /// <inheritdoc/>
        public void FetchNamespaceTables()
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(FetchNamespaceTables)))
            {
                m_session.FetchNamespaceTables();
            }
        }

        /// <inheritdoc/>
        public void FetchTypeTree(ExpandedNodeId typeId)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(FetchTypeTree)))
            {
                m_session.FetchTypeTree(typeId);
            }
        }

        /// <inheritdoc/>
        public void FetchTypeTree(ExpandedNodeIdCollection typeIds)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(FetchTypeTree)))
            {
                m_session.FetchTypeTree(typeIds);
            }
        }

        /// <inheritdoc/>
        public ReferenceDescriptionCollection ReadAvailableEncodings(NodeId variableId)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadAvailableEncodings)))
            {
                return m_session.ReadAvailableEncodings(variableId);
            }
        }

        /// <inheritdoc/>
        public ReferenceDescription FindDataDescription(NodeId encodingId)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(FindDataDescription)))
            {
                return m_session.FindDataDescription(encodingId);
            }
        }

        /// <inheritdoc/>
        public Task<DataDictionary> FindDataDictionary(NodeId descriptionId)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(FindDataDictionary)))
            {
                return m_session.FindDataDictionary(descriptionId);
            }
        }

        /// <inheritdoc/>
        public async Task<DataDictionary> LoadDataDictionary(ReferenceDescription dictionaryNode, bool forceReload = false)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(LoadDataDictionary)))
            {
                return await m_session.LoadDataDictionary(dictionaryNode, forceReload).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<Dictionary<NodeId, DataDictionary>> LoadDataTypeSystem(NodeId dataTypeSystem = null)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(LoadDataTypeSystem)))
            {
                return await m_session.LoadDataTypeSystem(dataTypeSystem).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public Node ReadNode(NodeId nodeId)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadNode)))
            {
                return m_session.ReadNode(nodeId);
            }
        }

        /// <inheritdoc/>
        public Node ReadNode(NodeId nodeId, NodeClass nodeClass, bool optionalAttributes = true)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadNode)))
            {
                return m_session.ReadNode(nodeId, nodeClass, optionalAttributes);
            }
        }

        /// <inheritdoc/>
        public void ReadNodes(IList<NodeId> nodeIds, out IList<Node> nodeCollection, out IList<ServiceResult> errors, bool optionalAttributes = false)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadNodes)))
            {
                m_session.ReadNodes(nodeIds, out nodeCollection, out errors, optionalAttributes);
            }
        }

        /// <inheritdoc/>
        public void ReadNodes(IList<NodeId> nodeIds, NodeClass nodeClass, out IList<Node> nodeCollection, out IList<ServiceResult> errors, bool optionalAttributes = false)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadNodes)))
            {
                m_session.ReadNodes(nodeIds, nodeClass, out nodeCollection, out errors, optionalAttributes);
            }
        }

        /// <inheritdoc/>
        public DataValue ReadValue(NodeId nodeId)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadValue)))
            {
                return m_session.ReadValue(nodeId);
            }
        }

        /// <inheritdoc/>
        public object ReadValue(NodeId nodeId, Type expectedType)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadValue)))
            {
                return m_session.ReadValue(nodeId, expectedType);
            }
        }

        /// <inheritdoc/>
        public void ReadValues(IList<NodeId> nodeIds, out DataValueCollection values, out IList<ServiceResult> errors)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadValues)))
            {
                m_session.ReadValues(nodeIds, out values, out errors);
            }
        }

        /// <inheritdoc/>
        public ReferenceDescriptionCollection FetchReferences(NodeId nodeId)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(FetchReferences)))
            {
                return m_session.FetchReferences(nodeId);
            }
        }

        /// <inheritdoc/>
        public void FetchReferences(IList<NodeId> nodeIds, out IList<ReferenceDescriptionCollection> referenceDescriptions, out IList<ServiceResult> errors)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(FetchReferences)))
            {
                m_session.FetchReferences(nodeIds, out referenceDescriptions, out errors);
            }
        }

        /// <inheritdoc/>
        public void Open(string sessionName, IUserIdentity identity)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Open)))
            {
                m_session.Open(sessionName, identity);
            }
        }

        /// <inheritdoc/>
        public void Open(string sessionName, uint sessionTimeout, IUserIdentity identity, IList<string> preferredLocales)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Open)))
            {
                m_session.Open(sessionName, sessionTimeout, identity, preferredLocales);
            }
        }

        /// <inheritdoc/>
        public void Open(string sessionName, uint sessionTimeout, IUserIdentity identity, IList<string> preferredLocales, bool checkDomain)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Open)))
            {
                m_session.Open(sessionName, sessionTimeout, identity, preferredLocales, checkDomain);
            }
        }

        /// <inheritdoc/>
        public void ChangePreferredLocales(StringCollection preferredLocales)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ChangePreferredLocales)))
            {
                m_session.ChangePreferredLocales(preferredLocales);
            }
        }

        /// <inheritdoc/>
        public void UpdateSession(IUserIdentity identity, StringCollection preferredLocales)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(UpdateSession)))
            {
                m_session.UpdateSession(identity, preferredLocales);
            }
        }

        /// <inheritdoc/>
        public void FindComponentIds(NodeId instanceId, IList<string> componentPaths, out NodeIdCollection componentIds, out List<ServiceResult> errors)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(FindComponentIds)))
            {
                m_session.FindComponentIds(instanceId, componentPaths, out componentIds, out errors);
            }
        }

        /// <inheritdoc/>
        public void ReadValues(IList<NodeId> variableIds, IList<Type> expectedTypes, out List<object> values, out List<ServiceResult> errors)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadValues)))
            {
                m_session.ReadValues(variableIds, expectedTypes, out values, out errors);
            }
        }

        /// <inheritdoc/>
        public void ReadDisplayName(IList<NodeId> nodeIds, out IList<string> displayNames, out IList<ServiceResult> errors)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadDisplayName)))
            {
                m_session.ReadDisplayName(nodeIds, out displayNames, out errors);
            }
        }
        /// <inheritdoc/>
        public async Task<(IList<Node>, IList<ServiceResult>)> ReadNodesAsync(IList<NodeId> nodeIds, NodeClass nodeClass, bool optionalAttributes = false, CancellationToken ct = default)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadNodesAsync)))
            {
                return await m_session.ReadNodesAsync(nodeIds, nodeClass, optionalAttributes, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<DataValue> ReadValueAsync(NodeId nodeId, CancellationToken ct = default)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadValueAsync)))
            {
                return await m_session.ReadValueAsync(nodeId, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<Node> ReadNodeAsync(NodeId nodeId, CancellationToken ct = default)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadNodeAsync)))
            {
                return await m_session.ReadNodeAsync(nodeId, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<Node> ReadNodeAsync(NodeId nodeId, NodeClass nodeClass, bool optionalAttributes = true, CancellationToken ct = default)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadNodeAsync)))
            {
                return await m_session.ReadNodeAsync(nodeId, nodeClass, optionalAttributes, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<(IList<Node>, IList<ServiceResult>)> ReadNodesAsync(IList<NodeId> nodeIds, bool optionalAttributes = false, CancellationToken ct = default)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadNodesAsync)))
            {
                return await m_session.ReadNodesAsync(nodeIds, optionalAttributes, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<(DataValueCollection, IList<ServiceResult>)> ReadValuesAsync(IList<NodeId> nodeIds, CancellationToken ct = default)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadValuesAsync)))
            {
                return await m_session.ReadValuesAsync(nodeIds, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public StatusCode Close(int timeout)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Close)))
            {
                return m_session.Close(timeout);
            }
        }

        /// <inheritdoc/>
        public StatusCode Close(bool closeChannel)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Close)))
            {
                return m_session.Close(closeChannel);
            }
        }

        /// <inheritdoc/>
        public StatusCode Close(int timeout, bool closeChannel)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Close)))
            {
                return m_session.Close(timeout, closeChannel);
            }
        }

        /// <inheritdoc/>
        public async Task<StatusCode> CloseAsync(CancellationToken ct = default)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(CloseAsync)))
            {
                return await m_session.CloseAsync(ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<StatusCode> CloseAsync(bool closeChannel, CancellationToken ct = default)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(CloseAsync)))
            {
                return await m_session.CloseAsync(closeChannel, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<StatusCode> CloseAsync(int timeout, CancellationToken ct = default)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(CloseAsync)))
            {
                return await m_session.CloseAsync(timeout, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<StatusCode> CloseAsync(int timeout, bool closeChannel, CancellationToken ct = default)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(CloseAsync)))
            {
                return await m_session.CloseAsync(timeout, closeChannel, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public bool AddSubscription(Subscription subscription)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(AddSubscription)))
            {
                return m_session.AddSubscription(subscription);
            }
        }

        /// <inheritdoc/>
        public bool RemoveSubscription(Subscription subscription)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(RemoveSubscription)))
            {
                return m_session.RemoveSubscription(subscription);
            }
        }

        /// <inheritdoc/>
        public bool RemoveSubscriptions(IEnumerable<Subscription> subscriptions)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(RemoveSubscriptions)))
            {
                return m_session.RemoveSubscriptions(subscriptions);
            }
        }

        /// <inheritdoc/>
        public bool TransferSubscriptions(SubscriptionCollection subscriptions, bool sendInitialValues)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(TransferSubscriptions)))
            {
                return m_session.TransferSubscriptions(subscriptions, sendInitialValues);
            }
        }

        /// <inheritdoc/>
        public bool RemoveTransferredSubscription(Subscription subscription)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(RemoveTransferredSubscription)))
            {
                return m_session.RemoveTransferredSubscription(subscription);
            }
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionAsync(Subscription subscription)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(RemoveSubscriptionAsync)))
            {
                return await m_session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionsAsync(IEnumerable<Subscription> subscriptions)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(RemoveSubscriptionsAsync)))
            {
                return await m_session.RemoveSubscriptionsAsync(subscriptions).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader Browse(RequestHeader requestHeader, ViewDescription view, NodeId nodeToBrowse, uint maxResultsToReturn, BrowseDirection browseDirection, NodeId referenceTypeId, bool includeSubtypes, uint nodeClassMask, out byte[] continuationPoint, out ReferenceDescriptionCollection references)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Browse)))
            {
                return m_session.Browse(requestHeader, view, nodeToBrowse, maxResultsToReturn, browseDirection, referenceTypeId, includeSubtypes, nodeClassMask, out continuationPoint, out references);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginBrowse(RequestHeader requestHeader, ViewDescription view, NodeId nodeToBrowse, uint maxResultsToReturn, BrowseDirection browseDirection, NodeId referenceTypeId, bool includeSubtypes, uint nodeClassMask, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginBrowse(requestHeader, view, nodeToBrowse, maxResultsToReturn, browseDirection, referenceTypeId, includeSubtypes, nodeClassMask, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndBrowse(IAsyncResult result, out byte[] continuationPoint, out ReferenceDescriptionCollection references)
        {
            return m_session.EndBrowse(result, out continuationPoint, out references);
        }

        /// <inheritdoc/>
        public ResponseHeader BrowseNext(RequestHeader requestHeader, bool releaseContinuationPoint, byte[] continuationPoint, out byte[] revisedContinuationPoint, out ReferenceDescriptionCollection references)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(BrowseNext)))
            {
                return m_session.BrowseNext(requestHeader, releaseContinuationPoint, continuationPoint, out revisedContinuationPoint, out references);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginBrowseNext(RequestHeader requestHeader, bool releaseContinuationPoint, byte[] continuationPoint, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginBrowseNext(requestHeader, releaseContinuationPoint, continuationPoint, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndBrowseNext(IAsyncResult result, out byte[] revisedContinuationPoint, out ReferenceDescriptionCollection references)
        {
            return m_session.EndBrowseNext(result, out revisedContinuationPoint, out references);
        }

        /// <inheritdoc/>
        public IList<object> Call(NodeId objectId, NodeId methodId, params object[] args)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Call)))
            {
                return m_session.Call(objectId, methodId, args);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginPublish(int timeout)
        {
            return m_session.BeginPublish(timeout);
        }

        /// <inheritdoc/>
        public bool Republish(uint subscriptionId, uint sequenceNumber)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Republish)))
            {
                return m_session.Republish(subscriptionId, sequenceNumber);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader CreateSession(RequestHeader requestHeader, ApplicationDescription clientDescription, string serverUri, string endpointUrl, string sessionName, byte[] clientNonce, byte[] clientCertificate, double requestedSessionTimeout, uint maxResponseMessageSize, out NodeId sessionId, out NodeId authenticationToken, out double revisedSessionTimeout, out byte[] serverNonce, out byte[] serverCertificate, out EndpointDescriptionCollection serverEndpoints, out SignedSoftwareCertificateCollection serverSoftwareCertificates, out SignatureData serverSignature, out uint maxRequestMessageSize)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(CreateSession)))
            {
                return m_session.CreateSession(requestHeader, clientDescription, serverUri, endpointUrl, sessionName, clientNonce, clientCertificate, requestedSessionTimeout, maxResponseMessageSize, out sessionId, out authenticationToken, out revisedSessionTimeout, out serverNonce, out serverCertificate, out serverEndpoints, out serverSoftwareCertificates, out serverSignature, out maxRequestMessageSize);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginCreateSession(RequestHeader requestHeader, ApplicationDescription clientDescription, string serverUri, string endpointUrl, string sessionName, byte[] clientNonce, byte[] clientCertificate, double requestedSessionTimeout, uint maxResponseMessageSize, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginCreateSession(requestHeader, clientDescription, serverUri, endpointUrl, sessionName, clientNonce, clientCertificate, requestedSessionTimeout, maxResponseMessageSize, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndCreateSession(IAsyncResult result, out NodeId sessionId, out NodeId authenticationToken, out double revisedSessionTimeout, out byte[] serverNonce, out byte[] serverCertificate, out EndpointDescriptionCollection serverEndpoints, out SignedSoftwareCertificateCollection serverSoftwareCertificates, out SignatureData serverSignature, out uint maxRequestMessageSize)
        {
            return m_session.EndCreateSession(result, out sessionId, out authenticationToken, out revisedSessionTimeout, out serverNonce, out serverCertificate, out serverEndpoints, out serverSoftwareCertificates, out serverSignature, out maxRequestMessageSize);
        }

        /// <inheritdoc/>
        public async Task<CreateSessionResponse> CreateSessionAsync(RequestHeader requestHeader, ApplicationDescription clientDescription, string serverUri, string endpointUrl, string sessionName, byte[] clientNonce, byte[] clientCertificate, double requestedSessionTimeout, uint maxResponseMessageSize, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(CreateSessionAsync)))
            {
                return await m_session.CreateSessionAsync(requestHeader, clientDescription, serverUri, endpointUrl, sessionName, clientNonce, clientCertificate, requestedSessionTimeout, maxResponseMessageSize, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader ActivateSession(RequestHeader requestHeader, SignatureData clientSignature, SignedSoftwareCertificateCollection clientSoftwareCertificates, StringCollection localeIds, ExtensionObject userIdentityToken, SignatureData userTokenSignature, out byte[] serverNonce, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ActivateSession)))
            {
                return m_session.ActivateSession(requestHeader, clientSignature, clientSoftwareCertificates, localeIds, userIdentityToken, userTokenSignature, out serverNonce, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginActivateSession(RequestHeader requestHeader, SignatureData clientSignature, SignedSoftwareCertificateCollection clientSoftwareCertificates, StringCollection localeIds, ExtensionObject userIdentityToken, SignatureData userTokenSignature, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginActivateSession(requestHeader, clientSignature, clientSoftwareCertificates, localeIds, userIdentityToken, userTokenSignature, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndActivateSession(IAsyncResult result, out byte[] serverNonce, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndActivateSession(result, out serverNonce, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<ActivateSessionResponse> ActivateSessionAsync(RequestHeader requestHeader, SignatureData clientSignature, SignedSoftwareCertificateCollection clientSoftwareCertificates, StringCollection localeIds, ExtensionObject userIdentityToken, SignatureData userTokenSignature, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ActivateSessionAsync)))
            {
                return await m_session.ActivateSessionAsync(requestHeader, clientSignature, clientSoftwareCertificates, localeIds, userIdentityToken, userTokenSignature, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader CloseSession(RequestHeader requestHeader, bool deleteSubscriptions)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(CloseSession)))
            {
                return m_session.CloseSession(requestHeader, deleteSubscriptions);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginCloseSession(RequestHeader requestHeader, bool deleteSubscriptions, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginCloseSession(requestHeader, deleteSubscriptions, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndCloseSession(IAsyncResult result)
        {
            return m_session.EndCloseSession(result);
        }

        /// <inheritdoc/>
        public async Task<CloseSessionResponse> CloseSessionAsync(RequestHeader requestHeader, bool deleteSubscriptions, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(CloseSessionAsync)))
            {
                return await m_session.CloseSessionAsync(requestHeader, deleteSubscriptions, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader Cancel(RequestHeader requestHeader, uint requestHandle, out uint cancelCount)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Cancel)))
            {
                return m_session.Cancel(requestHeader, requestHandle, out cancelCount);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginCancel(RequestHeader requestHeader, uint requestHandle, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginCancel(requestHeader, requestHandle, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndCancel(IAsyncResult result, out uint cancelCount)
        {
            return m_session.EndCancel(result, out cancelCount);
        }

        /// <inheritdoc/>
        public Task<CancelResponse> CancelAsync(RequestHeader requestHeader, uint requestHandle, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(CancelAsync)))
            {
                return m_session.CancelAsync(requestHeader, requestHandle, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader AddNodes(RequestHeader requestHeader, AddNodesItemCollection nodesToAdd, out AddNodesResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(AddNodes)))
            {
                return m_session.AddNodes(requestHeader, nodesToAdd, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginAddNodes(RequestHeader requestHeader, AddNodesItemCollection nodesToAdd, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginAddNodes(requestHeader, nodesToAdd, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndAddNodes(IAsyncResult result, out AddNodesResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndAddNodes(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<AddNodesResponse> AddNodesAsync(RequestHeader requestHeader, AddNodesItemCollection nodesToAdd, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(AddNodesAsync)))
            {
                return m_session.AddNodesAsync(requestHeader, nodesToAdd, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader AddReferences(RequestHeader requestHeader, AddReferencesItemCollection referencesToAdd, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(AddReferences)))
            {
                return m_session.AddReferences(requestHeader, referencesToAdd, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginAddReferences(RequestHeader requestHeader, AddReferencesItemCollection referencesToAdd, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginAddReferences(requestHeader, referencesToAdd, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndAddReferences(IAsyncResult result, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndAddReferences(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<AddReferencesResponse> AddReferencesAsync(RequestHeader requestHeader, AddReferencesItemCollection referencesToAdd, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(AddReferencesAsync)))
            {
                return m_session.AddReferencesAsync(requestHeader, referencesToAdd, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader DeleteNodes(RequestHeader requestHeader, DeleteNodesItemCollection nodesToDelete, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(DeleteNodes)))
            {
                return m_session.DeleteNodes(requestHeader, nodesToDelete, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginDeleteNodes(RequestHeader requestHeader, DeleteNodesItemCollection nodesToDelete, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginDeleteNodes(requestHeader, nodesToDelete, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndDeleteNodes(IAsyncResult result, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndDeleteNodes(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<DeleteNodesResponse> DeleteNodesAsync(RequestHeader requestHeader, DeleteNodesItemCollection nodesToDelete, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(DeleteNodesAsync)))
            {
                return m_session.DeleteNodesAsync(requestHeader, nodesToDelete, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader DeleteReferences(RequestHeader requestHeader, DeleteReferencesItemCollection referencesToDelete, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(DeleteReferences)))
            {
                return m_session.DeleteReferences(requestHeader, referencesToDelete, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginDeleteReferences(RequestHeader requestHeader, DeleteReferencesItemCollection referencesToDelete, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginDeleteReferences(requestHeader, referencesToDelete, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndDeleteReferences(IAsyncResult result, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndDeleteReferences(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<DeleteReferencesResponse> DeleteReferencesAsync(RequestHeader requestHeader, DeleteReferencesItemCollection referencesToDelete, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(DeleteReferencesAsync)))
            {
                return m_session.DeleteReferencesAsync(requestHeader, referencesToDelete, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader Browse(RequestHeader requestHeader, ViewDescription view, uint requestedMaxReferencesPerNode, BrowseDescriptionCollection nodesToBrowse, out BrowseResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Browse)))
            {
                return m_session.Browse(requestHeader, view, requestedMaxReferencesPerNode, nodesToBrowse, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginBrowse(RequestHeader requestHeader, ViewDescription view, uint requestedMaxReferencesPerNode, BrowseDescriptionCollection nodesToBrowse, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginBrowse(requestHeader, view, requestedMaxReferencesPerNode, nodesToBrowse, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndBrowse(IAsyncResult result, out BrowseResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndBrowse(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<BrowseResponse> BrowseAsync(RequestHeader requestHeader, ViewDescription view, uint requestedMaxReferencesPerNode, BrowseDescriptionCollection nodesToBrowse, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(BrowseAsync)))
            {
                return m_session.BrowseAsync(requestHeader, view, requestedMaxReferencesPerNode, nodesToBrowse, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader BrowseNext(RequestHeader requestHeader, bool releaseContinuationPoints, ByteStringCollection continuationPoints, out BrowseResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(BrowseNext)))
            {
                return m_session.BrowseNext(requestHeader, releaseContinuationPoints, continuationPoints, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginBrowseNext(RequestHeader requestHeader, bool releaseContinuationPoints, ByteStringCollection continuationPoints, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginBrowseNext(requestHeader, releaseContinuationPoints, continuationPoints, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndBrowseNext(IAsyncResult result, out BrowseResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndBrowseNext(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<BrowseNextResponse> BrowseNextAsync(RequestHeader requestHeader, bool releaseContinuationPoints, ByteStringCollection continuationPoints, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(BrowseNextAsync)))
            {
                return m_session.BrowseNextAsync(requestHeader, releaseContinuationPoints, continuationPoints, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader TranslateBrowsePathsToNodeIds(RequestHeader requestHeader, BrowsePathCollection browsePaths, out BrowsePathResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(TranslateBrowsePathsToNodeIds)))
            {
                return m_session.TranslateBrowsePathsToNodeIds(requestHeader, browsePaths, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginTranslateBrowsePathsToNodeIds(RequestHeader requestHeader, BrowsePathCollection browsePaths, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginTranslateBrowsePathsToNodeIds(requestHeader, browsePaths, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndTranslateBrowsePathsToNodeIds(IAsyncResult result, out BrowsePathResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndTranslateBrowsePathsToNodeIds(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(RequestHeader requestHeader, BrowsePathCollection browsePaths, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(TranslateBrowsePathsToNodeIdsAsync)))
            {
                return m_session.TranslateBrowsePathsToNodeIdsAsync(requestHeader, browsePaths, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader RegisterNodes(RequestHeader requestHeader, NodeIdCollection nodesToRegister, out NodeIdCollection registeredNodeIds)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(RegisterNodes)))
            {
                return m_session.RegisterNodes(requestHeader, nodesToRegister, out registeredNodeIds);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginRegisterNodes(RequestHeader requestHeader, NodeIdCollection nodesToRegister, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginRegisterNodes(requestHeader, nodesToRegister, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndRegisterNodes(IAsyncResult result, out NodeIdCollection registeredNodeIds)
        {
            return m_session.EndRegisterNodes(result, out registeredNodeIds);
        }

        /// <inheritdoc/>
        public Task<RegisterNodesResponse> RegisterNodesAsync(RequestHeader requestHeader, NodeIdCollection nodesToRegister, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(RegisterNodesAsync)))
            {
                return m_session.RegisterNodesAsync(requestHeader, nodesToRegister, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader UnregisterNodes(RequestHeader requestHeader, NodeIdCollection nodesToUnregister)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(UnregisterNodes)))
            {
                return m_session.UnregisterNodes(requestHeader, nodesToUnregister);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginUnregisterNodes(RequestHeader requestHeader, NodeIdCollection nodesToUnregister, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginUnregisterNodes(requestHeader, nodesToUnregister, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndUnregisterNodes(IAsyncResult result)
        {
            return m_session.EndUnregisterNodes(result);
        }

        /// <inheritdoc/>
        public Task<UnregisterNodesResponse> UnregisterNodesAsync(RequestHeader requestHeader, NodeIdCollection nodesToUnregister, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(UnregisterNodesAsync)))
            {
                return m_session.UnregisterNodesAsync(requestHeader, nodesToUnregister, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader QueryFirst(RequestHeader requestHeader, ViewDescription view, NodeTypeDescriptionCollection nodeTypes, ContentFilter filter, uint maxDataSetsToReturn, uint maxReferencesToReturn, out QueryDataSetCollection queryDataSets, out byte[] continuationPoint, out ParsingResultCollection parsingResults, out DiagnosticInfoCollection diagnosticInfos, out ContentFilterResult filterResult)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(QueryFirst)))
            {
                return m_session.QueryFirst(requestHeader, view, nodeTypes, filter, maxDataSetsToReturn, maxReferencesToReturn, out queryDataSets, out continuationPoint, out parsingResults, out diagnosticInfos, out filterResult);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginQueryFirst(RequestHeader requestHeader, ViewDescription view, NodeTypeDescriptionCollection nodeTypes, ContentFilter filter, uint maxDataSetsToReturn, uint maxReferencesToReturn, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginQueryFirst(requestHeader, view, nodeTypes, filter, maxDataSetsToReturn, maxReferencesToReturn, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndQueryFirst(IAsyncResult result, out QueryDataSetCollection queryDataSets, out byte[] continuationPoint, out ParsingResultCollection parsingResults, out DiagnosticInfoCollection diagnosticInfos, out ContentFilterResult filterResult)
        {
            return m_session.EndQueryFirst(result, out queryDataSets, out continuationPoint, out parsingResults, out diagnosticInfos, out filterResult);
        }

        /// <inheritdoc/>
        public Task<QueryFirstResponse> QueryFirstAsync(RequestHeader requestHeader, ViewDescription view, NodeTypeDescriptionCollection nodeTypes, ContentFilter filter, uint maxDataSetsToReturn, uint maxReferencesToReturn, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(QueryFirstAsync)))
            {
                return m_session.QueryFirstAsync(requestHeader, view, nodeTypes, filter, maxDataSetsToReturn, maxReferencesToReturn, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader QueryNext(RequestHeader requestHeader, bool releaseContinuationPoint, byte[] continuationPoint, out QueryDataSetCollection queryDataSets, out byte[] revisedContinuationPoint)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(QueryNext)))
            {
                return m_session.QueryNext(requestHeader, releaseContinuationPoint, continuationPoint, out queryDataSets, out revisedContinuationPoint);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginQueryNext(RequestHeader requestHeader, bool releaseContinuationPoint, byte[] continuationPoint, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginQueryNext(requestHeader, releaseContinuationPoint, continuationPoint, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndQueryNext(IAsyncResult result, out QueryDataSetCollection queryDataSets, out byte[] revisedContinuationPoint)
        {
            return m_session.EndQueryNext(result, out queryDataSets, out revisedContinuationPoint);
        }

        /// <inheritdoc/>
        public Task<QueryNextResponse> QueryNextAsync(RequestHeader requestHeader, bool releaseContinuationPoint, byte[] continuationPoint, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(QueryNextAsync)))
            {
                return m_session.QueryNextAsync(requestHeader, releaseContinuationPoint, continuationPoint, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader Read(RequestHeader requestHeader, double maxAge, TimestampsToReturn timestampsToReturn, ReadValueIdCollection nodesToRead, out DataValueCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Read)))
            {
                return m_session.Read(requestHeader, maxAge, timestampsToReturn, nodesToRead, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginRead(RequestHeader requestHeader, double maxAge, TimestampsToReturn timestampsToReturn, ReadValueIdCollection nodesToRead, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginRead(requestHeader, maxAge, timestampsToReturn, nodesToRead, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndRead(IAsyncResult result, out DataValueCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndRead(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<ReadResponse> ReadAsync(RequestHeader requestHeader, double maxAge, TimestampsToReturn timestampsToReturn, ReadValueIdCollection nodesToRead, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ReadAsync)))
            {
                return m_session.ReadAsync(requestHeader, maxAge, timestampsToReturn, nodesToRead, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader HistoryRead(RequestHeader requestHeader, ExtensionObject historyReadDetails, TimestampsToReturn timestampsToReturn, bool releaseContinuationPoints, HistoryReadValueIdCollection nodesToRead, out HistoryReadResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(HistoryRead)))
            {
                return m_session.HistoryRead(requestHeader, historyReadDetails, timestampsToReturn, releaseContinuationPoints, nodesToRead, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginHistoryRead(RequestHeader requestHeader, ExtensionObject historyReadDetails, TimestampsToReturn timestampsToReturn, bool releaseContinuationPoints, HistoryReadValueIdCollection nodesToRead, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginHistoryRead(requestHeader, historyReadDetails, timestampsToReturn, releaseContinuationPoints, nodesToRead, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndHistoryRead(IAsyncResult result, out HistoryReadResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndHistoryRead(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<HistoryReadResponse> HistoryReadAsync(RequestHeader requestHeader, ExtensionObject historyReadDetails, TimestampsToReturn timestampsToReturn, bool releaseContinuationPoints, HistoryReadValueIdCollection nodesToRead, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(HistoryReadAsync)))
            {
                return m_session.HistoryReadAsync(requestHeader, historyReadDetails, timestampsToReturn, releaseContinuationPoints, nodesToRead, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader Write(RequestHeader requestHeader, WriteValueCollection nodesToWrite, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Write)))
            {
                return m_session.Write(requestHeader, nodesToWrite, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginWrite(RequestHeader requestHeader, WriteValueCollection nodesToWrite, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginWrite(requestHeader, nodesToWrite, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndWrite(IAsyncResult result, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndWrite(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<WriteResponse> WriteAsync(RequestHeader requestHeader, WriteValueCollection nodesToWrite, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(WriteAsync)))
            {
                return m_session.WriteAsync(requestHeader, nodesToWrite, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader HistoryUpdate(RequestHeader requestHeader, ExtensionObjectCollection historyUpdateDetails, out HistoryUpdateResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(HistoryUpdate)))
            {
                return m_session.HistoryUpdate(requestHeader, historyUpdateDetails, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginHistoryUpdate(RequestHeader requestHeader, ExtensionObjectCollection historyUpdateDetails, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginHistoryUpdate(requestHeader, historyUpdateDetails, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndHistoryUpdate(IAsyncResult result, out HistoryUpdateResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndHistoryUpdate(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<HistoryUpdateResponse> HistoryUpdateAsync(RequestHeader requestHeader, ExtensionObjectCollection historyUpdateDetails, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(HistoryUpdateAsync)))
            {
                return m_session.HistoryUpdateAsync(requestHeader, historyUpdateDetails, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader Call(RequestHeader requestHeader, CallMethodRequestCollection methodsToCall, out CallMethodResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Call)))
            {
                return m_session.Call(requestHeader, methodsToCall, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginCall(RequestHeader requestHeader, CallMethodRequestCollection methodsToCall, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginCall(requestHeader, methodsToCall, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndCall(IAsyncResult result, out CallMethodResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndCall(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<CallResponse> CallAsync(RequestHeader requestHeader, CallMethodRequestCollection methodsToCall, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(CallAsync)))
            {
                return m_session.CallAsync(requestHeader, methodsToCall, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader CreateMonitoredItems(RequestHeader requestHeader, uint subscriptionId, TimestampsToReturn timestampsToReturn, MonitoredItemCreateRequestCollection itemsToCreate, out MonitoredItemCreateResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(CreateMonitoredItems)))
            {
                return m_session.CreateMonitoredItems(requestHeader, subscriptionId, timestampsToReturn, itemsToCreate, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginCreateMonitoredItems(RequestHeader requestHeader, uint subscriptionId, TimestampsToReturn timestampsToReturn, MonitoredItemCreateRequestCollection itemsToCreate, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginCreateMonitoredItems(requestHeader, subscriptionId, timestampsToReturn, itemsToCreate, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndCreateMonitoredItems(IAsyncResult result, out MonitoredItemCreateResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndCreateMonitoredItems(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(RequestHeader requestHeader, uint subscriptionId, TimestampsToReturn timestampsToReturn, MonitoredItemCreateRequestCollection itemsToCreate, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(CreateMonitoredItemsAsync)))
            {
                return m_session.CreateMonitoredItemsAsync(requestHeader, subscriptionId, timestampsToReturn, itemsToCreate, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader ModifyMonitoredItems(RequestHeader requestHeader, uint subscriptionId, TimestampsToReturn timestampsToReturn, MonitoredItemModifyRequestCollection itemsToModify, out MonitoredItemModifyResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ModifyMonitoredItems)))
            {
                return m_session.ModifyMonitoredItems(requestHeader, subscriptionId, timestampsToReturn, itemsToModify, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginModifyMonitoredItems(RequestHeader requestHeader, uint subscriptionId, TimestampsToReturn timestampsToReturn, MonitoredItemModifyRequestCollection itemsToModify, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginModifyMonitoredItems(requestHeader, subscriptionId, timestampsToReturn, itemsToModify, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndModifyMonitoredItems(IAsyncResult result, out MonitoredItemModifyResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndModifyMonitoredItems(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(RequestHeader requestHeader, uint subscriptionId, TimestampsToReturn timestampsToReturn, MonitoredItemModifyRequestCollection itemsToModify, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ModifyMonitoredItemsAsync)))
            {
                return m_session.ModifyMonitoredItemsAsync(requestHeader, subscriptionId, timestampsToReturn, itemsToModify, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader SetMonitoringMode(RequestHeader requestHeader, uint subscriptionId, MonitoringMode monitoringMode, UInt32Collection monitoredItemIds, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(SetMonitoringMode)))
            {
                return m_session.SetMonitoringMode(requestHeader, subscriptionId, monitoringMode, monitoredItemIds, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginSetMonitoringMode(RequestHeader requestHeader, uint subscriptionId, MonitoringMode monitoringMode, UInt32Collection monitoredItemIds, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginSetMonitoringMode(requestHeader, subscriptionId, monitoringMode, monitoredItemIds, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndSetMonitoringMode(IAsyncResult result, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndSetMonitoringMode(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<SetMonitoringModeResponse> SetMonitoringModeAsync(RequestHeader requestHeader, uint subscriptionId, MonitoringMode monitoringMode, UInt32Collection monitoredItemIds, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(SetMonitoringModeAsync)))
            {
                return m_session.SetMonitoringModeAsync(requestHeader, subscriptionId, monitoringMode, monitoredItemIds, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader SetTriggering(RequestHeader requestHeader, uint subscriptionId, uint triggeringItemId, UInt32Collection linksToAdd, UInt32Collection linksToRemove, out StatusCodeCollection addResults, out DiagnosticInfoCollection addDiagnosticInfos, out StatusCodeCollection removeResults, out DiagnosticInfoCollection removeDiagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(SetTriggering)))
            {
                return m_session.SetTriggering(requestHeader, subscriptionId, triggeringItemId, linksToAdd, linksToRemove, out addResults, out addDiagnosticInfos, out removeResults, out removeDiagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginSetTriggering(RequestHeader requestHeader, uint subscriptionId, uint triggeringItemId, UInt32Collection linksToAdd, UInt32Collection linksToRemove, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginSetTriggering(requestHeader, subscriptionId, triggeringItemId, linksToAdd, linksToRemove, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndSetTriggering(IAsyncResult result, out StatusCodeCollection addResults, out DiagnosticInfoCollection addDiagnosticInfos, out StatusCodeCollection removeResults, out DiagnosticInfoCollection removeDiagnosticInfos)
        {
            return m_session.EndSetTriggering(result, out addResults, out addDiagnosticInfos, out removeResults, out removeDiagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<SetTriggeringResponse> SetTriggeringAsync(RequestHeader requestHeader, uint subscriptionId, uint triggeringItemId, UInt32Collection linksToAdd, UInt32Collection linksToRemove, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(SetTriggeringAsync)))
            {
                return m_session.SetTriggeringAsync(requestHeader, subscriptionId, triggeringItemId, linksToAdd, linksToRemove, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader DeleteMonitoredItems(RequestHeader requestHeader, uint subscriptionId, UInt32Collection monitoredItemIds, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(DeleteMonitoredItems)))
            {
                return m_session.DeleteMonitoredItems(requestHeader, subscriptionId, monitoredItemIds, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginDeleteMonitoredItems(RequestHeader requestHeader, uint subscriptionId, UInt32Collection monitoredItemIds, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginDeleteMonitoredItems(requestHeader, subscriptionId, monitoredItemIds, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndDeleteMonitoredItems(IAsyncResult result, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndDeleteMonitoredItems(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(RequestHeader requestHeader, uint subscriptionId, UInt32Collection monitoredItemIds, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(DeleteMonitoredItemsAsync)))
            {
                return m_session.DeleteMonitoredItemsAsync(requestHeader, subscriptionId, monitoredItemIds, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader CreateSubscription(RequestHeader requestHeader, double requestedPublishingInterval, uint requestedLifetimeCount, uint requestedMaxKeepAliveCount, uint maxNotificationsPerPublish, bool publishingEnabled, byte priority, out uint subscriptionId, out double revisedPublishingInterval, out uint revisedLifetimeCount, out uint revisedMaxKeepAliveCount)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(CreateSubscription)))
            {
                return m_session.CreateSubscription(requestHeader, requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount, maxNotificationsPerPublish, publishingEnabled, priority, out subscriptionId, out revisedPublishingInterval, out revisedLifetimeCount, out revisedMaxKeepAliveCount);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginCreateSubscription(RequestHeader requestHeader, double requestedPublishingInterval, uint requestedLifetimeCount, uint requestedMaxKeepAliveCount, uint maxNotificationsPerPublish, bool publishingEnabled, byte priority, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginCreateSubscription(requestHeader, requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount, maxNotificationsPerPublish, publishingEnabled, priority, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndCreateSubscription(IAsyncResult result, out uint subscriptionId, out double revisedPublishingInterval, out uint revisedLifetimeCount, out uint revisedMaxKeepAliveCount)
        {
            return m_session.EndCreateSubscription(result, out subscriptionId, out revisedPublishingInterval, out revisedLifetimeCount, out revisedMaxKeepAliveCount);
        }

        /// <inheritdoc/>
        public Task<CreateSubscriptionResponse> CreateSubscriptionAsync(RequestHeader requestHeader, double requestedPublishingInterval, uint requestedLifetimeCount, uint requestedMaxKeepAliveCount, uint maxNotificationsPerPublish, bool publishingEnabled, byte priority, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(CreateSubscriptionAsync)))
            {
                return m_session.CreateSubscriptionAsync(requestHeader, requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount, maxNotificationsPerPublish, publishingEnabled, priority, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader ModifySubscription(RequestHeader requestHeader, uint subscriptionId, double requestedPublishingInterval, uint requestedLifetimeCount, uint requestedMaxKeepAliveCount, uint maxNotificationsPerPublish, byte priority, out double revisedPublishingInterval, out uint revisedLifetimeCount, out uint revisedMaxKeepAliveCount)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ModifySubscription)))
            {
                return m_session.ModifySubscription(requestHeader, subscriptionId, requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount, maxNotificationsPerPublish, priority, out revisedPublishingInterval, out revisedLifetimeCount, out revisedMaxKeepAliveCount);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginModifySubscription(RequestHeader requestHeader, uint subscriptionId, double requestedPublishingInterval, uint requestedLifetimeCount, uint requestedMaxKeepAliveCount, uint maxNotificationsPerPublish, byte priority, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginModifySubscription(requestHeader, subscriptionId, requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount, maxNotificationsPerPublish, priority, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndModifySubscription(IAsyncResult result, out double revisedPublishingInterval, out uint revisedLifetimeCount, out uint revisedMaxKeepAliveCount)
        {
            return m_session.EndModifySubscription(result, out revisedPublishingInterval, out revisedLifetimeCount, out revisedMaxKeepAliveCount);
        }

        /// <inheritdoc/>
        public Task<ModifySubscriptionResponse> ModifySubscriptionAsync(RequestHeader requestHeader, uint subscriptionId, double requestedPublishingInterval, uint requestedLifetimeCount, uint requestedMaxKeepAliveCount, uint maxNotificationsPerPublish, byte priority, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(ModifySubscriptionAsync)))
            {
                return m_session.ModifySubscriptionAsync(requestHeader, subscriptionId, requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount, maxNotificationsPerPublish, priority, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader SetPublishingMode(RequestHeader requestHeader, bool publishingEnabled, UInt32Collection subscriptionIds, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(SetPublishingMode)))
            {
                return m_session.SetPublishingMode(requestHeader, publishingEnabled, subscriptionIds, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginSetPublishingMode(RequestHeader requestHeader, bool publishingEnabled, UInt32Collection subscriptionIds, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginSetPublishingMode(requestHeader, publishingEnabled, subscriptionIds, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndSetPublishingMode(IAsyncResult result, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndSetPublishingMode(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<SetPublishingModeResponse> SetPublishingModeAsync(RequestHeader requestHeader, bool publishingEnabled, UInt32Collection subscriptionIds, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(SetPublishingModeAsync)))
            {
                return m_session.SetPublishingModeAsync(requestHeader, publishingEnabled, subscriptionIds, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader Publish(RequestHeader requestHeader, SubscriptionAcknowledgementCollection subscriptionAcknowledgements, out uint subscriptionId, out UInt32Collection availableSequenceNumbers, out bool moreNotifications, out NotificationMessage notificationMessage, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Publish)))
            {
                return m_session.Publish(requestHeader, subscriptionAcknowledgements, out subscriptionId, out availableSequenceNumbers, out moreNotifications, out notificationMessage, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginPublish(RequestHeader requestHeader, SubscriptionAcknowledgementCollection subscriptionAcknowledgements, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginPublish(requestHeader, subscriptionAcknowledgements, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndPublish(IAsyncResult result, out uint subscriptionId, out UInt32Collection availableSequenceNumbers, out bool moreNotifications, out NotificationMessage notificationMessage, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndPublish(result, out subscriptionId, out availableSequenceNumbers, out moreNotifications, out notificationMessage, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<PublishResponse> PublishAsync(RequestHeader requestHeader, SubscriptionAcknowledgementCollection subscriptionAcknowledgements, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(PublishAsync)))
            {
                return m_session.PublishAsync(requestHeader, subscriptionAcknowledgements, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader Republish(RequestHeader requestHeader, uint subscriptionId, uint retransmitSequenceNumber, out NotificationMessage notificationMessage)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Republish)))
            {
                return m_session.Republish(requestHeader, subscriptionId, retransmitSequenceNumber, out notificationMessage);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginRepublish(RequestHeader requestHeader, uint subscriptionId, uint retransmitSequenceNumber, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginRepublish(requestHeader, subscriptionId, retransmitSequenceNumber, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndRepublish(IAsyncResult result, out NotificationMessage notificationMessage)
        {
            return m_session.EndRepublish(result, out notificationMessage);
        }

        /// <inheritdoc/>
        public Task<RepublishResponse> RepublishAsync(RequestHeader requestHeader, uint subscriptionId, uint retransmitSequenceNumber, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(RepublishAsync)))
            {
                return m_session.RepublishAsync(requestHeader, subscriptionId, retransmitSequenceNumber, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader TransferSubscriptions(RequestHeader requestHeader, UInt32Collection subscriptionIds, bool sendInitialValues, out TransferResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(TransferSubscriptions)))
            {
                return m_session.TransferSubscriptions(requestHeader, subscriptionIds, sendInitialValues, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginTransferSubscriptions(RequestHeader requestHeader, UInt32Collection subscriptionIds, bool sendInitialValues, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginTransferSubscriptions(requestHeader, subscriptionIds, sendInitialValues, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndTransferSubscriptions(IAsyncResult result, out TransferResultCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndTransferSubscriptions(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<TransferSubscriptionsResponse> TransferSubscriptionsAsync(RequestHeader requestHeader, UInt32Collection subscriptionIds, bool sendInitialValues, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(TransferSubscriptionsAsync)))
            {
                return m_session.TransferSubscriptionsAsync(requestHeader, subscriptionIds, sendInitialValues, ct);
            }
        }

        /// <inheritdoc/>
        public ResponseHeader DeleteSubscriptions(RequestHeader requestHeader, UInt32Collection subscriptionIds, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(DeleteSubscriptions)))
            {
                return m_session.DeleteSubscriptions(requestHeader, subscriptionIds, out results, out diagnosticInfos);
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginDeleteSubscriptions(RequestHeader requestHeader, UInt32Collection subscriptionIds, AsyncCallback callback, object asyncState)
        {
            return m_session.BeginDeleteSubscriptions(requestHeader, subscriptionIds, callback, asyncState);
        }

        /// <inheritdoc/>
        public ResponseHeader EndDeleteSubscriptions(IAsyncResult result, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos)
        {
            return m_session.EndDeleteSubscriptions(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public Task<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(RequestHeader requestHeader, UInt32Collection subscriptionIds, CancellationToken ct)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(DeleteSubscriptionsAsync)))
            {
                return m_session.DeleteSubscriptionsAsync(requestHeader, subscriptionIds, ct);
            }
        }

        /// <inheritdoc/>
        public void AttachChannel(ITransportChannel channel)
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(AttachChannel)))
            {
                m_session.AttachChannel(channel);
            }
        }

        /// <inheritdoc/>
        public void DetachChannel()
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(DetachChannel)))
            {
                m_session.DetachChannel();
            }
        }

        /// <inheritdoc/>
        public StatusCode Close()
        {
            using (Activity activity = ActivitySrc.StartActivity(nameof(Close)))
            {
                return m_session.Close();
            }
        }

        /// <inheritdoc/>
        public uint NewRequestHandle()
        {
            return m_session.NewRequestHandle();
        }

        /// <inheritdoc/>
        protected void Dispose(bool disposing)
        {
            m_session.Dispose();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
#endif
