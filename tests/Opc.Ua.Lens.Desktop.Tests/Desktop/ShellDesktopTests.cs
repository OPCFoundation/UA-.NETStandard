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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Capabilities;
using UaLens.Connection;
using UaLens.Themes;
using UaLens.ViewModels;
using UaLens.Views;
using UaLens.Workspace;

namespace UaLens.Tests.Desktop;

/// <summary>
/// Exercises the shipped shell on its real desktop backend. Routed key events
/// test application routing, not physical keyboard delivery or screen readers.
/// </summary>
[TestFixture]
[Category("LensDesktop")]
[NonParallelizable]
public sealed class ShellDesktopTests
{
    [Test]
    public Task SynchronouslyStalledDiscoveryDoesNotBlockTheDesktopDispatcher()
    {
        return DesktopApplication.RunAsync(async () =>
        {
            var dispatcher = new AvaloniaWorkspaceDispatcher();
            await using var context = new DesktopConnectionContext(
                new ConnectionConfigurationCatalog(),
                dispatcher);
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            context.DiscoverAsync = (_, token) =>
            {
                entered.TrySetResult();
                release.Wait(token);
                throw new IOException("Controlled discovery failure.");
            };
            var operations = new PluginDocumentOperations(context.Connection);
            var workspace = new DocumentWorkspace<IPlugin>(
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                dispatcher,
                operations.SynchronizeConnectionAsync);
            await using var viewModel = new MainViewModel(
                context.Telemetry,
                context.Connection,
                workspace,
                new CommandRegistry(),
                operations,
                dispatcher,
                startResourceMonitor: _ => Task.FromException<UaLens.Diagnostics.ResourceMonitorHost>(
                    new InvalidOperationException("Unexpected monitor.")),
                capabilities: new Mock<ICapabilityService>().Object);
            viewModel.EndpointUrl = "ws://localhost:8080/api/ws";
            await using DesktopWindowScope scope = await DesktopWindowScope
                .OpenAsync(viewModel: viewModel)
                .ConfigureAwait(true);

            Task<bool> responsiveness = Task.Run(async () =>
            {
                await entered.Task.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(false);
                var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Dispatcher.UIThread.Post(dispatched.SetResult);
                Task completed = await Task.WhenAny(
                    dispatched.Task,
                    Task.Delay(TimeSpan.FromMilliseconds(500))).ConfigureAwait(false);
                bool responsive = ReferenceEquals(completed, dispatched.Task);
                release.Set();
                return responsive;
            });

            Task connect = viewModel.ConnectCommand.ExecuteAsync(null);

            Assert.That(await responsiveness.ConfigureAwait(true), Is.True,
                "A stalled transport must not block diagnostics, cancellation, or window repainting.");
            await connect.ConfigureAwait(true);
        });
    }

    [Test]
    public Task CatalogOpensActualToolAndTabCloseRestoresWelcome()
    {
        return DesktopApplication.RunAsync(async () =>
        {
            await using DesktopWindowScope scope = await DesktopWindowScope.OpenAsync().ConfigureAwait(true);
            Assert.That(scope.Window.FindControl<ScrollViewer>("WelcomePanel")!.IsVisible, Is.True);
            IPlugin document = await OpenFromCatalogAsync(scope, PluginKind.Subscription).ConfigureAwait(true);
            Assert.That(document.View, Is.InstanceOf<SubscriptionDocumentView>());
            Assert.That(scope.Window.FindControl<ContentControl>("DocumentHost")!.Content, Is.SameAs(document.View));
            Assert.That(scope.Window.FindControl<ListBox>("TabStrip")!.SelectedItem, Is.SameAs(document));
            Assert.That(scope.Window.FindControl<ScrollViewer>("WelcomePanel")!.IsVisible, Is.False);
            await CloseFromStripAsync(scope, document).ConfigureAwait(true);
            Assert.That(scope.ViewModel.Tabs, Is.Empty);
            Assert.That(scope.Window.FindControl<ContentControl>("DocumentHost")!.Content, Is.Null);
            Assert.That(scope.Window.FindControl<ScrollViewer>("WelcomePanel")!.IsVisible, Is.True);

            DesktopWindowScope.Click(scope.Window.FindControl<Button>("AddToolButton")!);
            Window catalog = scope.Window.OwnedWindows.Single();
            await DesktopWindowScope.FrameAsync(catalog).ConfigureAwait(true);
            DesktopWindowScope.Click(catalog.FindControl<Button>("CancelButton")!);
            await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
            Assert.That(scope.Window.OwnedWindows, Is.Empty);
            Assert.That(scope.ViewModel.Tabs, Is.Empty, "Cancelling the catalog must not create a tool.");
            DesktopWindowScope.Click(scope.Window.FindControl<Button>("AddToolButton")!);
            catalog = scope.Window.OwnedWindows.Single();
            var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            catalog.Closed += (_, _) => dismissed.TrySetResult();
            await DesktopWindowScope.FrameAsync(catalog).ConfigureAwait(true);
            DesktopWindowScope.Key(catalog.FindControl<TextBox>("SearchBox")!, Key.Escape);
            await dismissed.Task.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true);
            Assert.That(scope.Window.OwnedWindows, Is.Empty);
            Assert.That(scope.ViewModel.Tabs, Is.Empty, "Escape must dismiss the catalog without opening a document.");
        });
    }

    [Test]
    public Task RoutedKeyboardCyclesDocumentsWrapsAndRenamesWithoutStealingTextInput()
    {
        return DesktopApplication.RunAsync(async () =>
        {
            await using DesktopWindowScope scope = await DesktopWindowScope.OpenAsync().ConfigureAwait(true);
            IPlugin first = await OpenFromCatalogAsync(scope, PluginKind.Subscription).ConfigureAwait(true);
            IPlugin second = await OpenFromCatalogAsync(scope, PluginKind.EventView).ConfigureAwait(true);
            IPlugin third = await OpenFromCatalogAsync(scope, PluginKind.Historian).ConfigureAwait(true);
            var strip = scope.Window.FindControl<ListBox>("TabStrip")!;
            Assert.That(strip.Focus(), Is.True);
            await SelectByKeyAsync(scope, strip, first, KeyModifiers.Control).ConfigureAwait(true);
            await SelectByKeyAsync(scope, strip, third, KeyModifiers.Control | KeyModifiers.Shift)
                .ConfigureAwait(true);
            await SelectByKeyAsync(scope, strip, second, KeyModifiers.Control | KeyModifiers.Shift)
                .ConfigureAwait(true);
            TextBox editor = strip.GetVisualDescendants().OfType<TextBox>()
                .Single(control => control.Name == "TabTitleEdit" && control.DataContext == second);
            await DesktopWindowScope.ControlChangeAsync(editor, () => editor.IsFocused,
                () => Assert.That(DesktopWindowScope.Key(strip, Key.F2).Handled, Is.True)).ConfigureAwait(true);
            await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
            Assert.That(editor.IsFocused, Is.True, "F2 must move focus to the selected document's inline editor.");
            string originalTitle = second.Title;
            editor.Text = "Keyboard renamed events";
            Assert.That(second.Title, Is.EqualTo(originalTitle), "Typing must not commit the rename draft.");
            Assert.That(DesktopWindowScope.Key(editor, Key.W, KeyModifiers.Control).Handled, Is.False,
                "Document close must not steal a text-editing key route.");
            Assert.That(scope.ViewModel.Tabs, Has.Count.EqualTo(3));
            DesktopWindowScope.Key(editor, Key.Enter);
            await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
            Assert.That(second.IsRenaming, Is.False);
            Assert.That(second.Title, Is.EqualTo("Keyboard renamed events"));
            Assert.That(strip.Focus(), Is.True);
            await DesktopWindowScope.ControlChangeAsync(editor, () => editor.IsFocused,
                () => DesktopWindowScope.Key(strip, Key.F2)).ConfigureAwait(true);
            await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
            editor.Text = "Discard this draft";
            DesktopWindowScope.Key(editor, Key.Escape);
            await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
            Assert.That(second.Title, Is.EqualTo("Keyboard renamed events"));
            Assert.That(second.IsRenaming, Is.False);
            Assert.That(scope.Window.FocusManager.GetFocusedElement(), Is.SameAs(strip));
            await DesktopWindowScope.ChangeAsync(scope.ViewModel,
                () => scope.ViewModel.Tabs.Count == 2 && scope.ViewModel.SelectedTab != second,
                () => Assert.That(DesktopWindowScope.Key(strip, Key.W, KeyModifiers.Control).Handled, Is.True))
                .ConfigureAwait(true);
            await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
            Assert.That(scope.ViewModel.Tabs, Does.Not.Contain(second));
            Assert.That(scope.Window.FindControl<ContentControl>("DocumentHost")!.Content,
                Is.SameAs(scope.ViewModel.SelectedTab!.View));
        });
    }

    [Test]
    public Task WorkspaceMenusSaveAndLoadActualDocumentsAndSelectionOffline()
    {
        return DesktopApplication.RunAsync(async () =>
        {
            var file = new Mock<IStorageFile>(MockBehavior.Strict);
            var storage = new Mock<IStorageProvider>(MockBehavior.Strict);
            storage.Setup(provider => provider.SaveFilePickerAsync(It.Is<FilePickerSaveOptions>(
                options => options.DefaultExtension == "subex"))).ReturnsAsync(file.Object);
            storage.Setup(provider => provider.OpenFilePickerAsync(It.Is<FilePickerOpenOptions>(
                options => !options.AllowMultiple)))
                .ReturnsAsync((IReadOnlyList<IStorageFile>)[file.Object]);
            await using DesktopWindowScope scope = await DesktopWindowScope.OpenAsync(storage.Object)
                .ConfigureAwait(true);
            string path = Path.Combine(scope.DirectoryPath, "workspace.subex");
            file.SetupGet(item => item.Path).Returns(new Uri(path));
            file.SetupGet(item => item.Name).Returns("workspace.subex");
            IPlugin monitor = await OpenFromCatalogAsync(scope, PluginKind.Subscription).ConfigureAwait(true);
            IPlugin events = await OpenFromCatalogAsync(scope, PluginKind.EventView).ConfigureAwait(true);
            monitor.Title = "Saved monitor";
            events.Title = "Saved events";
            scope.ViewModel.SelectedTab = monitor;
            DesktopWindowScope.Key(scope.Window, Key.B, KeyModifiers.Control);
            await DesktopWindowScope.ChangeAsync(scope.ViewModel,
                () => scope.ViewModel.ConnectionStatus.StartsWith("Workspace saved to", StringComparison.Ordinal),
                () => Assert.That(DesktopWindowScope.Key(scope.Window, Key.S, KeyModifiers.Control).Handled, Is.True))
                .ConfigureAwait(true);
            Assert.That(File.Exists(path), Is.True);
            SessionFile? saved = await SessionFile.LoadAsync(path).ConfigureAwait(true);
            Assert.That(saved, Is.Not.Null);
            Assert.That(saved!.Documents.Select(document => document.Title),
                Is.EqualTo(s_savedTitles));
            Assert.That(saved.SelectedDocument, Is.Zero);
            Assert.That(saved.ShowAddressSpace, Is.False);
            await CloseFromStripAsync(scope, events).ConfigureAwait(true);
            await CloseFromStripAsync(scope, monitor).ConfigureAwait(true);
            await DesktopWindowScope.ChangeAsync(scope.ViewModel,
                () => scope.ViewModel.ConnectionStatus.StartsWith(
                    "Workspace restored offline", StringComparison.Ordinal),
                () => Assert.That(DesktopWindowScope.Key(scope.Window, Key.O, KeyModifiers.Control).Handled, Is.True))
                .ConfigureAwait(true);
            await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
            Assert.That(scope.ViewModel.Tabs.Select(document => document.Title),
                Is.EqualTo(s_savedTitles));
            Assert.That(scope.ViewModel.SelectedTab, Is.SameAs(scope.ViewModel.Tabs[0]).And.Not.SameAs(monitor));
            Assert.That(scope.ViewModel.IsAddressSpaceVisible, Is.False);
            Assert.That(scope.ViewModel.Connection.IsConnected, Is.False);
            Assert.That(scope.Window.FindControl<ContentControl>("DocumentHost")!.Content,
                Is.SameAs(scope.ViewModel.SelectedTab!.View));
            Assert.That(scope.Window.FindControl<Border>("OperationErrorBanner")!.IsVisible, Is.False);
            storage.VerifyAll();
        });
    }

    [Test]
    public Task ShutdownCancelsOwnedModalAndWaitsForDocumentCleanupBeforeClosingShell()
    {
        return DesktopApplication.RunAsync(async () =>
        {
            await using DesktopWindowScope scope = await DesktopWindowScope.OpenAsync().ConfigureAwait(true);
            IPlugin document = await OpenFromCatalogAsync(scope, PluginKind.Subscription).ConfigureAwait(true);
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var modal = new EndpointPickerDialog([]);
            modal.Opened += (_, _) => opened.TrySetResult();
            Task ownedWork = scope.ViewModel.Workspace.ConfigureAsync(document, async (_, cancellationToken) =>
            {
                try
                {
                    await EndpointCredentialsPicker.ShowCancelableAsync<(EndpointDescription, UserTokenPolicy?)?>(
                        modal, scope.Window, cancellationToken).ConfigureAwait(true);
                }
                finally
                {
                    Assert.That(cancellationToken.IsCancellationRequested, Is.True);
                    cancelled.TrySetResult();
                    await releaseCleanup.Task.WaitAsync(DesktopApplication.Timeout, CancellationToken.None)
                        .ConfigureAwait(true);
                }
            });
            try
            {
                await opened.Task.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true);
                await DesktopWindowScope.FrameAsync(modal).ConfigureAwait(true);
                Assert.That(scope.Window.OwnedWindows, Does.Contain(modal));
                scope.Window.Close();
                await cancelled.Task.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true);
                Assert.That(modal.IsVisible, Is.False, "Shutdown must dismiss the cancelled document-owned modal.");
                Assert.That(scope.ViewModel.Workspace.IsClosing, Is.True);
                Assert.That(scope.Closed.IsCompleted, Is.False,
                    "The shell must stay alive until task cleanup drains.");
                Assert.That(scope.Window.IsVisible, Is.True);
            }
            finally
            {
                releaseCleanup.TrySetResult();
            }
            await Assert.ThatAsync(() => ownedWork.WaitAsync(DesktopApplication.Timeout),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(true);
            await scope.Closed.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true);
            Assert.That(scope.ViewModel.Tabs, Is.Empty);
            Assert.That(scope.Window.OwnedWindows, Is.Empty);
            Assert.That(scope.Window.TryGetPlatformHandle(), Is.Null);
        });
    }

    [Test]
    public Task InspectorWorkerCompletionsPublishRowsOnTheNativeDispatcher()
    {
        return DesktopApplication.RunAsync(async () =>
        {
            await using DesktopWindowScope scope = await DesktopWindowScope.OpenAsync().ConfigureAwait(true);
            int uiThread = Environment.CurrentManagedThreadId;
            var attributeRead = new TaskCompletionSource<ReadResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var referenceRead = new TaskCompletionSource<ReadResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            ArrayOf<ReadValueId> requested = [];
            var attributeSession = new Mock<ISession>(MockBehavior.Strict);
            attributeSession.SetupGet(session => session.MessageContext)
                .Returns(ServiceMessageContext.Create(scope.ViewModel.Telemetry));
            attributeSession.Setup(session => session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Callback<RequestHeader?, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                    (_, _, _, ids, _) => requested = ids)
                .Returns(new ValueTask<ReadResponse>(attributeRead.Task));
            var referenceSession = new Mock<ISession>(MockBehavior.Strict);
            referenceSession.Setup(session => session.BrowseAsync(
                null, null, 0, It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            References =
                            [
                                new ReferenceDescription
                                {
                                    NodeId = new ExpandedNodeId("worker-child", 0),
                                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                                    DisplayName = new LocalizedText("Worker child"),
                                    NodeClass = NodeClass.Object,
                                    IsForward = true
                                }
                            ]
                        }
                    ]
                });
            referenceSession.Setup(session => session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ReadResponse>(referenceRead.Task));
            var dispatcher = new AvaloniaWorkspaceDispatcher();
            using var attributes = new NodeAttributesViewModel(
                scope.ViewModel.Telemetry, () => attributeSession.Object, dispatcher);
            using var references = new ReferencesViewModel(
                scope.ViewModel.Telemetry, () => referenceSession.Object, dispatcher);
            int attributeNotifications = 0;
            int referenceNotifications = 0;
            attributes.Rows.CollectionChanged += (_, _) =>
            {
                Assert.That(Dispatcher.UIThread.CheckAccess(), Is.True);
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(uiThread));
                attributeNotifications++;
            };
            references.Rows.CollectionChanged += (_, _) =>
            {
                Assert.That(Dispatcher.UIThread.CheckAccess(), Is.True);
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(uiThread));
                referenceNotifications++;
            };
            var attributeView = new NodeAttributesView { DataContext = attributes };
            var referenceView = new ReferencesView { DataContext = references };
            scope.Window.FindControl<ContentControl>("AttrsPanel")!.Content = attributeView;
            scope.Window.FindControl<ContentControl>("RefsPanel")!.Content = referenceView;
            scope.ViewModel.AttributesPanelMode = SidePanelMode.AttrsAndRefs;
            Task attributeLoading = attributes.LoadAsync(new NodeId("worker-attributes", 0), NodeClass.Object);
            Task referenceLoading = references.LoadAsync(new NodeId("worker-references", 0), NodeClass.Object);
            Assert.That(attributeLoading.IsCompleted, Is.False);
            Assert.That(referenceLoading.IsCompleted, Is.False);
            Assert.That(requested.Count, Is.GreaterThan(0));
            int workerThread = await Task.Run(() =>
            {
                attributeRead.SetResult(new ReadResponse
                {
                    Results = requested.ToList().Select(_ => new DataValue(Variant.From("worker value"))).ToArray()
                });
                referenceRead.SetResult(new ReadResponse
                {
                    Results = [new DataValue(Variant.From(new QualifiedName("HasComponentFromWorker")))]
                });
                return Environment.CurrentManagedThreadId;
            }).ConfigureAwait(true);
            Assert.That(workerThread, Is.Not.EqualTo(uiThread),
                "The service response must originate off the UI thread.");
            await Task.WhenAll(attributeLoading, referenceLoading).WaitAsync(DesktopApplication.Timeout)
                .ConfigureAwait(true);
            await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
            Assert.That(attributes.Rows, Has.Count.EqualTo(requested.Count));
            Assert.That(references.Rows, Has.Count.EqualTo(1));
            Assert.That(references.Rows[0].ReferenceType, Is.EqualTo("HasComponentFromWorker"));
            Assert.That(attributeNotifications, Is.GreaterThan(requested.Count));
            Assert.That(referenceNotifications, Is.GreaterThan(1));
            Assert.That(attributeView.FindControl<ListBox>("AttributesList")!.Items,
                Has.Count.EqualTo(requested.Count));
            Assert.That(referenceView.FindControl<ListBox>("ReferenceList")!.Items,
                Has.Count.EqualTo(1));
            attributes.Clear();
            references.Clear();
            Assert.That(attributes.Rows, Is.Empty);
            Assert.That(references.Rows, Is.Empty);
            attributeSession.VerifyAll();
            referenceSession.VerifyAll();
        });
    }

    [TestCase("Light", false)]
    [TestCase("Light", true)]
    [TestCase("DarkStandard", false)]
    [TestCase("DarkStandard", true)]
    [TestCase("System", false)]
    [TestCase("System", true)]
    public Task MinimumSizeKeepsShellActionsReachableAndExplorerCollapseRecoversMonitorLayout(
        string themeName,
        bool diagnostics)
    {
        return DesktopApplication.RunAsync(async () =>
        {
            await using DesktopWindowScope scope = await DesktopWindowScope.OpenAsync().ConfigureAwait(true);
            ThemePreset theme = Enum.Parse<ThemePreset>(themeName);
            await ThemeManager.SetThemeAsync(theme, scope.Appearance).ConfigureAwait(true);
            IPlugin document = await OpenFromCatalogAsync(scope, PluginKind.Subscription).ConfigureAwait(true);
            var toggle = scope.Window.FindControl<ToggleButton>("ToggleDiagButton")!;
            toggle.IsChecked = diagnostics;
            await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
            Assert.That(scope.Window.ClientSize.Width, Is.EqualTo(scope.Window.MinWidth).Within(1));
            Assert.That(scope.Window.ClientSize.Height, Is.EqualTo(scope.Window.MinHeight).Within(1));
            Assert.That(ThemeManager.Current, Is.EqualTo(theme));
            if (theme != ThemePreset.System)
            {
                Assert.That(scope.Window.ActualThemeVariant,
                    Is.EqualTo(theme == ThemePreset.Light ? ThemeVariant.Light : ThemeVariant.Dark));
            }
            Assert.That(scope.Window.Background, Is.InstanceOf<ISolidColorBrush>());
            Assert.That(((ISolidColorBrush)scope.Window.Background!).Color,
                Is.EqualTo(ThemeManager.GetColor("AppBg", default)),
                "The live window background must follow the selected semantic theme resources.");
            foreach (string name in s_shellActions)
            {
                AssertWithinClient(scope.Window, scope.Window.FindControl<Control>(name)!);
            }
            foreach ((string name, string label) in s_shellNames)
            {
                Control control = scope.Window.FindControl<Control>(name)!;
                AssertWithinClient(scope.Window, control);
                Assert.That(ControlAutomationPeer.CreatePeerForElement(control).GetName(), Is.EqualTo(label));
            }
            var endpoint = scope.Window.FindControl<TextBox>("EndpointBox")!;
            Assert.That(endpoint.Focus(), Is.True);
            Assert.That(scope.Window.FocusManager.GetFocusedElement(), Is.SameAs(endpoint));
            var host = scope.Window.FindControl<ContentControl>("DocumentHost")!;
            double narrowWidth = host.Bounds.Width;
            Assert.That(narrowWidth, Is.GreaterThan(0));
            DesktopWindowScope.Key(scope.Window, Key.B, KeyModifiers.Control);
            await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
            Assert.That(scope.ViewModel.IsAddressSpaceVisible, Is.False);
            Assert.That(host.Bounds.Width, Is.GreaterThan(narrowWidth + 300),
                "Collapsing the explorer must recover document space at minimum width.");
            AssertWithinClient(scope.Window, host);
            var monitor = (SubscriptionDocumentView)document.View!;
            Assert.That(host.Bounds.Width, Is.GreaterThan(400), "The monitor's fixed columns need at least 400 DIP.");
            AssertWithinClient(scope.Window, monitor.FindControl<ComboBox>("DocumentViewMode")!);
            AssertWithinClient(scope.Window, monitor.FindControl<Button>("SubscriptionSettingsButton")!);
            AssertWithinClient(scope.Window, monitor.FindControl<ListBox>("MonitorValuesList")!);
            AssertWithinDocument(host, monitor.FindControl<ComboBox>("DocumentViewMode")!);
            AssertWithinDocument(host, monitor.FindControl<Button>("SubscriptionSettingsButton")!);
            AssertWithinDocument(host, monitor.FindControl<ListBox>("MonitorValuesList")!);
            var mode = monitor.FindControl<ComboBox>("DocumentViewMode")!;
            Assert.That(mode.Focus(), Is.True);
            Assert.That(scope.Window.FocusManager.GetFocusedElement(), Is.SameAs(mode));
            Assert.That(scope.Window.FindControl<DiagnosticsView>("DiagnosticsPanel")!.IsVisible,
                Is.EqualTo(diagnostics));
        });
    }

    private static async Task<IPlugin> OpenFromCatalogAsync(DesktopWindowScope scope, PluginKind kind)
    {
        int count = scope.ViewModel.Tabs.Count;
        DesktopWindowScope.Click(scope.Window.FindControl<Button>("AddToolButton")!);
        var catalog = (ToolCatalogDialog)scope.Window.OwnedWindows.Single();
        await DesktopWindowScope.FrameAsync(catalog).ConfigureAwait(true);
        TextBox search = catalog.FindControl<TextBox>("SearchBox")!;
        Assert.That(search.IsFocused, Is.True, "The tool catalog must put keyboard focus in search.");
        search.Text = PluginRegistry.For(kind).DisplayName;
        await DesktopWindowScope.FrameAsync(catalog).ConfigureAwait(true);
        Button choice = catalog.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Tag is ToolCatalogEntry entry && entry.Kind == kind);
        await DesktopWindowScope.ChangeAsync(scope.ViewModel,
            () => scope.ViewModel.Tabs.Count == count + 1 && scope.ViewModel.SelectedTab?.Kind == kind,
            () => DesktopWindowScope.Click(choice)).ConfigureAwait(true);
        await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
        Assert.That(scope.Window.OwnedWindows, Is.Empty);
        Assert.That(scope.ViewModel.SelectedTab, Is.Not.Null);
        return scope.ViewModel.SelectedTab!;
    }

    private static async Task CloseFromStripAsync(DesktopWindowScope scope, IPlugin document)
    {
        scope.ViewModel.SelectedTab = document;
        await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
        Button close = scope.Window.FindControl<ListBox>("TabStrip")!.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Name == "CloseTabButton" && button.Tag == document);
        await DesktopWindowScope.ChangeAsync(scope.ViewModel,
            () => !scope.ViewModel.Tabs.Contains(document),
            () => DesktopWindowScope.Click(close)).ConfigureAwait(true);
        Assert.That(scope.ViewModel.CloseTabCommand.ExecutionTask, Is.Not.Null);
        await scope.ViewModel.CloseTabCommand.ExecutionTask!.WaitAsync(DesktopApplication.Timeout)
            .ConfigureAwait(true);
        await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
    }

    private static async Task SelectByKeyAsync(
        DesktopWindowScope scope,
        InputElement source,
        IPlugin expected,
        KeyModifiers modifiers)
    {
        await DesktopWindowScope.ChangeAsync(scope.ViewModel,
            () => scope.ViewModel.SelectedTab == expected,
            () => Assert.That(DesktopWindowScope.Key(source, Key.Tab, modifiers).Handled, Is.True))
            .ConfigureAwait(true);
        await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
        Assert.That(scope.Window.FindControl<ListBox>("TabStrip")!.SelectedItem, Is.SameAs(expected));
        Assert.That(scope.Window.FindControl<ContentControl>("DocumentHost")!.Content, Is.SameAs(expected.View));
    }

    private static void AssertWithinClient(Window window, Control control)
    {
        Assert.That(control.IsEffectivelyVisible, Is.True, $"{control.Name} is hidden.");
        Assert.That(control.Bounds.Width, Is.GreaterThan(0), $"{control.Name} has no horizontal layout.");
        Assert.That(control.Bounds.Height, Is.GreaterThan(0), $"{control.Name} has no vertical layout.");
        Point? position = control.TranslatePoint(default, window);
        Assert.That(position, Is.Not.Null, $"{control.Name} is detached from the desktop.");
        var bounds = new Rect(position!.Value, control.Bounds.Size);
        Assert.That(bounds.Left, Is.GreaterThanOrEqualTo(-1), $"{control.Name} extends left of the client area.");
        Assert.That(bounds.Top, Is.GreaterThanOrEqualTo(-1), $"{control.Name} extends above the client area.");
        Assert.That(bounds.Right, Is.LessThanOrEqualTo(window.ClientSize.Width + 1),
            $"{control.Name} extends right of the client area.");
        Assert.That(bounds.Bottom, Is.LessThanOrEqualTo(window.ClientSize.Height + 1),
            $"{control.Name} extends below the client area.");
    }

    private static void AssertWithinDocument(Control host, Control control)
    {
        Point? position = control.TranslatePoint(default, host);
        Assert.That(position, Is.Not.Null);
        var bounds = new Rect(position!.Value, control.Bounds.Size);
        Assert.That(bounds.Left, Is.GreaterThanOrEqualTo(-1), $"{control.Name} is clipped by its document.");
        Assert.That(bounds.Top, Is.GreaterThanOrEqualTo(-1), $"{control.Name} is clipped by its document.");
        Assert.That(bounds.Right, Is.LessThanOrEqualTo(host.Bounds.Width + 1),
            $"{control.Name} extends into another shell pane.");
        Assert.That(bounds.Bottom, Is.LessThanOrEqualTo(host.Bounds.Height + 1),
            $"{control.Name} extends below its document viewport.");
    }

    private static readonly string[] s_savedTitles = ["Saved monitor", "Saved events"];
    private static readonly string[] s_shellActions =
        ["EndpointBox", "ConnectButton", "AddToolButton", "ToggleDiagButton"];
    private static readonly (string Name, string Label)[] s_shellNames =
    [
        ("EndpointBox", "Server endpoint URL"),
        ("EndpointHistoryButton", "Recent and favourite endpoints"),
        ("FavoriteToggle", "Toggle endpoint favourite"),
        ("ConnectionSettingsButton", "Connection settings")
    ];
}
