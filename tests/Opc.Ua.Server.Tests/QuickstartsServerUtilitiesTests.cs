// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Test;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests the reusable Quickstarts server helpers.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    public class QuickstartsServerUtilitiesTests
    {
        /// <summary>
        /// Registers the classic and asynchronous Quickstarts factories.
        /// </summary>
        [Test]
        public void AddDefaultNodeManagersRegistersClassicAndAsyncFactories()
        {
            var server = new Mock<IStandardServer>();

            Quickstarts.Servers.Utils.AddDefaultNodeManagers(server.Object);

            server.Verify(
                s => s.AddNodeManager(It.Is<INodeManagerFactory>(
                    factory => factory is MemoryBuffer.MemoryBufferNodeManagerFactory)),
                Times.Once);
            server.Verify(
                s => s.AddNodeManager(It.Is<INodeManagerFactory>(
                    factory => factory is Boiler.BoilerNodeManagerFactory)),
                Times.Once);
            server.Verify(
                s => s.AddNodeManager(It.Is<IAsyncNodeManagerFactory>(
                    factory => factory is TestData.TestDataNodeManagerFactory)),
                Times.Once);
            server.Verify(
                s => s.AddNodeManager(It.Is<IAsyncNodeManagerFactory>(
                    factory => factory is global::Alarms.AlarmNodeManagerFactory)),
                Times.Once);
        }

        /// <summary>
        /// Exposes each factory through the interface that its node manager supports.
        /// </summary>
        [Test]
        public void DefaultNodeManagerFactoriesPreserveClassicAndAsyncCompatibility()
        {
            ArrayOf<INodeManagerFactory> classicFactories =
                Quickstarts.Servers.Utils.NodeManagerFactories;
            ArrayOf<IAsyncNodeManagerFactory> asyncFactories =
                Quickstarts.Servers.Utils.AsyncNodeManagerFactories;

            Assert.That(
                classicFactories,
                Has.Count.EqualTo(2));
            Assert.That(
                classicFactories[0],
                Is.TypeOf<MemoryBuffer.MemoryBufferNodeManagerFactory>());
            Assert.That(
                classicFactories[1],
                Is.TypeOf<Boiler.BoilerNodeManagerFactory>());
            Assert.That(
                asyncFactories,
                Has.Count.EqualTo(2));
            Assert.That(
                asyncFactories[0],
                Is.TypeOf<TestData.TestDataNodeManagerFactory>());
            Assert.That(
                asyncFactories[1],
                Is.TypeOf<global::Alarms.AlarmNodeManagerFactory>());
        }

        /// <summary>
        /// CTT mode shortens the alarm simulation step of the shared alarm factory and leaves the other
        /// factories alone.
        /// </summary>
        [Test]
        [NonParallelizable]
        public void ConfigureCttModeNodeManagersShortensAlarmSimulationInterval()
        {
            ArrayOf<IAsyncNodeManagerFactory> asyncFactories =
                Quickstarts.Servers.Utils.AsyncNodeManagerFactories;
            var alarmFactory = (global::Alarms.AlarmNodeManagerFactory)asyncFactories[1];
            int original = alarmFactory.SimulationInterval;
            try
            {
                Assert.That(
                    original,
                    Is.EqualTo(global::Alarms.AlarmNodeManager.DefaultSimulationInterval));

                Quickstarts.Servers.Utils.ConfigureCttModeNodeManagers();

                Assert.That(
                    alarmFactory.SimulationInterval,
                    Is.EqualTo(Quickstarts.Servers.Utils.CttAlarmSimulationInterval));
                Assert.That(
                    Quickstarts.Servers.Utils.CttAlarmSimulationInterval,
                    Is.LessThan(global::Alarms.AlarmNodeManager.DefaultSimulationInterval));
                ArrayOf<IAsyncNodeManagerFactory> after = Quickstarts.Servers.Utils.AsyncNodeManagerFactories;
                Assert.That(after, Has.Count.EqualTo(2));
                Assert.That(after[0], Is.SameAs(asyncFactories[0]));
                Assert.That(after[1], Is.SameAs(alarmFactory));
            }
            finally
            {
                alarmFactory.SimulationInterval = original;
            }
        }

        /// <summary>
        /// Enables the optional Reference Server behaviors without starting the server.
        /// </summary>
        [Test]
        public void ReferenceServerHelpersEnableRequestedBehaviors()
        {
            using var server = new ReferenceServer(NUnitTelemetryContext.Create());

            Quickstarts.Servers.Utils.UseSamplingGroupsInReferenceNodeManager(server);
            Quickstarts.Servers.Utils.EnableProvisioningMode(server);

            Assert.That(server.UseSamplingGroupsInReferenceNodeManager, Is.True);
            Assert.That(server.ProvisioningMode, Is.True);
        }
    }
}
