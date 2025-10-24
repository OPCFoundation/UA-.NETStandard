/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
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
            CertificateTypesProvider serverCertificateTypeProvider,
            EndpointDescriptionCollection endpoints,
            ITelemetryContext telemetry)
            : base(
                contextId,
                bufferManager,
                quotas,
                serverCertificateTypeProvider,
                endpoints,
                MessageSecurityMode.None,
                SecurityPolicies.None,
                telemetry)
        {
            m_logger = telemetry.CreateLogger<TcpListenerChannel>();
            Listener = listener;
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

            lock (DataLock)
            {
                // check for existing socket.
                if (Socket != null)
                {
                    throw new InvalidOperationException("Channel is already attached to a socket.");
                }

                ChannelId = channelId;
                State = TcpChannelState.Connecting;

                Socket = new TcpMessageSocket(this, socket, BufferManager, Quotas.MaxBufferSize, Telemetry);

                m_logger.LogTrace(
                    "{Channel} SOCKET ATTACHED: {SocketHandle:X8}, ChannelId={ChannelId}",
                    ChannelName,
                    Socket.Handle,
                    ChannelId);

                Socket.ReadNextMessage();
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
                    "Channel closed due to inactivity."));
            }
        }

        /// <summary>
        /// The time in milliseconds elapsed since the channel received or sent messages
        /// or received a keep alive.
        /// </summary>
        public int ElapsedSinceLastActiveTime => HiResClock.TickCount - LastActiveTickCount;

        /// <summary>
        /// Has the channel been used in a session
        /// </summary>
        public bool UsedBySession { get; protected set; }

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
        protected void ForceChannelFault(uint statusCode, string format, params object[] args)
        {
            ForceChannelFault(ServiceResult.Create(statusCode, format, args));

            m_logger.LogError(
                "ChannelId {Id}: ForceChannelFault due to {Message}.",
                ChannelId,
                Utils.Format(format, args));
        }

        /// <summary>
        /// Forces the channel into a faulted state as a result of a fatal error.
        /// </summary>
        protected void ForceChannelFault(
            Exception exception,
            uint defaultCode,
            string format,
            params object[] args)
        {
            ForceChannelFault(ServiceResult.Create(exception, defaultCode, format, args));

            m_logger.LogError(
                exception,
                "ChannelId {Id}: ForceChannelFault due to {Message}.",
                ChannelId,
                Utils.Format(format, args));
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
                    int? socketHandle = Socket?.Handle;
                    if (socketHandle is not null and not (-1))
                    {
                        m_logger.LogError(
                            "{Channel} ForceChannelFault Socket={SocketHandle:X8}, ChannelId={ChannelId}, TokenId={TokenId}, Reason={Reason}",
                            ChannelName,
                            socketHandle,
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
                if (Socket != null && m_responseRequired)
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
                        tcpTransportListener?.MarkAsPotentialProblematic(
                            ((IPEndPoint)Socket.RemoteEndpoint).Address);
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

                m_logger.LogInformation(
                    "{Channel} Cleanup Socket={SocketHandle:X8}, ChannelId={ChannelId}, TokenId={TokenId}, Reason={Reason}",
                    ChannelName,
                    (Socket?.Handle) ?? 0,
                    CurrentToken != null ? CurrentToken.ChannelId : 0,
                    CurrentToken != null ? CurrentToken.TokenId : 0,
                    reason.ToString());

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
                Socket?.Close();
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
                Socket?.Close();
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
            m_logger.LogTrace("ChannelId {ChannelId}: SendErrorMessage={Status}", ChannelId, error.StatusCode);

            byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "SendErrorMessage");

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
            m_logger.LogTrace(
                "ChannelId {Id}: Request {RequestId}: SendServiceFault={ServiceFault}",
                ChannelId,
                requestId,
                fault.StatusCode);

            BufferCollection buffers = null;

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

                m_logger.LogError(
                    e,
                    "ChannelId {Id}: Request {RequestId}: SendServiceFault={ServiceFault}: Unexpected error.",
                    ChannelId,
                    requestId,
                    fault.StatusCode);

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
        /// Sends a fault response secured with the asymmetric keys.
        /// </summary>
        protected void SendServiceFault(uint requestId, ServiceResult fault)
        {
            m_logger.LogTrace(
                "ChannelId {Id}: Request {RequestId}: SendServiceFault={ServiceFault}",
                ChannelId,
                requestId,
                fault.StatusCode);

            BufferCollection chunksToSend = null;

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

                // serialize fault.
                byte[] buffer = BinaryEncoder.EncodeMessage(response, Quotas.MessageContext);

                // secure message.
                chunksToSend = WriteAsymmetricMessage(
                    TcpMessageType.Open,
                    requestId,
                    ServerCertificate,
                    ClientCertificate,
                    new ArraySegment<byte>(buffer, 0, buffer.Length));

                // write the message to the server.
                BeginWriteMessage(chunksToSend, null);
                chunksToSend = null;
            }
            catch (Exception e)
            {
                chunksToSend?.Release(BufferManager, "SendServiceFault");

                m_logger.LogError(
                    e,
                    "ChannelId {Id}: Request {RequestId}: SendServiceFault={ServiceFault}: Unexpected error.",
                    ChannelId,
                    requestId,
                    fault.StatusCode);

                ForceChannelFault(
                    ServiceResult.Create(
                        e,
                        StatusCodes.BadTcpInternalError,
                        "Unexpected error sending a service fault."));
            }
        }

        /// <summary>
        /// Handles a reconnect request.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public virtual void Reconnect(
            IMessageSocket socket,
            uint requestId,
            uint sequenceNumber,
            X509Certificate2 clientCertificate,
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
        protected TcpChannelRequestEventHandler RequestReceived { get; private set; }

        /// <summary>
        /// The report open secure channel audit event handler.
        /// </summary>
        protected ReportAuditOpenSecureChannelEventHandler ReportAuditOpenSecureChannelEvent { get; private set; }

        /// <summary>
        /// The report close secure channel audit event handler.
        /// </summary>
        protected ReportAuditCloseSecureChannelEventHandler ReportAuditCloseSecureChannelEvent { get; private set; }

        /// <summary>
        /// The report certificate audit event handler.
        /// </summary>
        protected ReportAuditCertificateEventHandler ReportAuditCertificateEvent { get; private set; }

        private readonly ILogger m_logger;
        private bool m_responseRequired;
        private uint m_lastTokenId;
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
        X509Certificate2 clientCertificate,
        Exception exception);

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
        X509Certificate2 clientCertificate,
        Exception exception);
}
