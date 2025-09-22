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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Opc.Ua
{
    /// <summary>
    /// The client side interface with a UA server.
    /// </summary>
    public class ClientBase : IClientBase
    {
        /// <summary>
        /// Intializes the object with a channel and a message context.
        /// </summary>
        /// <param name="channel">The channel.</param>
        public ClientBase(ITransportChannel channel)
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            InitializeChannel(channel);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and
        /// unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            CloseChannel();

            Disposed = true;
        }

        /// <inheritdoc/>
        public EndpointDescription Endpoint => NullableTransportChannel?.EndpointDescription;

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration
            => NullableTransportChannel?.EndpointConfiguration;

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext => NullableTransportChannel?.MessageContext;

        /// <inheritdoc/>
        public ITransportChannel NullableTransportChannel
        {
            get
            {
                ITransportChannel channel = m_channel;

                if (channel != null && Disposed)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecureChannelClosed,
                        "Channel has been closed.");
                }

                return channel;
            }
        }

        /// <inheritdoc/>
        public ITransportChannel TransportChannel
        {
            get
            {
                ITransportChannel channel = m_channel;

                if (channel != null)
                {
                    if (!Disposed)
                    {
                        return channel;
                    }
                    throw new ServiceResultException(
                        StatusCodes.BadSecureChannelClosed,
                        "Channel has been disposed.");
                }
                throw new ServiceResultException(
                    StatusCodes.BadSecureChannelClosed,
                    "Channel has been closed.");
            }
            protected set
            {
                ITransportChannel channel = Interlocked.Exchange(ref m_channel, value);

                if (ReferenceEquals(channel, value))
                {
                    return;
                }

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
            }
        }

        /// <summary>
        /// The channel being wrapped by the client object.
        /// Note: deprecated, only to fulfill a few references
        /// in the generated code.
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

        /// <inheritdoc/>
        public DiagnosticsMasks ReturnDiagnostics { get; set; }

        /// <inheritdoc/>
        public int OperationTimeout
        {
            get => NullableTransportChannel?.OperationTimeout ?? 0;
            set
            {
                ITransportChannel channel = NullableTransportChannel;
                if (channel != null)
                {
                    channel.OperationTimeout = value;
                }
            }
        }

        /// <inheritdoc/>
        public virtual void AttachChannel(ITransportChannel channel)
        {
            InitializeChannel(channel);
        }

        /// <inheritdoc/>
        public virtual void DetachChannel()
        {
            Interlocked.Exchange(ref m_channel, null);
        }

        /// <inheritdoc/>
        public virtual async Task<StatusCode> CloseAsync(CancellationToken ct = default)
        {
            ITransportChannel channel = Interlocked.Exchange(ref m_channel, null);
            if (channel != null)
            {
                await channel.CloseAsync(ct).ConfigureAwait(false);
            }

            AuthenticationToken = null;

            return StatusCodes.Good;
        }

        /// <summary>
        /// Whether the object has been disposed.
        /// </summary>
        /// <value><c>true</c> if disposed; otherwise, <c>false</c>.</value>
        public bool Disposed { get; private set; }

        /// <summary>
        /// Generates a unique request handle.
        /// </summary>
        public uint NewRequestHandle()
        {
            return (uint)Utils.IncrementIdentifier(ref m_nextRequestHandle);
        }

        /// <summary>
        /// Initializes the channel.
        /// </summary>
        protected void InitializeChannel(ITransportChannel channel)
        {
            Interlocked.Exchange(ref m_channel, channel);
        }

        /// <summary>
        /// Closes the channel.
        /// </summary>
        protected void CloseChannel()
        {
            ITransportChannel channel = Interlocked.Exchange(ref m_channel, null);

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
        }

        /// <summary>
        /// Closes the channel.
        /// </summary>
        protected async Task CloseChannelAsync(CancellationToken ct)
        {
            ITransportChannel channel = Interlocked.Exchange(ref m_channel, null);

            if (channel != null)
            {
                try
                {
                    await channel.CloseAsync(ct).ConfigureAwait(false);
                    channel.Dispose();
                }
                catch
                {
                    // ignore errors.
                }
            }
        }

        /// <summary>
        /// Disposes the channel.
        /// </summary>
        protected void DisposeChannel()
        {
            ITransportChannel channel = Interlocked.Exchange(ref m_channel, null);

            try
            {
                channel?.Dispose();
            }
            catch
            {
                // ignore errors.
            }
        }

        /// <summary>
        /// An object used to synchronize access to the session state.
        /// </summary>
        /// <value>The synchronization object.</value>
        protected object SyncRoot { get; } = new object();

        /// <summary>
        /// The authorization token used to connect to the server.
        /// </summary>
        /// <value>The authentication token.</value>
        protected NodeId AuthenticationToken { get; set; }

        /// <summary>
        /// Updates the header of a service request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="useDefaults">if set to <c>true</c> use defaults].</param>
        protected virtual void UpdateRequestHeader(IServiceRequest request, bool useDefaults)
        {
            lock (SyncRoot)
            {
                request.RequestHeader ??= new RequestHeader();

                if (useDefaults)
                {
                    request.RequestHeader.ReturnDiagnostics = (uint)(int)ReturnDiagnostics;
                }

                if (request.RequestHeader.RequestHandle == 0)
                {
                    request.RequestHeader.RequestHandle = (uint)Utils.IncrementIdentifier(
                        ref m_nextRequestHandle);
                }

                if (NodeId.IsNull(request.RequestHeader.AuthenticationToken))
                {
                    request.RequestHeader.AuthenticationToken = AuthenticationToken;
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
        protected virtual void UpdateRequestHeader(
            IServiceRequest request,
            bool useDefaults,
            string serviceName)
        {
            UpdateRequestHeader(request, useDefaults);
            int incrementedCount = Interlocked.Increment(ref m_pendingRequestCount);
            Utils.EventLog.ServiceCallStart(
                serviceName,
                (int)request.RequestHeader.RequestHandle,
                incrementedCount);

            m_logger.LogTrace(
                "{ServiceName} Called. RequestHandle={RequestHandle}, PendingRequestCount={PendingRequestCount}",
                serviceName,
                request.RequestHeader.RequestHandle,
                incrementedCount);
        }

        /// <summary>
        /// Called when a request completes.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="response">The response.</param>
        /// <param name="serviceName">The name of the service.</param>
        protected virtual void RequestCompleted(
            IServiceRequest request,
            IServiceResponse response,
            string serviceName)
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
                Utils.EventLog.ServiceCallBadStop(
                    serviceName,
                    (int)requestHandle,
                    (int)statusCode.Code,
                    pendingRequestCount);

                m_logger.LogTrace(
                    "{Service} Completed. RequestHandle={RequestHandle}, PendingRequestCount={PendingRequestCount}, StatusCode={StatusCode}",
                    serviceName,
                    requestHandle,
                    pendingRequestCount,
                    statusCode);
            }
            else
            {
                Utils.EventLog.ServiceCallStop(
                    serviceName,
                    (int)requestHandle,
                    pendingRequestCount);

                m_logger.LogTrace(
                    "{Service} Completed. RequestHandle={RequestHandle}, PendingRequestCount={PendingRequestCount}",
                    serviceName,
                    requestHandle,
                    pendingRequestCount);
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
        /// <exception cref="ServiceResultException"></exception>
        protected static void ValidateResponse(ResponseHeader header)
        {
            if (header == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnknownResponse,
                    "Null header in response.");
            }

            if (StatusCode.IsBad(header.ServiceResult))
            {
                throw new ServiceResultException(
                    new ServiceResult(
                        header.ServiceResult,
                        header.ServiceDiagnostics,
                        header.StringTable));
            }
        }

        /// <summary>
        /// Validates a response returned by the server.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <param name="request">The request.</param>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static void ValidateResponse(IList response, IList request)
        {
            if (response is DiagnosticInfoCollection)
            {
                throw new ArgumentException(
                    "Must call ValidateDiagnosticInfos() for DiagnosticInfoCollections.",
                    nameof(response));
            }

            if (response == null || response.Count != request.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "The server returned a list without the expected number of elements.");
            }
        }

        /// <summary>
        /// Validates a response returned by the server.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <param name="request">The request.</param>
        /// <exception cref="ServiceResultException"></exception>
        public static void ValidateDiagnosticInfos(DiagnosticInfoCollection response, IList request)
        {
            // returning an empty list for diagnostic info arrays is allowed.
            if (response != null && response.Count != 0 && response.Count != request.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "The server forgot to fill in the DiagnosticInfos array correctly when returning an operation level error.");
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
                return new ServiceResult(
                    statusCode.Code,
                    diagnosticInfos[index],
                    responseHeader.StringTable);
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
                return new ServiceResult(
                    StatusCodes.BadUnexpectedError,
                    "The server returned a value for a data value.");
            }

            // check status code.
            if (StatusCode.IsBad(value.StatusCode))
            {
                return GetResult(value.StatusCode, index, diagnosticInfos, responseHeader);
            }

            // check data type.
            if (expectedType != null && !expectedType.IsInstanceOfType(value.Value))
            {
                return ServiceResult.Create(
                    StatusCodes.BadUnexpectedError,
                    "The server returned data value of type {0} when a value of type {1} was expected.",
                    value.Value != null ? value.Value.GetType().Name : "(null)",
                    expectedType.Name);
            }

            return null;
        }

        /// <summary>
        /// Logger to be used by the client inheritance chain
        /// </summary>
#pragma warning disable IDE1006 // Naming Styles
        protected ILogger m_logger { get; set; } = Utils.NullLogger.Instance;
#pragma warning restore IDE1006 // Naming Styles

        private ITransportChannel m_channel;
        private int m_nextRequestHandle;
        private int m_pendingRequestCount;
    }
}
