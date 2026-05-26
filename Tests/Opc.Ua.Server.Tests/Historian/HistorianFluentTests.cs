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

// CA2000: test code; disposables are ownership-transferred to test fixtures or are short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000

using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

#nullable enable

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Validates the fluent historian extensions that bridge the
    /// <see cref="INodeManagerBuilder"/> surface to the Part 11 historian
    /// provider model. Confirms that <c>UseHistorian()</c> reuses a
    /// per-builder <see cref="HistorianBuilder"/>, that
    /// <c>Historize()</c> sets the historizing flags and registers the
    /// variable with the server-wide
    /// <see cref="IHistorianProviderRegistry"/>, that
    /// <c>WithHistorian()</c> binds a per-node provider that takes
    /// precedence over the default, and that the bare "just works"
    /// <c>Historize()</c> path lazily installs an in-memory engine.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.None)] // ConditionalWeakTable keyed by builder; per-test fixture
    public class HistorianFluentTests
    {
        private const ushort kNs = 2;

        [Test]
        public void UseHistorianReturnsSameBuilderAcrossCalls()
        {
            (NodeManagerBuilder b, _) = CreateBuilderWithVariable();

            HistorianBuilder first = b.UseHistorian();
            HistorianBuilder second = b.UseHistorian();

            Assert.That(first, Is.SameAs(second));
        }

        [Test]
        public void HistorizeWithoutPriorUseHistorianLazilyCreatesInMemoryProvider()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            IServerInternal server = ((ServerSystemContext)b.Context).Server;
            IHistorianProviderRegistry registry = ((IHistorianRegistryProvider)server).HistorianRegistry;

            // Sanity: nothing registered before.
            Assert.That(registry.Providers, Has.Count.EqualTo(0));

            IVariableBuilder<int> view = b.Variable<int>(v.NodeId).Historize();

            Assert.That(view, Is.Not.Null);
            Assert.That(registry.Providers, Has.Count.EqualTo(1));
            IHistorianProvider? resolved = registry.Resolve(v.NodeId);
            Assert.That(resolved, Is.InstanceOf<InMemoryHistorianProvider>(),
                "Bare Historize() should lazily install an in-memory provider as the default.");
        }

        [Test]
        public void HistorizeSetsHistorizingAndAccessLevels()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            v.AccessLevel = AccessLevels.CurrentRead;
            v.UserAccessLevel = AccessLevels.CurrentRead;

            b.Variable<int>(v.NodeId).Historize();

            Assert.That(v.Historizing, Is.True);
            Assert.That((byte)(v.AccessLevel & AccessLevels.HistoryRead),
                Is.EqualTo(AccessLevels.HistoryRead));
            Assert.That((byte)(v.AccessLevel & AccessLevels.HistoryWrite),
                Is.EqualTo(AccessLevels.HistoryWrite));
            Assert.That((byte)(v.UserAccessLevel & AccessLevels.HistoryRead),
                Is.EqualTo(AccessLevels.HistoryRead));
        }

        [Test]
        public void HistorizeRespectsCustomAccessLevel()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            v.AccessLevel = AccessLevels.CurrentRead;
            v.UserAccessLevel = AccessLevels.CurrentRead;

            b.Variable<int>(v.NodeId).Historize(historyAccessLevel: AccessLevels.HistoryRead);

            Assert.That((byte)(v.AccessLevel & AccessLevels.HistoryRead),
                Is.EqualTo(AccessLevels.HistoryRead));
            Assert.That((byte)(v.AccessLevel & AccessLevels.HistoryWrite),
                Is.Zero,
                "HistoryWrite must not be set when only HistoryRead was requested.");
        }

        [Test]
        public void UseHistorianFluentChainRegistersExplicitProvider()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            IServerInternal server = ((ServerSystemContext)b.Context).Server;
            IHistorianProviderRegistry registry = ((IHistorianRegistryProvider)server).HistorianRegistry;

            using var customProvider = new InMemoryHistorianProvider();
            b.UseHistorian().UseProvider(customProvider).RegisterAsDefault();
            b.Variable<int>(v.NodeId).Historize();

            Assert.That(registry.Resolve(v.NodeId), Is.SameAs(customProvider));
        }

        [Test]
        public void WithHistorianBindsPerNodeAndTakesPrecedence()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            IServerInternal server = ((ServerSystemContext)b.Context).Server;
            IHistorianProviderRegistry registry = ((IHistorianRegistryProvider)server).HistorianRegistry;

            using var defaultProvider = new InMemoryHistorianProvider();
            using var specialProvider = new InMemoryHistorianProvider();

            b.UseHistorian().UseProvider(defaultProvider).RegisterAsDefault();
            b.Variable<int>(v.NodeId)
                .WithHistorian(specialProvider)
                .Historize();

            // Per-node binding wins over the default.
            Assert.That(registry.Resolve(v.NodeId), Is.SameAs(specialProvider));
        }

        [Test]
        public void HistorizeWithExplicitProviderArgumentBindsPerNode()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            IServerInternal server = ((ServerSystemContext)b.Context).Server;
            IHistorianProviderRegistry registry = ((IHistorianRegistryProvider)server).HistorianRegistry;

            using var perCallProvider = new InMemoryHistorianProvider();
            b.Variable<int>(v.NodeId).Historize(provider: perCallProvider);

            Assert.That(registry.Resolve(v.NodeId), Is.SameAs(perCallProvider));
            Assert.That(v.Historizing, Is.True);
        }

        [Test]
        public void HistorizeWithCapabilitiesAdvertisedByProvider()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            var custom = new HistorianNodeCapabilities
            {
                InsertData = true,
                ReplaceData = true,
                DeleteRaw = false,
                InsertAnnotation = true,
                ServerTimestampSupported = true,
            };

            b.Variable<int>(v.NodeId).Historize(capabilities: custom);

            IServerInternal server = ((ServerSystemContext)b.Context).Server;
            IHistorianProvider? provider =
                ((IHistorianRegistryProvider)server).HistorianRegistry.Resolve(v.NodeId);
            Assert.That(provider, Is.Not.Null);
            HistorianNodeCapabilities advertised = provider!
                .GetCapabilitiesAsync(v.NodeId, default).AsTask().GetAwaiter().GetResult();
            Assert.That(advertised.InsertAnnotation, Is.True);
            Assert.That(advertised.DeleteRaw, Is.False,
                "Provider should advertise the capability set the user supplied verbatim.");
        }

        [Test]
        public void HistorizeAlsoWorksFromUntypedNodeBuilder()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();

            INodeBuilder<BaseVariableState> view = b.Node<BaseVariableState>(v.NodeId);
            view.Historize();

            Assert.That(v.Historizing, Is.True);
        }

        [Test]
        public void HistorizeFailsCleanlyWhenServerLacksRegistry()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariableNoRegistry();

            Assert.That(
                () => b.Variable<int>(v.NodeId).Historize(),
                Throws.TypeOf<InvalidOperationException>());
        }

        private static (NodeManagerBuilder Builder, BaseDataVariableState Var) CreateBuilderWithVariable()
        {
            IServerInternal server = CreateServerWithRegistry();
            return CreateBuilderForServer(server);
        }

        private static (NodeManagerBuilder Builder, BaseDataVariableState Var) CreateBuilderWithVariableNoRegistry()
        {
            IServerInternal server = CreateServerWithoutRegistry();
            return CreateBuilderForServer(server);
        }

        private static (NodeManagerBuilder Builder, BaseDataVariableState Var) CreateBuilderForServer(IServerInternal server)
        {
            var ctx = new ServerSystemContext(server);
            var var1 = new BaseDataVariableState(parent: null)
            {
                NodeId = new NodeId("Var1", kNs),
                BrowseName = new QualifiedName("Var1", kNs),
                DisplayName = new LocalizedText("Var1"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };

            var roots = new Dictionary<QualifiedName, NodeState> { [var1.BrowseName] = var1 };
            var byId = new Dictionary<NodeId, NodeState> { [var1.NodeId] = var1 };

            var builder = new NodeManagerBuilder(
                ctx,
                nodeManager: Mock.Of<IAsyncNodeManager>(),
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState? n) ? n : null!,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState? n) ? n : null!,
                typeIdResolver: _ => []);
            return (builder, var1);
        }

        private static IServerInternal CreateServerWithRegistry()
        {
            var nsTable = new NamespaceTable();
            nsTable.Append("urn:test:fluent-historian");

            var mockTelemetry = new Mock<ITelemetryContext>();
            var registry = new HistorianProviderRegistry(nsTable);

            var mockServer = new Mock<IServerInternal>();
            mockServer.Setup(s => s.NamespaceUris).Returns(nsTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(nsTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);
            mockServer.As<IHistorianRegistryProvider>()
                .Setup(p => p.HistorianRegistry).Returns(registry);

            return mockServer.Object;
        }

        private static IServerInternal CreateServerWithoutRegistry()
        {
            var nsTable = new NamespaceTable();
            nsTable.Append("urn:test:fluent-noregistry");

            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockServer = new Mock<IServerInternal>();
            mockServer.Setup(s => s.NamespaceUris).Returns(nsTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(nsTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);
            return mockServer.Object;
        }
    }
}
