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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Moq;
using NUnit.Framework;
using UaLens.NodeSets;
using UaLens.NodeSets.Loading;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Tests.NodeSets
{
    [TestFixture]
    [Explicit("Requires the real Avalonia desktop backend and an X11 display on Linux.")]
    [Category("LensNodeSetsDesktop")]
    [NonParallelizable]
    public sealed class NodeSetDesktopTests
    {
        [Test]
        public Task FileMenuOpensMultipleModelsAndExistingExplorerInspectsThem()
        {
            return DesktopApplication.RunAsync(async () =>
            {
                var storage = new Mock<IStorageProvider>(MockBehavior.Strict);
                var repository = new Mock<INodeSetRepository>(MockBehavior.Strict);
                await using DesktopWindowScope scope = await DesktopWindowScope
                    .OpenAsync(storage.Object, repository.Object)
                    .ConfigureAwait(true);
                string app = Path.Combine(scope.DirectoryPath, "app.xml");
                string dependency = Path.Combine(scope.DirectoryPath, "dependency.xml");
                await File.WriteAllTextAsync(app, NodeSetAddressSpaceTests.kApp).ConfigureAwait(true);
                await File.WriteAllTextAsync(dependency, NodeSetAddressSpaceTests.kDependency).ConfigureAwait(true);
                storage.Setup(value => value.OpenFilePickerAsync(
                    It.Is<FilePickerOpenOptions>(options => options.AllowMultiple)))
                    .ReturnsAsync([FileAt(app), FileAt(dependency)]);
                await DesktopWindowScope.ChangeAsync(
                    scope.ViewModel, () => scope.ViewModel.IsOffline && !scope.ViewModel.IsLoadingNodeSets,
                    () => ClickOpenMenu(scope.Window))
                    .ConfigureAwait(true);
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                Assert.That(scope.ViewModel.OfflineAddressSpace!.Documents, Has.Count.EqualTo(3));
                Assert.That(scope.ViewModel.IsConnected, Is.False);
                AddressSpaceView explorer = scope.Window.FindControl<AddressSpaceView>("LiveTree")!;
                TextBox search = explorer.FindControl<TextBox>("SearchBox")!;
                search.Text = "Reading";
                DesktopWindowScope.Key(search, Key.Enter);
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                Assert.That(scope.ViewModel.SelectedNode!.Text, Is.EqualTo("Reading"));
                Assert.That(
                    scope.ViewModel.Attributes.Rows.Any(row => row.Name == "Value" && row.Value == "42"), Is.True);
                Assert.That(scope.Window.FindControl<Button>("NodeWriteBtn")!.IsEnabled, Is.False);
                Assert.That(scope.Window.FindControl<Button>("NodeMonitorBtn")!.IsEnabled, Is.False);
                ComboBox views = explorer.FindControl<ComboBox>("ViewKindCombo")!;
                views.SelectedItem = BrowseViewKind.AllNodes;
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                Assert.That(scope.ViewModel.Browser.CurrentViewKind, Is.EqualTo(BrowseViewKind.AllNodes));
                Assert.That(scope.ViewModel.Browser.Roots, Has.Count.GreaterThanOrEqualTo(3));
                search.Text = "Orphan";
                DesktopWindowScope.Key(search, Key.Enter);
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                Assert.That(scope.ViewModel.SelectedNode!.Text, Is.EqualTo("Orphan"));
                await DesktopWindowScope.ChangeAsync(scope.ViewModel, () => !scope.ViewModel.IsOffline,
                    () => DesktopWindowScope.Click(scope.Window.FindControl<Button>("CloseNodeSetsButton")!))
                    .ConfigureAwait(true);
                Assert.That(scope.ViewModel.Browser.Roots, Is.Empty);
                repository.VerifyNoOtherCalls();
            });
        }

        [Test]
        public Task ApprovedLookupForUnpublishedModelOpensLocalDependencyPicker()
        {
            return DesktopApplication.RunAsync(async () =>
            {
                var storage = new Mock<IStorageProvider>(MockBehavior.Strict);
                var repository = new Mock<INodeSetRepository>(MockBehavior.Strict);
                repository.Setup(value => value.FindAsync(
                    It.Is<NodeSetRequirement>(requirement => requirement.ModelUri == "urn:lens:dependency"),
                    It.IsAny<CancellationToken>())).ReturnsAsync((NodeSetDocument?)null);
                await using DesktopWindowScope scope = await DesktopWindowScope
                    .OpenAsync(storage.Object, repository.Object)
                    .ConfigureAwait(true);
                string app = Path.Combine(scope.DirectoryPath, "app.xml");
                string other = Path.Combine(scope.DirectoryPath, "other");
                Directory.CreateDirectory(other);
                string dependency = Path.Combine(other, "dependency.xml");
                await File.WriteAllTextAsync(app, NodeSetAddressSpaceTests.kApp).ConfigureAwait(true);
                await File.WriteAllTextAsync(dependency, NodeSetAddressSpaceTests.kDependency).ConfigureAwait(true);
                storage.Setup(value => value.OpenFilePickerAsync(
                    It.Is<FilePickerOpenOptions>(options => options.AllowMultiple)))
                    .ReturnsAsync([FileAt(app)]);
                storage.Setup(value => value.OpenFilePickerAsync(It.Is<FilePickerOpenOptions>(options =>
                    !options.AllowMultiple &&
                    options.Title!.Contains("urn:lens:dependency", StringComparison.Ordinal))))
                    .ReturnsAsync([FileAt(dependency)]);
                ClickOpenMenu(scope.Window);
                NodeSetDependencyDialog dialog = await WaitForDependencyDialogAsync(scope.Window).ConfigureAwait(true);
                repository.VerifyNoOtherCalls();
                await DesktopWindowScope.ChangeAsync(
                    scope.ViewModel, () => scope.ViewModel.IsOffline && !scope.ViewModel.IsLoadingNodeSets,
                    () => DesktopWindowScope.Click(dialog.GetVisualDescendants().OfType<Button>()
                        .Single(button => button.Name == "DownloadDependencyButton")))
                    .ConfigureAwait(true);
                Assert.That(scope.ViewModel.OfflineAddressSpace!.NamespaceUris.GetIndex("urn:lens:dependency"),
                    Is.GreaterThan(0));
                repository.Verify(value => value.FindAsync(
                    It.IsAny<NodeSetRequirement>(), It.IsAny<CancellationToken>()), Times.Once);
                storage.Verify(value => value.OpenFilePickerAsync(
                    It.Is<FilePickerOpenOptions>(options => !options.AllowMultiple)), Times.Once);
            });
        }

        [Test]
        public Task CancellingDependencyDialogPreservesTheExistingGraph()
        {
            return DesktopApplication.RunAsync(async () =>
            {
                var storage = new Mock<IStorageProvider>(MockBehavior.Strict);
                var repository = new Mock<INodeSetRepository>(MockBehavior.Strict);
                await using DesktopWindowScope scope = await DesktopWindowScope
                    .OpenAsync(storage.Object, repository.Object)
                    .ConfigureAwait(true);
                string dependency = Path.Combine(scope.DirectoryPath, "dependency.xml");
                await File.WriteAllTextAsync(dependency, NodeSetAddressSpaceTests.kDependency).ConfigureAwait(true);
                await scope.ViewModel.OpenNodeSetsAsync([dependency], Mock.Of<INodeSetDependencyResolver>())
                    .ConfigureAwait(true);
                NodeSetAddressSpace? original = scope.ViewModel.OfflineAddressSpace;
                string missing = Path.Combine(scope.DirectoryPath, "missing.xml");
                await File.WriteAllTextAsync(missing, """
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <NamespaceUris><Uri>urn:lens:missing</Uri></NamespaceUris>
                  <Models><Model ModelUri="urn:lens:missing">
                    <RequiredModel ModelUri="urn:lens:unavailable" />
                  </Model></Models>
                  <UAObject NodeId="ns=1;i=1" BrowseName="1:Test" />
                </UANodeSet>
                """).ConfigureAwait(true);
                storage.Setup(value => value.OpenFilePickerAsync(It.IsAny<FilePickerOpenOptions>()))
                    .ReturnsAsync([FileAt(missing)]);
                ClickOpenMenu(scope.Window);
                NodeSetDependencyDialog dialog = await WaitForDependencyDialogAsync(scope.Window).ConfigureAwait(true);
                await DesktopWindowScope.ChangeAsync(scope.ViewModel, () => !scope.ViewModel.IsLoadingNodeSets,
                    () => DesktopWindowScope.Click(dialog.GetVisualDescendants().OfType<Button>()
                        .Single(button => button.Name == "CancelDependencyButton")))
                    .ConfigureAwait(true);
                Assert.That(scope.ViewModel.OfflineAddressSpace, Is.SameAs(original));
                Assert.That(scope.Window.OwnedWindows, Is.Empty);
                repository.VerifyNoOtherCalls();
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public Task InvalidFilePreservesGraphAndKeepsDiagnosticsLogAndRetryUsable(bool malformedXml)
        {
            return DesktopApplication.RunAsync(async () =>
            {
                var storage = new Mock<IStorageProvider>(MockBehavior.Strict);
                var repository = new Mock<INodeSetRepository>(MockBehavior.Strict);
                await using DesktopWindowScope scope = await DesktopWindowScope
                    .OpenAsync(storage.Object, repository.Object).ConfigureAwait(true);
                string dependency = Path.Combine(scope.DirectoryPath, "dependency.xml");
                await File.WriteAllTextAsync(dependency, NodeSetAddressSpaceTests.kDependency).ConfigureAwait(true);
                await scope.ViewModel.OpenNodeSetsAsync([dependency], Mock.Of<INodeSetDependencyResolver>())
                    .ConfigureAwait(true);
                NodeSetAddressSpace? original = scope.ViewModel.OfflineAddressSpace;
                string path = Path.Combine(scope.DirectoryPath, "invalid.xml");
                await File.WriteAllTextAsync(path, malformedXml ? "<UANodeSet" : kUndeclaredAlias)
                    .ConfigureAwait(true);
                storage.Setup(value => value.OpenFilePickerAsync(It.IsAny<FilePickerOpenOptions>()))
                    .ReturnsAsync([FileAt(path)]);
                Border banner = scope.Window.FindControl<Border>("OperationErrorBanner")!;
                await DesktopWindowScope.ControlChangeAsync(banner, () => banner.IsVisible,
                    () => ClickOpenMenu(scope.Window)).ConfigureAwait(true);
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                Assert.That(scope.ViewModel.IsLoadingNodeSets, Is.False);
                Assert.That(scope.ViewModel.OfflineAddressSpace, Is.SameAs(original));
                Assert.That(scope.Window.OwnedWindows, Is.Empty);
                string? message = scope.Window.FindControl<TextBlock>("OperationErrorText")!.Text;
                Assert.That(message, Does.Contain(malformedXml ? "Cannot read NodeSet2" : "HasProperty"));
                Assert.That(scope.ViewModel.Telemetry.Buffer.SnapshotList().Any(entry =>
                    entry.Category == "NodeSets" &&
                    entry.Message.Contains(nameof(InvalidDataException), StringComparison.Ordinal)), Is.True);
                ToggleButton diagnostics = scope.Window.FindControl<ToggleButton>("ToggleDiagButton")!;
                ToggleButton log = scope.Window.FindControl<ToggleButton>("ToggleLogButton")!;
                Assert.That(diagnostics.IsEffectivelyEnabled, Is.True);
                Assert.That(log.IsEffectivelyEnabled, Is.True);
                diagnostics.IsChecked = true;
                log.IsChecked = true;
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                Assert.That(scope.Window.FindControl<DiagnosticsView>("DiagnosticsPanel")!.IsEffectivelyVisible,
                    Is.True);
                Assert.That(scope.Window.FindControl<Border>("LogPanel")!.IsEffectivelyVisible, Is.True);

                await File.WriteAllTextAsync(path, kUndeclaredAlias.Replace("<UAObject",
                    """<Aliases><Alias Alias="HasProperty">i=46</Alias></Aliases><UAObject""",
                    StringComparison.Ordinal)).ConfigureAwait(true);
                await DesktopWindowScope.ChangeAsync(scope.ViewModel,
                    () => !scope.ViewModel.IsLoadingNodeSets && scope.ViewModel.OfflineAddressSpace != original,
                    () => ClickOpenMenu(scope.Window)).ConfigureAwait(true);
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                Assert.That(scope.ViewModel.IsOffline, Is.True);
                Assert.That(banner.IsVisible, Is.False, "A successful retry must clear the previous import error.");
                repository.VerifyNoOtherCalls();
            });
        }

        [Test]
        public Task InvalidDownloadedDocumentIsReportedAndAllowsLocalDependencyFallback()
        {
            return DesktopApplication.RunAsync(async () =>
            {
                var storage = new Mock<IStorageProvider>(MockBehavior.Strict);
                var repository = new Mock<INodeSetRepository>(MockBehavior.Strict);
                repository.Setup(value => value.FindAsync(
                    It.IsAny<NodeSetRequirement>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidDataException("Invalid repository document."));
                await using DesktopWindowScope scope = await DesktopWindowScope
                    .OpenAsync(storage.Object, repository.Object).ConfigureAwait(true);
                string app = Path.Combine(scope.DirectoryPath, "app.xml");
                string other = Path.Combine(scope.DirectoryPath, "other");
                Directory.CreateDirectory(other);
                string dependency = Path.Combine(other, "dependency.xml");
                await File.WriteAllTextAsync(app, NodeSetAddressSpaceTests.kApp).ConfigureAwait(true);
                await File.WriteAllTextAsync(dependency, NodeSetAddressSpaceTests.kDependency).ConfigureAwait(true);
                storage.Setup(value => value.OpenFilePickerAsync(
                    It.Is<FilePickerOpenOptions>(options => options.AllowMultiple)))
                    .ReturnsAsync([FileAt(app)]);
                storage.Setup(value => value.OpenFilePickerAsync(It.Is<FilePickerOpenOptions>(options =>
                    !options.AllowMultiple &&
                    options.Title!.StartsWith("Download failed.", StringComparison.Ordinal))))
                    .Callback(() =>
                    {
                        Assert.That(scope.Window.FindControl<Border>("OperationErrorBanner")!.IsVisible, Is.True);
                        Assert.That(scope.Window.FindControl<TextBlock>("OperationErrorText")!.Text,
                            Does.Contain("Invalid repository document."));
                    })
                    .ReturnsAsync([FileAt(dependency)]);
                ClickOpenMenu(scope.Window);
                NodeSetDependencyDialog dialog = await WaitForDependencyDialogAsync(scope.Window).ConfigureAwait(true);
                await DesktopWindowScope.ChangeAsync(scope.ViewModel,
                    () => scope.ViewModel.IsOffline && !scope.ViewModel.IsLoadingNodeSets,
                    () => DesktopWindowScope.Click(dialog.GetVisualDescendants().OfType<Button>()
                        .Single(button => button.Name == "DownloadDependencyButton"))).ConfigureAwait(true);
                await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                Assert.That(scope.Window.IsEnabled, Is.True);
                Assert.That(scope.Window.OwnedWindows, Is.Empty);
                Assert.That(scope.Window.FindControl<Border>("OperationErrorBanner")!.IsVisible, Is.False);
                Assert.That(scope.ViewModel.Telemetry.Buffer.SnapshotList().Any(entry =>
                    entry.Message.Contains("Invalid repository document.", StringComparison.Ordinal)), Is.True);
                repository.Verify(value => value.FindAsync(
                    It.IsAny<NodeSetRequirement>(), It.IsAny<CancellationToken>()), Times.Once);
                storage.Verify(value => value.OpenFilePickerAsync(
                    It.Is<FilePickerOpenOptions>(options => !options.AllowMultiple)), Times.Once);
            });
        }

        private static void ClickOpenMenu(Window window)
        {
            window.FindControl<MenuItem>("MenuOpenNodeSets")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }

        private static IStorageFile FileAt(string path)
        {
            var file = new Mock<IStorageFile>(MockBehavior.Strict);
            file.SetupGet(value => value.Path).Returns(new Uri(Path.GetFullPath(path)));
            file.SetupGet(value => value.Name).Returns(Path.GetFileName(path));
            return file.Object;
        }

        private static async Task<NodeSetDependencyDialog> WaitForDependencyDialogAsync(Window owner)
        {
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < DesktopApplication.Timeout)
            {
                if (owner.OwnedWindows.OfType<NodeSetDependencyDialog>().FirstOrDefault() is { } dialog)
                {
                    await DesktopWindowScope.FrameAsync(dialog).ConfigureAwait(true);
                    return dialog;
                }
                await DesktopWindowScope.FrameAsync(owner).ConfigureAwait(true);
            }
            throw new TimeoutException("The missing-dependency dialog did not open.");
        }

        private const string kUndeclaredAlias = """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>urn:lens:alias</Uri></NamespaceUris>
              <Models><Model ModelUri="urn:lens:alias" /></Models>
              <UAObject NodeId="ns=1;i=1" BrowseName="1:Root">
                <References><Reference ReferenceType="HasProperty">ns=1;i=2</Reference></References>
              </UAObject>
            </UANodeSet>
            """;
    }
}
