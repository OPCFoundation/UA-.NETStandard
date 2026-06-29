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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;

namespace Opc.Ua.Stress.Tests.Channels.Fakes
{
    /// <summary>
    /// Fault behavior used by <see cref="FakeTransport"/>.
    /// </summary>
    public readonly record struct FaultMode
    {
        /// <summary>
        /// No transport fault is injected.
        /// </summary>
        public static FaultMode None { get; } = new(FakeTransportFaultKind.None, 0);

        /// <summary>
        /// Opening or reconnecting the transport throws a service exception.
        /// </summary>
        public static FaultMode OpenFails { get; } = new(FakeTransportFaultKind.OpenFails, 0);

        /// <summary>
        /// Opening or reconnecting the transport waits for the configured hang period.
        /// </summary>
        public static FaultMode OpenHangs { get; } = new(FakeTransportFaultKind.OpenHangs, 0);

        /// <summary>
        /// Service requests throw a service exception.
        /// </summary>
        public static FaultMode RequestFails { get; } = new(FakeTransportFaultKind.RequestFails, 0);

        /// <summary>
        /// Service requests wait for the configured hang period.
        /// </summary>
        public static FaultMode RequestHangs { get; } = new(FakeTransportFaultKind.RequestHangs, 0);

        /// <summary>
        /// Creates a mode that drops one specific request attempt.
        /// </summary>
        /// <param name="requestNumber">The one-based request number to drop.</param>
        /// <returns>A fault mode for the requested drop point.</returns>
        public static FaultMode DropOnNthRequest(int requestNumber)
        {
            if (requestNumber <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requestNumber));
            }

            return new FaultMode(FakeTransportFaultKind.DropOnNthRequest, requestNumber);
        }

        internal FakeTransportFaultKind Kind { get; }

        internal int DropRequestNumber { get; }

        private FaultMode(FakeTransportFaultKind kind, int dropRequestNumber)
        {
            Kind = kind;
            DropRequestNumber = dropRequestNumber;
        }
    }

    /// <summary>
    /// Configurable in-memory transport channel for channel-manager stress tests.
    /// </summary>
    public sealed class FakeTransport : ITransportChannel, ISecureChannel
    {
        /// <summary>
        /// Initializes a new fake transport with no telemetry context.
        /// </summary>
        public FakeTransport()
            : this(telemetry: null)
        {
        }

        /// <summary>
        /// Initializes a new fake transport.
        /// </summary>
        /// <param name="telemetry">Telemetry context to attach to generated message contexts.</param>
        public FakeTransport(ITelemetryContext? telemetry)
        {
            m_telemetry = telemetry;
            m_messageContext = ServiceMessageContext.Create(telemetry);
        }

        /// <summary>
        /// Gets or sets the delay applied before a successful open attempt.
        /// </summary>
        public TimeSpan OpenDelay
        {
            get
            {
                lock (m_lock)
                {
                    return m_openDelay;
                }
            }
            set
            {
                if (value < TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                lock (m_lock)
                {
                    m_openDelay = value;
                }
            }
        }

        /// <summary>
        /// Gets the number of open attempts.
        /// </summary>
        public int OpenCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_openCount;
                }
            }
        }

        /// <summary>
        /// Gets the number of close attempts.
        /// </summary>
        public int CloseCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_closeCount;
                }
            }
        }

        /// <summary>
        /// Gets the number of dispose attempts.
        /// </summary>
        public int DisposeCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_disposeCount;
                }
            }
        }

        /// <summary>
        /// Gets the number of reconnect attempts.
        /// </summary>
        public int ReconnectCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_reconnectCount;
                }
            }
        }

        /// <summary>
        /// Gets the client certificate thumbprint used for the last successful open.
        /// </summary>
        public string? ClientCertificateThumbprint
        {
            get
            {
                lock (m_lock)
                {
                    return m_clientCertificateThumbprint;
                }
            }
        }

        /// <summary>
        /// Gets or sets the optional barrier awaited by reconnect calls.
        /// </summary>
        public ChaosBarrier? ReconnectBarrier
        {
            get
            {
                lock (m_lock)
                {
                    return m_reconnectBarrier;
                }
            }
            set
            {
                lock (m_lock)
                {
                    m_reconnectBarrier = value;
                }
            }
        }

        /// <summary>
        /// Gets the number of service request attempts.
        /// </summary>
        public int RequestCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_requestCount;
                }
            }
        }

        /// <summary>
        /// Gets or sets a barrier that transport open and reconnect operations wait on.
        /// </summary>
        public ChaosBarrier? OpenBarrier
        {
            get
            {
                lock (m_lock)
                {
                    return m_openBarrier;
                }
            }
            set
            {
                lock (m_lock)
                {
                    m_openBarrier = value;
                }
            }
        }

        /// <inheritdoc/>
        public ChannelToken? CurrentToken
        {
            get
            {
                lock (m_lock)
                {
                    return m_currentToken;
                }
            }
        }

        /// <inheritdoc/>
        public TransportChannelFeatures SupportedFeatures
        {
            get
            {
                lock (m_lock)
                {
                    return m_supportedFeatures;
                }
            }
            set
            {
                lock (m_lock)
                {
                    m_supportedFeatures = value;
                }
            }
        }

        /// <inheritdoc/>
        public EndpointDescription EndpointDescription
        {
            get
            {
                lock (m_lock)
                {
                    return m_endpointDescription ?? throw new InvalidOperationException("The transport is not open.");
                }
            }
        }

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration
        {
            get
            {
                lock (m_lock)
                {
                    return m_endpointConfiguration ?? throw new InvalidOperationException("The transport is not open.");
                }
            }
        }

        /// <inheritdoc/>
        public byte[] ChannelThumbprint => [];

        /// <inheritdoc/>
        public byte[] ClientChannelCertificate => [];

        /// <inheritdoc/>
        public byte[] ServerChannelCertificate => [];

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext
        {
            get
            {
                lock (m_lock)
                {
                    return m_messageContext;
                }
            }
        }

        /// <inheritdoc/>
        public int OperationTimeout
        {
            get
            {
                lock (m_lock)
                {
                    return m_operationTimeout;
                }
            }
            set
            {
                lock (m_lock)
                {
                    m_operationTimeout = value;
                }
            }
        }

        /// <summary>
        /// Test-only byte transport surface; setting this on the fake lets
        /// callers exercise diagnostic paths that read
        /// <c>(channel as UaSCUaBinaryTransportChannel)?.Transport</c>.
        /// </summary>
        public IUaSCByteTransport? Socket
        {
            get
            {
                lock (m_lock)
                {
                    return m_socket;
                }
            }
            set
            {
                lock (m_lock)
                {
                    m_socket = value;
                }
            }
        }

        /// <inheritdoc/>
        public event ChannelTokenActivatedEventHandler OnTokenActivated
        {
            add
            {
                lock (m_lock)
                {
                    m_onTokenActivated += value;
                }
            }
            remove
            {
                lock (m_lock)
                {
                    m_onTokenActivated -= value;
                }
            }
        }

        /// <summary>
        /// Configures the fault injected by subsequent transport calls.
        /// </summary>
        /// <param name="mode">The fault mode to apply.</param>
        /// <param name="status">Optional service result used when a fault throws.</param>
        /// <param name="hangFor">Optional hang duration. The default is an infinite wait.</param>
        public void ConfigureFault(FaultMode mode, ServiceResult? status = null, TimeSpan? hangFor = null)
        {
            ValidateHangFor(hangFor);

            lock (m_lock)
            {
                m_faultMode = mode;
                m_faultStatus = status;
                m_hangFor = hangFor ?? Timeout.InfiniteTimeSpan;
            }
        }

        /// <inheritdoc/>
        public async ValueTask OpenAsync(
            Uri url,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            await OpenCoreAsync(url, settings, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask OpenAsync(
            ITransportWaitingConnection connection,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            Uri? url = TryCreateUri(settings.Description?.EndpointUrl);
            await OpenCoreAsync(url, settings, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask ReconnectAsync(
            ITransportWaitingConnection? connection = null,
            CancellationToken ct = default)
        {
            _ = connection;

            FaultSnapshot fault;
            ChaosBarrier? reconnectBarrier;
            lock (m_lock)
            {
                m_reconnectCount++;
                fault = CaptureFault();
                reconnectBarrier = m_reconnectBarrier;
            }

            if (reconnectBarrier != null)
            {
                await reconnectBarrier.SignalAndWaitForReleaseAsync(ct).ConfigureAwait(false);
            }

            await ApplyOpenFaultAsync(fault, ct).ConfigureAwait(false);
            ActivateToken();
        }

        /// <inheritdoc/>
        public async ValueTask<IServiceResponse> SendRequestAsync(
            IServiceRequest request,
            CancellationToken ct = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            FaultSnapshot fault;
            int requestNumber;
            lock (m_lock)
            {
                m_requestCount++;
                requestNumber = m_requestCount;
                fault = CaptureFault();
            }

            await ApplyRequestFaultAsync(fault, requestNumber, ct).ConfigureAwait(false);
            return CreateResponse(request);
        }

        /// <inheritdoc/>
        public ValueTask CloseAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            ChannelToken? currentToken;
            lock (m_lock)
            {
                m_closeCount++;
                m_isOpen = false;
                currentToken = m_currentToken;
                m_currentToken = null;
            }

            currentToken?.Dispose();
            return new ValueTask();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            ChannelToken? currentToken;
            lock (m_lock)
            {
                m_disposeCount++;
                m_isOpen = false;
                currentToken = m_currentToken;
                m_currentToken = null;
            }

            currentToken?.Dispose();
        }

        private static Uri? TryCreateUri(string? endpointUrl)
        {
            if (string.IsNullOrWhiteSpace(endpointUrl))
            {
                return null;
            }

            return Uri.TryCreate(endpointUrl, UriKind.Absolute, out Uri? uri) ? uri : null;
        }

        private static void ValidateHangFor(TimeSpan? hangFor)
        {
            if (hangFor < TimeSpan.Zero && hangFor != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(hangFor));
            }
        }

        private static async ValueTask DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            if (delay <= TimeSpan.Zero && delay != Timeout.InfiniteTimeSpan)
            {
                return;
            }

            await Task.Delay(delay, ct).ConfigureAwait(false);
        }

        private static ResponseHeader CreateGoodHeader()
        {
            return new ResponseHeader
            {
                ServiceResult = StatusCodes.Good,
                Timestamp = DateTime.UtcNow
            };
        }

        private static ActivateSessionResponse CreateActivateSessionResponse()
        {
            return new ActivateSessionResponse
            {
                ResponseHeader = CreateGoodHeader(),
                ServerNonce = ByteString.Empty,
                Results = [],
                DiagnosticInfos = []
            };
        }

        private static ReadResponse CreateReadResponse(ReadRequest request)
        {
            return new ReadResponse
            {
                ResponseHeader = CreateGoodHeader(),
                Results = CreateReadResults(request),
                DiagnosticInfos = []
            };
        }

        private static ArrayOf<DataValue> CreateReadResults(ReadRequest request)
        {
            int count = request.NodesToRead.Count;
            if (count == 1 && Equals(request.NodesToRead[0].NodeId, VariableIds.Server_ServerStatus_State))
            {
                return [CreateDataValue(new Variant((int)ServerState.Running))];
            }

            if (count == 2)
            {
                return
                [
                    CreateDataValue(new Variant(ArrayOf.Wrapped(Namespaces.OpcUa))),
                    CreateDataValue(new Variant(ArrayOf.Wrapped("urn:localhost:server")))
                ];
            }

            var values = new DataValue[count];
            for (int index = 0; index < count; index++)
            {
                if (count > 1 && index is 12 or 13 or 14)
                {
                    values[index] = CreateDataValue(new Variant((ushort)0));
                }
                else if (count > 1 && index == 18)
                {
                    values[index] = CreateDataValue(new Variant(0d));
                }
                else
                {
                    values[index] = CreateDataValue(new Variant(0u));
                }
            }

            return new ArrayOf<DataValue>(values);
        }

        private static DataValue CreateDataValue(Variant value)
        {
            return new DataValue(value, StatusCodes.Good);
        }

        private async ValueTask OpenCoreAsync(
            Uri? url,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            FaultSnapshot fault;
            TimeSpan openDelay;
            lock (m_lock)
            {
                m_openCount++;
                fault = CaptureFault();
                openDelay = m_openDelay;
            }

            await DelayAsync(openDelay, ct).ConfigureAwait(false);
            await ApplyOpenFaultAsync(fault, ct).ConfigureAwait(false);
            CompleteOpen(url, settings);
        }

        private void CompleteOpen(Uri? url, TransportChannelSettings settings)
        {
            EndpointDescription description = settings.Description ?? new EndpointDescription
            {
                EndpointUrl = url?.ToString() ?? "opc.tcp://localhost:4840/FakeTransport"
            };
            EndpointConfiguration configuration = settings.Configuration ?? new EndpointConfiguration();
            ServiceMessageContext messageContext = new(m_telemetry!, settings.Factory ?? EncodeableFactory.Create())
            {
                NamespaceUris = settings.NamespaceUris ?? new NamespaceTable()
            };

            lock (m_lock)
            {
                m_endpointDescription = description;
                m_endpointConfiguration = configuration;
                m_messageContext = messageContext;
                m_clientCertificateThumbprint = settings.ClientCertificate?.Thumbprint;
                m_isOpen = true;
            }

            ActivateToken();
        }

        private async ValueTask ApplyOpenFaultAsync(FaultSnapshot fault, CancellationToken ct)
        {
            if (fault.OpenBarrier != null)
            {
                await fault.OpenBarrier.SignalAndWaitForReleaseAsync(ct).ConfigureAwait(false);
            }

            switch (fault.Mode.Kind)
            {
                case FakeTransportFaultKind.OpenFails:
                    throw CreateFaultException(fault.Status, "Fake transport open failed.");
                case FakeTransportFaultKind.OpenHangs:
                    await DelayAsync(fault.HangFor, ct).ConfigureAwait(false);
                    break;
            }
        }

        private async ValueTask ApplyRequestFaultAsync(
            FaultSnapshot fault,
            int requestNumber,
            CancellationToken ct)
        {
            switch (fault.Mode.Kind)
            {
                case FakeTransportFaultKind.RequestFails:
                    throw CreateFaultException(fault.Status, "Fake transport request failed.");
                case FakeTransportFaultKind.RequestHangs:
                    await DelayAsync(fault.HangFor, ct).ConfigureAwait(false);
                    break;
                case FakeTransportFaultKind.DropOnNthRequest when requestNumber == fault.Mode.DropRequestNumber:
                    throw CreateFaultException(fault.Status, "Fake transport dropped the configured request.");
            }
        }

        private void ActivateToken()
        {
            ChannelToken currentToken;
            ChannelToken? previousToken;
            ChannelTokenActivatedEventHandler? handler;
            lock (m_lock)
            {
                if (!m_isOpen)
                {
                    return;
                }

                m_tokenId++;
                currentToken = new ChannelToken
                {
                    ChannelId = m_channelId,
                    TokenId = m_tokenId,
                    CreatedAt = DateTime.UtcNow,
                    Lifetime = 60000
                };
                previousToken = m_currentToken;
                m_currentToken = currentToken;
                handler = m_onTokenActivated;
            }

            handler?.Invoke(this, currentToken, previousToken);
            previousToken?.Dispose();
        }

        private FaultSnapshot CaptureFault()
        {
            return new FaultSnapshot(m_faultMode, m_faultStatus, m_hangFor, m_openBarrier);
        }

        private ServiceResultException CreateFaultException(ServiceResult? status, string message)
        {
            _ = this;
            return status != null
                ? new ServiceResultException(status)
                : new ServiceResultException(StatusCodes.BadCommunicationError, message);
        }

        private IServiceResponse CreateResponse(IServiceRequest request)
        {
            return request switch
            {
                CreateSessionRequest => CreateSessionResponse(),
                ActivateSessionRequest => CreateActivateSessionResponse(),
                ReadRequest readRequest => CreateReadResponse(readRequest),
                CloseSessionRequest => new CloseSessionResponse { ResponseHeader = CreateGoodHeader() },
                _ => throw new ServiceResultException(
                    StatusCodes.BadServiceUnsupported,
                    "Unexpected fake transport request type.")
            };
        }

        private CreateSessionResponse CreateSessionResponse()
        {
            EndpointDescription description;
            int sessionNumber;
            lock (m_lock)
            {
                description = m_endpointDescription ?? new EndpointDescription();
                m_sessionCounter++;
                sessionNumber = m_sessionCounter;
            }

            string suffix = sessionNumber.ToString(CultureInfo.InvariantCulture);
            return new CreateSessionResponse
            {
                ResponseHeader = CreateGoodHeader(),
                SessionId = new NodeId($"session-{suffix}", 1),
                AuthenticationToken = new NodeId($"token-{suffix}", 1),
                RevisedSessionTimeout = 60000,
                ServerNonce = ByteString.Empty,
                ServerCertificate = ByteString.Empty,
                ServerSignature = new SignatureData(),
                ServerEndpoints = [description],
                MaxRequestMessageSize = 1_048_576
            };
        }

        private readonly Lock m_lock = new();
        private readonly ITelemetryContext? m_telemetry;
        private readonly uint m_channelId = (uint)Interlocked.Increment(ref s_nextChannelId);
        private TimeSpan m_openDelay;
        private int m_openCount;
        private int m_closeCount;
        private int m_disposeCount;
        private int m_reconnectCount;
        private int m_requestCount;
        private int m_operationTimeout;
        private int m_sessionCounter;
        private uint m_tokenId;
        private bool m_isOpen;
        private string? m_clientCertificateThumbprint;
        private ChaosBarrier? m_reconnectBarrier;
        private FaultMode m_faultMode = FaultMode.None;
        private ServiceResult? m_faultStatus;
        private TimeSpan m_hangFor = Timeout.InfiniteTimeSpan;
        private TransportChannelFeatures m_supportedFeatures = TransportChannelFeatures.Reconnect;
        private ChaosBarrier? m_openBarrier;
        private EndpointDescription? m_endpointDescription;
        private EndpointConfiguration? m_endpointConfiguration;
        private IServiceMessageContext m_messageContext;
        private ChannelToken? m_currentToken;
        private IUaSCByteTransport? m_socket;
        private ChannelTokenActivatedEventHandler? m_onTokenActivated;
        private static int s_nextChannelId;

        private readonly record struct FaultSnapshot(
            FaultMode Mode,
            ServiceResult? Status,
            TimeSpan HangFor,
            ChaosBarrier? OpenBarrier);
    }

    internal enum FakeTransportFaultKind
    {
        None,
        OpenFails,
        OpenHangs,
        RequestFails,
        RequestHangs,
        DropOnNthRequest
    }
}
