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
                samplingRates ?? Array.Empty<SamplingRateGroup>());
        }

        private static OperationContext SessionlessContext()
        {
            return new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
        }

        [Test]
        public void ConstructorWithNullServerThrows()
        {
            var nm = new Mock<IAsyncNodeManager>();
            Assert.That(
                () => new SamplingGroupManager(
                    null!, nm.Object, 100, 200, Array.Empty<SamplingRateGroup>()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorWithNullNodeManagerThrows()
        {
            Mock<IServerInternal> mockServer = DeterministicServerMock.Create(out _);
            Assert.That(
                () => new SamplingGroupManager(
                    mockServer.Object, null!, 100, 200, Array.Empty<SamplingRateGroup>()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorWithEmptySamplingRatesUsesDefaults()
        {
            using SamplingGroupManager manager = CreateManager(
                out _, Array.Empty<SamplingRateGroup>());

            Assert.That(manager, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithNullSamplingRatesUsesDefaults()
        {
            Mock<IServerInternal> mockServer = DeterministicServerMock.Create(out _);
            var nm = new Mock<IAsyncNodeManager>();

            using var manager = new SamplingGroupManager(
                mockServer.Object, nm.Object, 100, 200, null!);

            Assert.That(manager, Is.Not.Null);
        }

        [Test]
        public void CreateMonitoredItemWithEventFilterCreatesExceptionBasedItem()
        {
            using SamplingGroupManager manager = CreateManager(out _);

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
                    Filter = new ExtensionObject(new EventFilter())
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
                new Opc.Ua.Range(),
                0,
                false);

            Assert.That(item, Is.Not.Null);
            Assert.That(item, Is.InstanceOf<MonitoredItem>());
            Assert.That(item.SamplingInterval, Is.Zero);
        }

        [Test]
        public void StopMonitoringRemovesExceptionBasedItem()
        {
            using SamplingGroupManager manager = CreateManager(out _);

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
                    Filter = new ExtensionObject(new EventFilter())
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
                new Opc.Ua.Range(),
                0,
                false);

            Assert.DoesNotThrow(() => manager.StopMonitoring(item));
            // second stop is a no-op because the item is no longer tracked.
            Assert.DoesNotThrow(() => manager.StopMonitoring(item));
        }

        [Test]
        public void ShutdownWithoutGroupsIsSafe()
        {
            using SamplingGroupManager manager = CreateManager(out _);

            Assert.DoesNotThrow(() => manager.Shutdown());
        }

        [Test]
        public void ApplyChangesWithoutGroupsIsSafe()
        {
            using SamplingGroupManager manager = CreateManager(out _);

            Assert.DoesNotThrow(() => manager.ApplyChanges());
        }
    }
}
