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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Language;
using NUnit.Framework;
using Opc.Ua;
using UaLens.NodeSets;
using UaLens.NodeSets.Loading;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.NodeSets
{
    [TestFixture]
    public sealed class NodeSetWorkspaceTests
    {
        [OneTimeSetUp]
        public async Task PrepareGraphAsync()
        {
            m_graph = await NodeSetAddressSpaceTests.CreateAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task SwitchesAllInspectorsToOfflineAndDisablesLiveActions()
        {
            var loader = new Mock<INodeSetLoader>(MockBehavior.Strict);
            loader.Setup(value => value.LoadAsync(
                It.IsAny<ArrayOf<string>>(), It.IsAny<INodeSetDependencyResolver>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(m_graph.Documents);
            var factory = new Mock<INodeSetAddressSpaceFactory>(MockBehavior.Strict);
            factory.Setup(value => value.CreateAsync(m_graph.Documents, It.IsAny<CancellationToken>()))
                .ReturnsAsync(m_graph);
            await using var vm = new MainViewModel(
                NodeSetAddressSpaceTests.Telemetry(), dispatcher: InlineWorkspaceDispatcher.Instance,
                nodeSetLoader: loader.Object, nodeSetFactory: factory.Object);
            await vm.OpenNodeSetsAsync(["app.xml"], Mock.Of<INodeSetDependencyResolver>()).ConfigureAwait(false);
            Assert.That(vm.IsOffline, Is.True);
            Assert.That(vm.IsConnected, Is.False);
            Assert.That(vm.Browser.OfflineSource, Is.SameAs(m_graph));
            Assert.That(vm.ShowAttributes && vm.ShowReferences, Is.True);
            ushort dependency = m_graph.NamespaceUris.GetIndexOrAppend("urn:lens:dependency");
            NodeViewModel selected = vm.Browser.ShowOfflineNode(new NodeId(2, dependency))!;
            await Task.WhenAll(
                vm.UpdateSelectionAsync(selected),
                vm.Attributes.LoadAsync(selected.NodeId, selected.NodeClass),
                vm.References.LoadAsync(selected.NodeId, selected.NodeClass)).ConfigureAwait(false);
            Assert.That(vm.Attributes.Rows.Any(row => row.Name == "Value" && row.Value == "42"), Is.True);
            Assert.That(vm.References.Rows.Any(row => row.TargetBrowseName == "Machine"), Is.True);
            Assert.That(
                vm.CanAddSelectedItem || vm.CanWriteVariable || vm.CanCallMethod || vm.SelectionHasEvents, Is.False);
            await vm.UpdateSelectionAsync(new NodeViewModel(
                vm.Browser, NodeId.Null, new NodeId(7, dependency), "Method", NodeClass.Method)).ConfigureAwait(false);
            Assert.That(vm.CanCallMethod, Is.False);
            await vm.CloseNodeSetsAsync().ConfigureAwait(false);
            Assert.That(vm.IsOffline, Is.False);
            Assert.That(vm.Browser.Roots, Is.Empty);
            Assert.That(vm.Attributes.Rows, Is.Empty);
            Assert.That(vm.References.Rows, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedOrCancelledReplacementKeepsPreviousGraph(bool cancel)
        {
            var loader = new Mock<INodeSetLoader>(MockBehavior.Strict);
            ISetupSequentialResult<Task<ArrayOf<NodeSetDocument>>> sequence = loader
                .SetupSequence(value => value.LoadAsync(
                    It.IsAny<ArrayOf<string>>(), It.IsAny<INodeSetDependencyResolver>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(m_graph.Documents);
            if (cancel)
            {
                sequence.ThrowsAsync(new OperationCanceledException());
            }
            else
            {
                sequence.ThrowsAsync(new FormatException("Wrong model URI."));
            }
            var factory = new Mock<INodeSetAddressSpaceFactory>(MockBehavior.Strict);
            factory.Setup(value => value.CreateAsync(m_graph.Documents, It.IsAny<CancellationToken>()))
                .ReturnsAsync(m_graph);
            await using var vm = new MainViewModel(
                NodeSetAddressSpaceTests.Telemetry(), dispatcher: InlineWorkspaceDispatcher.Instance,
                nodeSetLoader: loader.Object, nodeSetFactory: factory.Object);
            INodeSetDependencyResolver resolver = Mock.Of<INodeSetDependencyResolver>();
            await vm.OpenNodeSetsAsync(["app.xml"], resolver).ConfigureAwait(false);
            NodeViewModel originalRoot = vm.Browser.Roots[0];
            if (cancel)
            {
                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await vm.OpenNodeSetsAsync(["replacement.xml"], resolver).ConfigureAwait(false));
            }
            else
            {
                Assert.ThrowsAsync<FormatException>(async () =>
                    await vm.OpenNodeSetsAsync(["replacement.xml"], resolver).ConfigureAwait(false));
            }
            Assert.That(vm.OfflineAddressSpace, Is.SameAs(m_graph));
            Assert.That(vm.Browser.Roots[0], Is.SameAs(originalRoot));
            Assert.That(vm.IsLoadingNodeSets, Is.False);
            factory.Verify(value => value.CreateAsync(m_graph.Documents, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task DisposalCancelsAndDrainsAnInFlightImport()
        {
            var waiting = new TaskCompletionSource<ArrayOf<NodeSetDocument>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var loader = new Mock<INodeSetLoader>(MockBehavior.Strict);
            loader.Setup(value => value.LoadAsync(
                It.IsAny<ArrayOf<string>>(), It.IsAny<INodeSetDependencyResolver>(), It.IsAny<CancellationToken>()))
                .Returns<ArrayOf<string>, INodeSetDependencyResolver, CancellationToken>(
                    (_, _, cancellationToken) => waiting.Task.WaitAsync(cancellationToken));
            var vm = new MainViewModel(
                NodeSetAddressSpaceTests.Telemetry(), dispatcher: InlineWorkspaceDispatcher.Instance,
                nodeSetLoader: loader.Object);
            Task import = vm.OpenNodeSetsAsync(["waiting.xml"], Mock.Of<INodeSetDependencyResolver>());
            Assert.That(vm.IsLoadingNodeSets, Is.True);
            Assert.Throws<InvalidOperationException>(() =>
                vm.OpenNodeSetsAsync(["second.xml"], Mock.Of<INodeSetDependencyResolver>()));
            await vm.DisposeAsync().ConfigureAwait(false);
            Assert.That(import.IsCanceled, Is.True);
            Assert.That(vm.IsLoadingNodeSets, Is.False);
            Assert.That(vm.IsOffline, Is.False);
        }

        [Test]
        public void DependencyInjectionPreservesCustomLoaderRepositoryAndGraphFactory()
        {
            INodeSetLoader loader = Mock.Of<INodeSetLoader>();
            INodeSetRepository repository = Mock.Of<INodeSetRepository>();
            INodeSetAddressSpaceFactory factory = Mock.Of<INodeSetAddressSpaceFactory>();
            var services = new ServiceCollection();
            services.AddSingleton(loader);
            services.AddSingleton(repository);
            services.AddSingleton(factory);
            services.AddUaLens().AddUaLens();
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<INodeSetLoader>(), Is.SameAs(loader));
            Assert.That(provider.GetRequiredService<INodeSetRepository>(), Is.SameAs(repository));
            Assert.That(provider.GetRequiredService<INodeSetAddressSpaceFactory>(), Is.SameAs(factory));
            Assert.That(services.Count(value => value.ServiceType == typeof(INodeSetLoader)), Is.EqualTo(1));
        }

        private NodeSetAddressSpace m_graph = null!;
    }
}
