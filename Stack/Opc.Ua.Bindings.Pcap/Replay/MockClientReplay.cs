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
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.Pcap.Capture;
using Opc.Ua.Bindings.Pcap.Dissection;
using Opc.Ua.Bindings.Pcap.DependencyInjection;
using Opc.Ua.Bindings.Pcap.Frame;
using Opc.Ua.Bindings.Pcap.KeyLog;
using Opc.Ua.Client;

namespace Opc.Ua.Bindings.Pcap.Replay
{
    /// <summary>
    /// Diagnostic helper that replays selected captured service requests
    /// against a live target. MockClientReplay is not a complete replay; it
    /// supports replay of Read/Browse/Call requests against a live target using
    /// anonymous users and SecurityPolicy None. For full fidelity use a
    /// captured packet replay tool.
    /// </summary>
    public sealed class MockClientReplay : IAsyncDisposable
    {
        /// <summary>
        /// Constructs a mock-client replay over an existing capture source.
        /// </summary>
        public MockClientReplay(
            ICaptureSource source,
            string targetEndpointUrl,
            ILoggerFactory? loggerFactory = null)
            : this(source, targetEndpointUrl, new PcapOptions(), loggerFactory, validateEndpoint: false)
        {
        }

        /// <summary>
        /// Constructs a mock-client replay over an existing capture source.
        /// </summary>
        public MockClientReplay(
            ICaptureSource source,
            string targetEndpointUrl,
            PcapOptions options,
            ILoggerFactory? loggerFactory = null)
            : this(source, targetEndpointUrl, options, loggerFactory, validateEndpoint: true)
        {
        }

        private MockClientReplay(
            ICaptureSource source,
            string targetEndpointUrl,
            PcapOptions options,
            ILoggerFactory? loggerFactory,
            bool validateEndpoint)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetEndpointUrl);
            ArgumentNullException.ThrowIfNull(options);
            if (validateEndpoint)
            {
                ValidateReplayEndpoint(options, targetEndpointUrl);
            }

            m_source = source;
            m_targetEndpointUrl = targetEndpointUrl;
            m_options = options;
            m_loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            m_logger = m_loggerFactory.CreateLogger<MockClientReplay>();
            m_messageContext = new ServiceMessageContext(
                NoopTelemetryContext.Instance,
                EncodeableFactory.Create());
        }

        /// <summary>
        /// Replay speed multiplier. Values greater than one replay faster than
        /// real time; values less than one replay slower.
        /// </summary>
        public double Speed { get; set; } = 1.0;

        /// <summary>
        /// Runs the replay and returns summary statistics.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when <see cref="Speed"/> is not a finite positive number.
        /// </exception>
        /// <exception cref="PcapDiagnosticsException"></exception>
        public async ValueTask<MockReplayResult> RunAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ValidateSpeed(Speed);

            ValidateReplayEndpoint(m_options, m_targetEndpointUrl);

            var stopwatch = Stopwatch.StartNew();
            IReadOnlyList<DecodedServiceCall> calls = await DecodeServiceCallsAsync(ct).ConfigureAwait(false);
            List<CapturedRequest> requests = await DecodeReplayRequestsAsync(ct).ConfigureAwait(false);
            Dictionary<string, RequestTypeStatsBuilder> typeStats = BuildTypeStats(calls);

            int attempted = 0;
            int succeeded = 0;
            int failed = 0;

            ISession session = await CreateSessionAsync(ct).ConfigureAwait(false);
            try
            {
                DateTimeOffset? previousTimestamp = null;
                foreach (CapturedRequest captured in requests)
                {
                    ct.ThrowIfCancellationRequested();
                    if (captured.Request is not ReadRequest and not BrowseRequest and not CallRequest)
                    {
                        m_logger.LogWarning(
                            "Replay of {RequestType} not implemented; skipping.",
                            captured.Request.GetType().Name);
                        continue;
                    }

                    await DelayUntilReplayTimeAsync(previousTimestamp, captured.Timestamp, ct).ConfigureAwait(false);
                    previousTimestamp = captured.Timestamp;
                    attempted++;

                    try
                    {
                        await ReplayRequestAsync(session, captured.Request, ct).ConfigureAwait(false);
                        succeeded++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failed++;
                        m_logger.LogWarning(
                            ex,
                            "Replay of {RequestType} failed.",
                            captured.Request.GetType().Name);
                    }
                }
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }

            stopwatch.Stop();
            return new MockReplayResult
            {
                RequestsAttempted = attempted,
                RequestsSucceeded = succeeded,
                RequestsFailed = failed,
                TotalDuration = stopwatch.Elapsed,
                RequestTypes = CreateRequestTypeResults(typeStats)
            };
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await m_source.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        private async ValueTask<IReadOnlyList<DecodedServiceCall>> DecodeServiceCallsAsync(CancellationToken ct)
        {
            var reassembler = new ServiceCallReassembler(m_loggerFactory);
            await foreach (ChannelKeyMaterial material in m_source.ReadKeyMaterialAsync(ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                reassembler.LoadKeyMaterial(material);
            }

            IReadOnlyList<DecodedServiceCall> calls = await reassembler
                .ProcessAllAsync(m_source.ReadCapturedFramesAsync(null, ct), ct)
                .ConfigureAwait(false);
            return calls;
        }

        private async ValueTask<List<CapturedRequest>> DecodeReplayRequestsAsync(CancellationToken ct)
        {
            Dictionary<uint, OfflineSecureChannel> channels = await CreateChannelsAsync(ct).ConfigureAwait(false);
            var pending = new Dictionary<(uint channelId, uint requestId), PendingRequest>();
            var requests = new List<CapturedRequest>();

            await foreach (CaptureFrame frame in m_source.ReadCapturedFramesAsync(null, ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                if (frame.Direction != CaptureFrameDirection.ClientToServer ||
                    frame.Data.Length < TcpMessageLimits.SymmetricHeaderSize)
                {
                    continue;
                }

                uint messageType = BitConverter.ToUInt32(frame.Data.Span[..sizeof(uint)]);
                uint baseType = messageType & TcpMessageType.MessageTypeMask;
                if (baseType != TcpMessageType.Message && baseType != TcpMessageType.Close)
                {
                    continue;
                }

                uint channelId = BitConverter.ToUInt32(frame.Data.Span[8..12]);
                if (!channels.TryGetValue(channelId, out OfflineSecureChannel? channel))
                {
                    continue;
                }

                OfflineDecodedChunk decoded = channel.ReadChunk(frame.Data.Span, true);
                var key = (decoded.ChannelId, decoded.RequestId);
                if (!pending.TryGetValue(key, out PendingRequest? request))
                {
                    request = new PendingRequest(frame.Timestamp);
                    pending[key] = request;
                }

                request.Chunks.Add(decoded.Body.ToArray());
                if (decoded.IsFinal)
                {
                    IServiceRequest? decodedRequest = DecodeRequest(request.Chunks);
                    if (decodedRequest is not null)
                    {
                        requests.Add(new CapturedRequest(request.Timestamp, decodedRequest));
                    }

                    pending.Remove(key);
                }
            }

            foreach (OfflineSecureChannel channel in channels.Values)
            {
                channel.Dispose();
            }

            return requests;
        }

        private async ValueTask<Dictionary<uint, OfflineSecureChannel>> CreateChannelsAsync(CancellationToken ct)
        {
            var channels = new Dictionary<uint, OfflineSecureChannel>();
            await foreach (ChannelKeyMaterial material in m_source.ReadKeyMaterialAsync(ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                if (channels.TryGetValue(material.ChannelId, out OfflineSecureChannel? channel))
                {
                    channel.LoadKeyMaterial(material);
                    continue;
                }

                channels[material.ChannelId] = new OfflineSecureChannel(material, m_loggerFactory);
            }

            return channels;
        }

        private IServiceRequest? DecodeRequest(List<byte[]> chunks)
        {
            byte[] body = Concatenate(chunks);
            try
            {
                using var decoder = new BinaryDecoder(body, m_messageContext);
                ExpandedNodeId typeId = decoder.ReadExpandedNodeId(null);
                if (!m_messageContext.Factory.TryGetEncodeableType(typeId, out IEncodeableType? encodeableType))
                {
                    return null;
                }

                IEncodeable message = encodeableType.CreateInstance();
                message.Decode(decoder);
                return message as IServiceRequest;
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Unable to decode captured request for mock-client replay.");
                return null;
            }
        }

        private async ValueTask<ISession> CreateSessionAsync(CancellationToken ct)
        {
            var configuration = new ApplicationConfiguration(NoopTelemetryContext.Instance)
            {
                ApplicationName = "OpcUaDiagnosticsPcapReplay",
                ApplicationType = ApplicationType.Client,
                ApplicationUri = "urn:localhost:OpcUaDiagnosticsPcapReplay",
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration(),
                TransportQuotas = new TransportQuotas()
            };

            var endpointDescription = new EndpointDescription(m_targetEndpointUrl)
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        TokenType = UserTokenType.Anonymous
                    }
                ]
            };
            var endpointConfiguration = EndpointConfiguration.Create(configuration);
            var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);
            var factory = new DefaultSessionFactory(NoopTelemetryContext.Instance);
            return await factory.CreateAsync(
                configuration,
                endpoint,
                updateBeforeConnect: false,
                checkDomain: false,
                sessionName: "OpcUaDiagnosticsPcapReplay",
                sessionTimeout: 60_000,
                identity: new UserIdentity(),
                preferredLocales: default,
                ct).ConfigureAwait(false);
        }

        private static async ValueTask ReplayRequestAsync(ISession session, IServiceRequest request, CancellationToken ct)
        {
            switch (request)
            {
                case ReadRequest read:
                    await session.ReadAsync(
                        null,
                        read.MaxAge,
                        read.TimestampsToReturn,
                        read.NodesToRead,
                        ct).ConfigureAwait(false);
                    break;
                case BrowseRequest browse:
                    await session.BrowseAsync(
                        null,
                        browse.View,
                        browse.RequestedMaxReferencesPerNode,
                        browse.NodesToBrowse,
                        ct).ConfigureAwait(false);
                    break;
                case CallRequest call:
                    await session.CallAsync(null, call.MethodsToCall, ct).ConfigureAwait(false);
                    break;
            }
        }

        private async ValueTask DelayUntilReplayTimeAsync(
            DateTimeOffset? previousTimestamp,
            DateTimeOffset currentTimestamp,
            CancellationToken ct)
        {
            if (!previousTimestamp.HasValue)
            {
                return;
            }

            TimeSpan delay = currentTimestamp - previousTimestamp.Value;
            if (delay <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromTicks((long)(delay.Ticks / Speed)), ct).ConfigureAwait(false);
        }

        private static Dictionary<string, RequestTypeStatsBuilder> BuildTypeStats(IReadOnlyList<DecodedServiceCall> calls)
        {
            var builders = new Dictionary<string, RequestTypeStatsBuilder>(StringComparer.Ordinal);
            foreach (DecodedServiceCall call in calls)
            {
                if (string.IsNullOrWhiteSpace(call.RequestName))
                {
                    continue;
                }

                if (!builders.TryGetValue(call.RequestName, out RequestTypeStatsBuilder? builder))
                {
                    builder = new RequestTypeStatsBuilder(call.RequestName);
                    builders[call.RequestName] = builder;
                }

                builder.Add(call.Latency);
            }

            return builders;
        }

        private static List<MockReplayRequestTypeResult> CreateRequestTypeResults(
            Dictionary<string, RequestTypeStatsBuilder> builders)
        {
            var results = new List<MockReplayRequestTypeResult>(builders.Count);
            foreach (RequestTypeStatsBuilder builder in builders.Values)
            {
                results.Add(builder.ToResult());
            }

            return results;
        }

        private static byte[] Concatenate(List<byte[]> chunks)
        {
            int length = 0;
            foreach (byte[] chunk in chunks)
            {
                length += chunk.Length;
            }

            byte[] result = new byte[length];
            int offset = 0;
            foreach (byte[] chunk in chunks)
            {
                chunk.AsSpan().CopyTo(result.AsSpan(offset));
                offset += chunk.Length;
            }

            return result;
        }

        private sealed class PendingRequest
        {
            public PendingRequest(DateTimeOffset timestamp)
            {
                Timestamp = timestamp;
            }

            public DateTimeOffset Timestamp { get; }

            public List<byte[]> Chunks { get; } = [];
        }

        private sealed class RequestTypeStatsBuilder
        {
            public RequestTypeStatsBuilder(string requestName)
            {
                RequestName = requestName;
            }

            public string RequestName { get; }

            public int Count { get; private set; }

            public TimeSpan? AverageLatency => LatencyCount == 0
                ? null
                : TimeSpan.FromTicks(LatencyTicks / LatencyCount);

            public void Add(TimeSpan? latency)
            {
                Count++;
                if (latency.HasValue)
                {
                    LatencyTicks += latency.Value.Ticks;
                    LatencyCount++;
                }
            }

            public MockReplayRequestTypeResult ToResult()
            {
                return new MockReplayRequestTypeResult
                {
                    RequestName = RequestName,
                    Count = Count,
                    AverageLatency = AverageLatency
                };
            }

            private long LatencyTicks { get; set; }

            private int LatencyCount { get; set; }
        }

        private static void ValidateReplayEndpoint(
            PcapOptions options,
            string targetEndpointUrl)
        {
            if (!options.AllowMockClientReplay)
            {
                throw new PcapDiagnosticsException(
                    "Mock-client replay is disabled. Set " +
                    "PcapOptions.AllowMockClientReplay = true to enable, " +
                    "and populate PcapOptions.AllowedReplayEndpoints with " +
                    "the hostname(s) you intend to target.");
            }

            if (options.AllowedReplayEndpoints.Count == 0)
            {
                throw new PcapDiagnosticsException(
                    "Mock-client replay is enabled but " +
                    "PcapOptions.AllowedReplayEndpoints is empty; no " +
                    "endpoint can be targeted. Add the intended host(s) " +
                    "to the allow-list.");
            }

            if (!Uri.TryCreate(targetEndpointUrl, UriKind.Absolute, out Uri? uri))
            {
                throw new PcapDiagnosticsException(
                    $"Replay targetEndpointUrl '{targetEndpointUrl}' is not " +
                    "a valid absolute URI.");
            }

            if (!IsAllowedReplayScheme(uri.Scheme))
            {
                throw new PcapDiagnosticsException(
                    $"Replay targetEndpointUrl scheme '{uri.Scheme}' is not " +
                    "permitted. Only opc.tcp, opc.tcps, and opc.https are allowed.");
            }

            string host = uri.Host;
            bool allowed = false;
            foreach (string entry in options.AllowedReplayEndpoints)
            {
                if (string.Equals(entry, host, StringComparison.OrdinalIgnoreCase))
                {
                    allowed = true;
                    break;
                }
            }

            if (!allowed)
            {
                throw new PcapDiagnosticsException(
                    $"Replay targetEndpointUrl host '{host}' is not in " +
                    "PcapOptions.AllowedReplayEndpoints.");
            }
        }

        private static bool IsAllowedReplayScheme(string scheme)
        {
            return scheme switch
            {
                Utils.UriSchemeOpcTcp => true,
                "opc.tcps" => true,
                Utils.UriSchemeOpcHttps => true,
                _ => false
            };
        }

        /// <summary>
        /// Validates that <paramref name="speed"/> is a finite positive
        /// real number. Rejects <c>NaN</c>, ±infinity, zero, and negatives —
        /// any of which would otherwise cause division-by-zero, infinite
        /// loops, or rate-calculation overflow downstream.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when the speed is not in the allowed range.
        /// </exception>
        private static void ValidateSpeed(double speed)
        {
            if (double.IsNaN(speed) || double.IsInfinity(speed) || speed <= 0d)
            {
                throw new ArgumentException(
                    $"Replay speed {speed} is not a finite positive number. " +
                    "Provide a value > 0 (e.g., 1.0 for real-time, 2.0 for 2× faster).",
                    nameof(speed));
            }
        }

        private readonly struct CapturedRequest
        {
            public CapturedRequest(DateTimeOffset timestamp, IServiceRequest request)
            {
                Timestamp = timestamp;
                Request = request;
            }

            public DateTimeOffset Timestamp { get; }

            public IServiceRequest Request { get; }
        }

        private sealed class NoopTelemetryContext : ITelemetryContext
        {
            public static NoopTelemetryContext Instance { get; } = new();

            public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;

            public ActivitySource ActivitySource => s_activitySource;

            public Meter CreateMeter()
            {
                return new Meter("Opc.Ua.Bindings.Pcap.MockClientReplay");
            }

            private NoopTelemetryContext()
            {
            }

            private static readonly ActivitySource s_activitySource = new("Opc.Ua.Bindings.Pcap.MockClientReplay");
        }

        private readonly ICaptureSource m_source;
        private readonly string m_targetEndpointUrl;
        private readonly PcapOptions m_options;
        private readonly ILoggerFactory m_loggerFactory;
        private readonly ILogger m_logger;
        private readonly ServiceMessageContext m_messageContext;
    }

    /// <summary>
    /// Summary of a mock-client replay run.
    /// </summary>
    public sealed class MockReplayResult
    {
        /// <summary>
        /// Number of supported requests attempted against the target server.
        /// </summary>
        public int RequestsAttempted { get; init; }

        /// <summary>
        /// Number of attempted requests that completed without exception.
        /// </summary>
        public int RequestsSucceeded { get; init; }

        /// <summary>
        /// Number of attempted requests that failed.
        /// </summary>
        public int RequestsFailed { get; init; }

        /// <summary>
        /// Wall-clock duration of the replay run.
        /// </summary>
        public TimeSpan TotalDuration { get; init; }

        /// <summary>
        /// Per-request-type counts and average captured latency.
        /// </summary>
        public IReadOnlyList<MockReplayRequestTypeResult> RequestTypes { get; init; } = [];
    }

    /// <summary>
    /// Per-request-type replay statistics.
    /// </summary>
    public sealed class MockReplayRequestTypeResult
    {
        /// <summary>
        /// Captured OPC UA request type name.
        /// </summary>
        public string RequestName { get; init; } = string.Empty;

        /// <summary>
        /// Number of completed captured service calls of this type.
        /// </summary>
        public int Count { get; init; }

        /// <summary>
        /// Average captured request/response latency for this type.
        /// </summary>
        public TimeSpan? AverageLatency { get; init; }
    }
}
