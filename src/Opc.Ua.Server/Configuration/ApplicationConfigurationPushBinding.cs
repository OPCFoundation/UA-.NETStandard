// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Owns an independent push engine for an unpublished ApplicationConfiguration instance.
    /// The owning asynchronous node manager forwards session closure and disposes this binding before its nodes.
    /// The target provider retains ownership of its configuration and certificate manager.
    /// </summary>
    public sealed class ApplicationConfigurationPushBinding : IAsyncDisposable
    {
        /// <summary>
        /// Binds real certificate and trust handlers, rejecting shared management or sibling application security.
        /// Authorization must independently gate target access; standard channel and SecurityAdmin checks remain.
        /// The node must have been created with a node-id factory in its owner's namespace.
        /// </summary>
        public ApplicationConfigurationPushBinding(
            IServerInternal server,
            ApplicationConfiguration managementConfiguration,
            ServerSystemContext context,
            ApplicationConfigurationState node,
            IApplicationConfigurationPushTarget target,
            IPushConfigurationAuthorization authorization,
            IPendingCertificateKeyStore? pendingKeyStore = null,
            IPushCertificateKeyGenerator? keyGenerator = null,
            IApplicationConfigurationFileProvider? configurationFileProvider = null)
            : this(server, managementConfiguration, context, node, target, authorization, pendingKeyStore,
                keyGenerator, configurationFileProvider, null, null, null, null)
        {
        }

        internal ApplicationConfigurationPushBinding(
            IServerInternal server,
            ApplicationConfiguration managementConfiguration,
            ServerSystemContext context,
            ApplicationConfigurationState node,
            IApplicationConfigurationPushTarget target,
            IPushConfigurationAuthorization authorization,
            IPendingCertificateKeyStore? pendingKeyStore,
            IPushCertificateKeyGenerator? keyGenerator,
            IApplicationConfigurationFileProvider? configurationFileProvider,
            AsyncCustomNodeManager? permissionOwner,
            Func<bool>? isServing,
            Action? enter,
            Action? exit)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (managementConfiguration == null)
            {
                throw new ArgumentNullException(nameof(managementConfiguration));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (authorization == null)
            {
                throw new ArgumentNullException(nameof(authorization));
            }
            m_operations = authorization as IApplicationConfigurationPushOperations;
            PermissionOwner = permissionOwner;
            m_isServing = isServing;
            m_enter = enter;
            m_exit = exit;
            ApplicationConfiguration configuration = target.Configuration ??
                throw new ArgumentException("The target has no configuration.", nameof(target));
            if (string.IsNullOrEmpty(configuration.ApplicationUri) ||
                !string.Equals(node.ApplicationUri?.Value, configuration.ApplicationUri, StringComparison.Ordinal) ||
                node.NodeId.IsNull || node.NodeId.NamespaceIndex == 0)
            {
                throw new ArgumentException("The node must identify the target application in an own namespace.",
                    nameof(node));
            }
            if (configuration.CertificateManager == null)
            {
                throw new ArgumentException("The target requires an initialized certificate manager.", nameof(target));
            }
            if (configuration.SecurityConfiguration.ApplicationCertificates.Count == 0)
            {
                throw new ArgumentException(
                    "The target requires configured application certificate slots.", nameof(target));
            }
            IPendingCertificateKeyStore pendingKeys = pendingKeyStore ?? new DirectoryPendingCertificateKeyStore();
            IServerInternal servingServer = server is ChangePreparer.StagingServer staging
                ? staging.ServingServer
                : server;
            BindingRegistry registry = s_registries.GetValue(servingServer, static _ => new BindingRegistry());
            TargetReservation reservation = registry.Reserve(
                configuration, managementConfiguration, pendingKeys, configurationFileProvider,
                servingServer.ConfigurationNodeManager as ConfigurationNodeManager);
            try
            {
                m_manager = new ConfigurationNodeManager(
                    server,
                    configuration,
                    server.Telemetry.CreateLogger<ConfigurationNodeManager>(),
                    timeProvider: null,
                    coordinator: null,
                    pendingKeyStore: pendingKeys,
                    keyGenerator: keyGenerator,
                    serverConfigurationOptions: new ServerConfigurationOptions
                    {
                        PushAuthorization = authorization,
                        ConfigurationFileProvider = configurationFileProvider
                    });
                m_shared = new SharedReservation(reservation);
                m_attachment = m_manager.BindApplicationConfiguration(context, node, target, this);
            }
            catch (Exception error)
            {
                try
                {
                    m_manager?.Dispose();
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException(error, cleanupError);
                }
                finally
                {
                    reservation.Dispose();
                }
                throw;
            }
        }

        internal ApplicationConfigurationPushBinding(
            ApplicationConfigurationPushBinding previous,
            ServerSystemContext context,
            ApplicationConfigurationState node,
            AsyncCustomNodeManager permissionOwner,
            Func<bool> isServing,
            Action enter,
            Action exit)
        {
            m_operations = previous.m_operations;
            PermissionOwner = permissionOwner;
            m_isServing = isServing;
            m_enter = enter;
            m_exit = exit;
            m_manager = previous.m_manager;
            m_shared = previous.m_shared;
            lock (previous.m_lifetime)
            {
                if (previous.m_stopping)
                {
                    throw new ServiceResultException(StatusCodes.BadShutdown);
                }
                m_shared.Retain();
            }
            try
            {
                m_attachment = m_manager.AttachApplicationPushNodes(context, node, this, previous.m_attachment);
            }
            catch
            {
                m_shared.Release();
                throw;
            }
        }

        internal AsyncCustomNodeManager? PermissionOwner { get; }

        internal bool IsServing => m_isServing?.Invoke() != false;

        internal ConfigurationNodeManager.TrustHandoff PauseTrustEffects()
        {
            return m_manager.PauseTrustEffects();
        }

        internal void PrepareIdleSuccessor(ApplicationConfigurationPushBinding successor)
        {
            lock (m_lifetime)
            {
                if (m_stopping || m_activeCalls != 0 || m_attachment.HasOutstandingWork)
                {
                    throw new ServiceResultException(StatusCodes.BadWouldBlock);
                }
                m_frozenForHandoff = true;
            }
            successor.m_attachment.RefreshFrom(m_attachment);
        }

        internal void ReopenAfterAbortedHandoff()
        {
            lock (m_lifetime)
            {
                m_frozenForHandoff = false;
            }
        }

        /// <summary>
        /// Waits for deferred activation and trust effects of previously committed changes.
        /// </summary>
        public Task DrainPendingApplyChangesAsync(CancellationToken cancellationToken = default)
        {
            return m_manager.DrainPendingApplyChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Recovers this application's durable configuration update before its nodes are published.
        /// </summary>
        public async ValueTask RecoverConfigurationFileUpdateAsync(CancellationToken cancellationToken = default)
        {
            if (m_attachment.File is not { } file)
            {
                return;
            }
            await RunCoreAsync(async () =>
            {
                await file.RecoverPendingUpdateAsync(m_attachment.Context, cancellationToken).ConfigureAwait(false);
                return true;
            }, enterServing: false, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Cancels the closing session's transaction and abandons its open trust-list handles.
        /// </summary>
        public ValueTask SessionClosingAsync(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions,
            CancellationToken cancellationToken = default)
        {
            if (!TryEnterCall(requireServing: true))
            {
                return default;
            }
            try
            {
                m_attachment.SessionClosing(sessionId);
            }
            finally
            {
                ExitCall();
            }
            return default;
        }

        /// <summary>
        /// Rejects new dispatch, drains all active calls and deferred effects, and releases private handler state.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            TaskCompletionSource<bool>? completion = null;
            Task disposal;
            lock (m_lifetime)
            {
                m_stopping = true;
                if (m_activeCalls == 0)
                {
                    m_drained.TrySetResult(true);
                }
                if (m_disposal == null)
                {
                    completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_disposal = completion.Task;
                }
                disposal = m_disposal;
            }
            if (completion != null)
            {
                _ = CompleteDisposalAsync(completion);
            }
            return new ValueTask(disposal);
        }

        /// <summary>
        /// Keeps the push engine alive for the entire asynchronous method call.
        /// </summary>
        /// <typeparam name="T">The method result type.</typeparam>
        internal ValueTask<T> RunAsync<T>(
            Func<ValueTask<T>> action,
            CancellationToken cancellationToken = default)
        {
            return RunCoreAsync(action, enterServing: true, cancellationToken);
        }

        private async ValueTask<T> RunCoreAsync<T>(
            Func<ValueTask<T>> action,
            bool enterServing,
            CancellationToken cancellationToken)
        {
            if (enterServing)
            {
                m_enter?.Invoke();
            }
            try
            {
                if (!TryEnterCall())
                {
                    throw new ServiceResultException(StatusCodes.BadShutdown);
                }
                try
                {
                    return m_operations == null
                        ? await action().ConfigureAwait(false)
                        : await m_operations.ExecuteAsync(action, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    ExitCall();
                }
            }
            finally
            {
                if (enterServing)
                {
                    m_exit?.Invoke();
                }
            }
        }

        /// <summary>
        /// Admits dispatch or session cleanup while the engine is still available.
        /// </summary>
        private bool TryEnterCall(bool requireServing = false)
        {
            lock (m_lifetime)
            {
                if (m_stopping || (requireServing && (m_frozenForHandoff || !IsServing)))
                {
                    return false;
                }
                m_activeCalls++;
                return true;
            }
        }

        /// <summary>
        /// Releases dispatch and completes a pending shutdown drain when the last call returns.
        /// </summary>
        private void ExitCall()
        {
            lock (m_lifetime)
            {
                m_activeCalls--;
                if (m_stopping && m_activeCalls == 0)
                {
                    m_drained.TrySetResult(true);
                }
            }
        }

        /// <summary>
        /// Publishes teardown's result to the task shared by concurrent and reentrant callers.
        /// </summary>
        private async Task CompleteDisposalAsync(TaskCompletionSource<bool> completion)
        {
            try
            {
                await DisposeCoreAsync().ConfigureAwait(false);
                completion.TrySetResult(true);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        }

        /// <summary>
        /// Releases the engine only after dispatch and deferred work have drained.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            await m_drained.Task.ConfigureAwait(false);
            bool last = false;
            var errors = new List<Exception>();
            try
            {
                if (IsServing || m_shared.IsLast)
                {
                    await CompleteAsync(() => new ValueTask(m_manager.DrainPendingApplyChangesAsync()))
                        .ConfigureAwait(false);
                }
                if (m_attachment.File is { } file)
                {
                    await CompleteAsync(file.StopAsync).ConfigureAwait(false);
                }
                try
                {
                    m_attachment.Dispose();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
                last = m_shared.Release();
                if (last)
                {
                    await CompleteAsync(((INodeManagerShutdown)m_manager).PrepareForShutdownAsync)
                        .ConfigureAwait(false);
                    await CompleteAsync(() => new ValueTask(m_manager.StopAndDrainTrustMaterialChangesAsync()))
                        .ConfigureAwait(false);
                    try
                    {
                        await m_manager.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        errors.Add(error);
                    }
                }
            }
            finally
            {
                if (last)
                {
                    m_shared.Reservation.Dispose();
                }
            }
            if (errors.Count == 1)
            {
                ExceptionDispatchInfo.Capture(errors[0]).Throw();
            }
            if (errors.Count > 1)
            {
                throw new AggregateException(errors);
            }

            async ValueTask CompleteAsync(Func<ValueTask> cleanup)
            {
                try
                {
                    await cleanup().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
        }

        /// <summary>
        /// Rejects certificate-manager, configuration, identity and store aliases before any handler is installed.
        /// </summary>
        internal static void ValidateIsolation(
            ApplicationConfiguration application,
            ApplicationConfiguration other)
        {
            if (ReferenceEquals(application, other) ||
                ReferenceEquals(application.SecurityConfiguration, other.SecurityConfiguration) ||
                ReferenceEquals(application.CertificateManager, other.CertificateManager) ||
                string.Equals(application.ApplicationUri, other.ApplicationUri, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Managed applications require independent identities and certificate managers.");
            }
            HashSet<string> otherStores = GetStores(other.SecurityConfiguration);
            if (otherStores.Overlaps(GetStores(application.SecurityConfiguration)))
            {
                throw new ArgumentException("Managed applications cannot share certificate or trust stores.");
            }
        }

        /// <summary>
        /// Captures the configured store locations, including pending-key and rejected-certificate backing stores.
        /// Custom providers must expose distinct store paths for independently managed applications.
        /// </summary>
        private static HashSet<string> GetStores(SecurityConfiguration security)
        {
            var stores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CertificateIdentifier certificate in security.ApplicationCertificates)
            {
                if (string.IsNullOrEmpty(certificate.StorePath))
                {
                    throw new ArgumentException(
                        "Managed application certificate slots require an explicit store path.");
                }
                AddStore(new CertificateStoreIdentifier(certificate.StorePath!, certificate.StoreType!));
            }
            AddStore(security.TrustedPeerCertificates);
            AddStore(security.TrustedIssuerCertificates);
            AddStore(security.TrustedUserCertificates);
            AddStore(security.UserIssuerCertificates);
            AddStore(security.TrustedHttpsCertificates);
            AddStore(security.HttpsIssuerCertificates);
            AddStore(security.RejectedCertificateStore);
            return stores;

            void AddStore(CertificateStoreIdentifier? store)
            {
                if (!string.IsNullOrEmpty(store?.StorePath))
                {
                    string path = Utils.ReplaceSpecialFolderNames(store!.StorePath!)!;
                    if (store.StoreType == CertificateStoreType.Directory)
                    {
                        path = Path.GetFullPath(path);
                    }
                    stores.Add(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }
            }
        }

        /// <summary>
        /// Tracks live bindings on one management server to prevent sibling security aliases.
        /// </summary>
        private sealed class BindingRegistry
        {
            /// <summary>
            /// Reserves an application's security after excluding the host and all other active applications.
            /// </summary>
            public TargetReservation Reserve(
                ApplicationConfiguration configuration,
                ApplicationConfiguration managementConfiguration,
                IPendingCertificateKeyStore pendingKeyStore,
                IApplicationConfigurationFileProvider? configurationFileProvider,
                ConfigurationNodeManager? host)
            {
                lock (m_sync)
                {
                    host?.ValidateManagedApplicationIsolation(configuration);
                    ValidateIsolation(configuration, managementConfiguration);
                    if (host?.UsesPendingKeyStore(pendingKeyStore) == true)
                    {
                        throw new ArgumentException("The target cannot reuse the management server's pending keys.",
                            nameof(pendingKeyStore));
                    }
                    if (configurationFileProvider != null &&
                        (host?.UsesConfigurationFileProvider(configurationFileProvider) == true ||
                            m_applications.Values.Any(owner =>
                                EqualityComparer<IApplicationConfigurationFileProvider>.Default.Equals(
                                    owner.ConfigurationFileProvider, configurationFileProvider))))
                    {
                        throw new ArgumentException(
                            "The target cannot reuse another application's configuration-file provider.",
                            nameof(configurationFileProvider));
                    }
                    foreach (TargetReservation existing in m_applications.Values)
                    {
                        ValidateIsolation(configuration, existing.Configuration);
                        if (ReferenceEquals(pendingKeyStore, existing.PendingKeyStore))
                        {
                            throw new ArgumentException("The target cannot reuse another application's pending keys.",
                                nameof(pendingKeyStore));
                        }
                    }
                    var reservation = new TargetReservation(this, configuration, pendingKeyStore,
                        configurationFileProvider);
                    m_applications.Add(configuration, reservation);
                    return reservation;
                }
            }

            /// <summary>
            /// Releases an application's security reservation after its binding has drained.
            /// </summary>
            public void Release(TargetReservation reservation)
            {
                lock (m_sync)
                {
                    if (m_applications.TryGetValue(reservation.Configuration, out TargetReservation? owner) &&
                        ReferenceEquals(owner, reservation))
                    {
                        m_applications.Remove(reservation.Configuration);
                    }
                }
            }

            /// <summary>Serializes security reservations without exposing synchronization to callers.</summary>
            private readonly Lock m_sync = new();

            /// <summary>The configurations and pending keys reserved until their bindings have drained.</summary>
            private readonly Dictionary<ApplicationConfiguration, TargetReservation> m_applications = [];
        }

        /// <summary>
        /// Binds an isolation claim to its original host, target configuration and provider instances.
        /// </summary>
        /// <param name="Registry">The canonical host registry that owns this claim.</param>
        /// <param name="Configuration">The target configuration captured at admission.</param>
        /// <param name="PendingKeyStore">The effective pending key store.</param>
        /// <param name="ConfigurationFileProvider">The optional target file provider.</param>
        private sealed record TargetReservation(
            BindingRegistry Registry,
            ApplicationConfiguration Configuration,
            IPendingCertificateKeyStore PendingKeyStore,
            IApplicationConfigurationFileProvider? ConfigurationFileProvider) : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose()
            {
                Registry.Release(this);
            }
        }

        /// <summary>The deployment admission shared by calls and deferred effects.</summary>
        private readonly IApplicationConfigurationPushOperations? m_operations;

        /// <summary>The engine retained by all attachments to this target.</summary>
        private readonly ConfigurationNodeManager m_manager;

        private readonly SharedReservation m_shared;
        private readonly ConfigurationNodeManager.ApplicationPushAttachment m_attachment;
        private readonly Func<bool>? m_isServing;
        private readonly Action? m_enter;
        private readonly Action? m_exit;

        private sealed class SharedReservation(TargetReservation reservation)
        {
            public TargetReservation Reservation { get; } = reservation;

            public bool IsLast => Volatile.Read(ref m_count) == 1;

            public void Retain()
            {
                Interlocked.Increment(ref m_count);
            }

            public bool Release()
            {
                return Interlocked.Decrement(ref m_count) == 0;
            }

            private int m_count = 1;
        }

        /// <summary>Serializes dispatch admission and shutdown state.</summary>
        private readonly Lock m_lifetime = new();

        /// <summary>Completes once stopped dispatch has no active calls.</summary>
        private readonly TaskCompletionSource<bool> m_drained =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The count of admitted calls still using the engine.</summary>
        private int m_activeCalls;

        /// <summary>Whether shutdown has stopped new dispatch.</summary>
        private bool m_stopping;

        private bool m_frozenForHandoff;

        /// <summary>The shared asynchronous disposal completion.</summary>
        private Task? m_disposal;

        /// <summary>Associates live security reservations with their management server.</summary>
        private static readonly ConditionalWeakTable<IServerInternal, BindingRegistry> s_registries = new();
    }
}
