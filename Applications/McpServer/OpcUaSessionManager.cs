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
using System.Collections.Concurrent;
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
        private readonly ConcurrentDictionary<string, SessionInfo> m_sessions = new(StringComparer.OrdinalIgnoreCase);
        private ApplicationConfiguration? m_configuration;
        private bool m_disposed;

        public OpcUaSessionManager(ILogger<OpcUaSessionManager> logger)
        {
            m_logger = logger;
        }

        /// <summary>
        /// Holds information about a named OPC UA session.
        /// </summary>
        public sealed class SessionInfo
        {
            public required string Name { get; init; }
            public required Client.ISession Session { get; init; }
            public required EndpointDescription Endpoint { get; init; }
            public required string AuthType { get; init; }
            public SessionReconnectHandler? ReconnectHandler { get; set; }
            public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;
            public bool IsConnected => Session.Connected;
        }

        /// <summary>
        /// Gets the first session, or null if none connected. For backward compatibility.
        /// </summary>
        public Client.ISession? Session => m_sessions.Values.FirstOrDefault()?.Session;

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
        /// Gets whether any session is currently connected.
        /// </summary>
        public bool IsConnected => m_sessions.Values.Any(s => s.IsConnected);

        /// <summary>
        /// Gets a session by name, or the only active session if name is null.
        /// </summary>
        /// <exception cref="InvalidOperationException">Not connected or ambiguous session.</exception>
        public Client.ISession GetSessionOrThrow(string? name = null)
        {
            if (name != null)
            {
                if (m_sessions.TryGetValue(name, out SessionInfo? info) && info.IsConnected)
                {
                    return info.Session;
                }

                throw new InvalidOperationException(
                    $"Session '{name}' not found or not connected. Use the 'Connect' tool first.");
            }

            int count = m_sessions.Count;
            if (count == 0)
            {
                throw new InvalidOperationException(
                    "Not connected to an OPC UA server. Use the 'Connect' tool first.");
            }

            if (count == 1)
            {
                SessionInfo single = m_sessions.Values.First();
                if (single.IsConnected)
                {
                    return single.Session;
                }

                throw new InvalidOperationException(
                    "Not connected to an OPC UA server. Use the 'Connect' tool first.");
            }

            string names = string.Join(", ", m_sessions.Keys);
            throw new InvalidOperationException(
                $"Multiple sessions active. Specify a sessionName: {names}");
        }

        /// <summary>
        /// Gets all active sessions.
        /// </summary>
        public IReadOnlyCollection<SessionInfo> GetAllSessions() =>
            m_sessions.Values.ToList().AsReadOnly();

        /// <summary>
        /// Gets information about a specific named session.
        /// </summary>
        public SessionInfo? GetSessionInfo(string name) =>
            m_sessions.GetValueOrDefault(name);

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

            return await client.GetEndpointsAsync(default, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Connects to an OPC UA server with endpoint selection and authentication options.
        /// </summary>
        public async Task<string> ConnectAsync(
            string? name,
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
                // Auto-generate name from endpoint URL if not specified
                if (string.IsNullOrWhiteSpace(name))
                {
                    var uri = new Uri(endpointUrl);
                    name = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}-{1}",
                        uri.Host,
                        uri.Port);
                }

                // Disconnect existing session with same name
                if (m_sessions.TryGetValue(name, out SessionInfo? existing))
                {
                    await DisconnectInternalAsync(existing, ct).ConfigureAwait(false);
                    m_sessions.TryRemove(name, out _);
                }

                await EnsureConfigurationInternalAsync(autoAcceptCerts, ct).ConfigureAwait(false);

                if (autoAcceptCerts)
                {
                    m_configuration!.CertificateValidator!.CertificateValidation -= AutoAcceptCertificateValidation;
                    m_configuration.CertificateValidator!.CertificateValidation += AutoAcceptCertificateValidation;
                }

                m_logger.LogInformation("Connecting to {EndpointUrl} as '{Name}'...", endpointUrl, name);

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
                    m_configuration!.ApplicationName!,
                    60_000,
                    identity,
                    default,
                    ct).ConfigureAwait(false);

                if (session?.Connected == true)
                {
                    var reconnectHandler = new SessionReconnectHandler(m_telemetry, true, 15_000);
                    var sessionInfo = new SessionInfo
                    {
                        Name = name,
                        Session = session,
                        Endpoint = selectedEndpoint,
                        AuthType = authType,
                        ReconnectHandler = reconnectHandler,
                        ConnectedAt = DateTime.UtcNow
                    };

                    m_sessions[name] = sessionInfo;

                    session.KeepAliveInterval = 5000;
                    session.KeepAlive += (s, e) => SessionKeepAlive(sessionInfo, s, e);

                    m_logger.LogInformation(
                        "Connected '{Name}'. SessionName={SessionName}, SessionId={SessionId}",
                        name,
                        session.SessionName,
                        session.SessionId);

                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "Connected to {0} as '{1}'. SecurityMode={2}, SecurityPolicy={3}, Auth={4}, " +
                        "SessionName={5}, SessionId={6}",
                        endpointUrl,
                        name,
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
        /// Disconnects a named session, or the only session if name is null.
        /// </summary>
        public async Task<string> DisconnectAsync(string? name = null, CancellationToken ct = default)
        {
            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (name != null)
                {
                    if (!m_sessions.TryRemove(name, out SessionInfo? info))
                    {
                        return $"Session '{name}' not found.";
                    }

                    await DisconnectInternalAsync(info, ct).ConfigureAwait(false);
                    return $"Disconnected session '{name}'.";
                }

                int count = m_sessions.Count;
                if (count == 0)
                {
                    return "No active sessions to disconnect.";
                }

                if (count > 1)
                {
                    string names = string.Join(", ", m_sessions.Keys);
                    return $"Multiple sessions active. Specify a session name to disconnect: {names}";
                }

                // Exactly one session
                KeyValuePair<string, SessionInfo> single = m_sessions.First();
                m_sessions.TryRemove(single.Key, out _);
                await DisconnectInternalAsync(single.Value, ct).ConfigureAwait(false);
                return $"Disconnected session '{single.Key}'.";
            }
            finally
            {
                m_lock.Release();
            }
        }

        /// <summary>
        /// Gets connection status information for one or all sessions.
        /// </summary>
        public string GetConnectionStatus(string? name = null)
        {
            if (name != null)
            {
                if (!m_sessions.TryGetValue(name, out SessionInfo? info))
                {
                    return $"Session '{name}' not found.";
                }

                return FormatSessionStatus(info);
            }

            if (m_sessions.IsEmpty)
            {
                return "Not connected.";
            }

            var lines = new List<string>();
            foreach (SessionInfo info in m_sessions.Values)
            {
                lines.Add(FormatSessionStatus(info));
            }

            return string.Join(Environment.NewLine, lines);
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            foreach (SessionInfo info in m_sessions.Values)
            {
                info.ReconnectHandler?.Dispose();
                info.Session.Dispose();
            }

            m_sessions.Clear();
            m_lock.Dispose();
        }

        private static async Task DisconnectInternalAsync(SessionInfo info, CancellationToken ct)
        {
            info.ReconnectHandler?.Dispose();
            info.ReconnectHandler = null;

            await info.Session.CloseAsync(ct).ConfigureAwait(false);
            info.Session.Dispose();
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
                config.CertificateValidator!.CertificateValidation += AutoAcceptCertificateValidation;
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

            // Filter to match the same transport scheme as the requested URL
            var requestUri = new Uri(endpointUrl);
            candidates = candidates.Where(ep =>
                ep.EndpointUrl != null &&
                ep.EndpointUrl.StartsWith(requestUri.Scheme, StringComparison.OrdinalIgnoreCase));

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

        private void SessionKeepAlive(SessionInfo info, Client.ISession session, KeepAliveEventArgs e)
        {
            if (e.Status != null && ServiceResult.IsNotGood(e.Status))
            {
                m_logger.LogWarning(
                    "KeepAlive status for '{Name}': {Status}. Reconnecting...",
                    info.Name,
                    e.Status);

                if (info.ReconnectHandler != null && session is Session s)
                {
                    info.ReconnectHandler.BeginReconnect(s, 1000, (sender, _) =>
                    {
                        SessionReconnectComplete(info, sender);
                    });
                }
            }
        }

        private void SessionReconnectComplete(SessionInfo info, object? sender)
        {
            if (sender is not SessionReconnectHandler handler)
            {
                return;
            }

            Client.ISession? session = handler.Session;
            if (session != null)
            {
                // Update the session in the dictionary with a new SessionInfo
                m_sessions[info.Name] = new SessionInfo
                {
                    Name = info.Name,
                    Session = session,
                    Endpoint = info.Endpoint,
                    AuthType = info.AuthType,
                    ReconnectHandler = info.ReconnectHandler,
                    ConnectedAt = info.ConnectedAt
                };
                m_logger.LogInformation(
                    "Session '{Name}' reconnected. SessionId={SessionId}",
                    info.Name,
                    session.SessionId);
            }
        }

        private static string FormatSessionStatus(SessionInfo info)
        {
            if (!info.IsConnected)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "'{0}': Disconnected (was {1})",
                    info.Name,
                    info.Endpoint.EndpointUrl);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "'{0}': Connected to {1}. SessionName={2}, SessionId={3}, ServerUri={4}",
                info.Name,
                info.Endpoint.EndpointUrl,
                info.Session.SessionName,
                info.Session.SessionId,
                info.Session.ServerUris?.ToArray().FirstOrDefault() ?? "unknown");
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
