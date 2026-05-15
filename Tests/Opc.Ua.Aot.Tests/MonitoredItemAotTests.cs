/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua.Client;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests for monitored item operations.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class MonitoredItemAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task CreateMonitoredItemWithFilterAsync()
        {
            using var subscription = new Subscription(fixture.Session.DefaultSubscription)
            {
                DisplayName = "AotFilterItem",
                PublishingEnabled = true,
                PublishingInterval = 1000,
                KeepAliveCount = 5
            };

            fixture.Session.AddSubscription(subscription);
            await subscription.CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);

            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 1.0
            };

            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                AttributeId = Attributes.Value,
                DisplayName = "FilteredTime",
                Filter = filter,
                SamplingInterval = 500,
                QueueSize = 10
            };

            subscription.AddItem(item);
            await subscription.ApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(subscription.Created).IsTrue();
            await Assert.That((int)subscription.MonitoredItemCount)
                .IsEqualTo(1);

            await fixture.Session.RemoveSubscriptionAsync(subscription)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ModifyMonitoredItemAsync()
        {
            using var subscription = new Subscription(fixture.Session.DefaultSubscription)
            {
                DisplayName = "AotModifyItem",
                PublishingEnabled = true,
                PublishingInterval = 1000,
                KeepAliveCount = 5
            };

            fixture.Session.AddSubscription(subscription);
            await subscription.CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);

            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                AttributeId = Attributes.Value,
                DisplayName = "ModifiableTime",
                SamplingInterval = 1000
            };

            subscription.AddItem(item);
            await subscription.ApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That((int)subscription.MonitoredItemCount)
                .IsEqualTo(1);

            // Modify the sampling interval
            item.SamplingInterval = 500;
            await subscription.ApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(item.SamplingInterval).IsEqualTo(500);

            await fixture.Session.RemoveSubscriptionAsync(subscription)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task SetMonitoringModeAsync()
        {
            using var subscription = new Subscription(fixture.Session.DefaultSubscription)
            {
                DisplayName = "AotMonitoringMode",
                PublishingEnabled = true,
                PublishingInterval = 1000,
                KeepAliveCount = 5
            };

            fixture.Session.AddSubscription(subscription);
            await subscription.CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);

            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                AttributeId = Attributes.Value,
                DisplayName = "MonitoringModeTime"
            };

            subscription.AddItem(item);
            await subscription.ApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            // Set to Disabled
            ArrayOf<MonitoredItem> items = [item];
            await subscription.SetMonitoringModeAsync(
                MonitoringMode.Disabled, items,
                CancellationToken.None).ConfigureAwait(false);

            // Set back to Reporting
            await subscription.SetMonitoringModeAsync(
                MonitoringMode.Reporting, items,
                CancellationToken.None).ConfigureAwait(false);

            await Assert.That(subscription.Created).IsTrue();

            await fixture.Session.RemoveSubscriptionAsync(subscription)
                .ConfigureAwait(false);
        }
    }
}
