/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace ConsoleDataChannelStreaming
{
    /// <summary>
    /// Machine-readable observations emitted only after a finite real-socket run and successful cleanup.
    /// </summary>
    internal sealed record SampleResult
    {
        /// <summary>
        /// Gets the actual selected carrier.
        /// </summary>
        public string Carrier { get; init; } = string.Empty;
        /// <summary>
        /// Gets the configured or Session authorization route.
        /// </summary>
        public string Authorization { get; init; } = string.Empty;
        /// <summary>
        /// Gets the application's payload sender.
        /// </summary>
        public string Publisher { get; init; } = string.Empty;
        /// <summary>
        /// Gets whether that sender also initiated the transport.
        /// </summary>
        public bool PublisherIsTransportInitiator { get; init; }
        /// <summary>
        /// Gets the actual sender's opener role.
        /// </summary>
        public bool SourceIsOpener { get; init; }
        /// <summary>
        /// Gets the authenticated association identity.
        /// </summary>
        public Guid ContextId { get; init; }
        /// <summary>
        /// Gets the flow identifier, not a payload identifier.
        /// </summary>
        public uint ChannelId { get; init; }
        /// <summary>
        /// Gets the actual bound TCP or UDP port.
        /// </summary>
        public int Port { get; init; }
        /// <summary>
        /// Gets received complete messages, excluding any control records.
        /// </summary>
        public int Messages { get; init; }
        /// <summary>
        /// Gets received application payload bytes.
        /// </summary>
        public long Bytes { get; init; }
        /// <summary>
        /// Gets the digest of complete successfully sent messages.
        /// </summary>
        public string SourceSha256 { get; init; } = string.Empty;
        /// <summary>
        /// Gets the digest of complete received messages.
        /// </summary>
        public string ReceivedSha256 { get; init; } = string.Empty;
        /// <summary>
        /// Gets the actual final committed revision.
        /// </summary>
        public uint Revision { get; init; }
        /// <summary>
        /// Gets the data connection's UASC identifier, zero for native-only carriers.
        /// </summary>
        public uint SecureChannelId { get; init; }
        /// <summary>
        /// Gets the ordinary Service connection's UASC identifier, zero when absent.
        /// </summary>
        public uint ServiceSecureChannelId { get; init; }
        /// <summary>
        /// Gets reference identity evidence of one shared physical transport.
        /// </summary>
        public bool SameServiceTransport { get; init; }
        /// <summary>
        /// Gets the actual server Session creation count.
        /// </summary>
        public int Sessions { get; init; }
        /// <summary>
        /// Gets whether the optional manager model was actually installed.
        /// </summary>
        public bool ModelEnabled { get; init; }
        /// <summary>
        /// Gets server-recorded ordinary Read requests.
        /// </summary>
        public int Reads { get; init; }
        /// <summary>
        /// Gets server-recorded ordinary Publish requests.
        /// </summary>
        public int Publishes { get; init; }
        /// <summary>
        /// Gets server-recorded generated-proxy control Calls.
        /// </summary>
        public int Calls { get; init; }
        /// <summary>
        /// Gets Read responses received within the measured payload interval.
        /// </summary>
        public int ReadResponsesDuringStreaming { get; init; }
        /// <summary>
        /// Gets Publish responses received within the measured payload interval.
        /// </summary>
        public int PublishResponsesDuringStreaming { get; init; }
        /// <summary>
        /// Gets decoded notifications received within the measured payload interval.
        /// </summary>
        public int NotificationsDuringStreaming { get; init; }
        /// <summary>
        /// Gets the server-revised publishing interval.
        /// </summary>
        public double RevisedPublishingInterval { get; init; }
        /// <summary>
        /// Gets first-send to observed directional-EOF elapsed time.
        /// </summary>
        public double ElapsedMilliseconds { get; init; }
        /// <summary>
        /// Gets measured received payload throughput, not enqueue speed or a conformance promise.
        /// </summary>
        public double MegabitsPerSecond { get; init; }
        /// <summary>
        /// Gets whether all flow budgets and native ownership counters returned to zero after disposal.
        /// </summary>
        public bool ResourcesReleased { get; init; }
        /// <summary>
        /// Gets the explicitly requested WAV path, empty for streaming or playback.
        /// </summary>
        public string WaveFile { get; init; } = string.Empty;
    }

    /// <summary>
    /// Uses source-generated serialization rather than reflection-based payload serialization.
    /// </summary>
    [JsonSerializable(typeof(SampleResult))]
    internal sealed partial class SampleResultJsonContext : JsonSerializerContext;

    /// <summary>
    /// Borrows the host's logging factory for existing library telemetry.
    /// </summary>
    internal sealed class SampleTelemetry(ILoggerFactory loggerFactory) : TelemetryContextBase(loggerFactory);
}
