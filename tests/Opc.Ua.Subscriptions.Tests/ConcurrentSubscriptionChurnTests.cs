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

// CA2016: integration tests intentionally call cleanup in finally without forwarding the test
// cancellation token. The test CT may already be cancelled (the [CancelAfter] timeout), which
// would prevent cleanup from running. CloseAsync/DisposeAsync must complete regardless.
#pragma warning disable CA2016

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// Regression coverage for
    /// <see href="https://github.com/OPCFoundation/UA-.NETStandard/issues/4113"/>:
    /// adding and disposing subscriptions concurrently on a single
    /// <see cref="ManagedSessionType"/> silently starved a fraction of them.
    /// A subscription resets its server side id before its message processor
    /// completes, so retiring it by id evicted an arbitrary other subscription
    /// that had been added but not created yet. The evicted subscription was
    /// then missing from the publish dispatch registry and never received a
    /// notification.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ManagedSession")]
    [Category("SubscriptionManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ConcurrentSubscriptionChurnTests : ClientTestFramework
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
        [CancelAfter(180_000)]
        public async Task ConcurrentSubscriptionChurnNeverStarvesASubscription(
            CancellationToken ct)
        {
            const int kConcurrency = 6;
            const int kIterationsPerWorker = 8;

            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            ManagedSessionType session = await new ManagedSessionBuilder(
                    ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(nameof(ConcurrentSubscriptionChurnNeverStarvesASubscription))
                .WithSessionTimeout(TimeSpan.FromSeconds(120))
                .ConnectAsync(ct)
                .ConfigureAwait(false);

            var starved = new ConcurrentBag<int>();
            try
            {
                Assert.That(
                    session.TryGetSubscriptionManager(out ISubscriptionManager? manager),
                    Is.True);
                Assert.That(manager, Is.Not.Null);

                await Task.WhenAll(Enumerable.Range(0, kConcurrency)
                    .Select(worker => Task.Run(async () =>
                    {
                        for (int i = 0; i < kIterationsPerWorker; i++)
                        {
                            var handler = new RecordingSubscriptionHandler();
                            ISubscription subscription = session.AddSubscription(
                                handler,
                                new Client.Subscriptions.SubscriptionOptions
                                {
                                    PublishingInterval = TimeSpan.FromMilliseconds(200),
                                    PublishingEnabled = true,
                                    KeepAliveCount = 10,
                                    LifetimeCount = 1000
                                });
                            try
                            {
                                Assert.That(
                                    subscription.TryAddMonitoredItem(
                                        "CurrentTime",
                                        VariableIds.Server_ServerStatus_CurrentTime,
                                        o => o with
                                        {
                                            SamplingInterval =
                                                TimeSpan.FromMilliseconds(100),
                                            QueueSize = 1
                                        },
                                        out _),
                                    Is.True);

                                bool gotData = await handler
                                    .WaitForFirstDataAsync(
                                        TimeSpan.FromSeconds(20), ct)
                                    .ConfigureAwait(false);
                                if (!gotData)
                                {
                                    starved.Add((worker * kIterationsPerWorker) + i);
                                }
                            }
                            finally
                            {
                                await subscription.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                    }))).ConfigureAwait(false);

                Assert.That(starved, Is.Empty,
                    $"{starved.Count} of {kConcurrency * kIterationsPerWorker} " +
                    "subscriptions never received a data change notification.");

                // Every subscription was disposed, so the manager registry
                // must be empty again — an evicted subscription would leave
                // the counts inconsistent.
                Assert.That(manager!.Count, Is.Zero);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
