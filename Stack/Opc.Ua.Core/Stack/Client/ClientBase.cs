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
using System.Collections;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
	/// The client side interface with a UA server.
	/// </summary>
    public partial class ClientBase : IClientBase, IDisposable
    {
        #region Constructors
        /// <summary>
        /// Intializes the object with a channel and a message context.
        /// </summary>
        /// <param name="channel">The channel.</param>
        public ClientBase(ITransportChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));

            InitializeChannel(channel);
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            CloseChannel();

            m_disposed = true;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The description of the endpoint.
        /// </summary>
        public EndpointDescription Endpoint
        {
            get
            {
                ITransportChannel channel = TransportChannel;

                if (channel != null)
                {
                    return channel.EndpointDescription;
                }

                return null;
            }
        }

        /// <summary>
        /// The configuration for the endpoint.
        /// </summary>
        public EndpointConfiguration EndpointConfiguration
        {
            get
            {
                ITransportChannel channel = TransportChannel;

                if (channel != null)
                {
                    return channel.EndpointConfiguration;
                }

                return null;
            }
        }

        /// <summary>
        /// The message context used when serializing messages.
        /// </summary>
        /// <value>The message context.</value>
        public IServiceMessageContext MessageContext
        {
            get
            {
                ITransportChannel channel = TransportChannel;

                if (channel != null)
                {
                    return channel.MessageContext;
                }

                return null;
            }
        }

        /// <summary>
        /// Gets or set the channel being wrapped by the client object.
        /// </summary>
        /// <value>The transport channel.</value>
        public ITransportChannel TransportChannel
        {
            get
            {
                ITransportChannel channel = m_channel;

                if (channel != null)
                {
                    if (m_disposed)
                    {
                        throw new ObjectDisposedException("ClientBase has been disposed.");
                    }
                }

                return channel;
            }

            protected set
            {
                ITransportChannel channel = m_channel;
                m_channel = null;

                if (channel != null)
                {
                    try
                    {
                        channel.Close();
                        channel.Dispose();
                    }
                    catch
                    {
                        // ignore errors.
                    }
                }

                m_channel = value;
            }
        }

        /// <summary>
        /// The channel being wrapped by the client object.
        /// </summary>
        internal IChannelBase InnerChannel
        {
            get
            {
                ITransportChannel channel = TransportChannel;

                if (channel != null)
                {
                    return m_channel as IChannelBase;
                }

                return null;
            }
        }

        /// <summary>
        /// What diagnostics the server should return in the response.
        /// </summary>
        /// <value>The diagnostics.</value>
        public DiagnosticsMasks ReturnDiagnostics
        {
            get
            {
                return m_returnDiagnostics;
            }

            set
            {
                m_returnDiagnostics = value;
            }
        }

        /// <summary>
        /// Sets the timeout for an operation.
        /// </summary>
        public int OperationTimeout
        {
            get
            {
                ITransportChannel channel = TransportChannel;

                if (channel != null)
                {
                    return m_channel.OperationTimeout;
                }

                return 0;
            }

            set
            {
                ITransportChannel channel = TransportChannel;

                if (channel != null)
                {
                    m_channel.OperationTimeout = value;
                }
            }
        }

        /// <summary>
        /// Gets a value that indicates whether to use the TransportChannel when sending requests.
        /// </summary>
        protected bool UseTransportChannel
        {
            get
            {
                ITransportChannel channel = TransportChannel;

                if (channel == null)
                {
                    throw new ObjectDisposedException("TransportChannel is not available.");
                }

                return m_useTransportChannel;
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Attach the channel to an already created client.
        /// </summary>
        /// <param name="channel">Channel to be used by the client</param>
        public void AttachChannel(ITransportChannel channel)
        {
            InitializeChannel(channel);
        }

        /// <summary>
        /// Detach the channel.
        /// </summary>
        public void DetachChannel()
        {
            m_channel = null;
        }

        /// <summary>
        /// Closes the channel.
        /// </summary>
        public virtual StatusCode Close()
        {
            if (m_channel != null)
            {
                m_channel.Close();
                m_channel = null;
            }

            m_authenticationToken = null;
            return StatusCodes.Good;
        }

        /// <summary>
        /// Whether the object has been disposed.
        /// </summary>
        /// <value><c>true</c> if disposed; otherwise, <c>false</c>.</value>
        public bool Disposed
        {
            get
            {
                return m_disposed;
            }
        }

        /// <summary>
        /// Generates a unique request handle.
        /// </summary>
        public uint NewRequestHandle()
        {
            return (uint)Utils.IncrementIdentifier(ref m_nextRequestHandle);
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Initializes the channel.
        /// </summary>
        /// <param name="channel"></param>
        protected void InitializeChannel(ITransportChannel channel)
        {
            m_channel = channel;
            m_useTransportChannel = true;

            UaChannelBase uaChannel = channel as UaChannelBase;

            if (uaChannel != null)
            {
                m_useTransportChannel = uaChannel.m_uaBypassChannel != null || uaChannel.UseBinaryEncoding;
            }
        }

        /// <summary>
        /// Closes the channel.
        /// </summary>
        protected void CloseChannel()
        {
            if (m_channel != null)
            {
                try
                {
                    m_channel.Close();
                }
                catch
                {
                    // ignore errors.
                }

                DisposeChannel();
            }
        }

        /// <summary>
        /// Disposes the channel.
        /// </summary>
        protected void DisposeChannel()
        {
            if (m_channel != null)
            {
                try
                {
                    m_channel.Dispose();
                }
                catch
                {
                    // ignore errors.
                }

                m_channel = null;
            }
        }

        /// <summary>
        /// An object used to synchronize access to the session state.
        /// </summary>
        /// <value>The synchronization object.</value>
        protected object SyncRoot
        {
            get { return m_lock; }
        }

        /// <summary>
        /// The authorization token used to connect to the server.
        /// </summary>
        /// <value>The authentication token.</value>
        protected NodeId AuthenticationToken
        {
            get
            {
                return m_authenticationToken;
            }

            set
            {
                m_authenticationToken = value;
            }
        }

        /// <summary>
        /// Updates the header of a service request.
        /// </summary>
        /// <param name="request">The request.</param>
        [Obsolete("Must override the version with useDefault parameter.")]
        protected virtual void UpdateRequestHeader(IServiceRequest request)
        {
            UpdateRequestHeader(request, request == null);
        }

        /// <summary>
        /// Updates the header of a service request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="useDefaults">if set to <c>true</c> use defaults].</param>
        protected virtual void UpdateRequestHeader(IServiceRequest request, bool useDefaults)
        {
            lock (m_lock)
            {
                if (request.RequestHeader == null)
                {
                    request.RequestHeader = new RequestHeader();
                }

                if (useDefaults)
                {
                    request.RequestHeader.ReturnDiagnostics = (uint)(int)m_returnDiagnostics;
                }

                if (request.RequestHeader.RequestHandle == 0)
                {
                    request.RequestHeader.RequestHandle = (uint)Utils.IncrementIdentifier(ref m_nextRequestHandle);
                }

                if (NodeId.IsNull(request.RequestHeader.AuthenticationToken))
                {
                    request.RequestHeader.AuthenticationToken = m_authenticationToken;
                }

                request.RequestHeader.Timestamp = DateTime.UtcNow;
                request.RequestHeader.AuditEntryId = CreateAuditLogEntry(request);
            }
        }

        /// <summary>
        /// Updates the header of a service request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="useDefaults">if set to <c>true</c> the no request header was provided.</param>
        /// <param name="serviceName">The name of the service.</param>
        protected virtual void UpdateRequestHeader(IServiceRequest request, bool useDefaults, string serviceName)
        {
            UpdateRequestHeader(request, useDefaults);
            int incrementedCount = Interlocked.Increment(ref m_pendingRequestCount);
            Utils.EventLog.ServiceCallStart(serviceName, (int)request.RequestHeader.RequestHandle, incrementedCount);
        }

        /// <summary>
        /// Called when a request completes.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="response">The response.</param>
        /// <param name="serviceName">The name of the service.</param>
        protected virtual void RequestCompleted(IServiceRequest request, IServiceResponse response, string serviceName)
        {
            uint requestHandle = 0;
            StatusCode statusCode = StatusCodes.Good;

            if (request != null)
            {
                requestHandle = request.RequestHeader.RequestHandle;
            }
            else if (response != null)
            {
                requestHandle = response.ResponseHeader.RequestHandle;
                statusCode = response.ResponseHeader.ServiceResult;
            }

            if (response == null)
            {
                statusCode = StatusCodes.Bad;
            }

            int pendingRequestCount = Interlocked.Decrement(ref m_pendingRequestCount);

            if (statusCode != StatusCodes.Good)
            {
                Utils.EventLog.ServiceCallBadStop(serviceName, (int)requestHandle, (int)statusCode.Code, pendingRequestCount);
            }
            else
            {
                Utils.EventLog.ServiceCallStop(serviceName, (int)requestHandle, pendingRequestCount);
            }
        }

        /// <summary>
        /// Creates an audit log entry for the request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>log entry</returns>
        protected virtual string CreateAuditLogEntry(IServiceRequest request)
        {
            return request.RequestHeader.AuditEntryId;
        }

        /// <summary>
        /// Throws an exception if a response contains an error.
        /// </summary>
        /// <param name="header">The header.</param>
        protected static void ValidateResponse(ResponseHeader header)
        {
            if (header == null)
            {
                throw new ServiceResultException(StatusCodes.BadUnknownResponse, "Null header in response.");
            }

            if (StatusCode.IsBad(header.ServiceResult))
            {
                throw new ServiceResultException(new ServiceResult(header.ServiceResult, header.ServiceDiagnostics, header.StringTable));
            }
        }
        #endregion

        #region Static Methods
        /// <summary>
        /// Validates a response returned by the server.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <param name="request">The request.</param>
        public static void ValidateResponse(IList response, IList request)
        {
            if (response is DiagnosticInfoCollection)
            {
                throw new ArgumentException("Must call ValidateDiagnosticInfos() for DiagnosticInfoCollections.", nameof(response));
            }

            if (response == null || response.Count != request.Count)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError, "The server returned a list without the expected number of elements.");
            }
        }

        /// <summary>
        /// Validates a response returned by the server.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <param name="request">The request.</param>
        public static void ValidateDiagnosticInfos(DiagnosticInfoCollection response, IList request)
        {
            // returning an empty list for diagnostic info arrays is allowed.
            if (response != null && response.Count != 0 && response.Count != request.Count)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError, "The server forgot to fill in the DiagnosticInfos array correctly when returning an operation level error.");
            }
        }

        /// <summary>
        /// Converts a service response to a ServiceResult object.
        /// </summary>
        /// <param name="statusCode">The status code.</param>
        /// <param name="index">The index.</param>
        /// <param name="diagnosticInfos">The diagnostic information.</param>
        /// <param name="responseHeader">The response header.</param>
        /// <returns>Converted service response.</returns>
        public static ServiceResult GetResult(
            StatusCode statusCode,
            int index,
            DiagnosticInfoCollection diagnosticInfos,
            ResponseHeader responseHeader)
        {
            if (diagnosticInfos != null && diagnosticInfos.Count > index)
            {
                return new ServiceResult(statusCode.Code, diagnosticInfos[index], responseHeader.StringTable);
            }

            return new ServiceResult(statusCode.Code);
        }

        /// <summary>
        /// Validates a DataValue returned from the server.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="expectedType">The expected type.</param>
        /// <param name="index">The index.</param>
        /// <param name="diagnosticInfos">The diagnostic information.</param>
        /// <param name="responseHeader">The response header.</param>
        /// <returns>Result of the vaidation</returns>
        public static ServiceResult ValidateDataValue(
            DataValue value,
            Type expectedType,
            int index,
            DiagnosticInfoCollection diagnosticInfos,
            ResponseHeader responseHeader)
        {
            // check for null.
            if (value == null)
            {
                return new ServiceResult(StatusCodes.BadUnexpectedError, "The server returned a value for a data value.");
            }

            // check status code.
            if (StatusCode.IsBad(value.StatusCode))
            {
                return GetResult(value.StatusCode, index, diagnosticInfos, responseHeader);
            }

            // check data type.
            if (expectedType != null)
            {
                if (!expectedType.IsInstanceOfType(value.Value))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadUnexpectedError,
                        "The server returned data value of type {0} when a value of type {1} was expected.",
                        (value.Value != null) ? value.Value.GetType().Name : "(null)",
                        expectedType.Name);
                }
            }

            return null;
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private ITransportChannel m_channel;
        private NodeId m_authenticationToken;
        private DiagnosticsMasks m_returnDiagnostics;
        private int m_nextRequestHandle;
        private int m_pendingRequestCount;
        private bool m_disposed;
        private bool m_useTransportChannel;
        #endregion
    }

    /// <summary>
	/// The client side interface with a UA server.
	/// </summary>
    public partial class SessionClient : ISessionClient
    {
        #region IDisposable Implementation
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_sessionId = null;
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The server assigned identifier for the current session.
        /// </summary>
        /// <value>The session id.</value>
        public NodeId SessionId
        {
            get
            {
                return m_sessionId;
            }
        }

        /// <summary>
        /// Whether a session has beed created with the server.
        /// </summary>
        /// <value><c>true</c> if connected; otherwise, <c>false</c>.</value>
        public bool Connected
        {
            get
            {
                return m_sessionId != null;
            }
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Called when a new session is created.
        /// </summary>
        /// <param name="sessionId">The session id.</param>
        /// <param name="sessionCookie">The session cookie.</param>
        public virtual void SessionCreated(NodeId sessionId, NodeId sessionCookie)
        {
            lock (m_lock)
            {
                m_sessionId = sessionId;
                AuthenticationToken = sessionCookie;
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private NodeId m_sessionId;
        #endregion
    }
}
