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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Server.Tests.RuntimeNodeSet
{
    /// <summary>
    /// Unit tests for the <see cref="RuntimeNodeSetOptions.ConfigureAsync"/>
    /// async-lifetime hook: ordering with the synchronous
    /// <see cref="RuntimeNodeSetOptions.Configure"/> callback, single-seal
    /// semantics, and the generation-owner disposal guarantees provided by
    /// <see cref="RuntimeNodeSetNodeManager.DeleteAddressSpaceAsync"/>.
    /// </summary>
    [TestFixture]
    [Category("RuntimeNodeSet")]
    [Parallelizable(ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public sealed class RuntimeNodeSetConfigureAsyncTests
    {
        private const string kUriA = "urn:test:ConfigureAsyncModel";

        private static readonly string[] s_expectedOrder = ["sync", "async"];

        private sealed class TrackingAsyncDisposable : IAsyncDisposable
        {
            public bool Disposed { get; private set; }

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                return default;
            }
        }

        private static StreamRuntimeNodeSetSource MakeStreamSource(string modelUri)
        {
            string xml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">\r\n" +
                "  <NamespaceUris>\r\n" +
                "    <Uri>" +
                modelUri +
                "</Uri>\r\n" +
                "  </NamespaceUris>\r\n" +
                "  <Models>\r\n" +
                "    <Model ModelUri=\"" +
                modelUri +
                "\" />\r\n" +
                "  </Models>\r\n" +
                "  <UAObject NodeId=\"ns=1;i=1000\" BrowseName=\"1:ConfigureAsyncRoot\">\r\n" +
                "    <DisplayName>ConfigureAsyncRoot</DisplayName>\r\n" +
                "    <References>\r\n" +
                "      <Reference ReferenceType=\"i=40\">i=58</Reference>\r\n" +
                "      <Reference ReferenceType=\"i=35\" IsForward=\"false\">i=85</Reference>\r\n" +
                "    </References>\r\n" +
                "  </UAObject>\r\n" +
                "</UANodeSet>";
            return RuntimeNodeSetSource.FromStream(
                modelUri,
                _ => new ValueTask<Stream>(
                    new MemoryStream(Encoding.UTF8.GetBytes(xml))),
                [modelUri]);
        }

        /// <summary>
        /// Creates an <see cref="IServerInternal"/> mock with the namespace
        /// table, type tree, and master node manager scaffolding needed to
        /// exercise <see cref="RuntimeNodeSetNodeManager.CreateAddressSpaceAsync"/>
        /// and <see cref="RuntimeNodeSetNodeManager.DeleteAddressSpaceAsync"/>
        /// end to end.
        /// </summary>
        private static Mock<IServerInternal> BuildMockServer()
        {
            ILoggerFactory mockLoggerFactory = LoggerFactory.Create(b => b.AddDebug());
            var mockTelemetry = new Mock<ITelemetryContext>();
            mockTelemetry.SetupGet(t => t.LoggerFactory).Returns(mockLoggerFactory);

            var namespaceTable = new NamespaceTable();

            var mockConfigurationNodeManager = new Mock<IConfigurationNodeManager>();
            var mockMasterNodeManager = new Mock<IMasterNodeManager>();
            mockMasterNodeManager
                .SetupGet(m => m.ConfigurationNodeManager)
                .Returns(mockConfigurationNodeManager.Object);

            var mockServer = new Mock<IServerInternal>();
            mockServer.SetupGet(s => s.Telemetry).Returns(mockTelemetry.Object);
            mockServer.SetupGet(s => s.NamespaceUris).Returns(namespaceTable);
            mockServer.SetupGet(s => s.ServerUris).Returns(new StringTable());
            mockServer.SetupGet(s => s.TypeTree).Returns(new TypeTable(namespaceTable));
            mockServer.SetupGet(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.SetupGet(s => s.NodeManager).Returns(mockMasterNodeManager.Object);
            mockServer.SetupGet(s => s.MonitoredItemQueueFactory)
                .Returns(new MonitoredItemQueueFactory(mockTelemetry.Object));

            var systemContext = new ServerSystemContext(mockServer.Object);
            mockServer.SetupGet(s => s.DefaultSystemContext).Returns(systemContext);

            return mockServer;
        }

        /// <summary>
        /// Asserts that <paramref name="builder"/> has not been sealed yet by
        /// probing an unresolvable NodeId: an unsealed builder fails
        /// resolution with <see cref="StatusCodes.BadNodeIdUnknown"/>, while a
        /// sealed builder instead fails with
        /// <see cref="StatusCodes.BadInvalidState"/> before resolution is even
        /// attempted.
        /// </summary>
        private static void AssertBuilderNotSealed(INodeManagerBuilder builder)
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => builder.Node(new NodeId(Guid.NewGuid())));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// Asserts that <paramref name="builder"/> has been sealed: any
        /// further <c>Node(...)</c> call must fail with
        /// <see cref="StatusCodes.BadInvalidState"/>.
        /// </summary>
        private static void AssertBuilderSealed(INodeManagerBuilder builder)
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => builder.Node(new NodeId(Guid.NewGuid())));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        private static async Task<IAsyncNodeManager> CreateManagerAsync(
            Action<INodeManagerBuilder> configure = null,
            Func<INodeManagerBuilder, CancellationToken, ValueTask<IAsyncDisposable>> configureAsync = null)
        {
            StreamRuntimeNodeSetSource source = MakeStreamSource(kUriA);
            var factory = new RuntimeNodeSetNodeManagerFactory(new RuntimeNodeSetOptions
            {
                Sources = [source],
                DefaultNamespaceUri = kUriA,
                Configure = configure,
                ConfigureAsync = configureAsync
            });

            return await factory.CreateAsync(
                BuildMockServer().Object,
                new ApplicationConfiguration(),
                CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// A sync-only <see cref="RuntimeNodeSetOptions.Configure"/> callback
        /// keeps working exactly as before: it runs, the builder is sealed
        /// once, and no generation owner is tracked.
        /// </summary>
        [Test]
        public async Task SyncOnlyConfigureStillRunsAndSealsAsync()
        {
            bool configureRan = false;
            INodeManagerBuilder capturedBuilder = null;

            IAsyncNodeManager manager = await CreateManagerAsync(
                configure: builder =>
                {
                    configureRan = true;
                    capturedBuilder = builder;
                }).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalReferences, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(configureRan, Is.True);
            Assert.That(capturedBuilder, Is.Not.Null);

            AssertBuilderSealed(capturedBuilder!);

            await manager.DeleteAddressSpaceAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// An async-only <see cref="RuntimeNodeSetOptions.ConfigureAsync"/>
        /// callback runs before the builder is sealed: wiring calls made
        /// from inside the callback must succeed.
        /// </summary>
        [Test]
        public async Task AsyncOnlyConfigureRunsBeforeSealAsync()
        {
            bool configureAsyncRan = false;
            INodeManagerBuilder capturedBuilder = null;

            IAsyncNodeManager manager = await CreateManagerAsync(
                configureAsync: async (builder, ct) =>
                {
                    await Task.Yield();
                    capturedBuilder = builder;
                    AssertBuilderNotSealed(builder);
                    configureAsyncRan = true;
                    return null;
                }).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalReferences, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(configureAsyncRan, Is.True);

            AssertBuilderSealed(capturedBuilder!);

            await manager.DeleteAddressSpaceAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// When both callbacks are set, <c>Configure</c> runs first, then
        /// <c>ConfigureAsync</c>, both against the same unsealed builder,
        /// with a single <c>Seal()</c> applied once after both complete.
        /// </summary>
        [Test]
        public async Task BothConfigureCallbacksRunInOrderWithSingleSealAsync()
        {
            var order = new List<string>();
            INodeManagerBuilder capturedBuilder = null;

            IAsyncNodeManager manager = await CreateManagerAsync(
                configure: builder =>
                {
                    order.Add("sync");
                    capturedBuilder = builder;
                    AssertBuilderNotSealed(builder);
                },
                configureAsync: async (builder, ct) =>
                {
                    await Task.Yield();
                    order.Add("async");
                    Assert.That(builder, Is.SameAs(capturedBuilder));
                    // Still unsealed: the sync callback must not have sealed it.
                    AssertBuilderNotSealed(builder);
                    return null;
                }).ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalReferences, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(order, Is.EqualTo(s_expectedOrder));

            AssertBuilderSealed(capturedBuilder!);

            await manager.DeleteAddressSpaceAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// The generation owner returned by <c>ConfigureAsync</c> is
        /// disposed asynchronously when the manager's address space is
        /// torn down via <see cref="RuntimeNodeSetNodeManager.DeleteAddressSpaceAsync"/>
        /// (the normal-removal path).
        /// </summary>
        [Test]
        public async Task OwnerDisposedOnDeleteAddressSpaceAsync()
        {
            await using TrackingAsyncDisposable owner = new();

            IAsyncNodeManager manager = await CreateManagerAsync(
                configureAsync: (_, _) => new ValueTask<IAsyncDisposable>(owner))
                .ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalReferences, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(owner.Disposed, Is.False);

            await manager.DeleteAddressSpaceAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(owner.Disposed, Is.True);
        }

        /// <summary>
        /// If node-added notification fails after <c>ConfigureAsync</c>
        /// produced a generation owner, <c>CreateAddressSpaceAsync</c>
        /// disposes the owner before propagating the activation failure.
        /// </summary>
        [Test]
        public async Task OwnerDisposedWhenNodeAddedCallbackFailsAsync()
        {
            await using TrackingAsyncDisposable owner = new();

            IAsyncNodeManager manager = await CreateManagerAsync(
                configure: builder => builder
                    .Node("ConfigureAsyncRoot")
                    .OnNodeAdded((_, _) => throw new InvalidOperationException(
                        "Simulated node-added failure.")),
                configureAsync: (_, _) => new ValueTask<IAsyncDisposable>(owner))
                .ConfigureAwait(false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            InvalidOperationException exception = null;
            try
            {
                await manager.CreateAddressSpaceAsync(
                    externalReferences,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                exception = ex;
            }

            Assert.That(exception, Is.Not.Null);
            Assert.That(owner.Disposed, Is.True);

            await manager.DeleteAddressSpaceAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Each node-manager generation owns exactly one generation owner.
        /// Tearing down one generation (e.g. the old generation retired
        /// after a shadow reload) must not affect another, independently
        /// created generation's owner; that owner is only disposed when
        /// its own generation is retired.
        /// </summary>
        [Test]
        public async Task OwnerIsolatedAcrossGenerationsAndDisposedOnlyWhenRetiredAsync()
        {
            await using TrackingAsyncDisposable ownerOld = new();
            await using TrackingAsyncDisposable ownerNew = new();

            IAsyncNodeManager oldGeneration = await CreateManagerAsync(
                configureAsync: (_, _) => new ValueTask<IAsyncDisposable>(ownerOld))
                .ConfigureAwait(false);
            IAsyncNodeManager newGeneration = await CreateManagerAsync(
                configureAsync: (_, _) => new ValueTask<IAsyncDisposable>(ownerNew))
                .ConfigureAwait(false);

            var externalReferencesOld = new Dictionary<NodeId, IList<IReference>>();
            var externalReferencesNew = new Dictionary<NodeId, IList<IReference>>();
            await oldGeneration.CreateAddressSpaceAsync(externalReferencesOld, CancellationToken.None)
                .ConfigureAwait(false);
            await newGeneration.CreateAddressSpaceAsync(externalReferencesNew, CancellationToken.None)
                .ConfigureAwait(false);

            // Retiring the old generation (as happens once a shadow reload's
            // replacement generation has taken over all new requests) must
            // dispose only its own owner.
            await oldGeneration.DeleteAddressSpaceAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(ownerOld.Disposed, Is.True);
            Assert.That(ownerNew.Disposed, Is.False);

            await newGeneration.DeleteAddressSpaceAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(ownerNew.Disposed, Is.True);
        }
    }
}
