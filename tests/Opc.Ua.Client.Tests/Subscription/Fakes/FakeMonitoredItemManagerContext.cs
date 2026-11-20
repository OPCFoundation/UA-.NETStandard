// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.Options;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions.Fakes
{
    /// <summary>
    /// Hand-rolled fake for <see cref="IMonitoredItemManagerContext"/>.
    /// Replaces <c>Mock&lt;IMonitoredItemManagerContext&gt;</c>.
    /// </summary>
    internal sealed class FakeMonitoredItemManagerContext : IMonitoredItemManagerContext
    {
        public uint Id { get; set; }

        public IMonitoredItemServiceSetClientMethods MonitoredItemServiceSet { get; set; }
            = null!;

        public IMethodServiceSetClientMethods MethodServiceSet { get; set; }
            = null!;

        /// <summary>
        /// Required factory for <see cref="CreateMonitoredItem"/>. Tests must
        /// assign this before invoking the manager.
        /// </summary>
        public Func<string, IOptionsMonitor<MonitoredItems.MonitoredItemOptions>,
            IMonitoredItemContext, MonitoredItems.MonitoredItem> CreateMonitoredItemFactory
        { get; set; }
            = (_, _, _) => throw new InvalidOperationException(
                "CreateMonitoredItemFactory not set on FakeMonitoredItemManagerContext.");

        /// <summary>
        /// Number of times <see cref="Update"/> was invoked.
        /// </summary>
        public int UpdateCalls { get; private set; }

        /// <summary>
        /// Number of times <see cref="RequestRecreate"/> was invoked.
        /// </summary>
        public int RequestRecreateCalls { get; private set; }

        public MonitoredItems.MonitoredItem CreateMonitoredItem(string name,
            IOptionsMonitor<MonitoredItems.MonitoredItemOptions> options,
            IMonitoredItemContext context)
        {
            return CreateMonitoredItemFactory(name, options, context);
        }

        public void Update()
        {
            UpdateCalls++;
        }

        public void RequestRecreate()
        {
            RequestRecreateCalls++;
        }
    }
}
