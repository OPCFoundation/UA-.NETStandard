/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test <see cref="MasterNodeManager"/>
    /// </summary>
    [TestFixture, Category("MasterNodeManager")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class MasterNodeManagerTests
    {
        #region Test Methods
        /// <summary>
        /// Test for registering a namespace manager for a namespace
        /// not contained in the server's namespace table
        /// </summary>
        [Test]
        public async Task RegisterNamespaceManagerNewNamespace()
        {
            IServiceCollection services = new ServiceCollection()
                .AddConfigurationServices()
                .AddServerServices()
                .AddSingleton<IStandardServer, StandardServer>();

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            var fixture = new ServerFixture<IStandardServer>(
                serviceProvider.GetRequiredService<IStandardServer>(),
                serviceProvider.GetRequiredService<IApplicationInstance>());

            try
            {
                //-- Arrange
                const string ns = "http://test.org/UA/Data/";

                var nodeManager = new Mock<INodeManager>();
                nodeManager.Setup(x => x.NamespaceUris).Returns(new List<string>());

                //-- Act
                IStandardServer server = await fixture.StartAsync(TestContext.Out).ConfigureAwait(false);
                var sut = new MasterNodeManager(
                    server.CurrentInstance,
                    fixture.Config,
                    serviceProvider.GetRequiredService<IMainNodeManagerFactory>(),
                    null,
                    nodeManager.Object);
                sut.RegisterNamespaceManager(ns, nodeManager.Object);

                //-- Assert
                Assert.Contains(ns, server.CurrentInstance.NamespaceUris.ToArray());
                INodeManager[] registeredManagers = sut.NamespaceManagers[server.CurrentInstance.NamespaceUris.GetIndex(ns)];
                Assert.AreEqual(1, registeredManagers.Length);
                Assert.Contains(nodeManager.Object, registeredManagers);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Test for registering a namespace manager for a namespace
        /// contained in the server's namespace table
        /// </summary>
        [Test]
        public async Task RegisterNamespaceManagerExistingNamespace()
        {
            IServiceCollection services = new ServiceCollection()
                .AddConfigurationServices()
                .AddServerServices()
                .AddSingleton<IStandardServer, StandardServer>();

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            var fixture = new ServerFixture<IStandardServer>(
                serviceProvider.GetRequiredService<IStandardServer>(),
                serviceProvider.GetRequiredService<IApplicationInstance>());

            try
            {
                //-- Arrange
                const string ns = "http://test.org/UA/Data/";
                var namespaceUris = new List<string> { ns };

                var originalNodeManager = new Mock<INodeManager>();
                originalNodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                var newNodeManager = new Mock<INodeManager>();
                newNodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                //-- Act
                IStandardServer server = await fixture.StartAsync(TestContext.Out).ConfigureAwait(false);
                var sut = new MasterNodeManager(
                    server.CurrentInstance,
                    fixture.Config,
                    serviceProvider.GetRequiredService<IMainNodeManagerFactory>(),
                    null,
                    originalNodeManager.Object);
                sut.RegisterNamespaceManager(ns, newNodeManager.Object);

                //-- Assert
                Assert.Contains(ns, server.CurrentInstance.NamespaceUris.ToArray());
                INodeManager[] registeredManagers = sut.NamespaceManagers[server.CurrentInstance.NamespaceUris.GetIndex(ns)];
                Assert.AreEqual(2, registeredManagers.Length);
                Assert.Contains(originalNodeManager.Object, registeredManagers);
                Assert.Contains(newNodeManager.Object, registeredManagers);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Test for unregistering a namespace manager which had previously
        /// been registered
        /// </summary>
        [Test]
        [TestCase(3, 0)]
        [TestCase(3, 1)]
        [TestCase(3, 2)]
        public async Task UnregisterNamespaceManagerInCollection(int totalManagers, int indexToRemove)
        {
            IServiceCollection services = new ServiceCollection()
                .AddConfigurationServices()
                .AddServerServices()
                .AddSingleton<IStandardServer, StandardServer>();

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            var fixture = new ServerFixture<IStandardServer>(
                serviceProvider.GetRequiredService<IStandardServer>(),
                serviceProvider.GetRequiredService<IApplicationInstance>());

            try
            {
                //-- Arrange
                const string ns = "http://test.org/UA/Data/";
                var namespaceUris = new List<string> { ns };

                var additionalManagers = new INodeManager[totalManagers];
                for (int ii = 0; ii < totalManagers; ii++)
                {
                    var nodeManager = new Mock<INodeManager>();
                    nodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                    additionalManagers[ii] = nodeManager.Object;
                }

                INodeManager nodeManagerToRemove = additionalManagers[indexToRemove];

                //-- Act
                IStandardServer server = await fixture.StartAsync(TestContext.Out).ConfigureAwait(false);
                var sut = new MasterNodeManager(
                    server.CurrentInstance,
                    fixture.Config,
                    serviceProvider.GetRequiredService<IMainNodeManagerFactory>(),
                    null,
                    additionalManagers);
                bool result = sut.UnregisterNamespaceManager(ns, nodeManagerToRemove);

                //-- Assert
                Assert.IsTrue(result);
                Assert.Contains(ns, server.CurrentInstance.NamespaceUris.ToArray());
                INodeManager[] registeredManagers = sut.NamespaceManagers[server.CurrentInstance.NamespaceUris.GetIndex(ns)];
                Assert.AreEqual(totalManagers - 1, registeredManagers.Length);
                NUnit.Framework.Assert.That(registeredManagers, Has.No.Member(nodeManagerToRemove));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Test for unregistering a namespace manager which had not
        /// previously been registered
        /// </summary>
        [Test]
        public async Task UnregisterNamespaceManagerNotInCollection()
        {
            IServiceCollection services = new ServiceCollection()
                .AddConfigurationServices()
                .AddServerServices()
                .AddSingleton<IStandardServer, StandardServer>();

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            var fixture = new ServerFixture<IStandardServer>(
                serviceProvider.GetRequiredService<IStandardServer>(),
                serviceProvider.GetRequiredService<IApplicationInstance>());

            try
            {
                //-- Arrange
                const string ns = "http://test.org/UA/Data/";
                var namespaceUris = new List<string> { ns };

                var firstNodeManager = new Mock<INodeManager>();
                firstNodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                var secondNodeManager = new Mock<INodeManager>();
                secondNodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                var thirdNodeManager = new Mock<INodeManager>();
                thirdNodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                //-- Act
                IStandardServer server = await fixture.StartAsync(TestContext.Out).ConfigureAwait(false);
                var sut = new MasterNodeManager(
                    server.CurrentInstance,
                    fixture.Config,
                    serviceProvider.GetRequiredService<IMainNodeManagerFactory>(),
                    null,
                    firstNodeManager.Object,
                    // Do not add the secondNodeManager to additionalManagers
                    thirdNodeManager.Object);
                bool result = sut.UnregisterNamespaceManager(ns, secondNodeManager.Object);

                //-- Assert
                Assert.IsFalse(result);
                Assert.Contains(ns, server.CurrentInstance.NamespaceUris.ToArray());
                INodeManager[] registeredManagers = sut.NamespaceManagers[server.CurrentInstance.NamespaceUris.GetIndex(ns)];
                Assert.AreEqual(2, registeredManagers.Length);
                Assert.Contains(firstNodeManager.Object, registeredManagers);
                Assert.Contains(thirdNodeManager.Object, registeredManagers);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Test for unregistering a namespace manager which had not
        /// previously been registered and is for a namespace
        /// which is unknown by the server
        /// </summary>
        [Test]
        public async Task UnregisterNamespaceManagerUnknownNamespace()
        {
            IServiceCollection services = new ServiceCollection()
                .AddConfigurationServices()
                .AddServerServices()
                .AddSingleton<IStandardServer, StandardServer>();

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            var fixture = new ServerFixture<IStandardServer>(
                serviceProvider.GetRequiredService<IStandardServer>(),
                serviceProvider.GetRequiredService<IApplicationInstance>());

            try
            {
                //-- Arrange
                const string originalNs = "http://test.org/UA/Data/";

                var originalNodeManager = new Mock<INodeManager>();
                originalNodeManager.Setup(x => x.NamespaceUris).Returns(new List<string> { originalNs });

                const string newNs = "http://test.org/UA/Data/Instance";
                var newNodeManager = new Mock<INodeManager>();
                newNodeManager.Setup(x => x.NamespaceUris).Returns(new List<string> { originalNs, newNs });

                //-- Act
                IStandardServer server = await fixture.StartAsync(TestContext.Out).ConfigureAwait(false);
                var sut = new MasterNodeManager(
                    server.CurrentInstance,
                    fixture.Config,
                    serviceProvider.GetRequiredService<IMainNodeManagerFactory>(),
                    null,
                    originalNodeManager.Object);
                bool result = sut.UnregisterNamespaceManager(newNs, newNodeManager.Object);

                //-- Assert
                Assert.IsFalse(result);
                NUnit.Framework.Assert.That(server.CurrentInstance.NamespaceUris.ToArray(), Has.No.Member(newNs));

                Assert.Contains(originalNs, server.CurrentInstance.NamespaceUris.ToArray());
                INodeManager[] registeredManagers = sut.NamespaceManagers[server.CurrentInstance.NamespaceUris.GetIndex(originalNs)];
                Assert.AreEqual(1, registeredManagers.Length);
                Assert.Contains(originalNodeManager.Object, registeredManagers);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }
        #endregion
    }
}
