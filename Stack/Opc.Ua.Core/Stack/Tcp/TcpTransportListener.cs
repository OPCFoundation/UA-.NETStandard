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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Creates a new TcpTransportListener with ITransportListener interface.
    /// </summary>
    public class TcpTransportListenerFactory : TcpServiceHost
    {
        /// <inheritdoc/>
        public override string UriScheme => Utils.UriSchemeOpcTcp;

        /// <inheritdoc/>
        public override ITransportListener Create(ITelemetryContext telemetry)
        {
            return new TcpTransportListener(telemetry);
        }
    }

    /// <summary>
    /// Represents a potential problematic ActiveClient
    /// </summary>
    public class ActiveClient
    {
        /// <summary>
        /// Time of the last recorded problematic action
        /// </summary>
        public int LastActionTicks { get; set; }

        /// <summary>
        /// Counter for number of recorded potential problematic actions
        /// </summary>
        public int ActiveActionCount { get; set; }

        /// <summary>
        /// Ticks until the client is Blocked
        /// </summary>
        public int BlockedUntilTicks { get; set; }
    }

    /// <summary>
    /// Manages clients with potential problematic activities
    /// </summary>
    internal sealed class ActiveClientTracker : IDisposable
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public ActiveClientTracker(ITelemetryContext telemetry)
        {
            m_logger = telemetry.CreateLogger<ActiveClientTracker>();
            m_cleanupTimer = new Timer(
                CleanupExpiredEntries,
                null,
                kCleanupIntervalMs,
                kCleanupIntervalMs);
        }

        /// <summary>
        /// Checks if an IP address is currently blocked
        /// </summary>
        public bool IsBlocked(IPAddress ipAddress)
        {
            if (m_activeClients.TryGetValue(ipAddress, out ActiveClient client))
            {
                int currentTicks = HiResClock.TickCount;
                return IsBlockedTicks(client.BlockedUntilTicks, currentTicks);
            }
            return false;
        }

        /// <summary>
        /// Adds a potential problematic action entry for a client
        /// </summary>
        public void AddClientAction(IPAddress ipAddress)
        {
            int currentTicks = HiResClock.TickCount;

            m_activeClients.AddOrUpdate(
                ipAddress,
                // If client is new , create a new entry
                key => new ActiveClient
                {
                    LastActionTicks = currentTicks,
                    ActiveActionCount = 1,
                    BlockedUntilTicks = 0
                },
                // If the client exists, update its entry
                (key, existingEntry) =>
                {
                    // If IP currently blocked simply do nothing
                    if (IsBlockedTicks(existingEntry.BlockedUntilTicks, currentTicks))
                    {
                        return existingEntry;
                    }

                    // Elapsed time since last recorded action
                    int elapsedSinceLastRecAction = currentTicks - existingEntry.LastActionTicks;

                    if (elapsedSinceLastRecAction <= kActionsIntervalMs)
                    {
                        existingEntry.ActiveActionCount++;

                        if (existingEntry.ActiveActionCount > kNrActionsTillBlock)
                        {
                            // Block the IP
                            existingEntry.BlockedUntilTicks = currentTicks + kBlockDurationMs;
                            m_logger.LogError(
                                "RemoteClient IPAddress: {IpAddress} blocked for {Duration} ms due to exceeding {ActionCOunt} actions under {ActionInterval} ms ",
                                ipAddress.ToString(),
                                kBlockDurationMs,
                                kNrActionsTillBlock,
                                kActionsIntervalMs);
                        }
                    }
                    else
                    {
                        // Reset the count as the last action was outside the interval
                        existingEntry.ActiveActionCount = 1;
                    }

                    existingEntry.LastActionTicks = currentTicks;

                    return existingEntry;
                });
        }

        /// <summary>
        /// Dispose the cleanup timer
        /// </summary>
        public void Dispose()
        {
            m_cleanupTimer?.Dispose();
        }

        /// <summary>
        /// Periodically cleans up expired active client entries to avoid memory leak and unblock clients whose duration has expired.
        /// </summary>
        private void CleanupExpiredEntries(object state)
        {
            int currentTicks = HiResClock.TickCount;

            foreach (KeyValuePair<IPAddress, ActiveClient> entry in m_activeClients)
            {
                IPAddress clientIp = entry.Key;
                ActiveClient rClient = entry.Value;

                // Unblock client if blocking duration has been exceeded
                if (rClient.BlockedUntilTicks != 0 &&
                    !IsBlockedTicks(rClient.BlockedUntilTicks, currentTicks))
                {
                    rClient.BlockedUntilTicks = 0;
                    rClient.ActiveActionCount = 0;
                    m_logger.LogDebug(
                        "Active Client with IP {IpAddress} is now unblocked, blocking duration of {BlockDurationMs} ms has been exceeded",
                        clientIp.ToString(),
                        kBlockDurationMs);
                }

                // Remove clients that haven't had any potential problematic actions in the last m_kEntryExpirationMs interval
                int elapsedSinceBadActionTicks = currentTicks - rClient.LastActionTicks;
                if (elapsedSinceBadActionTicks > kEntryExpirationMs)
                {
                    // Even if TryRemove fails it will most probably succeed at the next execution
                    if (m_activeClients.TryRemove(clientIp, out _))
                    {
                        m_logger.LogDebug(
                            "Active Client with IP {IpAddress} is not tracked any longer, hasn't had actions for more than {ExpirationMs} ms",
                            clientIp.ToString(),
                            kEntryExpirationMs);
                    }
                }
            }
        }

        /// <summary>
        /// Determines if the IP is currently blocked based on the block expiration ticks and current ticks
        /// </summary>
        private static bool IsBlockedTicks(int blockedUntilTicks, int currentTicks)
        {
            if (blockedUntilTicks == 0)
            {
                return false;
            }
            // C# signed arithmetic
            int diff = blockedUntilTicks - currentTicks;
            // If currentTicks < blockedUntilTicks then it is still blocked
            // Works even if TickCount has wrapped around due to C# signed integer arithmetic
            return diff > 0;
        }

        private readonly ConcurrentDictionary<IPAddress, ActiveClient> m_activeClients = new();

        private const int kActionsIntervalMs = 10_000;
        private const int kNrActionsTillBlock = 3;

        /// <summary>
        /// 30 seconds
        /// </summary>
        private const int kBlockDurationMs = 30_000;
        private const int kCleanupIntervalMs = 15_000;

        /// <summary>
        /// 10 minutes
        /// </summary>
        private const int kEntryExpirationMs = 600_000;

        private readonly ILogger m_logger;
        private readonly Timer m_cleanupTimer;
    }

    /// <summary>
    /// Manages the transport for a UA TCP server.
    /// </summary>
    public class TcpTransportListener : ITransportListener, ITcpChannelListener
    {
        /// <summary>
        /// The limit of queued connections for the listener socket..
        /// </summary>
        private const int kSocketBacklog = 10;

        /// <summary>
        /// Create listener
        /// </summary>
        /// <param name="telemetry">Telemetry context to use</param>
        public TcpTransportListener(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<TcpTransportListener>();
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (m_lock)
                {
                    if (m_inactivityDetectionTimer != null)
                    {
                        Utils.SilentDispose(m_inactivityDetectionTimer);
                        m_inactivityDetectionTimer = null;
                    }

                    if (m_listeningSocket != null)
                    {
                        Utils.SilentDispose(m_listeningSocket);
                        m_listeningSocket = null;
                    }

                    if (m_listeningSocketIPv6 != null)
                    {
                        Utils.SilentDispose(m_listeningSocketIPv6);
                        m_listeningSocketIPv6 = null;
                    }

                    if (m_channels != null)
                    {
                        KeyValuePair<uint, TcpListenerChannel>[] channels = [.. m_channels];
                        m_channels.Clear();
                        m_channels = null;
                        foreach (KeyValuePair<uint, TcpListenerChannel> channelKeyValue in channels)
                        {
                            Utils.SilentDispose(channelKeyValue.Value);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The URI scheme handled by the listener.
        /// </summary>
        public string UriScheme => Utils.UriSchemeOpcTcp;

        /// <summary>
        /// The Id of the transport listener.
        /// </summary>
        public string ListenerId { get; private set; }

        /// <summary>
        /// Opens the listener and starts accepting connection.
        /// </summary>
        /// <param name="baseAddress">The base address.</param>
        /// <param name="settings">The settings to use when creating the listener.</param>
        /// <param name="callback">The callback to use when requests arrive via the channel.</param>
        /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Open(
            Uri baseAddress,
            TransportListenerSettings settings,
            ITransportListenerCallback callback)
        {
            // assign a unique guid to the listener.
            ListenerId = Guid.NewGuid().ToString();

            EndpointUrl = baseAddress;
            m_descriptions = settings.Descriptions;
            EndpointConfiguration configuration = settings.Configuration;

            // initialize the quotas.
            var messageContext = new ServiceMessageContext(m_telemetry)
            {
                NamespaceUris = settings.NamespaceUris,
                ServerUris = new StringTable(),
                Factory = settings.Factory
            };
            m_quotas = new ChannelQuotas(messageContext);

            if (configuration != null)
            {
                m_inactivityDetectPeriod = configuration.ChannelLifetime / 2;
                m_quotas.MaxBufferSize = configuration.MaxBufferSize;
                m_quotas.MaxMessageSize = TcpMessageLimits.AlignRoundMaxMessageSize(
                    configuration.MaxMessageSize);
                m_quotas.ChannelLifetime = configuration.ChannelLifetime;
                m_quotas.SecurityTokenLifetime = configuration.SecurityTokenLifetime;
                messageContext.MaxArrayLength = configuration.MaxArrayLength;
                messageContext.MaxByteStringLength = configuration.MaxByteStringLength;
                messageContext.MaxMessageSize = TcpMessageLimits.AlignRoundMaxMessageSize(
                    configuration.MaxMessageSize);
                messageContext.MaxStringLength = configuration.MaxStringLength;
                messageContext.MaxEncodingNestingLevels = configuration.MaxEncodingNestingLevels;
                messageContext.MaxDecoderRecoveries = configuration.MaxDecoderRecoveries;
            }

            m_quotas.CertificateValidator = settings.CertificateValidator;

            // save the server certificate.
            m_serverCertificateTypesProvider = settings.ServerCertificateTypesProvider;

            m_bufferManager = new BufferManager("Server", m_quotas.MaxBufferSize, m_telemetry);
            m_channels = new ConcurrentDictionary<uint, TcpListenerChannel>();
            m_reverseConnectListener = settings.ReverseConnectListener;
            MaxChannelCount = settings.MaxChannelCount;

            // save the callback to the server.
            m_callback = callback;

            // start the listener.
            Start();
        }

        /// <summary>
        /// Closes the listener and stops accepting connection.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Close()
        {
            Stop();
        }

        /// <inheritdoc/>
        public void UpdateChannelLastActiveTime(string globalChannelId)
        {
            try
            {
                string channelIdString = globalChannelId[(ListenerId.Length + 1)..];
                uint channelId = Convert.ToUInt32(channelIdString, CultureInfo.InvariantCulture);

                if (channelId > 0 &&
                    m_channels?.TryGetValue(channelId, out TcpListenerChannel channel) == true)
                {
                    channel?.UpdateLastActiveTime();
                }
            }
            catch
            {
                // ignore errors for calls with invalid channel id
            }
        }

        /// <summary>
        /// Gets the URL for the listener's endpoint.
        /// </summary>
        /// <value>The URL for the listener's endpoint.</value>
        public Uri EndpointUrl { get; private set; }

        /// <summary>
        /// Binds a new socket to an existing channel.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public bool ReconnectToExistingChannel(
            IMessageSocket socket,
            uint requestId,
            uint sequenceNumber,
            uint channelId,
            X509Certificate2 clientCertificate,
            ChannelToken token,
            OpenSecureChannelRequest request)
        {
            TcpListenerChannel channel = null;

            lock (m_lock)
            {
                if (m_channels?.TryGetValue(channelId, out channel) != true)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTcpSecureChannelUnknown,
                        "Could not find secure channel referenced in the OpenSecureChannel request.");
                }
            }

            channel.Reconnect(socket, requestId, sequenceNumber, clientCertificate, token, request);

            m_logger.LogInformation("ChannelId {Id}: reconnected", channelId);
            return true;
        }

        /// <summary>
        /// Called when a channel closes.
        /// </summary>
        public void ChannelClosed(uint channelId)
        {
            if (m_channels?.TryRemove(channelId, out TcpListenerChannel channel) == true)
            {
                Utils.SilentDispose(channel);
                m_logger.LogInformation("ChannelId {Id}: closed", channelId);
            }
            else
            {
                m_logger.LogInformation("ChannelId {Id}: closed, but channel was not found", channelId);
            }
        }

        /// <summary>
        /// Raised when a new connection is waiting for a client.
        /// </summary>
        public event ConnectionWaitingHandlerAsync ConnectionWaiting;

        /// <summary>
        /// Raised when a monitored connection's status changed.
        /// </summary>
        public event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged;

        /// <inheritdoc/>
        public void CreateReverseConnection(Uri url, int timeout)
        {
            var channel = new TcpServerChannel(
                ListenerId,
                this,
                m_bufferManager,
                m_quotas,
                m_serverCertificateTypesProvider,
                m_descriptions,
                m_telemetry);

            uint channelId = GetNextChannelId();
            channel.StatusChanged += Channel_StatusChanged;
            channel.BeginReverseConnect(
                channelId,
                url,
                OnReverseHelloComplete,
                channel,
                Math.Min(timeout, m_quotas.ChannelLifetime));
        }

        private void Channel_StatusChanged(
            TcpServerChannel channel,
            ServiceResult status,
            bool closed)
        {
            ConnectionStatusChanged?.Invoke(
                this,
                new ConnectionStatusEventArgs(channel.ReverseConnectionUrl, status, closed));
        }

        /// <summary>
        /// Indicate that the reverse hello connection attempt completed.
        /// </summary>
        /// <remarks>
        /// The server tried to connect to a client using a reverse hello message.
        /// </remarks>
        /// <exception cref="ServiceResultException"></exception>
        private void OnReverseHelloComplete(IAsyncResult result)
        {
            var channel = (TcpServerChannel)result.AsyncState;
            try
            {
                channel.EndReverseConnect(result);

                if (!m_channels.TryAdd(channel.Id, channel))
                {
                    throw new ServiceResultException(StatusCodes.BadInternalError);
                }

                if (m_callback != null)
                {
                    channel.SetRequestReceivedCallback(
                        new TcpChannelRequestEventHandler(OnRequestReceivedAsync));
                    channel.SetReportOpenSecureChannelAuditCallback(
                        new ReportAuditOpenSecureChannelEventHandler(
                            OnReportAuditOpenSecureChannelEvent));
                    channel.SetReportCloseSecureChannelAuditCallback(
                        new ReportAuditCloseSecureChannelEventHandler(
                            OnReportAuditCloseSecureChannelEvent));
                    channel.SetReportCertificateAuditCallback(
                        new ReportAuditCertificateEventHandler(OnReportAuditCertificateEvent));
                }

                channel = null;
            }
            catch (Exception e)
            {
                ConnectionStatusChanged?.Invoke(
                    this,
                    new ConnectionStatusEventArgs(
                        channel.ReverseConnectionUrl,
                        new ServiceResult(e),
                        true));
            }
            finally
            {
                Utils.SilentDispose(channel);
            }
        }

        /// <summary>
        /// Starts listening at the specified port.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void Start()
        {
            lock (m_lock)
            {
                // Track potential problematic client behavior only if Basic128Rsa15 security policy is offered
                if (m_descriptions != null &&
                    m_descriptions.Any(d => d.SecurityPolicyUri == SecurityPolicies.Basic128Rsa15))
                {
                    m_activeClientTracker = new ActiveClientTracker(m_telemetry);
                }

                // ensure a valid port.
                int port = EndpointUrl.Port;

                if (port is <= 0 or > ushort.MaxValue)
                {
                    port = Utils.UaTcpDefaultPort;
                }

                UriHostNameType hostType = Uri.CheckHostName(EndpointUrl.Host);
                bool bindToSpecifiedAddress =
                    hostType is not UriHostNameType.Dns and not UriHostNameType.Unknown and not UriHostNameType.Basic;
                IPAddress ipAddress = bindToSpecifiedAddress
                    ? IPAddress.Parse(EndpointUrl.Host)
                    : IPAddress.Any;

                // create IPv4 or IPv6 socket.
                try
                {
                    var endpoint = new IPEndPoint(ipAddress, port);
                    m_listeningSocket = new Socket(
                        endpoint.AddressFamily,
                        SocketType.Stream,
                        ProtocolType.Tcp)
                    {
                        NoDelay = true,
                        LingerState = new LingerOption(true, 5)
                    };
                    var args = new SocketAsyncEventArgs();
                    args.Completed += OnAccept;
                    args.UserToken = m_listeningSocket;
                    m_listeningSocket.Bind(endpoint);
                    m_listeningSocket.Listen(kSocketBacklog);

                    m_inactivityDetectionTimer = new Timer(
                        DetectInactiveChannels,
                        null,
                        m_inactivityDetectPeriod,
                        m_inactivityDetectPeriod);

                    if (!m_listeningSocket.AcceptAsync(args))
                    {
                        OnAccept(null, args);
                    }
                }
                catch (Exception ex)
                {
                    // no IPv4 support.
                    if (m_listeningSocket != null)
                    {
                        m_listeningSocket.Dispose();
                        m_listeningSocket = null;
                    }
                    m_logger.LogWarning("Failed to create IPv4 listening socket: {Message}", ex.Message);
                }

                if (ipAddress == IPAddress.Any)
                {
                    // create IPv6 socket
                    try
                    {
                        var endpointIPv6 = new IPEndPoint(IPAddress.IPv6Any, port);
                        m_listeningSocketIPv6 = new Socket(
                            endpointIPv6.AddressFamily,
                            SocketType.Stream,
                            ProtocolType.Tcp)
                        {
                            NoDelay = true,
                            LingerState = new LingerOption(true, 5)
                        };
                        var args = new SocketAsyncEventArgs { UserToken = m_listeningSocketIPv6 };
                        args.Completed += OnAccept;

                        m_listeningSocketIPv6.Bind(endpointIPv6);
                        m_listeningSocketIPv6.Listen(kSocketBacklog);
                        if (!m_listeningSocketIPv6.AcceptAsync(args))
                        {
                            OnAccept(null, args);
                        }
                    }
                    catch (Exception ex)
                    {
                        // no IPv6 support
                        if (m_listeningSocketIPv6 != null)
                        {
                            m_listeningSocketIPv6.Dispose();
                            m_listeningSocketIPv6 = null;
                        }
                        m_logger.LogWarning("Failed to create IPv6 listening socket: {Message}", ex.Message);
                    }
                }
                if (m_listeningSocketIPv6 == null && m_listeningSocket == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNoCommunication,
                        "Failed to establish tcp listener sockets for Ipv4 and IPv6.");
                }
            }
        }

        /// <summary>
        /// Stops listening.
        /// </summary>
        public void Stop()
        {
            lock (m_lock)
            {
                ConnectionWaiting = null;
                ConnectionStatusChanged = null;

                if (m_listeningSocket != null)
                {
                    m_listeningSocket.Dispose();
                    m_listeningSocket = null;
                }

                if (m_listeningSocketIPv6 != null)
                {
                    m_listeningSocketIPv6.Dispose();
                    m_listeningSocketIPv6 = null;
                }
            }
        }

        /// <summary>
        /// Transfers the channel to a waiting connection.
        /// </summary>
        /// <returns>TRUE if the channel should be kept open; FALSE otherwise.</returns>
        [Obsolete("Use TransferListenerChannelAsync instead.")]
        public Task<bool> TransferListenerChannel(uint channelId, string serverUri, Uri endpointUrl)
        {
            return TransferListenerChannelAsync(channelId, serverUri, endpointUrl);
        }

        /// <summary>
        /// Transfers the channel to a waiting connection.
        /// </summary>
        /// <returns>TRUE if the channel should be kept open; FALSE otherwise.</returns>
        /// <exception cref="ServiceResultException"></exception>
        public async Task<bool> TransferListenerChannelAsync(
            uint channelId,
            string serverUri,
            Uri endpointUrl)
        {
            bool accepted = false;

            // remove it so it does not get cleaned up as an inactive connection.
            if (m_channels?.TryRemove(channelId, out TcpListenerChannel channel) != true)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "Could not find secure channel request.");
            }

            // notify the application.
            if (ConnectionWaiting != null)
            {
                var args = new TcpConnectionWaitingEventArgs(
                    serverUri,
                    endpointUrl,
                    channel.Socket);
                await ConnectionWaiting(this, args).ConfigureAwait(false);
                accepted = args.Accepted;
            }

            if (!accepted)
            {
                // add back in for other connection attempt.
                m_channels?.TryAdd(channelId, channel);
            }

            return accepted;
        }

        /// <summary>
        /// Called when a UpdateCertificate event occured.
        /// </summary>
        public void CertificateUpdate(
            ICertificateValidator validator,
            CertificateTypesProvider serverCertificateTypes)
        {
            m_quotas.CertificateValidator = validator;
            m_serverCertificateTypesProvider = serverCertificateTypes;
            foreach (EndpointDescription description in m_descriptions)
            {
                // TODO: why only if SERVERCERT != null
                if (description.ServerCertificate != null)
                {
                    X509Certificate2 serverCertificate = serverCertificateTypes
                        .GetInstanceCertificate(
                            description.SecurityPolicyUri);
                    if (serverCertificateTypes.SendCertificateChain)
                    {
                        description.ServerCertificate = serverCertificateTypes
                            .LoadCertificateChainRaw(
                                serverCertificate);
                    }
                    else
                    {
                        description.ServerCertificate = serverCertificate.RawData;
                    }
                }
            }
        }

        /// <summary>
        /// Mark a remote endpoint as potential problematic
        /// </summary>
        internal void MarkAsPotentialProblematic(IPAddress remoteEndpoint)
        {
            m_logger.LogDebug(
                "MarkClientAsPotentialProblematic address: {RemoteEndpoint} ",
                remoteEndpoint.ToString());
            m_activeClientTracker?.AddClientAction(remoteEndpoint);
        }

        /// <summary>
        /// Handles a new connection.
        /// </summary>
        private void OnAccept(object sender, SocketAsyncEventArgs e)
        {
            TcpListenerChannel channel = null;
            bool repeatAccept = false;
            do
            {
                bool isBlocked = false;

                // Track potential problematic client behavior only if Basic128Rsa15 security policy is offered
                if (m_activeClientTracker != null)
                {
                    // Filter out the Remote IP addresses which are detected with potential problematic behavior
                    IPAddress ipAddress = ((IPEndPoint)e?.AcceptSocket?.RemoteEndPoint)?.Address;
                    if (ipAddress != null && m_activeClientTracker.IsBlocked(ipAddress))
                    {
                        m_logger.LogDebug(
                            "OnAccept: RemoteEndpoint address: {IpAddress} refused access for behaving as potential problematic ",
                            ((IPEndPoint)e.AcceptSocket.RemoteEndPoint).Address);
                        isBlocked = true;
                    }
                }

                repeatAccept = false;
                lock (m_lock)
                {
                    if (e.UserToken is not Socket listeningSocket)
                    {
                        m_logger.LogError("OnAccept: Listensocket was null.");
                        e.Dispose();
                        return;
                    }

                    ConcurrentDictionary<uint, TcpListenerChannel> channels = m_channels;
                    if (channels != null && !isBlocked)
                    {
                        // TODO: .Count is flagged as hotpath, implement separate counter
                        int channelCount = channels.Count;

                        // Remove oldest channel that does not have a session attached to it
                        // before reaching m_maxChannelCount
                        if (MaxChannelCount > 0 && MaxChannelCount == channelCount)
                        {
                            KeyValuePair<uint, TcpListenerChannel>[] snapshot = [.. channels];

                            // Identify channels without established sessions
                            KeyValuePair<uint, TcpListenerChannel>[] nonSessionChannels =
                            [
                                .. snapshot.Where(ch => !ch.Value.UsedBySession)
                            ];

                            if (nonSessionChannels.Length != 0)
                            {
                                KeyValuePair<uint, TcpListenerChannel> oldestIdChannel
                                    = nonSessionChannels.Aggregate(
                                    (max, current) =>
                                        current.Value.ElapsedSinceLastActiveTime > max.Value
                                            .ElapsedSinceLastActiveTime
                                            ? current
                                            : max);

                                m_logger.LogInformation(
                                    "TCPLISTENER: Channel Id {Id} scheduled for IdleCleanup - Oldest without established session.",
                                    oldestIdChannel.Value.Id);
                                oldestIdChannel.Value.IdleCleanup();
                                m_logger.LogInformation(
                                    "TCPLISTENER: Channel Id {Id} finished IdleCleanup - Oldest without established session.",
                                    oldestIdChannel.Value.Id);

                                channelCount--;
                            }
                        }

                        bool serveChannel = !(MaxChannelCount > 0 &&
                            MaxChannelCount < channelCount);
                        if (!serveChannel)
                        {
                            m_logger.LogError(
                                "OnAccept: Maximum number of channels {CurrentCount} reached, serving channels is stopped until number is lower or equal than {MaxChannelCount} ",
                                channelCount,
                                MaxChannelCount);
                            Utils.SilentDispose(e.AcceptSocket);
                        }

                        // check if the accept socket has been created.
                        if (serveChannel &&
                            e.AcceptSocket != null &&
                            e.SocketError == SocketError.Success)
                        {
                            channel = null;
                            try
                            {
                                if (m_reverseConnectListener)
                                {
                                    // create the channel to manage incoming reverse connections.
                                    channel = new TcpReverseConnectChannel(
                                        ListenerId,
                                        this,
                                        m_bufferManager,
                                        m_quotas,
                                        m_descriptions,
                                        m_telemetry);
                                }
                                else
                                {
                                    // create the channel to manage incoming connections.
                                    channel = new TcpServerChannel(
                                        ListenerId,
                                        this,
                                        m_bufferManager,
                                        m_quotas,
                                        m_serverCertificateTypesProvider,
                                        m_descriptions,
                                        m_telemetry);
                                }

                                if (m_callback != null)
                                {
                                    channel.SetRequestReceivedCallback(
                                        new TcpChannelRequestEventHandler(OnRequestReceivedAsync));
                                    channel.SetReportOpenSecureChannelAuditCallback(
                                        new ReportAuditOpenSecureChannelEventHandler(
                                            OnReportAuditOpenSecureChannelEvent));
                                    channel.SetReportCloseSecureChannelAuditCallback(
                                        new ReportAuditCloseSecureChannelEventHandler(
                                            OnReportAuditCloseSecureChannelEvent));
                                    channel.SetReportCertificateAuditCallback(
                                        new ReportAuditCertificateEventHandler(
                                            OnReportAuditCertificateEvent));
                                }

                                uint channelId;
                                do
                                {
                                    // get channel id
                                    channelId = GetNextChannelId();

                                    // save the channel for shutdown and reconnects.
                                    // retry to get a channel id if it is already in use.
                                } while (!channels.TryAdd(channelId, channel));

                                // start accepting messages on the channel.
                                channel.Attach(channelId, e.AcceptSocket);

                                channel = null;
                            }
                            catch (Exception ex)
                            {
                                m_logger.LogError(ex, "Unexpected error accepting a new connection.");
                            }
                            finally
                            {
                                Utils.SilentDispose(channel);
                            }
                        }
                    }

                    e.Dispose();

                    if (e.SocketError != SocketError.OperationAborted)
                    {
                        // go back and wait for the next connection.
                        try
                        {
                            e = new SocketAsyncEventArgs();
                            e.Completed += OnAccept;
                            e.UserToken = listeningSocket;
                            if (!listeningSocket.AcceptAsync(e))
                            {
                                repeatAccept = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogError(ex, "Unexpected error listening for a new connection.");
                        }
                    }
                }
            } while (repeatAccept);
        }

        /// <summary>
        /// The inactive timer callback which detects stale channels.
        /// </summary>
        private void DetectInactiveChannels(object state = null)
        {
            var channels = new List<TcpListenerChannel>();

            bool cleanup = false;
            foreach (KeyValuePair<uint, TcpListenerChannel> chEntry in m_channels)
            {
                if (chEntry.Value.ElapsedSinceLastActiveTime > m_quotas.ChannelLifetime)
                {
                    channels.Add(chEntry.Value);
                    cleanup = true;
                }
            }

            if (cleanup)
            {
                m_logger.LogInformation(
                    "TCPLISTENER: {ChannelCount} channels scheduled for IdleCleanup.",
                    channels.Count);
                foreach (TcpListenerChannel channel in channels)
                {
                    channel.IdleCleanup();
                }
                m_logger.LogInformation("TCPLISTENER: {ChannelCount} channels finished IdleCleanup.", channels.Count);
            }
        }

        /// <summary>
        /// The maximum number of secure channels
        /// </summary>
        public int MaxChannelCount { get; private set; }

        /// <summary>
        /// Handles requests arriving from a channel.
        /// </summary>
        private async void OnRequestReceivedAsync(
            TcpListenerChannel channel,
            uint requestId,
            IServiceRequest request)
        {
            try
            {
                if (m_callback != null)
                {
                    var context = new SecureChannelContext(
                        channel.GlobalChannelId,
                        channel.EndpointDescription,
                        RequestEncoding.Binary);

                    IServiceResponse response = await m_callback.ProcessRequestAsync(
                        context,
                        request).ConfigureAwait(false);

                    try
                    {
                        ((TcpServerChannel)channel).SendResponse(requestId, response);
                    }
                    catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadSecureChannelClosed)
                    {
                        // try to find the new channel id for the authentication token to send response over new channel
                        NodeId authenticationToken = request.RequestHeader.AuthenticationToken;
                        if (m_callback.TryGetSecureChannelIdForAuthenticationToken(
                                authenticationToken,
                                out uint channelId
                            ) &&
                            m_channels.TryGetValue(channelId, out TcpListenerChannel newChannel))
                        {
                            var serverChannel = (TcpServerChannel)newChannel;

                            // if the channel is not the same as the one we started with, send the response over the new channel
                            if (serverChannel != channel)
                            {
                                serverChannel.SendResponse(requestId, response);
                                return;
                            }
                        }
                        // if we could not find a new channel, just log the error
                        throw;
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "TCPLISTENER - Unexpected error processing request.");
            }
        }

        /// <summary>
        /// Callback for reporting the open secure channel audit event
        /// </summary>
        private void OnReportAuditOpenSecureChannelEvent(
            TcpServerChannel channel,
            OpenSecureChannelRequest request,
            X509Certificate2 clientCertificate,
            Exception exception)
        {
            try
            {
                m_callback?.ReportAuditOpenSecureChannelEvent(
                    channel.GlobalChannelId,
                    channel.EndpointDescription,
                    request,
                    clientCertificate,
                    exception);
            }
            catch (Exception e)
            {
                m_logger.LogError(
                    e,
                    "TCPLISTENER - Unexpected error sending OpenSecureChannel Audit event.");
            }
        }

        /// <summary>
        /// Callback for reporting the close secure channel audit event
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
                m_logger.LogError(
                    e,
                    "TCPLISTENER - Unexpected error sending CloseSecureChannel Audit event.");
            }
        }

        /// <summary>
        /// Callback for reporting the certificate audit events
        /// </summary>
        private void OnReportAuditCertificateEvent(
            X509Certificate2 clientCertificate,
            Exception exception)
        {
            try
            {
                m_callback?.ReportAuditCertificateEvent(clientCertificate, exception);
            }
            catch (Exception e)
            {
                m_logger.LogError(
                    e,
                    "TCPLISTENER - Unexpected error sending Certificate Audit event.");
            }
        }

        /// <summary>
        /// Get the next channel id. Handles overflow.
        /// </summary>
        private uint GetNextChannelId()
        {
            // wraps at Int32.MaxValue back to 1
            return (uint)Utils.IncrementIdentifier(ref m_lastChannelId);
        }

        private readonly Lock m_lock = new();
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private EndpointDescriptionCollection m_descriptions;
        private BufferManager m_bufferManager;
        private ChannelQuotas m_quotas;
        private CertificateTypesProvider m_serverCertificateTypesProvider;
        private int m_lastChannelId;
        private Socket m_listeningSocket;
        private Socket m_listeningSocketIPv6;
        private ConcurrentDictionary<uint, TcpListenerChannel> m_channels;
        private ITransportListenerCallback m_callback;
        private bool m_reverseConnectListener;
        private int m_inactivityDetectPeriod;
        private Timer m_inactivityDetectionTimer;
        private ActiveClientTracker m_activeClientTracker;
    }

    /// <summary>
    /// The Tcp specific arguments passed to the ConnectionWaiting event.
    /// </summary>
    public class TcpConnectionWaitingEventArgs : ConnectionWaitingEventArgs
    {
        internal TcpConnectionWaitingEventArgs(
            string serverUrl,
            Uri endpointUrl,
            IMessageSocket socket)
            : base(serverUrl, endpointUrl)
        {
            Socket = socket;
        }

        /// <inheritdoc/>
        public override object Handle => Socket;

        /// <inheritdoc/>
        internal IMessageSocket Socket { get; }
    }
}
