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

using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Tests;

#pragma warning disable CS0618 // tests exercise the obsolete classic API

namespace Opc.Ua.Subscriptions.Classic.Tests
{
    /// <summary>
    /// Exercises persisted relative-path items against the server without recreating them.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("TransferSubscription")]
    public sealed class RelativePathTransferTests : ClientTestFramework
    {
        /// <summary>
        /// Uses the reference server's UA TCP endpoint.
        /// </summary>
        public RelativePathTransferTests()
            : base(Utils.UriSchemeOpcTcp)
        {
        }

        /// <inheritdoc/>
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SingleSession = true;
            ClientFixtureSubscriptionEngineFactory = ClassicSubscriptionEngineFactory.Instance;
            return OneTimeSetUpCoreAsync(securityNone: true);
        }

        /// <inheritdoc/>
        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <inheritdoc/>
        [SetUp]
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        /// <inheritdoc/>
        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        /// <summary>
        /// Transfer and saved-session reactivation retain item identity through maintenance.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RestoredRelativePathRetainsIdentityAndSupportsMaintenanceAsync(bool reactivate)
        {
            using ISession origin = await ClientFixture.ConnectAsync(
                ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
            origin.DeleteSubscriptionsOnClose = false;
            using var subscription = new Subscription(origin.DefaultSubscription)
            {
                PublishingInterval = 100,
                KeepAliveCount = 10,
                LifetimeCount = 1000,
                PublishingEnabled = true
            };
            var item = new MonitoredItem(Telemetry)
            {
                StartNodeId = ObjectIds.Server,
                RelativePath = "/ServerStatus/CurrentTime",
                SamplingInterval = 100
            };
            subscription.AddItem(item);
            Assert.That(origin.AddSubscription(subscription), Is.True);
            await subscription.CreateAsync().ConfigureAwait(false);
            Assert.That(item.Status.Error, Is.Null);
            Assert.That(item.ResolvedNodeId, Is.EqualTo(VariableIds.Server_ServerStatus_CurrentTime));
            uint subscriptionId = subscription.Id;
            uint serverId = item.ServerId;
            uint clientHandle = item.ClientHandle;
            using var snapshot = new MemoryStream();
            origin.Save(snapshot, [subscription]);
            snapshot.Position = 0;

            ISession target;
            if (reactivate)
            {
                using var configurationStream = new MemoryStream();
                origin.SaveSessionConfiguration(configurationStream);
                configurationStream.Position = 0;
                SessionConfiguration configuration = SessionConfiguration.Create(configurationStream, Telemetry)!;
                origin.TransportChannel.Dispose();
                ITransportChannel channel = await ClientFixture.CreateChannelAsync(
                    configuration.ConfiguredEndpoint!, false).ConfigureAwait(false);
                target = ClientFixture.CreateSession(channel, configuration.ConfiguredEndpoint!);
                try
                {
                    Assert.That(target.ApplySessionConfiguration(configuration), Is.True);
                    await target.ReconnectAsync(channel).ConfigureAwait(false);
                }
                catch
                {
                    target.Dispose();
                    throw;
                }
            }
            else
            {
                await origin.CloseAsync().ConfigureAwait(false);
                target = await ClientFixture.ConnectAsync(
                    ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
            }

            using (target)
            {
                Subscription restored = target.Load(snapshot, true).Single();
                MonitoredItem restoredItem = restored.MonitoredItems.Single();
                int notifications = 0;
                restoredItem.Notification += (_, _) => Interlocked.Increment(ref notifications);
                Assert.That(restoredItem.ResolvedNodeId, Is.EqualTo(VariableIds.Server_ServerStatus_CurrentTime));
                Assert.That(restoredItem.Status.NodeId, Is.EqualTo(VariableIds.Server_ServerStatus_CurrentTime));

                bool adopted = reactivate
                    ? await target.ReactivateSubscriptionsAsync([restored], true).ConfigureAwait(false)
                    : await target.TransferSubscriptionsAsync([restored], true).ConfigureAwait(false);
                Assert.That(adopted, Is.True);
                Assert.That(restored.Id, Is.EqualTo(subscriptionId));
                Assert.That(restoredItem.ServerId, Is.EqualTo(serverId));
                Assert.That(restoredItem.ClientHandle, Is.EqualTo(clientHandle));
                await TestPolling.WaitUntilAsync(
                    () => Volatile.Read(ref notifications) > 0,
                    timeoutMessage: "The restored relative-path item did not receive notifications.")
                    .ConfigureAwait(false);

                await restored.ApplyChangesAsync().ConfigureAwait(false);
                var added = new MonitoredItem(Telemetry)
                {
                    StartNodeId = ObjectIds.Server,
                    RelativePath = "/ServerStatus/State"
                };
                restored.AddItem(added);
                await restored.ApplyChangesAsync().ConfigureAwait(false);
                Assert.That(added.Created, Is.True);
                Assert.That(added.ResolvedNodeId, Is.EqualTo(VariableIds.Server_ServerStatus_State));
                Assert.That(restoredItem.ServerId, Is.EqualTo(serverId));
                ArrayOf<uint> handles = await ReadHandlesAsync(target, restored.Id).ConfigureAwait(false);
                Assert.That(handles.ToArray(), Is.EquivalentTo(new[] { serverId, added.ServerId }));

                restoredItem.SamplingInterval = 200;
                await restored.ApplyChangesAsync().ConfigureAwait(false);
                await restored.SetMonitoringModeAsync(MonitoringMode.Sampling, [restoredItem])
                    .ConfigureAwait(false);
                Assert.That(restoredItem.ServerId, Is.EqualTo(serverId));
                Assert.That(restoredItem.Status.NodeId, Is.EqualTo(VariableIds.Server_ServerStatus_CurrentTime));
                restored.RemoveItem(restoredItem);
                await restored.ApplyChangesAsync().ConfigureAwait(false);
                Assert.That(restoredItem.Created, Is.False);
                handles = await ReadHandlesAsync(target, restored.Id).ConfigureAwait(false);
                Assert.That(handles.ToArray(), Is.EqualTo(new[] { added.ServerId }));
                await target.CloseAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads authoritative server handles to detect unintended recreation or duplication.
        /// </summary>
        private static async Task<ArrayOf<uint>> ReadHandlesAsync(ISession session, uint subscriptionId)
        {
            CallResponse response = await session.CallAsync(null,
                [
                    new CallMethodRequest
                    {
                        ObjectId = ObjectIds.Server,
                        MethodId = MethodIds.Server_GetMonitoredItems,
                        InputArguments = [Variant.From(subscriptionId)]
                    }
                ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].OutputArguments[0].TryGetValue(out ArrayOf<uint> handles), Is.True);
            return handles;
        }
    }
}
