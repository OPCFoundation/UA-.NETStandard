/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
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
            m_target = target ?? throw new ArgumentNullException(nameof(target));
            if (authorization == null)
            {
                throw new ArgumentNullException(nameof(authorization));
            }
            m_operations = authorization as IApplicationConfigurationPushOperations;
            m_hasConfigurationFile = configurationFileProvider != null;
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
            m_registry = s_registries.GetValue(server, static _ => new BindingRegistry());
            m_registry.Reserve(configuration, managementConfiguration, pendingKeys, configurationFileProvider,
                server.ConfigurationNodeManager as ConfigurationNodeManager);
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
                m_manager.BindApplicationConfiguration(context, node, target, this);
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
                    ReleaseRegistration();
                }
                throw;
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
            if (!m_hasConfigurationFile)
            {
                return;
            }
            await RunAsync(async () =>
            {
                await m_manager.RecoverConfigurationFileUpdateAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Cancels the closing session's transaction and abandons its open trust-list handles.
        /// </summary>
        public async ValueTask SessionClosingAsync(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions,
            CancellationToken cancellationToken = default)
        {
            if (!TryEnterCall())
            {
                return;
            }
            try
            {
                await m_manager.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                ExitCall();
            }
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
        internal async ValueTask<T> RunAsync<T>(
            Func<ValueTask<T>> action,
            CancellationToken cancellationToken = default)
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

        /// <summary>
        /// Admits dispatch or session cleanup while the engine is still available.
        /// </summary>
        private bool TryEnterCall()
        {
            lock (m_lifetime)
            {
                if (m_stopping)
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
            var errors = new List<Exception>();
            try
            {
                await CompleteAsync(() => new ValueTask(m_manager.DrainPendingApplyChangesAsync()))
                    .ConfigureAwait(false);
                await CompleteAsync(((INodeManagerShutdown)m_manager).PrepareForShutdownAsync).ConfigureAwait(false);
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
            finally
            {
                ReleaseRegistration();
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
        /// Releases the target's isolation reservation after its engine can no longer write.
        /// </summary>
        private void ReleaseRegistration()
        {
            m_registry.Release(m_target.Configuration);
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
            public void Reserve(
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
                            m_configurationFiles.ContainsValue(configurationFileProvider)))
                    {
                        throw new ArgumentException(
                            "The target cannot reuse another application's configuration-file provider.",
                            nameof(configurationFileProvider));
                    }
                    foreach (KeyValuePair<ApplicationConfiguration, IPendingCertificateKeyStore> existing
                        in m_applications)
                    {
                        ValidateIsolation(configuration, existing.Key);
                        if (ReferenceEquals(pendingKeyStore, existing.Value))
                        {
                            throw new ArgumentException("The target cannot reuse another application's pending keys.",
                                nameof(pendingKeyStore));
                        }
                    }
                    m_applications.Add(configuration, pendingKeyStore);
                    if (configurationFileProvider != null)
                    {
                        m_configurationFiles.Add(configuration, configurationFileProvider);
                    }
                }
            }

            /// <summary>
            /// Releases an application's security reservation after its binding has drained.
            /// </summary>
            public void Release(ApplicationConfiguration configuration)
            {
                lock (m_sync)
                {
                    m_applications.Remove(configuration);
                    m_configurationFiles.Remove(configuration);
                }
            }

            /// <summary>Serializes security reservations without exposing synchronization to callers.</summary>
            private readonly Lock m_sync = new();

            /// <summary>The configurations and pending keys reserved until their bindings have drained.</summary>
            private readonly Dictionary<ApplicationConfiguration, IPendingCertificateKeyStore> m_applications = [];

            /// <summary>The externally owned configuration providers reserved by live bindings.</summary>
            private readonly Dictionary<ApplicationConfiguration, IApplicationConfigurationFileProvider>
                m_configurationFiles = [];
        }

        /// <summary>The externally owned application runtime.</summary>
        private readonly IApplicationConfigurationPushTarget m_target;

        /// <summary>The deployment admission shared by calls and deferred effects.</summary>
        private readonly IApplicationConfigurationPushOperations? m_operations;

        /// <summary>Whether this binding exposes an independently configured application file.</summary>
        private readonly bool m_hasConfigurationFile;

        /// <summary>The binding-owned push engine.</summary>
        private readonly ConfigurationNodeManager m_manager;

        /// <summary>The management server's active security reservations.</summary>
        private readonly BindingRegistry m_registry;

        /// <summary>Serializes dispatch admission and shutdown state.</summary>
        private readonly Lock m_lifetime = new();

        /// <summary>Completes once stopped dispatch has no active calls.</summary>
        private readonly TaskCompletionSource<bool> m_drained =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The count of admitted calls still using the engine.</summary>
        private int m_activeCalls;

        /// <summary>Whether shutdown has stopped new dispatch.</summary>
        private bool m_stopping;

        /// <summary>The shared asynchronous disposal completion.</summary>
        private Task? m_disposal;

        /// <summary>Associates live security reservations with their management server.</summary>
        private static readonly ConditionalWeakTable<IServerInternal, BindingRegistry> s_registries = new();
    }
}
