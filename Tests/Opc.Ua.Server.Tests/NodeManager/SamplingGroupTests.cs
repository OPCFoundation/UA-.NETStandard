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

// CA2000: test code; the sampling group is short-lived and disposed per test.
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Deterministic, offline unit tests for <see cref="SamplingGroup"/> that
    /// exercise construction validation and the <c>MeetsGroupCriteria</c>
    /// branches without ever starting the background sampling loop.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("SamplingGroup")]
    [Parallelizable(ParallelScope.All)]
    public class SamplingGroupTests
    {
        private static List<SamplingRateGroup> SamplingRates()
        {
            return [new SamplingRateGroup(1000, 0, 1)];
        }

        private static OperationContext SessionlessContext()
        {
            return new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
        }

        private static OperationContext SessionContext(ISession session)
        {
            return new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None, session);
        }

        private static Mock<ISession> CreateSessionMock(NodeId id, IUserIdentity identity)
        {
            var session = new Mock<ISession>();
            session.SetupGet(s => s.Id).Returns(id);
            session.SetupGet(s => s.EffectiveIdentity).Returns(identity);
            return session;
        }

        private static SamplingGroup CreateGroup(
            IUserIdentity identity,
            double samplingInterval = 500,
            OperationContext context = null)
        {
            Mock<IServerInternal> mockServer = DeterministicServerMock.Create(out _);
            var mockNodeManager = new Mock<IAsyncNodeManager>();
            return new SamplingGroup(
                mockServer.Object,
                mockNodeManager.Object,
                SamplingRates(),
                context ?? SessionlessContext(),
                samplingInterval,
                identity);
        }

        private static Mock<ISampledDataChangeMonitoredItem> CreateItem(
            int type = MonitoredItemTypeMask.DataChange,
            MonitoringMode mode = MonitoringMode.Reporting,
            double samplingInterval = 500)
        {
            var item = new Mock<ISampledDataChangeMonitoredItem>();
            item.SetupGet(m => m.Id).Returns(1);
            item.SetupGet(m => m.MonitoredItemType).Returns(type);
            item.SetupGet(m => m.MonitoringMode).Returns(mode);
            item.SetupGet(m => m.SamplingInterval).Returns(samplingInterval);
            return item;
        }

        [Test]
        public void ConstructorWithNullServerThrows()
        {
            var nm = new Mock<IAsyncNodeManager>();
            Assert.That(
                () => new SamplingGroup(
                    null!, nm.Object, SamplingRates(), SessionlessContext(), 500,
                    new Mock<IUserIdentity>().Object),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorWithNullNodeManagerThrows()
        {
            Mock<IServerInternal> mockServer = DeterministicServerMock.Create(out _);
            Assert.That(
                () => new SamplingGroup(
                    mockServer.Object, null!, SamplingRates(), SessionlessContext(), 500,
                    new Mock<IUserIdentity>().Object),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorWithNullSamplingRatesThrows()
        {
            Mock<IServerInternal> mockServer = DeterministicServerMock.Create(out _);
            var nm = new Mock<IAsyncNodeManager>();
            Assert.That(
                () => new SamplingGroup(
                    mockServer.Object, nm.Object, null!, SessionlessContext(), 500,
                    new Mock<IUserIdentity>().Object),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorSessionlessWithoutOwnerIdentityThrows()
        {
            Mock<IServerInternal> mockServer = DeterministicServerMock.Create(out _);
            var nm = new Mock<IAsyncNodeManager>();
            Assert.That(
                () => new SamplingGroup(
                    mockServer.Object, nm.Object, SamplingRates(), SessionlessContext(), 500,
                    savedOwnerIdentity: null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void StartMonitoringRejectsNonDataChangeItem()
        {
            var identity = new Mock<IUserIdentity>();
            using SamplingGroup group = CreateGroup(identity.Object);
            Mock<ISampledDataChangeMonitoredItem> item = CreateItem(type: MonitoredItemTypeMask.Events);

            bool added = group.StartMonitoring(SessionlessContext(), item.Object, identity.Object);

            Assert.That(added, Is.False);
        }

        [Test]
        public void StartMonitoringRejectsDisabledItem()
        {
            var identity = new Mock<IUserIdentity>();
            using SamplingGroup group = CreateGroup(identity.Object);
            Mock<ISampledDataChangeMonitoredItem> item = CreateItem(mode: MonitoringMode.Disabled);

            bool added = group.StartMonitoring(SessionlessContext(), item.Object, identity.Object);

            Assert.That(added, Is.False);
        }

        [Test]
        public void StartMonitoringRejectsMismatchedSamplingInterval()
        {
            var identity = new Mock<IUserIdentity>();
            using SamplingGroup group = CreateGroup(identity.Object);
            Mock<ISampledDataChangeMonitoredItem> item = CreateItem(samplingInterval: 5000);

            bool added = group.StartMonitoring(SessionlessContext(), item.Object, identity.Object);

            Assert.That(added, Is.False);
        }

        [Test]
        public void StartMonitoringRejectsMismatchedSession()
        {
            var identity = new Mock<IUserIdentity>();
            Mock<ISession> sessionA = CreateSessionMock(new NodeId(1, 1), identity.Object);
            Mock<ISession> sessionB = CreateSessionMock(new NodeId(2, 1), identity.Object);
            using SamplingGroup group = CreateGroup(
                identity.Object, context: SessionContext(sessionA.Object));
            Mock<ISampledDataChangeMonitoredItem> item = CreateItem();

            bool added = group.StartMonitoring(
                SessionContext(sessionB.Object), item.Object, identity.Object);

            Assert.That(added, Is.False);
        }

        [Test]
        public void StartMonitoringAcceptsMatchingItem()
        {
            var identity = new Mock<IUserIdentity>();
            Mock<ISession> session = CreateSessionMock(new NodeId(1234, 1), identity.Object);
            OperationContext context = SessionContext(session.Object);
            using SamplingGroup group = CreateGroup(identity.Object, context: context);
            Mock<ISampledDataChangeMonitoredItem> item = CreateItem();

            bool added = group.StartMonitoring(context, item.Object, identity.Object);

            Assert.That(added, Is.True);
            item.Verify(m => m.SetSamplingInterval(1000.0), Times.Once);
        }

        [Test]
        public void StopMonitoringReturnsFalseForUnknownItem()
        {
            var identity = new Mock<IUserIdentity>();
            using SamplingGroup group = CreateGroup(identity.Object);
            Mock<ISampledDataChangeMonitoredItem> item = CreateItem();

            Assert.That(group.StopMonitoring(item.Object), Is.False);
        }

        [Test]
        public void ModifyMonitoringReturnsFalseForUnknownItem()
        {
            var identity = new Mock<IUserIdentity>();
            using SamplingGroup group = CreateGroup(identity.Object);
            Mock<ISampledDataChangeMonitoredItem> item = CreateItem();

            Assert.That(group.ModifyMonitoring(SessionlessContext(), item.Object), Is.False);
        }

        [Test]
        public void ApplyChangesWithoutItemsReportsGroupEmpty()
        {
            var identity = new Mock<IUserIdentity>();
            using SamplingGroup group = CreateGroup(identity.Object);

            Assert.That(group.ApplyChanges(), Is.True);
        }

        [Test]
        public void ShutdownIsSafeWhenNotStarted()
        {
            var identity = new Mock<IUserIdentity>();
            using SamplingGroup group = CreateGroup(identity.Object);

            Assert.DoesNotThrow(group.Shutdown);
        }
    }
}
