/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.SubscriptionBench;
using UaLens.Subscriptions;
using UaLens.Tests.Desktop;
using UaLens.Tests.Observe;
using UaLens.ViewModels;
using UaLens.Views;
using UaLens.Workspace;

namespace UaLens.Tests.Workspace;

public sealed partial class PluginDocumentLifecycleTests
{

    private static MonitoredItemConfig Item(int id, string name)
    {
        return new MonitoredItemConfig
        {
            Id = id,
            NodeId = new NodeId(name, 2),
            DisplayName = name,
            QueueSize = 13,
            SamplingInterval = TimeSpan.FromMilliseconds(25),
            DiscardOldest = false
        };
    }
}
