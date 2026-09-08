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

using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;

// CA2000: test code; the manager and any monitored items it owns are torn down
// with the fixture.
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Deterministic, offline unit tests for <see cref="SamplingGroupManager"/>
    /// covering construction, the event-filter creation path and the lifecycle
    /// helpers, without starting the background sampling loop.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("SamplingGroupManager")]
    [Parallelizable(ParallelScope.All)]
    public class SamplingGroupManagerTests
    {
        private static SamplingGroupManager CreateManager(
            out Mock<IServerInternal> mockServer,
            IEnumerable<SamplingRateGroup> samplingRates = null)
        {
            mockServer = DeterministicServerMock.Create(out _);
            var mockNodeManager = new Mock<IAsyncNodeManager>();
            return new SamplingGroupManager(
                mockServer.Object,
                mockNodeManager.Object,
                100,
                200,
                samplingRates ?? []);
        }

        private static OperationContext SessionlessContext()
        {
            return new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
        }

        private static OperationContext SessionContext()
        {
            return new OperationContext(
                new RequestHeader(),
                null,
                RequestType.CreateMonitoredItems,
                RequestLifetime.None,
                new Mock<ISession>().Object);
        }

        /// <summary>
        /// Verifies that sampling-group manager construction rejects a null server.
        /// </summary>
        [Test]
        public void ConstructorWithNullServerThrows()
        {
            var nm = new Mock<IAsyncNodeManager>();
            Assert.That(
                () => new SamplingGroupManager(
                    null!, nm.Object, 100, 200, []),
                Throws.TypeOf<ArgumentNullException>());
        }

        /// <summary>
        /// Verifies that sampling-group manager construction rejects a null node manager.
        /// </summary>
        [Test]
        public void ConstructorWithNullNodeManagerThrows()
        {
            Mock<IServerInternal> mockServer = DeterministicServerMock.Create(out _);
            Assert.That(
                () => new SamplingGroupManager(
                    mockServer.Object, null!, 100, 200, []),
                Throws.TypeOf<ArgumentNullException>());
        }

        /// <summary>
        /// Verifies that an empty sampling-rate collection selects the default rates.
        /// </summary>
        [Test]
        public void ConstructorWithEmptySamplingRatesUsesDefaults()
        {
            using SamplingGroupManager manager = CreateManager(
                out _, []);

            Assert.That(manager, Is.Not.Null);
        }

        /// <summary>
        /// Verifies that a null sampling-rate collection selects the default rates.
        /// </summary>
        [Test]
        public void ConstructorWithNullSamplingRatesUsesDefaults()
        {
            Mock<IServerInternal> mockServer = DeterministicServerMock.Create(out _);
            var nm = new Mock<IAsyncNodeManager>();

            using var manager = new SamplingGroupManager(
                mockServer.Object, nm.Object, 100, 200, null!);

            Assert.That(manager, Is.Not.Null);
        }

        /// <summary>
        /// Verifies that creating a monitored item with an event filter creates an exception-based item.
        /// </summary>
        [Test]
        public void CreateMonitoredItemWithEventFilterCreatesExceptionBasedItem()
        {
            using SamplingGroupManager manager = CreateManager(out _);
            var filter = new EventFilter();

            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId(),
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 1000,
                    QueueSize = 5,
                    DiscardOldest = true,
                    Filter = new ExtensionObject(filter)
                }
            };
            ISampledDataChangeMonitoredItem item = manager.CreateMonitoredItem(
                SessionlessContext(),
                1,
                1000.0,
                TimestampsToReturn.Both,
                7,
                null!,
                itemToCreate,
                filter,
                new Range(),
                0,
                false);

            Assert.That(item, Is.Not.Null);
            Assert.That(item, Is.InstanceOf<MonitoredItem>());
            Assert.That(item.SamplingInterval, Is.Zero);
        }

        /// <summary>
        /// Verifies that stopping monitoring removes an exception-based item.
        /// </summary>
        [Test]
        public void StopMonitoringRemovesExceptionBasedItem()
        {
            using SamplingGroupManager manager = CreateManager(out _);
            var filter = new EventFilter();

            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId(),
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 1000,
                    QueueSize = 5,
                    DiscardOldest = true,
                    Filter = new ExtensionObject(filter)
                }
            };
            ISampledDataChangeMonitoredItem item = manager.CreateMonitoredItem(
                SessionlessContext(),
                1,
                1000.0,
                TimestampsToReturn.Both,
                8,
                null!,
                itemToCreate,
                filter,
                new Range(),
                0,
                false);

            Assert.That(() => manager.StopMonitoring(item), Throws.Nothing);
            // second stop is a no-op because the item is no longer tracked.
            Assert.That(() => manager.StopMonitoring(item), Throws.Nothing);
        }

        /// <summary>
        /// Verifies that shutting down a manager without sampling groups is safe.
        /// </summary>
        [Test]
        public void ShutdownWithoutGroupsIsSafe()
        {
            using SamplingGroupManager manager = CreateManager(out _);

            Assert.DoesNotThrow(manager.Shutdown);
        }

        /// <summary>
        /// Verifies that applying changes without sampling groups is safe.
        /// </summary>
        [Test]
        public void ApplyChangesWithoutGroupsIsSafe()
        {
            using SamplingGroupManager manager = CreateManager(out _);

            Assert.DoesNotThrow(manager.ApplyChanges);
        }

        /// <summary>
        /// Verifies that create and modify retain original filters and request objects separately from revised filters.
        /// </summary>
        [Test]
        public void RevisedFiltersPreserveOriginalFiltersAndRequests()
        {
            using SamplingGroupManager manager = CreateManager(out _);
            var originalCreateFilter = new DataChangeFilter
            {
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 1
            };
            var revisedCreateFilter = new DataChangeFilter
            {
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 2
            };
            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = new NodeId("V", 1),
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 1000,
                    QueueSize = 5,
                    DiscardOldest = true,
                    Filter = new ExtensionObject(originalCreateFilter)
                }
            };
            MonitoringParameters createParameters = itemToCreate.RequestedParameters;
            using OperationContext context = SessionContext();

            ISampledDataChangeMonitoredItem item = manager.CreateMonitoredItem(
                context,
                1,
                1000,
                TimestampsToReturn.Both,
                9,
                null!,
                itemToCreate,
                revisedCreateFilter,
                new Range(),
                0,
                false);

            IStoredMonitoredItem stored = item.ToStorableMonitoredItem();
            Assert.That(stored.OriginalFilter, Is.SameAs(originalCreateFilter));
            Assert.That(stored.FilterToUse, Is.SameAs(revisedCreateFilter));
            Assert.That(itemToCreate.RequestedParameters, Is.SameAs(createParameters));
            Assert.That(
                createParameters.Filter.TryGetValue(out MonitoringFilter createRequestFilter),
                Is.True);
            Assert.That(createRequestFilter, Is.SameAs(originalCreateFilter));
            Assert.That(originalCreateFilter.DeadbandType, Is.EqualTo((uint)DeadbandType.Absolute));
            Assert.That(originalCreateFilter.DeadbandValue, Is.EqualTo(1));
            Assert.That(createParameters.ClientHandle, Is.EqualTo(1));
            Assert.That(createParameters.SamplingInterval, Is.EqualTo(1000));
            Assert.That(createParameters.QueueSize, Is.EqualTo(5));
            Assert.That(createParameters.DiscardOldest, Is.True);

            var originalModifyFilter = new DataChangeFilter
            {
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 3
            };
            var revisedModifyFilter = new DataChangeFilter
            {
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 4
            };
            var itemToModify = new MonitoredItemModifyRequest
            {
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 2,
                    SamplingInterval = 1000,
                    QueueSize = 5,
                    DiscardOldest = true,
                    Filter = new ExtensionObject(originalModifyFilter)
                }
            };
            MonitoringParameters modifyParameters = itemToModify.RequestedParameters;

            ServiceResult result = manager.ModifyMonitoredItem(
                context,
                TimestampsToReturn.Both,
                item,
                itemToModify,
                revisedModifyFilter,
                new Range());

            Assert.That(ServiceResult.IsGood(result), Is.True);
            stored = item.ToStorableMonitoredItem();
            Assert.That(stored.OriginalFilter, Is.SameAs(originalModifyFilter));
            Assert.That(stored.FilterToUse, Is.SameAs(revisedModifyFilter));
            Assert.That(itemToModify.RequestedParameters, Is.SameAs(modifyParameters));
            Assert.That(
                modifyParameters.Filter.TryGetValue(out MonitoringFilter modifyRequestFilter),
                Is.True);
            Assert.That(modifyRequestFilter, Is.SameAs(originalModifyFilter));
            Assert.That(originalModifyFilter.DeadbandType, Is.EqualTo((uint)DeadbandType.Absolute));
            Assert.That(originalModifyFilter.DeadbandValue, Is.EqualTo(3));
            Assert.That(modifyParameters.ClientHandle, Is.EqualTo(2));
            Assert.That(modifyParameters.SamplingInterval, Is.EqualTo(1000));
            Assert.That(modifyParameters.QueueSize, Is.EqualTo(5));
            Assert.That(modifyParameters.DiscardOldest, Is.True);
        }
    }
}
