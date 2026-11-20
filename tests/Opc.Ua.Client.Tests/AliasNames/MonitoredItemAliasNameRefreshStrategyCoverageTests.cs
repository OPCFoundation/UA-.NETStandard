// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.AliasNames;
using Opc.Ua.Client.AliasNames.Refresh;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.AliasNames
{
    [TestFixture]
    [Category("AliasNames")]
    public sealed class MonitoredItemAliasNameRefreshStrategyCoverageTests
    {
        [Test]
        public async Task StartAsyncNoOpsWhenLastChangeCannotBeResolvedAsync()
        {
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            harness.BrowsePathHandler = _ => new BrowsePathResult
            {
                StatusCode = StatusCodes.BadNoMatch,
                Targets = []
            };
            var client = new AliasNameClient(harness.Session, new NodeId(9001));
            var strategy = new MonitoredItemAliasNameRefreshStrategy();
            int invalidations = 0;

            await strategy.StartAsync(client, () => invalidations++, CancellationToken.None).ConfigureAwait(false);
            await strategy.DisposeAsync().ConfigureAwait(false);

            Assert.That(invalidations, Is.Zero);
        }

        [Test]
        public void OnNotificationInvalidatesOnlyWhenVersionChanges()
        {
            var strategy = new MonitoredItemAliasNameRefreshStrategy();
            int invalidations = 0;
            typeof(MonitoredItemAliasNameRefreshStrategy)
                .GetField("m_onInvalidate", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(strategy, (System.Action)(() => invalidations++));
            MethodInfo method = typeof(MonitoredItemAliasNameRefreshStrategy).GetMethod(
                "OnNotification",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var item = new MonitoredItem(NUnitTelemetryContext.Create());

            method.Invoke(strategy, [item, new MonitoredItemNotificationEventArgs(new EventFieldList())]);
            item.SaveValueInCache(new MonitoredItemNotification { Value = new DataValue(Variant.From("not-a-uint")) });
            method.Invoke(strategy, [item, new MonitoredItemNotificationEventArgs(item.LastValue!)]);
            item.SaveValueInCache(new MonitoredItemNotification { Value = new DataValue(Variant.From(10u)) });
            method.Invoke(strategy, [item, new MonitoredItemNotificationEventArgs(item.LastValue!)]);
            method.Invoke(strategy, [item, new MonitoredItemNotificationEventArgs(item.LastValue!)]);
            item.SaveValueInCache(new MonitoredItemNotification { Value = new DataValue(Variant.From(11u)) });
            method.Invoke(strategy, [item, new MonitoredItemNotificationEventArgs(item.LastValue!)]);

            Assert.That(invalidations, Is.EqualTo(2));
        }
    }
}
