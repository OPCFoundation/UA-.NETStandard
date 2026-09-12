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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;

namespace Opc.Ua.Subscriptions.Tests
{
    [TestFixture]
    [Category("Subscription")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class SamplingModeRegressionTests : TestFixture
    {
        [OneTimeSetUp]
        public async Task AddSamplingManagerAsync()
        {
            await ReferenceServer.NodeManagerLifecycle.AddAsync(
                new SamplingManagerFactory(), callerContext: null).ConfigureAwait(false);
            await Session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            int index = Session.NamespaceUris.GetIndex(kNamespaceUri);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            m_nodeId = new NodeId("Value", (ushort)index);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DisabledItemStartsInitialAndPeriodicSamplingWhenEnabledAsync(bool passThroughSampling)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await WriteValueAsync(1.0, deadline.Token).ConfigureAwait(false);
            CreateSubscriptionResponse subscription = await Session.CreateSubscriptionAsync(
                null, 50, 100, 3, 0, true, 0, deadline.Token).ConfigureAwait(false);
            uint subscriptionId = subscription.SubscriptionId;
            try
            {
                CreateMonitoredItemsResponse created = await Session.CreateMonitoredItemsAsync(
                    null,
                    subscriptionId,
                    TimestampsToReturn.Both,
                    [
                        new MonitoredItemCreateRequest
                        {
                            ItemToMonitor = new ReadValueId
                            {
                                NodeId = m_nodeId,
                                AttributeId = Attributes.Value
                            },
                            MonitoringMode = MonitoringMode.Disabled,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 1,
                                SamplingInterval = 50,
                                QueueSize = 1,
                                DiscardOldest = true
                            }
                        }
                    ],
                    deadline.Token).ConfigureAwait(false);
                Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                uint itemId = created.Results[0].MonitoredItemId;
                if (passThroughSampling)
                {
                    SetMonitoringModeResponse sampling = await Session.SetMonitoringModeAsync(
                        null, subscriptionId, MonitoringMode.Sampling, [itemId], deadline.Token)
                        .ConfigureAwait(false);
                    Assert.That(sampling.Results[0], Is.EqualTo(StatusCodes.Good));
                }
                SetMonitoringModeResponse reporting = await Session.SetMonitoringModeAsync(
                    null, subscriptionId, MonitoringMode.Reporting, [itemId], deadline.Token)
                    .ConfigureAwait(false);
                Assert.That(reporting.Results[0], Is.EqualTo(StatusCodes.Good));
                DataValue initial = await NextValueAsync(deadline.Token).ConfigureAwait(false);
                AssertValue(initial, 1.0);

                await WriteValueAsync(2.0, deadline.Token).ConfigureAwait(false);
                DataValue next = await NextValueAsync(deadline.Token).ConfigureAwait(false);
                AssertValue(next, 2.0);
            }
            finally
            {
                await Session.DeleteSubscriptionsAsync(null, [subscriptionId], CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        private async ValueTask WriteValueAsync(double value, CancellationToken ct)
        {
            WriteResponse response = await Session.WriteAsync(
                null,
                [
                    new WriteValue
                    {
                        NodeId = m_nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(value)
                    }
                ],
                ct).ConfigureAwait(false);
            Assert.That(response.Results[0], Is.EqualTo(StatusCodes.Good));
        }

        private async Task<DataValue> NextValueAsync(CancellationToken ct)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                PublishResponse response = await Session.PublishAsync(
                    new RequestHeader { TimeoutHint = 2_000 }, [], ct).ConfigureAwait(false);
                for (int i = 0; i < response.NotificationMessage.NotificationData.Count; i++)
                {
                    if (response.NotificationMessage.NotificationData[i]
                        .TryGetValue(out DataChangeNotification? data) &&
                        data!.MonitoredItems.Count > 0)
                    {
                        return data.MonitoredItems[0].Value;
                    }
                }
            }
            throw new AssertionException("No sampled value arrived within eight publishing cycles.");
        }

        private static void AssertValue(in DataValue value, double expected)
        {
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        private const string kNamespaceUri = "urn:opcfoundation:subscription-tests:sampling-regression";
        private NodeId m_nodeId;

        private sealed class SamplingManagerFactory : IAsyncNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => [kNamespaceUri];

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<IAsyncNodeManager>(new SamplingManager(server, configuration));
            }
        }

        private sealed class SamplingManager : AsyncCustomNodeManager
        {
            public SamplingManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration, true, kNamespaceUri)
            {
            }

            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                var variable = new BaseDataVariableState(null);
                variable.CreateAsPredefinedNode(SystemContext, cancellationToken);
                variable.NodeId = new NodeId("Value", NamespaceIndex);
                variable.BrowseName = new QualifiedName("Value", NamespaceIndex);
                variable.DisplayName = new LocalizedText("Value");
                variable.TypeDefinitionId = VariableTypeIds.BaseDataVariableType;
                variable.DataType = DataTypeIds.Double;
                variable.ValueRank = ValueRanks.Scalar;
                variable.MinimumSamplingInterval = 50;
                variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.Value = new Variant(1.0);
                await AddPredefinedNodeAsync(SystemContext, variable, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
