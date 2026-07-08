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

#if NET8_0_OR_GREATER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
    /// <see cref="ITransportListenerFactory"/> contract. Inherits
    /// <see cref="TcpServiceHost.CreateServiceHostAsync"/> from the
    /// raw-socket <see cref="TcpServiceHost"/> base so the discovery
    /// endpoint-description list matches the raw-socket factory
    /// exactly; only <see cref="Create"/> differs (returns a Kestrel-
    /// hosted listener instead of the raw-socket one).
    /// </summary>
    public class KestrelTcpTransportListenerFactory : TcpServiceHost
    {
        /// <inheritdoc/>
        public override string UriScheme => Utils.UriSchemeOpcTcp;

        /// <inheritdoc/>
        public override ITransportListener Create(ITelemetryContext telemetry)
        {
            return new KestrelTcpTransportListener(telemetry);
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
    public sealed class KestrelTcpTransportListener : ITransportListener, ITcpChannelListener, ITransportListenerCertificateRotation
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
        public async ValueTask OpenAsync(
            Uri baseAddress,
            TransportListenerSettings settings,
            ITransportListenerCallback callback,
            CancellationToken ct = default)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            ListenerId = Guid.NewGuid().ToString();
            EndpointUrl = baseAddress ?? throw new ArgumentNullException(nameof(baseAddress));
            m_descriptions = settings.Descriptions ?? [];

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
            await m_host.StartAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask CloseAsync(CancellationToken ct = default)
        {
            return StopAsync(ct);
        }

        /// <inheritdoc/>
        public async ValueTask StopAsync(CancellationToken ct = default)
        {
            IHost? host = m_host;
            m_host = null;
            if (host != null)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(5));
                    await host.StopAsync(cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort shutdown.
                }
                host.Dispose();
            }

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
        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public void CreateReverseConnection(Uri url, int timeout)
        {
            throw new NotImplementedException(
                        "Reverse connect is not implemented for KestrelTcpTransportListener; use the raw-socket TcpTransportListener.");
        }

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
            // Mirror TcpTransportListener.CertificateUpdate: refresh the
            // validator + cert registry; the channel registry will pick
            // up the new server cert on the next OpenSecureChannel /
            // renegotiation. The Kestrel listener has no TLS bind to
            // rotate (opc.tcp is plaintext), so the only listener-side
            // state to update is the references we hold.
            m_quotas?.CertificateValidator = validator;
            m_serverCertificates = serverCertificates;
        }

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<string>> CloseChannelsForCertificateAsync(
            Security.Certificates.Certificate oldCertificate,
            CancellationToken ct = default)
        {
            if (oldCertificate == null)
            {
                throw new ArgumentNullException(nameof(oldCertificate));
            }

            string oldThumbprint = oldCertificate.Thumbprint;
            if (string.IsNullOrEmpty(oldThumbprint))
            {
                return new ValueTask<IReadOnlyList<string>>(Array.Empty<string>());
            }

            // Snapshot the channel map so we can iterate without holding
            // any lock the per-channel close paths might also need.
            (TcpListenerChannel Channel, TaskCompletionSource<bool> Done)[] entries =
                m_channels?.Values.ToArray()
                ?? [];

            if (entries.Length == 0)
            {
                return new ValueTask<IReadOnlyList<string>>(Array.Empty<string>());
            }

            var closed = new List<string>(entries.Length);
            foreach (var entry in entries)
            {
                try
                {
                    if (entry.Channel.TryCloseForCertificateRotation(oldThumbprint, out string? globalChannelId) &&
                        !string.IsNullOrEmpty(globalChannelId))
                    {
                        closed.Add(globalChannelId!);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(
                        ex,
                        "KestrelTcp failed to close channel for certificate rotation (thumbprint {Thumbprint}).",
                        oldThumbprint);
                }
            }

            Logger.LogInformation(
                Utils.TraceMasks.Security,
                "KestrelTcp closed {Count} SecureChannel(s) for certificate rotation (thumbprint {Thumbprint}).",
                closed.Count,
                oldThumbprint);

            return new ValueTask<IReadOnlyList<string>>(closed);
        }

        /// <inheritdoc cref="ITcpChannelListener.ReconnectToExistingChannel"/>
        public bool ReconnectToExistingChannel(
            IUaSCByteTransport transport,
            uint requestId,
            uint sequenceNumber,
            uint channelId,
            Security.Certificates.Certificate clientCertificate,
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
        {
            return TransferListenerChannelAsync(channelId, serverUri, endpointUrl);
        }
#pragma warning restore CS0618

        /// <inheritdoc/>
        public Task<bool> TransferListenerChannelAsync(uint channelId, string serverUri, Uri endpointUrl)
        {
            return TransferReverseConnectChannelAsync(channelId, serverUri, endpointUrl);
        }

        /// <inheritdoc/>
        public void ChannelClosed(uint channelId)
        {
            UnregisterChannel(channelId);
        }

        internal uint NextChannelId()
        {
            return (uint)Interlocked.Increment(ref m_nextChannelId);
        }

        /// <summary>
        /// True when <see cref="OpenAsync"/> was called with
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
        /// <exception cref="ServiceResultException"></exception>
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

        /// <summary>
        /// Synchronous bridge to the async handler (the
        /// TcpChannelRequestEventHandler delegate returns void).
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="requestId"></param>
        /// <param name="request"></param>
        private void OnRequestReceived(
            TcpListenerChannel channel,
            uint requestId,
            IServiceRequest request)
        {
            _ = DispatchRequestAsync(channel, requestId, request);
        }

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
                    })
                        .ConfigureServices(services =>
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
#endif // NET8_0_OR_GREATER
