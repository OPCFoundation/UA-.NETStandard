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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// Integration tests for the server wide MinSupportedSampleRate configuration option
    /// and its interplay with the MinimumSamplingInterval declared by a node. The fixture
    /// starts a reference server that advertises a non-zero minimum supported sample rate.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Subscription")]
    [Category("MonitoredItem")]
    [Category("MinSupportedSampleRate")]
    public class MinSupportedSampleRateTests : TestFixture
    {
        /// <summary>
        /// Chosen so that it is above the MinimumSamplingInterval of
        /// <c>Scalar_Static_Float</c> (100 ms) and below the MinimumSamplingInterval of
        /// <c>Server.ServerStatus.CurrentTime</c> (1000 ms), which lets a single server
        /// cover both directions of the interplay.
        /// </summary>
        private const double kMinSupportedSampleRate = 500;

        private const double kNodeMinimumAboveFloor = 1000;
        private const double kRequestedBelowFloor = 10;
        private const double kPublishingInterval = 1000;

        protected override void ConfigureServer(ApplicationConfiguration configuration)
        {
            configuration.ServerConfiguration.MinSupportedSampleRate = kMinSupportedSampleRate;
        }

        [SetUp]
        public async Task SetUp()
        {
            m_subscriptionId = await CreateSetupSubscriptionAsync(
                publishingInterval: kPublishingInterval,
                requestedLifetimeCount: 100,
                requestedMaxKeepAliveCount: 10).ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDown()
        {
            if (m_subscriptionId > 0)
            {
                await Session.DeleteSubscriptionsAsync(
                    null,
                    new uint[] { m_subscriptionId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
                m_subscriptionId = 0;
            }
        }

        [Test]
        public async Task ServerCapabilitiesReportsConfiguredMinSupportedSampleRateAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                new ReadValueId[]
                {
                    new()
                    {
                        NodeId = VariableIds.Server_ServerCapabilities_MinSupportedSampleRate,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(
                response.Results[0].WrappedValue.GetDouble(),
                Is.EqualTo(kMinSupportedSampleRate),
                "MinSupportedSampleRate must reflect the configured value.");
        }

        [Test]
        public async Task SamplingIntervalBelowMinSupportedSampleRateIsRevisedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticFloat);

            MonitoredItemCreateResult result = await CreateItemAsync(
                nodeId,
                kRequestedBelowFloor).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(
                result.RevisedSamplingInterval,
                Is.EqualTo(kMinSupportedSampleRate),
                "The server wide minimum must raise a node minimum of 100 ms to 500 ms.");
        }

        [Test]
        public async Task NodeMinimumSamplingIntervalAboveFloorWinsAsync()
        {
            NodeId nodeId = ToNodeId(
                new ExpandedNodeId(
                    "Scalar_Static_Arrays2D_LocalizedText",
                    Constants.ReferenceServerNamespaceUri));

            MonitoredItemCreateResult result = await CreateItemAsync(
                nodeId,
                kRequestedBelowFloor).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(
                result.RevisedSamplingInterval,
                Is.EqualTo(kNodeMinimumAboveFloor),
                "A node minimum above the server wide minimum must win.");
        }

        [Test]
        public async Task ServerStatusCurrentTimeIsRaisedToMinSupportedSampleRateAsync()
        {
            // the reference server declares a MinimumSamplingInterval of 250 ms for
            // CurrentTime, which is below the configured minimum supported sample rate.
            MonitoredItemCreateResult result = await CreateItemAsync(
                VariableIds.Server_ServerStatus_CurrentTime,
                kRequestedBelowFloor).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(
                result.RevisedSamplingInterval,
                Is.EqualTo(kMinSupportedSampleRate));
        }

        [Test]
        public async Task ContinuousNodeBypassesMinSupportedSampleRateAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            MonitoredItemCreateResult result = await CreateItemAsync(
                nodeId,
                kRequestedBelowFloor).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(
                result.RevisedSamplingInterval,
                Is.EqualTo(kRequestedBelowFloor),
                "Nodes that report by exception are not bound by the minimum sample rate.");
        }

        [Test]
        public async Task NonValueAttributeIsBoundByMinSupportedSampleRateAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            MonitoredItemCreateResult result = await CreateItemAsync(
                nodeId,
                kRequestedBelowFloor,
                attributeId: Attributes.DisplayName).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(
                result.RevisedSamplingInterval,
                Is.EqualTo(kMinSupportedSampleRate),
                "Attributes other than Value do not declare a MinimumSamplingInterval.");
        }

        [Test]
        public async Task SamplingIntervalAboveMinSupportedSampleRateIsKeptAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticFloat);

            MonitoredItemCreateResult result = await CreateItemAsync(
                nodeId,
                5000).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.RevisedSamplingInterval, Is.EqualTo(5000.0));
        }

        [Test]
        public async Task NegativeSamplingIntervalUsesPublishingIntervalAndFloorAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticFloat);

            MonitoredItemCreateResult result = await CreateItemAsync(
                nodeId,
                -1).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(
                result.RevisedSamplingInterval,
                Is.EqualTo(kPublishingInterval),
                "A negative sampling interval resolves to the publishing interval.");
        }

        [Test]
        public async Task ModifyMonitoredItemBelowMinSupportedSampleRateIsRevisedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticFloat);

            MonitoredItemCreateResult createResult = await CreateItemAsync(
                nodeId,
                5000).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(createResult.StatusCode), Is.True);

            ModifyMonitoredItemsResponse modifyResponse = await Session.ModifyMonitoredItemsAsync(
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                new MonitoredItemModifyRequest[]
                {
                    new()
                    {
                        MonitoredItemId = createResult.MonitoredItemId,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = kRequestedBelowFloor,
                            QueueSize = 10,
                            DiscardOldest = true
                        }
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modifyResponse.Results[0].StatusCode), Is.True);
            Assert.That(
                modifyResponse.Results[0].RevisedSamplingInterval,
                Is.EqualTo(kMinSupportedSampleRate));
        }

        [Test]
        public async Task ModifyMonitoredItemOnContinuousNodeBypassesFloorAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            MonitoredItemCreateResult createResult = await CreateItemAsync(
                nodeId,
                5000).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(createResult.StatusCode), Is.True);

            ModifyMonitoredItemsResponse modifyResponse = await Session.ModifyMonitoredItemsAsync(
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                new MonitoredItemModifyRequest[]
                {
                    new()
                    {
                        MonitoredItemId = createResult.MonitoredItemId,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = kRequestedBelowFloor,
                            QueueSize = 10,
                            DiscardOldest = true
                        }
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modifyResponse.Results[0].StatusCode), Is.True);
            Assert.That(
                modifyResponse.Results[0].RevisedSamplingInterval,
                Is.EqualTo(kRequestedBelowFloor));
        }

        [Test]
        public async Task EventMonitoredItemIsNotBoundByMinSupportedSampleRateAsync()
        {
            var filter = new EventFilter();
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.EventId));
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.Message));

            MonitoredItemCreateResult result = await CreateItemAsync(
                ObjectIds.Server,
                kRequestedBelowFloor,
                attributeId: Attributes.EventNotifier,
                filter: new ExtensionObject(filter)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(
                result.RevisedSamplingInterval,
                Is.EqualTo(kRequestedBelowFloor),
                "Event monitored items are not bound by the minimum supported sample rate.");
        }

        private async Task<MonitoredItemCreateResult> CreateItemAsync(
            NodeId nodeId,
            double samplingInterval,
            uint attributeId = Attributes.Value,
            ExtensionObject filter = default)
        {
            CreateMonitoredItemsResponse response = await Session.CreateMonitoredItemsAsync(
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[]
                {
                    new()
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = nodeId,
                            AttributeId = attributeId
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = samplingInterval,
                            Filter = filter,
                            DiscardOldest = true,
                            QueueSize = 10
                        }
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(1));

            return response.Results[0];
        }

        private uint m_subscriptionId;
    }
}
