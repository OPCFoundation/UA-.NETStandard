// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;
using Opc.Ua.Identity;
using Opc.Ua.Redaction;
using Opc.Ua.Schema;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    public partial class StandardServer
    {
        /// <summary>
        /// The lifecycle of the server application: the startup that builds the server
        /// internals, managers and address space on top of the transport start of
        /// <see cref="ServerBase"/>, the cleanup of a failed startup, and the staged
        /// shutdown that retains completed stages across stop attempts.
        /// </summary>
        /// <remarks>
        /// Nested in <see cref="StandardServer"/> because the startup builds the server
        /// through the protected virtual factory methods (CreateMasterNodeManager,
        /// CreateSessionManager, OnServerStarted and so on) that derived servers override.
        /// The order of the start sequence is unchanged.
        /// </remarks>
        private sealed class ServerLifecycle
        {
            /// <summary>
            /// Creates the lifecycle of a server.
            /// </summary>
            /// <param name="server">The server whose lifecycle is run.</param>
            public ServerLifecycle(StandardServer server)
            {
                m_server = server;
            }

            /// <summary>
            /// Starts the server application after the transport-independent part of
            /// <see cref="ServerBase.StartApplicationAsync"/> completed: creates the
            /// server internals and managers, starts the address space, publishes the
            /// initial node managers and starts the subscription and session managers.
            /// A failed startup releases what it created.
            /// </summary>
            /// <param name="configuration">The configuration of the server.</param>
            /// <param name="cancellationToken">Cancels the startup.</param>
            public async ValueTask StartAsync(ApplicationConfiguration configuration, CancellationToken cancellationToken)
            {
                m_server.m_nodeManagerLifecycle.PrepareForStartup();
                await m_server.m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
                // The managers belong to the datastore once registered with it; until then a
                // failed startup must release them itself.
                ISessionManager? unregisteredSessionManager = null;
                ISubscriptionManager? unregisteredSubscriptionManager = null;
                NodeManagerLifecycle.InitialPublication? initialPublication = null;
                try
                {
                    m_server.m_nodeManagerLifecycle.BeginStartup();
                    m_server.m_logger.ServerStartApplicationApplicationName(configuration.ApplicationName);

                    // Setup the minimum nonce length
                    Nonce.SetMinNonceValue((uint)configuration.SecurityConfiguration.NonceLength);

                    // create the datastore for the instance.
                    m_server.m_serverInternal = new ServerInternalData(
                        m_server.ServerProperties!,
                        configuration,
                        m_server.MessageContext,
                        m_server.TimeProvider,
                        m_server.SecurityPolicyRegistry)
                    {
                        // the validator CreateSession checks client certificates
                        // with; ActivateSession validates an embedded user token
                        // signing certificate with it.
                        CertificateValidator = m_server.CertificateManager
                    };

                    m_server.m_serverInternal.DefaultSystemContext.LocalEventOccurrenceIdProvider =
                        m_server.EventOccurrenceIdProvider;

                    foreach (IUserTokenAuthenticator authenticator in m_server.m_preStartAuthenticators)
                    {
                        m_server.m_serverInternal.IdentityRegistry.Register(authenticator);
                    }
                    foreach (IIdentityAugmenter augmenter in m_server.m_preStartIdentityAugmenters)
                    {
                        m_server.m_serverInternal.IdentityRegistry.RegisterAugmenter(augmenter);
                    }

                    m_server.m_serverInternal.SetNodeIdFactory(m_server.NodeIdFactory);
                    m_server.m_serverInternal.SetNodeIdCollisionDetection(m_server.DetectNodeIdCollisions);
                    if (m_server.NodeIdFactory is Hosting.IServerPreStartupTask factoryInitialization)
                    {
                        await factoryInitialization.OnServerStartingAsync(
                            m_server.m_serverInternal,
                            cancellationToken).ConfigureAwait(false);
                    }

                    var historianRegistry =
                        (Historian.HistorianProviderRegistry)
                            m_server.m_serverInternal.HistorianRegistry;
                    foreach (HistorianProviderRegistration registration in
                        m_server.m_historianProviders)
                    {
                        historianRegistry.RegisterDefault(
                            registration.Provider,
                            registration.OwnsProvider);
                    }
                    foreach (Hosting.IServerPreStartupTask task in m_server.m_preStartupTasks)
                    {
                        await task.OnServerStartingAsync(
                            m_server.m_serverInternal,
                            cancellationToken).ConfigureAwait(false);
                    }

                    m_server.m_serverInternal.SetRoleManager(m_server.CreateRoleManager(m_server.m_serverInternal, configuration));

                    if (m_server.CreateUserManagement(m_server.m_serverInternal, configuration) is { } userManagement)
                    {
                        m_server.m_serverInternal.SetUserManagement(userManagement);
                    }

                    if (m_server.m_serverInternal.Factory is not EncodeableFactory)
                    {
                        throw new NotSupportedException("The server factory does not support prepared publication.");
                    }

                    // create the manager responsible for providing localized string resources.
                    m_server.m_logger.ServerCreateResourceManager();
                    ResourceManager resourceManager = m_server.CreateResourceManager(
                        m_server.m_serverInternal,
                        configuration);

                    // create the manager responsible for incoming requests.
                    m_server.m_logger.ServerCreateRequestManager();
                    RequestManager requestManager = m_server.CreateRequestManager(
                        m_server.m_serverInternal,
                        configuration);

                    //create the main node manager factory
                    IMainNodeManagerFactory mainNodeManagerFactory = m_server.CreateMainNodeManagerFactory(m_server.m_serverInternal, configuration);

                    m_server.m_serverInternal.SetMainNodeManagerFactory(mainNodeManagerFactory);

                    // create the master node manager.
                    m_server.m_logger.ServerCreateMasterNodeManager();
                    IMasterNodeManager masterNodeManager = m_server.CreateMasterNodeManager(
                        m_server.m_serverInternal,
                        configuration);

                    // add the node manager to the datastore.
                    m_server.m_serverInternal.SetNodeManager(masterNodeManager);
                    if (masterNodeManager is IDynamicNodeManagerHost startupHost)
                    {
                        m_server.m_nodeManagerLifecycle.InitializeSystemSlots(m_server.m_serverInternal, startupHost);
                        startupHost.SetLifetimeOwner(
                            m_server.StopBaseAsync,
                            () => !m_server.m_nodeManagerLifecycle.HasPendingShutdownCleanup);
                        startupHost.ValidateStartupFactories();
                    }

                    // put the node manager into a state that allows it to be used by other objects.
                    await masterNodeManager.StartupAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (masterNodeManager is IDynamicNodeManagerHost initialHost)
                    {
                        initialPublication = await m_server.m_nodeManagerLifecycle
                            .BeginInitialPublicationAsync(
                                m_server.m_serverInternal, initialHost.InitialFactories, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    // create the manager responsible for handling events.
                    m_server.m_logger.ServerCreateEventManager();
                    EventManager eventManager = m_server.CreateEventManager(m_server.m_serverInternal, configuration);

                    // creates the server object.
                    await m_server.m_serverInternal.CreateServerObjectAsync(
                        eventManager,
                        resourceManager,
                        requestManager,
                        cancellationToken).ConfigureAwait(false);

                    // do any additional processing now that the node manager is up and running.
                    m_server.OnNodeManagerStarted(m_server.m_serverInternal);

                    // run asynchronous post-address-space initialization (e.g. runtime
                    // complex-type loading) before the server begins accepting connections.
                    await m_server.OnNodeManagerStartedAsync(m_server.m_serverInternal, cancellationToken)
                        .ConfigureAwait(false);

                    // create the manager responsible for aggregates.
                    m_server.m_logger.ServerCreateAggregateManager();
                    AggregateManager aggregateManager = await m_server.CreateAggregateManagerAsync(
                        m_server.m_serverInternal,
                        configuration,
                        cancellationToken).ConfigureAwait(false);
                    if (configuration.ServerConfiguration is { } serverConfiguration)
                    {
                        aggregateManager.MaxMonitoredItemRawValues =
                            serverConfiguration.MaxMonitoredItemAggregateRawValues;
                        aggregateManager.MaxProcessedHistoryRawValues =
                            serverConfiguration.MaxProcessedHistoryRawValues;
                    }
                    m_server.m_serverInternal.SetAggregateManager(aggregateManager);

                    // create the manager responsible for modelling rules.
                    m_server.m_logger.ServerCreateModellingRulesManager();
                    m_server.m_serverInternal.SetModellingRulesManager(
                        await m_server.CreateModellingRulesManagerAsync(m_server.m_serverInternal, configuration, cancellationToken).ConfigureAwait(false));

                    // create the manager responsible for conformance units / server profiles.
                    m_server.m_logger.ServerCreateConformanceUnitsManager();
                    m_server.m_serverInternal.SetConformanceUnitsManager(
                        await m_server.CreateConformanceUnitsManagerAsync(m_server.m_serverInternal, configuration, cancellationToken).ConfigureAwait(false));

                    // describe every namespace the server exposes with a
                    // NamespaceMetadata Object (OPC 10000-5) so clients can
                    // version-check the models they cache.
                    await m_server.PublishNamespaceMetadataAsync(m_server.m_serverInternal, cancellationToken)
                        .ConfigureAwait(false);

                    // start the session manager.
                    m_server.m_logger.ServerCreateSessionManager();
                    ISessionManager sessionManager = m_server.CreateSessionManager(
                        m_server.m_serverInternal,
                        configuration);
                    unregisteredSessionManager = sessionManager;
                    await sessionManager.StartupAsync(cancellationToken)
                        .ConfigureAwait(false);

                    // use event to trigger channel that should not be closed.
                    sessionManager.SessionChannelKeepAlive += m_server.SessionChannelKeepAliveEvent;

                    //create the MonitoredItemQueueFactory
                    IMonitoredItemQueueFactory? monitoredItemQueueFactory
                        = m_server.CreateMonitoredItemQueueFactory(
                        m_server.m_serverInternal,
                        configuration);

                    //add the MonitoredItemQueueFactory to the datastore; a factory the server
                    //does not own (e.g. supplied by the caller) survives restarts.
                    m_server.m_serverInternal.SetMonitoredItemQueueFactory(
                        monitoredItemQueueFactory!,
                        ownsFactory: monitoredItemQueueFactory != null &&
                            m_server.OwnsMonitoredItemQueueFactory(monitoredItemQueueFactory));

                    //create the SubscriptionStore
                    ISubscriptionStore? subscriptionStore = m_server.CreateSubscriptionStore(
                        m_server.m_serverInternal,
                        configuration);

                    //add the SubscriptionStore to the datastore
                    m_server.m_serverInternal.SetSubscriptionStore(subscriptionStore!);

                    IHistoryContinuationPointStore? historyContinuationPointStore =
                        m_server.HistoryContinuationPointStore ??
                        subscriptionStore as IHistoryContinuationPointStore;
                    if (historyContinuationPointStore != null)
                    {
                        m_server.m_serverInternal.SetHistoryContinuationPointStore(
                            historyContinuationPointStore);
                    }

                    // start the subscription manager.
                    m_server.m_logger.ServerCreateSubscriptionManager();
                    ISubscriptionManager subscriptionManager = m_server.CreateSubscriptionManager(
                        m_server.m_serverInternal,
                        configuration);
                    unregisteredSubscriptionManager = subscriptionManager;
                    await subscriptionManager.StartupAsync(cancellationToken)
                        .ConfigureAwait(false);

                    // add the session manager to the datastore.
                    m_server.m_serverInternal.SetSessionManager(sessionManager, subscriptionManager);
                    unregisteredSessionManager = null;
                    unregisteredSubscriptionManager = null;

                    // every subsystem is bound; refuse any further binding so nothing can
                    // rewire a running server.
                    m_server.m_serverInternal.CompleteBindPhase();

                    m_server.ServerError = null!;

                    // setup registration information.
                    await m_server.m_discoveryRegistration
                        .ConfigureAsync(
                            configuration,
                            m_server.ServerDescription!,
                            m_server.BaseAddresses.Select(address => address.DiscoveryUrl).ToList())
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException cancellation)
                    when (cancellationToken.IsCancellationRequested)
                {
                    Exception? publicationFailure = await CleanupFailedStartupAsync(
                        initialPublication,
                        unregisteredSessionManager,
                        unregisteredSubscriptionManager).ConfigureAwait(false);
                    if (publicationFailure is not null)
                    {
                        throw new AggregateException(
                            "Startup cancellation and initial-publication cleanup failed.", cancellation, publicationFailure);
                    }
                    throw;
                }
                catch (Exception e)
                {
                    const string message = "Unexpected error starting application";
                    m_server.m_logger.Message(e, message);
                    Exception? publicationFailure = await CleanupFailedStartupAsync(
                        initialPublication,
                        unregisteredSessionManager,
                        unregisteredSubscriptionManager).ConfigureAwait(false);
                    Exception failure = publicationFailure is null
                        ? e
                        : new AggregateException("Startup and initial-publication cleanup failed.", e, publicationFailure);
                    var error = new ServiceResultException(
                        failure is ServiceResultException serviceFailure
                            ? serviceFailure.StatusCode
                            : StatusCodes.BadInternalError,
                        message,
                        failure);
                    m_server.ServerError = error.Result;
                    throw error;
                }
                finally
                {
                    m_server.m_semaphoreSlim.Release();
                }

                try
                {
                    if (initialPublication is not null)
                    {
                        await initialPublication.CompleteAsync().ConfigureAwait(false);
                    }
                    // later URI changes each raise UrisVersion, also while requests are halted.
                    if (m_server.m_serverInternal is { } startedServer)
                    {
                        ServerUrisVersion.MarkStarted(startedServer);
                    }
                    // Application setup servers enter NoConfiguration (OPC 10000-12 G.2).
                    m_server.SetServerState(m_server.StartupServerState);
                    m_server.m_discoveryRegistration.StartAfterReadiness();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    m_server.m_logger.Message(ex, "A NodeManager failed during startup readiness.");
                    Exception? cleanupFailure = initialPublication is null
                        ? null
                        : await initialPublication.AbortAsync().ConfigureAwait(false);
                    if (ReferenceEquals(cleanupFailure, ex))
                    {
                        cleanupFailure = null;
                    }
                    try
                    {
                        await m_server.StopAsync(CancellationToken.None).ConfigureAwait(false);
                        if (ServiceResult.IsBad(m_server.ServerError))
                        {
                            Exception failure = new ServiceResultException(m_server.ServerError);
                            cleanupFailure = cleanupFailure is null
                                ? failure
                                : new AggregateException(cleanupFailure, failure);
                        }
                    }
                    catch (Exception cleanupException) when (cleanupException is not OutOfMemoryException)
                    {
                        cleanupFailure = cleanupFailure is null
                            ? cleanupException
                            : new AggregateException(cleanupFailure, cleanupException);
                    }
                    if (cleanupFailure is not null)
                    {
                        var failure = new AggregateException(
                            "NodeManager startup readiness and server cleanup failed.", ex, cleanupFailure);
                        m_server.ServerError = new ServiceResult(failure);
                        throw failure;
                    }
                    m_server.ServerError = new ServiceResult(ex);
                    throw;
                }

                // all initialization is complete.
                m_server.m_logger.ServerStarted();

                // Surface the live transport listener registry through the
                // ITransportListenerRegistryProvider so consumers such as
                // ConfigurationNodeManager can drive post-response channel
                // cuts after ApplyChanges (OPC UA Part 12 §7.10.9).
                m_server.m_serverInternal.SetTransportListenerRegistry(m_server.TransportListeners.AsReadOnly());

                // Surface the advertised endpoints through the
                // IServerEndpointRegistryProvider so ConfigurationNodeManager can
                // reject deleting a certificate still referenced by an
                // EndpointDescription (OPC UA Part 12 §7.10.7).
                m_server.m_serverInternal.SetServerEndpoints(m_server.GetEndpoints());

                // Endpoint-dependent contributors cannot advertise security profiles during the earlier address-space pass.
                await m_server.m_serverInternal.ConformanceUnitsManager.PublishAsync(cancellationToken).ConfigureAwait(false);

                m_server.OnServerStarted(m_server.m_serverInternal);

                // OPC 10000-12 §7.8.3: start periodic certificate-expiration and
                // TrustList-staleness alarm monitoring now that the server is
                // running and the subscription/event infrastructure is ready, so
                // alarm transition events can be reported safely.
                m_server.m_serverInternal.ConfigurationNodeManager?
                    .StartAlarmMonitoring(kCertificateAlarmMonitoringInterval);

                m_server.m_configurationReload.Start(configuration);
            }

            /// <summary>
            /// Releases the session and subscription managers that a failed startup
            /// created but did not register with the datastore. The session manager
            /// is shut down so its monitor stops; the subscription manager is only
            /// disposed, so nothing is stored.
            /// </summary>
            private async ValueTask DisposeUnregisteredManagersAsync(
                ISessionManager? sessionManager,
                ISubscriptionManager? subscriptionManager,
                List<Exception> failures)
            {
                if (sessionManager is not null)
                {
                    try
                    {
                        await sessionManager.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OutOfMemoryException)
                    {
                        m_server.m_logger.ReleasingManagerOfFailedStartupFailed(e);
                        failures.Add(e);
                    }
                    try
                    {
                        sessionManager.Dispose();
                    }
                    catch (Exception e) when (e is not OutOfMemoryException)
                    {
                        m_server.m_logger.ReleasingManagerOfFailedStartupFailed(e);
                        failures.Add(e);
                    }
                }

                try
                {
                    if (subscriptionManager is IAsyncDisposable asyncSubscriptionManager)
                    {
                        await asyncSubscriptionManager.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        subscriptionManager?.Dispose();
                    }
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    m_server.m_logger.ReleasingManagerOfFailedStartupFailed(e);
                    failures.Add(e);
                }
            }

            /// <summary>
            /// Joins the initial commit before reclaiming a partially initialized address space.
            /// </summary>
            /// <returns>The cleanup failures to surface together with the original startup failure.</returns>
            private async ValueTask<Exception?> CleanupFailedStartupAsync(
                NodeManagerLifecycle.InitialPublication? initialPublication,
                ISessionManager? sessionManager,
                ISubscriptionManager? subscriptionManager)
            {
                var failures = new List<Exception>();
                if (initialPublication is not null &&
                    await initialPublication.AbortAsync().ConfigureAwait(false) is { } publicationFailure)
                {
                    failures.Add(publicationFailure);
                }
                await DisposeUnregisteredManagersAsync(sessionManager, subscriptionManager, failures).ConfigureAwait(false);
                ServerInternalData? serverInternal = m_server.m_serverInternal;
                if (serverInternal is not null)
                {
                    await RunShutdownStageAsync(failures, serverInternal.DrainRoleStateBindingAsync).ConfigureAwait(false);
                    if (serverInternal.NodeManager is not null)
                    {
                        await RunShutdownStageAsync(
                            failures,
                            () => m_server.m_nodeManagerLifecycle
                                .BeginShutdownAsync(serverInternal, CancellationToken.None)).ConfigureAwait(false);
                        await CompleteNodeManagerShutdownAsync(serverInternal, failures).ConfigureAwait(false);
                    }
                    if (!m_server.m_nodeManagerLifecycle.HasPendingShutdownCleanup)
                    {
                        await RunShutdownStageAsync(failures, serverInternal.DisposeAsync).ConfigureAwait(false);
                        m_server.m_serverInternal = null;
                    }
                }
                return failures.Count == 0
                    ? null
                    : new AggregateException("Failed startup address-space cleanup failed.", failures);
            }

            /// <summary>
            /// Stops the server application in stages: the shutdown delay, the LDS
            /// registration, the configuration watcher and certificate subscription, and
            /// the server internals. Completed stages are retained so a later stop attempt
            /// only retries the stages that failed.
            /// </summary>
            /// <param name="cancellationToken">Cancels the consumer drains.</param>
            public async ValueTask StopAsync(CancellationToken cancellationToken)
            {
                m_server.m_logger.ServerStopping();
                ServerInternalData? serverInternal = m_server.m_serverInternal;
                ShutdownProgress progress = serverInternal is null
                    ? new ShutdownProgress()
                    : m_shutdownProgress.GetValue(serverInternal, static _ => new ShutdownProgress());
                var failures = new List<Exception>();
                m_server.m_discoveryRegistration.RequestShutdown();
                await RunShutdownStageOnceAsync(
                    progress, ShutdownStage.Delay, failures, () => m_server.WaitForShutdownDelayAsync(cancellationToken))
                    .ConfigureAwait(false);
                await RunShutdownStageOnceAsync(
                    progress, ShutdownStage.Registration, failures, m_server.m_discoveryRegistration.StopAsync).ConfigureAwait(false);
                await RunShutdownStageOnceAsync(progress, ShutdownStage.ConfigurationWatcher, failures, () =>
                {
                    m_server.m_configurationReload.DisposeConfigurationWatcher();
                    return default;
                }).ConfigureAwait(false);
                await RunShutdownStageOnceAsync(progress, ShutdownStage.CertificateSubscription, failures, () =>
                {
                    m_server.m_configurationReload.DisposeCertificateManagerSubscription();
                    return default;
                }).ConfigureAwait(false);
                if ((progress.CompletedStages & ShutdownStage.Registration) != 0)
                {
                    await RunShutdownStageOnceAsync(progress, ShutdownStage.Discovery, failures,
                        () => m_server.m_discoveryRegistration.UnregisterAsync(cancellationToken)).ConfigureAwait(false);
                }

                if (serverInternal is not null)
                {
                    await TearDownServerInternalAsync(serverInternal, progress, failures, cancellationToken)
                        .ConfigureAwait(false);
                    if ((progress.CompletedStages & ShutdownStage.Internals) != 0)
                    {
                        await m_server.m_semaphoreSlim.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                        try
                        {
                            if (ReferenceEquals(m_server.m_serverInternal, serverInternal))
                            {
                                m_server.m_serverInternal = null;
                            }
                        }
                        finally
                        {
                            m_server.m_semaphoreSlim.Release();
                        }
                    }
                }
                if (failures.Count != 0)
                {
                    throw new AggregateException("One or more server shutdown stages failed.", failures);
                }
            }

            /// <summary>
            /// Stops consumers before retiring their address-space dependencies.
            /// </summary>
            /// <remarks>
            /// Request admission stops and executing requests drain before subscription and session shutdown.
            /// Failed consumer drains retain the address space; independent registration failures do not skip
            /// application cleanup. Successful stages are retained for a later stop attempt. A subsequent
            /// <see cref="ServerBase.StartAsync(ApplicationConfiguration, CancellationToken, System.Uri[])"/>
            /// re-creates the request queue and server internals.
            /// </remarks>
            /// <param name="serverInternal">The server instance being stopped.</param>
            /// <param name="progress">The completed shutdown stages for this server instance.</param>
            /// <param name="failures">The failures to report after independent stages have been attempted.</param>
            /// <param name="cancellationToken">The token used to cancel consumer drains.</param>
            private async Task TearDownServerInternalAsync(
                ServerInternalData serverInternal,
                ShutdownProgress progress,
                List<Exception> failures,
                CancellationToken cancellationToken)
            {
                await RunShutdownStageOnceAsync(
                        progress, ShutdownStage.RequestQueue, failures,
                        () => m_server.StopRequestQueueAsync(cancellationToken))
                    .ConfigureAwait(false);
                if ((progress.CompletedStages & ShutdownStage.RequestQueue) == 0)
                {
                    // An executing request may still use the address space, so do not retire any of its dependencies.
                    return;
                }
                await RunShutdownStageOnceAsync(
                        progress, ShutdownStage.RoleBinding, failures,
                        serverInternal.DrainRoleStateBindingAsync)
                    .ConfigureAwait(false);
                await RunShutdownStageOnceAsync(
                        progress, ShutdownStage.LifecyclePreparation, failures,
                        () => m_server.m_nodeManagerLifecycle.BeginShutdownAsync(serverInternal, cancellationToken))
                    .ConfigureAwait(false);

                await RunShutdownStageOnceAsync(progress, ShutdownStage.Subscriptions, failures, async () =>
                {
                    if (serverInternal.SubscriptionManager is { } subscriptionManager)
                    {
                        await subscriptionManager.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
                await RunShutdownStageOnceAsync(progress, ShutdownStage.Sessions, failures, async () =>
                {
                    if (serverInternal.SessionManager is { } sessionManager)
                    {
                        sessionManager.SessionChannelKeepAlive -= m_server.SessionChannelKeepAliveEvent;
                        await sessionManager.ShutdownAsync(cancellationToken).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);

                const ShutdownStage consumers = ShutdownStage.RequestQueue | ShutdownStage.RoleBinding |
                    ShutdownStage.LifecyclePreparation | ShutdownStage.Subscriptions | ShutdownStage.Sessions |
                    ShutdownStage.ConfigurationWatcher | ShutdownStage.CertificateSubscription;
                if ((progress.CompletedStages & consumers) != consumers)
                {
                    // Application registrations do not depend on session or subscription teardown; system managers,
                    // the core and sessions stay in place until the retry completes the consumer stages.
                    if ((progress.CompletedStages & ShutdownStage.LifecyclePreparation) != 0)
                    {
                        await RunShutdownStageOnceAsync(
                            progress, ShutdownStage.ApplicationManagers, failures,
                            () => m_server.m_nodeManagerLifecycle.CompleteShutdownAsync(
                                serverInternal, retireSystems: false, CancellationToken.None))
                            .ConfigureAwait(false);
                    }
                }
                else
                {
                    if ((progress.CompletedStages & ShutdownStage.NodeManagers) == 0)
                    {
                        int previousFailures = failures.Count;
                        await CompleteNodeManagerShutdownAsync(serverInternal, failures).ConfigureAwait(false);
                        if (failures.Count == previousFailures && !m_server.m_nodeManagerLifecycle.HasPendingShutdownCleanup)
                        {
                            progress.CompletedStages |= ShutdownStage.NodeManagers;
                        }
                    }
                    const ShutdownStage internals = ShutdownStage.NodeManagers | ShutdownStage.Registration |
                        ShutdownStage.Discovery;
                    if ((progress.CompletedStages & internals) == internals)
                    {
                        await RunShutdownStageOnceAsync(
                            progress, ShutdownStage.Internals, failures, serverInternal.DisposeAsync).ConfigureAwait(false);
                    }
                }
            }

            /// <summary>
            /// Retries unfinished shutdown stages without replaying successful cleanup.
            /// </summary>
            /// <param name="progress">The shutdown stages completed for the current server internals.</param>
            /// <param name="stage">The stage to run if it has not completed.</param>
            /// <param name="failures">The failures to report after independent stages have been attempted.</param>
            /// <param name="shutdown">The cleanup operation.</param>
            /// <returns>A task completed after the stage succeeds or its failure is recorded.</returns>
            private static async Task RunShutdownStageOnceAsync(
                ShutdownProgress progress,
                ShutdownStage stage,
                List<Exception> failures,
                Func<ValueTask> shutdown)
            {
                if ((progress.CompletedStages & stage) != 0)
                {
                    return;
                }
                int previousFailures = failures.Count;
                await RunShutdownStageAsync(failures, shutdown).ConfigureAwait(false);
                if (failures.Count == previousFailures)
                {
                    progress.CompletedStages |= stage;
                }
            }

            /// <summary>
            /// Records successful shutdown work for one server instance.
            /// </summary>
            private sealed class ShutdownProgress
            {
                /// <summary>
                /// Gets or sets the stages that completed without failure.
                /// </summary>
                internal ShutdownStage CompletedStages { get; set; }
            }

            /// <summary>
            /// Identifies shutdown stages whose successful completion is retained for retries.
            /// </summary>
            [Flags]
            private enum ShutdownStage
            {
                /// <summary>No shutdown stage has completed.</summary>
                None = 0,

                /// <summary>The connected-client shutdown delay.</summary>
                Delay = 1,

                /// <summary>The discovery registration worker.</summary>
                Registration = 2,

                /// <summary>The configuration watcher.</summary>
                ConfigurationWatcher = 4,

                /// <summary>The certificate change subscription.</summary>
                CertificateSubscription = 8,

                /// <summary>The discovery-server unregistration.</summary>
                Discovery = 16,

                /// <summary>The request queue drain.</summary>
                RequestQueue = 32,

                /// <summary>The role-state binding drain.</summary>
                RoleBinding = 64,

                /// <summary>The accepted NodeManager work drain and shutdown preparation.</summary>
                LifecyclePreparation = 128,

                /// <summary>The subscription workers and subscription cleanup.</summary>
                Subscriptions = 256,

                /// <summary>The session worker and remaining session cleanup.</summary>
                Sessions = 512,

                /// <summary>The application and system NodeManager cleanup.</summary>
                NodeManagers = 1024,

                /// <summary>The server internals disposal.</summary>
                Internals = 2048,

                /// <summary>The application-only NodeManager cleanup performed while consumers are still failing.</summary>
                ApplicationManagers = 4096
            }

            /// <summary>
            /// Reclaims the address space through the same ownership path during shutdown and failed startup.
            /// </summary>
            private async ValueTask CompleteNodeManagerShutdownAsync(
                ServerInternalData serverInternal,
                List<Exception> failures)
            {
                await RunShutdownStageAsync(
                    failures,
                    () => m_server.m_nodeManagerLifecycle
                        .CompleteShutdownAsync(serverInternal, retireSystems: true, CancellationToken.None))
                    .ConfigureAwait(false);
                if (!m_server.m_nodeManagerLifecycle.HasPendingShutdownCleanup &&
                    serverInternal.NodeManager is { } master &&
                    master is not IDynamicNodeManagerHost)
                {
                    await RunShutdownStageAsync(
                        failures,
                        () => master.ShutdownAsync(CancellationToken.None)).ConfigureAwait(false);
                }
            }

            /// <summary>Retains successful shutdown stages without extending a stopped server's lifetime.</summary>
            private readonly ConditionalWeakTable<ServerInternalData, ShutdownProgress> m_shutdownProgress = new();

            private readonly StandardServer m_server;
        }
    }
}
