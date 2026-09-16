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

using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.NodeSets.Loading;
using UaLens.Tests.Desktop;
using UaLens.Tests.NodeSets;
using UaLens.Themes;
using UaLens.ViewModels;
using UaLens.Views;
using UaLens.Workspace;

namespace UaLens.Tests.Presentation
{
    [TestFixture]
    [Explicit("Requires the real Avalonia desktop backend and an X11 display on Linux.")]
    [Category("LensNamespaceDesktop")]
    [NonParallelizable]
    internal sealed class NamespaceHighlightDesktopTests
    {
        [TestCase(false, ThemePreset.Light)]
        [TestCase(false, ThemePreset.DarkStandard)]
        [TestCase(false, ThemePreset.DarkNavy)]
        [TestCase(true, ThemePreset.Light)]
        [TestCase(true, ThemePreset.DarkStandard)]
        [TestCase(true, ThemePreset.DarkNavy)]
        public Task NamespacePickerHighlightsExistingAndLazyNodesWithoutChangingSelection(bool offline, ThemePreset theme)
        {
            return DesktopApplication.RunAsync(async () =>
            {
                await using DesktopWindowScope scope = await DesktopWindowScope.OpenAsync().ConfigureAwait(true);
                await ThemeManager.SetThemeAsync(theme, scope.Appearance).ConfigureAwait(true);
                scope.ViewModel.IsAddressSpaceVisible = true;
                AddressSpaceView view = scope.Window.FindControl<AddressSpaceView>("LiveTree")!;
                ISession? current = null;
                BrowserViewModel browser;
                NodeViewModel machine;
                NodeViewModel reading;
                if (offline)
                {
                    string app = Path.Combine(scope.DirectoryPath, "app.xml");
                    string dependency = Path.Combine(scope.DirectoryPath, "dependency.xml");
                    await File.WriteAllTextAsync(app, NodeSetAddressSpaceTests.kApp).ConfigureAwait(true);
                    await File.WriteAllTextAsync(dependency, NodeSetAddressSpaceTests.kDependency).ConfigureAwait(true);
                    await scope.ViewModel.OpenNodeSetsAsync([app, dependency], Mock.Of<INodeSetDependencyResolver>())
                        .ConfigureAwait(true);
                    browser = scope.ViewModel.Browser;
                    NamespaceTable namespaces = browser.OfflineSource!.NamespaceUris;
                    machine = browser.ShowOfflineNode(new NodeId(1, namespaces.GetIndexOrAppend("urn:lens:app")))!;
                    reading = browser.ShowOfflineNode(new NodeId(2, namespaces.GetIndexOrAppend("urn:lens:dependency")))!;
                }
                else
                {
                    Mock<ISession> session = NamespaceHighlightTests.Session("urn:lens:app", "urn:lens:dependency");
                    session.Setup(value => value.BrowseAsync(
                        null, null, 0, It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                        .Returns<RequestHeader?, ViewDescription?, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                            (_, _, _, descriptions, _) =>
                            {
                                ArrayOf<ReferenceDescription> references = descriptions[0].NodeId == ObjectIds.RootFolder
                                    ? [Reference(1, 1, "Machine"), Reference(2, 2, "Reading")]
                                    : descriptions[0].NodeId == new NodeId(1, 1)
                                        ? [Reference(2, 2, "Reading")]
                                        : [];
                                return new ValueTask<BrowseResponse>(new BrowseResponse
                                {
                                    Results = descriptions.ToList().Select((_, index) =>
                                        new BrowseResult { References = index == 0 ? references : [] }).ToArray()
                                });
                            });
                    current = session.Object;
                    browser = new BrowserViewModel(
                        scope.ViewModel.Telemetry, () => current, new AvaloniaWorkspaceDispatcher());
                    view.DataContext = browser;
                    browser.Reload();
                    machine = browser.Roots[0].Children.Single(node => node.Text == "Machine");
                    reading = browser.Roots[0].Children.Single(node => node.Text == "Reading");
                }
                browser.ShowFilters = false;
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                ComboBox combo = view.FindControl<ComboBox>("NamespaceCombo")!;
                TreeView tree = view.FindControl<TreeView>("Tree")!;
                Assert.That(combo.IsEffectivelyVisible, Is.True, "Namespace selection must not require showing filters.");
                Assert.That(combo.IsEffectivelyEnabled, Is.True);
                Assert.That(combo.Items, Has.Count.EqualTo(4));
                Assert.That(combo.Bounds.Width, Is.GreaterThan(0));
                combo.Focus();
                DesktopWindowScope.Key(combo, Key.F4);
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                Assert.That(combo.IsDropDownOpen, Is.True);
                DesktopWindowScope.Key(combo, Key.Escape);
                Assert.That(combo.IsDropDownOpen, Is.False);
                tree.SelectedItem = machine;
                combo.SelectedItem = browser.NamespaceOptions.Single(
                    option => option.NamespaceUri == "urn:lens:dependency");
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                Assert.That(browser.SelectedNamespace!.NamespaceUri, Is.EqualTo("urn:lens:dependency"));
                Assert.That(combo.GetVisualDescendants().OfType<TextBlock>().Any(
                    text => text.IsEffectivelyVisible && text.Text == browser.SelectedNamespace.DisplayName), Is.True);
                AssertHighlighted(view, reading, true);
                AssertHighlighted(view, machine, false);
                Assert.That(tree.SelectedItem, Is.SameAs(machine));

                await DesktopWindowScope.ChangeAsync(machine, () => machine.ChildrenLoaded,
                    () => machine.IsExpanded = true).ConfigureAwait(true);
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                NodeViewModel child = machine.Children.Single(node => node.Text == "Reading");
                AssertHighlighted(view, child, true);
                combo.SelectedItem = browser.NamespaceOptions.Single(option => option.NamespaceUri == "urn:lens:app");
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                AssertHighlighted(view, machine, true);
                AssertHighlighted(view, reading, false);
                AssertHighlighted(view, child, false);
                Assert.That(tree.SelectedItem, Is.SameAs(machine));
                Assert.That(machine.IsExpanded, Is.True);

                combo.SelectedItem = NamespaceOption.None;
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                AssertHighlighted(view, machine, false);
                Assert.That(tree.SelectedItem, Is.SameAs(machine));
                combo.SelectedItem = browser.NamespaceOptions[1];
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                NodeViewModel core = browser.Roots.Single(node => node.NodeId == ObjectIds.RootFolder);
                AssertHighlighted(view, core, true);
                if (offline)
                {
                    await scope.ViewModel.CloseNodeSetsAsync().ConfigureAwait(true);
                }
                else
                {
                    current = null;
                    browser.Reload();
                }
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                Assert.That(combo.IsEnabled, Is.False);
                Assert.That(combo.SelectedItem, Is.EqualTo(NamespaceOption.None));
                Assert.That(browser.Roots, Is.Empty);
            });
        }

        private static void AssertHighlighted(AddressSpaceView view, NodeViewModel node, bool expected)
        {
            Border row = view.GetVisualDescendants().OfType<Border>().Single(
                border => border.Classes.Contains("nodeRow") && ReferenceEquals(border.DataContext, node));
            Assert.That(row.IsEffectivelyVisible, Is.True);
            Assert.That(row.Classes.Contains("namespaceMatch"), Is.EqualTo(expected));
            Assert.That(ColorOf(row.Background), Is.EqualTo(expected ? ResourceColor("HighlightBg") : Colors.Transparent));
            TextBlock label = row.GetVisualDescendants().OfType<TextBlock>().Single(
                text => text.Classes.Contains("nodeLabel"));
            Assert.That(label.FontWeight, Is.EqualTo(expected ? FontWeight.SemiBold : FontWeight.Normal));
            if (expected)
            {
                Assert.That(ColorOf(row.BorderBrush), Is.EqualTo(ResourceColor("AccentBlue")));
                Assert.That(ColorOf(label.Foreground), Is.EqualTo(ResourceColor("TextPrimary")));
            }
        }

        private static Color ResourceColor(string key)
        {
            Application application = Application.Current!;
            Assert.That(application.TryGetResource(key, application.ActualThemeVariant, out object? resource), Is.True);
            return resource is ISolidColorBrush brush
                ? brush.Color
                : throw new AssertionException($"Theme resource '{key}' is not a solid brush.");
        }

        private static Color ColorOf(IBrush? brush)
        {
            return brush is ISolidColorBrush solid
                ? solid.Color
                : throw new AssertionException("The node row did not render with a solid brush.");
        }

        private static ReferenceDescription Reference(uint id, ushort ns, string name)
        {
            return new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(id, ns),
                BrowseName = new QualifiedName(name, ns),
                DisplayName = new LocalizedText(name),
                NodeClass = NodeClass.Object
            };
        }
    }
}
