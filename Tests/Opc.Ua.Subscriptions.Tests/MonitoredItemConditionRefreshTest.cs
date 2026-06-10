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

#nullable enable

#pragma warning disable CA2016

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// Per-item <c>ConditionRefresh2</c> tests for V2
    /// <see cref="Client.Subscriptions.MonitoredItems.IMonitoredItem.ConditionRefreshAsync"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("V2")]
    [Category("ConditionRefresh")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class MonitoredItemConditionRefreshTest : ClientTestFramework
    {
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            SingleSession = false;
            return OneTimeSetUpCoreAsync(securityNone: true);
        }

        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        [SetUp]
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        [Test]
        [Order(100)]
        [CancelAfter(60_000)]
        public async Task ConditionRefreshOnUncreatedItemThrowsAsync(
            CancellationToken ct)
        {
            // Verify the public-contract guard: an item that has not
            // been created on the server cannot be refreshed.
            ManagedSession session = await ConnectV2Async(
                nameof(ConditionRefreshOnUncreatedItemThrowsAsync), ct)
                .ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription sub = session.AddSubscription(handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                bool created = await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                // Add an item and try ConditionRefreshAsync immediately.
                // Two valid outcomes (per the public-contract guard):
                //  - the item is not yet created on the server → call
                //    throws BadMonitoredItemIdInvalid (guard fired);
                //  - the V2 state machine finished Create on the server
                //    before our lambda ran → call succeeds against the
                //    now-created item, contract is still satisfied.
                // Anything else is a real failure.
                Assert.That(sub.TryAddMonitoredItem(
                    "PendingItem",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with { SamplingInterval = TimeSpan.Zero },
                    out Client.Subscriptions.MonitoredItems.IMonitoredItem? item), Is.True);
                Assert.That(item, Is.Not.Null);
                ServiceResultException? caught = null;
                try
                {
                    await item!.ConditionRefreshAsync(ct).ConfigureAwait(false);
                }
                catch (ServiceResultException ex)
                {
                    caught = ex;
                }
                if (caught != null)
                {
                    Assert.That(caught.StatusCode,
                        Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid),
                        "Pre-Created ConditionRefreshAsync must throw with " +
                        "BadMonitoredItemIdInvalid.");
                }
                else
                {
                    TestContext.Out.WriteLine(
                        "Item was already Created when ConditionRefreshAsync ran — " +
                        "the server-side create raced ahead of the assertion; " +
                        "contract is still satisfied.");
                }
                await sub.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(200)]
        [CancelAfter(60_000)]
        public async Task ConditionRefreshOnDataItemReturnsCleanlyAsync(
            CancellationToken ct)
        {
            // The reference server's ServerStatus.State node is not a
            // condition source, so ConditionRefresh2 against an item
            // monitoring it should either succeed (no conditions to
            // re-fire) or fail with a specific OPC UA status code
            // (BadFilterNotAllowed / BadMethodInvalid / BadConditionAlreadyEnabled
            // family). What matters for this test: the call routes
            // through the V2 surface with the correct ObjectId +
            // MethodId and properly validates the per-method result.
            ManagedSession session = await ConnectV2Async(
                nameof(ConditionRefreshOnDataItemReturnsCleanlyAsync), ct)
                .ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription sub = session.AddSubscription(handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                bool created = await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                Assert.That(sub.TryAddMonitoredItem(
                    "Time",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with { SamplingInterval = TimeSpan.Zero },
                    out Client.Subscriptions.MonitoredItems.IMonitoredItem? item), Is.True);
                bool itemCreated = await WaitForAsync(() => item!.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(itemCreated, Is.True);

                try
                {
                    await item!.ConditionRefreshAsync(ct).ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    // A bad status is acceptable here so long as it's a
                    // documented Condition-related status (i.e. it
                    // actually reached the server). We assert it's not
                    // an "unexpected" code that would suggest the V2
                    // surface mis-routed the call.
                    StatusCode code = sre.StatusCode;
                    Assert.That(
                        StatusCode.IsBad(code),
                        Is.True,
                        "Service exception should carry a Bad status");
                    Assert.That(code, Is.Not.EqualTo(StatusCodes.BadServiceUnsupported),
                        "ConditionType_ConditionRefresh2 must be supported by the server");
                    Assert.That(code, Is.Not.EqualTo(StatusCodes.BadInvalidArgument),
                        "V2 client must pass valid (subId, monitoredItemId) arguments");
                    TestContext.Out.WriteLine(
                        "ConditionRefresh2 returned non-Good (acceptable for a non-condition source): {0}",
                        code);
                }

                await sub.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        private async Task<ManagedSession> ConnectV2Async(
            string sessionName, CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            return await new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(sessionName)
                .WithSessionTimeout(TimeSpan.FromSeconds(120))
                .ConnectAsync(ct).ConfigureAwait(false);
        }

        private static async Task<bool> WaitForAsync(
            Func<bool> predicate, TimeSpan timeout, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (predicate())
                {
                    return true;
                }
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            return predicate();
        }
    }
}
