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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.NodeSets;
using UaLens.Tests.NodeSets;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.Presentation
{
    [TestFixture]
    public sealed class NamespaceHighlightTests
    {
        [OneTimeSetUp]
        public async Task CreateGraphAsync()
        {
            m_graph = await NodeSetAddressSpaceTests.CreateAsync().ConfigureAwait(false);
        }

        [Test]
        public void ServerOptionsIncludeCoreAndResetOnReplacementAndDisconnect()
        {
            Mock<ISession> session = Session("urn:first", "urn:second");
            ISession? current = session.Object;
            BrowserViewModel browser = Browser(() => current);
            Assert.That(browser.HasNamespaces, Is.False);
            browser.Reload();
            Assert.That(browser.NamespaceOptions.Select(option => option.NamespaceUri),
                Is.EqualTo([string.Empty, Namespaces.OpcUa, "urn:first", "urn:second"]));
            Assert.That(browser.NamespaceOptions[2].DisplayName, Is.EqualTo("1: urn:first"));
            var node = new NodeViewModel(browser, NodeId.Null, new NodeId(1, 1), "first", NodeClass.Object);
            browser.Roots.Add(node);
            browser.SelectedNamespace = browser.NamespaceOptions[2];
            Assert.That(node.IsNamespaceHighlighted, Is.True);

            current = Session("urn:replacement").Object;
            browser.Reload();
            Assert.That(browser.SelectedNamespace, Is.EqualTo(NamespaceOption.None));
            Assert.That(node.IsNamespaceHighlighted, Is.False);
            Assert.That(browser.NamespaceOptions[2].NamespaceUri, Is.EqualTo("urn:replacement"));
            current = null;
            browser.Reload();
            Assert.That(browser.NamespaceOptions, Is.EqualTo([NamespaceOption.None]));
            Assert.That(browser.HasNamespaces, Is.False);
            Assert.That(browser.Roots, Is.Empty);
        }

        [Test]
        public void SelectionNotifiesCollapsedDescendantsWithoutFilteringOrHighlightingPlaceholders()
        {
            Mock<ISession> session = Session("urn:first", "urn:second");
            BrowserViewModel browser = Browser(() => session.Object);
            browser.Reload();
            NodeViewModel root = browser.Roots[0];
            var first = new NodeViewModel(browser, root.NodeId, new NodeId(1, 1), "first", NodeClass.Object);
            var second = new NodeViewModel(browser, first.NodeId, new NodeId(2, 2), "second", NodeClass.Object);
            root.Children.Add(first);
            first.Children.Add(second);
            var changes = new List<bool>();
            second.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(NodeViewModel.IsNamespaceHighlighted))
                {
                    changes.Add(second.IsNamespaceHighlighted);
                }
            };
            browser.SelectedNamespace = browser.NamespaceOptions[3];
            Assert.That(second.IsNamespaceHighlighted, Is.True);
            Assert.That(first.IsNamespaceHighlighted, Is.False);
            Assert.That(first.IsExpanded, Is.False);
            browser.SelectedNamespace = browser.NamespaceOptions[2];
            Assert.That(first.IsNamespaceHighlighted, Is.True);
            Assert.That(second.IsNamespaceHighlighted, Is.False);
            Assert.That(changes, Is.EqualTo(s_highlightChanges));
            browser.SelectedNamespace = browser.NamespaceOptions[1];
            Assert.That(root.IsNamespaceHighlighted, Is.True);
            Assert.That(first.Children[0].IsPlaceholder, Is.True);
            Assert.That(first.Children[0].IsNamespaceHighlighted, Is.False);
            browser.SelectedNamespace = NamespaceOption.None;
            Assert.That(root.IsNamespaceHighlighted, Is.False);
            Assert.That(root.Children[0], Is.SameAs(first));
            Assert.That(first.Children[1], Is.SameAs(second));
        }

        [Test]
        public async Task PendingBrowseUsesTheLatestSelectionForNewChildrenAndNamespaceOptions()
        {
            Mock<ISession> session = Session("urn:first");
            BrowserViewModel browser = Browser(() => session.Object);
            browser.Reload();
            var parent = new NodeViewModel(browser, NodeId.Null, new NodeId(1, 1), "parent", NodeClass.Object);
            browser.Roots.Add(parent);
            var pending = new TaskCompletionSource<BrowseResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Setup(value => value.BrowseAsync(
                null, null, 0, It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<BrowseResponse>(pending.Task));
            Task loading = browser.LoadChildrenAsync(parent);
            browser.SelectedNamespace = browser.NamespaceOptions[2];
            session.Object.NamespaceUris.Append("urn:late");
            pending.SetResult(new BrowseResponse
            {
                Results =
                [
                    new BrowseResult
                    {
                        References =
                        [
                            new ReferenceDescription
                            {
                                NodeId = new ExpandedNodeId(3, 1),
                                BrowseName = new QualifiedName("different-browse-namespace", 2),
                                NodeClass = NodeClass.Variable
                            }
                        ]
                    }
                ]
            });
            await loading.ConfigureAwait(false);
            Assert.That(parent.Children.Single().IsNamespaceHighlighted, Is.True,
                "Highlight the NodeId namespace, not the BrowseName namespace.");
            Assert.That(browser.NamespaceOptions[3].NamespaceUri, Is.EqualTo("urn:late"));
            Assert.That(browser.SelectedNamespace!.NamespaceUri, Is.EqualTo("urn:first"));
        }

        [Test]
        public async Task RefreshAndViewChangesPreserveTheNamespaceSelection()
        {
            Mock<ISession> session = Session("urn:first");
            BrowserViewModel browser = Browser(() => session.Object);
            browser.Reload();
            browser.SelectedNamespace = browser.NamespaceOptions[1];
            NamespaceOption? selected = browser.SelectedNamespace;
            browser.Reload();
            Assert.That(browser.SelectedNamespace, Is.SameAs(selected));
            Assert.That(browser.Roots[0].IsNamespaceHighlighted, Is.True);
            await browser.SetViewKindAsync(BrowseViewKind.VariableTypes, CancellationToken.None).ConfigureAwait(false);
            Assert.That(browser.SelectedNamespace, Is.SameAs(selected));
            Assert.That(browser.Roots[0].IsNamespaceHighlighted, Is.True);
        }

        [Test]
        public async Task OfflineNamespacesUseCombinedIndexesForSearchRootsAndLazyChildren()
        {
            BrowserViewModel browser = Browser(() => null);
            browser.SetOfflineSource(m_graph);
            ushort dependency = m_graph.NamespaceUris.GetIndexOrAppend("urn:lens:dependency");
            ushort app = m_graph.NamespaceUris.GetIndexOrAppend("urn:lens:app");
            Assert.That(dependency, Is.Not.EqualTo(1), "The dependency file's authored namespace index is remapped.");
            Assert.That(browser.NamespaceOptions.Skip(1).Select(option => option.NamespaceUri),
                Is.EqualTo(m_graph.NamespaceUris.ToArray()));
            browser.SelectedNamespace = browser.NamespaceOptions.Single(
                option => option.NamespaceUri == "urn:lens:dependency");
            NodeViewModel machine = browser.ShowOfflineNode(new NodeId(1, app))!;
            Assert.That(machine.IsNamespaceHighlighted, Is.False);
            await browser.LoadChildrenAsync(machine).ConfigureAwait(false);
            Assert.That(machine.Children.Single(node => node.Text == "Reading").IsNamespaceHighlighted, Is.True);
            NodeViewModel reading = browser.ShowOfflineNode(new NodeId(2, dependency))!;
            Assert.That(reading.IsNamespaceHighlighted, Is.True);
            await browser.SetViewKindAsync(BrowseViewKind.AllNodes, CancellationToken.None).ConfigureAwait(false);
            NodeViewModel group = browser.Roots.Single(node => node.IndexNamespace == dependency);
            Assert.That(group.IsNamespaceHighlighted, Is.False, "Namespace grouping headers are not UA nodes.");
            await browser.LoadChildrenAsync(group).ConfigureAwait(false);
            Assert.That(group.Children.All(node => node.IsNamespaceHighlighted), Is.True);
            browser.SelectedNamespace = null;
            Assert.That(group.Children.Any(node => node.IsNamespaceHighlighted), Is.False);
        }

        [Test]
        public void OfflineSourceTakesPrecedenceAndClosingItRestoresServerNamespacesWithoutStaleSelection()
        {
            Mock<ISession> session = Session("urn:server");
            BrowserViewModel browser = Browser(() => session.Object);
            browser.Reload();
            browser.SelectedNamespace = browser.NamespaceOptions[2];
            browser.SetOfflineSource(m_graph);
            Assert.That(browser.SelectedNamespace, Is.EqualTo(NamespaceOption.None));
            Assert.That(browser.NamespaceOptions.Any(option => option.NamespaceUri == "urn:server"), Is.False);
            browser.SelectedNamespace = browser.NamespaceOptions[2];
            browser.SetOfflineSource(null);
            Assert.That(browser.SelectedNamespace, Is.EqualTo(NamespaceOption.None));
            Assert.That(browser.NamespaceOptions[2].NamespaceUri, Is.EqualTo("urn:server"));
        }

        internal static Mock<ISession> Session(params ArrayOf<string> namespaces)
        {
            var table = new NamespaceTable();
            foreach (string uri in namespaces)
            {
                table.Append(uri);
            }
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.NamespaceUris).Returns(table);
            session.Setup(value => value.BrowseAsync(
                null, null, 0, It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse { Results = [new BrowseResult()] });
            return session;
        }

        internal static BrowserViewModel Browser(System.Func<ISession?> session)
        {
            return new BrowserViewModel(
                NodeSetAddressSpaceTests.Telemetry(), session, InlineWorkspaceDispatcher.Instance)
            {
                CurrentViewKind = BrowseViewKind.ObjectTypes
            };
        }

        private NodeSetAddressSpace m_graph = null!;
        private static readonly bool[] s_highlightChanges = [true, false];
    }
}
