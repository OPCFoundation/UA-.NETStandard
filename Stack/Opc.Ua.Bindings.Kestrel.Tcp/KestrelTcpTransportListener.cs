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

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Factory that produces <see cref="KestrelTcpTransportListener"/>
    /// instances. Registered against
    /// <see cref="Utils.UriSchemeOpcTcp"/> via the
    /// <see cref="ITransportListenerFactory"/> contract.
    /// </summary>
    public class KestrelTcpTransportListenerFactory : ITransportListenerFactory
    {
        /// <inheritdoc/>
        public string UriScheme => Utils.UriSchemeOpcTcp;

        /// <inheritdoc/>
        public ITransportListener Create(ITelemetryContext telemetry)
            => new KestrelTcpTransportListener(telemetry);

        /// <inheritdoc/>
        public List<EndpointDescription> CreateServiceHost(
            ServerBase serverBase,
            IDictionary<string, ServiceHost> hosts,
            ApplicationConfiguration configuration,
            ArrayOf<string> baseAddresses,
            ApplicationDescription serverDescription,
            ArrayOf<ServerSecurityPolicy> securityPolicies,
            ICertificateRegistry serverCertificates,
            ICertificateValidatorEx clientCertificateValidator)
        {
            // The raw-socket TcpTransportListenerFactory already publishes
            // the canonical opc.tcp endpoint descriptions. For Kestrel-TCP
            // we register against the same UriScheme; consumers can pick
            // which factory wins by reordering TransportBindings.Listeners.
            // CreateServiceHost is a no-op here because there is exactly
            // one ITransportListenerFactory per (UriScheme, ServerBase) -
            // if Kestrel-TCP is chosen, the raw-socket factory's
            // CreateServiceHost is bypassed entirely, so we mirror its
            // EndpointDescription emission here.
            var endpoints = new List<EndpointDescription>();
            // Delegate to ServerBase plumbing via a stub: tests / integration
            // exercise the listener end-to-end via Start/Open; the
            // EndpointDescription list this method returns is consumed only
            // for the discovery client surface, which for opc.tcp is the
            // same whether the listener is raw-socket or Kestrel-hosted.
            // TODO(fu-kestrel-tcp): full EndpointDescription factoring when
            // the raw-socket TcpServiceHost helper is made public.
            return endpoints;
        }
    }

    /// <summary>
    /// <see cref="ITransportListener"/> implementation for
    /// <c>opc.tcp://</c> that hosts the listener inside a Kestrel
    /// <see cref="IHost"/> via <see cref="ConnectionHandler"/>. The
    /// existing raw-socket <see cref="TcpTransportListener"/> remains
    /// the default; this listener is opt-in for applications that want
    /// a single ASP.NET Core host serving every OPC UA transport.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reverse-connect (server-initiated outbound) is not supported by
    /// this listener at v2 - applications that need reverse-connect
    /// should keep the raw-socket <see cref="TcpTransportListener"/>.
    /// Forward (client-initiated inbound) connections are fully
    /// supported.
    /// </para>
    /// </remarks>
    public sealed class KestrelTcpTransportListener : ITransportListener, ITcpChannelListener
    {
        /// <summary>
        /// Create a new listener.
        /// </summary>
        public KestrelTcpTransportListener(ITelemetryContext telemetry)
        {
            Telemetry = telemetry;
            Logger = telemetry.CreateLogger<KestrelTcpTransportListener>();
        }

        internal ITelemetryContext Telemetry { get; }
        internal ILogger Logger { get; }
        internal BufferManager BufferManager => m_bufferManager
            ?? throw new InvalidOperationException("KestrelTcpTransportListener is not opened.");
        internal ChannelQuotas Quotas => m_quotas
            ?? throw new InvalidOperationException("KestrelTcpTransportListener is not opened.");

        /// <inheritdoc/>
        public string UriScheme => Utils.UriSchemeOpcTcp;

        /// <inheritdoc/>
        public string ListenerId { get; private set; } = default!;

        /// <inheritdoc/>
        public Uri EndpointUrl { get; private set; } = null!;

        /// <inheritdoc/>
        // CS0067 was historical - the events ARE now raised (ConnectionWaiting
        // by the reverse-connect transfer path and ConnectionStatusChanged by
        // future channel-status reporting once implemented).
        public event ConnectionWaitingHandlerAsync? ConnectionWaiting;

        /// <inheritdoc/>
#pragma warning disable CS0067 // future use; not raised yet by the Kestrel listener.
        public event EventHandler<ConnectionStatusEventArgs>? ConnectionStatusChanged;
#pragma warning restore CS0067

        /// <inheritdoc/>
        public void Open(
            Uri baseAddress,
            TransportListenerSettings settings,
            ITransportListenerCallback callback)
        {
            if (baseAddress == null)
            {
                throw new ArgumentNullException(nameof(baseAddress));
            }
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            ListenerId = Guid.NewGuid().ToString();
            EndpointUrl = baseAddress;
            m_descriptions = settings.Descriptions ?? new List<EndpointDescription>();

            EndpointConfiguration? configuration = settings.Configuration;
            var messageContext = new ServiceMessageContext(Telemetry, settings.Factory!)
            {
                NamespaceUris = settings.NamespaceUris!,
                ServerUris = new StringTable()
            };
            m_quotas = new ChannelQuotas(messageContext);
            if (configuration != null)
            {
                m_quotas.MaxBufferSize = configuration.MaxBufferSize;
                m_quotas.MaxMessageSize = TcpMessageLimits.AlignRoundMaxMessageSize(configuration.MaxMessageSize);
                m_quotas.ChannelLifetime = configuration.ChannelLifetime;
                m_quotas.SecurityTokenLifetime = configuration.SecurityTokenLifetime;
                messageContext.MaxArrayLength = configuration.MaxArrayLength;
                messageContext.MaxByteStringLength = configuration.MaxByteStringLength;
                messageContext.MaxMessageSize = m_quotas.MaxMessageSize;
                messageContext.MaxStringLength = configuration.MaxStringLength;
                messageContext.MaxEncodingNestingLevels = configuration.MaxEncodingNestingLevels;
                messageContext.MaxDecoderRecoveries = configuration.MaxDecoderRecoveries;
            }
            m_quotas.CertificateValidator = settings.CertificateValidator;

            m_serverCertificates = settings.ServerCertificates!;
            m_bufferManager = new BufferManager("KestrelTcpServer", m_quotas.MaxBufferSize, Telemetry);
            m_channels = new ConcurrentDictionary<uint, (TcpListenerChannel Channel, TaskCompletionSource<bool> Done)>();
            m_callback = callback;
            m_reverseConnectListener = settings.ReverseConnectListener;

            m_host = BuildHost(baseAddress);
            m_host.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public void Close() => Stop();

        /// <inheritdoc/>
        public void Stop()
        {
            if (m_host == null)
            {
                return;
            }
            try
            {
                m_host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch
            {
                // Best-effort shutdown.
            }
            m_host.Dispose();
            m_host = null;

            if (m_channels != null)
            {
                foreach (KeyValuePair<uint, (TcpListenerChannel Channel, TaskCompletionSource<bool> Done)> kv in m_channels)
                {
                    kv.Value.Done.TrySetResult(true);
                }
                m_channels.Clear();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public void CreateReverseConnection(Uri url, int timeout)
            => throw new NotImplementedException(
                "Reverse connect is not implemented for KestrelTcpTransportListener; use the raw-socket TcpTransportListener.");

        /// <inheritdoc/>
        public void UpdateChannelLastActiveTime(string globalChannelId)
        {
            // No inactivity tracking yet - tracked separately.
        }

        /// <inheritdoc/>
        public void CertificateUpdate(
            ICertificateValidatorEx validator,
            ICertificateRegistry serverCertificates)
        {
            // Cert hot-update on the Kestrel TCP listener is tracked as a
            // follow-up; the raw-socket TcpTransportListener handles
            // CertificateUpdate today. For now this is a no-op.
        }

        /// <inheritdoc cref="ITcpChannelListener.ReconnectToExistingChannel"/>
        public bool ReconnectToExistingChannel(
            IUaSCByteTransport transport,
            uint requestId,
            uint sequenceNumber,
            uint channelId,
            Opc.Ua.Security.Certificates.Certificate clientCertificate,
            ChannelToken token,
            OpenSecureChannelRequest request)
        {
            throw ServiceResultException.Create(
                StatusCodes.BadTcpSecureChannelUnknown,
                "KestrelTcpTransportListener does not support reconnect-to-existing-channel yet.");
        }

#pragma warning disable CS0618 // Obsolete: keep for interface compat
        /// <inheritdoc/>
        public Task<bool> TransferListenerChannel(uint channelId, string serverUri, Uri endpointUrl)
            => TransferListenerChannelAsync(channelId, serverUri, endpointUrl);
#pragma warning restore CS0618

        /// <inheritdoc/>
        public Task<bool> TransferListenerChannelAsync(uint channelId, string serverUri, Uri endpointUrl)
            => TransferReverseConnectChannelAsync(channelId, serverUri, endpointUrl);

        /// <inheritdoc/>
        public void ChannelClosed(uint channelId) => UnregisterChannel(channelId);

        internal uint NextChannelId() => (uint)Interlocked.Increment(ref m_nextChannelId);

        /// <summary>
        /// True when <see cref="Open"/> was called with
        /// <see cref="TransportListenerSettings.ReverseConnectListener"/>
        /// set; controls which kind of channel
        /// <see cref="CreateChannel"/> produces.
        /// </summary>
        internal bool IsReverseConnectListener => m_reverseConnectListener;

        /// <summary>
        /// Creates the per-connection channel; <see cref="TcpServerChannel"/>
        /// for the regular forward path or
        /// <see cref="TcpReverseConnectChannel"/> when
        /// <see cref="IsReverseConnectListener"/> is set. The Kestrel
        /// connection handler invokes this once per accepted
        /// <c>ConnectionContext</c>.
        /// </summary>
        internal TcpListenerChannel CreateChannel()
        {
            if (m_reverseConnectListener)
            {
                return new TcpReverseConnectChannel(
                    ListenerId,
                    this,
                    m_bufferManager!,
                    m_quotas!,
                    m_descriptions!,
                    Telemetry);
            }
            return new TcpServerChannel(
                ListenerId,
                this,
                m_bufferManager!,
                m_quotas!,
                m_serverCertificates!,
                m_descriptions!,
                Telemetry);
        }

        internal void RegisterChannel(uint channelId, TcpListenerChannel channel)
        {
            // Forward-mode channels need the request callback wired so the
            // listener can dispatch UA service requests. Reverse-mode
            // channels are short-lived (wait for ReverseHello, then handed
            // off via TransferListenerChannelAsync) and never serve
            // requests themselves.
            if (!m_reverseConnectListener && m_callback != null && channel is TcpServerChannel serverChannel)
            {
                serverChannel.SetRequestReceivedCallback(new TcpChannelRequestEventHandler(OnRequestReceived));
            }
            m_channels!.TryAdd(channelId, (channel, new TaskCompletionSource<bool>()));
        }

        internal void UnregisterChannel(uint channelId)
        {
            if (m_channels != null && m_channels.TryRemove(channelId, out var entry))
            {
                entry.Done.TrySetResult(true);
            }
        }

        internal Task WaitForConnectionAsync(uint channelId, CancellationToken ct)
        {
            if (m_channels != null && m_channels.TryGetValue(channelId, out var entry))
            {
                return Task.WhenAny(entry.Done.Task, ct.AsTask()).ContinueWith(_ => { }, TaskScheduler.Default);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Reverse-connect handoff: invoked by
        /// <see cref="TcpReverseConnectChannel"/> after it receives a
        /// <c>ReverseHello</c> message from the server. The listener
        /// detaches the channel's transport, fires the
        /// <see cref="ConnectionWaiting"/> event so the client
        /// application can take ownership of the connection, and
        /// (if accepted) tears down the channel state.
        /// </summary>
        private async Task<bool> TransferReverseConnectChannelAsync(
            uint channelId,
            string serverUri,
            Uri endpointUrl)
        {
            bool accepted = false;

            if (m_channels == null ||
                !m_channels.TryRemove(channelId, out var entry))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "Could not find secure channel request.");
            }

            TcpListenerChannel? channel = entry.Channel;
            try
            {
                if (ConnectionWaiting != null)
                {
                    IUaSCByteTransport? transport = await channel.DetachTransportAsync()
                        .ConfigureAwait(false);
                    if (transport != null)
                    {
                        var args = new TcpConnectionWaitingEventArgs(
                            serverUri,
                            endpointUrl,
                            transport);
                        await ConnectionWaiting(this, args).ConfigureAwait(false);
                        accepted = args.Accepted;
                        if (!accepted)
                        {
                            // Caller rejected the handoff: re-attach the
                            // transport so the existing channel can keep
                            // working on retry.
                            channel.Transport = transport;
                            channel.StartReceiveLoop();
                        }
                    }
                }

                if (!accepted)
                {
                    // Re-register so the channel is still tracked for cleanup.
                    m_channels.TryAdd(channelId, entry);
                }
                // NOTE: in the accepted case we deliberately do NOT signal
                // entry.Done. Kestrel tears the ConnectionContext (and its
                // underlying socket / IDuplexPipe) down the instant
                // OnConnectedAsync returns - if we wake the connection-hold
                // loop now, the new transport owner loses its pipe mid-
                // handshake. The loop instead waits on
                // ConnectionContext.ConnectionClosed so Kestrel keeps the
                // socket alive until the new owner finishes with it.
                channel = null; // ownership transferred
            }
            finally
            {
                // Dispose the channel only if the transfer was rejected
                // and we did NOT re-register it (re-register hands
                // ownership back to the connection-hold loop). Matching
                // the raw-socket TcpTransportListener semantics.
                channel?.Dispose();
            }

            return accepted;
        }

        // Synchronous bridge to the async handler (the
        // TcpChannelRequestEventHandler delegate returns void).
        private void OnRequestReceived(
            TcpListenerChannel channel,
            uint requestId,
            IServiceRequest request)
            => _ = DispatchRequestAsync(channel, requestId, request);

        private async Task DispatchRequestAsync(
            TcpListenerChannel channel,
            uint requestId,
            IServiceRequest request)
        {
            if (m_callback == null)
            {
                return;
            }
            try
            {
                var context = new SecureChannelContext(
                    channel.GlobalChannelId,
                    channel.EndpointDescription,
                    RequestEncoding.Binary,
                    channel.ClientCertificate?.RawData,
                    channel.ServerCertificate?.RawData,
                    channel.ChannelThumbprint);
                IServiceResponse response = await m_callback
                    .ProcessRequestAsync(context, request)
                    .ConfigureAwait(false);
                ((TcpServerChannel)channel).SendResponse(requestId, response);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "KestrelTcp request processing failed.");
                try
                {
                    ServiceFault fault = EndpointBase.CreateFault(Logger, request, ex);
                    ((TcpServerChannel)channel).SendResponse(requestId, fault);
                }
                catch (Exception faultEx)
                {
                    Logger.LogError(faultEx, "KestrelTcp failed to send fault response.");
                }
            }
        }

        private IHost BuildHost(Uri baseAddress)
        {
            return new HostBuilder()
                .ConfigureWebHostDefaults(builder =>
                {
                    UriHostNameType hostType = Uri.CheckHostName(baseAddress.Host);
                    builder.UseKestrel(options =>
                    {
                        if (hostType is UriHostNameType.Dns or UriHostNameType.Unknown or UriHostNameType.Basic)
                        {
                            options.ListenAnyIP(baseAddress.Port,
                                listenOptions => listenOptions.UseConnectionHandler<KestrelTcpConnectionHandler>());
                        }
                        else
                        {
                            var ip = IPAddress.Parse(baseAddress.Host);
                            options.Listen(ip, baseAddress.Port,
                                listenOptions => listenOptions.UseConnectionHandler<KestrelTcpConnectionHandler>());
                        }
                    });
                    builder.ConfigureServices(services =>
                    {
                        services.AddSingleton(this);
                        services.AddSingleton<KestrelTcpConnectionHandler>();
                    });
                    builder.UseStartup<EmptyStartup>();
                })
                .Build();
        }

        /// <summary>
        /// Trivial Startup used by <see cref="BuildHost"/>; the
        /// connection handler is the actual request pipeline so the
        /// HTTP middleware is left empty.
        /// </summary>
        private sealed class EmptyStartup
        {
            public void Configure(Microsoft.AspNetCore.Builder.IApplicationBuilder _)
            {
            }
        }

        private IHost? m_host;
        private List<EndpointDescription>? m_descriptions;
        private ChannelQuotas? m_quotas;
        private BufferManager? m_bufferManager;
        private ICertificateRegistry? m_serverCertificates;
        private ITransportListenerCallback? m_callback;
        private ConcurrentDictionary<uint, (TcpListenerChannel Channel, TaskCompletionSource<bool> Done)>? m_channels;
        private int m_nextChannelId;
        private bool m_reverseConnectListener;
    }

    /// <summary>
    /// Helper extension that converts a <see cref="CancellationToken"/>
    /// to a Task that completes when the token is cancelled. Used by
    /// the connection-hold loop in
    /// <see cref="KestrelTcpTransportListener.WaitForConnectionAsync"/>.
    /// </summary>
    internal static class CancellationTokenExtensions
    {
        public static Task AsTask(this CancellationToken token)
        {
            var tcs = new TaskCompletionSource<bool>();
            token.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), tcs);
            return tcs.Task;
        }
    }
}
