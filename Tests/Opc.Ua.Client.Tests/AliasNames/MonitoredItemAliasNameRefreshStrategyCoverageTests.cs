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
