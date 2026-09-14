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
    /// <summary>
    /// Verifies enabling disabled monitored items starts initial and periodic sampling through live services.
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class SamplingModeRegressionTests : TestFixture
    {
        /// <summary>
        /// Registers a sampling-enabled variable and resolves its namespace in the client session.
        /// </summary>
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

        /// <summary>
        /// Verifies transition to Reporting produces the initial value and later updates, optionally via Sampling mode.
        /// </summary>
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

        /// <summary>
        /// Writes the variable through the client service and requires a successful result.
        /// </summary>
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

        /// <summary>
        /// Reads the next data-change value within a bounded number of publishing cycles.
        /// </summary>
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

        /// <summary>
        /// Requires a Good value containing the exact expected Double.
        /// </summary>
        private static void AssertValue(in DataValue value, double expected)
        {
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// Identifies the address space containing the sampling regression variable.
        /// </summary>
        private const string kNamespaceUri = "urn:opcfoundation:subscription-tests:sampling-regression";

        /// <summary>
        /// Identifies the variable whose writes are observed by the monitored item.
        /// </summary>
        private NodeId m_nodeId;

        /// <summary>
        /// Creates the sampling-enabled address space for live subscription tests.
        /// </summary>
        private sealed class SamplingManagerFactory : IAsyncNodeManagerFactory
        {
            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris => [kNamespaceUri];

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<IAsyncNodeManager>(new SamplingManager(server, configuration));
            }
        }

        /// <summary>
        /// Hosts a writable variable using sampling-group monitoring.
        /// </summary>
        private sealed class SamplingManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Enables sampling-group management for the test namespace.
            /// </summary>
            public SamplingManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration, true, kNamespaceUri)
            {
            }

            /// <summary>
            /// Registers the readable and writable Double with a 50 millisecond minimum sampling interval.
            /// </summary>
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
