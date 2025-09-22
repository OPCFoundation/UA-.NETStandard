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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Decorator class for traceable session with Activity Source.
    /// </summary>
    public class TraceableSession : ISession
    {
        /// <summary>
        /// Obsolete default constructor
        /// </summary>
        [Obsolete("Use TraceableSession(ITelemetryContext) instead.")]
        public TraceableSession(ISession session)
            : this(session, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public TraceableSession(ISession session, ITelemetryContext telemetry)
        {
            Session = session;
            m_telemetry = telemetry;
            SessionFactory = new TraceableSessionFactory(telemetry);
        }

        /// <inheritdoc/>
        public ISession Session { get; }

        /// <inheritdoc/>
        public event KeepAliveEventHandler KeepAlive
        {
            add => Session.KeepAlive += value;
            remove => Session.KeepAlive -= value;
        }

        /// <inheritdoc/>
        public event NotificationEventHandler Notification
        {
            add => Session.Notification += value;
            remove => Session.Notification -= value;
        }

        /// <inheritdoc/>
        public event PublishErrorEventHandler PublishError
        {
            add => Session.PublishError += value;
            remove => Session.PublishError -= value;
        }

        /// <inheritdoc/>
        public event PublishSequenceNumbersToAcknowledgeEventHandler PublishSequenceNumbersToAcknowledge
        {
            add => Session.PublishSequenceNumbersToAcknowledge += value;
            remove => Session.PublishSequenceNumbersToAcknowledge -= value;
        }

        /// <inheritdoc/>
        public event EventHandler SubscriptionsChanged
        {
            add => Session.SubscriptionsChanged += value;
            remove => Session.SubscriptionsChanged -= value;
        }

        /// <inheritdoc/>
        public event EventHandler SessionClosing
        {
            add => Session.SessionClosing += value;
            remove => Session.SessionClosing -= value;
        }

        /// <inheritdoc/>
        public event EventHandler SessionConfigurationChanged
        {
            add => Session.SessionConfigurationChanged += value;
            remove => Session.SessionConfigurationChanged -= value;
        }

        /// <inheritdoc/>
        public event RenewUserIdentityEventHandler RenewUserIdentity
        {
            add => Session.RenewUserIdentity += value;
            remove => Session.RenewUserIdentity -= value;
        }

        /// <inheritdoc/>
        public ISessionFactory SessionFactory { get; }

        /// <inheritdoc/>
        public ConfiguredEndpoint ConfiguredEndpoint => Session.ConfiguredEndpoint;

        /// <inheritdoc/>
        public string SessionName => Session.SessionName;

        /// <inheritdoc/>
        public double SessionTimeout => Session.SessionTimeout;

        /// <inheritdoc/>
        public object Handle => Session.Handle;

        /// <inheritdoc/>
        public IUserIdentity Identity => Session.Identity;

        /// <inheritdoc/>
        public IEnumerable<IUserIdentity> IdentityHistory => Session.IdentityHistory;

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris => Session.NamespaceUris;

        /// <inheritdoc/>
        public StringTable ServerUris => Session.ServerUris;

        /// <inheritdoc/>
        public ISystemContext SystemContext => Session.SystemContext;

        /// <inheritdoc/>
        public IEncodeableFactory Factory => Session.Factory;

        /// <inheritdoc/>
        public ITypeTable TypeTree => Session.TypeTree;

        /// <inheritdoc/>
        public INodeCache NodeCache => Session.NodeCache;

        /// <inheritdoc/>
        public FilterContext FilterContext => Session.FilterContext;

        /// <inheritdoc/>
        public StringCollection PreferredLocales => Session.PreferredLocales;

        /// <inheritdoc/>
        public IEnumerable<Subscription> Subscriptions => Session.Subscriptions;

        /// <inheritdoc/>
        public int SubscriptionCount => Session.SubscriptionCount;

        /// <inheritdoc/>
        public bool DeleteSubscriptionsOnClose
        {
            get => Session.DeleteSubscriptionsOnClose;
            set => Session.DeleteSubscriptionsOnClose = value;
        }

        /// <inheritdoc/>
        public Subscription DefaultSubscription
        {
            get => Session.DefaultSubscription;
            set => Session.DefaultSubscription = value;
        }

        /// <inheritdoc/>
        public int KeepAliveInterval
        {
            get => Session.KeepAliveInterval;
            set => Session.KeepAliveInterval = value;
        }

        /// <inheritdoc/>
        public bool KeepAliveStopped => Session.KeepAliveStopped;

        /// <inheritdoc/>
        public DateTime LastKeepAliveTime => Session.LastKeepAliveTime;

        /// <inheritdoc/>
        public int LastKeepAliveTickCount => Session.LastKeepAliveTickCount;

        /// <inheritdoc/>
        public int OutstandingRequestCount => Session.OutstandingRequestCount;

        /// <inheritdoc/>
        public int DefunctRequestCount => Session.DefunctRequestCount;

        /// <inheritdoc/>
        public int GoodPublishRequestCount => Session.GoodPublishRequestCount;

        /// <inheritdoc/>
        public int MinPublishRequestCount
        {
            get => Session.MinPublishRequestCount;
            set => Session.MinPublishRequestCount = value;
        }

        /// <inheritdoc/>
        public int MaxPublishRequestCount
        {
            get => Session.MaxPublishRequestCount;
            set => Session.MaxPublishRequestCount = value;
        }

        /// <inheritdoc/>
        public OperationLimits OperationLimits => Session.OperationLimits;

        /// <inheritdoc/>
        public bool TransferSubscriptionsOnReconnect
        {
            get => Session.TransferSubscriptionsOnReconnect;
            set => Session.TransferSubscriptionsOnReconnect = value;
        }

        /// <inheritdoc/>
        public NodeId SessionId => Session.SessionId;

        /// <inheritdoc/>
        public bool Connected => Session.Connected;

        /// <inheritdoc/>
        public EndpointDescription Endpoint => Session.Endpoint;

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration => Session.EndpointConfiguration;

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext => Session.MessageContext;

        /// <inheritdoc/>
        public ITransportChannel NullableTransportChannel => Session.NullableTransportChannel;

        /// <inheritdoc/>
        public ITransportChannel TransportChannel => Session.TransportChannel;

        /// <inheritdoc/>
        public DiagnosticsMasks ReturnDiagnostics
        {
            get => Session.ReturnDiagnostics;
            set => Session.ReturnDiagnostics = value;
        }

        /// <inheritdoc/>
        public int OperationTimeout
        {
            get => Session.OperationTimeout;
            set => Session.OperationTimeout = value;
        }

        /// <inheritdoc/>
        public bool Disposed => Session.Disposed;

        /// <inheritdoc/>
        public bool CheckDomain => Session.CheckDomain;

        /// <inheritdoc/>
        public ContinuationPointPolicy ContinuationPointPolicy
        {
            get => Session.ContinuationPointPolicy;
            set => Session.ContinuationPointPolicy = value;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }
            // Presume that the wrapper is being compared to the
            // wrapped object, e.g. in a keep alive callback.
            if (ReferenceEquals(Session, obj))
            {
                return true;
            }

            return Session?.Equals(obj) ?? false;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Session?.GetHashCode() ?? base.GetHashCode();
        }

        /// <inheritdoc/>
        public async Task ReconnectAsync(CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            await Session.ReconnectAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task ReconnectAsync(
            ITransportWaitingConnection connection,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            await Session.ReconnectAsync(connection, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task ReconnectAsync(ITransportChannel channel, CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            await Session.ReconnectAsync(channel, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task ReloadInstanceCertificateAsync(CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            await Session.ReloadInstanceCertificateAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Save(string filePath, IEnumerable<Type> knownTypes = null)
        {
            using Activity activity = m_telemetry.StartActivity();
            Session.Save(filePath, knownTypes);
        }

        /// <inheritdoc/>
        public void Save(
            Stream stream,
            IEnumerable<Subscription> subscriptions,
            IEnumerable<Type> knownTypes = null)
        {
            using Activity activity = m_telemetry.StartActivity();
            Session.Save(stream, subscriptions, knownTypes);
        }

        /// <inheritdoc/>
        public void Save(
            string filePath,
            IEnumerable<Subscription> subscriptions,
            IEnumerable<Type> knownTypes = null)
        {
            using Activity activity = m_telemetry.StartActivity();
            Session.Save(filePath, subscriptions, knownTypes);
        }

        /// <inheritdoc/>
        public IEnumerable<Subscription> Load(
            Stream stream,
            bool transferSubscriptions = false,
            IEnumerable<Type> knownTypes = null)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.Load(stream, transferSubscriptions, knownTypes);
        }

        /// <inheritdoc/>
        public IEnumerable<Subscription> Load(
            string filePath,
            bool transferSubscriptions = false,
            IEnumerable<Type> knownTypes = null)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.Load(filePath, transferSubscriptions, knownTypes);
        }

        /// <inheritdoc/>
        public async Task FetchTypeTreeAsync(ExpandedNodeId typeId, CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            await Session.FetchTypeTreeAsync(typeId, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task FetchTypeTreeAsync(
            ExpandedNodeIdCollection typeIds,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            await Session.FetchTypeTreeAsync(typeIds, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<ReferenceDescriptionCollection> FetchReferencesAsync(
            NodeId nodeId,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.FetchReferencesAsync(nodeId, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<(IList<ReferenceDescriptionCollection>, IList<ServiceResult>)> FetchReferencesAsync(
            IList<NodeId> nodeIds,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.FetchReferencesAsync(nodeIds, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task ChangePreferredLocalesAsync(
            StringCollection preferredLocales,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            await Session.ChangePreferredLocalesAsync(preferredLocales, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task UpdateSessionAsync(
            IUserIdentity identity,
            StringCollection preferredLocales,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            await Session.UpdateSessionAsync(identity, preferredLocales, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void FindComponentIds(
            NodeId instanceId,
            IList<string> componentPaths,
            out NodeIdCollection componentIds,
            out IList<ServiceResult> errors)
        {
            using Activity activity = m_telemetry.StartActivity();
            Session.FindComponentIds(instanceId, componentPaths, out componentIds, out errors);
        }

        /// <inheritdoc/>
        public async Task<byte[]> ReadByteStringInChunksAsync(NodeId nodeId, CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.ReadByteStringInChunksAsync(nodeId, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task OpenAsync(
            string sessionName,
            IUserIdentity identity,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            await Session.OpenAsync(sessionName, identity, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task OpenAsync(
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            await Session.OpenAsync(sessionName, sessionTimeout, identity, preferredLocales, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task OpenAsync(
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            bool checkDomain,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            await Session
                .OpenAsync(sessionName, sessionTimeout, identity, preferredLocales, checkDomain, ct)
                .ConfigureAwait(false);
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
            using Activity activity = m_telemetry.StartActivity();
            await Session
                .OpenAsync(
                    sessionName,
                    sessionTimeout,
                    identity,
                    preferredLocales,
                    checkDomain,
                    closeChannel,
                    ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task FetchNamespaceTablesAsync(CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            await Session.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<(IList<Node>, IList<ServiceResult>)> ReadNodesAsync(
            IList<NodeId> nodeIds,
            NodeClass nodeClass,
            bool optionalAttributes = false,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.ReadNodesAsync(nodeIds, nodeClass, optionalAttributes, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<DataValue> ReadValueAsync(NodeId nodeId, CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.ReadValueAsync(nodeId, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<Node> ReadNodeAsync(NodeId nodeId, CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.ReadNodeAsync(nodeId, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<Node> ReadNodeAsync(
            NodeId nodeId,
            NodeClass nodeClass,
            bool optionalAttributes = true,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.ReadNodeAsync(nodeId, nodeClass, optionalAttributes, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<(IList<Node>, IList<ServiceResult>)> ReadNodesAsync(
            IList<NodeId> nodeIds,
            bool optionalAttributes = false,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.ReadNodesAsync(nodeIds, optionalAttributes, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<(DataValueCollection, IList<ServiceResult>)> ReadValuesAsync(
            IList<NodeId> nodeIds,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.ReadValuesAsync(nodeIds, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<StatusCode> CloseAsync(CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.CloseAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<StatusCode> CloseAsync(bool closeChannel, CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.CloseAsync(closeChannel, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<StatusCode> CloseAsync(int timeout, CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.CloseAsync(timeout, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<StatusCode> CloseAsync(
            int timeout,
            bool closeChannel,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.CloseAsync(timeout, closeChannel, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public bool AddSubscription(Subscription subscription)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.AddSubscription(subscription);
        }

        /// <inheritdoc/>
        public bool RemoveTransferredSubscription(Subscription subscription)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.RemoveTransferredSubscription(subscription);
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionAsync(Subscription subscription)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionsAsync(IEnumerable<Subscription> subscriptions)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.RemoveSubscriptionsAsync(subscriptions).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public IAsyncResult BeginPublish(int timeout)
        {
            return Session.BeginPublish(timeout);
        }

        /// <inheritdoc/>
        public void StartPublishing(int timeout, bool fullQueue)
        {
            using Activity activity = m_telemetry.StartActivity();
            Session.StartPublishing(timeout, fullQueue);
        }

        /// <inheritdoc/>
        public async Task<(bool, ServiceResult)> RepublishAsync(
            uint subscriptionId,
            uint sequenceNumber,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.RepublishAsync(subscriptionId, sequenceNumber, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use CreateSessionAsync() instead.")]
        public ResponseHeader CreateSession(
            RequestHeader requestHeader,
            ApplicationDescription clientDescription,
            string serverUri,
            string endpointUrl,
            string sessionName,
            byte[] clientNonce,
            byte[] clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            out NodeId sessionId,
            out NodeId authenticationToken,
            out double revisedSessionTimeout,
            out byte[] serverNonce,
            out byte[] serverCertificate,
            out EndpointDescriptionCollection serverEndpoints,
            out SignedSoftwareCertificateCollection serverSoftwareCertificates,
            out SignatureData serverSignature,
            out uint maxRequestMessageSize)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.CreateSession(
                requestHeader,
                clientDescription,
                serverUri,
                endpointUrl,
                sessionName,
                clientNonce,
                clientCertificate,
                requestedSessionTimeout,
                maxResponseMessageSize,
                out sessionId,
                out authenticationToken,
                out revisedSessionTimeout,
                out serverNonce,
                out serverCertificate,
                out serverEndpoints,
                out serverSoftwareCertificates,
                out serverSignature,
                out maxRequestMessageSize);
        }

        /// <inheritdoc/>
        [Obsolete("Use CreateSessionAsync() instead.")]
        public IAsyncResult BeginCreateSession(
            RequestHeader requestHeader,
            ApplicationDescription clientDescription,
            string serverUri,
            string endpointUrl,
            string sessionName,
            byte[] clientNonce,
            byte[] clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginCreateSession(
                requestHeader,
                clientDescription,
                serverUri,
                endpointUrl,
                sessionName,
                clientNonce,
                clientCertificate,
                requestedSessionTimeout,
                maxResponseMessageSize,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use CreateSessionAsync() instead.")]
        public ResponseHeader EndCreateSession(
            IAsyncResult result,
            out NodeId sessionId,
            out NodeId authenticationToken,
            out double revisedSessionTimeout,
            out byte[] serverNonce,
            out byte[] serverCertificate,
            out EndpointDescriptionCollection serverEndpoints,
            out SignedSoftwareCertificateCollection serverSoftwareCertificates,
            out SignatureData serverSignature,
            out uint maxRequestMessageSize)
        {
            return Session.EndCreateSession(
                result,
                out sessionId,
                out authenticationToken,
                out revisedSessionTimeout,
                out serverNonce,
                out serverCertificate,
                out serverEndpoints,
                out serverSoftwareCertificates,
                out serverSignature,
                out maxRequestMessageSize);
        }

        /// <inheritdoc/>
        public async Task<CreateSessionResponse> CreateSessionAsync(
            RequestHeader requestHeader,
            ApplicationDescription clientDescription,
            string serverUri,
            string endpointUrl,
            string sessionName,
            byte[] clientNonce,
            byte[] clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .CreateSessionAsync(
                    requestHeader,
                    clientDescription,
                    serverUri,
                    endpointUrl,
                    sessionName,
                    clientNonce,
                    clientCertificate,
                    requestedSessionTimeout,
                    maxResponseMessageSize,
                    ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use ActivateSessionAsync() instead.")]
        public ResponseHeader ActivateSession(
            RequestHeader requestHeader,
            SignatureData clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection localeIds,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            out byte[] serverNonce,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.ActivateSession(
                requestHeader,
                clientSignature,
                clientSoftwareCertificates,
                localeIds,
                userIdentityToken,
                userTokenSignature,
                out serverNonce,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use ActivateSessionAsync() instead.")]
        public IAsyncResult BeginActivateSession(
            RequestHeader requestHeader,
            SignatureData clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection localeIds,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginActivateSession(
                requestHeader,
                clientSignature,
                clientSoftwareCertificates,
                localeIds,
                userIdentityToken,
                userTokenSignature,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use ActivateSessionAsync() instead.")]
        public ResponseHeader EndActivateSession(
            IAsyncResult result,
            out byte[] serverNonce,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndActivateSession(
                result,
                out serverNonce,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<ActivateSessionResponse> ActivateSessionAsync(
            RequestHeader requestHeader,
            SignatureData clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection localeIds,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .ActivateSessionAsync(
                    requestHeader,
                    clientSignature,
                    clientSoftwareCertificates,
                    localeIds,
                    userIdentityToken,
                    userTokenSignature,
                    ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use CloseSessionAsync() instead.")]
        public ResponseHeader CloseSession(RequestHeader requestHeader, bool deleteSubscriptions)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.CloseSession(requestHeader, deleteSubscriptions);
        }

        /// <inheritdoc/>
        [Obsolete("Use CloseSessionAsync() instead.")]
        public IAsyncResult BeginCloseSession(
            RequestHeader requestHeader,
            bool deleteSubscriptions,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginCloseSession(
                requestHeader,
                deleteSubscriptions,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use CloseSessionAsync() instead.")]
        public ResponseHeader EndCloseSession(IAsyncResult result)
        {
            return Session.EndCloseSession(result);
        }

        /// <inheritdoc/>
        public async Task<CloseSessionResponse> CloseSessionAsync(
            RequestHeader requestHeader,
            bool deleteSubscriptions,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.CloseSessionAsync(requestHeader, deleteSubscriptions, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use CancelAsync() instead.")]
        public ResponseHeader Cancel(
            RequestHeader requestHeader,
            uint requestHandle,
            out uint cancelCount)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.Cancel(requestHeader, requestHandle, out cancelCount);
        }

        /// <inheritdoc/>
        [Obsolete("Use CancelAsync() instead.")]
        public IAsyncResult BeginCancel(
            RequestHeader requestHeader,
            uint requestHandle,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginCancel(requestHeader, requestHandle, callback, asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use CancelAsync() instead.")]
        public ResponseHeader EndCancel(IAsyncResult result, out uint cancelCount)
        {
            return Session.EndCancel(result, out cancelCount);
        }

        /// <inheritdoc/>
        public async Task<CancelResponse> CancelAsync(
            RequestHeader requestHeader,
            uint requestHandle,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.CancelAsync(requestHeader, requestHandle, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use AddNodesAsync() instead.")]
        public ResponseHeader AddNodes(
            RequestHeader requestHeader,
            AddNodesItemCollection nodesToAdd,
            out AddNodesResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.AddNodes(requestHeader, nodesToAdd, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use AddNodesAsync() instead.")]
        public IAsyncResult BeginAddNodes(
            RequestHeader requestHeader,
            AddNodesItemCollection nodesToAdd,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginAddNodes(requestHeader, nodesToAdd, callback, asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use AddNodesAsync() instead.")]
        public ResponseHeader EndAddNodes(
            IAsyncResult result,
            out AddNodesResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndAddNodes(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<AddNodesResponse> AddNodesAsync(
            RequestHeader requestHeader,
            AddNodesItemCollection nodesToAdd,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.AddNodesAsync(requestHeader, nodesToAdd, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use AddReferencesAsync() instead.")]
        public ResponseHeader AddReferences(
            RequestHeader requestHeader,
            AddReferencesItemCollection referencesToAdd,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.AddReferences(
                requestHeader,
                referencesToAdd,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use AddReferencesAsync() instead.")]
        public IAsyncResult BeginAddReferences(
            RequestHeader requestHeader,
            AddReferencesItemCollection referencesToAdd,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginAddReferences(requestHeader, referencesToAdd, callback, asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use AddReferencesAsync() instead.")]
        public ResponseHeader EndAddReferences(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndAddReferences(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<AddReferencesResponse> AddReferencesAsync(
            RequestHeader requestHeader,
            AddReferencesItemCollection referencesToAdd,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.AddReferencesAsync(requestHeader, referencesToAdd, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use DeleteNodesAsync() instead.")]
        public ResponseHeader DeleteNodes(
            RequestHeader requestHeader,
            DeleteNodesItemCollection nodesToDelete,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.DeleteNodes(
                requestHeader,
                nodesToDelete,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use DeleteNodesAsync() instead.")]
        public IAsyncResult BeginDeleteNodes(
            RequestHeader requestHeader,
            DeleteNodesItemCollection nodesToDelete,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginDeleteNodes(requestHeader, nodesToDelete, callback, asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use DeleteNodesAsync() instead.")]
        public ResponseHeader EndDeleteNodes(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndDeleteNodes(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<DeleteNodesResponse> DeleteNodesAsync(
            RequestHeader requestHeader,
            DeleteNodesItemCollection nodesToDelete,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.DeleteNodesAsync(requestHeader, nodesToDelete, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use DeleteReferencesAsync() instead.")]
        public ResponseHeader DeleteReferences(
            RequestHeader requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.DeleteReferences(
                requestHeader,
                referencesToDelete,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use DeleteReferencesAsync() instead.")]
        public IAsyncResult BeginDeleteReferences(
            RequestHeader requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginDeleteReferences(
                requestHeader,
                referencesToDelete,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use DeleteReferencesAsync() instead.")]
        public ResponseHeader EndDeleteReferences(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndDeleteReferences(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<DeleteReferencesResponse> DeleteReferencesAsync(
            RequestHeader requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.DeleteReferencesAsync(requestHeader, referencesToDelete, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use BrowseAsync() instead.")]
        public ResponseHeader Browse(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.Browse(
                requestHeader,
                view,
                requestedMaxReferencesPerNode,
                nodesToBrowse,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use BrowseAsync() instead.")]
        public IAsyncResult BeginBrowse(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginBrowse(
                requestHeader,
                view,
                requestedMaxReferencesPerNode,
                nodesToBrowse,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use BrowseAsync() instead.")]
        public ResponseHeader EndBrowse(
            IAsyncResult result,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndBrowse(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<BrowseResponse> BrowseAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .BrowseAsync(requestHeader, view, requestedMaxReferencesPerNode, nodesToBrowse, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use BrowseNextAsync() instead.")]
        public ResponseHeader BrowseNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.BrowseNext(
                requestHeader,
                releaseContinuationPoints,
                continuationPoints,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use BrowseNextAsync() instead.")]
        public IAsyncResult BeginBrowseNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginBrowseNext(
                requestHeader,
                releaseContinuationPoints,
                continuationPoints,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use BrowseNextAsync() instead.")]
        public ResponseHeader EndBrowseNext(
            IAsyncResult result,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndBrowseNext(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<BrowseNextResponse> BrowseNextAsync(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .BrowseNextAsync(requestHeader, releaseContinuationPoints, continuationPoints, ct)
                .ConfigureAwait(false);
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
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .ManagedBrowseAsync(
                    requestHeader,
                    view,
                    nodesToBrowse,
                    maxResultsToReturn,
                    browseDirection,
                    referenceTypeId,
                    includeSubtypes,
                    nodeClassMask,
                    ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use TranslateBrowsePathsToNodeIdsAsync() instead.")]
        public ResponseHeader TranslateBrowsePathsToNodeIds(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.TranslateBrowsePathsToNodeIds(
                requestHeader,
                browsePaths,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use TranslateBrowsePathsToNodeIdsAsync() instead.")]
        public IAsyncResult BeginTranslateBrowsePathsToNodeIds(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginTranslateBrowsePathsToNodeIds(
                requestHeader,
                browsePaths,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use TranslateBrowsePathsToNodeIdsAsync() instead.")]
        public ResponseHeader EndTranslateBrowsePathsToNodeIds(
            IAsyncResult result,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndTranslateBrowsePathsToNodeIds(
                result,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .TranslateBrowsePathsToNodeIdsAsync(requestHeader, browsePaths, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use RegisterNodesAsync() instead.")]
        public ResponseHeader RegisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToRegister,
            out NodeIdCollection registeredNodeIds)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.RegisterNodes(requestHeader, nodesToRegister, out registeredNodeIds);
        }

        /// <inheritdoc/>
        [Obsolete("Use RegisterNodesAsync() instead.")]
        public IAsyncResult BeginRegisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToRegister,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginRegisterNodes(requestHeader, nodesToRegister, callback, asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use RegisterNodesAsync() instead.")]
        public ResponseHeader EndRegisterNodes(
            IAsyncResult result,
            out NodeIdCollection registeredNodeIds)
        {
            return Session.EndRegisterNodes(result, out registeredNodeIds);
        }

        /// <inheritdoc/>
        public async Task<RegisterNodesResponse> RegisterNodesAsync(
            RequestHeader requestHeader,
            NodeIdCollection nodesToRegister,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.RegisterNodesAsync(requestHeader, nodesToRegister, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use UnregisterNodesAsync() instead.")]
        public ResponseHeader UnregisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToUnregister)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.UnregisterNodes(requestHeader, nodesToUnregister);
        }

        /// <inheritdoc/>
        [Obsolete("Use UnregisterNodesAsync() instead.")]
        public IAsyncResult BeginUnregisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToUnregister,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginUnregisterNodes(
                requestHeader,
                nodesToUnregister,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use UnregisterNodesAsync() instead.")]
        public ResponseHeader EndUnregisterNodes(IAsyncResult result)
        {
            return Session.EndUnregisterNodes(result);
        }

        /// <inheritdoc/>
        public async Task<UnregisterNodesResponse> UnregisterNodesAsync(
            RequestHeader requestHeader,
            NodeIdCollection nodesToUnregister,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.UnregisterNodesAsync(requestHeader, nodesToUnregister, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use QueryFirstAsync() instead.")]
        public ResponseHeader QueryFirst(
            RequestHeader requestHeader,
            ViewDescription view,
            NodeTypeDescriptionCollection nodeTypes,
            ContentFilter filter,
            uint maxDataSetsToReturn,
            uint maxReferencesToReturn,
            out QueryDataSetCollection queryDataSets,
            out byte[] continuationPoint,
            out ParsingResultCollection parsingResults,
            out DiagnosticInfoCollection diagnosticInfos,
            out ContentFilterResult filterResult)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.QueryFirst(
                requestHeader,
                view,
                nodeTypes,
                filter,
                maxDataSetsToReturn,
                maxReferencesToReturn,
                out queryDataSets,
                out continuationPoint,
                out parsingResults,
                out diagnosticInfos,
                out filterResult);
        }

        /// <inheritdoc/>
        [Obsolete("Use QueryFirstAsync() instead.")]
        public IAsyncResult BeginQueryFirst(
            RequestHeader requestHeader,
            ViewDescription view,
            NodeTypeDescriptionCollection nodeTypes,
            ContentFilter filter,
            uint maxDataSetsToReturn,
            uint maxReferencesToReturn,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginQueryFirst(
                requestHeader,
                view,
                nodeTypes,
                filter,
                maxDataSetsToReturn,
                maxReferencesToReturn,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use QueryFirstAsync() instead.")]
        public ResponseHeader EndQueryFirst(
            IAsyncResult result,
            out QueryDataSetCollection queryDataSets,
            out byte[] continuationPoint,
            out ParsingResultCollection parsingResults,
            out DiagnosticInfoCollection diagnosticInfos,
            out ContentFilterResult filterResult)
        {
            return Session.EndQueryFirst(
                result,
                out queryDataSets,
                out continuationPoint,
                out parsingResults,
                out diagnosticInfos,
                out filterResult);
        }

        /// <inheritdoc/>
        public async Task<QueryFirstResponse> QueryFirstAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            NodeTypeDescriptionCollection nodeTypes,
            ContentFilter filter,
            uint maxDataSetsToReturn,
            uint maxReferencesToReturn,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .QueryFirstAsync(
                    requestHeader,
                    view,
                    nodeTypes,
                    filter,
                    maxDataSetsToReturn,
                    maxReferencesToReturn,
                    ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use QueryNextAsync() instead.")]
        public ResponseHeader QueryNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoint,
            byte[] continuationPoint,
            out QueryDataSetCollection queryDataSets,
            out byte[] revisedContinuationPoint)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.QueryNext(
                requestHeader,
                releaseContinuationPoint,
                continuationPoint,
                out queryDataSets,
                out revisedContinuationPoint);
        }

        /// <inheritdoc/>
        [Obsolete("Use QueryNextAsync() instead.")]
        public IAsyncResult BeginQueryNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoint,
            byte[] continuationPoint,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginQueryNext(
                requestHeader,
                releaseContinuationPoint,
                continuationPoint,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use QueryNextAsync() instead.")]
        public ResponseHeader EndQueryNext(
            IAsyncResult result,
            out QueryDataSetCollection queryDataSets,
            out byte[] revisedContinuationPoint)
        {
            return Session.EndQueryNext(result, out queryDataSets, out revisedContinuationPoint);
        }

        /// <inheritdoc/>
        public async Task<QueryNextResponse> QueryNextAsync(
            RequestHeader requestHeader,
            bool releaseContinuationPoint,
            byte[] continuationPoint,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .QueryNextAsync(requestHeader, releaseContinuationPoint, continuationPoint, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use ReadAsync() instead.")]
        public ResponseHeader Read(
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            out DataValueCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.Read(
                requestHeader,
                maxAge,
                timestampsToReturn,
                nodesToRead,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use ReadAsync() instead.")]
        public IAsyncResult BeginRead(
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginRead(
                requestHeader,
                maxAge,
                timestampsToReturn,
                nodesToRead,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use ReadAsync() instead.")]
        public ResponseHeader EndRead(
            IAsyncResult result,
            out DataValueCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndRead(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<ReadResponse> ReadAsync(
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .ReadAsync(requestHeader, maxAge, timestampsToReturn, nodesToRead, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use HistoryReadAsync() instead.")]
        public ResponseHeader HistoryRead(
            RequestHeader requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            out HistoryReadResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.HistoryRead(
                requestHeader,
                historyReadDetails,
                timestampsToReturn,
                releaseContinuationPoints,
                nodesToRead,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use HistoryReadAsync() instead.")]
        public IAsyncResult BeginHistoryRead(
            RequestHeader requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginHistoryRead(
                requestHeader,
                historyReadDetails,
                timestampsToReturn,
                releaseContinuationPoints,
                nodesToRead,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use HistoryReadAsync() instead.")]
        public ResponseHeader EndHistoryRead(
            IAsyncResult result,
            out HistoryReadResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndHistoryRead(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<HistoryReadResponse> HistoryReadAsync(
            RequestHeader requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .HistoryReadAsync(
                    requestHeader,
                    historyReadDetails,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodesToRead,
                    ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use WriteAsync() instead.")]
        public ResponseHeader Write(
            RequestHeader requestHeader,
            WriteValueCollection nodesToWrite,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.Write(requestHeader, nodesToWrite, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use WriteAsync() instead.")]
        public IAsyncResult BeginWrite(
            RequestHeader requestHeader,
            WriteValueCollection nodesToWrite,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginWrite(requestHeader, nodesToWrite, callback, asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use WriteAsync() instead.")]
        public ResponseHeader EndWrite(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndWrite(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<WriteResponse> WriteAsync(
            RequestHeader requestHeader,
            WriteValueCollection nodesToWrite,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.WriteAsync(requestHeader, nodesToWrite, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use HistoryUpdateAsync() instead.")]
        public ResponseHeader HistoryUpdate(
            RequestHeader requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.HistoryUpdate(
                requestHeader,
                historyUpdateDetails,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use HistoryUpdateAsync() instead.")]
        public IAsyncResult BeginHistoryUpdate(
            RequestHeader requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginHistoryUpdate(
                requestHeader,
                historyUpdateDetails,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use HistoryUpdateAsync() instead.")]
        public ResponseHeader EndHistoryUpdate(
            IAsyncResult result,
            out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndHistoryUpdate(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<HistoryUpdateResponse> HistoryUpdateAsync(
            RequestHeader requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.HistoryUpdateAsync(requestHeader, historyUpdateDetails, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use CallAsync() instead.")]
        public ResponseHeader Call(
            RequestHeader requestHeader,
            CallMethodRequestCollection methodsToCall,
            out CallMethodResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.Call(requestHeader, methodsToCall, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use CallAsync() instead.")]
        public IAsyncResult BeginCall(
            RequestHeader requestHeader,
            CallMethodRequestCollection methodsToCall,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginCall(requestHeader, methodsToCall, callback, asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use CallAsync() instead.")]
        public ResponseHeader EndCall(
            IAsyncResult result,
            out CallMethodResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndCall(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<CallResponse> CallAsync(
            RequestHeader requestHeader,
            CallMethodRequestCollection methodsToCall,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.CallAsync(requestHeader, methodsToCall, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use CreateMonitoredItemsAsync() instead.")]
        public ResponseHeader CreateMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.CreateMonitoredItems(
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToCreate,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use CreateMonitoredItemsAsync() instead.")]
        public IAsyncResult BeginCreateMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginCreateMonitoredItems(
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToCreate,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use CreateMonitoredItemsAsync() instead.")]
        public ResponseHeader EndCreateMonitoredItems(
            IAsyncResult result,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndCreateMonitoredItems(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .CreateMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToCreate,
                    ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use ModifyMonitoredItemsAsync() instead.")]
        public ResponseHeader ModifyMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.ModifyMonitoredItems(
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToModify,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use ModifyMonitoredItemsAsync() instead.")]
        public IAsyncResult BeginModifyMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginModifyMonitoredItems(
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToModify,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use ModifyMonitoredItemsAsync() instead.")]
        public ResponseHeader EndModifyMonitoredItems(
            IAsyncResult result,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndModifyMonitoredItems(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .ModifyMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToModify,
                    ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use SetMonitoringModeAsync() instead.")]
        public ResponseHeader SetMonitoringMode(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.SetMonitoringMode(
                requestHeader,
                subscriptionId,
                monitoringMode,
                monitoredItemIds,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use SetMonitoringModeAsync() instead.")]
        public IAsyncResult BeginSetMonitoringMode(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginSetMonitoringMode(
                requestHeader,
                subscriptionId,
                monitoringMode,
                monitoredItemIds,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use SetMonitoringModeAsync() instead.")]
        public ResponseHeader EndSetMonitoringMode(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndSetMonitoringMode(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .SetMonitoringModeAsync(
                    requestHeader,
                    subscriptionId,
                    monitoringMode,
                    monitoredItemIds,
                    ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use SetTriggeringAsync() instead.")]
        public ResponseHeader SetTriggering(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            out StatusCodeCollection addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.SetTriggering(
                requestHeader,
                subscriptionId,
                triggeringItemId,
                linksToAdd,
                linksToRemove,
                out addResults,
                out addDiagnosticInfos,
                out removeResults,
                out removeDiagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use SetTriggeringAsync() instead.")]
        public IAsyncResult BeginSetTriggering(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginSetTriggering(
                requestHeader,
                subscriptionId,
                triggeringItemId,
                linksToAdd,
                linksToRemove,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use SetTriggeringAsync() instead.")]
        public ResponseHeader EndSetTriggering(
            IAsyncResult result,
            out StatusCodeCollection addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos)
        {
            return Session.EndSetTriggering(
                result,
                out addResults,
                out addDiagnosticInfos,
                out removeResults,
                out removeDiagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<SetTriggeringResponse> SetTriggeringAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .SetTriggeringAsync(
                    requestHeader,
                    subscriptionId,
                    triggeringItemId,
                    linksToAdd,
                    linksToRemove,
                    ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use DeleteMonitoredItemsAsync() instead.")]
        public ResponseHeader DeleteMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.DeleteMonitoredItems(
                requestHeader,
                subscriptionId,
                monitoredItemIds,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use DeleteMonitoredItemsAsync() instead.")]
        public IAsyncResult BeginDeleteMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginDeleteMonitoredItems(
                requestHeader,
                subscriptionId,
                monitoredItemIds,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use DeleteMonitoredItemsAsync() instead.")]
        public ResponseHeader EndDeleteMonitoredItems(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndDeleteMonitoredItems(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .DeleteMonitoredItemsAsync(requestHeader, subscriptionId, monitoredItemIds, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use CreateSubscriptionAsync() instead.")]
        public ResponseHeader CreateSubscription(
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            out uint subscriptionId,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.CreateSubscription(
                requestHeader,
                requestedPublishingInterval,
                requestedLifetimeCount,
                requestedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                publishingEnabled,
                priority,
                out subscriptionId,
                out revisedPublishingInterval,
                out revisedLifetimeCount,
                out revisedMaxKeepAliveCount);
        }

        /// <inheritdoc/>
        [Obsolete("Use CreateSubscriptionAsync() instead.")]
        public IAsyncResult BeginCreateSubscription(
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginCreateSubscription(
                requestHeader,
                requestedPublishingInterval,
                requestedLifetimeCount,
                requestedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                publishingEnabled,
                priority,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use CreateSubscriptionAsync() instead.")]
        public ResponseHeader EndCreateSubscription(
            IAsyncResult result,
            out uint subscriptionId,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            return Session.EndCreateSubscription(
                result,
                out subscriptionId,
                out revisedPublishingInterval,
                out revisedLifetimeCount,
                out revisedMaxKeepAliveCount);
        }

        /// <inheritdoc/>
        public async Task<CreateSubscriptionResponse> CreateSubscriptionAsync(
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .CreateSubscriptionAsync(
                    requestHeader,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    publishingEnabled,
                    priority,
                    ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use ModifySubscriptionAsync() instead.")]
        public ResponseHeader ModifySubscription(
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.ModifySubscription(
                requestHeader,
                subscriptionId,
                requestedPublishingInterval,
                requestedLifetimeCount,
                requestedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                priority,
                out revisedPublishingInterval,
                out revisedLifetimeCount,
                out revisedMaxKeepAliveCount);
        }

        /// <inheritdoc/>
        [Obsolete("Use ModifySubscriptionAsync() instead.")]
        public IAsyncResult BeginModifySubscription(
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginModifySubscription(
                requestHeader,
                subscriptionId,
                requestedPublishingInterval,
                requestedLifetimeCount,
                requestedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                priority,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use ModifySubscriptionAsync() instead.")]
        public ResponseHeader EndModifySubscription(
            IAsyncResult result,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            return Session.EndModifySubscription(
                result,
                out revisedPublishingInterval,
                out revisedLifetimeCount,
                out revisedMaxKeepAliveCount);
        }

        /// <inheritdoc/>
        public async Task<ModifySubscriptionResponse> ModifySubscriptionAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .ModifySubscriptionAsync(
                    requestHeader,
                    subscriptionId,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    priority,
                    ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use SetPublishingModeAsync() instead.")]
        public ResponseHeader SetPublishingMode(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.SetPublishingMode(
                requestHeader,
                publishingEnabled,
                subscriptionIds,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use SetPublishingModeAsync() instead.")]
        public IAsyncResult BeginSetPublishingMode(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginSetPublishingMode(
                requestHeader,
                publishingEnabled,
                subscriptionIds,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use SetPublishingModeAsync() instead.")]
        public ResponseHeader EndSetPublishingMode(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndSetPublishingMode(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<SetPublishingModeResponse> SetPublishingModeAsync(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .SetPublishingModeAsync(requestHeader, publishingEnabled, subscriptionIds, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use PublishAsync() instead.")]
        public ResponseHeader Publish(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            out uint subscriptionId,
            out UInt32Collection availableSequenceNumbers,
            out bool moreNotifications,
            out NotificationMessage notificationMessage,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.Publish(
                requestHeader,
                subscriptionAcknowledgements,
                out subscriptionId,
                out availableSequenceNumbers,
                out moreNotifications,
                out notificationMessage,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use PublishAsync() instead.")]
        public IAsyncResult BeginPublish(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginPublish(
                requestHeader,
                subscriptionAcknowledgements,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use PublishAsync() instead.")]
        public ResponseHeader EndPublish(
            IAsyncResult result,
            out uint subscriptionId,
            out UInt32Collection availableSequenceNumbers,
            out bool moreNotifications,
            out NotificationMessage notificationMessage,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndPublish(
                result,
                out subscriptionId,
                out availableSequenceNumbers,
                out moreNotifications,
                out notificationMessage,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<PublishResponse> PublishAsync(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.PublishAsync(requestHeader, subscriptionAcknowledgements, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use RepublishAsync() instead.")]
        public ResponseHeader Republish(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            out NotificationMessage notificationMessage)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.Republish(
                requestHeader,
                subscriptionId,
                retransmitSequenceNumber,
                out notificationMessage);
        }

        /// <inheritdoc/>
        [Obsolete("Use RepublishAsync() instead.")]
        public IAsyncResult BeginRepublish(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginRepublish(
                requestHeader,
                subscriptionId,
                retransmitSequenceNumber,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use RepublishAsync() instead.")]
        public ResponseHeader EndRepublish(
            IAsyncResult result,
            out NotificationMessage notificationMessage)
        {
            return Session.EndRepublish(result, out notificationMessage);
        }

        /// <inheritdoc/>
        public async Task<RepublishResponse> RepublishAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .RepublishAsync(requestHeader, subscriptionId, retransmitSequenceNumber, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use TransferSubscriptionsAsync() instead.")]
        public ResponseHeader TransferSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            out TransferResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.TransferSubscriptions(
                requestHeader,
                subscriptionIds,
                sendInitialValues,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use TransferSubscriptionsAsync() instead.")]
        public IAsyncResult BeginTransferSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginTransferSubscriptions(
                requestHeader,
                subscriptionIds,
                sendInitialValues,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use TransferSubscriptionsAsync() instead.")]
        public ResponseHeader EndTransferSubscriptions(
            IAsyncResult result,
            out TransferResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndTransferSubscriptions(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .TransferSubscriptionsAsync(requestHeader, subscriptionIds, sendInitialValues, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete("Use DeleteSubscriptionsAsync() instead.")]
        public ResponseHeader DeleteSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.DeleteSubscriptions(
                requestHeader,
                subscriptionIds,
                out results,
                out diagnosticInfos);
        }

        /// <inheritdoc/>
        [Obsolete("Use DeleteSubscriptionsAsync() instead.")]
        public IAsyncResult BeginDeleteSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            AsyncCallback callback,
            object asyncState)
        {
            return Session.BeginDeleteSubscriptions(
                requestHeader,
                subscriptionIds,
                callback,
                asyncState);
        }

        /// <inheritdoc/>
        [Obsolete("Use DeleteSubscriptionsAsync() instead.")]
        public ResponseHeader EndDeleteSubscriptions(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return Session.EndDeleteSubscriptions(result, out results, out diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            CancellationToken ct)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.DeleteSubscriptionsAsync(requestHeader, subscriptionIds, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void AttachChannel(ITransportChannel channel)
        {
            using Activity activity = m_telemetry.StartActivity();
            Session.AttachChannel(channel);
        }

        /// <inheritdoc/>
        public void DetachChannel()
        {
            using Activity activity = m_telemetry.StartActivity();
            Session.DetachChannel();
        }

        /// <inheritdoc/>
        public uint NewRequestHandle()
        {
            return Session.NewRequestHandle();
        }

        /// <summary>
        /// Disposes the session.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // note: do not null the session here,
                // properties may still be accessed after dispose.
                Utils.SilentDispose(Session);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public SessionConfiguration SaveSessionConfiguration(Stream stream = null)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.SaveSessionConfiguration(stream);
        }

        /// <inheritdoc/>
        public bool ApplySessionConfiguration(SessionConfiguration sessionConfiguration)
        {
            using Activity activity = m_telemetry.StartActivity();
            return Session.ApplySessionConfiguration(sessionConfiguration);
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionAsync(
            Subscription subscription,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.RemoveSubscriptionAsync(subscription, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionsAsync(
            IEnumerable<Subscription> subscriptions,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.RemoveSubscriptionsAsync(subscriptions, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<bool> ReactivateSubscriptionsAsync(
            SubscriptionCollection subscriptions,
            bool sendInitialValues,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session
                .ReactivateSubscriptionsAsync(subscriptions, sendInitialValues, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<bool> TransferSubscriptionsAsync(
            SubscriptionCollection subscriptions,
            bool sendInitialValues,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.TransferSubscriptionsAsync(subscriptions, sendInitialValues, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<IList<object>> CallAsync(
            NodeId objectId,
            NodeId methodId,
            CancellationToken ct = default,
            params object[] args)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.CallAsync(objectId, methodId, ct, args).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<(bool, IList<ServiceResult>)> ResendDataAsync(
            IEnumerable<Subscription> subscriptions,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.ResendDataAsync(subscriptions, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<(IList<string>, IList<ServiceResult>)> ReadDisplayNameAsync(
            IList<NodeId> nodeIds,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.ReadDisplayNameAsync(
                nodeIds,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<(NodeIdCollection, IList<ServiceResult>)> FindComponentIdsAsync(
            NodeId instanceId,
            IList<string> componentPaths,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.FindComponentIdsAsync(
                instanceId,
                componentPaths,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<ReferenceDescriptionCollection> ReadAvailableEncodingsAsync(
            NodeId variableId,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.ReadAvailableEncodingsAsync(
                variableId,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<ReferenceDescription> FindDataDescriptionAsync(
            NodeId encodingId,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.FindDataDescriptionAsync(
                encodingId,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<T> ReadValueAsync<T>(
            NodeId nodeId,
            CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.ReadValueAsync<T>(nodeId, ct).ConfigureAwait(false);
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
            using Activity activity = m_telemetry.StartActivity();
            return await Session.BrowseAsync(
                requestHeader,
                view,
                nodesToBrowse,
                maxResultsToReturn,
                browseDirection,
                referenceTypeId,
                includeSubtypes,
                nodeClassMask,
                ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<(
            ResponseHeader responseHeader,
            ByteStringCollection revisedContinuationPoints,
            IList<ReferenceDescriptionCollection> referencesList,
            IList<ServiceResult> errors)>
            BrowseNextAsync(
                RequestHeader requestHeader,
                ByteStringCollection continuationPoints,
                bool releaseContinuationPoint,
                CancellationToken ct = default)
        {
            using Activity activity = m_telemetry.StartActivity();
            return await Session.BrowseNextAsync(
                requestHeader,
                continuationPoints,
                releaseContinuationPoint,
                ct)
                .ConfigureAwait(false);
        }

        private readonly ITelemetryContext m_telemetry;
    }
}
