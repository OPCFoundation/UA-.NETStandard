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
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class AddressSpaceViewWorkflowTests
{
    [Test]
    public Task SelectionAndActionsPublishExactNodeOnce()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            NodeViewModel variable = Node(context, 4, "Temperature", NodeClass.Variable);
            NodeViewModel method = Node(context, 9, "Start", NodeClass.Method);
            context.Browser.Roots.Add(variable);
            context.Browser.Roots.Add(method);
            var view = new AddressSpaceView { DataContext = context.Browser };
            var selected = new List<NodeViewModel>();
            var actions = new List<(string Name, NodeViewModel? Node)>();
            view.NodeSelected += selected.Add;
            view.AddItemRequested += node => actions.Add(("MenuAddItem", node));
            view.AddRecursivelyRequested += node => actions.Add(("MenuAddRecursive", node));
            view.CallMethodRequested += node => actions.Add(("MenuCallMethod", node));
            view.WriteValueRequested += node => actions.Add(("MenuWriteValue", node));
            view.ReadHistoryRequested += node => actions.Add(("MenuReadHistory", node));
            view.ShowEventsRequested += node => actions.Add(("MenuShowEvents", node));
            view.ShowAlarmsRequested += node => actions.Add(("MenuShowAlarms", node));
            view.InspectModelRequested += node => actions.Add(("MenuInspectModel", node));
            view.PerfRequested += node => actions.Add(("MenuPerf", node));
            view.AddToBenchRequested += node => actions.Add(("MenuAddToBench", node));
            view.ExportValueRequested += node => actions.Add(("MenuExportValue", node));
            view.FindByPathRequested += node => actions.Add(("MenuFindByPath", node));
            view.ViewNodeStateRequested += node => actions.Add(("MenuViewNodeState", node));
            DesktopInteraction.Owner.Content = view;
            try
            {
                TreeView tree = DesktopInteraction.Control<TreeView>(view, "Tree");
                tree.SelectedItem = variable;
                Assert.That(selected, Is.EqualTo(new[] { variable }));
                string[] names =
                [
                    "MenuAddItem", "MenuAddRecursive", "MenuCallMethod", "MenuWriteValue", "MenuReadHistory",
                    "MenuShowEvents", "MenuShowAlarms", "MenuInspectModel", "MenuPerf", "MenuAddToBench",
                    "MenuExportValue", "MenuFindByPath", "MenuViewNodeState"
                ];
                foreach (string name in names)
                {
                    MenuItem menu = DesktopInteraction.Control<MenuItem>(view, name);
                    menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                }
                Assert.That(actions.Select(action => action.Name), Is.EqualTo(names));
                Assert.That(actions.Select(action => action.Node), Is.All.SameAs(variable));
                tree.SelectedItem = method;
                tree.SelectedItem = null;
                Assert.That(selected, Is.EqualTo(new[] { variable, method }));
                actions.Clear();
                foreach (string name in names)
                {
                    DesktopInteraction.Control<MenuItem>(view, name)
                        .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                }
                Assert.That(actions, Is.EqualTo(new[] { ("MenuFindByPath", (NodeViewModel?)null) }));
            }
            finally
            {
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task ContextActionsMatchSuppliedPolicyAndClearWhenSelectionIsMissing(bool enabled)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            NodeViewModel node = Node(context, 4, "Object", NodeClass.Object);
            context.Browser.Roots.Add(node);
            var policy = new Mock<IContextMenuPolicy>(MockBehavior.Strict);
            policy.Setup(p => p.Inspect(node)).Returns(new ContextMenuVisibility(
                enabled, enabled, !enabled, !enabled, enabled, enabled, !enabled, enabled, !enabled, enabled));
            var view = new AddressSpaceView { DataContext = context.Browser, ContextMenuPolicy = policy.Object };
            DesktopInteraction.Owner.Content = view;
            ContextMenu menu = DesktopInteraction.Control<ContextMenu>(view, "NodeMenu");
            try
            {
                TreeView tree = DesktopInteraction.Control<TreeView>(view, "Tree");
                tree.SelectedItem = node;
                tree.RaiseEvent(new ContextRequestedEventArgs());
                Assert.That(DesktopInteraction.Control<MenuItem>(view, "MenuAddItem").IsVisible, Is.EqualTo(enabled));
                Assert.That(DesktopInteraction.Control<MenuItem>(view, "MenuCallMethod").IsVisible,
                    Is.EqualTo(!enabled));
                Assert.That(DesktopInteraction.Control<MenuItem>(view, "MenuShowEvents").IsVisible,
                    Is.EqualTo(enabled));
                Assert.That(DesktopInteraction.Control<MenuItem>(view, "MenuShowAlarms").IsVisible,
                    Is.EqualTo(enabled));
                Assert.That(DesktopInteraction.Control<MenuItem>(view, "MenuInspectModel").IsVisible,
                    Is.EqualTo(enabled));
                Assert.That(DesktopInteraction.Control<MenuItem>(view, "MenuExportValue").IsVisible,
                    Is.EqualTo(!enabled));
                Assert.That(DesktopInteraction.Control<MenuItem>(view, "MenuViewNodeState").IsVisible, Is.True);
                menu.Close();
                tree.SelectedItem = null;
                tree.RaiseEvent(new ContextRequestedEventArgs());
                Assert.That(DesktopInteraction.Control<MenuItem>(view, "MenuAddItem").IsVisible, Is.False);
                Assert.That(DesktopInteraction.Control<MenuItem>(view, "MenuShowEvents").IsVisible, Is.False);
                Assert.That(DesktopInteraction.Control<MenuItem>(view, "MenuViewNodeState").IsVisible, Is.False);
                Assert.That(DesktopInteraction.Control<MenuItem>(view, "MenuFindByPath").IsVisible, Is.True);
                policy.Verify(p => p.Inspect(node), Times.Once);
            }
            finally
            {
                menu.Close();
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    [Test]
    public Task SearchWalksLoadedChildrenWrapsAndSwitchesViewWithoutNetwork()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            NodeViewModel root = Node(context, 1, "Folder", NodeClass.Object);
            NodeViewModel first = Node(context, 4, "Motor temperature", NodeClass.Variable);
            NodeViewModel second = Node(context, 9, "motor pressure", NodeClass.Variable);
            root.Children.Clear();
            root.Children.Add(first);
            root.Children.Add(second);
            root.IsExpanded = true;
            context.Browser.Roots.Add(root);
            var view = new AddressSpaceView { DataContext = context.Browser };
            DesktopInteraction.Owner.Content = view;
            try
            {
                TextBox search = DesktopInteraction.Control<TextBox>(view, "SearchBox");
                TreeView tree = DesktopInteraction.Control<TreeView>(view, "Tree");
                search.Text = "MOTOR";
                KeyEventArgs enter = KeyEvent(search, Key.Enter);
                search.RaiseEvent(enter);
                Assert.That(enter.Handled, Is.True);
                Assert.That(tree.SelectedItem, Is.SameAs(first));
                search.RaiseEvent(KeyEvent(search, Key.F3));
                Assert.That(tree.SelectedItem, Is.SameAs(second));
                search.RaiseEvent(KeyEvent(search, Key.F3));
                Assert.That(tree.SelectedItem, Is.SameAs(second));
                search.RaiseEvent(KeyEvent(search, Key.F3));
                Assert.That(tree.SelectedItem, Is.SameAs(first));
                search.Text = "ns=2;i=9";
                search.RaiseEvent(KeyEvent(search, Key.Enter));
                Assert.That(tree.SelectedItem, Is.SameAs(second));
                context.Browser.ShowFilters = true;
                Assert.That(search.IsVisible, Is.True);
                ComboBox kinds = DesktopInteraction.Control<ComboBox>(view, "ViewKindCombo");
                await DesktopInteraction.CollectionChangedAsync(context.Browser.Roots,
                    () => context.Browser.Roots.Count == 0,
                    () => kinds.SelectedItem = BrowseViewKind.DataTypes).ConfigureAwait(true);
                Assert.That(context.Browser.CurrentViewKind, Is.EqualTo(BrowseViewKind.DataTypes));
                Assert.That(kinds.Items.Cast<BrowseViewKind>(), Does.Contain(BrowseViewKind.ReferenceTypes));
                Assert.That(context.Discoveries, Is.Empty);
            }
            finally
            {
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    private static NodeViewModel Node(DesktopConnectionContext context, uint id, string name, NodeClass nodeClass)
    {
        return new NodeViewModel(context.Browser, NodeId.Null, new NodeId(id, 2), name, nodeClass)
        {
            ChildrenLoaded = true
        };
    }

    private static KeyEventArgs KeyEvent(Control source, Key key)
    {
        return new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Source = source, Key = key };
    }
}
