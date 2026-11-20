// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Client.Subscriptions.Fakes
{
    /// <summary>
    /// Hand-rolled fake for <see cref="ISubscriptionContext"/>. Holds
    /// references to the public service-set interfaces the subscription
    /// uses (which the tests still mock with Moq because they are public
    /// source-generated interfaces). Replaces
    /// <c>Mock&lt;ISubscriptionContext&gt;</c>.
    /// </summary>
    internal sealed class FakeSubscriptionContext : ISubscriptionContext
    {
        public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromSeconds(60);

        public ISubscriptionServiceSetClientMethods SubscriptionServiceSet { get; set; }
            = null!;

        public IMonitoredItemServiceSetClientMethods MonitoredItemServiceSet { get; set; }
            = null!;

        public IMethodServiceSetClientMethods MethodServiceSet { get; set; }
            = null!;
    }
}
