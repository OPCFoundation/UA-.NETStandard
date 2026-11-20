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
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The standard implementation of a UA server.
    /// </summary>
    /// <remarks>
    /// <see cref="IDisposable.Dispose"/> performs the same orderly shutdown as
    /// <see cref="DisposeAsync"/> and blocks until all owned resources have been
    /// released. Callers that can await should still prefer <see cref="DisposeAsync"/>
    /// so the shutdown does not block their thread.
    /// </remarks>
    public partial class StandardServer : SessionServerBase, IStandardServer, IAsyncDisposable, IServerSessionBindingProvider,
        IRequestParkingPolicySource, IServerApplicationCertificateActivator
    {
        /// <inheritdoc/>
        public StandardServer(ITelemetryContext telemetry)
            : this(telemetry, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="StandardServer"/>
        /// with an explicit <see cref="TimeProvider"/> so the server's
        /// timers and duration calculations can be mocked in tests.
        /// </summary>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="timeProvider">The time provider used for all
        /// time / duration calculations performed by the server, or
        /// <c>null</c> to use <see cref="TimeProvider.System"/>.</param>
        public StandardServer(ITelemetryContext telemetry, TimeProvider? timeProvider)
            : this(telemetry, timeProvider, null)
        {
        }

        /// <summary>
        /// Initializes the server with an optional discovery-registration worker factory.
        /// </summary>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="timeProvider">The server time provider.</param>
        /// <param name="registrationWorkerFactory">The worker factory, or null for normal registration.</param>
        internal StandardServer(
            ITelemetryContext telemetry,
            TimeProvider? timeProvider,
            Func<CancellationToken, Task>? registrationWorkerFactory)
            : this(telemetry, timeProvider, registrationWorkerFactory, null)
        {
        }

        /// <summary>
        /// Initializes the server with construction-bound registration and lifecycle writer factories.
        /// </summary>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="timeProvider">The server time provider.</param>
        /// <param name="registrationWorkerFactory">The worker factory, or null for normal registration.</param>
        /// <param name="lifecycleWriterFactory">The writer factory, or null for the server-owned default.</param>
        internal StandardServer(
            ITelemetryContext telemetry,
            TimeProvider? timeProvider,
            Func<CancellationToken, Task>? registrationWorkerFactory,
            Func<Func<bool>, LifecycleWriter>? lifecycleWriterFactory)
            : base(telemetry)
        {
            TimeProvider = timeProvider ?? TimeProvider.System;
            m_discoveryRegistration = new DiscoveryRegistration(
                this,
                () => Configuration,
                TimeProvider,
                m_logger,
                registrationWorkerFactory);
            m_admission = new SessionAdmission(this, TimeProvider);
            m_discoveryServices = new DiscoveryServices(this);
            m_configurationReload = new ConfigurationReload(this);
            m_sessionEstablishment = new SessionEstablishment(this);
            m_lifecycle = new ServerLifecycle(this);
            m_nodeManagerLifecycle = new NodeManagerLifecycle(this, TimeProvider, lifecycleWriterFactory);
            m_eventLogger = telemetry.CreateLogger(
                ServerCompatibilityEventIds.CategoryName);
        }

        /// <summary>
        /// When <c>true</c> (the default) the server builds stand-in encodeables
        /// for custom DataTypes that were loaded from a NodeSet at runtime, once
        /// the address space is available and before the server accepts
        /// connections. DataTypes already backed by a compiled type are skipped.
        /// Set to <c>false</c> to opt out.
        /// </summary>
        public bool LoadComplexTypes { get; set; } = true;

        /// <summary>
        /// Gets the provider used to add, reload, and remove NodeManagers at runtime.
        /// </summary>
        public INodeManagerLifecycle NodeManagerLifecycle => m_nodeManagerLifecycle;

        /// <summary>
        /// Gets or sets the factory that every <see cref="AsyncCustomNodeManager"/>
        /// this server hosts mints runtime NodeIds with.
        /// </summary>
        /// <remarks>
        /// Set before the server starts; leaving it <c>null</c> leaves each
        /// NodeManager on its own default. A server composed through
        /// dependency injection picks this up from the registered
        /// <see cref="IRebasableNodeIdFactory"/>.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The current factory has an identity policy and the replacement value is <c>null</c>.
        /// </exception>
        public IRebasableNodeIdFactory? NodeIdFactory
        {
            get;
            set => field = field is INodeIdFactoryPolicy policy
                ? policy.Apply(value ?? throw new ArgumentNullException(nameof(value)))
                : value;
        }

        /// <summary>
        /// Gets or sets whether this server's NodeManagers refuse to mint a
        /// NodeId they already gave a different browse path.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Set before the server starts. Leaving it <c>null</c> leaves each
        /// factory on <see cref="DefaultNodeIdFactory.DetectCollisionsByDefault"/>,
        /// which is on in a debug build and off otherwise.
        /// </para>
        /// <para>
        /// This is a server-wide decision rather than a per-NodeManager one,
        /// because the record a factory keeps to answer the question costs
        /// memory that grows with the address space.
        /// </para>
        /// </remarks>
        public bool? DetectNodeIdCollisions { get; set; }

        /// <summary>
        /// Gets the active application configuration, failing if the server has not been configured.
        /// </summary>
        /// <exception cref="InvalidOperationException">The server has not been configured.</exception>
        internal ApplicationConfiguration CurrentConfiguration
            => Configuration
                ?? throw new InvalidOperationException("The server has not been configured.");

        /// <summary>
        /// The <see cref="TimeProvider"/> used by the server for all
        /// time / duration calculations and timer scheduling.
        /// </summary>
        protected TimeProvider TimeProvider { get; }

        /// <inheritdoc/>
        public ValueTask ActivateAsync(
            NodeId certificateType,
            string expectedThumbprint,
            CancellationToken cancellationToken = default)
        {
            ActivateCertificate(certificateType, expectedThumbprint, cancellationToken);
            return default;
        }

        /// <summary>
        /// Applies the current certificate-manager entry to listeners before releasing its registry lease.
        /// </summary>
        private void ActivateCertificate(
            NodeId certificateType,
            string expectedThumbprint,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (certificateType.IsNull || string.IsNullOrWhiteSpace(expectedThumbprint))
            {
                throw new ArgumentException("A certificate type and thumbprint are required.");
            }

            if (CertificateManager is not ICertificateRegistry registry ||
                CertificateManager is not ICertificateValidatorEx validator)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The server certificate manager cannot activate application certificates.");
            }

            using CertificateEntry? entry = registry.AcquireApplicationCertificateByType(certificateType);
            if (entry == null ||
                !string.Equals(entry.Certificate.Thumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The requested application certificate is not the active certificate-manager entry.");
            }

            // Unlike the observer notification path, this explicit activation path propagates listener
            // failures to its caller. The listener contract is synchronous: a completed call has applied
            // the new registry reference and endpoint certificate before this method returns.
            RefreshCertificateReferences(validator, registry);
            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Gets or sets the optional redundant server-set provider consulted by
        /// <c>FindServers</c>.
        /// </summary>
        public IRedundantServerSetProvider? RedundantServerSetProvider { get; set; }

        /// <summary>
        /// Gets or sets the optional load-direction seam consulted by
        /// <c>GetEndpoints</c> to direct a Client to a peer Server (extension
        /// beyond OPC 10000-4 §6.6; opt-in and gated).
        /// </summary>
        public IGetEndpointsDirector? GetEndpointsDirector { get; set; }

        /// <summary>
        /// An optional subscription store used to persist subscriptions and (for high
        /// availability) restore monitored-item queues. When <c>null</c> (the default) no
        /// subscription persistence is used unless a subclass overrides
        /// <see cref="CreateSubscriptionStore"/>. Set this before the server starts (the
        /// hosting layer wires it from dependency injection).
        /// </summary>
        public ISubscriptionStore? SubscriptionStore { get; set; }

        /// <summary>
        /// Optional durable store for portable HistoryRead continuation points.
        /// </summary>
        public IHistoryContinuationPointStore? HistoryContinuationPointStore { get; set; }

        /// <summary>
        /// An optional monitored-item queue factory. When <c>null</c> (the default) the
        /// built-in <see cref="Server.MonitoredItemQueueFactory"/> is used unless a
        /// subclass overrides <see cref="CreateMonitoredItemQueueFactory"/>. Set this before
        /// the server starts (the hosting layer wires it from dependency injection) to plug in
        /// a custom factory, e.g. a shared-store mirror for high availability.
        /// </summary>
        public IMonitoredItemQueueFactory? MonitoredItemQueueFactory { get; set; }

        /// <summary>
        /// The deterministic admission-control options used when no
        /// <see cref="RateLimiterProvider"/> is supplied. Rate limiting is ON by
        /// default with conservative limits; set this before the server starts to
        /// tune or disable it without dependency injection.
        /// </summary>
        public ServerRateLimitOptions RateLimitOptions
        {
            get => m_rateLimitOptions ??= new ServerRateLimitOptions();
            set => m_rateLimitOptions = value;
        }

        /// <summary>
        /// The admission-control limiter provider. When left <c>null</c>, a
        /// <see cref="DefaultServerRateLimiterProvider"/> is built from
        /// <see cref="RateLimitOptions"/> at start. Set a custom provider (for
        /// example resolved from dependency injection) before the server starts to
        /// plug in a different rate-limiting algorithm.
        /// </summary>
        public IServerRateLimiterProvider? RateLimiterProvider
        {
            get => m_admission.RateLimiterProvider;
            set => m_admission.RateLimiterProvider = value;
        }

        /// <summary>
        /// Runtime isolation options, applied at startup unless a custom provider is supplied.
        /// Balanced is the default; SharedOnly explicitly retains shared-capacity compatibility.
        /// The configured handshake deadline is validated regardless of mode or provider.
        /// </summary>
        public ServerResourceIsolationOptions ResourceIsolationOptions { get; set; } = new();

        /// <summary>
        /// Optional trusted ingress/owner mapper. Set before startup.
        /// </summary>
        public IResourceIsolationClassifier? ResourceIsolationClassifier { get; set; }

        /// <summary>
        /// Optional complex type load options applied when
        /// <see cref="LoadComplexTypes"/> is enabled.
        /// </summary>
        internal ServerComplexTypeOptions? ComplexTypeOptions { get; set; }

        /// <summary>
        /// Optional supplementary registry composed into the schema resolver
        /// for schema-only types that have no encodeable.
        /// </summary>
        internal DataTypeDefinitionRegistry? ComplexTypeRegistry { get; set; }

        /// <summary>
        /// Optional dependency-injection resolver holder filled with the
        /// factory-backed schema resolver after complex types are loaded.
        /// </summary>
        internal ServerDataTypeDefinitionResolver? ComplexTypeResolverHolder { get; set; }

        /// <summary>
        /// Asynchronously shuts down and disposes this server.
        /// </summary>
        /// <returns>A task that completes after all owned server resources have been released.</returns>
        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Asynchronously shuts down and disposes this server. Concurrent callers await the same attempt;
        /// successful disposal is retained and failed shutdown can be retried.
        /// </summary>
        /// <returns>A task that completes after all owned server resources have been released.</returns>
        protected virtual ValueTask DisposeAsyncCore()
        {
            Task dispose;
            lock (m_shutdownStateLock)
            {
                if (m_disposeTask is null || m_disposeTask.IsFaulted || m_disposeTask.IsCanceled)
                {
                    m_disposeTask = DisposeCoreAsync();
                }
                dispose = m_disposeTask;
            }
            return new ValueTask(dispose);
        }

        /// <summary>
        /// Performs orderly shutdown before releasing registration, configuration and owned server resources.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            var failures = new List<Exception>();
            await RunShutdownStageAsync(failures, () => new ValueTask(LifecycleWaitDiagnostics.WaitAsync(
                StopAsync(CancellationToken.None).AsTask(),
                TimeProvider,
                LifecycleWaitDiagnostics.WarningThreshold,
                elapsed => m_logger.LifecycleWaitExceeded(
                    "server StopAsync during disposal",
                    elapsed,
                    "Server resources remain owned until orderly shutdown completes."))))
                .ConfigureAwait(false);

            ServerInternalData? serverInternal = m_serverInternal;
            bool internalsDisposed = serverInternal is null;

            m_discoveryRegistration.RequestShutdown();
            await RunShutdownStageAsync(failures, m_discoveryRegistration.StopAsync).ConfigureAwait(false);
            RunShutdownStage(failures, DisposeConfigurationWatcher);
            RunShutdownStage(failures, DisposeCertificateManagerSubscription);

            if (internalsDisposed)
            {
                RunShutdownStage(failures, () => m_nodeManagerLifecycle.Dispose());
                RunShutdownStage(failures, DisposeOwnedRateLimiterProvider);
                RunShutdownStage(failures, DisposeBaseResources);
                try
                {
                    m_admission.ResetOwnedResourceIsolation();
                    ResourceIsolationProvider = null;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failures.Add(ex);
                }
                RunShutdownStage(failures, m_semaphoreSlim.Dispose);
            }

            ThrowShutdownFailures(failures);
        }

        private void DisposeConfigurationWatcher()
        {
            m_configurationReload.DisposeConfigurationWatcher();
        }

        private void DisposeCertificateManagerSubscription()
        {
            m_configurationReload.DisposeCertificateManagerSubscription();
        }

        private void DisposeOwnedRateLimiterProvider()
        {
            m_admission.DisposeOwnedRateLimiterProvider();
        }

        private void DisposeBaseResources()
        {
            base.Dispose(true);
        }

        /// <summary>
        /// Synchronously disposes this server.
        /// </summary>
        /// <remarks>
        /// When <paramref name="disposing"/> is <c>true</c>, this method deliberately
        /// blocks on <see cref="DisposeAsyncCore"/>. That is the owner-approved exception
        /// to the repository's sync-over-async rule for classes that implement both
        /// synchronous and asynchronous disposal.
        /// </remarks>
        /// <param name="disposing">
        /// <c>true</c> when called from the synchronous dispose path; <c>false</c> when
        /// called by a finalizer.
        /// </param>
        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                base.Dispose(false);
                return;
            }

            DisposeAsyncCore().AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public override ValueTask<FindServersResponse> FindServersAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            string? endpointUrl,
            ArrayOf<string> localeIds,
            ArrayOf<string> serverUris,
            RequestLifetime requestLifetime)
        {
            return m_discoveryServices.FindServersAsync(
                requestHeader, endpointUrl, localeIds, serverUris, requestLifetime);
        }

        /// <inheritdoc/>
        public override ValueTask<GetEndpointsResponse> GetEndpointsAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            string? endpointUrl,
            ArrayOf<string> localeIds,
            ArrayOf<string> profileUris,
            RequestLifetime requestLifetime)
        {
            return m_discoveryServices.GetEndpointsAsync(
                requestHeader, endpointUrl, localeIds, profileUris, requestLifetime);
        }

        /// <summary>
        /// Returns the endpoints that match the base address and endpoint url.
        /// </summary>
        protected ArrayOf<EndpointDescription> GetEndpointDescriptions(
            string? endpointUrl,
            IList<BaseAddress> baseAddresses,
            ArrayOf<string> localeIds)
        {
            return m_discoveryServices.GetEndpointDescriptions(endpointUrl, baseAddresses, localeIds);
        }

        /// <inheritdoc/>
        public override void ReportAuditOpenSecureChannelEvent(
            string globalChannelId,
            EndpointDescription endpointDescription,
            OpenSecureChannelRequest request,
            Certificate clientCertificate,
            Exception exception)
        {
            ServerInternal?.ReportAuditOpenSecureChannelEvent(
                globalChannelId,
                endpointDescription,
                request,
                clientCertificate,
                exception,
                m_logger);
        }

        /// <inheritdoc/>
        public override void ReportAuditCloseSecureChannelEvent(
            string globalChannelId,
            Exception exception)
        {
            ServerInternal?.ReportAuditCloseSecureChannelEvent(globalChannelId, exception, m_logger);
        }

        /// <inheritdoc/>
        public override void ReportAuditCertificateEvent(
            Certificate clientCertificate,
            Exception exception)
        {
            ServerInternal?.ReportAuditCertificateEvent(clientCertificate, exception, m_logger);
        }

        /// <inheritdoc/>
        public override ValueTask<CreateSessionResponse> CreateSessionAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ApplicationDescription? clientDescription,
            string? serverUri,
            string? endpointUrl,
            string? sessionName,
            ByteString clientNonce,
            ByteString clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            RequestLifetime requestLifetime)
        {
            return m_sessionEstablishment.CreateSessionAsync(
                secureChannelContext,
                requestHeader,
                clientDescription,
                serverUri,
                endpointUrl,
                sessionName,
                clientNonce,
                clientCertificate,
                requestedSessionTimeout,
                maxResponseMessageSize,
                requestLifetime);
        }

        /// <summary>
        /// Creates the server signature returned during CreateSession.
        /// </summary>
        /// <param name="context">The request context.</param>
        /// <param name="instanceCertificate">The instance certificate used to sign data.</param>
        /// <param name="parsedClientCertificate">The client certificate supplied in the request.</param>
        /// <param name="clientNonce">The client nonce supplied in the request.</param>
        /// <param name="serverNonce">The server nonce generated for the response.</param>
        /// <returns>The server signature or <c>null</c> when signing is not required.</returns>
        protected virtual SignatureData? CreateSessionServerSignature(
            OperationContext context,
            Certificate instanceCertificate,
            Certificate? parsedClientCertificate,
            ByteString clientNonce,
            ByteString serverNonce)
        {
            return SessionSecurityPolicyHelper.CreateServerSignature(
                context,
                instanceCertificate,
                parsedClientCertificate,
                clientNonce,
                serverNonce,
                SecurityPolicyRegistry);
        }

        /// <summary>
        /// Process additional header values during session creation for ephemeral-key user token policies.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="additionalHeader">The additional request header.</param>
        /// <returns>An AdditionalParametersType object containing the processed parameters.</returns>
        protected virtual AdditionalParametersType? CreateSessionProcessAdditionalParameters(
            ISession session,
            ExtensionObject additionalHeader)
        {
            if (additionalHeader.TryGetValue(out AdditionalParametersType? parameters))
            {
                parameters = CreateSessionProcessAdditionalParameters(session, parameters);
            }
            return parameters;
        }

        /// <summary>
        /// Process additional parameters during session creation and set the session's user token security policy.
        /// </summary>
        /// <param name="session">The session</param>
        /// <param name="parameters">The additional parameters for the session</param>
        /// <returns>An AdditionalParametersType object containing the processed parameters</returns>
        protected virtual AdditionalParametersType CreateSessionProcessAdditionalParameters(
            ISession session,
            AdditionalParametersType parameters)
        {
            return SessionSecurityPolicyHelper.ProcessCreateSessionAdditionalParameters(
                session,
                parameters,
                m_logger,
                SecurityPolicyRegistry)!;
        }

        /// <summary>
        /// Process additional header values during session activation for ephemeral-key user token policies.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="additionalHeader">The additional request header.</param>
        /// <returns>
        /// An AdditionalParametersType object containing the processed parameters, or
        /// <c>null</c> when there is nothing to return.
        /// </returns>
        /// <remarks>
        /// Runs for every successful ActivateSession, with or without an additional
        /// header, so a client that used its EphemeralKey always receives a new one
        /// (OPC 10000-6 6.8.2). The new key was installed by the activation; if the
        /// returned parameters carry no ECDHKey it is appended to the response.
        /// </remarks>
        protected virtual AdditionalParametersType? ActivateSessionProcessAdditionalParameters(
            ISession session,
            ExtensionObject additionalHeader)
        {
            if (!additionalHeader.TryGetValue(out AdditionalParametersType? parameters))
            {
                parameters = new AdditionalParametersType();
            }
            parameters = ActivateSessionProcessAdditionalParameters(session, parameters);
            return parameters == null || parameters.Parameters.IsEmpty ? null : parameters;
        }

        /// <summary>
        /// Process additional parameters during session activation for ephemeral-key user token policies.
        /// </summary>
        /// <param name="session">The session</param>
        /// <param name="parameters">The additional parameters for the session</param>
        /// <returns>An AdditionalParametersType object containing the processed parameters</returns>
        protected virtual AdditionalParametersType ActivateSessionProcessAdditionalParameters(
            ISession session,
            AdditionalParametersType parameters)
        {
            return SessionSecurityPolicyHelper.ProcessActivateSessionAdditionalParameters(
                session,
                parameters,
                m_logger,
                SecurityPolicyRegistry);
        }

        /// <summary>
        /// Appends the EphemeralKey the activation installed for a used key when the
        /// processed parameters do not carry an ECDHKey.
        /// </summary>
        internal static AdditionalParametersType? AppendUnsentEphemeralKey(
            ISession session,
            AdditionalParametersType? parameters)
        {
            if (session is not Session serverSession)
            {
                return parameters;
            }

            // An ECDHKey entry is already the answer: a new key, or a rejected
            // ECDHPolicyUri (Bad_SecurityPolicyRejected), for which a pending
            // replacement stays with the session for a later activation.
            if (parameters != null)
            {
                foreach (KeyValuePair parameter in parameters.Parameters)
                {
                    if (parameter.Key == AdditionalParameterNames.ECDHKey)
                    {
                        return parameters;
                    }
                }
            }

            if (serverSession.TakeUnsentEphemeralKey() is not EphemeralKeyType key)
            {
                return parameters;
            }

            var entry = new KeyValuePair
            {
                Key = QualifiedName.From(AdditionalParameterNames.ECDHKey),
                Value = new ExtensionObject(key)
            };
            return new AdditionalParametersType
            {
                Parameters = parameters == null ? [entry] : [.. parameters.Parameters, entry]
            };
        }

        /// <inheritdoc/>
        public override ValueTask<ActivateSessionResponse> ActivateSessionAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            SignatureData? clientSignature,
            ArrayOf<SignedSoftwareCertificate> clientSoftwareCertificates,
            ArrayOf<string> localeIds,
            ExtensionObject userIdentityToken,
            SignatureData? userTokenSignature,
            RequestLifetime requestLifetime)
        {
            return m_sessionEstablishment.ActivateSessionAsync(
                secureChannelContext,
                requestHeader,
                clientSignature,
                clientSoftwareCertificates,
                localeIds,
                userIdentityToken,
                userTokenSignature,
                requestLifetime);
        }

        /// <summary>
        /// Returns whether the error is a security error.
        /// </summary>
        /// <param name="error">The error.</param>
        /// <returns>
        /// <c>true</c> if the error is one of the security errors, otherwise <c>false</c>.
        /// </returns>
        protected bool IsSecurityError(StatusCode error)
        {
            return
                error == StatusCodes.BadUserSignatureInvalid ||
                error == StatusCodes.BadUserAccessDenied ||
                error == StatusCodes.BadSecurityPolicyRejected ||
                error == StatusCodes.BadSecurityModeRejected ||
                error == StatusCodes.BadSecurityChecksFailed ||
                error == StatusCodes.BadSecureChannelTokenUnknown ||
                error == StatusCodes.BadSecureChannelIdInvalid ||
                error == StatusCodes.BadNoValidCertificates ||
                error == StatusCodes.BadIdentityTokenInvalid ||
                error == StatusCodes.BadIdentityTokenRejected ||
                error == StatusCodes.BadIdentityChangeNotSupported ||
                error == StatusCodes.BadCertificateUseNotAllowed ||
                error == StatusCodes.BadCertificateUriInvalid ||
                error == StatusCodes.BadCertificateUntrusted ||
                error == StatusCodes.BadCertificateTimeInvalid ||
                error == StatusCodes.BadCertificateRevoked ||
                error == StatusCodes.BadCertificateRevocationUnknown ||
                error == StatusCodes.BadCertificateIssuerUseNotAllowed ||
                error == StatusCodes.BadCertificateIssuerTimeInvalid ||
                error == StatusCodes.BadCertificateIssuerRevoked ||
                error == StatusCodes.BadCertificateIssuerRevocationUnknown ||
                error == StatusCodes.BadCertificateInvalid ||
                error == StatusCodes.BadCertificateHostNameInvalid ||
                error == StatusCodes.BadCertificatePolicyCheckFailed ||
                error == StatusCodes.BadApplicationSignatureInvalid;
        }

        /// <summary>
        /// Creates the response header.
        /// </summary>
        /// <param name="requestHeader">The object that contains description for the RequestHeader DataType.</param>
        /// <param name="exception">The exception used to create DiagnosticInfo assigned to the ServiceDiagnostics.</param>
        /// <returns>Returns a description for the ResponseHeader DataType. </returns>
        protected ResponseHeader CreateResponse(
            RequestHeader? requestHeader,
            ServiceResultException exception)
        {
            var responseHeader = new ResponseHeader
            {
                ServiceResult = exception.StatusCode,

                Timestamp = TimeProvider.GetUtcNow().UtcDateTime,
                RequestHandle = requestHeader!.RequestHandle
            };

            var stringTable = new StringTable();
            responseHeader.ServiceDiagnostics = new DiagnosticInfo(
                exception,
                (DiagnosticsMasks)requestHeader.ReturnDiagnostics,
                true,
                stringTable,
                m_logger);
            responseHeader.StringTable = stringTable.ToArray();

            return responseHeader;
        }

        /// <summary>
        /// The service-specific part of a session service: runs once the request was
        /// validated, inside the service envelope of
        /// <see cref="ExecuteServiceAsync{TRequest, TResponse}"/>, and returns the response
        /// with its response header created.
        /// </summary>
        /// <param name="server">The server (handlers are static to avoid a closure per call).</param>
        /// <param name="context">The context of the validated request.</param>
        /// <param name="requestHeader">The validated request header.</param>
        /// <param name="requestLifetime">The lifetime of the request.</param>
        /// <typeparam name="TRequest">The service parameters (a tuple for several).</typeparam>
        /// <typeparam name="TResponse">The service response.</typeparam>
        /// <param name="request">The service parameters.</param>
        private delegate ValueTask<TResponse> ServiceHandler<TRequest, TResponse>(
            StandardServer server,
            OperationContext context,
            RequestHeader requestHeader,
            RequestLifetime requestLifetime,
            TRequest request);

        /// <summary>
        /// Reports the audit event of a session service that failed with a service fault,
        /// before the fault is translated for the client.
        /// </summary>
        /// <typeparam name="TRequest">The service parameters.</typeparam>
        private delegate void ServiceFaultAudit<TRequest>(
            StandardServer server,
            OperationContext context,
            TRequest request,
            ServiceResultException fault);

        /// <summary>
        /// The service envelope shared by the session services: validates the request
        /// (which creates the operation context), runs the service, and on a service fault
        /// counts the rejected request, reports the service's fault audit (if it has one)
        /// and translates the fault for the client; the request is completed in any case.
        /// </summary>
        /// <remarks>
        /// When the service has no fault audit, the envelope drops its copy of the request
        /// parameters once the service started, so a request that waits long (a held
        /// Publish) does not keep them alive for the whole wait.
        /// </remarks>
        /// <typeparam name="TRequest">The service parameters (a tuple for several).</typeparam>
        /// <typeparam name="TResponse">The service response.</typeparam>
        private async ValueTask<TResponse> ExecuteServiceAsync<TRequest, TResponse>(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            RequestType requestType,
            RequestLifetime requestLifetime,
            TRequest request,
            ServiceHandler<TRequest, TResponse> service,
            ServiceFaultAudit<TRequest>? faultAudit = null)
        {
            using OperationContext context = await ValidateRequestAsync(
                secureChannelContext,
                requestHeader,
                requestType,
                requestLifetime).ConfigureAwait(false);

            try
            {
                ValueTask<TResponse> pending = service(
                    this,
                    context,
                    requestHeader,
                    requestLifetime,
                    request);

                if (faultAudit == null)
                {
                    request = default!;
                }

                return await pending.ConfigureAwait(false);
            }
            catch (ServiceResultException e)
            {
                CountRejectedServiceRequest(context, e.StatusCode);

                faultAudit?.Invoke(this, context, request, e);

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <inheritdoc/>
        public override ValueTask<CloseSessionResponse> CloseSessionAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            bool deleteSubscriptions,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.CloseSession,
                requestLifetime,
                deleteSubscriptions,
                static async (server, context, header, lifetime, deleteSubscriptions) =>
                {
                    ISession? session = server.ServerInternal.SessionManager
                        .GetSession(header.AuthenticationToken);

                    await server.ServerInternal.CloseSessionAsync(
                        context,
                        context.Session.Id,
                        deleteSubscriptions,
                        lifetime.CancellationToken).ConfigureAwait(false);

                    // report the audit event for close session
                    server.ServerInternal.ReportAuditCloseSessionEvent(
                        context.AuditEntryId!,
                        session!,
                        server.m_logger,
                        "Session/CloseSession");

                    var response = new CloseSessionResponse();
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return response;
                });
        }

        /// <inheritdoc/>
        public override ValueTask<CancelResponse> CancelAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            uint requestHandle,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.Cancel,
                requestLifetime,
                requestHandle,
                static (server, context, header, _, requestHandle) =>
                {
                    server.ServerInternal.RequestManager.CancelRequests(
                        context,
                        requestHandle,
                        out uint cancelCount);

                    var response = new CancelResponse
                    {
                        CancelCount = cancelCount
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return new ValueTask<CancelResponse>(response);
                });
        }

        /// <inheritdoc/>
        public override ValueTask<BrowseResponse> BrowseAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ViewDescription? view,
            uint requestedMaxReferencesPerNode,
            ArrayOf<BrowseDescription> nodesToBrowse,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.Browse,
                requestLifetime,
                (secureChannelContext, view, requestedMaxReferencesPerNode, nodesToBrowse),
                static async (server, context, header, lifetime, request) =>
                {
                    server.ValidateOperationLimits(
                        request.nodesToBrowse,
                        server.OperationLimits.MaxNodesPerBrowse);
                    context.StartResponseBudget(
                        server.GetResponseByteBudget(request.secureChannelContext));

                    (ArrayOf<BrowseResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos) =
                        await server.ServerInternal.NodeManager.BrowseAsync(
                            context,
                            request.view!,
                            request.requestedMaxReferencesPerNode,
                            request.nodesToBrowse,
                            lifetime.CancellationToken)
                        .ConfigureAwait(false);

                    var response = new BrowseResponse
                    {
                        Results = results,
                        DiagnosticInfos = diagnosticInfos
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return response;
                });
        }

        /// <summary>
        /// The response byte budget of Browse, BrowseNext, Call, Read and HistoryRead: the
        /// message size limit
        /// for a binary encoded request, whose results it bounds from below. Other encodings
        /// can be smaller than that bound (JSON numbers), so they have no budget.
        /// </summary>
        private int GetResponseByteBudget(SecureChannelContext secureChannelContext)
        {
            return secureChannelContext?.MessageEncoding == RequestEncoding.Binary
                ? MessageContext.MaxMessageSize
                : 0;
        }

        /// <inheritdoc/>
        public override ValueTask<BrowseNextResponse> BrowseNextAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            bool releaseContinuationPoints,
            ArrayOf<ByteString> continuationPoints,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.BrowseNext,
                requestLifetime,
                (secureChannelContext, releaseContinuationPoints, continuationPoints),
                static async (server, context, header, lifetime, request) =>
                {
                    server.ValidateOperationLimits(
                        request.continuationPoints,
                        server.OperationLimits.MaxNodesPerBrowse);
                    context.StartResponseBudget(
                        server.GetResponseByteBudget(request.secureChannelContext));

                    (ArrayOf<BrowseResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos) =
                        await server.ServerInternal.NodeManager.BrowseNextAsync(
                            context,
                            request.releaseContinuationPoints,
                            request.continuationPoints,
                            lifetime.CancellationToken)
                        .ConfigureAwait(false);

                    var response = new BrowseNextResponse
                    {
                        Results = results,
                        DiagnosticInfos = diagnosticInfos
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return response;
                });
        }

        /// <inheritdoc/>
        public override ValueTask<AddNodesResponse> AddNodesAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ArrayOf<AddNodesItem> nodesToAdd,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.AddNodes,
                requestLifetime,
                nodesToAdd,
                static async (server, context, header, lifetime, nodesToAdd) =>
                {
                    server.ValidateOperationLimits(
                        nodesToAdd,
                        server.OperationLimits.MaxNodesPerNodeManagement);

                    (ArrayOf<AddNodesResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos) =
                        await server.ServerInternal.NodeManager.AddNodesAsync(
                            context,
                            nodesToAdd,
                            lifetime.CancellationToken).ConfigureAwait(false);

                    server.ReportAuditAddNodes(context, nodesToAdd, AggregateStatusCode(results));

                    var response = new AddNodesResponse
                    {
                        Results = results,
                        DiagnosticInfos = diagnosticInfos
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return response;
                },
                static (server, context, nodesToAdd, e) =>
                    server.ReportAuditAddNodes(context, nodesToAdd, e.StatusCode));
        }

        private void ReportAuditAddNodes(
            OperationContext context,
            ArrayOf<AddNodesItem> nodesToAdd,
            StatusCode statusCode)
        {
            ServerInternal.ReportAuditAddNodesEvent(
                new ServerSystemContext(ServerInternal, context),
                nodesToAdd,
                "NodeManagement/AddNodes",
                statusCode,
                m_logger);
        }

        /// <inheritdoc/>
        public override ValueTask<DeleteNodesResponse> DeleteNodesAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ArrayOf<DeleteNodesItem> nodesToDelete,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.DeleteNodes,
                requestLifetime,
                nodesToDelete,
                static async (server, context, header, lifetime, nodesToDelete) =>
                {
                    server.ValidateOperationLimits(
                        nodesToDelete,
                        server.OperationLimits.MaxNodesPerNodeManagement);

                    (ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnosticInfos) =
                        await server.ServerInternal.NodeManager.DeleteNodesAsync(
                            context,
                            nodesToDelete,
                            lifetime.CancellationToken).ConfigureAwait(false);

                    server.ReportAuditDeleteNodes(context, nodesToDelete, AggregateStatusCode(results));

                    var response = new DeleteNodesResponse
                    {
                        Results = results,
                        DiagnosticInfos = diagnosticInfos
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return response;
                },
                static (server, context, nodesToDelete, e) =>
                    server.ReportAuditDeleteNodes(context, nodesToDelete, e.StatusCode));
        }

        private void ReportAuditDeleteNodes(
            OperationContext context,
            ArrayOf<DeleteNodesItem> nodesToDelete,
            StatusCode statusCode)
        {
            ServerInternal.ReportAuditDeleteNodesEvent(
                new ServerSystemContext(ServerInternal, context),
                nodesToDelete,
                "NodeManagement/DeleteNodes",
                statusCode,
                m_logger);
        }

        /// <inheritdoc/>
        public override ValueTask<AddReferencesResponse> AddReferencesAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ArrayOf<AddReferencesItem> referencesToAdd,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.AddReferences,
                requestLifetime,
                referencesToAdd,
                static async (server, context, header, lifetime, referencesToAdd) =>
                {
                    server.ValidateOperationLimits(
                        referencesToAdd,
                        server.OperationLimits.MaxNodesPerNodeManagement);

                    (ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnosticInfos) =
                        await server.ServerInternal.NodeManager.AddReferencesAsync(
                            context,
                            referencesToAdd,
                            lifetime.CancellationToken).ConfigureAwait(false);

                    server.ReportAuditAddReferences(context, referencesToAdd, AggregateStatusCode(results));

                    var response = new AddReferencesResponse
                    {
                        Results = results,
                        DiagnosticInfos = diagnosticInfos
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return response;
                },
                static (server, context, referencesToAdd, e) =>
                    server.ReportAuditAddReferences(context, referencesToAdd, e.StatusCode));
        }

        private void ReportAuditAddReferences(
            OperationContext context,
            ArrayOf<AddReferencesItem> referencesToAdd,
            StatusCode statusCode)
        {
            ServerInternal.ReportAuditAddReferencesEvent(
                new ServerSystemContext(ServerInternal, context),
                referencesToAdd,
                "NodeManagement/AddReferences",
                statusCode,
                m_logger);
        }

        /// <inheritdoc/>
        public override ValueTask<DeleteReferencesResponse> DeleteReferencesAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ArrayOf<DeleteReferencesItem> referencesToDelete,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.DeleteReferences,
                requestLifetime,
                referencesToDelete,
                static async (server, context, header, lifetime, referencesToDelete) =>
                {
                    server.ValidateOperationLimits(
                        referencesToDelete,
                        server.OperationLimits.MaxNodesPerNodeManagement);

                    (ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnosticInfos) =
                        await server.ServerInternal.NodeManager.DeleteReferencesAsync(
                            context,
                            referencesToDelete,
                            lifetime.CancellationToken).ConfigureAwait(false);

                    server.ReportAuditDeleteReferences(context, referencesToDelete, AggregateStatusCode(results));

                    var response = new DeleteReferencesResponse
                    {
                        Results = results,
                        DiagnosticInfos = diagnosticInfos
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return response;
                },
                static (server, context, referencesToDelete, e) =>
                    server.ReportAuditDeleteReferences(context, referencesToDelete, e.StatusCode));
        }

        private void ReportAuditDeleteReferences(
            OperationContext context,
            ArrayOf<DeleteReferencesItem> referencesToDelete,
            StatusCode statusCode)
        {
            ServerInternal.ReportAuditDeleteReferencesEvent(
                new ServerSystemContext(ServerInternal, context),
                referencesToDelete,
                "NodeManagement/DeleteReferences",
                statusCode,
                m_logger);
        }

        /// <summary>
        /// Aggregates per-item status codes into a single audit status:
        /// <see cref="StatusCodes.Good"/> when every item succeeded,
        /// the first bad status otherwise.
        /// </summary>
        private static StatusCode AggregateStatusCode(ArrayOf<StatusCode> results)
        {
            for (int ii = 0; ii < results.Count; ii++)
            {
                StatusCode code = results[ii];
                if (StatusCode.IsBad(code))
                {
                    return code;
                }
            }
            return StatusCodes.Good;
        }

        /// <summary>
        /// Aggregates AddNodes per-item statuses into a single audit status.
        /// </summary>
        private static StatusCode AggregateStatusCode(ArrayOf<AddNodesResult> results)
        {
            for (int ii = 0; ii < results.Count; ii++)
            {
                AddNodesResult result = results[ii];
                if (result == null)
                {
                    continue;
                }
                if (StatusCode.IsBad(result.StatusCode))
                {
                    return result.StatusCode;
                }
            }
            return StatusCodes.Good;
        }

        /// <inheritdoc/>
        public override ValueTask<RegisterNodesResponse> RegisterNodesAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToRegister,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.RegisterNodes,
                requestLifetime,
                nodesToRegister,
                static (server, context, header, _, nodesToRegister) =>
                {
                    server.ValidateOperationLimits(
                        nodesToRegister,
                        server.OperationLimits.MaxNodesPerRegisterNodes);

                    server.ServerInternal.NodeManager
                        .RegisterNodes(context, nodesToRegister, out ArrayOf<NodeId> registeredNodeIds);

                    var response = new RegisterNodesResponse
                    {
                        RegisteredNodeIds = registeredNodeIds
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return new ValueTask<RegisterNodesResponse>(response);
                });
        }

        /// <inheritdoc/>
        public override ValueTask<UnregisterNodesResponse> UnregisterNodesAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToUnregister,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.UnregisterNodes,
                requestLifetime,
                nodesToUnregister,
                static (server, context, header, _, nodesToUnregister) =>
                {
                    server.ValidateOperationLimits(
                        nodesToUnregister,
                        server.OperationLimits.MaxNodesPerRegisterNodes);

                    server.ServerInternal.NodeManager.UnregisterNodes(context, nodesToUnregister);

                    var response = new UnregisterNodesResponse();
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return new ValueTask<UnregisterNodesResponse>(response);
                });
        }

        /// <inheritdoc/>
        public override ValueTask<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ArrayOf<BrowsePath> browsePaths,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.TranslateBrowsePathsToNodeIds,
                requestLifetime,
                browsePaths,
                static async (server, context, header, lifetime, browsePaths) =>
                {
                    // The limit bounds the browsePaths array only (Part 5 6.3.11); the number of
                    // RelativePath elements is capped per operation by the dispatcher.
                    server.ValidateOperationLimits(
                        browsePaths,
                        server.OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds);

                    (ArrayOf<BrowsePathResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos) =
                        await server.ServerInternal.NodeManager.TranslateBrowsePathsToNodeIdsAsync(
                            context,
                            browsePaths,
                            lifetime.CancellationToken)
                        .ConfigureAwait(false);

                    var response = new TranslateBrowsePathsToNodeIdsResponse
                    {
                        Results = results,
                        DiagnosticInfos = diagnosticInfos
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return response;
                });
        }

        /// <inheritdoc/>
        public override ValueTask<ReadResponse> ReadAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<ReadValueId> nodesToRead,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.Read,
                requestLifetime,
                (maxAge, timestampsToReturn, nodesToRead, secureChannelContext),
                static async (server, context, header, lifetime, request) =>
                {
                    server.ValidateOperationLimits(
                        request.nodesToRead,
                        server.OperationLimits.MaxNodesPerRead);
                    context.StartResponseBudget(
                        server.GetResponseByteBudget(request.secureChannelContext));

                    (ArrayOf<DataValue> results, ArrayOf<DiagnosticInfo> diagnosticInfos) =
                        await server.ServerInternal.NodeManager.ReadAsync(
                            context,
                            request.maxAge,
                            request.timestampsToReturn,
                            request.nodesToRead,
                            lifetime.CancellationToken).ConfigureAwait(false);

                    var response = new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = diagnosticInfos
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return response;
                },
                static (server, context, _, e) =>
                    server.ServerInternal.ReportAuditEvent(context, "Read", e, server.m_logger));
        }

        /// <inheritdoc/>
        public override ValueTask<HistoryReadResponse> HistoryReadAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            ArrayOf<HistoryReadValueId> nodesToRead,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.HistoryRead,
                requestLifetime,
                (historyReadDetails, timestampsToReturn, releaseContinuationPoints, nodesToRead, secureChannelContext),
                static async (server, context, header, lifetime, request) =>
                {
                    if (request.historyReadDetails.TryGetValue(out ReadEventDetails? _))
                    {
                        server.ValidateOperationLimits(
                            request.nodesToRead,
                            server.OperationLimits.MaxNodesPerHistoryReadEvents);
                    }
                    else
                    {
                        server.ValidateOperationLimits(
                            request.nodesToRead,
                            server.OperationLimits.MaxNodesPerHistoryReadData);

                        // every node is answered with one value per requested time, whatever is stored
                        if (request.historyReadDetails.TryGetValue(out ReadAtTimeDetails? atTime) && atTime != null &&
                            (long)atTime.ReqTimes.Count * request.nodesToRead.Count >
                                HistorianDispatcher.kMaxAtTimeRequestedValues)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadTooManyOperations,
                                "ReadAtTime asks for {0} requested times on {1} nodes; at most {2} values per request.",
                                atTime.ReqTimes.Count,
                                request.nodesToRead.Count,
                                HistorianDispatcher.kMaxAtTimeRequestedValues);
                        }
                    }
                    int responseBudget = ResponseSizeEstimator.GetHistoryReadResponseBudget(
                        server.GetResponseByteBudget(request.secureChannelContext),
                        request.nodesToRead.Count,
                        (context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0);
                    if (responseBudget < 0)
                    {
                        context.StartExhaustedResponseBudget();
                    }
                    else
                    {
                        context.StartResponseBudget(responseBudget);
                    }

                    (ArrayOf<HistoryReadResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos) =
                        await server.ServerInternal.NodeManager.HistoryReadAsync(
                            context,
                            request.historyReadDetails,
                            request.timestampsToReturn,
                            request.releaseContinuationPoints,
                            request.nodesToRead,
                            lifetime.CancellationToken)
                        .ConfigureAwait(false);

                    var response = new HistoryReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = diagnosticInfos
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return response;
                },
                static (server, context, _, e) =>
                    server.ServerInternal.ReportAuditEvent(context, "HistoryRead", e, server.m_logger));
        }

        /// <inheritdoc/>
        public override ValueTask<WriteResponse> WriteAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ArrayOf<WriteValue> nodesToWrite,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.Write,
                requestLifetime,
                nodesToWrite,
                static async (server, context, header, lifetime, nodesToWrite) =>
                {
                    server.ValidateOperationLimits(
                        nodesToWrite,
                        server.OperationLimits.MaxNodesPerWrite);

                    (ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnosticInfos) =
                        await server.ServerInternal.NodeManager
                            .WriteAsync(context, nodesToWrite, lifetime.CancellationToken)
                            .ConfigureAwait(false);

                    var response = new WriteResponse
                    {
                        Results = results,
                        DiagnosticInfos = diagnosticInfos
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return response;
                },
                static (server, context, _, e) =>
                    server.ServerInternal.ReportAuditEvent(context, "Write", e, server.m_logger));
        }

        /// <inheritdoc/>
        public override ValueTask<HistoryUpdateResponse> HistoryUpdateAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ArrayOf<ExtensionObject> historyUpdateDetails,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.HistoryUpdate,
                requestLifetime,
                historyUpdateDetails,
                static async (server, context, header, lifetime, historyUpdateDetails) =>
                {
                    server.ValidateOperationLimits(historyUpdateDetails);
                    server.ValidateHistoryUpdateOperationLimits(historyUpdateDetails);

                    (ArrayOf<HistoryUpdateResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos) =
                        await server.ServerInternal.NodeManager.HistoryUpdateAsync(
                            context,
                            historyUpdateDetails,
                            lifetime.CancellationToken).ConfigureAwait(false);

                    var response = new HistoryUpdateResponse
                    {
                        Results = results,
                        DiagnosticInfos = diagnosticInfos
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return response;
                },
                static (server, context, _, e) =>
                    server.ServerInternal.ReportAuditEvent(context, "HistoryUpdate", e, server.m_logger));
        }

        /// <summary>
        /// Validates the historyUpdateDetails array against the limit of every kind of
        /// update it contains, so a mixed batch is bounded by the smaller limit.
        /// </summary>
        private void ValidateHistoryUpdateOperationLimits(
            ArrayOf<ExtensionObject> historyUpdateDetails)
        {
            bool hasEventDetails = false;
            bool hasDataDetails = false;
            foreach (ExtensionObject details in historyUpdateDetails)
            {
                if (details.IsNull || !details.TryGetValue(out HistoryUpdateDetails? historyUpdateDetail))
                {
                    continue;
                }

                Type detailsType = historyUpdateDetail!.GetType();
                if (detailsType == typeof(UpdateEventDetails) ||
                    detailsType == typeof(DeleteEventDetails))
                {
                    hasEventDetails = true;
                }
                else if (detailsType == typeof(UpdateDataDetails) ||
                    detailsType == typeof(UpdateStructureDataDetails) ||
                    detailsType == typeof(DeleteRawModifiedDetails) ||
                    detailsType == typeof(DeleteAtTimeDetails))
                {
                    hasDataDetails = true;
                }
            }

            if (hasEventDetails)
            {
                ValidateOperationLimits(
                    historyUpdateDetails.Count,
                    OperationLimits.MaxNodesPerHistoryUpdateEvents);
            }

            if (hasDataDetails)
            {
                ValidateOperationLimits(
                    historyUpdateDetails.Count,
                    OperationLimits.MaxNodesPerHistoryUpdateData);
            }
        }

        /// <inheritdoc/>
        public override ValueTask<CreateSubscriptionResponse> CreateSubscriptionAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.CreateSubscription,
                requestLifetime,
                (requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    publishingEnabled,
                    priority),
                static async (server, context, header, lifetime, request) =>
                {
                    CreateSubscriptionResponse response =
                        await server.ServerInternal.SubscriptionManager.CreateSubscriptionAsync(
                            context,
                            request.requestedPublishingInterval,
                            request.requestedLifetimeCount,
                            request.requestedMaxKeepAliveCount,
                            request.maxNotificationsPerPublish,
                            request.publishingEnabled,
                            request.priority,
                            lifetime.CancellationToken).ConfigureAwait(false);

                    response.ResponseHeader = server.CreateResponse(header, context);
                    return response;
                });
        }

        /// <inheritdoc/>
        public override ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds,
            bool sendInitialValues,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.TransferSubscriptions,
                requestLifetime,
                (subscriptionIds, sendInitialValues),
                static async (server, context, header, lifetime, request) =>
                {
                    server.ValidateOperationLimits(request.subscriptionIds);

                    TransferSubscriptionsResponse response =
                        await server.ServerInternal.SubscriptionManager.TransferSubscriptionsAsync(
                            context,
                            request.subscriptionIds,
                            request.sendInitialValues,
                            lifetime.CancellationToken).ConfigureAwait(false);

                    response.ResponseHeader = server.CreateResponse(header, context);
                    return response;
                });
        }

        /// <inheritdoc/>
        public override ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.DeleteSubscriptions,
                requestLifetime,
                subscriptionIds,
                static async (server, context, header, lifetime, subscriptionIds) =>
                {
                    server.ValidateOperationLimits(subscriptionIds);

                    DeleteSubscriptionsResponse response =
                        await server.ServerInternal.SubscriptionManager.DeleteSubscriptionsAsync(
                            context,
                            subscriptionIds,
                            lifetime.CancellationToken).ConfigureAwait(false);

                    response.ResponseHeader = server.CreateResponse(header, context);
                    return response;
                });
        }

        /// <inheritdoc/>
        public override ValueTask<PublishResponse> PublishAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.Publish,
                requestLifetime,
                subscriptionAcknowledgements,
                static async (server, context, header, lifetime, subscriptionAcknowledgements) =>
                {
                    server.m_logger.PUBLISHRequestHandleRECEIVEDTIMETimestampHhMm(
                        context.SessionId,
                        header.RequestHandle,
                        header.Timestamp);

#pragma warning disable CA2025 // awaited below, before the context is disposed
                    Task<PublishResponse> publish = server.ServerInternal.SubscriptionManager.PublishAsync(
                        context,
                        subscriptionAcknowledgements,
                        lifetime.ParkSink,
                        lifetime.CancellationToken);
#pragma warning restore CA2025

                    // The acknowledgements are applied before the request waits: neither
                    // this state machine nor the envelope keeps them for the whole wait
                    // of a held Publish.
                    subscriptionAcknowledgements = default;
                    PublishResponse response = await publish.ConfigureAwait(false);

                    response.ResponseHeader = server.CreateResponse(header, context);
                    return response;
                });
        }

        /// <inheritdoc/>
        public override ValueTask<RepublishResponse> RepublishAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.Republish,
                requestLifetime,
                (subscriptionId, retransmitSequenceNumber),
                static (server, context, header, _, request) =>
                {
                    NotificationMessage notificationMessage = server.ServerInternal.SubscriptionManager.Republish(
                        context,
                        request.subscriptionId,
                        request.retransmitSequenceNumber);

                    var response = new RepublishResponse
                    {
                        NotificationMessage = notificationMessage
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return new ValueTask<RepublishResponse>(response);
                });
        }

        /// <inheritdoc/>
        public override ValueTask<ModifySubscriptionResponse> ModifySubscriptionAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.ModifySubscription,
                requestLifetime,
                (subscriptionId,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    priority),
                static (server, context, header, _, request) =>
                {
                    server.ServerInternal.SubscriptionManager.ModifySubscription(
                        context,
                        request.subscriptionId,
                        request.requestedPublishingInterval,
                        request.requestedLifetimeCount,
                        request.requestedMaxKeepAliveCount,
                        request.maxNotificationsPerPublish,
                        request.priority,
                        out double revisedPublishingInterval,
                        out uint revisedLifetimeCount,
                        out uint revisedMaxKeepAliveCount);

                    var response = new ModifySubscriptionResponse
                    {
                        RevisedPublishingInterval = revisedPublishingInterval,
                        RevisedLifetimeCount = revisedLifetimeCount,
                        RevisedMaxKeepAliveCount = revisedMaxKeepAliveCount
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return new ValueTask<ModifySubscriptionResponse>(response);
                });
        }

        /// <inheritdoc/>
        public override ValueTask<SetPublishingModeResponse> SetPublishingModeAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            bool publishingEnabled,
            ArrayOf<uint> subscriptionIds,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.SetPublishingMode,
                requestLifetime,
                (publishingEnabled, subscriptionIds),
                static (server, context, header, _, request) =>
                {
                    server.ValidateOperationLimits(request.subscriptionIds);

                    server.ServerInternal.SubscriptionManager.SetPublishingMode(
                        context,
                        request.publishingEnabled,
                        request.subscriptionIds,
                        out ArrayOf<StatusCode> results,
                        out ArrayOf<DiagnosticInfo> diagnosticInfos);

                    var response = new SetPublishingModeResponse
                    {
                        Results = results,
                        DiagnosticInfos = diagnosticInfos
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return new ValueTask<SetPublishingModeResponse>(response);
                });
        }

        /// <inheritdoc/>
        public override ValueTask<SetTriggeringResponse> SetTriggeringAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            ArrayOf<uint> linksToAdd,
            ArrayOf<uint> linksToRemove,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.SetTriggering,
                requestLifetime,
                (subscriptionId, triggeringItemId, linksToAdd, linksToRemove),
                static (server, context, header, _, request) =>
                {
                    if (request.linksToAdd.Count == 0 && request.linksToRemove.Count == 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadNothingToDo);
                    }

                    int monitoredItemsCount = 0;
                    monitoredItemsCount += request.linksToAdd.Count;
                    monitoredItemsCount += request.linksToRemove.Count;
                    server.ValidateOperationLimits(
                        monitoredItemsCount,
                        server.OperationLimits.MaxMonitoredItemsPerCall);

                    server.ServerInternal.SubscriptionManager.SetTriggering(
                        context,
                        request.subscriptionId,
                        request.triggeringItemId,
                        request.linksToAdd,
                        request.linksToRemove,
                        out ArrayOf<StatusCode> addResults,
                        out ArrayOf<DiagnosticInfo> addDiagnosticInfos,
                        out ArrayOf<StatusCode> removeResults,
                        out ArrayOf<DiagnosticInfo> removeDiagnosticInfos);

                    var response = new SetTriggeringResponse
                    {
                        AddResults = addResults,
                        AddDiagnosticInfos = addDiagnosticInfos,
                        RemoveResults = removeResults,
                        RemoveDiagnosticInfos = removeDiagnosticInfos
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return new ValueTask<SetTriggeringResponse>(response);
                });
        }

        /// <inheritdoc/>
        public override ValueTask<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.CreateMonitoredItems,
                requestLifetime,
                (subscriptionId, timestampsToReturn, itemsToCreate),
                static async (server, context, header, lifetime, request) =>
                {
                    server.ValidateOperationLimits(
                        request.itemsToCreate,
                        server.OperationLimits.MaxMonitoredItemsPerCall);
                    ValidateTimestampsToReturn(request.timestampsToReturn);

                    CreateMonitoredItemsResponse response =
                        await server.ServerInternal.SubscriptionManager.CreateMonitoredItemsAsync(
                            context,
                            request.subscriptionId,
                            request.timestampsToReturn,
                            request.itemsToCreate,
                            lifetime.CancellationToken).ConfigureAwait(false);

                    response.ResponseHeader = server.CreateResponse(header, context);
                    return response;
                });
        }

        /// <inheritdoc/>
        public override ValueTask<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.ModifyMonitoredItems,
                requestLifetime,
                (subscriptionId, timestampsToReturn, itemsToModify),
                static async (server, context, header, lifetime, request) =>
                {
                    server.ValidateOperationLimits(
                        request.itemsToModify,
                        server.OperationLimits.MaxMonitoredItemsPerCall);
                    ValidateTimestampsToReturn(request.timestampsToReturn);

                    ModifyMonitoredItemsResponse response =
                        await server.ServerInternal.SubscriptionManager.ModifyMonitoredItemsAsync(
                            context,
                            request.subscriptionId,
                            request.timestampsToReturn,
                            request.itemsToModify,
                            lifetime.CancellationToken).ConfigureAwait(false);

                    response.ResponseHeader = server.CreateResponse(header, context);
                    return response;
                });
        }

        /// <inheritdoc/>
        public override ValueTask<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            uint subscriptionId,
            ArrayOf<uint> monitoredItemIds,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.DeleteMonitoredItems,
                requestLifetime,
                (subscriptionId, monitoredItemIds),
                static async (server, context, header, lifetime, request) =>
                {
                    server.ValidateOperationLimits(
                        request.monitoredItemIds,
                        server.OperationLimits.MaxMonitoredItemsPerCall);

                    DeleteMonitoredItemsResponse response =
                        await server.ServerInternal.SubscriptionManager.DeleteMonitoredItemsAsync(
                            context,
                            request.subscriptionId,
                            request.monitoredItemIds,
                            lifetime.CancellationToken).ConfigureAwait(false);

                    response.ResponseHeader = server.CreateResponse(header, context);
                    return response;
                });
        }

        /// <inheritdoc/>
        public override ValueTask<SetMonitoringModeResponse> SetMonitoringModeAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            ArrayOf<uint> monitoredItemIds,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.SetMonitoringMode,
                requestLifetime,
                (subscriptionId, monitoringMode, monitoredItemIds),
                static async (server, context, header, lifetime, request) =>
                {
                    server.ValidateOperationLimits(
                        request.monitoredItemIds,
                        server.OperationLimits.MaxMonitoredItemsPerCall);

                    // Part 4 §5.13.4.3: an unknown monitoring mode is a service fault.
                    if (request.monitoringMode is < MonitoringMode.Disabled or > MonitoringMode.Reporting)
                    {
                        throw new ServiceResultException(StatusCodes.BadMonitoringModeInvalid);
                    }

                    (ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnosticInfos) =
                        await server.ServerInternal.SubscriptionManager.SetMonitoringModeAsync(
                            context,
                            request.subscriptionId,
                            request.monitoringMode,
                            request.monitoredItemIds,
                            lifetime.CancellationToken)
                        .ConfigureAwait(false);

                    var response = new SetMonitoringModeResponse
                    {
                        Results = results,
                        DiagnosticInfos = diagnosticInfos
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return response;
                });
        }

        /// <inheritdoc/>
        public override ValueTask<CallResponse> CallAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ArrayOf<CallMethodRequest> methodsToCall,
            RequestLifetime requestLifetime)
        {
            return ExecuteServiceAsync(
                secureChannelContext,
                requestHeader,
                RequestType.Call,
                requestLifetime,
                (secureChannelContext, methodsToCall),
                static async (server, context, header, lifetime, request) =>
                {
                    server.ValidateOperationLimits(
                        request.methodsToCall,
                        server.OperationLimits.MaxNodesPerMethodCall);
                    context.StartResponseBudget(
                        server.GetResponseByteBudget(request.secureChannelContext));

                    (ArrayOf<CallMethodResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos) =
                        await server.ServerInternal.NodeManager.CallAsync(
                            context,
                            request.methodsToCall,
                            lifetime.CancellationToken)
                        .ConfigureAwait(false);

                    var response = new CallResponse
                    {
                        Results = results,
                        DiagnosticInfos = diagnosticInfos
                    };
                    response.ResponseHeader = server.CreateResponse(header, context, response.ResponseHeader);
                    return response;
                },
                static (server, context, _, e) =>
                    server.ServerInternal.ReportMethodServiceAuditEvent(context, "Call", e, server.m_logger));
        }

        /// <inheritdoc/>
        public IServerInternal CurrentInstance =>
            Volatile.Read(ref m_serverInternal) ?? throw new ServiceResultException(StatusCodes.BadServerHalted);

        /// <summary>
        /// Returns the current status of the server.
        /// </summary>
        /// <returns>Returns a ServerStatusDataType object</returns>
        /// <exception cref="ServiceResultException"></exception>
        [Obsolete(
            "No longer thread safe. To read the value use CurrentState, to write use CurrentInstance.UpdateServerStatus."
        )]
        public ServerStatusDataType GetStatus()
        {
            if (m_serverInternal == null)
            {
                throw new ServiceResultException(StatusCodes.BadServerHalted);
            }
            return m_serverInternal.NonThreadSafeStatus.Value;
        }

        /// <inheritdoc/>
        public ServerState CurrentState => ServerInternal.CurrentState;

        /// <summary>
        /// Registers the server with the discovery server.
        /// </summary>
        /// <returns>Boolean value.</returns>
        [Obsolete("Use RegisterWithDiscoveryServerAsync instead.")]
        public bool RegisterWithDiscoveryServer()
        {
            return RegisterWithDiscoveryServerAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public ValueTask<bool> RegisterWithDiscoveryServerAsync(CancellationToken ct = default)
        {
            return m_discoveryRegistration.RegisterAsync(ct);
        }

        /// <summary>
        /// The synchronization object.
        /// </summary>
        [Obsolete("Use your own synchronization mechanism.")]
        protected object? Lock => null;

        /// <summary>
        /// The state object associated with the server.
        /// </summary>
        /// <value>The server internal data.</value>
        /// <exception cref="ServiceResultException"></exception>
        protected IServerInternal ServerInternal =>
            m_serverInternal ?? throw new ServiceResultException(StatusCodes.BadServerHalted);

        /// <summary>
        /// Creates the response header with the diagnostic strings of the request.
        /// </summary>
        /// <remarks>
        /// A request that wrote no diagnostic strings, which is nearly every request, has
        /// no string table of its own (see <see cref="OperationContext.StringTable"/>); an
        /// empty table of the thread stands in for it, so the response still carries an
        /// empty string table. The table is taken from the thread for the call and given
        /// back only if it is still empty when the call returns: an override of
        /// <see cref="ServerBase.CreateResponse(RequestHeader, StringTable)"/> that adds
        /// strings to it, or throws, keeps it, and a response created during the call
        /// (re-entrantly) gets a table of its own.
        /// A service that just constructed its response passes the response's default
        /// header as <paramref name="responseHeaderToFill"/>; the base implementation fills
        /// it instead of allocating another header (see
        /// <see cref="ServerBase.CreateResponse(RequestHeader, StringTable, ResponseHeader)"/>).
        /// </remarks>
        internal ResponseHeader CreateResponse(
            RequestHeader? requestHeader,
            OperationContext context,
            ResponseHeader? responseHeaderToFill = null)
        {
            StringTable? strings = context.CreatedStringTable;
            if (strings != null)
            {
                return CreateResponse(requestHeader!, strings, responseHeaderToFill);
            }

            strings = t_noStrings ?? new StringTable();
            t_noStrings = null;
            ResponseHeader responseHeader = CreateResponse(requestHeader!, strings, responseHeaderToFill);
            if (strings.Count == 0)
            {
                t_noStrings = strings;
            }

            return responseHeader;
        }

        [ThreadStatic]
        private static StringTable? t_noStrings;

        /// <summary>
        /// Verifies that the request header is valid.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <exception cref="ServiceResultException"></exception>
        protected override void ValidateRequest([NotNull] RequestHeader? requestHeader)
        {
            // check for server error.
            ServiceResult error = ServerError;

            if (ServiceResult.IsBad(error))
            {
                throw new ServiceResultException(error);
            }

            // check server state.
            ServerInternalData? serverInternal = m_serverInternal;

            if (serverInternal == null ||
                (!serverInternal.IsRunning && !NodeManagerReadinessScope.Allows(serverInternal)))
            {
                throw new ServiceResultException(StatusCodes.BadServerHalted);
            }

            base.ValidateRequest(requestHeader);
        }

        /// <summary>
        /// The <see cref="ServerState"/> the server enters once it has
        /// started. <see cref="ServerState.Running"/> by default; a server that
        /// starts in the application setup state (OPC 10000-12 Annex G.2,
        /// for example to wait for its certificates and TrustList to be
        /// provisioned) returns <see cref="ServerState.NoConfiguration"/>.
        /// The server accepts requests in both states.
        /// </summary>
        protected virtual ServerState StartupServerState => ServerState.Running;

        /// <summary>
        /// Updates the server state.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <exception cref="ServiceResultException"></exception>
        protected virtual void SetServerState(ServerState state)
        {
            m_semaphoreSlim.Wait();
            try
            {
                if (ServiceResult.IsBad(ServerError))
                {
                    throw new ServiceResultException(ServerError);
                }

                if (m_serverInternal == null)
                {
                    throw new ServiceResultException(StatusCodes.BadServerHalted);
                }

                m_logger.ServerEnterStateState(state);

                m_serverInternal.CurrentState = state;
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Reports an error during initialization after the base server object has been started.
        /// </summary>
        /// <param name="error">The error.</param>
        protected virtual void SetServerError(ServiceResult error)
        {
            m_semaphoreSlim.Wait();
            try
            {
                ServerError = error;
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Handles an error when validating the application instance certificate provided by a client.
        /// </summary>
        /// <remarks>
        /// The default implementation throws, which rejects the CreateSession request. An override
        /// that returns accepts the error: CreateSession keeps the certificate and continues with
        /// the remaining checks (trust, ApplicationUri match, SecureChannel certificate match),
        /// each of which reports its own failure here.
        /// </remarks>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="result">The result.</param>
        /// <exception cref="ServiceResultException"></exception>
        protected virtual void OnApplicationCertificateError(
            ByteString clientCertificate,
            ServiceResult result)
        {
            // see https://reference.opcfoundation.org/Core/Part4/v105/docs/6.1.3
            StatusCode resultCode = CertificateErrorReporting.GetClientStatusCode(result);

            throw new ServiceResultException(new ServiceResult(resultCode, result));
        }

        /// <summary>
        /// Verifies that the request header is valid.
        /// <para>
        /// This is not virtual, because a subclass that rejected a request after the request had
        /// been registered would leave it registered forever: the caller never receives the
        /// context, so nothing ever disposes it, and every NodeManager lifecycle operation would
        /// then wait for a request that has already failed. Override
        /// <see cref="OnRequestValidatedAsync"/> instead, which is invoked with the registered
        /// request and whose failures are cleaned up here.
        /// </para>
        /// </summary>
        /// <param name="secureChannelContext">The secure channel context.</param>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="requestType">Type of the request.</param>
        /// <param name="requestLifetime">The request lifetime.</param>
        /// <exception cref="ServiceResultException"></exception>
        protected async ValueTask<OperationContext> ValidateRequestAsync(
            SecureChannelContext secureChannelContext,
            [NotNull] RequestHeader? requestHeader,
            RequestType requestType,
            RequestLifetime requestLifetime)
        {
            base.ValidateRequest(requestHeader);

            ServerInternalData? serverInternal = m_serverInternal;
            if (serverInternal == null ||
                (!serverInternal.IsRunning && !NodeManagerReadinessScope.Allows(serverInternal)))
            {
                throw new ServiceResultException(StatusCodes.BadServerHalted);
            }

            RequestManager requestManager = serverInternal.RequestManager;

            OperationContext context;
            try
            {
                context = await serverInternal.SessionManager
                    .ValidateRequestAsync(requestHeader, secureChannelContext, requestType, requestLifetime).ConfigureAwait(false);
            }
            catch (ServiceResultException e)
            {
                // The services count their rejections only once the request was admitted, so
                // a request rejected by session validation (e.g. Bad_SessionIdInvalid or
                // Bad_SecureChannelIdInvalid) is counted here (OPC 10000-5 12.9).
                CountRejectedRequest(serverInternal, requestType, e.StatusCode);
                throw;
            }

            if (m_eventLogger.IsEventLogEnabled())
            {
                string? requestTypeString = Enum.GetName(
#if !NET8_0_OR_GREATER
                   typeof(RequestType),
#endif
                   context.RequestType);
                m_eventLogger.CompatibilityServerCall(
                    requestTypeString!,
                    context.RequestId,
                    context.SessionId);
            }

            // An ActivateSession runs without a Session context, so record which Session it
            // targets: closing that Session aborts the activation like any other of its
            // outstanding requests (OPC 10000-4 5.7.2.1) instead of waiting for it to finish.
            // A close that marked the Session closing before this registration is seen by the
            // activation itself once it holds the Session's activation gate.
            if (requestType == RequestType.ActivateSession && context.Session == null)
            {
                context.ActivationTargetSessionId = serverInternal.SessionManager
                    .GetSession(requestHeader.AuthenticationToken)?.Id ?? default;
            }

            // Hand the validated request over to its execution scope. The context owns the scope
            // from here, so disposing the context completes the request.
            requestManager.AttachRequestScope(context);

            // A close that started after the Session admitted this request, but before the
            // request was registered, did not see it when it aborted the Session's outstanding
            // requests (OPC 10000-4 5.7.2.1). The close marks the Session closing before it
            // sweeps the registered requests under the request manager lock, and registration
            // ends with a full fence, so a request the sweep missed sees the mark here.
            if (context.Session is ISession admittedSession &&
                SessionTermination.IsClosingOrClaimed(admittedSession))
            {
                CountRejectedRequest(serverInternal, requestType, StatusCodes.BadSessionClosed);
                context.Dispose();
                throw new ServiceResultException(StatusCodes.BadSessionClosed);
            }

            // A Cancel that ran while this request was still queued cancels it now
            // (OPC 10000-4 5.7.5.2). A failing cancellation callback is logged rather than
            // thrown, so the request is still completed and rejected with the Cancel's status.
            if (requestManager.IsCancelledBeforeAdmission(context))
            {
                try
                {
                    requestManager.TryCancelRequest(context, StatusCodes.BadRequestCancelledByClient);
                }
                finally
                {
                    context.Dispose();
                }
                throw new ServiceResultException(StatusCodes.BadRequestCancelledByClient);
            }

            try
            {
                if (requestType != RequestType.Publish && serverInternal.NodeManager is IRequestRouting routing)
                {
                    routing.PinRequest(context);
                }
                await OnRequestValidatedAsync(context).ConfigureAwait(false);
            }
            catch (ServiceResultException e)
            {
                CountRejectedRequest(serverInternal, requestType, e.StatusCode);
                context.Dispose();
                throw;
            }
            catch
            {
                context.Dispose();
                throw;
            }

            return context;
        }

        /// <summary>
        /// Counts a request that a service rejected after admission. A method of its own, so
        /// that the service method allocates the closure only when it rejects a request.
        /// The request also counts as an error of its Session (OPC 10000-5 12.13).
        /// </summary>
        private void CountRejectedServiceRequest(OperationContext context, StatusCode statusCode)
        {
            (context.Session as Session)?.UpdateRejectedRequestCounters(
                context.RequestType,
                statusCode);

            ServerInternal.UpdateServerDiagnostics(diagnostics =>
            {
                diagnostics.RejectedRequestsCount++;

                if (IsSecurityError(statusCode))
                {
                    diagnostics.SecurityRejectedRequestsCount++;
                }
            });
        }

        /// <summary>
        /// Counts a request rejected before its service ran in the server diagnostics.
        /// </summary>
        private void CountRejectedRequest(
            ServerInternalData serverInternal,
            RequestType requestType,
            StatusCode statusCode)
        {
            // CreateSessionAsync counts every CreateSession it rejects before admission,
            // including those rejected here, as a rejected session (and request), so it is
            // not counted twice (OPC 10000-5 12.9).
            if (requestType == RequestType.CreateSession)
            {
                return;
            }

            serverInternal.UpdateServerDiagnostics(diagnostics =>
            {
                diagnostics.RejectedRequestsCount++;

                if (IsSecurityError(statusCode))
                {
                    diagnostics.SecurityRejectedRequestsCount++;
                }
            });
        }

        /// <summary>
        /// Called once a request has been validated and registered, so that a subclass can apply
        /// its own admission rules. Throwing rejects the request and completes it.
        /// </summary>
        /// <param name="context">The context of the request that was validated.</param>
        /// <exception cref="ServiceResultException">The request is rejected.</exception>
        protected virtual ValueTask OnRequestValidatedAsync(OperationContext context)
        {
            return default;
        }

        /// <summary>
        /// Validate operation limits.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="operation">A list of operations.</param>
        /// <param name="operationLimit">The operation limit property.</param>
        /// <exception cref="ServiceResultException">BadNothingToDo if list is null or empty.</exception>
        /// <exception cref="ServiceResultException">BadTooManyOperations if list is larger than operation limit property.</exception>
        protected void ValidateOperationLimits<T>(
            ArrayOf<T> operation,
            PropertyState<uint>? operationLimit = null)
        {
            if (operation.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadNothingToDo);
            }
            ValidateOperationLimits(operation.Count, operationLimit);
        }

        /// <summary>
        /// Validate operation limits.
        /// </summary>
        /// <param name="count">A count of operations.</param>
        /// <param name="operationLimit">The operation limit property.</param>
        /// <exception cref="ServiceResultException">BadTooManyOperations if count is larger than operation limit property.</exception>
        protected void ValidateOperationLimits(int count, PropertyState<uint>? operationLimit)
        {
            uint operationLimitValue = operationLimit != null ? operationLimit.Value : 0;
            if (operationLimitValue > 0 && count > operationLimitValue)
            {
                throw new ServiceResultException(StatusCodes.BadTooManyOperations);
            }
        }

        /// <summary>
        /// Validates the TimestampsToReturn service parameter before any operation is dispatched,
        /// so the service fault does not depend on whether the operations resolve
        /// (Part 4 §5.13.2.3, §5.13.3.3).
        /// </summary>
        /// <exception cref="ServiceResultException">BadTimestampsToReturnInvalid for an unknown value.</exception>
        private static void ValidateTimestampsToReturn(TimestampsToReturn timestampsToReturn)
        {
            if (timestampsToReturn is < TimestampsToReturn.Source or > TimestampsToReturn.Neither)
            {
                throw new ServiceResultException(StatusCodes.BadTimestampsToReturnInvalid);
            }
        }

        /// <summary>
        /// Translates an exception.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="e">The ServiceResultException e.</param>
        /// <returns>Returns an exception thrown when a UA defined error occurs, the return type is <seealso cref="ServiceResultException"/>.</returns>
        protected virtual ServiceResultException TranslateException(
            OperationContext context,
            ServiceResultException e)
        {
            ArrayOf<string> preferredLocales = default;

            if (context != null && context.Session != null)
            {
                preferredLocales = context.Session.PreferredLocales;
            }

            return TranslateException(context!.DiagnosticsMask, preferredLocales, e);
        }

        /// <summary>
        /// Translates an exception.
        /// </summary>
        /// <param name="diagnosticsMasks">The fields to return.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="e">The ServiceResultException e.</param>
        /// <returns>Returns an exception thrown when a UA defined error occurs, the return type is <seealso cref="ServiceResultException"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="e"/> is null.
        /// </exception>
        protected virtual ServiceResultException TranslateException(
            DiagnosticsMasks diagnosticsMasks,
            ArrayOf<string> preferredLocales,
            ServiceResultException e)
        {
            if (e == null)
            {
                throw new ArgumentNullException(nameof(e));
            }

            // check if inner result required.
            ServiceResult? innerResult = null;

            if ((
                    diagnosticsMasks &
                    (DiagnosticsMasks.ServiceInnerDiagnostics |
                        DiagnosticsMasks.ServiceInnerStatusCode)
                ) != 0)
            {
                innerResult = e.InnerResult;
            }

            // check if translated text required.
            LocalizedText translatedText = default;

            if ((diagnosticsMasks & DiagnosticsMasks.ServiceLocalizedText) != 0)
            {
                translatedText = e.LocalizedText;
            }

            // create new result object.
            var result = new ServiceResult(
                e.NamespaceUri,
                new StatusCode(e.StatusCode.Code, e.SymbolicId),
                translatedText,
                e.AdditionalInfo,
                innerResult);

            // translate result.
            result = ServerInternal.ResourceManager.Translate(preferredLocales, result)!;
            return new ServiceResultException(result);
        }

        /// <summary>
        /// Translates a service result.
        /// </summary>
        /// <param name="diagnosticsMasks">The fields to return.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="result">The result.</param>
        /// <returns>Returns a class that combines the status code and diagnostic info structures.</returns>
        protected virtual ServiceResult? TranslateResult(
            DiagnosticsMasks diagnosticsMasks,
            ArrayOf<string> preferredLocales,
            ServiceResult result)
        {
            if (result == null)
            {
                return null;
            }

            return ServerInternal.ResourceManager.Translate(preferredLocales, result);
        }

        /// <summary>
        /// Verifies that the request header is valid.
        /// </summary>
        /// <param name="context">The operation context.</param>
        /// <exception cref="ServiceResultException"></exception>
        protected virtual void OnRequestComplete(OperationContext context)
        {
            if (Volatile.Read(ref m_serverInternal) == null)
            {
                throw new ServiceResultException(StatusCodes.BadServerHalted);
            }

            // The request itself is completed by disposing the OperationContext, which owns
            // the execution scope. This hook remains for derived servers that extend it.
        }

        /// <summary>
        /// Raised when the configuration changes.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="args">The <see cref="ConfigurationWatcherEventArgs"/> instance containing the event data.</param>
        protected virtual async void OnConfigurationChangedAsync(
            object? sender,
            ConfigurationWatcherEventArgs args)
        {
            await m_configurationReload.ReloadAsync(args).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override ValueTask OnUpdateConfigurationAsync(
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            return m_configurationReload.ApplyAsync(configuration, cancellationToken);
        }

        /// <summary>
        /// Called before the server starts.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            m_semaphoreSlim.Wait();
            try
            {
                m_discoveryRegistration.ClearShutdownRequest();
                base.OnServerStarting(configuration);
                InitializeResourceIsolation(configuration, MessageContext.Telemetry);

                InitializeRateLimiting();

                // save minimum nonce length.
                m_minNonceLength = configuration.SecurityConfiguration.NonceLength;

                // try first RegisterServer2
                m_discoveryRegistration.PreferRegisterServer2();
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Injects the configured listener backlog and connection rate limiter into
        /// the transport listener settings.
        /// </summary>
        /// <param name="settings">The listener settings being assembled.</param>
        /// <param name="endpointUri">The endpoint the listener will serve.</param>
        protected override void ConfigureTransportListenerSettings(
            TransportListenerSettings settings,
            Uri endpointUri)
        {
            ResourceIsolationOptions.ValidateHandshakeTimeout();
            base.ConfigureTransportListenerSettings(settings, endpointUri);
            m_admission.ConfigureTransportListenerSettings(settings);
        }

        /// <summary>
        /// Recreates server-owned isolation from validated limits without replacing a borrowed provider or budget.
        /// </summary>
        internal void InitializeResourceIsolation(
            ApplicationConfiguration configuration,
            ITelemetryContext telemetry)
        {
            m_admission.InitializeResourceIsolation(configuration, telemetry);
        }

        /// <summary>
        /// Recreates server-owned rate limits against the active isolation plan while preserving borrowed providers.
        /// </summary>
        internal void InitializeRateLimiting()
        {
            m_admission.InitializeRateLimiting();
        }

        /// <inheritdoc/>
        bool IServerSessionBindingProvider.HasSession(string secureChannelId)
        {
            return (m_serverInternal?.SessionManager as IServerSessionBindingProvider)?.HasSession(secureChannelId)
                ?? false;
        }

        /// <inheritdoc/>
        bool IServerSessionBindingProvider.TryGetSessionContext(
            NodeId authenticationToken,
            SecureChannelContext channelContext,
            [NotNullWhen(true)] out SessionBindingContext? context)
        {
            if (m_serverInternal?.SessionManager is IServerSessionBindingProvider provider)
            {
                return provider.TryGetSessionContext(authenticationToken, channelContext, out context);
            }
            context = null;
            return false;
        }

        /// <summary>
        /// Acquires an admission permit for a single session establishment operation.
        /// </summary>
        /// <exception cref="ServiceResultException">The server is too busy to admit the operation.</exception>
        internal IDisposable? BeginSessionEstablishmentOrThrow(
            SecureChannelContext channelContext,
            NodeId authenticationToken)
        {
            return m_admission.BeginSessionEstablishmentOrThrow(channelContext, authenticationToken);
        }

        /// <summary>
        /// The parking policy the endpoints use, see <see cref="SessionAdmission.RequestParkingPolicy"/>.
        /// </summary>
        IRequestParkingPolicy? IRequestParkingPolicySource.RequestParkingPolicy => m_admission.RequestParkingPolicy;

        /// <summary>
        /// The prefix of the machine-readable retry-after token of a <c>BadServerTooBusy</c> fault.
        /// </summary>
        internal const string RetryAfterHintPrefix = SessionAdmission.RetryAfterHintPrefix;

        /// <summary>
        /// Creates the endpoints and creates the hosts.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="bindingFactory">The transport listener binding factory.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// Returns IList of a host for a UA service together with the
        /// aggregated discovery information.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// No transport listener factory is registered for a configured endpoint scheme.
        /// </exception>
        protected override async ValueTask<ServiceHostInitializationResult> InitializeServiceHostsAsync(
            ApplicationConfiguration configuration,
            ITransportBindingRegistry bindingFactory,
            CancellationToken cancellationToken = default)
        {
            var hosts = new Dictionary<string, ServiceHost>();

            // ensure at least one security policy exists.
            if (configuration.ServerConfiguration!.SecurityPolicies.IsEmpty)
            {
                configuration.ServerConfiguration.SecurityPolicies =
                    configuration.ServerConfiguration.SecurityPolicies.AddItem(new ServerSecurityPolicy());
            }

            // ensure at least one user token policy exists.
            if (configuration.ServerConfiguration.UserTokenPolicies.IsEmpty)
            {
                var userTokenPolicy = new UserTokenPolicy { TokenType = UserTokenType.Anonymous };
                userTokenPolicy.PolicyId = userTokenPolicy.TokenType.ToString();

                configuration.ServerConfiguration.UserTokenPolicies += userTokenPolicy;
            }

            // set server description.
            var serverDescription = new ApplicationDescription
            {
                ApplicationUri = configuration.ApplicationUri,
                ApplicationName = new LocalizedText("en-US", configuration.ApplicationName),
                ApplicationType = configuration.ApplicationType,
                ProductUri = configuration.ProductUri,
                DiscoveryUrls = GetDiscoveryUrls()
            };

            List<EndpointDescription> endpointsList = [];
            ArrayOf<string> baseAddresses = configuration.ServerConfiguration.BaseAddresses;
            foreach (
                string scheme in Utils.DefaultUriSchemes.Where(scheme =>
                    baseAddresses.Contains(a => a.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))))
            {
                ITransportListenerFactory binding = bindingFactory.GetListenerFactory(scheme) ??
                    throw new InvalidOperationException(
                        $"No OPC UA transport listener is registered for endpoint scheme '{scheme}'. " +
                        "Register the matching transport binding, for example by calling " +
                        $"Add{GetTransportName(scheme)}Transport().");

                List<EndpointDescription> endpointsForHost = await binding.CreateServiceHostAsync(
                    this,
                    hosts,
                    configuration,
                    configuration.ServerConfiguration.BaseAddresses,
                    serverDescription,
                    configuration.ServerConfiguration.SecurityPolicies,
                    CertificateManager!,
                    configuration.CertificateManager!,
                    cancellationToken).ConfigureAwait(false);
                endpointsList.AddRange(endpointsForHost);
            }
            return new ServiceHostInitializationResult(
                [.. hosts.Values],
                serverDescription,
                endpointsList);
        }

        /// <summary>
        /// Maps a URI scheme to the name used by the corresponding
        /// <c>Add*Transport()</c> DI extension method.
        /// </summary>
        private static string GetTransportName(string scheme)
        {
            return scheme switch
            {
                Utils.UriSchemeOpcTcp => "OpcTcp",
                Utils.UriSchemeHttps or Utils.UriSchemeOpcHttps => "Https",
                Utils.UriSchemeWss or Utils.UriSchemeOpcWss => "Wss",
                _ => scheme
            };
        }

        /// <summary>
        /// Creates an instance of the service host.
        /// </summary>
        public override ServiceHost CreateServiceHost(ServerBase server, params Uri[] addresses)
        {
            return new ServiceHost(this, addresses);
        }

        /// <summary>
        /// Returns an instance of the endpoint to use.
        /// </summary>
        protected override EndpointBase GetEndpointInstance(ServerBase server)
        {
            return new SessionEndpoint(server);
        }

        /// <inheritdoc/>
        protected override async ValueTask StartApplicationAsync(ApplicationConfiguration configuration, CancellationToken cancellationToken = default)
        {
            await base.StartApplicationAsync(configuration, cancellationToken)
                .ConfigureAwait(false);
            await m_lifecycle.StartAsync(configuration, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the server through the <see cref="ServerBase"/> implementation, the
        /// lifetime owner the startup hands to a dynamic node manager host.
        /// </summary>
        private ValueTask StopBaseAsync(CancellationToken cancellationToken)
        {
            return base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// Registers a user-token authenticator immediately or retains it until server startup.
        /// </summary>
        /// <param name="authenticator">The provider that authenticates user tokens.</param>
        internal void RegisterIdentityAuthenticator(IUserTokenAuthenticator authenticator)
        {
            if (authenticator == null)
            {
                throw new ArgumentNullException(nameof(authenticator));
            }

            if (m_serverInternal is { } serverInternal)
            {
                serverInternal.IdentityRegistry.Register(authenticator);
                return;
            }

            m_preStartAuthenticators.Add(authenticator);
        }

        /// <summary>
        /// Registers an identity augmenter immediately or retains it until server startup.
        /// </summary>
        /// <param name="augmenter">The provider that augments authenticated identities.</param>
        internal void RegisterIdentityAugmenter(IIdentityAugmenter augmenter)
        {
            if (augmenter == null)
            {
                throw new ArgumentNullException(nameof(augmenter));
            }

            if (m_serverInternal is { } serverInternal)
            {
                serverInternal.IdentityRegistry.RegisterAugmenter(augmenter);
                return;
            }

            m_preStartIdentityAugmenters.Add(augmenter);
        }

        /// <inheritdoc/>
        protected override ValueTask OnServerStoppingAsync(CancellationToken cancellationToken = default)
        {
            return m_lifecycle.StopAsync(cancellationToken);
        }

        internal static async Task RunShutdownStageAsync(
            List<Exception> failures,
            Func<ValueTask> stage)
        {
            try
            {
                await stage().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failures.Add(ex);
            }
        }

        private static void RunShutdownStage(List<Exception> failures, Action stage)
        {
            try
            {
                stage();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failures.Add(ex);
            }
        }

        private static void ThrowShutdownFailures(List<Exception> failures)
        {
            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures.Count > 1)
            {
                throw new AggregateException("One or more server disposal stages failed.", failures);
            }
        }

        /// <summary>
        /// Trys to get the secure channel id for an AuthenticationToken.
        /// The ChannelId is known to the sessions of the Server.
        /// Each session has an AuthenticationToken which can be used to identify the session.
        /// </summary>
        /// <param name="authenticationToken">The AuthenticationToken from the RequestHeader</param>
        /// <param name="channelId">The Channel id</param>
        /// <returns>returns true if a channelId was found for the provided AuthenticationToken</returns>
        public override bool TryGetSecureChannelIdForAuthenticationToken(
            NodeId authenticationToken,
            out uint channelId)
        {
            ISession? session = ServerInternal.SessionManager.GetSession(authenticationToken);

            if (session == null)
            {
                channelId = 0;
                return false;
            }

            return uint.TryParse(session.SecureChannelId, out channelId);
        }

        /// <summary>
        /// Implements the server shutdown delay if session are connected.
        /// </summary>
        protected void ShutDownDelay()
        {
            try
            {
                IServerInternal server = ServerInternal;
                if (!BeginShutdownCountdown(server))
                {
                    return;
                }
                for (int timeTillShutdown = Configuration!.ServerConfiguration!.ShutdownDelay;
                    timeTillShutdown > 0;
                    timeTillShutdown--)
                {
                    if (!UpdateShutdownCountdown(server, timeTillShutdown))
                    {
                        break;
                    }
                    Thread.Sleep(1000);
                }
            }
            catch
            {
                // ignore error during shutdown procedure.
            }
        }

        /// <summary>
        /// Lets connected clients observe shutdown without blocking the server's asynchronous stop operation.
        /// </summary>
        /// <param name="cancellationToken">The token used to cancel the countdown.</param>
        /// <returns>A task completed when the countdown expires or all clients disconnect.</returns>
        private async ValueTask WaitForShutdownDelayAsync(CancellationToken cancellationToken)
        {
            if (m_serverInternal is not { } server || !BeginShutdownCountdown(server))
            {
                return;
            }
            for (int remaining = Configuration!.ServerConfiguration!.ShutdownDelay; remaining > 0; remaining--)
            {
                if (!UpdateShutdownCountdown(server, remaining))
                {
                    break;
                }
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Announces shutdown to connected clients before their sessions are terminated.
        /// </summary>
        /// <param name="server">The server whose connected clients should observe shutdown.</param>
        /// <returns>True when connected clients need the configured countdown.</returns>
        private static bool BeginShutdownCountdown(IServerInternal server)
        {
            if (server.SessionManager.GetSessions().Count == 0)
            {
                return false;
            }
            server.UpdateServerStatus(status =>
            {
                status.Value.ShutdownReason = new LocalizedText("en-US", "Application is shutting down.");
                ServerStatusState variable = status!.Variable!;
                variable.ShutdownReason!.Value = new LocalizedText("en-US", "Application is shutting down.");
                status.Value.State = ServerState.Shutdown;
                variable.State!.Value = ServerState.Shutdown;
                variable.ClearChangeMasks(server.DefaultSystemContext, true);
            });
            return true;
        }

        /// <summary>
        /// Publishes the countdown while clients remain connected.
        /// </summary>
        /// <param name="server">The server being stopped.</param>
        /// <param name="remaining">The seconds remaining before session termination.</param>
        /// <returns>True when at least one session is still connected.</returns>
        private bool UpdateShutdownCountdown(IServerInternal server, int remaining)
        {
            server.UpdateServerStatus(status =>
            {
                status.Value.SecondsTillShutdown = (uint)remaining;
                status!.Variable!.SecondsTillShutdown!.Value = (uint)remaining;
                status.Variable.ClearChangeMasks(server.DefaultSystemContext, true);
            });
            int sessionCount = server.SessionManager.GetSessions().Count;
            if (sessionCount == 0)
            {
                return false;
            }
            m_logger.SessionCountActiveSessionsSecondsUntilShutdown(sessionCount, remaining);
            return true;
        }

        /// <summary>
        /// Creates the request manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>
        /// Returns an object that manages requests from within the server, return type is <seealso cref="RequestManager"/>.
        /// </returns>
        protected virtual RequestManager CreateRequestManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return new RequestManager(server);
        }

        /// <summary>
        /// Creates the aggregate manager used by the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>The manager.</returns>
        protected virtual async ValueTask<AggregateManager> CreateAggregateManagerAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            var manager = new AggregateManager(server);

            await manager.RegisterStandardAggregatesAsync(cancellationToken).ConfigureAwait(false);

            return manager;
        }

        /// <summary>
        /// Creates the modelling rules manager used by the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="cancellationToken">The cancellation Token</param>
        /// <returns>The manager.</returns>
        protected virtual async ValueTask<ModellingRulesManager> CreateModellingRulesManagerAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            var manager = new ModellingRulesManager(server);

            await manager.RegisterModellingRuleAsync(ObjectIds.ModellingRule_Mandatory, BrowseNames.ModellingRule_Mandatory, cancellationToken)
                .ConfigureAwait(false);
            await manager.RegisterModellingRuleAsync(ObjectIds.ModellingRule_Optional, BrowseNames.ModellingRule_Optional, cancellationToken)
                .ConfigureAwait(false);
            await manager.RegisterModellingRuleAsync(ObjectIds.ModellingRule_ExposesItsArray, BrowseNames.ModellingRule_ExposesItsArray, cancellationToken)
                .ConfigureAwait(false);
            await manager.RegisterModellingRuleAsync(
                ObjectIds.ModellingRule_OptionalPlaceholder,
                BrowseNames.ModellingRule_OptionalPlaceholder,
                cancellationToken).ConfigureAwait(false);
            await manager.RegisterModellingRuleAsync(
                ObjectIds.ModellingRule_MandatoryPlaceholder,
                BrowseNames.ModellingRule_MandatoryPlaceholder,
                cancellationToken).ConfigureAwait(false);

            return manager;
        }

        /// <summary>
        /// Creates the conformance units manager used by the server.
        /// </summary>
        /// <remarks>
        /// The default implementation aggregates the conformance units and
        /// server profiles from every registered node manager implementing
        /// <see cref="IConformanceContributor"/> and publishes them to the
        /// address space. Servers that advertise a fixed set may override this to
        /// register additional units before publishing.
        /// </remarks>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="cancellationToken">The cancellation Token</param>
        /// <returns>The manager.</returns>
        protected virtual async ValueTask<ConformanceUnitsManager> CreateConformanceUnitsManagerAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            var manager = new ConformanceUnitsManager(server);

            foreach (IAsyncNodeManager nodeManager in server.NodeManager.AsyncNodeManagers)
            {
                // A natively-async node manager is the contributor itself; a
                // wrapped synchronous node manager exposes it through its
                // SyncNodeManager. Honour either shape.
                if ((nodeManager as IConformanceContributor
                    ?? nodeManager.SyncNodeManager as IConformanceContributor)
                    is IConformanceContributor contributor)
                {
                    manager.Register(contributor);
                }
            }

            await manager.PublishAsync(cancellationToken).ConfigureAwait(false);

            return manager;
        }

        /// <summary>
        /// Publishes a <c>NamespaceMetadataType</c> Object under
        /// <c>Server/Namespaces</c> for every namespace in the server's
        /// <c>NamespaceArray</c>.
        /// </summary>
        /// <remarks>
        /// OPC 10000-5 requires the <c>Namespaces</c> Object to describe the
        /// namespaces the server provides; clients use the published version and
        /// publication date to validate the models they cache. Servers that
        /// manage their namespace metadata themselves may override this to do
        /// nothing.
        /// </remarks>
        /// <param name="server">The server.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        protected virtual ValueTask PublishNamespaceMetadataAsync(
            IServerInternal server,
            CancellationToken cancellationToken = default)
        {
            return new NamespaceMetadataPublisher(server).PublishAsync(cancellationToken);
        }

        /// <summary>
        /// Creates the resource manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns an object that manages access to localized resources, the return type is <seealso cref="ResourceManager"/>.</returns>
        protected virtual ResourceManager CreateResourceManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            var resourceManager = new ResourceManager(configuration);

            // load default text for all status codes.
            resourceManager.LoadDefaultText();

            return resourceManager;
        }

        /// <summary>
        /// Gets registered factories in their initial address-space creation order.
        /// </summary>
        /// <returns>The asynchronous factories followed by adapted synchronous factories.</returns>
        protected ArrayOf<IAsyncNodeManagerFactory> GetInitialNodeManagerFactories()
        {
            return
            [
                .. m_asyncNodeManagerFactories,
                .. m_nodeManagerFactories.Select(
                    static factory => new NodeManagerLifecycleExtensions.SyncFactoryAdapter(factory))
            ];
        }

        /// <summary>
        /// Creates the master node manager for the server (async).
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The configured master node manager.</returns>
        /// <remarks>
        /// Subclasses that need to plug in custom async node-manager factories should
        /// override this method. The default implementation awaits each registered
        /// <see cref="IAsyncNodeManagerFactory"/> and delegates the synchronous
        /// factories to the same initial Add pipeline after the system address spaces are initialized.
        /// </remarks>
        protected virtual async ValueTask<IMasterNodeManager> CreateMasterNodeManagerAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArrayOf<IAsyncNodeManagerFactory> initialFactories = GetInitialNodeManagerFactories();
            MasterNodeManager? master = null;
            try
            {
                master = new MasterNodeManager(server, configuration, null, initialFactories);
                IMasterNodeManager result = await new ValueTask<IMasterNodeManager>(master)
                    .ConfigureAwait(false);
                master = null;
                return result;
            }
            finally
            {
                master?.Dispose();
            }
        }

        /// <summary>
        /// Creates the master node manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns the master node manager for the server, the return type is <seealso cref="MasterNodeManager"/>.</returns>
        protected virtual IMasterNodeManager CreateMasterNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return CreateMasterNodeManagerAsync(server, configuration).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates the master node manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns the master node manager for the server, the return type is <seealso cref="MasterNodeManager"/>.</returns>
        protected virtual IMainNodeManagerFactory CreateMainNodeManagerFactory(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return new MainNodeManagerFactory(configuration, server);
        }

        /// <summary>
        /// Creates the event manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns an object that manages all events raised within the server, the return type is <seealso cref="EventManager"/>.</returns>
        protected virtual EventManager CreateEventManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return new EventManager(
                server,
                (uint)configuration.ServerConfiguration!.MaxEventQueueSize,
                (uint)configuration.ServerConfiguration.MaxDurableEventQueueSize,
                EventIdentityAdmissionOptions);
        }

        /// <summary>
        /// Gets or sets the bounded event identity policy used when creating the
        /// server's event manager. Set before startup, or register the options in DI.
        /// </summary>
        public EventIdentityAdmissionOptions EventIdentityAdmissionOptions { get; set; } = new();

        /// <summary>
        /// An optional factory used to build the server's session manager. When
        /// <c>null</c> (the default), the built-in <see cref="SessionManager"/>
        /// is used. Set this before the server starts (the hosting layer wires
        /// it from dependency injection) to plug in a custom session manager,
        /// e.g. a distributed one for high availability.
        /// </summary>
        public ISessionManagerFactory? SessionManagerFactory { get; set; }

        /// <summary>
        /// Creates the role manager for the server before the RoleSet address space is bound.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns an object that manages Part 18 role mappings.</returns>
        protected virtual IRoleManager CreateRoleManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return server.RoleManager;
        }

        /// <summary>
        /// Creates the user management model for the server before the address space is bound.
        /// </summary>
        /// <remarks>
        /// Returns <c>null</c> when the server does not expose the OPC UA Part 18 §5
        /// user-management model. Override to supply a concrete implementation, or register
        /// an <see cref="UserManagement.IUserManagement"/> in the service container when
        /// hosting through dependency injection.
        /// </remarks>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns the user management model, or <c>null</c>.</returns>
        protected virtual UserManagement.IUserManagement? CreateUserManagement(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return null;
        }

        /// <summary>
        /// Creates the session manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns a generic session manager object for a server, the return type is <seealso cref="ISessionManager"/>.</returns>
        protected virtual ISessionManager CreateSessionManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            if (SessionManagerFactory != null)
            {
                return SessionManagerFactory.Create(
                    server, configuration, TimeProvider, GetInstanceCertificateForPolicy);
            }
            return new SessionManager(server, configuration, TimeProvider);
        }

        /// <summary>
        /// Resolves the server's instance certificate for a security policy URI,
        /// for use by a custom session manager factory.
        /// </summary>
        private Certificate? GetInstanceCertificateForPolicy(string securityPolicyUri)
        {
            // Acquire a caller-owned handle for the policy's instance certificate
            // and return an independent ref-counted handle to the session manager,
            // which disposes it after the restored session takes its own reference
            // (the Session constructor AddRefs the server certificate).
            CertificateManager? certificates = CertificateManager;
            if (certificates == null)
            {
                return null;
            }
            EndpointDescription? endpoint = Endpoints.Find(
                candidate => candidate.SecurityPolicyUri == securityPolicyUri);
            using CertificateEntry? entry = endpoint == null
                ? certificates.AcquireApplicationCertificateBySecurityPolicy(securityPolicyUri)
                : AcquireEndpointCertificate(endpoint, certificates, SecurityPolicyRegistry);
            return entry?.Certificate?.AddRef();
        }

        /// <summary>
        /// Creates the session manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns a generic session manager object for a server, the return type is <seealso cref="SubscriptionManager"/>.</returns>
        protected virtual ISubscriptionManager CreateSubscriptionManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return new SubscriptionManager(server, configuration);
        }

        /// <summary>
        /// Creates the (durable) monitored item queue factory for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns a (durable) monitored item queue factory for a server, the return type is <seealso cref="IMonitoredItemQueueFactory"/>.</returns>
        protected virtual IMonitoredItemQueueFactory? CreateMonitoredItemQueueFactory(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return MonitoredItemQueueFactory
                ?? new MonitoredItemQueueFactory(MessageContext.Telemetry);
        }

        /// <summary>
        /// Whether the server owns (and disposes on stop) the factory returned by
        /// <see cref="CreateMonitoredItemQueueFactory"/>. A factory supplied through
        /// <see cref="MonitoredItemQueueFactory"/> is owned by the caller.
        /// </summary>
        /// <param name="factory">The factory returned by <see cref="CreateMonitoredItemQueueFactory"/>.</param>
        /// <returns><c>true</c> when the server disposes the factory on shutdown.</returns>
        protected virtual bool OwnsMonitoredItemQueueFactory(IMonitoredItemQueueFactory factory)
        {
            return !ReferenceEquals(factory, MonitoredItemQueueFactory);
        }

        /// <summary>
        /// Creates the subscriptionStore for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns a subscriptionStore for a server, the return type is <seealso cref="ISubscriptionStore"/>.</returns>
        protected virtual ISubscriptionStore? CreateSubscriptionStore(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return SubscriptionStore;
        }

        /// <summary>
        /// Called after the node managers have been started.
        /// </summary>
        /// <param name="server">The server.</param>
        protected virtual void OnNodeManagerStarted(IServerInternal server)
        {
            // may be overridden by the subclass.
        }

        /// <summary>
        /// Called asynchronously after the node managers have been started and
        /// the complete address space is available, but before the server
        /// begins accepting connections. By default this builds stand-in
        /// encodeables for custom DataTypes loaded from a NodeSet at runtime
        /// (when <see cref="LoadComplexTypes"/> is enabled). Subclasses may
        /// override to augment the server once the full address space exists.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        protected virtual async ValueTask OnNodeManagerStartedAsync(
            IServerInternal server,
            CancellationToken cancellationToken = default)
        {
            await RefreshComplexTypesAsync(
                server,
                additionalNodeManager: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Called after the server has been started.
        /// </summary>
        /// <param name="server">The server.</param>
        protected virtual void OnServerStarted(IServerInternal server)
        {
            // may be overridden by the subclass.
        }

        /// <summary>
        /// Loads custom data types and refreshes the schema resolver when complex-type loading is enabled.
        /// </summary>
        /// <param name="server">The server whose custom data types are loaded.</param>
        /// <param name="additionalNodeManager">An additional NodeManager to read types from, if any.</param>
        /// <param name="publishResolver">Whether to publish the resolver on the server.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The refreshed resolver, or null when complex-type loading is disabled.</returns>
        internal async ValueTask<IDataTypeDefinitionResolver?> RefreshComplexTypesAsync(
            IServerInternal server,
            IAsyncNodeManager? additionalNodeManager = null,
            bool publishResolver = true,
            CancellationToken cancellationToken = default)
        {
            if (LoadComplexTypes)
            {
                // Build stand-in encodeables for custom DataTypes loaded from a
                // NodeSet at runtime (types already in the factory are skipped)
                // and expose the primed factory as the schema resolver.
                IDataTypeDefinitionResolver resolver = additionalNodeManager is null
                    ? await server
                        .LoadComplexTypesAsync(
                            server.Telemetry,
                            ComplexTypeOptions,
                            ComplexTypeRegistry,
                            cancellationToken)
                        .ConfigureAwait(false)
                    : await server
                        .LoadComplexTypesAsync(
                            server.Telemetry,
                            ComplexTypeOptions,
                            ComplexTypeRegistry,
                            additionalNodeManager,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (publishResolver)
                {
                    ComplexTypeResolverHolder?.SetResolver(resolver);
                }
                return resolver;
            }
            return null;
        }

        /// <inheritdoc/>
        public IEnumerable<INodeManagerFactory> NodeManagerFactories => m_nodeManagerFactories;

        /// <inheritdoc/>
        public IEnumerable<IAsyncNodeManagerFactory> AsyncNodeManagerFactories
            => m_asyncNodeManagerFactories;

        /// <inheritdoc/>
        public virtual void AddNodeManager(INodeManagerFactory nodeManagerFactory)
        {
            m_nodeManagerFactories.Add(nodeManagerFactory);
        }

        /// <inheritdoc/>
        public virtual void AddNodeManager(IAsyncNodeManagerFactory nodeManagerFactory)
        {
            m_asyncNodeManagerFactories.Add(nodeManagerFactory);
        }

        /// <summary>
        /// Stages a historian provider for startup, retaining ownership if any registration requires it.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <c>null</c>.</exception>
        internal void AddHistorianProvider(
            Historian.IHistorianProvider provider,
            bool ownsProvider)
        {
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            for (int ii = 0; ii < m_historianProviders.Count; ii++)
            {
                HistorianProviderRegistration registration =
                    m_historianProviders[ii];
                if (ReferenceEquals(registration.Provider, provider))
                {
                    if (ownsProvider && !registration.OwnsProvider)
                    {
                        m_historianProviders[ii] =
                            registration with { OwnsProvider = true };
                    }
                    return;
                }
            }
            m_historianProviders.Add(
                new HistorianProviderRegistration(provider, ownsProvider));
        }

        /// <summary>
        /// Stages a pre-startup task without registering the same instance more than once.
        /// </summary>
        /// <remarks>
        /// Staged tasks run in staging order once the server context exists and before
        /// the address space is created. Stage them before the server is started;
        /// a task staged later does not run.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="task"/> is <c>null</c>.</exception>
        protected internal void AddPreStartupTask(Hosting.IServerPreStartupTask task)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }
            foreach (Hosting.IServerPreStartupTask existing in m_preStartupTasks)
            {
                if (ReferenceEquals(existing, task))
                {
                    return;
                }
            }
            m_preStartupTasks.Add(task);
        }

        /// <inheritdoc/>
        public virtual void RemoveNodeManager(INodeManagerFactory nodeManagerFactory)
        {
            m_nodeManagerFactories.Remove(nodeManagerFactory);
        }

        /// <inheritdoc/>
        public virtual void RemoveNodeManager(IAsyncNodeManagerFactory nodeManagerFactory)
        {
            m_asyncNodeManagerFactories.Remove(nodeManagerFactory);
        }

        /// <summary>
        /// Reacts to a session channel keep alive event to signal
        /// a listener channel that a session is still active.
        /// </summary>
        private void SessionChannelKeepAliveEvent(ISession session, SessionEventReason reason)
        {
            Debug.Assert(reason == SessionEventReason.ChannelKeepAlive);

            string? secureChannelId = session?.SecureChannelId;
            if (!string.IsNullOrEmpty(secureChannelId))
            {
                ITransportListener? transportListener = TransportListeners.FirstOrDefault(tl =>
                    secureChannelId!.StartsWith(tl.ListenerId, StringComparison.Ordinal));
                transportListener?.UpdateChannelLastActiveTime(secureChannelId!);
            }
        }

        private OperationLimitsState OperationLimits
            => ServerInternal.ServerObject.ServerCapabilities!.OperationLimits!;

        private readonly Lock m_shutdownStateLock = new();

        [SuppressMessage(
            "Usage",
            "CA2213:Disposable fields should be disposed",
            Justification = "Disposed exactly once by DisposeBaseResources.")]
        private readonly SemaphoreSlim m_semaphoreSlim = new(1, 1);

        [SuppressMessage(
            "Usage",
            "CA2213:Disposable fields should be disposed",
            Justification = "Disposed by OnServerStoppingAsync while the server is stopped.")]
        private ServerInternalData? m_serverInternal;

        /// <summary>The canonical lifecycle owned by this server and exposed through its public provider.</summary>
        private readonly NodeManagerLifecycle m_nodeManagerLifecycle;

        private Task? m_disposeTask;

        /// <summary>
        /// Reports whether the server disposal task has completed for lifecycle assertions.
        /// </summary>
        internal bool BaseResourcesDisposedForTest
        {
            get
            {
                lock (m_shutdownStateLock)
                {
                    return m_disposeTask is { IsCompleted: true };
                }
            }
        }

        private readonly ConfigurationReload m_configurationReload;
        private readonly SessionEstablishment m_sessionEstablishment;
        private readonly ServerLifecycle m_lifecycle;
        private readonly DiscoveryRegistration m_discoveryRegistration;

        /// <summary>
        /// The LDS registration component (exposed for tests).
        /// </summary>
        internal DiscoveryRegistration DiscoveryRegistration => m_discoveryRegistration;
        private int m_minNonceLength;
        private readonly List<INodeManagerFactory> m_nodeManagerFactories = [];
        private readonly List<IAsyncNodeManagerFactory> m_asyncNodeManagerFactories = [];

        private readonly List<HistorianProviderRegistration>
            m_historianProviders = [];

        private readonly List<Hosting.IServerPreStartupTask> m_preStartupTasks = [];
        private readonly List<IUserTokenAuthenticator> m_preStartAuthenticators = [];
        private readonly List<IIdentityAugmenter> m_preStartIdentityAugmenters = [];

        private ServerRateLimitOptions? m_rateLimitOptions;
        private readonly SessionAdmission m_admission;
        private readonly DiscoveryServices m_discoveryServices;
        private readonly ILogger m_eventLogger;

        private readonly record struct HistorianProviderRegistration(
            Historian.IHistorianProvider Provider,
            bool OwnsProvider);

        /// <summary>
        /// The interval at which the <see cref="ConfigurationNodeManager"/>
        /// re-evaluates the certificate-expiration and TrustList-staleness
        /// alarms (OPC 10000-12 §7.8.3) once the server is running.
        /// </summary>
        private static readonly TimeSpan kCertificateAlarmMonitoringInterval = TimeSpan.FromSeconds(60);

        private sealed class CertificateManagerChangeObserver : IObserver<CertificateChangeEvent>
        {
            private readonly StandardServer _server;
            private readonly ILogger _logger;

            public CertificateManagerChangeObserver(StandardServer server, ILogger logger)
            {
                _server = server;
                _logger = logger;
            }

            public void OnNext(CertificateChangeEvent value)
            {
                if (value.Kind == CertificateChangeKind.ApplicationCertificateUpdated)
                {
                    _logger.CertificateManagerApplicationCertificateUpdated(value.CertificateType);

                    // Fan-out the cert update to endpoint descriptions and
                    // transport listeners. The reload itself was performed by
                    // CertificateManager.UpdateAsync /
                    // ReloadApplicationCertificatesAsync before this notification
                    // fired, so we only need to refresh downstream consumers.
                    try
                    {
                        ICertificateValidatorEx validator = _server.CertificateManager!;
                        var args = new CertificateUpdateEventArgs(
                            _server.Configuration?.SecurityConfiguration!,
                            validator);
                        _server.OnCertificateUpdateAsync(_server, args);
                    }
                    catch (Exception ex)
                    {
                        _logger.CertificateManagerChangeObserverFailedToFanOut(ex);
                    }
                }
            }

            public void OnError(Exception error)
            {
            }

            public void OnCompleted()
            {
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for StandardServer.
    /// </summary>
    internal static partial class StandardServerLog
    {
        /// <summary>
        /// Logs a client endpoint URL that does not match the server's hostnames.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 0, Level = LogLevel.Warning,
            Message = "Server - Client connects with an endpointUrl [{EndpointUrl}] which does not match " +
                "Server hostnames.")]
        public static partial void ServerClientConnectsWithAnEndpointUrlEndpointUrl(
            this ILogger logger,
            [NetworkAddress] string? endpointUrl);

        /// <summary>
        /// Logs successful session creation and the assigned session identifier.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 1, Level = LogLevel.Information,
            Message = "Server - SESSION CREATED. SessionId={SessionId}")]
        public static partial void ServerSESSIONCREATEDSessionIdSessionId(this ILogger logger, NodeId sessionId);

        /// <summary>
        /// Logs a session creation failure and its error message.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 2, Level = LogLevel.Error,
            Message = "Server - SESSION CREATE failed. {ErrorMessage}")]
        public static partial void ServerSESSIONCREATEFailedErrorMessage(
            this ILogger logger,
            [SensitiveText] string? errorMessage);

        /// <summary>
        /// Logs successful activation of a session.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 3, Level = LogLevel.Information,
            Message = "Server - SESSION ACTIVATED. SessionId={SessionId}")]
        public static partial void ServerSESSIONACTIVATED(this ILogger logger, NodeId? sessionId);

        /// <summary>
        /// Logs a session activation failure and its error message.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 4, Level = LogLevel.Information,
            Message = "Server - SESSION ACTIVATE failed. SessionId={SessionId}, {ErrorMessage}")]
        public static partial void ServerSESSIONACTIVATEFailedErrorMessage(
            this ILogger logger,
            NodeId? sessionId,
            [SensitiveText] string? errorMessage);

        /// <summary>
        /// Logs receipt of a Publish request with its request handle, session, and timestamp.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 5, Level = LogLevel.Trace,
            Message = "PUBLISH #{RequestHandle} RECEIVED. TIME={Timestamp:hh:mm:ss.fff}, SessionId={SessionId}")]
        public static partial void PUBLISHRequestHandleRECEIVEDTIMETimestampHhMm(
            this ILogger logger,
            NodeId? sessionId,
            uint requestHandle,
            DateTimeUtc timestamp);

        /// <summary>
        /// Logs a discovery server registration API failure for an endpoint.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 6, Level = LogLevel.Warning,
            Message = "RegisterServer{Api} failed for {EndpointUrl}. Exception={ErrorMessage}")]
        public static partial void RegisterServerApiFailedForEndpointUrlException(
            this ILogger logger,
            string? api,
            [NetworkAddress] Uri? endpointUrl,
            [SensitiveText] string? errorMessage);

        /// <summary>
        /// Logs a failure to close the local discovery server connection cleanly.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 7, Level = LogLevel.Warning,
            Message = "Could not cleanly close connection with LDS. Exception={ErrorMessage}")]
        public static partial void NotCleanlyCloseConnectionWithLDSException(
            this ILogger logger,
            [SensitiveText] string? errorMessage);

        /// <summary>
        /// Logs successful discovery registration and the interval until the next registration.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 8, Level = LogLevel.Information,
            Message = "Register server succeeded. Registering again in {RegistrationInterval} ms")]
        public static partial void RegisterServerSucceededRegisteringAgainInRegistrationInterval(
            this ILogger logger,
            double registrationInterval);

        /// <summary>
        /// Logs failed discovery registration and the interval until the next attempt.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 9, Level = LogLevel.Information,
            Message = "Register server failed. Trying again in {RegistrationInterval} ms")]
        public static partial void RegisterServerFailedTryingAgainInRegistrationInterval(
            this ILogger logger,
            double registrationInterval);

        /// <summary>
        /// Logs an unexpected exception in the discovery registration timer callback.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 10, Level = LogLevel.Error,
            Message = "Unexpected exception handling registration timer.")]
        public static partial void UnexpectedExceptionHandlingRegistrationTimer(this ILogger logger, Exception ex);

        /// <summary>
        /// Logs a transition into the specified server state.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 11, Level = LogLevel.Information,
            Message = "Server - Enter {State} state.")]
        public static partial void ServerEnterStateState(this ILogger logger, ServerState state);

        /// <summary>
        /// Logs a server service call using the retained EventSource-compatible event identity.
        /// </summary>
        [LoggerMessage(
            EventId = ServerCompatibilityEventIds.ServerCall,
            EventName = "ServerCall",
            Level = LogLevel.Information,
            Message = "Server Call={RequestType}, Id={RequestId}, SessionId={SessionId}")]
        public static partial void CompatibilityServerCall(
            this ILogger logger,
            string requestType,
            uint requestId,
            NodeId? sessionId);

        /// <summary>
        /// Logs a failure to load an updated application configuration file.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 13, Level = LogLevel.Error,
            Message = "Could not load updated configuration file from: {FilePath}")]
        public static partial void NotLoadUpdatedConfigurationFileFromFilePath(
            this ILogger logger,
            Exception ex,
            [FilePath] string? filePath);

        /// <summary>
        /// Logs a failure to apply an application configuration update.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 14, Level = LogLevel.Error,
            Message = "Failed to update configuration.")]
        public static partial void FailedToUpdateConfiguration(this ILogger logger, Exception ex);

        /// <summary>
        /// Logs startup of the named server application.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 15, Level = LogLevel.Information,
            Message = "Server - Start application {ApplicationName}.")]
        public static partial void ServerStartApplicationApplicationName(this ILogger logger, string? applicationName);

        /// <summary>
        /// Logs creation of the server resource manager.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 16, Level = LogLevel.Information,
            Message = "Server - CreateResourceManager.")]
        public static partial void ServerCreateResourceManager(this ILogger logger);

        /// <summary>
        /// Logs creation of the server request manager.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 17, Level = LogLevel.Information,
            Message = "Server - CreateRequestManager.")]
        public static partial void ServerCreateRequestManager(this ILogger logger);

        /// <summary>
        /// Logs creation of the master node manager.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 18, Level = LogLevel.Information,
            Message = "Server - CreateMasterNodeManager.")]
        public static partial void ServerCreateMasterNodeManager(this ILogger logger);

        /// <summary>
        /// Logs creation of the server event manager.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 19, Level = LogLevel.Information,
            Message = "Server - CreateEventManager.")]
        public static partial void ServerCreateEventManager(this ILogger logger);

        /// <summary>
        /// Logs creation of the server aggregate manager.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 20, Level = LogLevel.Information,
            Message = "Server - CreateAggregateManager.")]
        public static partial void ServerCreateAggregateManager(this ILogger logger);

        /// <summary>
        /// Logs creation of the server modelling rules manager.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 21, Level = LogLevel.Information,
            Message = "Server - CreateModellingRulesManager.")]
        public static partial void ServerCreateModellingRulesManager(this ILogger logger);

        /// <summary>
        /// Logs creation of the server conformance units manager.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 22, Level = LogLevel.Information,
            Message = "Server - CreateConformanceUnitsManager.")]
        public static partial void ServerCreateConformanceUnitsManager(this ILogger logger);

        /// <summary>
        /// Logs creation of the server session manager.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 23, Level = LogLevel.Information,
            Message = "Server - CreateSessionManager.")]
        public static partial void ServerCreateSessionManager(this ILogger logger);

        /// <summary>
        /// Logs creation of the server subscription manager.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 24, Level = LogLevel.Information,
            Message = "Server - CreateSubscriptionManager.")]
        public static partial void ServerCreateSubscriptionManager(this ILogger logger);

        /// <summary>
        /// Logs startup of the discovery registration timer.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 25, Level = LogLevel.Information,
            Message = "Server - Registration Timer started.")]
        public static partial void ServerRegistrationTimerStarted(this ILogger logger);

        /// <summary>
        /// Logs a critical server failure with its exception and explanatory message.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 26, Level = LogLevel.Critical,
            Message = "{Message}")]
        public static partial void Message(this ILogger logger, Exception ex, string? message);

        /// <summary>
        /// Logs completion of server startup.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 27, Level = LogLevel.Information,
            Message = "Server - Started.")]
        public static partial void ServerStarted(this ILogger logger);

        /// <summary>
        /// Logs startup of the application configuration watcher.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 28, Level = LogLevel.Information,
            Message = "Server - Configuration watcher started.")]
        public static partial void ServerConfigurationWatcherStarted(this ILogger logger);

        /// <summary>
        /// Logs certificate manager initialization and the number of managed trust lists.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 29, Level = LogLevel.Information,
            Message = "CertificateManager initialized with {Count} trust lists.")]
        public static partial void CertificateManagerInitializedWithCountTrustLists(this ILogger logger, int count);

        /// <summary>
        /// Logs the start of server shutdown.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 30, Level = LogLevel.Information,
            Message = "Server - Stopping.")]
        public static partial void ServerStopping(this ILogger logger);

        /// <summary>
        /// Logs the active session count and remaining time before shutdown.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 31, Level = LogLevel.Information,
            Message = "{SessionCount} active sessions. Seconds until shutdown: {TimeTillShutdown}s")]
        public static partial void SessionCountActiveSessionsSecondsUntilShutdown(
            this ILogger logger,
            int sessionCount,
            int timeTillShutdown);

        /// <summary>
        /// Logs an application certificate update for the specified certificate type.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 32, Level = LogLevel.Information,
            Message = "CertificateManager: Application certificate updated for type {CertType}.")]
        public static partial void CertificateManagerApplicationCertificateUpdated(
            this ILogger logger,
            NodeId? certType);

        /// <summary>
        /// Logs a failure to propagate a certificate manager update to the server.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 33, Level = LogLevel.Error,
            Message = "CertificateManager change observer failed to fan-out cert update.")]
        public static partial void CertificateManagerChangeObserverFailedToFanOut(this ILogger logger, Exception ex);

        /// <summary>
        /// Logs a resource disposal failure during server shutdown.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 35, Level = LogLevel.Error,
            Message = "Server shutdown resource disposal failed. {ErrorMessage}")]
        public static partial void ServerShutdownResourceDisposalFailed(
            this ILogger logger,
            Exception ex,
            [SensitiveText] string? errorMessage);

        /// <summary>
        /// Logs a failure to audit or count a rejected CreateSession.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 36, Level = LogLevel.Warning,
            Message = "Server - reporting a rejected CreateSession failed; the rejection stands.")]
        public static partial void ReportingRejectedCreateSessionFailed(this ILogger logger, Exception ex);

        /// <summary>
        /// Logs a failed best-effort step after an ActivateSession committed.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 38, Level = LogLevel.Warning,
            Message = "Server - ActivateSession post-commit step failed for session {SessionId}; the activation stands.")]
        public static partial void ActivateSessionPostCommitStepFailed(
            this ILogger logger,
            Exception ex,
            NodeId sessionId);

        /// <summary>
        /// Logs a failure to release a manager created by a failed startup.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.StandardServer + 39, Level = LogLevel.Warning,
            Message = "Server - releasing a manager created by the failed startup failed.")]
        public static partial void ReleasingManagerOfFailedStartupFailed(this ILogger logger, Exception ex);
    }
}
