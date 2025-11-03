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

#nullable enable

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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

        /// <summary>
        /// How to record activity of the client
        /// </summary>
        public ClientTraceFlags ActivityTraceFlags { get; set; }

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
            if (!Disposed)
            {
                CloseChannelAsync(default).GetAwaiter().GetResult();

                Utils.SilentDispose(m_meter);
                m_instruments.Clear();
                m_meter = null;

                Disposed = true;
            }
        }

        /// <inheritdoc/>
        public EndpointDescription? Endpoint => NullableTransportChannel?.EndpointDescription;

        /// <inheritdoc/>
        public EndpointConfiguration? EndpointConfiguration
            => NullableTransportChannel?.EndpointConfiguration;

        /// <inheritdoc/>
        public IServiceMessageContext? MessageContext => NullableTransportChannel?.MessageContext;

        /// <inheritdoc/>
        public ITransportChannel? NullableTransportChannel
        {
            get
            {
                ITransportChannel? channel = m_channel;

                if (channel != null && Disposed)
                {
                    // This is a bug in your code.
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecureChannelClosed,
                        "Channel is set but client has been disposed.");
                }

                return channel;
            }
        }

        /// <inheritdoc/>
        public ITransportChannel TransportChannel
        {
            get
            {
                ITransportChannel? channel = m_channel;
                if (channel != null)
                {
                    if (Disposed)
                    {
                        // This is a bug in your code.
                        throw ServiceResultException.Create(
                            StatusCodes.BadSecureChannelClosed,
                            "Channel is set but client has been disposed.");
                    }
                    return channel;
                }
                if (Disposed)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecureChannelClosed,
                        "Client has been disposed and channel is closed.");
                }
                throw new ServiceResultException(
                    StatusCodes.BadSecureChannelClosed,
                    "Channel has been closed.");
            }
            protected set
            {
                ITransportChannel? channel = Interlocked.Exchange(ref m_channel, value);

                if (ReferenceEquals(channel, value))
                {
                    return;
                }

                if (channel != null)
                {
                    try
                    {
                        // TODO: Make async method instead of setter
                        channel.CloseAsync(default).AsTask().GetAwaiter().GetResult();
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
        internal IChannelBase? InnerChannel
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
                ITransportChannel? channel = NullableTransportChannel;
                if (channel != null)
                {
                    channel.OperationTimeout = value;
                }
            }
        }

        /// <inheritdoc/>
        public int DefaultTimeoutHint { get; set; }

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
            ITransportChannel? channel = Interlocked.Exchange(ref m_channel, null);
            if (channel != null)
            {
                await channel.CloseAsync(ct).ConfigureAwait(false);
            }

            AuthenticationToken = NodeId.Null;

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
        [Obsolete("Use CloseChannelAsync instead.")]
        protected void CloseChannel()
        {
            CloseChannelAsync(default).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Closes the channel.
        /// </summary>
        protected async Task CloseChannelAsync(CancellationToken ct)
        {
            ITransportChannel? channel = Interlocked.Exchange(ref m_channel, null);

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
            ITransportChannel? channel = Interlocked.Exchange(ref m_channel, null);

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
        /// The authorization token used to connect to the server.
        /// </summary>
        /// <value>The authentication token.</value>
        protected NodeId AuthenticationToken { get; set; } = NodeId.Null;

        /// <summary>
        /// Updates the header of a service request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="useDefaults">if set to <c>true</c> use defaults.</param>
        protected virtual void UpdateRequestHeader(IServiceRequest request, bool useDefaults)
        {
            ThrowIfDisposed();
            request.RequestHeader ??= new RequestHeader();

            if (request.RequestHeader.ReturnDiagnostics == 0)
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

            if (request.RequestHeader.TimeoutHint == 0)
            {
                if (DefaultTimeoutHint > 0)
                {
                    request.RequestHeader.TimeoutHint = (uint)DefaultTimeoutHint;
                }
                else if (OperationTimeout > 0)
                {
                    request.RequestHeader.TimeoutHint = (uint)OperationTimeout;
                }
            }

            request.RequestHeader.Timestamp = DateTime.UtcNow;
            request.RequestHeader.AuditEntryId = CreateAuditLogEntry(request);
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
            if (ActivityTraceFlags == ClientTraceFlags.None)
            {
                // Do not record or propagate activity
                return;
            }

            if ((ActivityTraceFlags & ClientTraceFlags.Log) != 0)
            {
                m_logger.LogInformation("{Activity}#{Handle} started...", serviceName,
                    request.RequestHeader.RequestHandle);
            }
            if ((ActivityTraceFlags & ClientTraceFlags.EventLog) != 0)
            {
                Utils.EventLog.ServiceCallStart(
                    serviceName,
                    (int)request.RequestHeader.RequestHandle,
                    incrementedCount);
            }
            if ((ActivityTraceFlags & ClientTraceFlags.Traces) != 0)
            {
                Activity? context = Activity.Current;
                if (context == null)
                {
                    return;
                }

                context.AddEvent(new ActivityEvent("Started", tags:
                [
                    new System.Collections.Generic.KeyValuePair<string, object?>(
                        nameof(RequestHeader.RequestHandle),
                        (object?)request.RequestHeader.RequestHandle),
                    new System.Collections.Generic.KeyValuePair<string, object?>(
                        nameof(RequestHeader.AuditEntryId),
                        (object?)request.RequestHeader.AuditEntryId)
                ]));

                // https://reference.opcfoundation.org/Core/Part26/v105/docs/5.5.4
                Span<byte> spanId = stackalloc byte[8];
                Span<byte> traceId = stackalloc byte[16];
                context.SpanId.CopyTo(spanId);
                context.TraceId.CopyTo(traceId);
                var spanContextParameter = new KeyValuePair
                {
                    Key = "SpanContext",
                    Value = new Variant(new SpanContextDataType
                    {
#if NET8_0_OR_GREATER
                        SpanId = BitConverter.ToUInt64(spanId),
                        TraceId = (Uuid)new Guid(traceId)
#else
                        SpanId = BitConverter.ToUInt64(spanId.ToArray(), 0),
                        TraceId = (Uuid)new Guid(traceId.ToArray())
#endif
                    })
                };
                if (request.RequestHeader.AdditionalHeader?.Body == null)
                {
                    var additionalHeader = new AdditionalParametersType();
                    additionalHeader.Parameters.Add(spanContextParameter);
                    request.RequestHeader.AdditionalHeader
                        = new ExtensionObject(additionalHeader);
                }
                else if (request.RequestHeader.AdditionalHeader.Body is
                    AdditionalParametersType existingParameters)
                {
                    // Merge the trace data into the existing parameters.
                    existingParameters.Parameters.Add(spanContextParameter);
                }
            }
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
            int pendingRequestCount = Interlocked.Decrement(ref m_pendingRequestCount);
            if (ActivityTraceFlags == ClientTraceFlags.None)
            {
                // Do not record results
                return;
            }

            uint requestHandle = 0;
            StatusCode statusCode;
            if (response != null)
            {
                requestHandle = response.ResponseHeader.RequestHandle;
                statusCode = response.ResponseHeader.ServiceResult;
            }
            else
            {
                if (request != null)
                {
                    requestHandle = request.RequestHeader.RequestHandle;
                }
                statusCode = StatusCodes.Bad;
            }

            DateTime? timestamp = request?.RequestHeader?.Timestamp;
            TimeSpan? duration = timestamp != null ? DateTime.UtcNow - timestamp.Value : null;
            if ((ActivityTraceFlags & ClientTraceFlags.Log) != 0)
            {
                if (ServiceResult.IsGood(statusCode))
                {
                    m_logger.LogInformation("{Activity}#{Handle} success received after {Elapsed}.",
                        serviceName, requestHandle, duration);
                }
                else
                {
                    m_logger.LogError("{Activity}#{Handle} failed with {StatusCode} in {Elapsed}.",
                        serviceName, requestHandle, statusCode, duration);
                }
            }
            if ((ActivityTraceFlags & ClientTraceFlags.EventLog) != 0)
            {
                if (statusCode != StatusCodes.Good)
                {
                    Utils.EventLog.ServiceCallBadStop(
                        serviceName,
                        (int)requestHandle,
                        (int)statusCode.Code,
                        pendingRequestCount);
                }
                else
                {
                    Utils.EventLog.ServiceCallStop(
                        serviceName,
                        (int)requestHandle,
                        pendingRequestCount);
                }
            }
            // Add event to current trace
            if ((ActivityTraceFlags & ClientTraceFlags.Traces) != 0)
            {
                Activity? context = Activity.Current;
                if (context == null)
                {
                    return;
                }
                context.AddEvent(new ActivityEvent("Completed", tags:
                [
                    new System.Collections.Generic.KeyValuePair<string, object?>(
                        nameof(RequestHeader.RequestHandle),
                        (object?)requestHandle),
                    new System.Collections.Generic.KeyValuePair<string, object?>(
                        nameof(ResponseHeader.ServiceResult),
                        (object?)statusCode)
                ]));
            }
            // Record request duration metrics
            if ((ActivityTraceFlags & ClientTraceFlags.Metrics) != 0 &&
                m_meter != null &&
                duration != null)
            {
                GetDurationInstrument(m_meter).Record(
                    duration.Value.TotalSeconds,
                    new TagList(
                    new System.Collections.Generic.KeyValuePair<string, object?>(
                        "opc.ua.request.service",
                        serviceName),
                    new System.Collections.Generic.KeyValuePair<string, object?>(
                        "opc.ua.response.status.code",
                        statusCode.CodeBits),
                    new System.Collections.Generic.KeyValuePair<string, object?>(
                        "server.address",
                        Endpoint?.EndpointUrl),
                    new System.Collections.Generic.KeyValuePair<string, object?>(
                        "opc.ua.request.timeout",
                        NullableTransportChannel?.OperationTimeout)));
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

            if (response == null || request == null || response.Count != request.Count)
            {
                throw ServiceResultException.Unexpected(
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
            if (response != null && response.Count != 0 && request != null && response.Count != request.Count)
            {
                throw ServiceResultException.Unexpected(
                    "The server failed to fill in the DiagnosticInfos array correctly when returning an operation level error.");
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
            DiagnosticInfoCollection? diagnosticInfos,
            ResponseHeader? responseHeader)
        {
            if (diagnosticInfos != null && diagnosticInfos.Count > index)
            {
                return new ServiceResult(
                    statusCode.Code,
                    diagnosticInfos[index],
                    responseHeader?.StringTable ?? []);
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
                return ServiceResult.Create(
                    StatusCodes.BadUnexpectedError,
                    "The server did not return a value for a data value.");
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
                    StatusCodes.BadTypeMismatch,
                    "The server returned data value of type {0} when a value of type {1} was expected.",
                    value.Value != null ? value.Value.GetType().Name : "(null)",
                    expectedType.Name);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Throw if the object has been disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        protected void ThrowIfDisposed()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(ClientBase));
            }
        }

        /// <summary>
        /// Get or add duration instrument
        /// </summary>
        /// <param name="meter"></param>
        /// <returns></returns>
        private Histogram<double> GetDurationInstrument(Meter meter)
        {
            // TODO: This should be created in constructor which
            // we will do as soon as clientBase accepts ITelemetryContext
            return (Histogram<double>)m_instruments.GetOrAdd(
                "duration",
                n => meter.CreateHistogram(
                    "opc.ua.client.request.duration",
                    "s",
                    "Measures the time taken to perform the request",
                    advice: new InstrumentAdvice<double>
                    {
                        HistogramBucketBoundaries =
                        [
                            0.005,
                            0.01,
                            0.025,
                            0.05,
                            0.075,
                            0.1,
                            0.25,
                            0.5,
                            0.75,
                            1,
                            2.5,
                            5,
                            7.5,
                            10,
                            30,
                            60
                        ]
                    }));
        }

#pragma warning disable IDE1006 // Naming Styles
        /// <summary>
        /// Logger to be used by the client inheritance chain
        /// </summary>
        protected ILogger m_logger { get; set; } = Utils.Null.Logger;

        /// <summary>
        /// Meter to be used by the client inheritance chain
        /// </summary>
        protected Meter? m_meter { get; set; }
#pragma warning restore IDE1006 // Naming Styles

        private ITransportChannel? m_channel;
        private readonly ConcurrentDictionary<string, Instrument<double>> m_instruments = [];
        private int m_nextRequestHandle;
        private int m_pendingRequestCount;
    }

    /// <summary>
    /// Defines flags that control activity tracing
    /// using the telemtry context provided to the client.
    /// </summary>
    [Flags]
    public enum ClientTraceFlags
    {
        /// <summary>
        /// No telemetry is recorded (default).
        /// </summary>
        None = 0,

        /// <summary>
        /// Record an instrumentation metric for the activity
        /// </summary>
        Metrics = 0x1,

        /// <summary>
        /// Forward activity trace information to the server
        /// </summary>
        Traces = 0x2,

        /// <summary>
        /// Write the activity as a record to the log
        /// </summary>
        Log = 0x4,

        /// <summary>
        /// Write activity to event log (legacy)
        /// </summary>
        EventLog = 0x10
    }
}
