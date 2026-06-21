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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Gds.Client
{
    public class LocalDiscoveryServerClient : ILocalDiscoveryServerClient
    {
        /// <summary>
        /// Create local discovery client
        /// </summary>
        /// <param name="configuration">Application configuration to use</param>
        /// <param name="diagnosticsMasks">The return diagnostics for all discovery requests</param>
        public LocalDiscoveryServerClient(
            ApplicationConfiguration configuration,
            DiagnosticsMasks diagnosticsMasks = DiagnosticsMasks.None)
            : this(configuration, channelManager: null, sessionFactory: null, diagnosticsMasks)
        {
        }

        /// <summary>
        /// Create local discovery client that acquires discovery channels from a channel manager.
        /// </summary>
        /// <param name="configuration">Application configuration to use.</param>
        /// <param name="channelManager">The channel manager used for discovery requests.</param>
        /// <param name="diagnosticsMasks">The return diagnostics for all discovery requests.</param>
        public LocalDiscoveryServerClient(
            ApplicationConfiguration configuration,
            IClientChannelManager channelManager,
            DiagnosticsMasks diagnosticsMasks = DiagnosticsMasks.None)
            : this(
                  configuration,
                  channelManager ?? throw new ArgumentNullException(nameof(channelManager)),
                  sessionFactory: null,
                  diagnosticsMasks)
        {
        }

        /// <summary>
        /// Create local discovery client that acquires discovery channels from a session factory.
        /// </summary>
        /// <param name="configuration">Application configuration to use.</param>
        /// <param name="sessionFactory">The session factory used to acquire discovery channels.</param>
        /// <param name="diagnosticsMasks">The return diagnostics for all discovery requests.</param>
        public LocalDiscoveryServerClient(
            ApplicationConfiguration configuration,
            ISessionFactory sessionFactory,
            DiagnosticsMasks diagnosticsMasks = DiagnosticsMasks.None)
            : this(
                  configuration,
                  channelManager: null,
                  sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory)),
                  diagnosticsMasks)
        {
        }

        private LocalDiscoveryServerClient(
            ApplicationConfiguration configuration,
            IClientChannelManager? channelManager,
            ISessionFactory? sessionFactory,
            DiagnosticsMasks diagnosticsMasks)
        {
            ApplicationConfiguration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            DiagnosticsMasks = diagnosticsMasks;
            MessageContext = configuration.CreateMessageContext();
            m_channelManager = channelManager;
            m_sessionFactory = sessionFactory;

            // set some defaults for the preferred locales.
            System.Globalization.CultureInfo culture = System.Globalization.CultureInfo
                .CurrentUICulture;

            var locales = new List<string> { culture.Name };

            culture = System.Globalization.CultureInfo.CurrentCulture;

            if (!locales.Contains(culture.Name))
            {
                locales.Add(culture.Name);
            }

            if (!locales.Contains("en-US"))
            {
                locales.Add("en-US");
            }

            PreferredLocales = [.. locales];
        }

        /// <summary>
        /// Creates a local discovery client that uses a shared channel manager for discovery requests.
        /// </summary>
        /// <param name="manager">The client channel manager used to acquire discovery channels.</param>
        /// <param name="configuration">Application configuration to use.</param>
        /// <param name="diagnosticsMasks">The return diagnostics for all discovery requests.</param>
        /// <param name="ct">A cancellation token to cancel the operation with.</param>
        /// <returns>A local discovery server client.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="manager"/> or <paramref name="configuration"/> is <c>null</c>.
        /// </exception>
        public static Task<LocalDiscoveryServerClient> CreateAsync(
            IClientChannelManager manager,
            ApplicationConfiguration configuration,
            DiagnosticsMasks diagnosticsMasks = DiagnosticsMasks.None,
            CancellationToken ct = default)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            ct.ThrowIfCancellationRequested();
            var client = new LocalDiscoveryServerClient(configuration, manager, diagnosticsMasks);
            return Task.FromResult(client);
        }

        /// <summary>
        /// Creates a local discovery client that uses a session factory for discovery requests.
        /// </summary>
        /// <param name="sessionFactory">The session factory used to acquire discovery channels.</param>
        /// <param name="configuration">Application configuration to use.</param>
        /// <param name="diagnosticsMasks">The return diagnostics for all discovery requests.</param>
        /// <param name="ct">A cancellation token to cancel the operation with.</param>
        /// <returns>A local discovery server client.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="sessionFactory"/> or <paramref name="configuration"/> is <c>null</c>.
        /// </exception>
        public static Task<LocalDiscoveryServerClient> CreateAsync(
            ISessionFactory sessionFactory,
            ApplicationConfiguration configuration,
            DiagnosticsMasks diagnosticsMasks = DiagnosticsMasks.None,
            CancellationToken ct = default)
        {
            if (sessionFactory == null)
            {
                throw new ArgumentNullException(nameof(sessionFactory));
            }

            ct.ThrowIfCancellationRequested();
            sessionFactory.ReturnDiagnostics = diagnosticsMasks;
            var client = new LocalDiscoveryServerClient(configuration, sessionFactory, diagnosticsMasks);
            return Task.FromResult(client);
        }

        public ApplicationConfiguration ApplicationConfiguration { get; }
        public DiagnosticsMasks DiagnosticsMasks { get; }
        public IServiceMessageContext MessageContext { get; }

        public ArrayOf<string> PreferredLocales { get; set; }

        public int DefaultOperationTimeout { get; set; }

        public ValueTask<ArrayOf<ApplicationDescription>> FindServersAsync(
            CancellationToken ct = default)
        {
            return FindServersAsync(null, null, ct);
        }

        public async ValueTask<ArrayOf<ApplicationDescription>> FindServersAsync(
            string? endpointUrl,
            string? endpointTransportProfileUri,
            CancellationToken ct = default)
        {
            using DiscoveryClient client = await CreateClientAsync(
                endpointUrl,
                endpointTransportProfileUri,
                ct).ConfigureAwait(false);

            FindServersResponse response = await client.FindServersAsync(
                null,
                endpointUrl,
                PreferredLocales,
                default,
                ct).ConfigureAwait(false);

            return response.Servers;
        }

        public ValueTask<ArrayOf<EndpointDescription>> GetEndpointsAsync(string endpointUrl, CancellationToken ct = default)
        {
            return GetEndpointsAsync(endpointUrl, null, ct);
        }

        public async ValueTask<ArrayOf<EndpointDescription>> GetEndpointsAsync(
            string endpointUrl,
            string? endpointTransportProfileUri,
            CancellationToken ct = default)
        {
            using DiscoveryClient client = await CreateClientAsync(endpointUrl, endpointTransportProfileUri, ct)
                .ConfigureAwait(false);

            GetEndpointsResponse response = await client.GetEndpointsAsync(
                null,
                endpointUrl,
                PreferredLocales,
                default,
                ct)
                .ConfigureAwait(false);

            return response.Endpoints;
        }

        public ValueTask<(ArrayOf<ServerOnNetwork>, DateTimeUtc lastCounterResetTime)> FindServersOnNetworkAsync(
            uint startingRecordId,
            uint maxRecordsToReturn,
            CancellationToken ct = default)
        {
            return FindServersOnNetworkAsync(
                null,
                null,
                startingRecordId,
                maxRecordsToReturn,
                default,
                ct);
        }

        public async ValueTask<(ArrayOf<ServerOnNetwork>, DateTimeUtc lastCounterResetTime)> FindServersOnNetworkAsync(
            string? endpointUrl,
            string? endpointTransportProfileUri,
            uint startingRecordId,
            uint maxRecordsToReturn,
            ArrayOf<string> serverCapabilityFilters,
            CancellationToken ct = default)
        {
            using DiscoveryClient client = await CreateClientAsync(endpointUrl, endpointTransportProfileUri, ct)
                .ConfigureAwait(false);

            FindServersOnNetworkResponse response = await client.FindServersOnNetworkAsync(
                null,
                startingRecordId,
                maxRecordsToReturn,
                serverCapabilityFilters,
                ct).ConfigureAwait(false);

            return (response.Servers, response.LastCounterResetTime);
        }

        protected virtual Task<DiscoveryClient> CreateClientAsync(
            string? endpointUrl,
            string? endpointTransportProfileUri,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(endpointUrl))
            {
                endpointUrl = kDefaultUrl;
            }

            if (!Uri.IsWellFormedUriString(endpointUrl, UriKind.Absolute))
            {
                throw new ArgumentException("Not a valid URL.", nameof(endpointUrl));
            }

            var configuration = EndpointConfiguration.Create(ApplicationConfiguration);

            if (DefaultOperationTimeout != 0)
            {
                configuration.OperationTimeout = DefaultOperationTimeout;
            }

            var discoveryUri = new Uri(endpointUrl);

            if (m_channelManager != null)
            {
                return DiscoveryClient.CreateAsync(
                    m_channelManager,
                    discoveryUri,
                    configuration,
                    MessageContext.Telemetry,
                    DiagnosticsMasks,
                    ct);
            }

            if (m_sessionFactory != null)
            {
                return CreateClientAsync(m_sessionFactory, discoveryUri, configuration, ct);
            }

            return DiscoveryClient.CreateAsync(
                ApplicationConfiguration,
                discoveryUri,
                configuration,
                DiagnosticsMasks,
                ct);
        }

        private async Task<DiscoveryClient> CreateClientAsync(
            ISessionFactory sessionFactory,
            Uri discoveryUri,
            EndpointConfiguration endpointConfiguration,
            CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = CreateDiscoveryEndpoint(discoveryUri, endpointConfiguration);
            ITransportChannel channel = await sessionFactory.CreateChannelAsync(
                ApplicationConfiguration,
                connection: null!,
                endpoint,
                updateBeforeConnect: false,
                checkDomain: false,
                ct).ConfigureAwait(false);
            return await DiscoveryClient.CreateAsync(
                channel,
                MessageContext.Telemetry,
                DiagnosticsMasks,
                ct).ConfigureAwait(false);
        }

        private static ConfiguredEndpoint CreateDiscoveryEndpoint(
            Uri discoveryUri,
            EndpointConfiguration endpointConfiguration)
        {
            var endpoint = new EndpointDescription
            {
                EndpointUrl = discoveryUri.OriginalString,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };
            endpoint.Server.ApplicationUri = endpoint.EndpointUrl;
            endpoint.Server.ApplicationType = ApplicationType.DiscoveryServer;

            return new ConfiguredEndpoint(null, endpoint, endpointConfiguration)
            {
                UpdateBeforeConnect = false
            };
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            try
            {
                await m_disposeCts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
            m_disposeCts.Dispose();
            GC.SuppressFinalize(this);
        }

        private readonly CancellationTokenSource m_disposeCts = new();
        private readonly IClientChannelManager? m_channelManager;
        private readonly ISessionFactory? m_sessionFactory;
        private bool m_disposed;
        private const string kDefaultUrl = "opc.tcp://localhost:4840";
    }
}
