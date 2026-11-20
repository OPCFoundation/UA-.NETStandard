// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using UaLens.Plugins.Continuity;
using UaLens.Telemetry;
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;
using SubscriptionState = Opc.Ua.Client.Subscriptions.SubscriptionState;

namespace UaLens.Tests.Diagnose;

internal sealed class ContinuityStackTestSubscription
{
    public ContinuityStackTestSubscription(ContinuityStackTestContext owner, uint serverId)
    {
        m_owner = owner;
        Subscription.SetupGet(value => value.Created).Returns(() => !Disposed);
        Subscription.SetupGet(value => value.PartitionIds).Returns(new[] { serverId });
        Subscription.SetupGet(value => value.PartitionCount).Returns(1);
        Subscription.SetupGet(value => value.MonitoredItems).Returns(m_items.Object);
        m_items.SetupGet(value => value.Items).Returns(() => Items);
        m_items.SetupGet(value => value.Count).Returns(() => (uint)Items.Count);
        m_items.Setup(value => value.TryAdd(It.IsAny<string>(), It.IsAny<IOptionsMonitor<MonitoredItemOptions>>(),
            out It.Ref<IMonitoredItem?>.IsAny)).Returns(new AddItemCallback(AddItem));
        Subscription.Setup(value => value.SetAsDurableAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns((TimeSpan _, CancellationToken ct) =>
            {
                ct.ThrowIfCancellationRequested();
                owner.Trace.Add("Durable");
                if (owner.DenyDurability)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                DurabilityBeforeItems = Items.Count == 0;
                return ValueTask.FromResult(TimeSpan.FromHours(2));
            });
        Subscription.Setup(value => value.RecreateAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) =>
            {
                ct.ThrowIfCancellationRequested();
                owner.Trace.Add("Recreate");
                return ValueTask.CompletedTask;
            });
        Subscription.Setup(value => value.DisposeAsync()).Returns(() =>
        {
            Disposed = true;
            owner.Trace.Add("Dispose");
            return ValueTask.CompletedTask;
        });
    }

    public Mock<IPartitionedSubscription> Subscription { get; } = new();

    public List<IMonitoredItem> Items { get; } = new();

    public bool Disposed { get; private set; }

    public bool DurabilityBeforeItems { get; private set; }

    public IMonitoredItem AddItem(string name)
    {
        var item = new Mock<IMonitoredItem>();
        item.SetupGet(value => value.Name).Returns(name);
        item.SetupGet(value => value.Created).Returns(true);
        item.SetupGet(value => value.Error).Returns(ServiceResult.Good);
        item.SetupGet(value => value.CurrentQueueSize).Returns(100);
        item.SetupGet(value => value.CurrentSamplingInterval).Returns(TimeSpan.FromMilliseconds(250));
        Items.Add(item.Object);
        m_owner.Trace.Add("Item");
        return item.Object;
    }

    private bool AddItem(string name, IOptionsMonitor<MonitoredItemOptions> options, out IMonitoredItem? item)
    {
        item = AddItem(name);
        return true;
    }

    private delegate bool AddItemCallback(
        string name, IOptionsMonitor<MonitoredItemOptions> options, out IMonitoredItem? item);

    private readonly ContinuityStackTestContext m_owner;
    private readonly Mock<IMonitoredItemCollection> m_items = new();
}
