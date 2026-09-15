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
using System.Text.Json;
using System.Text.Json.Serialization;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using UaLens.Plugins.Continuity;

namespace UaLens.Diagnostics;

/// <summary>
/// Allowlist export: endpoints, identity references, node names/ids, raw values,
/// messages, runtime snapshots and server session tokens never reach the DTO.
/// Redaction is structural rather than a best-effort secret-matching expression.
/// </summary>
internal static class DiagnosticEvidenceExport
{
    public static ByteString Create(
        ISession? session,
        PublishLogObserver? publishLog,
        ContinuityConfiguration? configuration = null,
        ContinuityTimelineSnapshot? timeline = null)
    {
        var dto = new DiagnosticEvidenceDto
        {
            CapturedUtc = DateTime.UtcNow,
            ClientSessionCorrelation = session is null ? null : DiagnosticCorrelation.Session(session),
            Connected = session?.Connected,
            DisplayDrops = publishLog?.DroppedDisplayEntries,
            DisplayEvictions = publishLog?.EvictedDisplayEntries,
            Counters = timeline?.Counters
        };
        if (configuration is not null)
        {
            dto.Configuration = new DiagnosticConfigurationDto
            {
                Scenario = configuration.Scenario.ToString(),
                TargetCount = configuration.Targets.Count,
                RequestedPublishMs = configuration.PublishingIntervalMs,
                RequestedSampleMs = configuration.SamplingIntervalMs,
                QueueSize = configuration.QueueSize,
                LifetimeCount = configuration.LifetimeCount,
                KeepAliveCount = configuration.KeepAliveCount,
                ItemsPerPartition = configuration.ItemsPerPartition,
                DurableRequested = configuration.Durable,
                DurableLifetimeHours = configuration.DurableLifetimeHours,
                MonotonicSampleContract = configuration.MonotonicSample
            };
        }
        if (session?.Connected == true)
        {
            dto.ServerSessionIdKnown = !session.SessionId.IsNull;
            dto.EffectiveMaxNodesPerRead = session.OperationLimits.MaxNodesPerRead;
            dto.EffectiveMaxNodesPerWrite = session.OperationLimits.MaxNodesPerWrite;
            dto.EffectiveMaxMonitoredItemsPerCall = session.OperationLimits.MaxMonitoredItemsPerCall;
            dto.OutstandingRequests = session.OutstandingRequestCount;
            if (session.TryGetSubscriptionManager(out ISubscriptionManager? manager) && manager is not null)
            {
                dto.LogicalSubscriptions = manager.Count;
                dto.PublishWorkers = manager.PublishWorkerCount;
                dto.GoodPublishRequests = manager.GoodPublishRequestCount;
                dto.BadPublishRequests = manager.BadPublishRequestCount;
                dto.MissingMessageSlots = manager.MissingMessageCount;
                dto.RepublishAttempts = manager.RepublishMessageCount;
            }
        }
        if (timeline is not null)
        {
            int start = Math.Max(0, timeline.Entries.Count - MaxTimelineEntries);
            dto.TimelineEntriesOmitted = start;
            for (int i = start; i < timeline.Entries.Count; i++)
            {
                ContinuityEvidence entry = timeline.Entries[i];
                dto.Timeline.Add(new DiagnosticTimelineEntryDto
                {
                    Ordinal = entry.Ordinal,
                    ElapsedMs = entry.ElapsedMs,
                    ReceivedUtc = entry.ReceivedUtc,
                    Kind = entry.Kind.ToString(),
                    LifecycleState = entry.LifecycleState?.ToString(),
                    PublishFlags = entry.PublishFlags.ToString(),
                    ClientSessionCorrelation = entry.ClientSessionId == Guid.Empty ? null : entry.ClientSessionId,
                    ClientSubscriptionCorrelation =
                        entry.ClientSubscriptionId == 0 ? null : entry.ClientSubscriptionId,
                    PartitionServerId = entry.PartitionServerId == 0 ? null : entry.PartitionServerId,
                    SequenceNumber = entry.SequenceNumber,
                    PublishUtc = entry.PublishUtc,
                    SourceUtc = entry.SourceUtc,
                    StatusCode = entry.StatusCode,
                    NumericSamplePresent = entry.NumericSample.HasValue
                });
            }
        }
        if (publishLog is not null)
        {
            ArrayOf<PublishLogEntry> entries = publishLog.CaptureSnapshot();
            int start = Math.Max(0, entries.Count - MaxPublishEntries);
            dto.PublishEntriesOmitted = start;
            for (int i = start; i < entries.Count; i++)
            {
                PublishLogEntry entry = entries[i];
                dto.Publishes.Add(new DiagnosticPublishEntryDto
                {
                    ReceivedUtc = entry.ReceivedAtLocal.ToUniversalTime(),
                    PublishUtc = entry.PublishTimeUtc == DateTime.MinValue ? null : entry.PublishTimeUtc,
                    ClientSessionCorrelation = entry.ClientSessionId == Guid.Empty ? null : entry.ClientSessionId,
                    ClientSubscriptionCorrelation =
                        entry.ClientSubscriptionId == 0 ? null : entry.ClientSubscriptionId,
                    PartitionServerId = entry.SubscriptionId == 0 ? null : entry.SubscriptionId,
                    SequenceNumber = entry.SequenceNumber == 0 ? null : entry.SequenceNumber,
                    NotificationCount = entry.NotifCount,
                    Kind = entry.Kind.ToString()
                });
            }
        }
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            dto, DiagnosticEvidenceJsonContext.Default.DiagnosticEvidenceDto);
        if (bytes.Length > MaxBytes)
        {
            throw new InvalidOperationException("The diagnostic export exceeded its fixed size bound.");
        }
        return ByteString.From(bytes);
    }

    public const int MaxTimelineEntries = 200;
    public const int MaxPublishEntries = 200;
    public const int MaxBytes = 512 * 1024;
}

internal sealed class DiagnosticEvidenceDto
{
    public int Version { get; set; } = 1;

    public DateTime CapturedUtc { get; set; }

    public string Redaction { get; set; } =
        "No endpoints, identities, credential references, node ids/names, raw values, " +
        "free-text logs or runtime snapshots.";

    public string Interpretation { get; set; } =
        "Source, server publish, client callback and UI clocks differ. No synchronized-clock, hard-real-time or " +
        "zero-loss claim. Null counters are unavailable, not zero. Zero effective limits mean unspecified. " +
        "Republish counts are attempts, not successful recoveries. Callback receipt is metadata capture while " +
        "processing the callback, not a network packet timestamp. Packet capture is unconfigured.";

    public string SessionCounterScope { get; set; } =
        "Primary workspace session; timeline rows retain separate client correlations for auxiliary lab sessions.";

    public Guid? ClientSessionCorrelation { get; set; }

    public bool? Connected { get; set; }

    public bool? ServerSessionIdKnown { get; set; }

    public long? DisplayDrops { get; set; }

    public long? DisplayEvictions { get; set; }

    public int? OutstandingRequests { get; set; }

    public uint? EffectiveMaxNodesPerRead { get; set; }

    public uint? EffectiveMaxNodesPerWrite { get; set; }

    public uint? EffectiveMaxMonitoredItemsPerCall { get; set; }

    public int? LogicalSubscriptions { get; set; }

    public int? PublishWorkers { get; set; }

    public int? GoodPublishRequests { get; set; }

    public int? BadPublishRequests { get; set; }

    public long? MissingMessageSlots { get; set; }

    public long? RepublishAttempts { get; set; }

    public int TimelineEntriesOmitted { get; set; }

    public int PublishEntriesOmitted { get; set; }

    public DiagnosticConfigurationDto? Configuration { get; set; }

    public ContinuityCounters? Counters { get; set; }

    public List<DiagnosticTimelineEntryDto> Timeline { get; set; } = new();

    public List<DiagnosticPublishEntryDto> Publishes { get; set; } = new();
}

internal sealed class DiagnosticConfigurationDto
{
    public string Scenario { get; set; } = string.Empty;

    public int TargetCount { get; set; }

    public double RequestedPublishMs { get; set; }

    public double RequestedSampleMs { get; set; }

    public uint QueueSize { get; set; }

    public uint RequestedMaxNotificationsPerPublish { get; set; } = ContinuityState.MaxNotificationsPerPublish;

    public uint KeepAliveCount { get; set; }

    public uint LifetimeCount { get; set; }

    public uint ItemsPerPartition { get; set; }

    public bool DurableRequested { get; set; }

    public int DurableLifetimeHours { get; set; }

    public bool MonotonicSampleContract { get; set; }
}

internal sealed class DiagnosticTimelineEntryDto
{
    public long Ordinal { get; set; }

    public double ElapsedMs { get; set; }

    public DateTime ReceivedUtc { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string? LifecycleState { get; set; }

    public string PublishFlags { get; set; } = string.Empty;

    public Guid? ClientSessionCorrelation { get; set; }

    public long? ClientSubscriptionCorrelation { get; set; }

    public uint? PartitionServerId { get; set; }

    public uint? SequenceNumber { get; set; }

    public DateTime? PublishUtc { get; set; }

    public DateTime? SourceUtc { get; set; }

    public uint? StatusCode { get; set; }

    public bool NumericSamplePresent { get; set; }
}

internal sealed class DiagnosticPublishEntryDto
{
    public DateTime ReceivedUtc { get; set; }

    public DateTime? PublishUtc { get; set; }

    public Guid? ClientSessionCorrelation { get; set; }

    public long? ClientSubscriptionCorrelation { get; set; }

    public uint? PartitionServerId { get; set; }

    public uint? SequenceNumber { get; set; }

    public int NotificationCount { get; set; }

    public string Kind { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(DiagnosticEvidenceDto))]
internal sealed partial class DiagnosticEvidenceJsonContext : JsonSerializerContext;
