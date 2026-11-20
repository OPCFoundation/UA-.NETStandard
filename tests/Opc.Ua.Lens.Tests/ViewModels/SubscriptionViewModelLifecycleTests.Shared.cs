// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Subscriptions;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Tests.ViewModels;

public sealed partial class SubscriptionViewModelLifecycleTests
{

    private static MonitoredItemConfig Item(int id, string name)
    {
        return new MonitoredItemConfig
        {
            Id = id,
            DisplayName = name,
            NodeId = new NodeId(name, 2),
            SamplingInterval = TimeSpan.FromMilliseconds(100),
            QueueSize = 3
        };
    }

    private static Task<NotificationEvent> ReadAsync(ChannelReader<NotificationEvent> reader)
    {
        return reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static async Task FlushAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

}
