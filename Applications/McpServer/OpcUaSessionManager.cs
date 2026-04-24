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
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Manages the OPC UA client session lifecycle for the MCP server.
    /// </summary>
    public sealed class OpcUaSessionManager : IDisposable
    {
        private const string kApplicationName = "OPC UA MCP Server";
        private const string kConfigSectionName = "Opc.Ua.Mcp";
        private const string kConfigFileName = "Opc.Ua.Mcp.Config.xml";

        private readonly ILogger<OpcUaSessionManager> m_logger;
        private readonly SemaphoreSlim m_lock = new(1, 1);
        private readonly ITelemetryContext m_telemetry = new NullTelemetry();
        private ApplicationConfiguration? m_configuration;
        private Client.ISession? m_session;
        private SessionReconnectHandler? m_reconnectHandler;
        private bool m_disposed;

        public OpcUaSessionManager(ILogger<OpcUaSessionManager> logger)
        {
            m_logger = logger;
        }

        /// <summary>
        /// Gets the current session, or null if not connected.
        /// </summary>
        public Client.ISession? Session => m_session;

        /// <summary>
        /// Gets the telemetry context used by this session manager.
        /// </summary>
        public ITelemetryContext Telemetry => m_telemetry;

        /// <summary>
        /// Gets the loaded application configuration, or null if not yet loaded.
        /// </summary>
        public ApplicationConfiguration? Configuration => m_configuration;

        /// <summary>
        /// Ensures the application configuration is loaded, loading it if necessary.
        /// </summary>
        public async Task<ApplicationConfiguration> EnsureConfigurationAsync(CancellationToken ct = default)
        {
            if (m_configuration != null)
            {
                return m_configuration;
            }

            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await EnsureConfigurationInternalAsync(false, ct).ConfigureAwait(false);
                return m_configuration!;
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Gets whether a session is currently connected.
        /// </summary>
        public bool IsConnected => m_session?.Connected == true;

        /// <summary>
        /// Gets the current session, throwing if not connected.
        /// </summary>
        /// <exception cref="InvalidOperationException">Not connected to an OPC UA server.</exception>
        public Client.ISession GetSessionOrThrow()
        {
            Client.ISession? session = m_session;
            if (session == null || !session.Connected)
            {
                throw new InvalidOperationException(
                    "Not connected to an OPC UA server. Use the 'Connect' tool first.");
            }
            return session;
        }

        /// <summary>
        /// Discovers all endpoints available at the given discovery URL.
        /// </summary>
        public async Task<ArrayOf<EndpointDescription>> DiscoverEndpointsAsync(
            string discoveryUrl,
            CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);

            await EnsureConfigurationInternalAsync(false, ct).ConfigureAwait(false);

            var uri = new Uri(discoveryUrl);
            var endpointConfiguration = EndpointConfiguration.Create(m_configuration!);

            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                m_configuration!,
                uri,
                endpointConfiguration,
                ct: ct).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(default, ct)
                .ConfigureAwait(false);

            return endpoints;
        }

        /// <summary>
        /// Connects to an OPC UA server with endpoint selection and authentication options.
        /// </summary>
        public async Task<string> ConnectAsync(
            string endpointUrl,
            string? securityMode,
            string? securityPolicy,
            string authType,
            string? username,
            string? password,
            bool autoAcceptCerts,
            CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);

            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_session?.Connected == true)
                {
                    await DisconnectInternalAsync(ct).ConfigureAwait(false);
                }

                await EnsureConfigurationInternalAsync(autoAcceptCerts, ct).ConfigureAwait(false);

                if (autoAcceptCerts)
                {
                    m_configuration!.CertificateValidator.CertificateValidation -= AutoAcceptCertificateValidation;
                    m_configuration.CertificateValidator.CertificateValidation += AutoAcceptCertificateValidation;
                }

                m_logger.LogInformation("Connecting to {EndpointUrl}...", endpointUrl);

                EndpointDescription selectedEndpoint = await SelectEndpointAsync(
                    endpointUrl, securityMode, securityPolicy, authType, ct).ConfigureAwait(false);

#pragma warning disable CA2000 // Dispose objects before losing scope
                UserIdentity identity = BuildUserIdentity(authType, username, password);
#pragma warning restore CA2000 // Dispose objects before losing scope

                var endpointConfiguration = EndpointConfiguration.Create(m_configuration!);
                var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);

                var sessionFactory = new DefaultSessionFactory(m_telemetry);
                Client.ISession session = await sessionFactory.CreateAsync(
                    m_configuration!,
                    endpoint,
                    true,
                    false,
                    m_configuration!.ApplicationName,
                    60_000,
                    identity,
                    default,
                    ct).ConfigureAwait(false);

                if (session?.Connected == true)
                {
                    m_session = session;
                    session.KeepAliveInterval = 5000;
                    session.KeepAlive += SessionKeepAlive;

                    m_reconnectHandler = new SessionReconnectHandler(m_telemetry, true, 15_000);

                    m_logger.LogInformation(
                        "Connected. SessionName={SessionName}, SessionId={SessionId}",
                        session.SessionName,
                        session.SessionId);

                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "Connected to {0}. SecurityMode={1}, SecurityPolicy={2}, Auth={3}, " +
                        "SessionName={4}, SessionId={5}",
                        endpointUrl,
                        selectedEndpoint.SecurityMode,
                        selectedEndpoint.SecurityPolicyUri,
                        authType,
                        session.SessionName,
                        session.SessionId);
                }

                throw new ServiceResultException(StatusCodes.BadConnectionClosed, "Session creation failed.");
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Disconnects the current session.
        /// </summary>
        public async Task<string> DisconnectAsync(CancellationToken ct = default)
        {
            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_session == null || !m_session.Connected)
                {
                    return "No active session to disconnect.";
                }

                string sessionName = m_session.SessionName;
                await DisconnectInternalAsync(ct).ConfigureAwait(false);
                return $"Disconnected session '{sessionName}'.";
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Gets connection status information.
        /// </summary>
        public string GetConnectionStatus()
        {
            Client.ISession? session = m_session;
            if (session == null || !session.Connected)
            {
                return "Not connected.";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "Connected to {0}. SessionName={1}, SessionId={2}, ServerUri={3}",
                session.Endpoint?.EndpointUrl ?? "unknown",
                session.SessionName,
                session.SessionId,
                session.ServerUris?.ToArray().FirstOrDefault() ?? "unknown");
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            m_reconnectHandler?.Dispose();
            m_session?.Dispose();
            m_lock.Dispose();
        }

        private async Task DisconnectInternalAsync(CancellationToken ct)
        {
            if (m_session != null)
            {
                m_session.KeepAlive -= SessionKeepAlive;
                m_reconnectHandler?.Dispose();
                m_reconnectHandler = null;

                await m_session.CloseAsync(ct).ConfigureAwait(false);
                m_session.Dispose();
                m_session = null;
            }
        }

        private async Task EnsureConfigurationInternalAsync(bool autoAcceptCerts, CancellationToken ct)
        {
            if (m_configuration != null)
            {
                return;
            }

#pragma warning disable CA2000 // Dispose objects before losing scope
            var application = new ApplicationInstance(m_telemetry)
            {
                ApplicationName = kApplicationName,
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = kConfigSectionName
            };
#pragma warning restore CA2000 // Dispose objects before losing scope

            string configFile = Path.Combine(AppContext.BaseDirectory, kConfigFileName);
            ApplicationConfiguration config;
            if (File.Exists(configFile))
            {
                config = await application.LoadApplicationConfigurationAsync(configFile, false, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                config = await application.LoadApplicationConfigurationAsync(false, ct)
                    .ConfigureAwait(false);
            }

            bool hasAppCert = await application.CheckApplicationInstanceCertificatesAsync(false, ct: ct)
                .ConfigureAwait(false);
            if (!hasAppCert)
            {
                m_logger.LogWarning("Application certificate not found. Security may be limited.");
            }

            if (autoAcceptCerts)
            {
                config.CertificateValidator.CertificateValidation += AutoAcceptCertificateValidation;
            }

            m_configuration = config;
        }

        private async Task<EndpointDescription> SelectEndpointAsync(
            string endpointUrl,
            string? securityMode,
            string? securityPolicy,
            string authType,
            CancellationToken ct)
        {
            bool hasFilter = securityMode != null || securityPolicy != null;

            if (!hasFilter)
            {
                // Auto-select most secure, fall back to no-security
                EndpointDescription? best = await CoreClientUtils.SelectEndpointAsync(
                    m_configuration!,
                    endpointUrl,
                    true,
                    m_telemetry,
                    ct: ct).ConfigureAwait(false);

                if (best == null)
                {
                    best = await CoreClientUtils.SelectEndpointAsync(
                        m_configuration!,
                        endpointUrl,
                        false,
                        m_telemetry,
                        ct: ct).ConfigureAwait(false);
                }

                if (best == null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotFound,
                        "No endpoints found at the specified URL.");
                }

                return best;
            }

            ArrayOf<EndpointDescription> allEndpoints =
                await DiscoverEndpointsAsync(endpointUrl, ct).ConfigureAwait(false);

            IEnumerable<EndpointDescription> candidates = allEndpoints.ToArray() ??
                Array.Empty<EndpointDescription>();

            if (securityMode != null)
            {
                MessageSecurityMode mode = ParseSecurityMode(securityMode);
                candidates = candidates.Where(ep => ep.SecurityMode == mode);
            }

            if (securityPolicy != null)
            {
                candidates = candidates.Where(ep =>
                    ep.SecurityPolicyUri != null &&
                    ep.SecurityPolicyUri.EndsWith(
                        "#" + securityPolicy, StringComparison.OrdinalIgnoreCase));
            }

            // Filter by auth compatibility
            UserTokenType requiredTokenType = ParseAuthTokenType(authType);
            candidates = candidates.Where(ep =>
                (ep.UserIdentityTokens.ToArray() ?? Array.Empty<UserTokenPolicy>())
                    .Any(t => t.TokenType == requiredTokenType));

            EndpointDescription? selected = candidates
                .OrderByDescending(ep => ep.SecurityLevel)
                .FirstOrDefault();

            if (selected == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "No endpoint matches the specified criteria: securityMode={0}, " +
                        "securityPolicy={1}, authType={2}. " +
                        "Use GetEndpoints to see available configurations.",
                        securityMode ?? "(any)",
                        securityPolicy ?? "(any)",
                        authType));
            }

            return selected;
        }

        private static MessageSecurityMode ParseSecurityMode(string securityMode)
        {
            return securityMode.ToUpperInvariant() switch
            {
                "NONE" => MessageSecurityMode.None,
                "SIGN" => MessageSecurityMode.Sign,
                "SIGNANDENCRYPT" => MessageSecurityMode.SignAndEncrypt,
                _ => throw new ArgumentException(
                    $"Invalid securityMode '{securityMode}'. " +
                    "Expected 'None', 'Sign', or 'SignAndEncrypt'.",
                    nameof(securityMode))
            };
        }

        private static UserTokenType ParseAuthTokenType(string authType)
        {
            return authType.ToUpperInvariant() switch
            {
                "ANONYMOUS" => UserTokenType.Anonymous,
                "USERNAME" => UserTokenType.UserName,
                "CERTIFICATE" => UserTokenType.Certificate,
                _ => throw new ArgumentException(
                    $"Invalid authType '{authType}'. " +
                    "Expected 'Anonymous', 'Username', or 'Certificate'.",
                    nameof(authType))
            };
        }

        private static UserIdentity BuildUserIdentity(
            string authType,
            string? username,
            string? password)
        {
            switch (authType.ToUpperInvariant())
            {
                case "ANONYMOUS":
                    return new UserIdentity();

                case "USERNAME":
                    if (string.IsNullOrEmpty(username))
                    {
                        throw new ArgumentException(
                            "Username is required for 'Username' authentication.",
                            nameof(username));
                    }

                    return new UserIdentity(
                        username,
                        System.Text.Encoding.UTF8.GetBytes(password ?? string.Empty));

                case "CERTIFICATE":
                    throw new NotSupportedException(
                        "Certificate authentication is not yet supported through the MCP " +
                        "Connect tool. Certificate auth requires certificate store " +
                        "configuration which is beyond the scope of MCP tool parameters. " +
                        "Use 'Anonymous' or 'Username' authentication instead.");

                default:
                    throw new ArgumentException(
                        $"Invalid authType '{authType}'. " +
                        "Expected 'Anonymous', 'Username', or 'Certificate'.",
                        nameof(authType));
            }
        }

        private void SessionKeepAlive(Client.ISession session, KeepAliveEventArgs e)
        {
            if (e.Status != null && ServiceResult.IsNotGood(e.Status))
            {
                m_logger.LogWarning(
                    "KeepAlive status: {Status}. Reconnecting...",
                    e.Status);

                if (m_reconnectHandler != null && session is Session s)
                {
                    m_reconnectHandler.BeginReconnect(s, 1000, SessionReconnectComplete);
                }
            }
        }

        private void SessionReconnectComplete(object? sender, EventArgs e)
        {
            if (sender is not SessionReconnectHandler handler)
            {
                return;
            }

            Client.ISession? session = handler.Session;
            if (session != null)
            {
                m_session = session;
                m_logger.LogInformation("Session reconnected. SessionId={SessionId}", session.SessionId);
            }
        }

        private static void AutoAcceptCertificateValidation(
            CertificateValidator sender,
            CertificateValidationEventArgs e)
        {
            e.Accept = true;
        }

        private sealed class NullTelemetry : ITelemetryContext
        {
            public ILoggerFactory LoggerFactory { get; } = NullLoggerFactory.Instance;

            public Meter CreateMeter()
            {
                return new("Opc.Ua.Mcp");
            }

            public ActivitySource ActivitySource { get; } = new("Opc.Ua.Mcp");
        }
    }
}
