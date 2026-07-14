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
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Called when a server-side <see cref="TcpListenerChannel"/>
    /// activates a new <see cref="ChannelToken"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ This delegate exposes symmetric channel keys. See remarks on <c>OnTokenActivated</c>.
    /// </remarks>
    public delegate void ListenerChannelTokenActivatedEventHandler(
        TcpListenerChannel channel,
        ChannelToken? currentToken,
        ChannelToken? previousToken);

    /// <summary>
    /// Manages the listening side of a UA TCP channel.
    /// </summary>
    public class TcpListenerChannel : UaSCUaBinaryChannel
    {
        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        public TcpListenerChannel(
            string contextId,
            ITcpChannelListener listener,
            BufferManager bufferManager,
            ChannelQuotas quotas,
            ICertificateRegistry serverCertificates,
            List<EndpointDescription> endpoints,
            ITelemetryContext telemetry)
            : this(
                contextId,
                listener,
                bufferManager,
                quotas,
                serverCertificates,
                endpoints,
                telemetry,
                null)
        {
        }

        /// <summary>
        /// Attaches the object to an existing socket using the supplied
        /// <see cref="TimeProvider"/> for activity tracking.
        /// </summary>
        public TcpListenerChannel(
            string contextId,
            ITcpChannelListener listener,
            BufferManager bufferManager,
            ChannelQuotas quotas,
            ICertificateRegistry serverCertificates,
            List<EndpointDescription> endpoints,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider)
            : base(
                contextId,
                bufferManager,
                quotas,
                serverCertificates,
                endpoints,
                MessageSecurityMode.None,
                SecurityPolicies.None,
                telemetry,
                timeProvider)
        {
            m_logger = telemetry.CreateLogger<TcpListenerChannel>();
            Listener = listener;

            // Bridge the base channel's token-activated callback to the
            // public event so external diagnostic taps can observe token
            // transitions without needing to subclass.
            TokenActivatedCallback = (current, previous)
                => m_tokenActivated?.Invoke(this, current, previous);
        }

        /// <summary>
        /// Raised when the channel activates a new <see cref="ChannelToken"/>
        /// (initial activation, renewal or final close). The handler is
        /// invoked with the newly active token and the previously active
        /// token, both of which may be <c>null</c>. The token's derived
        /// signing and encrypting key material is intentionally
        /// <see langword="internal"/> on <see cref="ChannelToken"/>; tools
        /// that need offline decryption capture it through a separate
        /// stack-level diagnostics API.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠️ Registering a handler grants the consumer full access to the
        /// symmetric channel keys carried by the activated <see cref="ChannelToken"/>
        /// — signing key, encryption key, IV, and nonces. This is a key-disclosure
        /// surface intended only for in-process diagnostic bindings (for example,
        /// the <c>Opc.Ua.Pcap</c> capture binding).
        /// </para>
        /// <para>
        /// All non-diagnostic consumers MUST instead inject an
        /// <c>IFrameCaptureSink</c> through the binding registry, which carries
        /// the same audit semantics. Operations triggered through this event
        /// must be recorded through <c>IPcapAuditSink</c> (or equivalent) so
        /// key access remains observable.
        /// </para>
        /// </remarks>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public event ListenerChannelTokenActivatedEventHandler? OnTokenActivated
        {
            add => m_tokenActivated += value;
            remove => m_tokenActivated -= value;
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        /// <summary>
        /// The channel name used in trace output.
        /// </summary>
        public virtual string ChannelName => "TCPLISTENERCHANNEL";

        /// <summary>
        /// The TCP channel listener.
        /// </summary>
        protected ITcpChannelListener Listener { get; }

        /// <summary>
        /// Optional decorator applied to the byte transport when the channel
        /// is attached to an accepted socket. Set by the listener from
        /// <see cref="TransportListenerSettings.AcceptedTransportDecorator"/>
        /// so an in-process capture binding can wrap the transport. When
        /// <c>null</c> (the default) the transport is used unchanged.
        /// </summary>
        internal Func<IUaSCByteTransport, IUaSCByteTransport>? TransportDecorator { get; set; }

        /// <summary>
        /// Sets the callback used to receive notifications of new events.
        /// </summary>
        public void SetRequestReceivedCallback(TcpChannelRequestEventHandler callback)
        {
            lock (DataLock)
            {
                RequestReceived = callback;
            }
        }

        /// <summary>
        /// Sets the callback used to raise channel audit events.
        /// </summary>
        public void SetReportOpenSecureChannelAuditCallback(
            ReportAuditOpenSecureChannelEventHandler callback)
        {
            lock (DataLock)
            {
                ReportAuditOpenSecureChannelEvent = callback;
            }
        }

        /// <summary>
        /// Sets the callback used to raise channel audit events.
        /// </summary>
        public void SetReportCloseSecureChannelAuditCallback(
            ReportAuditCloseSecureChannelEventHandler callback)
        {
            lock (DataLock)
            {
                ReportAuditCloseSecureChannelEvent = callback;
            }
        }

        /// <summary>
        /// Sets the callback used to raise channel audit events.
        /// </summary>
        public void SetReportCertificateAuditCallback(ReportAuditCertificateEventHandler callback)
        {
            lock (DataLock)
            {
                ReportAuditCertificateEvent = callback;
            }
        }

        /// <summary>
        /// Attaches the channel to an existing socket.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="socket"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException"></exception>
        public void Attach(uint channelId, Socket socket)
        {
            if (socket == null)
            {
                throw new ArgumentNullException(nameof(socket));
            }
#pragma warning disable CA2000 // transport ownership is transferred to Attach below
            IUaSCByteTransport transport = new TcpByteTransport(socket, BufferManager, Quotas.MaxBufferSize, Telemetry);
            transport = TransportDecorator?.Invoke(transport) ?? transport;
            Attach(channelId, transport);
#pragma warning restore CA2000
        }

        /// <summary>
        /// Attaches the channel to an existing byte transport (TCP, WebSocket,
        /// or any other <see cref="IUaSCByteTransport"/> implementation) and
        /// starts the channel's receive loop.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="transport"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException"></exception>
        public void Attach(uint channelId, IUaSCByteTransport transport)
        {
            if (transport == null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            lock (DataLock)
            {
                if (Transport != null)
                {
                    throw new InvalidOperationException("Channel is already attached to a transport.");
                }

                ChannelId = channelId;
                State = TcpChannelState.Connecting;

                Transport = transport;

                m_logger.TcpListenChannelLog0(
                    ChannelName,
                    Transport.RemoteEndpoint,
                    ChannelId);

                StartReceiveLoop();
            }
        }

        /// <summary>
        /// Clean up an Opening or Open channel that has been idle for too long.
        /// </summary>
        public void IdleCleanup()
        {
            TcpChannelState state;

            lock (DataLock)
            {
                state = State;
                if (state is TcpChannelState.Open or TcpChannelState.Connecting)
                {
                    state = State = TcpChannelState.Closing;
                }
            }

            if (state is TcpChannelState.Closing or TcpChannelState.Opening or TcpChannelState.Faulted)
            {
                OnCleanup(new ServiceResult(
                    StatusCodes.BadNoCommunication,
                    LocalizedText.From("Channel closed due to inactivity.")));
            }
        }

        /// <summary>
        /// The time in milliseconds elapsed since the channel received or sent messages
        /// or received a keep alive.
        /// </summary>
        public int ElapsedSinceLastActiveTime
        {
            get
            {
                long ms = (long)GetElapsedSinceLastActive().TotalMilliseconds;
                return (int)Math.Min(int.MaxValue, Math.Max(0, ms));
            }
        }

        /// <summary>
        /// Gets whether at least one active session is using the channel.
        /// </summary>
        public bool UsedBySession => Volatile.Read(ref m_sessionCount) > 0;

        /// <summary>
        /// Records an active session on the channel.
        /// </summary>
        protected void AddSession()
        {
            Interlocked.Increment(ref m_sessionCount);
        }

        /// <summary>
        /// Records a closed session on the channel.
        /// </summary>
        protected void RemoveSession()
        {
            int sessionCount;
            do
            {
                sessionCount = Volatile.Read(ref m_sessionCount);
                if (sessionCount == 0)
                {
                    return;
                }
            } while (Interlocked.CompareExchange(ref m_sessionCount, sessionCount - 1, sessionCount) !=
                sessionCount);
        }

        /// <summary>
        /// Force-closes the channel if it was negotiated against the
        /// server certificate identified by <paramref name="oldThumbprint"/>.
        /// Implements the channel-cut step required by OPC UA Part 12
        /// §7.10.9 (ApplyChanges → force renegotiate affected
        /// SecureChannels). The listener socket is unaffected — only the
        /// per-channel TCP connection is torn down so the client's
        /// reconnect logic can transfer the Session over a fresh
        /// SecureChannel.
        /// </summary>
        /// <param name="oldThumbprint">
        /// The thumbprint of the previously-active server application
        /// certificate. Compared case-insensitively against the channel's
        /// own <see cref="UaSCUaBinaryChannel.ServerCertificate"/>
        /// thumbprint.
        /// </param>
        /// <param name="globalChannelId">
        /// On <c>true</c> return, the
        /// <see cref="UaSCUaBinaryChannel.GlobalChannelId"/> of the
        /// channel that was just closed; otherwise <c>null</c>.
        /// </param>
        /// <returns>
        /// <c>true</c> when the channel matched and was closed;
        /// <c>false</c> when the channel's server certificate does not
        /// match, the channel is already closed/faulted, or there is no
        /// negotiated server certificate (e.g. a SecurityPolicy.None
        /// channel).
        /// </returns>
        internal bool TryCloseForCertificateRotation(
            string oldThumbprint,
            out string? globalChannelId)
        {
            globalChannelId = null;
            if (string.IsNullOrEmpty(oldThumbprint))
            {
                return false;
            }

            lock (DataLock)
            {
                if (State is TcpChannelState.Closed or TcpChannelState.Faulted)
                {
                    return false;
                }

                string? currentThumbprint = ServerCertificate?.Thumbprint;
                if (string.IsNullOrEmpty(currentThumbprint) ||
                    !string.Equals(currentThumbprint, oldThumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                globalChannelId = GlobalChannelId;
                m_logger.TcpListenChannelLog1(ChannelName, ChannelId, oldThumbprint);

                var reason = ServiceResult.Create(
                    StatusCodes.BadCertificateInvalid,
                    "Server certificate rotated. Renegotiate the SecureChannel.");

                if (Transport != null)
                {
                    try
                    {
                        SendErrorMessage(reason);
                    }
                    catch
                    {
                        // Best-effort — the goal is to close the channel
                        // even if the error message cannot be flushed.
                    }
                }

                ChannelFaulted();
                NotifyMonitors(reason, true);
                return true;
            }
        }

        /// <summary>
        /// Returns an independent public-key copy of the channel's
        /// negotiated client (peer) certificate for re-validation against an
        /// updated TrustList, or <see langword="null"/> when the channel has
        /// no client certificate (for example a
        /// <see cref="SecurityPolicies.None"/> channel) or is already
        /// closed/faulted. The returned reference is owned by the caller,
        /// which must dispose it. Snapshotting under <c>DataLock</c> avoids
        /// racing the channel's own disposal of its live client certificate.
        /// </summary>
        internal Certificate? SnapshotClientCertificateForRevalidation()
        {
            lock (DataLock)
            {
                if (State is TcpChannelState.Closed or TcpChannelState.Faulted)
                {
                    return null;
                }

                byte[]? rawData = ClientCertificate?.RawData;
                if (rawData == null || rawData.Length == 0)
                {
                    return null;
                }

                return Certificate.FromRawData(rawData);
            }
        }

        /// <summary>
        /// Force-closes the channel because its negotiated client (peer)
        /// certificate is no longer trusted after a committed TrustList
        /// change. Implements the peer-trust channel-cut step required by
        /// OPC UA Part 12 §7.10.9 (ApplyChanges → force renegotiate affected
        /// SecureChannels). The listener socket is unaffected — only the
        /// per-channel TCP connection is torn down so the peer re-negotiates
        /// and is re-validated against the updated TrustList.
        /// </summary>
        /// <param name="globalChannelId">
        /// On <c>true</c> return, the
        /// <see cref="UaSCUaBinaryChannel.GlobalChannelId"/> of the channel
        /// that was just closed; otherwise <c>null</c>.
        /// </param>
        /// <returns>
        /// <c>true</c> when the channel was closed; <c>false</c> when it was
        /// already closed or faulted.
        /// </returns>
        internal bool CloseForUntrustedPeerCertificate(out string? globalChannelId)
        {
            globalChannelId = null;

            lock (DataLock)
            {
                if (State is TcpChannelState.Closed or TcpChannelState.Faulted)
                {
                    return false;
                }

                globalChannelId = GlobalChannelId;
                m_logger.LogInformation(
                    Utils.TraceMasks.Security,
                    "{Channel} ChannelId={ChannelId}: closing because the peer certificate is no longer trusted.",
                    ChannelName,
                    ChannelId);

                var reason = ServiceResult.Create(
                    StatusCodes.BadCertificateUntrusted,
                    "Peer certificate is no longer trusted. Renegotiate the SecureChannel.");

                if (Transport != null)
                {
                    try
                    {
                        SendErrorMessage(reason);
                    }
                    catch
                    {
                        // Best-effort — the goal is to close the channel
                        // even if the error message cannot be flushed.
                    }
                }

                ChannelFaulted();
                NotifyMonitors(reason, true);
                return true;
            }
        }

        /// <summary>
        /// Handles a socket error.
        /// </summary>
        protected override void HandleSocketError(ServiceResult result)
        {
            lock (DataLock)
            {
                // channel fault.
                if (ServiceResult.IsBad(result))
                {
                    ForceChannelFault(result);
                    return;
                }

                // gracefully shutdown the channel.
                ChannelClosed();
            }
        }

        /// <summary>
        /// Forces the channel into a faulted state as a result of a fatal error.
        /// </summary>
        protected void ForceChannelFault(StatusCode statusCode, string format, params object[] args)
        {
            ForceChannelFault(ServiceResult.Create(statusCode, format, args));

            if (m_logger.IsEnabled(LogLevel.Error))
            {
                m_logger.TcpListenChannelLog2(ChannelId, Utils.Format(format, args));
            }
        }

        /// <summary>
        /// Forces the channel into a faulted state as a result of a fatal error.
        /// </summary>
        protected void ForceChannelFault(
            Exception exception,
            StatusCode defaultCode,
            string format,
            params object[] args)
        {
            ForceChannelFault(ServiceResult.Create(exception, defaultCode, format, args));

            if (m_logger.IsEnabled(LogLevel.Error))
            {
                m_logger.TcpListenChannelLog3(exception, ChannelId, Utils.Format(format, args));
            }
        }

        /// <summary>
        /// Forces the channel into a faulted state as a result of a fatal error.
        /// </summary>
        protected void ForceChannelFault(ServiceResult reason)
        {
            lock (DataLock)
            {
                CompleteReverseHello(new ServiceResultException(reason));

                // nothing to do if channel already in a faulted state.
                if (State == TcpChannelState.Faulted)
                {
                    return;
                }

                bool close = false;
                if (State is not TcpChannelState.Connecting and not TcpChannelState.Opening)
                {
                    EndPoint? remoteEndpoint = Transport?.RemoteEndpoint;
                    if (remoteEndpoint != null)
                    {
                        m_logger
                            .TcpListenChannelLog4(
                                ChannelName,
                                remoteEndpoint,
                                CurrentToken != null ? CurrentToken.ChannelId : 0,
                                CurrentToken != null ? CurrentToken.TokenId : 0,
                                reason);
                    }
                }
                else
                {
                    // Close immediately if the client never got out of connecting or opening state
                    close = true;
                }

                // send error and close response.
                if (Transport != null && m_responseRequired)
                {
                    SendErrorMessage(reason);
                }

                State = TcpChannelState.Faulted;
                m_responseRequired = false;

                if (close)
                {
                    // mark the RemoteAddress as potential problematic if Basic128Rsa15
                    if ((SecurityPolicyUri == SecurityPolicies.Basic128Rsa15) &&
                        (
                            reason.StatusCode == StatusCodes.BadSecurityChecksFailed ||
                            reason.StatusCode == StatusCodes.BadTcpMessageTypeInvalid))
                    {
                        var tcpTransportListener = Listener as TcpTransportListener;
                        if (Transport?.RemoteEndpoint is IPEndPoint ipEndpoint)
                        {
                            tcpTransportListener?.MarkAsPotentialProblematic(ipEndpoint.Address);
                        }
                    }

                    // close channel immediately.
                    ChannelFaulted();
                }

                // notify any monitors.
                NotifyMonitors(reason, close);
            }
        }

        /// <summary>
        /// Called when the channel needs to be cleaned up.
        /// </summary>
        private void OnCleanup(object state)
        {
            lock (DataLock)
            {
                // nothing to do if the channel is now open or closed.
                if (State is TcpChannelState.Closed or TcpChannelState.Open)
                {
                    return;
                }

                // get reason for cleanup.
                if (state is not ServiceResult reason)
                {
                    reason = new ServiceResult(StatusCodes.BadTimeout);
                }

                if (m_logger.IsEnabled(LogLevel.Information))
                {
                    m_logger.TcpListenChannelLog5(
                        ChannelName,
                        Transport?.RemoteEndpoint,
                        CurrentToken != null ? CurrentToken.ChannelId : 0,
                        CurrentToken != null ? CurrentToken.TokenId : 0,
                        reason.ToString());
                }

                // close channel.
                ChannelClosed();
            }
        }

        /// <summary>
        /// Closes the channel and releases resources.
        /// Sets state to Closed and notifies monitors.
        /// </summary>
        protected void ChannelClosed()
        {
            try
            {
                Transport?.Close();
            }
            finally
            {
                State = TcpChannelState.Closed;
                Listener.ChannelClosed(ChannelId);

                // notify any monitors.
                NotifyMonitors(new ServiceResult(StatusCodes.BadConnectionClosed), true);
            }
        }

        /// <summary>
        /// Closes the channel and releases resources.
        /// Sets state to Faulted.
        /// </summary>
        protected void ChannelFaulted()
        {
            try
            {
                Transport?.Close();
            }
            finally
            {
                State = TcpChannelState.Faulted;
                Listener.ChannelClosed(ChannelId);
            }
        }

        /// <summary>
        /// Sends an error message over the socket.
        /// </summary>
        protected void SendErrorMessage(ServiceResult error)
        {
            m_logger.TcpListenChannelLog6(ChannelId, error.StatusCode);

            byte[]? buffer = BufferManager.TakeBuffer(SendBufferSize, "SendErrorMessage");

            try
            {
                using var encoder = new BinaryEncoder(
                    buffer,
                    0,
                    SendBufferSize,
                    Quotas.MessageContext);
                encoder.WriteUInt32(null, TcpMessageType.Error);
                encoder.WriteUInt32(null, 0);

                WriteErrorMessageBody(encoder, error);

                int size = encoder.Close();
                UpdateMessageSize(buffer, 0, size);

                BeginWriteMessage(new ArraySegment<byte>(buffer, 0, size), null);
                buffer = null;
            }
            finally
            {
                if (buffer != null)
                {
                    BufferManager.ReturnBuffer(buffer, "SendErrorMessage");
                }
            }
        }

        /// <summary>
        /// Sends a fault response secured with the symmetric keys.
        /// </summary>
        protected void SendServiceFault(ChannelToken token, uint requestId, ServiceResult fault)
        {
            m_logger.TcpListenChannelLog7(ChannelId, requestId, fault.StatusCode);

            BufferCollection? buffers = null;

            try
            {
                // construct fault.
                var response = new ServiceFault();

                response.ResponseHeader.ServiceResult = fault.Code;

                var stringTable = new StringTable();

                response.ResponseHeader.ServiceDiagnostics = new DiagnosticInfo(
                    fault,
                    DiagnosticsMasks.NoInnerStatus,
                    true,
                    stringTable,
                    m_logger);

                response.ResponseHeader.StringTable = stringTable.ToArray();

                // the limits should never be exceeded when sending a fault.

                // secure message.
                buffers = WriteSymmetricMessage(
                    TcpMessageType.Message,
                    requestId,
                    token,
                    response,
                    false,
                    out bool limitsExceeded);

                // send message.
                BeginWriteMessage(buffers, null);
                buffers = null;
            }
            catch (Exception e)
            {
                buffers?.Release(BufferManager, "SendServiceFault");

                m_logger.TcpListenChannelLog8(e, ChannelId, requestId, fault.StatusCode);

                ForceChannelFault(
                    ServiceResult.Create(
                        e,
                        StatusCodes.BadTcpInternalError,
                        "Unexpected error sending a service fault."));
            }
        }

        /// <summary>
        /// Notify if the channel status changed.
        /// </summary>
        protected virtual void NotifyMonitors(ServiceResult status, bool closed)
        {
            // intentionally left empty
        }

        /// <summary>
        /// Called to indicate an error or success if the listener
        /// channel initiated a reverse hello connection.
        /// </summary>
        /// <remarks>
        /// The callback is only used by the server channel.
        /// The listener channel uses the callback to indicate
        /// an error condition to the server channel.
        /// </remarks>
        protected virtual void CompleteReverseHello(Exception e)
        {
            // intentionally left empty
        }

        /// <summary>
        /// Handles a reconnect request.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public virtual void Reconnect(
            IUaSCByteTransport transport,
            uint requestId,
            uint sequenceNumber,
            Certificate clientCertificate,
            ChannelToken token,
            OpenSecureChannelRequest request)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Set the flag if a response is required for the use case of reverse connect.
        /// </summary>
        protected void SetResponseRequired(bool responseRequired)
        {
            m_responseRequired = responseRequired;
        }

        /// <summary>
        /// Returns a new token id.
        /// </summary>
        protected uint GetNewTokenId()
        {
            return Utils.IncrementIdentifier(ref m_lastTokenId);
        }

        /// <summary>
        /// The channel request event handler.
        /// </summary>
        protected TcpChannelRequestEventHandler? RequestReceived { get; private set; }

        /// <summary>
        /// The report open secure channel audit event handler.
        /// </summary>
        protected ReportAuditOpenSecureChannelEventHandler? ReportAuditOpenSecureChannelEvent { get; private set; }

        /// <summary>
        /// The report close secure channel audit event handler.
        /// </summary>
        protected ReportAuditCloseSecureChannelEventHandler? ReportAuditCloseSecureChannelEvent { get; private set; }

        /// <summary>
        /// The report certificate audit event handler.
        /// </summary>
        protected ReportAuditCertificateEventHandler? ReportAuditCertificateEvent { get; private set; }

        private readonly ILogger m_logger;
        private bool m_responseRequired;
        private uint m_lastTokenId;
        private int m_sessionCount;
        private event ListenerChannelTokenActivatedEventHandler? m_tokenActivated;
    }

    /// <summary>
    /// Used to report an incoming request.
    /// </summary>
    public delegate void TcpChannelRequestEventHandler(
        TcpListenerChannel channel,
        uint requestId,
        IServiceRequest request);

    /// <summary>
    /// Used to report the status of the channel.
    /// </summary>
    public delegate void TcpChannelStatusEventHandler(
        TcpServerChannel channel,
        ServiceResult status,
        bool closed);

    /// <summary>
    /// Used to report an open secure channel audit event.
    /// </summary>
    public delegate void ReportAuditOpenSecureChannelEventHandler(
        TcpServerChannel channel,
        OpenSecureChannelRequest request,
        Certificate? clientCertificate,
        Exception? exception);

    /// <summary>
    /// Used to report a close secure channel audit event
    /// </summary>
    public delegate void ReportAuditCloseSecureChannelEventHandler(
        TcpServerChannel channel,
        Exception exception);

    /// <summary>
    /// Used to report an open secure channel audit event.
    /// </summary>
    public delegate void ReportAuditCertificateEventHandler(
        Certificate clientCertificate,
        Exception exception);

    /// <summary>
    /// Source-generated log messages for TcpListenerChannel.
    /// </summary>
    internal static partial class TcpListenerChannelLog
    {
        [LoggerMessage(EventId = CoreEventIds.TcpListenerChannel + 0, Level = LogLevel.Debug,
            Message = "{Channel} TRANSPORT ATTACHED: {RemoteEndpoint}, ChannelId={ChannelId}")]
        public static partial void TcpListenChannelLog0(
            this ILogger logger,
            string channel,
            global::System.Net.EndPoint? remoteEndpoint,
            uint channelId);

        [LoggerMessage(EventId = CoreEventIds.TcpListenerChannel + 1, Level = LogLevel.Information,
            Message = "{Channel} ChannelId={ChannelId}: closing for certificate rotation (thumbprint {Thumbprint}).")]
        public static partial void TcpListenChannelLog1(
            this ILogger logger,
            string channel,
            uint channelId,
            string thumbprint);

        [LoggerMessage(EventId = CoreEventIds.TcpListenerChannel + 2, Level = LogLevel.Error,
            Message = "ChannelId {Id}: ForceChannelFault due to {Message}.")]
        public static partial void TcpListenChannelLog2(
            this ILogger logger,
            uint id,
            string message);

        [LoggerMessage(EventId = CoreEventIds.TcpListenerChannel + 3, Level = LogLevel.Error,
            Message = "ChannelId {Id}: ForceChannelFault due to {Message}.")]
        public static partial void TcpListenChannelLog3(
            this ILogger logger,
            global::System.Exception? exception,
            uint id,
            string message);

        [LoggerMessage(EventId = CoreEventIds.TcpListenerChannel + 4, Level = LogLevel.Error,
            Message = "{Channel} ForceChannelFault Transport={RemoteEndpoint}, ChannelId={ChannelId}, " +
                "TokenId={TokenId}, Reason={Reason}")]
        public static partial void TcpListenChannelLog4(
            this ILogger logger,
            string channel,
            global::System.Net.EndPoint? remoteEndpoint,
            uint channelId,
            uint tokenId,
            global::Opc.Ua.ServiceResult reason);

        [LoggerMessage(EventId = CoreEventIds.TcpListenerChannel + 5, Level = LogLevel.Information,
            Message = "{Channel} Cleanup Transport={RemoteEndpoint}, ChannelId={ChannelId}, " +
                "TokenId={TokenId}, Reason={Reason}")]
        public static partial void TcpListenChannelLog5(
            this ILogger logger,
            string channel,
            global::System.Net.EndPoint? remoteEndpoint,
            uint channelId,
            uint tokenId,
            string reason);

        [LoggerMessage(EventId = CoreEventIds.TcpListenerChannel + 6, Level = LogLevel.Debug,
            Message = "ChannelId {ChannelId}: SendErrorMessage={Status}")]
        public static partial void TcpListenChannelLog6(
            this ILogger logger,
            uint channelId,
            global::Opc.Ua.StatusCode status);

        [LoggerMessage(EventId = CoreEventIds.TcpListenerChannel + 7, Level = LogLevel.Debug,
            Message = "ChannelId {Id}: Request {RequestId}: SendServiceFault={ServiceFault}")]
        public static partial void TcpListenChannelLog7(
            this ILogger logger,
            uint id,
            uint requestId,
            global::Opc.Ua.StatusCode serviceFault);

        [LoggerMessage(EventId = CoreEventIds.TcpListenerChannel + 8, Level = LogLevel.Error,
            Message = "ChannelId {Id}: Request {RequestId}: SendServiceFault={ServiceFault}: Unexpected error.")]
        public static partial void TcpListenChannelLog8(
            this ILogger logger,
            global::System.Exception? exception,
            uint id,
            uint requestId,
            global::Opc.Ua.StatusCode serviceFault);
    }

}
