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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

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
        /// <summary>
        /// Creates a factory using the default buffer-manager factory.
        /// </summary>
        public KestrelTcpTransportListenerFactory()
            : this(DefaultBufferManagerFactory.Instance)
        {
        }

        /// <summary>
        /// Creates a factory using the specified buffer-manager factory.
        /// </summary>
        /// <param name="bufferManagerFactory">Factory used to create listener buffer managers.</param>
        public KestrelTcpTransportListenerFactory(
            IBufferManagerFactory bufferManagerFactory)
        {
            m_bufferManagerFactory = bufferManagerFactory ??
                throw new ArgumentNullException(nameof(bufferManagerFactory));
        }

        /// <inheritdoc/>
        public override string UriScheme => Utils.UriSchemeOpcTcp;

        /// <inheritdoc/>
        public override ITransportListener Create(ITelemetryContext telemetry)
        {
            return new KestrelTcpTransportListener(telemetry, m_bufferManagerFactory);
        }

        private readonly IBufferManagerFactory m_bufferManagerFactory;
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
    public sealed class KestrelTcpTransportListener
        : ITransportListener,
            ITcpChannelListener,
            ITransportListenerCertificateRotation,
            ITransportListenerPeerCertificateChainRotation
    {
        /// <summary>
        /// Create a new listener.
        /// </summary>
        public KestrelTcpTransportListener(ITelemetryContext telemetry)
            : this(telemetry, DefaultBufferManagerFactory.Instance)
        {
        }

        /// <summary>
        /// Creates a listener with the specified buffer-manager factory.
        /// </summary>
        /// <param name="telemetry">Telemetry context to use.</param>
        /// <param name="bufferManagerFactory">Factory used to create listener buffer managers.</param>
        public KestrelTcpTransportListener(
            ITelemetryContext telemetry,
            IBufferManagerFactory bufferManagerFactory)
        {
            Telemetry = telemetry;
            Logger = telemetry.CreateLogger<KestrelTcpTransportListener>();
            m_bufferManagerFactory = bufferManagerFactory ??
                throw new ArgumentNullException(nameof(bufferManagerFactory));
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
            m_quotas = new ChannelQuotas(messageContext)
            {
                SecurityPolicyRegistry = settings.SecurityPolicyRegistry,
                SessionBindingProvider = settings.SessionBindingProvider,
                ResourceIsolationProvider = settings.ResourceIsolationProvider,
                HandshakeTimeout = settings.HandshakeTimeout
            };
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
            m_quotas.ChunkReassemblyBudget = settings.ChunkReassemblyBudget ??
                ChunkReassemblyBudget.CreateDefault(configuration);

            m_serverCertificates = settings.ServerCertificates!;
            m_bufferManager = new BufferManager(
                m_bufferManagerFactory.Create(
                    "KestrelTcpServer",
                    m_quotas.MaxBufferSize,
                    Telemetry));
            m_channels = new ConcurrentDictionary<uint, (TcpListenerChannel Channel, TaskCompletionSource<bool> Done)>();
            m_callback = callback;
            m_reverseConnectListener = settings.ReverseConnectListener;
            m_maxChannelCount = settings.MaxChannelCount;
            m_pendingAccepts = 0;

            // As in TcpTransportListener the channel limit is enforced
            // against the channel table (ReserveChannel) rather than as a
            // hard lease cap, so the oldest unused SecureChannel without a
            // Session can be closed before a new connection is refused
            // (OPC 10000-4 5.6.2.1).
            m_admission = new UaScConnectionAdmission(
                0,
                settings.ConnectionRateLimiter,
                settings.ResourceIsolationProvider,
                m_quotas.HandshakeTimeout,
                telemetry: Telemetry);

            m_host = BuildHost(baseAddress);
            await m_host.StartAsync(ct).ConfigureAwait(false);

            m_inactivityDetectionTimer?.Dispose();
            m_inactivityDetectionTimer = null;
            int inactivityDetectPeriod = GetInactivityDetectPeriod(m_quotas.ChannelLifetime);
            if (inactivityDetectPeriod > 0)
            {
                m_inactivityDetectionTimer = TimeProvider.System.CreateTimer(
                    DetectInactiveChannels,
                    null,
                    TimeSpan.FromMilliseconds(inactivityDetectPeriod),
                    TimeSpan.FromMilliseconds(inactivityDetectPeriod));
            }
        }

        /// <summary>
        /// Returns the period of the inactivity sweep in milliseconds, or 0
        /// when no sweep runs. As in <see cref="TcpTransportListener"/> a
        /// channel lifetime that is not positive disables the sweep instead
        /// of closing every channel; a tiny lifetime is swept at most once
        /// per <see cref="kMinInactivityDetectPeriod"/>.
        /// </summary>
        internal static int GetInactivityDetectPeriod(int channelLifetime)
        {
            if (channelLifetime <= 0)
            {
                return 0;
            }
            return Math.Max(kMinInactivityDetectPeriod, channelLifetime / 2);
        }

        /// <inheritdoc/>
        public ValueTask CloseAsync(CancellationToken ct = default)
        {
            return StopAsync(ct);
        }

        /// <inheritdoc/>
        public async ValueTask StopAsync(CancellationToken ct = default)
        {
            m_inactivityDetectionTimer?.Dispose();
            m_inactivityDetectionTimer = null;
            try
            {
                m_admission?.Stop();
            }
            catch (AggregateException ex)
            {
                Logger.KestrelTcpAdmissionStopFailed(ex);
            }
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
            try
            {
                string channelIdString = globalChannelId[(ListenerId.Length + 1)..];
                uint channelId = Convert.ToUInt32(channelIdString, CultureInfo.InvariantCulture);

                if (channelId > 0 &&
                    m_channels?.TryGetValue(
                        channelId,
                        out (TcpListenerChannel Channel, TaskCompletionSource<bool> Done) entry) == true)
                {
                    entry.Channel.UpdateLastActiveTime();
                }
            }
            catch
            {
                // ignore errors for calls with invalid channel id
            }
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
            Certificate oldCertificate,
            CancellationToken ct = default)
        {
            if (oldCertificate == null)
            {
                throw new ArgumentNullException(nameof(oldCertificate));
            }

            string oldThumbprint = oldCertificate.Thumbprint;
            if (string.IsNullOrEmpty(oldThumbprint))
            {
                return new ValueTask<IReadOnlyList<string>>([]);
            }

            // Snapshot the channel map so we can iterate without holding
            // any lock the per-channel close paths might also need.
            (TcpListenerChannel Channel, TaskCompletionSource<bool> Done)[] entries =
                m_channels?.Values.ToArray()
                ?? [];

            if (entries.Length == 0)
            {
                return new ValueTask<IReadOnlyList<string>>([]);
            }

            var closed = new List<string>(entries.Length);

            foreach ((TcpListenerChannel Channel, _) in entries)
            {
                try
                {
                    if (Channel.TryCloseForCertificateRotation(oldThumbprint, out string? globalChannelId) &&
                        !string.IsNullOrEmpty(globalChannelId))
                    {
                        closed.Add(globalChannelId!);
                    }
                }
                catch (Exception ex)
                {
                    Logger.FailedToCloseChannelForCertificateRotation(ex, oldThumbprint);
                }
            }

            Logger.ClosedSecureChannelsForCertificateRotation(closed.Count, oldThumbprint);

            return new ValueTask<IReadOnlyList<string>>(closed);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The Kestrel-hosted <c>opc.tcp</c> transport validates the client
        /// application certificate presented during <c>OpenSecureChannel</c>
        /// against the <see cref="TrustListIdentifier.Peers"/> store, so only
        /// a change to that TrustList forces this listener's channels to
        /// renegotiate.
        /// </remarks>
        public TrustListIdentifier PeerCertificateTrustListScope => TrustListIdentifier.Peers;

        /// <inheritdoc/>
        public ValueTask<ArrayOf<string>> CloseChannelsForUntrustedPeerChainsAsync(
            Func<CertificateCollection, CancellationToken, ValueTask<bool>> isPeerTrustedAsync,
            CancellationToken ct = default)
        {
            if (isPeerTrustedAsync == null)
            {
                throw new ArgumentNullException(nameof(isPeerTrustedAsync));
            }
            (TcpListenerChannel Channel, TaskCompletionSource<bool> Done)[] entries =
                m_channels?.Values.ToArray() ?? [];
            return CloseChannelsForUntrustedPeersCoreAsync(entries, isPeerTrustedAsync, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<IReadOnlyList<string>> CloseChannelsForUntrustedPeersAsync(
            Func<Certificate, CancellationToken, ValueTask<bool>> isPeerTrustedAsync,
            CancellationToken ct = default)
        {
            if (isPeerTrustedAsync == null)
            {
                throw new ArgumentNullException(nameof(isPeerTrustedAsync));
            }

            ArrayOf<string> closed = await CloseChannelsForUntrustedPeerChainsAsync(
                (chain, token) => isPeerTrustedAsync(chain[0], token), ct).ConfigureAwait(false);
            return closed.ToArray() ?? [];
        }

        private async ValueTask<ArrayOf<string>> CloseChannelsForUntrustedPeersCoreAsync(
            (TcpListenerChannel Channel, TaskCompletionSource<bool> Done)[] entries,
            Func<CertificateCollection, CancellationToken, ValueTask<bool>> isPeerTrustedAsync,
            CancellationToken ct)
        {
            var closed = new List<string>(entries.Length);
            foreach ((TcpListenerChannel Channel, TaskCompletionSource<bool> Done) in entries)
            {
                TcpListenerChannel channel = Channel;
                ct.ThrowIfCancellationRequested();
                CertificateCollection? peerCertificate = null;
                try
                {
                    peerCertificate = channel.SnapshotClientCertificateChainForRevalidation();
                    if (peerCertificate == null)
                    {
                        // No client certificate (e.g. SecurityPolicy.None) —
                        // the channel is unaffected by a peer-trust change.
                        continue;
                    }

                    bool trusted;
                    try
                    {
                        trusted = await isPeerTrustedAsync(peerCertificate, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Best-effort: if trust cannot be determined, leave
                        // the channel open rather than cutting a possibly
                        // still-trusted peer.
                        Logger.KestrelFailedToRevalidateCertificateForChannel(ex, channel.GlobalChannelId);
                        continue;
                    }

                    if (trusted)
                    {
                        continue;
                    }

                    if (channel.CloseForUntrustedPeerCertificate(out string? globalChannelId) &&
                        !string.IsNullOrEmpty(globalChannelId))
                    {
                        closed.Add(globalChannelId!);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Best-effort: log and continue closing remaining
                    // channels. Failure to close one channel must not block
                    // others from being renegotiated.
                    Logger.KestrelFailedToCloseChannelForPeerTrustChange(ex, channel.GlobalChannelId);
                }
                finally
                {
                    peerCertificate?.Dispose();
                }
            }

            Logger.KestrelClosedUntrustedPeerSecureChannels(closed.Count);

            return closed.ToArrayOf();
        }

        /// <inheritdoc cref="ITcpChannelListener.ReconnectToExistingChannel"/>
        public bool ReconnectToExistingChannel(
            TcpListenerChannel reconnectingChannel,
            IUaSCByteTransport transport,
            uint requestId,
            uint sequenceNumber,
            uint channelId,
            Certificate clientCertificate,
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

        internal bool TryAdmitConnection(
            EndPoint? remoteEndpoint,
            [NotNullWhen(true)] out UaScConnectionAdmission.Lease? lease)
        {
            lease = null;
            if (m_admission == null ||
                !m_admission.TryAcquire(remoteEndpoint, out lease, TryReclaimUnusedChannel))
            {
                return false;
            }
            if (!ReserveChannel())
            {
                lease.Dispose();
                lease = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Releases a channel slot reserved by <see cref="TryAdmitConnection"/>
        /// when the connection ends before <see cref="RegisterChannel"/>.
        /// </summary>
        internal void ReleaseChannelReservation()
        {
            lock (m_lock)
            {
                if (m_pendingAccepts > 0)
                {
                    m_pendingAccepts--;
                }
            }
        }

        /// <summary>
        /// Reserves capacity in the channel table, reclaiming the oldest
        /// unused channel without a session when the limit is reached
        /// (OPC 10000-4 5.6.2.1).
        /// </summary>
        private bool ReserveChannel()
        {
            var attempted = new HashSet<TcpListenerChannel>();
            while (true)
            {
                int channelCount;
                lock (m_lock)
                {
                    if (m_channels == null)
                    {
                        return false;
                    }
                    channelCount = m_channels.Count + m_pendingAccepts;
                    if (m_maxChannelCount <= 0 || channelCount < m_maxChannelCount)
                    {
                        m_pendingAccepts++;
                        return true;
                    }
                }
                if (!TryReclaimUnusedChannel(attempted))
                {
                    Logger.KestrelTcpChannelLimitReached(channelCount, m_maxChannelCount);
                    return false;
                }
            }
        }

        private bool TryReclaimUnusedChannel()
        {
            return TryReclaimUnusedChannel([]);
        }

        /// <summary>
        /// Closes the least recently active open channel that serves no
        /// session, mirroring <see cref="TcpTransportListener"/>.
        /// </summary>
        private bool TryReclaimUnusedChannel(HashSet<TcpListenerChannel> attempted)
        {
            ConcurrentDictionary<uint, (TcpListenerChannel Channel, TaskCompletionSource<bool> Done)>? channels;
            TcpListenerChannel[] candidates;
            lock (m_lock)
            {
                channels = m_channels;
                if (channels == null)
                {
                    return false;
                }
                candidates = [.. channels.Values.Select(entry => entry.Channel)];
            }
            foreach (TcpListenerChannel candidate in candidates.OrderByDescending(
                channel => channel.ElapsedSinceLastActiveTime))
            {
                if (attempted.Contains(candidate) || candidate.UsedBySession)
                {
                    continue;
                }
                lock (m_lock)
                {
                    // The snapshot may be stale: only reclaim a channel that is
                    // still the registered channel for its id in the live table.
                    if (!ReferenceEquals(channels, m_channels) ||
                        !channels.TryGetValue(
                            candidate.Id,
                            out (TcpListenerChannel Channel, TaskCompletionSource<bool> Done) registered) ||
                        !ReferenceEquals(candidate, registered.Channel) ||
                        !m_idleCleanupClaims.Add(candidate))
                    {
                        continue;
                    }
                }
                attempted.Add(candidate);
                try
                {
                    if (candidate.TryIdleCleanupForAdmission())
                    {
                        Logger.KestrelTcpReclaimedUnusedChannel(candidate.Id);
                        return true;
                    }
                }
                finally
                {
                    lock (m_lock)
                    {
                        m_idleCleanupClaims.Remove(candidate);
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// The inactivity timer callback: closes channels that were not
        /// active for longer than the channel lifetime, mirroring
        /// <see cref="TcpTransportListener"/>.
        /// </summary>
        private void DetectInactiveChannels(object? state)
        {
            ConcurrentDictionary<uint, (TcpListenerChannel Channel, TaskCompletionSource<bool> Done)>? channels =
                m_channels;
            ChannelQuotas? quotas = m_quotas;
            if (channels == null || quotas == null)
            {
                return;
            }
            try
            {
                foreach ((TcpListenerChannel Channel, TaskCompletionSource<bool> _) in channels.Values)
                {
                    if (Channel.ElapsedSinceLastActiveTime > quotas.ChannelLifetime)
                    {
                        Logger.KestrelTcpInactiveChannelCleanup(Channel.Id);
                        Channel.IdleCleanup();
                    }
                }
            }
            catch (Exception ex)
            {
                // Timer callback: never let an exception escape.
                Logger.KestrelTcpInactivityDetectionFailed(ex);
            }
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

                // OPC 10000-4 6.5.5: OpenSecureChannel, CloseSecureChannel and
                // certificate errors raise audit events, as in TcpTransportListener.
                serverChannel.SetReportOpenSecureChannelAuditCallback(OnReportAuditOpenSecureChannelEvent);
                serverChannel.SetReportCloseSecureChannelAuditCallback(OnReportAuditCloseSecureChannelEvent);
                serverChannel.SetReportCertificateAuditCallback(OnReportAuditCertificateEvent);
            }
            lock (m_lock)
            {
                ConcurrentDictionary<uint, (TcpListenerChannel Channel, TaskCompletionSource<bool> Done)> channels =
                    m_channels ?? throw new InvalidOperationException("KestrelTcpTransportListener is not opened.");
                if (m_pendingAccepts > 0)
                {
                    m_pendingAccepts--;
                }
                channels.TryAdd(channelId, (
                    channel,
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)));
            }
        }

        internal void UnregisterChannel(uint channelId)
        {
            if (m_channels != null && m_channels.TryRemove(channelId, out (TcpListenerChannel Channel, TaskCompletionSource<bool> Done) entry))
            {
                entry.Done.TrySetResult(true);
            }
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
                !m_channels.TryRemove(channelId, out (TcpListenerChannel Channel, TaskCompletionSource<bool> Done) entry))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "Could not find secure channel request.");
            }

            TcpListenerChannel channel = entry.Channel;
            IUaSCByteTransport? transport = null;
            try
            {
                ConnectionWaitingHandlerAsync? handler = ConnectionWaiting;
                if (handler != null)
                {
                    transport = await channel.DetachTransportAsync()
                        .ConfigureAwait(false);
                    if (transport != null)
                    {
                        var args = new TcpConnectionWaitingEventArgs(
                            serverUri,
                            endpointUrl,
                            transport);
                        await handler(this, args).ConfigureAwait(false);
                        accepted = args.Accepted;
                        if (accepted)
                        {
                            if (transport is IUaSCHandshakeCompletionSource completion)
                            {
                                completion.CompleteHandshake();
                            }
                        }
                        else
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
                else
                {
                    channel.Dispose();
                }
            }
            catch
            {
                try
                {
                    transport?.Close();
                }
                finally
                {
                    channel.Dispose();
                }
                throw;
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
            // Keeps a sessionless channel classified as in use while the
            // request is processed so admission does not reclaim it.
            using IDisposable usage = channel.TrackPendingRequest();
            try
            {
                var context = new SecureChannelContext(
                    channel.GlobalChannelId,
                    channel.EndpointDescription,
                    RequestEncoding.Binary,
                    channel.ClientCertificateRawData,
                    channel.ServerCertificateRawData,
                    channel.ChannelThumbprint,
                    (channel.Transport?.RemoteEndpoint as IPEndPoint)?.Address);
                IServiceResponse response = await m_callback
                    .ProcessRequestAsync(context, request)
                    .ConfigureAwait(false);
                await ((TcpServerChannel)channel).SendResponseAsync(requestId, response).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.RequestProcessingFailed(ex);
                try
                {
                    ServiceFault fault = EndpointBase.CreateFault(Logger, request, ex);
                    await ((TcpServerChannel)channel).SendResponseAsync(requestId, fault).ConfigureAwait(false);
                }
                catch (Exception faultEx)
                {
                    Logger.FailedToSendFaultResponse(faultEx);
                }
            }
        }

        /// <summary>
        /// Callback for reporting the open secure channel audit event.
        /// </summary>
        private void OnReportAuditOpenSecureChannelEvent(
            TcpServerChannel channel,
            OpenSecureChannelRequest request,
            Certificate? clientCertificate,
            Exception? exception)
        {
            try
            {
                m_callback?.ReportAuditOpenSecureChannelEvent(
                    channel.GlobalChannelId,
                    channel.EndpointDescription!,
                    request,
                    clientCertificate!,
                    exception!);
            }
            catch (Exception e)
            {
                Logger.KestrelTcpAuditReportFailed(e);
            }
        }

        /// <summary>
        /// Callback for reporting the close secure channel audit event.
        /// </summary>
        private void OnReportAuditCloseSecureChannelEvent(
            TcpServerChannel channel,
            Exception exception)
        {
            try
            {
                m_callback?.ReportAuditCloseSecureChannelEvent(channel.GlobalChannelId, exception);
            }
            catch (Exception e)
            {
                Logger.KestrelTcpAuditReportFailed(e);
            }
        }

        /// <summary>
        /// Callback for reporting the certificate audit events.
        /// </summary>
        private void OnReportAuditCertificateEvent(
            Certificate clientCertificate,
            Exception exception)
        {
            try
            {
                m_callback?.ReportAuditCertificateEvent(clientCertificate, exception);
            }
            catch (Exception e)
            {
                Logger.KestrelTcpAuditReportFailed(e);
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
        private readonly IBufferManagerFactory m_bufferManagerFactory;
        private ICertificateRegistry? m_serverCertificates;
        private ITransportListenerCallback? m_callback;
        private ConcurrentDictionary<uint, (TcpListenerChannel Channel, TaskCompletionSource<bool> Done)>? m_channels;
        private int m_nextChannelId;
        private bool m_reverseConnectListener;
        private UaScConnectionAdmission? m_admission;
        private readonly Lock m_lock = new();

        /// <summary>
        /// Tracks channels already selected for reclamation by concurrent admission attempts.
        /// </summary>
        private readonly HashSet<TcpListenerChannel> m_idleCleanupClaims = [];

        /// <summary>
        /// Counts reserved admission slots not yet represented by registered channels.
        /// </summary>
        private int m_pendingAccepts;
        private int m_maxChannelCount;
        private ITimer? m_inactivityDetectionTimer;

        /// <summary>
        /// The shortest period of the inactivity sweep in milliseconds.
        /// </summary>
        private const int kMinInactivityDetectPeriod = 1000;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="KestrelTcpTransportListener"/>.
    /// </summary>
    internal static partial class KestrelTcpTransportListenerLog
    {
        [LoggerMessage(EventId = BindingsHttpsEventIds.KestrelTcpTransportListener + 0, Level = LogLevel.Warning,
            Message = "KestrelTcp failed to close channel for certificate rotation (thumbprint {Thumbprint}).")]
        public static partial void FailedToCloseChannelForCertificateRotation(
            this ILogger logger,
            Exception exception,
            string thumbprint);

        [LoggerMessage(EventId = BindingsHttpsEventIds.KestrelTcpTransportListener + 1, Level = LogLevel.Information,
            Message = "KestrelTcp closed {Count} SecureChannel(s) for certificate rotation (thumbprint {Thumbprint}).")]
        public static partial void ClosedSecureChannelsForCertificateRotation(
            this ILogger logger,
            int count,
            string thumbprint);

        [LoggerMessage(EventId = BindingsHttpsEventIds.KestrelTcpTransportListener + 2, Level = LogLevel.Error,
            Message = "KestrelTcp request processing failed.")]
        public static partial void RequestProcessingFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = BindingsHttpsEventIds.KestrelTcpTransportListener + 3, Level = LogLevel.Error,
            Message = "KestrelTcp failed to send fault response.")]
        public static partial void FailedToSendFaultResponse(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = BindingsHttpsEventIds.KestrelTcpTransportListener + 4, Level = LogLevel.Warning,
            Message = "KestrelTcp failed to re-validate certificate for channel {ChannelId}; leaving it open.")]
        public static partial void KestrelFailedToRevalidateCertificateForChannel(
            this ILogger logger,
            Exception exception,
            string channelId);

        [LoggerMessage(EventId = BindingsHttpsEventIds.KestrelTcpTransportListener + 5, Level = LogLevel.Warning,
            Message = "KestrelTcp failed to close channel {ChannelId} for peer-certificate trust change.")]
        public static partial void KestrelFailedToCloseChannelForPeerTrustChange(
            this ILogger logger,
            Exception exception,
            string channelId);

        [LoggerMessage(EventId = BindingsHttpsEventIds.KestrelTcpTransportListener + 6, Level = LogLevel.Information,
            Message = "KestrelTcp closed {Count} SecureChannel(s) whose peer certificate is no longer trusted.")]
        public static partial void KestrelClosedUntrustedPeerSecureChannels(this ILogger logger, int count);

        [LoggerMessage(EventId = BindingsHttpsEventIds.KestrelTcpTransportListener + 7, Level = LogLevel.Error,
            Message = "Kestrel TCP failed to close one or more admitted connections during listener shutdown.")]
        public static partial void KestrelTcpAdmissionStopFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = BindingsHttpsEventIds.KestrelTcpChannelLifetime + 0, Level = LogLevel.Warning,
            Message = "KestrelTcp maximum channel count reached ({ChannelCount}/{MaxChannelCount}) and no unused channel could be reclaimed.")]
        public static partial void KestrelTcpChannelLimitReached(
            this ILogger logger,
            int channelCount,
            int maxChannelCount);

        [LoggerMessage(EventId = BindingsHttpsEventIds.KestrelTcpChannelLifetime + 1, Level = LogLevel.Information,
            Message = "KestrelTcp closed unused channel {ChannelId} without a session to admit a new connection.")]
        public static partial void KestrelTcpReclaimedUnusedChannel(this ILogger logger, uint channelId);

        [LoggerMessage(EventId = BindingsHttpsEventIds.KestrelTcpChannelLifetime + 2, Level = LogLevel.Information,
            Message = "KestrelTcp closing channel {ChannelId} due to inactivity.")]
        public static partial void KestrelTcpInactiveChannelCleanup(this ILogger logger, uint channelId);

        [LoggerMessage(EventId = BindingsHttpsEventIds.KestrelTcpChannelLifetime + 3, Level = LogLevel.Error,
            Message = "KestrelTcp inactivity detection failed.")]
        public static partial void KestrelTcpInactivityDetectionFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = BindingsHttpsEventIds.KestrelTcpChannelLifetime + 4, Level = LogLevel.Error,
            Message = "KestrelTcp failed to report a SecureChannel audit event.")]
        public static partial void KestrelTcpAuditReportFailed(this ILogger logger, Exception exception);
    }
}
#endif // NET8_0_OR_GREATER
