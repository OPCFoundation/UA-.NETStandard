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
using UaLens.Subscriptions;
using UaLens.Views;

namespace UaLens.Plugins.SubscriptionBench;

/// <summary>
/// One variable in the saved bench pool: its NodeId and a display label.
/// </summary>
internal readonly record struct BenchPoolNode(NodeId NodeId, string DisplayName);

/// <summary>
/// Validated, non-live bench configuration produced from a persisted snapshot.
/// It carries the intended slider sizes as inert values; applying it never starts
/// live scaling.
/// </summary>
internal sealed record BenchRestoredState(
    ArrayOf<BenchPoolNode> Pool,
    int SavedSubscriptions,
    int SavedItemsPerSubscription,
    SubscriptionConfig Subscription,
    MonitoredItemSettings ItemSettings,
    bool ShowEngineDetails);

/// <summary>
/// Serializable, versioned snapshot of the Subscription Bench's safe configuration:
/// the variable pool, the intended slider sizes, the shared subscription and item
/// settings, and display options. It deliberately excludes live server handles,
/// collected data history and any credentials, and it never captures the
/// session-wide publish pipeline (that belongs to the primary connection).
/// </summary>
internal sealed class SubscriptionBenchStateDto
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<BenchPoolNodeDto> Pool { get; set; } = new();
    public int SavedSubscriptions { get; set; }
    public int SavedItemsPerSubscription { get; set; }
    public BenchSubscriptionDto Subscription { get; set; } = new();
    public BenchItemDto Item { get; set; } = new();
    public bool ShowEngineDetails { get; set; }
}

internal sealed class BenchPoolNodeDto
{
    public string NodeId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

internal sealed class BenchSubscriptionDto
{
    public double PublishingIntervalMs { get; set; } = 1000;
    public uint KeepAliveCount { get; set; } = 10;
    public uint LifetimeCount { get; set; } = 1000;
    public uint MaxNotificationsPerPublish { get; set; }
    public byte Priority { get; set; }
    public bool PublishingEnabled { get; set; } = true;
}

internal sealed class BenchItemDto
{
    public double SamplingIntervalMs { get; set; }
    public uint QueueSize { get; set; } = 1;
    public bool DiscardOldest { get; set; } = true;
    public int MonitoringMode { get; set; } = (int)Opc.Ua.MonitoringMode.Reporting;
    public BenchFilterDto? Filter { get; set; }
}

internal sealed class BenchFilterDto
{
    public int Trigger { get; set; }
    public uint DeadbandType { get; set; }
    public double DeadbandValue { get; set; }
}

/// <summary>
/// Builds and validates <see cref="SubscriptionBenchStateDto"/> snapshots. Validation
/// surfaces unknown versions and invalid payloads as exceptions rather than silently
/// substituting defaults.
/// </summary>
internal static class SubscriptionBenchState
{
    private const double MaxIntervalMs = 3_600_000;

    public static SubscriptionBenchStateDto CreateDto(
        IReadOnlyList<BenchPoolNode> pool,
        int savedSubscriptions,
        int savedItemsPerSubscription,
        SubscriptionConfig subscription,
        MonitoredItemSettings itemSettings,
        bool showEngineDetails)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(itemSettings);
        var dto = new SubscriptionBenchStateDto
        {
            Version = SubscriptionBenchStateDto.CurrentVersion,
            SavedSubscriptions = Math.Max(0, savedSubscriptions),
            SavedItemsPerSubscription = Math.Max(0, savedItemsPerSubscription),
            Subscription = new BenchSubscriptionDto
            {
                PublishingIntervalMs = subscription.PublishingInterval.TotalMilliseconds,
                KeepAliveCount = subscription.KeepAliveCount,
                LifetimeCount = subscription.LifetimeCount,
                MaxNotificationsPerPublish = subscription.MaxNotificationsPerPublish,
                Priority = subscription.Priority,
                PublishingEnabled = subscription.PublishingEnabled
            },
            Item = new BenchItemDto
            {
                SamplingIntervalMs = itemSettings.SamplingInterval.TotalMilliseconds,
                QueueSize = itemSettings.QueueSize,
                DiscardOldest = itemSettings.DiscardOldest,
                MonitoringMode = (int)itemSettings.MonitoringMode,
                Filter = itemSettings.DataChangeFilter is { } filter
                    ? new BenchFilterDto
                    {
                        Trigger = (int)filter.Trigger,
                        DeadbandType = filter.DeadbandType,
                        DeadbandValue = filter.DeadbandValue
                    }
                    : null
            },
            ShowEngineDetails = showEngineDetails
        };
        foreach (BenchPoolNode node in pool)
        {
            dto.Pool.Add(new BenchPoolNodeDto
            {
                NodeId = node.NodeId.ToString() ?? string.Empty,
                DisplayName = node.DisplayName
            });
        }
        return dto;
    }

    public static BenchRestoredState Validate(SubscriptionBenchStateDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.Version != SubscriptionBenchStateDto.CurrentVersion)
        {
            throw new FormatException(
                $"Unsupported Subscription Bench state version '{dto.Version}'.");
        }
        if (dto.SavedSubscriptions < 0 || dto.SavedItemsPerSubscription < 0)
        {
            throw new FormatException("Saved bench sizes must not be negative.");
        }

        var pool = new List<BenchPoolNode>(dto.Pool?.Count ?? 0);
        foreach (BenchPoolNodeDto entry in dto.Pool ?? new List<BenchPoolNodeDto>())
        {
            if (string.IsNullOrWhiteSpace(entry.NodeId))
            {
                throw new FormatException("A saved pool entry is missing its NodeId.");
            }
            if (!NodeId.TryParse(entry.NodeId, out NodeId nodeId) || nodeId.IsNull)
            {
                throw new FormatException($"A saved pool entry has an invalid NodeId '{entry.NodeId}'.");
            }
            pool.Add(new BenchPoolNode(nodeId, entry.DisplayName ?? string.Empty));
        }

        BenchSubscriptionDto sub = dto.Subscription
            ?? throw new FormatException("The saved bench subscription settings are missing.");
        if (!double.IsFinite(sub.PublishingIntervalMs) || sub.PublishingIntervalMs < 0
            || sub.PublishingIntervalMs > MaxIntervalMs)
        {
            throw new FormatException("The saved publishing interval is out of range.");
        }

        BenchItemDto item = dto.Item
            ?? throw new FormatException("The saved bench item settings are missing.");
        if (!double.IsFinite(item.SamplingIntervalMs) || item.SamplingIntervalMs < -1
            || item.SamplingIntervalMs > MaxIntervalMs)
        {
            throw new FormatException("The saved sampling interval is out of range.");
        }
        if (!Enum.IsDefined((MonitoringMode)item.MonitoringMode))
        {
            throw new FormatException("The saved monitoring mode is invalid.");
        }
        DataChangeFilter? filter = null;
        if (item.Filter is { } f)
        {
            if (!Enum.IsDefined((DataChangeTrigger)f.Trigger)
                || f.DeadbandType > (uint)DeadbandType.Percent
                || !double.IsFinite(f.DeadbandValue) || f.DeadbandValue < 0
                || (f.DeadbandType == (uint)DeadbandType.Percent && f.DeadbandValue > 100))
            {
                throw new FormatException("The saved data-change filter is invalid.");
            }
            filter = new DataChangeFilter
            {
                Trigger = (DataChangeTrigger)f.Trigger,
                DeadbandType = f.DeadbandType,
                DeadbandValue = f.DeadbandValue
            };
        }

        var subscription = new SubscriptionConfig
        {
            PublishingInterval = TimeSpan.FromMilliseconds(sub.PublishingIntervalMs),
            KeepAliveCount = sub.KeepAliveCount,
            LifetimeCount = sub.LifetimeCount,
            MaxNotificationsPerPublish = sub.MaxNotificationsPerPublish,
            Priority = sub.Priority,
            PublishingEnabled = sub.PublishingEnabled
        };
        var itemSettings = new MonitoredItemSettings
        {
            SamplingInterval = TimeSpan.FromMilliseconds(item.SamplingIntervalMs),
            QueueSize = item.QueueSize,
            DiscardOldest = item.DiscardOldest,
            MonitoringMode = (MonitoringMode)item.MonitoringMode,
            DataChangeFilter = filter
        };
        return new BenchRestoredState(
            [.. pool],
            dto.SavedSubscriptions,
            dto.SavedItemsPerSubscription,
            subscription,
            itemSettings,
            dto.ShowEngineDetails);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(SubscriptionBenchStateDto))]
internal sealed partial class SubscriptionBenchStateJsonContext : JsonSerializerContext;
