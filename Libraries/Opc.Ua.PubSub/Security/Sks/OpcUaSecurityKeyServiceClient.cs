/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// <see cref="ISecurityKeyService"/> implementation that talks
    /// to a Security Key Service over OPC UA. Opens a single
    /// <see cref="ManagedSession"/> against the configured endpoint
    /// on first use and reuses it across calls; raises
    /// <see cref="AvailabilityChanged"/> on connectivity transitions
    /// so callers can move WriterGroups / ReaderGroups into the
    /// correct PubSubState without coupling to transport details.
    /// </summary>
    /// <remarks>
    /// Implements the SKS pull profile defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3.2">
    /// Part 14 §8.3.2 GetSecurityKeys</see>. The underlying call
    /// targets the well-known
    /// <c>ObjectIds.PublishSubscribe</c> Object and the
    /// <c>MethodIds.PublishSubscribe_GetSecurityKeys</c> method.
    /// </remarks>
    public sealed class OpcUaSecurityKeyServiceClient : ISecurityKeyService, IAsyncDisposable
    {
        private static readonly NodeId s_objectId = ObjectIds.PublishSubscribe;
        private static readonly NodeId s_methodId = MethodIds.PublishSubscribe_GetSecurityKeys;

        private readonly Func<CancellationToken, ValueTask<ISession>> m_sessionFactory;
        private readonly ILogger m_logger;
#pragma warning disable IDE0052 // Kept for the injected SKS clock; reconnect pacing is implemented by the caller today.
        private readonly TimeProvider m_timeProvider;
#pragma warning restore IDE0052
        private readonly SemaphoreSlim m_sessionGate = new(1, 1);
        private readonly Lock m_stateLock = new();
        private ISession? m_session;
        private bool? m_lastReportedAvailable;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new
        /// <see cref="OpcUaSecurityKeyServiceClient"/> that opens a
        /// fresh <see cref="ManagedSession"/> against
        /// <paramref name="endpoint"/> when first used.
        /// </summary>
        /// <param name="endpoint">SKS endpoint description.</param>
        /// <param name="applicationConfiguration">
        /// Application configuration that owns the certificate
        /// store, transport quotas and security policies.
        /// </param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Time source.</param>
        /// <param name="allowInsecureChannel">
        /// Allows non-encrypted SKS channels. This is unsafe for symmetric keys and is disabled by default.
        /// </param>
        public OpcUaSecurityKeyServiceClient(
            EndpointDescription endpoint,
            ApplicationConfiguration applicationConfiguration,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            bool allowInsecureChannel = false)
            : this(
                CreateDefaultFactory(endpoint, applicationConfiguration, telemetry, allowInsecureChannel),
                telemetry,
                timeProvider)
        {
            if (endpoint is null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            if (applicationConfiguration is null)
            {
                throw new ArgumentNullException(nameof(applicationConfiguration));
            }
        }

        /// <summary>
        /// Internal constructor used by tests to inject a fake
        /// <see cref="ISession"/> factory and exercise the call
        /// translation logic without spinning up a real OPC UA
        /// session.
        /// </summary>
        /// <param name="sessionFactory">
        /// Async factory that creates and connects a session.
        /// </param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Time source.</param>
        internal OpcUaSecurityKeyServiceClient(
            Func<CancellationToken, ValueTask<ISession>> sessionFactory,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            if (sessionFactory is null)
            {
                throw new ArgumentNullException(nameof(sessionFactory));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            m_sessionFactory = sessionFactory;
            m_logger = telemetry.CreateLogger<OpcUaSecurityKeyServiceClient>();
            m_timeProvider = timeProvider;
        }

        /// <inheritdoc/>
        public event EventHandler<SksAvailabilityChangedEventArgs>? AvailabilityChanged;

        /// <inheritdoc/>
        public async ValueTask<SksKeyResponse> GetSecurityKeysAsync(
            SksKeyRequest request,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(request.SecurityGroupId))
            {
                throw new OpcUaSksException(
                    StatusCodes.BadInvalidArgument,
                    "SecurityGroupId must be non-empty.");
            }

            ISession session;
            try
            {
                session = await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OpcUaSksException ex)
            {
                RaiseAvailabilityChanged(false, ex.Status, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                RaiseAvailabilityChanged(
                    false,
                    StatusCodes.BadCommunicationError,
                    ex.Message);
                throw new OpcUaSksException(
                    StatusCodes.BadCommunicationError,
                    "Failed to open SKS session.",
                    ex);
            }

            ArrayOf<Variant> outputArguments;
            try
            {
                outputArguments = await session.CallAsync(
                    s_objectId,
                    s_methodId,
                    cancellationToken,
                    Variant.From(request.SecurityGroupId),
                    Variant.From(request.StartingTokenId),
                    Variant.From(request.RequestedKeyCount))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                RaiseAvailabilityChanged(false, ex.StatusCode, ex.Message);
                throw new OpcUaSksException(
                    ex.StatusCode,
                    $"GetSecurityKeys returned {ex.StatusCode}.",
                    ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RaiseAvailabilityChanged(
                    false,
                    StatusCodes.BadCommunicationError,
                    ex.Message);
                throw new OpcUaSksException(
                    StatusCodes.BadCommunicationError,
                    "GetSecurityKeys call failed.",
                    ex);
            }

            SksKeyResponse response = ParseResponse(outputArguments);
            RaiseAvailabilityChanged(true, StatusCodes.Good, null);
            return response;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            ISession? session;
            lock (m_stateLock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                session = m_session;
                m_session = null;
            }
            if (session is not null)
            {
                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogDebug(
                        ex,
                        "Error disposing SKS session.");
                }
            }
            m_sessionGate.Dispose();
        }

        private async ValueTask<ISession> EnsureSessionAsync(CancellationToken ct)
        {
            ISession? existing;
            lock (m_stateLock)
            {
                existing = m_session;
            }
            if (existing is not null && existing.Connected)
            {
                return existing;
            }
            await m_sessionGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_session is { Connected: true })
                {
                    return m_session;
                }
                if (m_session is not null)
                {
                    try
                    {
                        await m_session.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogDebug(
                            ex,
                            "Error disposing stale SKS session.");
                    }
                    m_session = null;
                }
                ISession session = await m_sessionFactory(ct).ConfigureAwait(false);
                m_session = session ??
                    throw new OpcUaSksException(
                        StatusCodes.BadCommunicationError,
                        "SKS session factory returned null.");
                return m_session;
            }
            finally
            {
                m_sessionGate.Release();
            }
        }

        private void RaiseAvailabilityChanged(bool isAvailable, StatusCode status, string? reason)
        {
            EventHandler<SksAvailabilityChangedEventArgs>? handler = AvailabilityChanged;
            bool shouldRaise;
            lock (m_stateLock)
            {
                shouldRaise = m_lastReportedAvailable != isAvailable;
                m_lastReportedAvailable = isAvailable;
            }
            if (!shouldRaise || handler is null)
            {
                return;
            }
            handler.Invoke(
                this,
                new SksAvailabilityChangedEventArgs(isAvailable, status, reason));
        }

        private static SksKeyResponse ParseResponse(ArrayOf<Variant> outputs)
        {
            if (outputs.Count < 5)
            {
                throw new OpcUaSksException(
                    StatusCodes.BadDecodingError,
                    $"GetSecurityKeys returned {outputs.Count} output arguments; expected 5.");
            }
            if (!outputs[0].TryGetValue(out string? securityPolicyUri) || securityPolicyUri is null)
            {
                throw new OpcUaSksException(
                    StatusCodes.BadDecodingError,
                    "GetSecurityKeys SecurityPolicyUri is missing or not a String.");
            }
            if (!outputs[1].TryGetValue(out uint firstTokenId))
            {
                throw new OpcUaSksException(
                    StatusCodes.BadDecodingError,
                    "GetSecurityKeys FirstTokenId is missing or not a UInt32.");
            }
            if (!outputs[2].TryGetValue(out ArrayOf<ByteString> keys))
            {
                throw new OpcUaSksException(
                    StatusCodes.BadDecodingError,
                    "GetSecurityKeys Keys is missing or not a ByteString[].");
            }
            if (!outputs[3].TryGetValue(out double timeToNextKeyMs))
            {
                throw new OpcUaSksException(
                    StatusCodes.BadDecodingError,
                    "GetSecurityKeys TimeToNextKey is missing or not a Duration.");
            }
            if (!outputs[4].TryGetValue(out double keyLifetimeMs))
            {
                throw new OpcUaSksException(
                    StatusCodes.BadDecodingError,
                    "GetSecurityKeys KeyLifetime is missing or not a Duration.");
            }

            byte[][] packed = new byte[keys.Count][];
            for (int i = 0; i < keys.Count; i++)
            {
                ByteString key = keys[i];
                packed[i] = key.IsNull
                    ? []
                    : key.Span.ToArray();
            }

            if (keyLifetimeMs <= 0)
            {
                throw new OpcUaSksException(
                    StatusCodes.BadDecodingError,
                    $"GetSecurityKeys KeyLifetime is malformed ({keyLifetimeMs} ms); expected a positive Duration.");
            }
            if (timeToNextKeyMs < 0)
            {
                throw new OpcUaSksException(
                    StatusCodes.BadDecodingError,
                    $"GetSecurityKeys TimeToNextKey is malformed ({timeToNextKeyMs} ms); expected a non-negative Duration.");
            }

            var keyLifetime = TimeSpan.FromMilliseconds(keyLifetimeMs);
            var timeToNextKey = TimeSpan.FromMilliseconds(timeToNextKeyMs);
            return new SksKeyResponse(
                securityPolicyUri,
                firstTokenId,
                packed,
                timeToNextKey,
                keyLifetime);
        }

        private static Func<CancellationToken, ValueTask<ISession>> CreateDefaultFactory(
            EndpointDescription endpoint,
            ApplicationConfiguration applicationConfiguration,
            ITelemetryContext telemetry,
            bool allowInsecureChannel)
        {
            if (endpoint is null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            if (applicationConfiguration is null)
            {
                throw new ArgumentNullException(nameof(applicationConfiguration));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (!allowInsecureChannel)
            {
                ValidateSksEndpointSecurity(endpoint);
            }
            return async ct =>
            {
                if (!allowInsecureChannel)
                {
                    ValidateSksEndpointSecurity(endpoint);
                }
                var configuredEndpoint = new ConfiguredEndpoint(
                    null,
                    endpoint,
                    EndpointConfiguration.Create(applicationConfiguration));
                return await new ManagedSessionBuilder(
                        applicationConfiguration,
                        telemetry)
                    .UseEndpoint(configuredEndpoint)
                    .WithSessionName("Opc.Ua.PubSub.Sks")
                    .ConnectAsync(ct)
                    .ConfigureAwait(false);
            };
        }

        private static void ValidateSksEndpointSecurity(EndpointDescription endpoint)
        {
            if (endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityModeRejected,
                    "SKS endpoints must use SignAndEncrypt because GetSecurityKeys returns long-lived symmetric keys.");
            }

            string securityPolicyUri = endpoint.SecurityPolicyUri ?? SecurityPolicies.None;
            if (!IsApprovedSksSecurityPolicy(securityPolicyUri))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityModeRejected,
                    $"SKS endpoint security policy '{securityPolicyUri}' is not approved for GetSecurityKeys.");
            }

            if (!HasNonAnonymousUserToken(endpoint))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityModeRejected,
                    "SKS endpoints must advertise at least one non-anonymous user token policy.");
            }
        }

        private static bool IsApprovedSksSecurityPolicy(string securityPolicyUri)
        {
            return SecurityPolicies.GetInfo(securityPolicyUri) is not null &&
                !string.Equals(securityPolicyUri, SecurityPolicies.None, StringComparison.Ordinal) &&
                !string.Equals(securityPolicyUri, SecurityPolicies.Basic128Rsa15, StringComparison.Ordinal) &&
                !string.Equals(securityPolicyUri, SecurityPolicies.Basic256, StringComparison.Ordinal);
        }

        private static bool HasNonAnonymousUserToken(EndpointDescription endpoint)
        {
            if (endpoint.UserIdentityTokens.IsNull)
            {
                return false;
            }

            for (int i = 0; i < endpoint.UserIdentityTokens.Count; i++)
            {
                if (endpoint.UserIdentityTokens[i].TokenType != UserTokenType.Anonymous)
                {
                    return true;
                }
            }

            return false;
        }

        private void ThrowIfDisposed()
        {
            lock (m_stateLock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(OpcUaSecurityKeyServiceClient));
                }
            }
        }
    }
}
