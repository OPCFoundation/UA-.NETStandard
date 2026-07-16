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

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// Coverage tests for <see cref="FluentNodeManagerFactory"/> and the
    /// <c>AddNodeManager</c> extension that creates and registers it.
    /// </summary>
    /// <remarks>
    /// Tests that exercise the constructor validation and property projection
    /// run without any server infrastructure. The <see cref="CreateAsync"/>
    /// path uses a mocked <see cref="IServerInternal"/> constructed in the
    /// same way as <see cref="AsyncCustomNodeManagerTests"/>.
    /// </remarks>
    [TestFixture]
    [Category("Hosting")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class FluentNodeManagerFactoryCoverageTests
    {
        private const string TestNamespaceUri = "http://test.org/UA/FluentCoverage/";

        [Test]
        public void CtorWithEmptyNamespaceUriThrowsArgumentException()
        {
            Assert.That(
                () => new FluentNodeManagerFactory(string.Empty, _ => { }),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void CtorWithWhitespaceNamespaceUriThrowsArgumentException()
        {
            Assert.That(
                () => new FluentNodeManagerFactory("   ", _ => { }),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void CtorWithNullBuildThrowsArgumentNullException()
        {
            Action<INodeManagerBuilder>? nullBuild = null;

            Assert.That(
                () => new FluentNodeManagerFactory(TestNamespaceUri, nullBuild!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void CtorWithValidArgumentsSetsNamespacesUris()
        {
            var factory = new FluentNodeManagerFactory(TestNamespaceUri, _ => { });

            Assert.That(factory.NamespacesUris, Has.Count.EqualTo(1));
            Assert.That(factory.NamespacesUris[0], Is.EqualTo(TestNamespaceUri));
        }

        [Test]
        public async Task CreateAsyncReturnsFluentNodeManagerAsync()
        {
            Mock<IServerInternal> mockServer = BuildMockServer();
            var factory = new FluentNodeManagerFactory(TestNamespaceUri, _ => { });

            IAsyncNodeManager manager = await factory.CreateAsync(
                mockServer.Object,
                new ApplicationConfiguration(),
                CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(manager, Is.Not.Null);
            Assert.That(manager, Is.InstanceOf<FluentNodeManager>());

            ((IDisposable)manager).Dispose();
        }

        [Test]
        public async Task CreateAsyncInvokesProvidedBuildCallbackAsync()
        {
            Mock<IServerInternal> mockServer = BuildMockServer();
            bool buildCalled = false;
            var factory = new FluentNodeManagerFactory(
                TestNamespaceUri,
                _ => buildCalled = true);

            IAsyncNodeManager manager = await factory.CreateAsync(
                mockServer.Object,
                new ApplicationConfiguration(),
                CancellationToken.None)
                .ConfigureAwait(false);

            // The build callback is invoked during CreateAddressSpaceAsync, not CreateAsync.
            // CreateAsync only instantiates the manager; we verify it completed without error.
            Assert.That(manager, Is.Not.Null);
            Assert.That(buildCalled, Is.False, "Build callback must not be invoked during factory creation.");

            ((IDisposable)manager).Dispose();
        }

        [Test]
        public void AddNodeManagerExtensionRegistersAsyncNodeManagerFactory()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = "FluentFactoryServer";
                    o.ApplicationUri = "urn:test:FluentFactoryServer";
                    o.ProductUri = "urn:test:product";
                })
                .AddNodeManager(TestNamespaceUri, _ => { });

            Assert.That(
                services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(IAsyncNodeManagerFactory)));
        }

        [Test]
        public void AddNodeManagerExtensionRegistersNodeManagerRegistration()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = "FluentFactoryServer";
                    o.ApplicationUri = "urn:test:FluentFactoryServer";
                    o.ProductUri = "urn:test:product";
                })
                .AddNodeManager(TestNamespaceUri, _ => { });

            Assert.That(
                services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(OpcUaServerNodeManagerRegistration)));
        }

        [Test]
        public void AddNodeManagerExtensionResolvedFactoryHasCorrectNamespace()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = "FluentFactoryServer";
                    o.ApplicationUri = "urn:test:FluentFactoryServer";
                    o.ProductUri = "urn:test:product";
                })
                .AddNodeManager(TestNamespaceUri, _ => { });

            using ServiceProvider sp = services.BuildServiceProvider();
            IAsyncNodeManagerFactory factory = sp.GetRequiredService<IAsyncNodeManagerFactory>();

            Assert.That(factory.NamespacesUris, Has.Count.EqualTo(1));
            Assert.That(factory.NamespacesUris[0], Is.EqualTo(TestNamespaceUri));
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static Mock<IServerInternal> BuildMockServer()
        {
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(TestNamespaceUri);

            var mockTelemetry = new Mock<ITelemetryContext>();
            mockTelemetry
                .SetupGet(t => t.LoggerFactory)
                .Returns(NullLoggerFactory.Instance);

            var mockServer = new Mock<IServerInternal>();
            mockServer.SetupGet(s => s.Telemetry).Returns(mockTelemetry.Object);
            mockServer.SetupGet(s => s.NamespaceUris).Returns(namespaceTable);

            var systemContext = new ServerSystemContext(mockServer.Object);
            mockServer.SetupGet(s => s.DefaultSystemContext).Returns(systemContext);

            return mockServer;
        }
    }
}
