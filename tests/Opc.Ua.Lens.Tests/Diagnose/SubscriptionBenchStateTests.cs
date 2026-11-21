// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.SubscriptionBench;
using UaLens.Subscriptions;
using UaLens.Views;

namespace UaLens.Tests.Diagnose;

[TestFixture]
public sealed class SubscriptionBenchStateTests
{
    private static BenchRestoredState RoundTrip(SubscriptionBenchStateDto dto)
    {
        JsonElement element = JsonSerializer.SerializeToElement(
            dto, SubscriptionBenchStateJsonContext.Default.SubscriptionBenchStateDto);
        SubscriptionBenchStateDto restoredDto = element.Deserialize(
            SubscriptionBenchStateJsonContext.Default.SubscriptionBenchStateDto)!;
        return SubscriptionBenchState.Validate(restoredDto);
    }

    [Test]
    public void RoundTripsPoolSlidersSettingsAndDisplayOptions()
    {
        var pool = new List<BenchPoolNode>
        {
            new(new NodeId(1u), "One"),
            new(new NodeId("s", 2), "Two")
        };
        var subscription = new SubscriptionConfig
        {
            PublishingInterval = TimeSpan.FromMilliseconds(500),
            KeepAliveCount = 7
        };
        var item = new MonitoredItemSettings
        {
            QueueSize = 4,
            DataChangeFilter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValueTimestamp,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 1.5
            }
        };

        SubscriptionBenchStateDto dto =
            SubscriptionBenchState.CreateDto(pool, 5, 100, subscription, item, showEngineDetails: true);
        BenchRestoredState restored = RoundTrip(dto);

        Assert.That(restored.Pool.Count, Is.EqualTo(2));
        Assert.That(restored.Pool[0].NodeId, Is.EqualTo(new NodeId(1u)));
        Assert.That(restored.Pool[1].DisplayName, Is.EqualTo("Two"));
        Assert.That(restored.SavedSubscriptions, Is.EqualTo(5));
        Assert.That(restored.SavedItemsPerSubscription, Is.EqualTo(100));
        Assert.That(restored.Subscription.PublishingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
        Assert.That(restored.Subscription.KeepAliveCount, Is.EqualTo(7u));
        Assert.That(restored.ItemSettings.QueueSize, Is.EqualTo(4u));
        Assert.That(restored.ItemSettings.DataChangeFilter, Is.Not.Null);
        Assert.That(restored.ItemSettings.DataChangeFilter!.DeadbandValue, Is.EqualTo(1.5));
        Assert.That(restored.ShowEngineDetails, Is.True);
    }

    [Test]
    public void UnknownVersionIsRejected()
    {
        var dto = new SubscriptionBenchStateDto { Version = 99 };
        Assert.That(() => SubscriptionBenchState.Validate(dto), Throws.InstanceOf<FormatException>());
    }

    [Test]
    public void InvalidNodeIdInThePoolIsRejected()
    {
        var dto = new SubscriptionBenchStateDto();
        dto.Pool.Add(new BenchPoolNodeDto { NodeId = "not-a-node-id", DisplayName = "x" });
        Assert.That(() => SubscriptionBenchState.Validate(dto), Throws.InstanceOf<FormatException>());
    }

    [Test]
    public void OutOfRangeSamplingIntervalIsRejected()
    {
        var dto = new SubscriptionBenchStateDto { Item = new BenchItemDto { SamplingIntervalMs = -5 } };
        Assert.That(() => SubscriptionBenchState.Validate(dto), Throws.InstanceOf<FormatException>());
    }

    [Test]
    public void InvalidMonitoringModeIsRejected()
    {
        var dto = new SubscriptionBenchStateDto { Item = new BenchItemDto { MonitoringMode = 999 } };
        Assert.That(() => SubscriptionBenchState.Validate(dto), Throws.InstanceOf<FormatException>());
    }

    [Test]
    public void NegativeSavedSizesAreRejected()
    {
        var dto = new SubscriptionBenchStateDto { SavedSubscriptions = -1 };
        Assert.That(() => SubscriptionBenchState.Validate(dto), Throws.InstanceOf<FormatException>());
    }
}
