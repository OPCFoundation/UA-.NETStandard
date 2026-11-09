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
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet.Channel;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.Http;
using Opc.Ua.WotCon.Bindings.Modbus;
using Opc.Ua.WotCon.Bindings.Mqtt;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Host-injectable composition seam for existing WoT planners and policy-bound executor channels.
    /// Implementations receive an already authorized exact target, never an untrusted connector callback.
    /// </summary>
    public interface IWotRuntimeExecutorFactory
    {
        /// <summary>
        /// Gets the offline registry whose executable capabilities describe this factory.
        /// </summary>
        IWotBinderRegistry Binders { get; }

        /// <summary>
        /// Opens one compiled operation using the captured network grant and optional borrowed UA session.
        /// Ownership of the returned connection transfers to the named runtime.
        /// </summary>
        ValueTask<WotRuntimeConnection> OpenAsync(
            WotCompiledForm form, McpNetworkGrant grant, IWotCredentialProvider credentials,
            ISession? session = null, CancellationToken ct = default);
    }

    /// <summary>
    /// Connects only the four shipped executors, enforcing DNS/IP policy at their actual TCP seam.
    /// </summary>
    public sealed class WotRuntimeExecutorFactory : IWotRuntimeExecutorFactory
    {
        /// <summary>
        /// Creates offline planner metadata without constructing a client or opening a connection.
        /// </summary>
        public WotRuntimeExecutorFactory(WotRuntimeOptions options, ITelemetryContext telemetry)
        {
            m_options = options?.Snapshot() ?? throw new ArgumentNullException(nameof(options));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            Binders = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll(),
                [
                    new HttpWotBindingExecutor(),
                    new MqttWotBindingExecutor(),
                    new ModbusWotBindingExecutor(),
                    new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions())
                ],
                bounds: m_options.CreateBounds(), telemetry: telemetry);
        }

        /// <inheritdoc/>
        public IWotBinderRegistry Binders { get; }

        /// <inheritdoc/>
        public async ValueTask<WotRuntimeConnection> OpenAsync(
            WotCompiledForm form, McpNetworkGrant grant, IWotCredentialProvider credentials,
            ISession? session = null, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(form);
            ArgumentNullException.ThrowIfNull(grant);
            ArgumentNullException.ThrowIfNull(credentials);
            ct.ThrowIfCancellationRequested();
            var target = WotRuntimeTarget.FromForm(form, session);
            if (target.Endpoint != grant.Endpoint ||
                target.Operation != grant.Operation ||
                target.Resource != grant.Resource ||
                WotRuntimeTarget.Capability(form.OpToken) != grant.Capability)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                    "The connection grant does not match this compiled operation.");
            }
            HttpClient? client = null;
            IWotBindingExecutor executor;
            try
            {
                switch (form.Binding.Id)
                {
                    case "w3c.http":
                        client = CreateHttpClient(grant);
                        HttpClient ownedClient = client;
                        executor = new HttpWotBindingExecutor(new HttpWotBindingOptions
                        {
                            ClientFactory = () => ownedClient,
                            // The owned handler below disables redirects, cookies, proxy and ambient credentials.
                            CallerClientHandlesRedirectSafety = true,
                            MaxAutomaticRedirects = 0,
                            ObserveInterval = TimeSpan.FromMilliseconds(m_options.ObserveIntervalMilliseconds)
                        });
                        break;
                    case "w3c.mqtt":
                        executor = new MqttWotBindingExecutor(new MqttWotBindingOptions
                        {
                            StreamProvider = new ApprovedMqttStreamProvider(grant),
                            MaximumPacketSize = checked((uint)m_options.MaxPayloadBytes + 8192),
                            ReadTimeout = TimeSpan.FromMilliseconds(m_options.OperationTimeoutMilliseconds),
                            ValidateServerCertificate = true,
                            AllowCredentialsOverPlaintext = false
                        });
                        break;
                    case "w3c.modbus":
                        executor = new ModbusWotBindingExecutor(new ModbusWotBindingOptions
                        {
                            StreamConnector = (host, port, token) => ConnectAsync(grant, host, port, token),
                            ObserveInterval = TimeSpan.FromMilliseconds(m_options.ObserveIntervalMilliseconds)
                        });
                        break;
                    case "opc.opcua":
                        ArgumentNullException.ThrowIfNull(session);
                        ISession borrowed = session;
                        executor = new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                        {
                            ConstrainedSessionFactory = (request, token) =>
                            {
                                token.ThrowIfCancellationRequested();
                                if (!Uri.TryCreate(request.EndpointUrl, UriKind.Absolute, out Uri? requested) ||
                                    requested != grant.Endpoint)
                                {
                                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                                        "The OPC UA executor requested a different endpoint.");
                                }
                                return ValueTask.FromResult(borrowed);
                            },
                            DisposeSession = false,
                            ObserveInterval = TimeSpan.FromMilliseconds(m_options.ObserveIntervalMilliseconds),
                            EventQueueSize = (uint)m_options.MaxNotifications
                        });
                        break;
                    default:
                        throw new ServiceResultException(StatusCodes.BadNotSupported,
                            "No approved live executor exists for this binding.");
                }
                if (!string.Equals(executor.Identity.Key, form.Binding.Key, StringComparison.Ordinal) ||
                    !form.IsExecutable ||
                    !executor.CanExecute(form))
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "The pinned binder version or operation has no matching executor.");
                }
                var endpointPolicy = new WotEndpointPolicy
                {
                    // Only a captured grant can reach this path; its exact CIDRs are checked at every connect.
                    AllowLoopback = true,
                    AllowPrivateAddresses = true
                };
                endpointPolicy.AllowedHosts.Add(grant.Endpoint.IdnHost);
                var channels = new WotProtocolBinderRegistry(
                    WotBuiltInBinders.CreateAll(), [executor], credentials,
                    bounds: m_options.CreateBounds(), endpointPolicy: endpointPolicy, telemetry: m_telemetry);
                IWotBindingChannel channel = await channels.OpenChannelAsync(form, ct).ConfigureAwait(false);
                var connection = new WotRuntimeConnection(channel, client);
                client = null;
                return connection;
            }
            finally
            {
                client?.Dispose();
            }
        }

        /// <summary>
        /// Creates a private client whose socket connection consumes the approved address, not another DNS lookup.
        /// </summary>
        private HttpClient CreateHttpClient(McpNetworkGrant grant)
        {
            SocketsHttpHandler? handler = null;
            try
            {
                handler = new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    UseProxy = false,
                    Credentials = null,
                    AutomaticDecompression = DecompressionMethods.None,
                    ConnectTimeout = TimeSpan.FromMilliseconds(m_options.OperationTimeoutMilliseconds),
                    ConnectCallback = (context, ct) =>
                        ConnectAsync(grant, context.DnsEndPoint.Host, context.DnsEndPoint.Port, ct)
                };
                handler.SslOptions.CertificateRevocationCheckMode = X509RevocationMode.Online;
                var client = new HttpClient(handler, disposeHandler: true)
                {
                    Timeout = Timeout.InfiniteTimeSpan
                };
                handler = null;
                return client;
            }
            finally
            {
                handler?.Dispose();
            }
        }

        /// <summary>
        /// Refuses any library-requested retargeting before invoking the grant's DNS-safe connector.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static ValueTask<Stream> ConnectAsync(
            McpNetworkGrant grant, string host, int port, CancellationToken ct)
        {
            int expectedPort = WotRuntimeTarget.EffectivePort(grant.Endpoint);
            if (!string.Equals(host.Trim('[', ']'), grant.Endpoint.IdnHost.Trim('[', ']'),
                StringComparison.OrdinalIgnoreCase) ||
                port != expectedPort)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                    "The executor attempted to connect outside its pinned endpoint.");
            }
            return grant.ConnectTcpAsync(ct);
        }

        private readonly WotRuntimeOptions m_options;
        private readonly ITelemetryContext m_telemetry;

        /// <summary>
        /// Supplies plaintext TCP to MQTTnet; its own TLS layer keeps the original broker hostname.
        /// </summary>
        private sealed class ApprovedMqttStreamProvider(McpNetworkGrant grant) : IMqttClientStreamProvider
        {
            /// <inheritdoc/>
            public Task<Stream> ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken)
            {
                return endPoint switch
                {
                    DnsEndPoint dns => WotRuntimeExecutorFactory.ConnectAsync(
                        grant, dns.Host, dns.Port, cancellationToken).AsTask(),
                    IPEndPoint ip => WotRuntimeExecutorFactory.ConnectAsync(
                        grant, ip.Address.ToString(), ip.Port, cancellationToken).AsTask(),
                    _ => throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                        "The MQTT endpoint does not match an approved DNS or IP target.")
                };
            }
        }
    }

    /// <summary>
    /// Owns one existing executor channel and any caller-owned HTTP client supplied to that executor.
    /// </summary>
    public sealed class WotRuntimeConnection : IAsyncDisposable
    {
        /// <summary>
        /// Transfers ownership of an executor channel to a runtime connection.
        /// </summary>
        public WotRuntimeConnection(IWotBindingChannel channel)
            : this(channel, null)
        {
        }

        /// <summary>
        /// Additionally owns the client that an HTTP executor deliberately treats as borrowed.
        /// </summary>
        internal WotRuntimeConnection(IWotBindingChannel channel, HttpClient? client)
        {
            Channel = channel ?? throw new ArgumentNullException(nameof(channel));
            m_client = client;
        }

        /// <summary>
        /// Gets the existing executor channel without transferring ownership.
        /// </summary>
        public IWotBindingChannel Channel { get; }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_state)
            {
                m_disposal ??= DisposeCoreAsync();
                return new ValueTask(m_disposal);
            }
        }

        /// <summary>
        /// Disposes the channel once and always releases the module-owned HTTP client.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            try
            {
                await Channel.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                m_client?.Dispose();
            }
        }

        private readonly HttpClient? m_client;
        private readonly Lock m_state = new();
        private Task? m_disposal;
    }
}
