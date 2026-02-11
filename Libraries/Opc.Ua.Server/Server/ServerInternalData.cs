/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A class that stores the globally accessible state of a server instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a readonly class that is initialized when the server starts up. It provides
    /// access to global objects and data that different parts of the server may require.
    /// It also defines some global methods.
    /// </para>
    /// <para>
    /// This object is constructed is three steps:
    /// - the configuration is provided.
    /// - the node managers et. al. are provided.
    /// - the session/subscription managers are provided.
    /// </para>
    /// <para>The server is not running until all three steps are complete.</para>
    /// <para>
    /// The references returned from this object do not change after all three states are complete.
    /// This ensures the object is thread safe even though it does not use a lock.
    /// Objects returned from this object can be assumed to be threadsafe unless otherwise stated.
    /// </para>
    /// </remarks>
    public class ServerInternalData : IServerInternal
    {
        /// <summary>
        /// Initializes the datastore with the server configuration.
        /// </summary>
        /// <param name="serverDescription">The server description.</param>
        /// <param name="configuration">The configuration.</param>
        /// <param name="messageContext">The message context.</param>
        /// <param name="certificateValidator">The certificate validator.</param>
        /// <param name="instanceCertificateProvider">The certificate type provider.</param>
        public ServerInternalData(
            ServerProperties serverDescription,
            ApplicationConfiguration configuration,
            IServiceMessageContext messageContext,
            CertificateValidator certificateValidator,
            CertificateTypesProvider instanceCertificateProvider)
        {
            m_serverDescription = serverDescription;
            m_configuration = configuration;
            MessageContext = messageContext;
            InstanceCertificateProvider = instanceCertificateProvider;
            m_endpointAddresses = [];

            foreach (string baseAddresses in m_configuration.ServerConfiguration.BaseAddresses)
            {
                Uri url = Utils.ParseUri(baseAddresses);

                if (url != null)
                {
                    m_endpointAddresses.Add(url);
                }
            }

            NamespaceUris = MessageContext.NamespaceUris;
            Factory = MessageContext.Factory;

            ServerUris = new StringTable();
            TypeTree = new TypeTable(NamespaceUris);

            // add the server uri to the server table.
            ServerUris.Append(m_configuration.ApplicationUri);

            // create the default system context.
            DefaultSystemContext = new ServerSystemContext(this);
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Utils.SilentDispose(ResourceManager);
                Utils.SilentDispose(RequestManager);
                Utils.SilentDispose(AggregateManager);
                Utils.SilentDispose(ModellingRulesManager);
                Utils.SilentDispose(NodeManager);
                Utils.SilentDispose(SessionManager);
                Utils.SilentDispose(SubscriptionManager);
                Utils.SilentDispose(MonitoredItemQueueFactory);
            }
        }

        /// <summary>
        /// The session manager to use with the server.
        /// </summary>
        /// <value>The session manager.</value>
        public ISessionManager SessionManager { get; private set; }

        /// <summary>
        /// The subscription manager to use with the server.
        /// </summary>
        /// <value>The subscription manager.</value>
        public ISubscriptionManager SubscriptionManager { get; private set; }

        /// <summary>
        /// Stores the MasterNodeManager, the DiagnosticsNodeManager and the CoreNodeManager
        /// </summary>
        /// <param name="nodeManager">The node manager.</param>
        public void SetNodeManager(MasterNodeManager nodeManager)
        {
            NodeManager = nodeManager;
            DiagnosticsNodeManager = nodeManager.DiagnosticsNodeManager;
            ConfigurationNodeManager = nodeManager.ConfigurationNodeManager;
            CoreNodeManager = nodeManager.CoreNodeManager;
        }

        /// <summary>
        /// Stores the MainNodeManagerFactory
        /// </summary>
        /// <param name="mainNodeManagerFactory">The main node manager factory.</param>
        public void SetMainNodeManagerFactory(IMainNodeManagerFactory mainNodeManagerFactory)
        {
            MainNodeManagerFactory = mainNodeManagerFactory;
        }

        /// <summary>
        /// Sets the EventManager, the ResourceManager, the RequestManager and the AggregateManager.
        /// </summary>
        /// <param name="eventManager">The event manager.</param>
        /// <param name="resourceManager">The resource manager.</param>
        /// <param name="requestManager">The request manager.</param>
        public void CreateServerObject(
            EventManager eventManager,
            ResourceManager resourceManager,
            RequestManager requestManager)
        {
            EventManager = eventManager;
            ResourceManager = resourceManager;
            RequestManager = requestManager;

            // create the server object.
            CreateServerObject();
        }

        /// <summary>
        /// Stores the SessionManager, the SubscriptionManager in the datastore.
        /// </summary>
        /// <param name="sessionManager">The session manager.</param>
        /// <param name="subscriptionManager">The subscription manager.</param>
        public void SetSessionManager(
            ISessionManager sessionManager,
            ISubscriptionManager subscriptionManager)
        {
            SessionManager = sessionManager;
            SubscriptionManager = subscriptionManager;
        }

        /// <summary>
        /// Stores the MonitoredItemQueueFactory in the datastore.
        /// </summary>
        /// <param name="monitoredItemQueueFactory">The MonitoredItemQueueFactory.</param>
        public void SetMonitoredItemQueueFactory(
            IMonitoredItemQueueFactory monitoredItemQueueFactory)
        {
            MonitoredItemQueueFactory = monitoredItemQueueFactory;
        }

        /// <summary>
        /// Stores the Subscriptionstore in the datastore.
        /// </summary>
        /// <param name="subscriptionStore">The subscriptionstore.</param>
        public void SetSubscriptionStore(ISubscriptionStore subscriptionStore)
        {
            SubscriptionStore = subscriptionStore;
        }

        /// <summary>
        /// Stores the AggregateManager in the datastore.
        /// </summary>
        /// <param name="aggregateManager">The AggregateManager.</param>
        public void SetAggregateManager(AggregateManager aggregateManager)
        {
            AggregateManager = aggregateManager;
        }

        /// <summary>
        /// Stores the ModellingRulesManager in the datastore.
        /// </summary>
        /// <param name="modellingRulesManager">The ModellingRulesManager.</param>
        public void SetModellingRulesManager(ModellingRulesManager modellingRulesManager)
        {
            ModellingRulesManager = modellingRulesManager;
        }

        /// <summary>
        /// The endpoint addresses used by the server.
        /// </summary>
        /// <value>The endpoint addresses.</value>
        public IEnumerable<Uri> EndpointAddresses => m_endpointAddresses;

        /// <summary>
        /// The context to use when serializing/deserializing extension objects.
        /// </summary>
        /// <value>The message context.</value>
        public IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Provides access to the certificate types supported by the server.
        /// </summary>
        public CertificateTypesProvider InstanceCertificateProvider { get; }

        /// <summary>
        /// The default system context for the server.
        /// </summary>
        /// <value>The default system context.</value>
        public ServerSystemContext DefaultSystemContext { get; }

        /// <summary>
        /// The table of namespace uris known to the server.
        /// </summary>
        /// <value>The namespace URIs.</value>
        public NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// The table of remote server uris known to the server.
        /// </summary>
        /// <value>The server URIs.</value>
        public StringTable ServerUris { get; }

        /// <summary>
        /// The factory used to create encodeable objects that the server understands.
        /// </summary>
        /// <value>The factory.</value>
        public IEncodeableFactory Factory { get; }

        /// <summary>
        /// The datatypes, object types and variable types known to the server.
        /// </summary>
        /// <value>The type tree.</value>
        /// <remarks>
        /// The type tree table is a global object that all components of a server have access to.
        /// Node managers must populate this table with all types that they define.
        /// This object is thread safe.
        /// </remarks>
        public TypeTable TypeTree { get; }

        /// <summary>
        /// The master node manager for the server.
        /// </summary>
        /// <value>The node manager.</value>
        public MasterNodeManager NodeManager { get; private set; }

        /// <inheritdoc/>
        public IMainNodeManagerFactory MainNodeManagerFactory { get; private set; }

        /// <summary>
        /// The internal node manager for the servers.
        /// </summary>
        /// <value>The core node manager.</value>
        public CoreNodeManager CoreNodeManager { get; private set; }

        /// <summary>
        /// Returns the node manager that managers the server diagnostics.
        /// </summary>
        /// <value>The diagnostics node manager.</value>
        public DiagnosticsNodeManager DiagnosticsNodeManager { get; private set; }

        /// <inheritdoc/>
        public ConfigurationNodeManager ConfigurationNodeManager { get; private set; }

        /// <summary>
        /// The manager for events that all components use to queue events that occur.
        /// </summary>
        /// <value>The event manager.</value>
        public EventManager EventManager { get; private set; }

        /// <summary>
        /// A manager for localized resources that components can use to localize text.
        /// </summary>
        /// <value>The resource manager.</value>
        public ResourceManager ResourceManager { get; private set; }

        /// <summary>
        /// A manager for outstanding requests that allows components to receive notifications if the timeout or are cancelled.
        /// </summary>
        /// <value>The request manager.</value>
        public RequestManager RequestManager { get; private set; }

        /// <summary>
        /// A manager for aggregate calculators supported by the server.
        /// </summary>
        /// <value>The aggregate manager.</value>
        public AggregateManager AggregateManager { get; private set; }

        /// <summary>
        /// A manager for modelling rules supported by the server.
        /// </summary>
        /// <value>The modelling rules manager.</value>
        public ModellingRulesManager ModellingRulesManager { get; private set; }

        /// <summary>
        /// The manager for active sessions.
        /// </summary>
        /// <value>The session manager.</value>
        ISessionManager IServerInternal.SessionManager => SessionManager;

        /// <summary>
        /// The manager for active subscriptions.
        /// </summary>
        ISubscriptionManager IServerInternal.SubscriptionManager => SubscriptionManager;

        /// <summary>
        /// The factory for durable monitored item queues
        /// </summary>
        public IMonitoredItemQueueFactory MonitoredItemQueueFactory { get; private set; }

        /// <summary>
        /// The store to persist and retrieve subscriptions
        /// </summary>
        public ISubscriptionStore SubscriptionStore { get; private set; }

        /// <inheritdoc/>
        public ITelemetryContext Telemetry => MessageContext.Telemetry;

        /// <summary>
        /// Returns the status object for the server.
        /// </summary>
        /// <value>The status.</value>
        [Obsolete("No longer thread safe. To read the value use CurrentState, to write use UpdateServerStatus.")]
        public ServerStatusValue Status => NonThreadSafeStatus;

        /// <summary>
        /// Gets or sets the current state of the server.
        /// </summary>
        /// <value>The state of the current.</value>
        public ServerState CurrentState
        {
            get
            {
                lock (NonThreadSafeStatus.Lock)
                {
                    return NonThreadSafeStatus.Value.State;
                }
            }
            set
            {
                lock (NonThreadSafeStatus.Lock)
                {
                    NonThreadSafeStatus.Value.State = value;
                }
            }
        }

        /// <summary>
        /// Returns the Server object node
        /// </summary>
        /// <value>The Server object node.</value>
        public ServerObjectState ServerObject { get; private set; }

        /// <summary>
        /// Used to synchronize access to the server diagnostics.
        /// </summary>
        /// <value>The diagnostics lock.</value>
        public object DiagnosticsLock { get; } = new object();

        /// <summary>
        /// Used to synchronize write access to
        /// the server diagnostics.
        /// </summary>
        /// <value>The diagnostics lock.</value>
        public object DiagnosticsWriteLock
        {
            get
            {
                // implicitly force diagnostics update
                DiagnosticsNodeManager?.ForceDiagnosticsScan();
                return DiagnosticsLock;
            }
        }

        /// <summary>
        /// Returns the diagnostics structure for the server.
        /// </summary>
        /// <value>The server diagnostics.</value>
        public ServerDiagnosticsSummaryDataType ServerDiagnostics { get; private set; }

        /// <summary>
        /// Whether the server is currently running.
        /// </summary>
        /// <value>
        /// <c>true</c> if this instance is running; otherwise, <c>false</c>.
        /// </value>
        /// <remarks>
        /// This flag is set to false when the server shuts down. Threads running should check this flag whenever
        /// they return from a blocking operation. If it is false the thread should clean up and terminate.
        /// </remarks>
        public bool IsRunning
        {
            get
            {
                if (NonThreadSafeStatus == null)
                {
                    return false;
                }

                lock (NonThreadSafeStatus.Lock)
                {
                    if (NonThreadSafeStatus.Value.State == ServerState.Running)
                    {
                        return true;
                    }

                    if (NonThreadSafeStatus.Value.State == ServerState.Shutdown &&
                        NonThreadSafeStatus.Value.SecondsTillShutdown > 0)
                    {
                        return true;
                    }

                    return false;
                }
            }
        }

        /// <summary>
        /// Whether the server is collecting diagnostics.
        /// </summary>
        /// <value><c>true</c> if diagnostics are enabled; otherwise, <c>false</c>.</value>
        public bool DiagnosticsEnabled
        {
            get
            {
                if (DiagnosticsNodeManager == null)
                {
                    return false;
                }

                return DiagnosticsNodeManager.DiagnosticsEnabled;
            }
        }

        /// <summary>
        /// Status but non thread safe - internal so not part of public api
        /// </summary>
        internal ServerStatusValue NonThreadSafeStatus { get; private set; }

        /// <summary>
        /// Closes the specified session.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="sessionId">The session identifier.</param>
        /// <param name="deleteSubscriptions">if set to <c>true</c> subscriptions are to be deleted.</param>
        public void CloseSession(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions)
        {
            CloseSessionAsync(context, sessionId, deleteSubscriptions)
                .AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Closes the specified session.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="sessionId">The session identifier.</param>
        /// <param name="deleteSubscriptions">if set to <c>true</c> subscriptions are to be deleted.</param>
        /// <param name="cancellationToken">The cancellationToken</param>
        public async ValueTask CloseSessionAsync(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions,
            CancellationToken cancellationToken = default)
        {
            await NodeManager.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken)
                .ConfigureAwait(false);
            await SubscriptionManager.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken)
                .ConfigureAwait(false);
            SessionManager.CloseSession(sessionId);
        }

        /// <summary>
        /// Deletes the specified subscription.
        /// </summary>
        /// <param name="subscriptionId">The subscription identifier.</param>
        /// <param name="cancellationToken">The cancellation token</param>
        public async ValueTask DeleteSubscriptionAsync(uint subscriptionId, CancellationToken cancellationToken = default)
        {
            await SubscriptionManager.DeleteSubscriptionAsync(null, subscriptionId, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Called by any component to report a global event.
        /// </summary>
        /// <param name="e">The event.</param>
        public void ReportEvent(IFilterTarget e)
        {
            ReportEvent(DefaultSystemContext, e);
        }

        /// <summary>
        /// Called by any component to report a global event.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="e">The event.</param>
        public void ReportEvent(ISystemContext context, IFilterTarget e)
        {
            if ((!Auditing) && (e is AuditEventState))
            {
                // do not report auditing events if server Auditing flag is false
                return;
            }

            ServerObject?.ReportEvent(context, e);
        }

        /// <summary>
        /// Refreshes the conditions for the specified subscription.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="subscriptionId">The subscription identifier.</param>
        public void ConditionRefresh(OperationContext context, uint subscriptionId)
        {
            SubscriptionManager.ConditionRefresh(context, subscriptionId);
        }

        /// <summary>
        /// Refreshes the conditions for the specified subscription.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="subscriptionId">The subscription identifier.</param>
        /// <param name="monitoredItemId">The monitored item identifier.</param>
        public void ConditionRefresh2(
            OperationContext context,
            uint subscriptionId,
            uint monitoredItemId)
        {
            SubscriptionManager.ConditionRefresh2(context, subscriptionId, monitoredItemId);
        }

        /// <summary>
        /// Updates the server status safely.
        /// </summary>
        /// <param name="action">Action to perform on the server status object.</param>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is <c>null</c>.</exception>
        public void UpdateServerStatus(Action<ServerStatusValue> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            lock (DiagnosticsLock) // TODO: Should this not take the status lock?
            {
                action.Invoke(NonThreadSafeStatus);
            }
        }

        /// <inheritdoc/>
        public bool Auditing { get; private set; }

        /// <inheritdoc/>
        public ISystemContext DefaultAuditContext => DefaultSystemContext.Copy();

        /// <inheritdoc/>
        public void ReportAuditEvent(ISystemContext context, AuditEventState e)
        {
            if (!Auditing)
            {
                // do not report auditing events if server Auditing flag is false
                return;
            }

            ReportEvent(context, e);
        }

        /// <summary>
        /// Creates the ServerObject and attaches it to the NodeManager.
        /// </summary>
        private void CreateServerObject()
        {
            lock (DiagnosticsLock)
            {
                // get the server object.
                ServerObjectState serverObject = ServerObject =
                    DiagnosticsNodeManager.FindPredefinedNode<ServerObjectState>(ObjectIds.Server);

                // update server capabilities.
                serverObject.ServiceLevel.Value = 255;
                serverObject.ServerCapabilities.LocaleIdArray.Value = ResourceManager
                    .GetAvailableLocales();
                serverObject.ServerCapabilities.ServerProfileArray.Value =
                [
                    .. m_configuration.ServerConfiguration.ServerProfileArray
                ];
                serverObject.ServerCapabilities.MinSupportedSampleRate.Value = 0;
                serverObject.ServerCapabilities.MaxBrowseContinuationPoints.Value = (ushort)
                    m_configuration.ServerConfiguration.MaxBrowseContinuationPoints;
                serverObject.ServerCapabilities.MaxQueryContinuationPoints.Value = (ushort)
                    m_configuration.ServerConfiguration.MaxQueryContinuationPoints;
                serverObject.ServerCapabilities.MaxHistoryContinuationPoints.Value = (ushort)
                    m_configuration.ServerConfiguration.MaxHistoryContinuationPoints;
                serverObject.ServerCapabilities.MaxArrayLength.Value = (uint)
                    m_configuration.TransportQuotas.MaxArrayLength;
                serverObject.ServerCapabilities.MaxStringLength.Value = (uint)
                    m_configuration.TransportQuotas.MaxStringLength;
                serverObject.ServerCapabilities.MaxByteStringLength.Value = (uint)
                    m_configuration.TransportQuotas.MaxByteStringLength;

                // Any operational limits Property that is provided shall have a non zero value.
                OperationLimitsState operationLimits = serverObject.ServerCapabilities
                    .OperationLimits;
                OperationLimits configOperationLimits = m_configuration.ServerConfiguration
                    .OperationLimits;
                if (configOperationLimits != null)
                {
                    operationLimits.MaxNodesPerRead = SetPropertyValue(
                        operationLimits.MaxNodesPerRead,
                        configOperationLimits.MaxNodesPerRead);
                    operationLimits.MaxNodesPerHistoryReadData = SetPropertyValue(
                        operationLimits.MaxNodesPerHistoryReadData,
                        configOperationLimits.MaxNodesPerHistoryReadData);
                    operationLimits.MaxNodesPerHistoryReadEvents = SetPropertyValue(
                        operationLimits.MaxNodesPerHistoryReadEvents,
                        configOperationLimits.MaxNodesPerHistoryReadEvents);
                    operationLimits.MaxNodesPerWrite = SetPropertyValue(
                        operationLimits.MaxNodesPerWrite,
                        configOperationLimits.MaxNodesPerWrite);
                    operationLimits.MaxNodesPerHistoryUpdateData = SetPropertyValue(
                        operationLimits.MaxNodesPerHistoryUpdateData,
                        configOperationLimits.MaxNodesPerHistoryUpdateData);
                    operationLimits.MaxNodesPerHistoryUpdateEvents = SetPropertyValue(
                        operationLimits.MaxNodesPerHistoryUpdateEvents,
                        configOperationLimits.MaxNodesPerHistoryUpdateEvents);
                    operationLimits.MaxNodesPerMethodCall = SetPropertyValue(
                        operationLimits.MaxNodesPerMethodCall,
                        configOperationLimits.MaxNodesPerMethodCall);
                    operationLimits.MaxNodesPerBrowse = SetPropertyValue(
                        operationLimits.MaxNodesPerBrowse,
                        configOperationLimits.MaxNodesPerBrowse);
                    operationLimits.MaxNodesPerRegisterNodes = SetPropertyValue(
                        operationLimits.MaxNodesPerRegisterNodes,
                        configOperationLimits.MaxNodesPerRegisterNodes);
                    operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds = SetPropertyValue(
                        operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds,
                        configOperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds);
                    operationLimits.MaxNodesPerNodeManagement = SetPropertyValue(
                        operationLimits.MaxNodesPerNodeManagement,
                        configOperationLimits.MaxNodesPerNodeManagement);
                    operationLimits.MaxMonitoredItemsPerCall = SetPropertyValue(
                        operationLimits.MaxMonitoredItemsPerCall,
                        configOperationLimits.MaxMonitoredItemsPerCall);
                }
                else
                {
                    operationLimits.MaxNodesPerRead =
                        operationLimits.MaxNodesPerHistoryReadData =
                        operationLimits.MaxNodesPerHistoryReadEvents =
                        operationLimits.MaxNodesPerWrite =
                        operationLimits.MaxNodesPerHistoryUpdateData =
                        operationLimits.MaxNodesPerHistoryUpdateEvents =
                        operationLimits.MaxNodesPerMethodCall =
                        operationLimits.MaxNodesPerBrowse =
                        operationLimits.MaxNodesPerRegisterNodes =
                        operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds =
                        operationLimits.MaxNodesPerNodeManagement =
                        operationLimits.MaxMonitoredItemsPerCall =
                            null;
                }

                // setup PublishSubscribe Status State value
                const PubSubState pubSubState = PubSubState.Disabled;

                BaseVariableState default_PubSubState =
                    DiagnosticsNodeManager.FindPredefinedNode<BaseVariableState>(
                        VariableIds.PublishSubscribe_Status_State);
                default_PubSubState.Value = pubSubState;

                // setup value for SupportedTransportProfiles
                BaseVariableState default_SupportedTransportProfiles =
                    DiagnosticsNodeManager.FindPredefinedNode<BaseVariableState>(
                        VariableIds.PublishSubscribe_SupportedTransportProfiles);
                default_SupportedTransportProfiles.Value = "uadp";

                // setup callbacks for dynamic values.
                serverObject.NamespaceArray.OnSimpleReadValue = OnReadNamespaceArray;
                serverObject.NamespaceArray.MinimumSamplingInterval = 1000;

                serverObject.ServerArray.OnSimpleReadValue = OnReadServerArray;
                serverObject.ServerArray.MinimumSamplingInterval = 1000;

                // dynamic change of enabledFlag is disabled to pass CTT
                serverObject.ServerDiagnostics.EnabledFlag.AccessLevel = AccessLevels.CurrentRead;
                serverObject.ServerDiagnostics.EnabledFlag.UserAccessLevel = AccessLevels
                    .CurrentRead;
                serverObject.ServerDiagnostics.EnabledFlag.OnSimpleReadValue
                    = OnReadDiagnosticsEnabledFlag;
                serverObject.ServerDiagnostics.EnabledFlag.OnSimpleWriteValue
                    = OnWriteDiagnosticsEnabledFlag;
                serverObject.ServerDiagnostics.EnabledFlag.MinimumSamplingInterval = 1000;

                // initialize status.
                var serverStatus = new ServerStatusDataType
                {
                    StartTime = DateTime.UtcNow,
                    CurrentTime = DateTime.UtcNow,
                    State = ServerState.Shutdown
                };

                var buildInfo = new BuildInfo
                {
                    ProductName = m_serverDescription.ProductName,
                    ProductUri = m_serverDescription.ProductUri,
                    ManufacturerName = m_serverDescription.ManufacturerName,
                    SoftwareVersion = m_serverDescription.SoftwareVersion,
                    BuildNumber = m_serverDescription.BuildNumber,
                    BuildDate = m_serverDescription.BuildDate
                };
                BuildInfoVariableState buildInfoVariableState =
                    DiagnosticsNodeManager.FindPredefinedNode<BuildInfoVariableState>(
                        VariableIds.Server_ServerStatus_BuildInfo);
                var buildInfoVariable = new BuildInfoVariableValue(
                    buildInfoVariableState,
                    buildInfo,
                    null);
                serverStatus.BuildInfo = buildInfoVariable.Value;

                serverObject.ServerStatus.MinimumSamplingInterval = 1000;
                serverObject.ServerStatus.CurrentTime.MinimumSamplingInterval = 1000;

                NonThreadSafeStatus = new ServerStatusValue(
                    serverObject.ServerStatus,
                    serverStatus,
                    DiagnosticsLock)
                {
                    Timestamp = DateTime.UtcNow,
                    OnBeforeRead = OnReadServerStatus
                };

                // initialize diagnostics.
                ServerDiagnostics = new ServerDiagnosticsSummaryDataType
                {
                    ServerViewCount = 0,
                    CurrentSessionCount = 0,
                    CumulatedSessionCount = 0,
                    SecurityRejectedSessionCount = 0,
                    RejectedSessionCount = 0,
                    SessionTimeoutCount = 0,
                    SessionAbortCount = 0,
                    PublishingIntervalCount = 0,
                    CurrentSubscriptionCount = 0,
                    CumulatedSubscriptionCount = 0,
                    SecurityRejectedRequestsCount = 0,
                    RejectedRequestsCount = 0
                };

                DiagnosticsNodeManager.CreateServerDiagnostics(
                    DefaultSystemContext,
                    ServerDiagnostics,
                    OnUpdateDiagnostics);

                // set the diagnostics enabled state.
                DiagnosticsNodeManager.SetDiagnosticsEnabled(
                    DefaultSystemContext,
                    m_configuration.ServerConfiguration.DiagnosticsEnabled);

                var configurationNodeManager = DiagnosticsNodeManager as ConfigurationNodeManager;
                configurationNodeManager?.CreateServerConfiguration(
                    DefaultSystemContext,
                    m_configuration);

                // Initialize history capabilities and update Server EventNotifier accordingly
                DiagnosticsNodeManager.UpdateServerEventNotifier();

                Auditing = m_configuration.ServerConfiguration.AuditingEnabled;
                PropertyState<bool> auditing = serverObject.Auditing;
                auditing.OnSimpleWriteValue += OnWriteAuditing;
                auditing.OnSimpleReadValue += OnReadAuditing;
                auditing.Value = Auditing;
                auditing.RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_AuthenticatedUser,
                        Permissions = (uint)(PermissionType.Browse | PermissionType.Read)
                    },
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                        Permissions = (uint)(
                            PermissionType.Browse |
                            PermissionType.Write |
                            PermissionType.ReadRolePermissions |
                            PermissionType.Read)
                    }
                ];
                auditing.AccessLevel = AccessLevels.CurrentRead;
                auditing.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                auditing.MinimumSamplingInterval = 1000;
            }
        }

        /// <summary>
        /// Updates the server status before a read.
        /// </summary>
        private void OnReadServerStatus(
            ISystemContext context,
            BaseVariableValue variable,
            NodeState component)
        {
            lock (DiagnosticsLock)
            {
                DateTime now = DateTime.UtcNow;
                NonThreadSafeStatus.Timestamp = now;
                NonThreadSafeStatus.Value.CurrentTime = now;

                // update other timestamps in NodeState objects which are used to derive the source timestamp
                if (variable is ServerStatusValue serverStatusValue &&
                    serverStatusValue.Variable is ServerStatusState serverStatusState)
                {
                    serverStatusState.Timestamp = now;
                    serverStatusState.CurrentTime.Timestamp = now;
                    serverStatusState.State.Timestamp = now;
                }
            }
        }

        /// <summary>
        /// Returns a copy of the namespace array.
        /// </summary>
        private ServiceResult OnReadNamespaceArray(
            ISystemContext context,
            NodeState node,
            ref object value)
        {
            value = NamespaceUris.ToArray();
            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns a copy of the server array.
        /// </summary>
        private ServiceResult OnReadServerArray(
            ISystemContext context,
            NodeState node,
            ref object value)
        {
            value = ServerUris.ToArray();
            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns Diagnostics.EnabledFlag
        /// </summary>
        private ServiceResult OnReadDiagnosticsEnabledFlag(
            ISystemContext context,
            NodeState node,
            ref object value)
        {
            value = DiagnosticsNodeManager.DiagnosticsEnabled;
            return ServiceResult.Good;
        }

        /// <summary>
        /// Sets the Diagnostics.EnabledFlag
        /// </summary>
        private ServiceResult OnWriteDiagnosticsEnabledFlag(
            ISystemContext context,
            NodeState node,
            ref object value)
        {
            bool enabled = (bool)value;
            DiagnosticsNodeManager.SetDiagnosticsEnabled(DefaultSystemContext, enabled);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Updates the Server.Auditing flag.
        /// </summary>
        private ServiceResult OnWriteAuditing(
            ISystemContext context,
            NodeState node,
            ref object value)
        {
            Auditing = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Reads the Server.Auditing flag.
        /// </summary>
        private ServiceResult OnReadAuditing(
            ISystemContext context,
            NodeState node,
            ref object value)
        {
            value = Auditing;
            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns a copy of the current diagnostics.
        /// </summary>
        private ServiceResult OnUpdateDiagnostics(
            ISystemContext context,
            NodeState node,
            ref object value)
        {
            lock (ServerDiagnostics)
            {
                value = Utils.Clone(ServerDiagnostics);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Set the property to null if the value is zero,
        /// to the value otherwise.
        /// </summary>
        private static PropertyState<uint> SetPropertyValue(
            PropertyState<uint> property,
            uint value)
        {
            if (value != 0)
            {
                property.Value = value;
            }
            else
            {
                property = null;
            }
            return property;
        }

        private readonly ServerProperties m_serverDescription;
        private readonly ApplicationConfiguration m_configuration;
        private readonly List<Uri> m_endpointAddresses;
    }
}
