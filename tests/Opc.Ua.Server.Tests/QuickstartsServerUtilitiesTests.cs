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
