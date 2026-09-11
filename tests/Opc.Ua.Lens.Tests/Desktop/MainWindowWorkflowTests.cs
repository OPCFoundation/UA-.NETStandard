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
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Capabilities;
using UaLens.Connection;
using UaLens.Themes;
using UaLens.ViewModels;
using UaLens.Views;
using UaLens.Workspace;

namespace UaLens.Tests.Desktop;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class MainWindowWorkflowTests
{
    [Test]
    public Task WelcomeAndHistoryShowOnlyTheOwnedFavoriteEndpoints()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ShellContext();
            await FavoritesStore.SaveAsync(s_favorites, path: context.FavoritesPath).ConfigureAwait(true);
            MainWindow window = context.Show();
            ListBox recent = DesktopInteraction.Control<ListBox>(window, "WelcomeRecentList");
            await DesktopInteraction.CollectionChangedAsync(
                recent.Items, () => recent.Items.Count == 2, () => { }).ConfigureAwait(true);
            Assert.That(recent.Items.Cast<string>(), Is.EqualTo(s_favorites));
            Assert.That(DesktopInteraction.Control<ScrollViewer>(window, "WelcomePanel").IsVisible, Is.True);
            Assert.That(DesktopInteraction.Control<ContentControl>(window, "DocumentHost").IsVisible, Is.False);
            Assert.That(context.Model.Tabs, Is.Empty);
            recent.SelectedIndex = 1;
            Assert.That(context.Model.EndpointUrl, Is.EqualTo(s_favorites[1]));
            var history = DesktopInteraction.Control<Button>(window, "EndpointHistoryButton");
            DesktopInteraction.Click(history);
            var flyout = (MenuFlyout)history.Flyout!;
            MenuItem[] entries = flyout.Items.OfType<MenuItem>().ToArray();
            Assert.That(entries.Select(entry => entry.Header), Is.EqualTo(s_history));
            Assert.That(entries[0].IsEnabled, Is.False);
            entries[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(context.Model.EndpointUrl, Is.EqualTo(s_favorites[0]));
            Assert.That(context.Connection.Discoveries, Is.Empty);
            Assert.That(context.Connection.Connection.CurrentSession, Is.Null);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task FavoriteWritesAreDrainedAtShutdownAndNeverMutateAnotherWorkspace(bool remove)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ShellContext();
            await FavoritesStore.SaveAsync(s_favorites, path: context.FavoritesPath).ConfigureAwait(true);
            MainWindow window = context.Show();
            ListBox recent = DesktopInteraction.Control<ListBox>(window, "WelcomeRecentList");
            await DesktopInteraction.CollectionChangedAsync(
                recent.Items, () => recent.Items.Count == 2, () => { }).ConfigureAwait(true);
            context.Model.EndpointUrl = remove ? s_favorites[0] : "opc.tcp://new.example.test:4840/Line";
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(window, "FavoriteToggle"));
            await window.DisposeAsync().ConfigureAwait(true);
            List<string> saved = await FavoritesStore.LoadAsync(path: context.FavoritesPath).ConfigureAwait(true);
            Assert.That(saved, Is.EqualTo(remove ? s_afterRemove : s_afterAdd));
            Assert.That(context.Connection.Connection.CurrentSession, Is.Null);
        });
    }

    [Test]
    public Task InvalidFavoriteAndMalformedOwnedPreferenceSurfaceErrorsWithoutOverwritingData()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ShellContext();
            await File.WriteAllTextAsync(context.FavoritesPath, "{invalid").ConfigureAwait(true);
            MainWindow window = context.Show();
            await DesktopInteraction.ModelChangedAsync(context.Model,
                () => context.Model.ConnectionStatus.StartsWith("Favourites could not be loaded",
                    StringComparison.Ordinal), () => Task.CompletedTask).ConfigureAwait(true);
            Assert.That(await File.ReadAllTextAsync(context.FavoritesPath).ConfigureAwait(true), Is.EqualTo("{invalid"));
            context.Model.EndpointUrl = "not-an-endpoint";
            await DesktopInteraction.ModelChangedAsync(context.Model,
                () => context.Model.ConnectionStatus.StartsWith("Enter an absolute", StringComparison.Ordinal), () =>
                {
                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(window, "FavoriteToggle"));
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
            Assert.That(await File.ReadAllTextAsync(context.FavoritesPath).ConfigureAwait(true), Is.EqualTo("{invalid"));
            Assert.That(DesktopInteraction.Control<ListBox>(window, "WelcomeRecentList").Items, Is.Empty);
        });
    }

    [TestCase(Key.B, KeyModifiers.Control, "AddressSpace")]
    [TestCase(Key.F, KeyModifiers.Control | KeyModifiers.Shift, "Filters")]
    [TestCase(Key.G, KeyModifiers.Control | KeyModifiers.Shift, "Diagnostics")]
    [TestCase(Key.L, KeyModifiers.Control, "Log")]
    public Task WindowShortcutsToggleOnlyTheirOwnPanelAndDoNotStealTyping(
        Key key, KeyModifiers modifiers, string panel)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ShellContext();
            MainWindow window = context.Show();
            bool initial = Visible(context, panel);
            TextBox input = DesktopInteraction.Control<TextBox>(window, "EndpointBox");
            var typing = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = input
            };
            input.RaiseEvent(typing);
            Assert.That(Visible(context, panel), Is.EqualTo(initial));
            var gesture = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = window
            };
            window.RaiseEvent(gesture);
            await FlushAsync().ConfigureAwait(true);
            Assert.That(gesture.Handled, Is.True);
            Assert.That(Visible(context, panel), Is.EqualTo(!initial));
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = window
            });
            await FlushAsync().ConfigureAwait(true);
            Assert.That(Visible(context, panel), Is.EqualTo(initial));
        });
    }

    [TestCase(false, false, (int)SidePanelMode.None)]
    [TestCase(true, false, (int)SidePanelMode.AttrsOnly)]
    [TestCase(false, true, (int)SidePanelMode.RefsOnly)]
    [TestCase(true, true, (int)SidePanelMode.AttrsAndRefs)]
    public Task InspectorMenusControlBothRowsAndTheSelectedMode(bool attributes, bool references, int mode)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ShellContext();
            MainWindow window = context.Show();
            MenuItem attrs = DesktopInteraction.Control<MenuItem>(window, "MenuToggleAttrs");
            MenuItem refs = DesktopInteraction.Control<MenuItem>(window, "MenuToggleRefs");
            attrs.IsChecked = attributes;
            refs.IsChecked = references;
            attrs.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await FlushAsync().ConfigureAwait(true);
            Assert.That(context.Model.AttributesPanelMode, Is.EqualTo((SidePanelMode)mode));
            Assert.That(context.Model.ShowAttributes, Is.EqualTo(attributes));
            Assert.That(context.Model.ShowReferences, Is.EqualTo(references));
            Grid grid = DesktopInteraction.Control<Grid>(window, "LeftStackGrid");
            Assert.That(grid.RowDefinitions[4].Height.IsStar, Is.EqualTo(attributes));
            Assert.That(grid.RowDefinitions[6].Height.IsStar, Is.EqualTo(references));
            Assert.That(grid.RowDefinitions[5].Height.Value, Is.EqualTo(attributes && references ? 4 : 0));
        });
    }

    [TestCase((int)PluginKind.GdsDiscovery, "WelcomeDiscoveryBtn")]
    [TestCase((int)PluginKind.CertificateManager, "WelcomeCertificatesBtn")]
    public Task WelcomeActionsCreateTheCorrectDocumentAndReplaceTheWelcomePage(int kind, string button)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ShellContext();
            MainWindow window = context.Show();
            await DesktopInteraction.CollectionChangedAsync(context.Model.Tabs, () => context.Model.Tabs.Count == 1,
                () => DesktopInteraction.Click(DesktopInteraction.Control<Button>(window, button))).ConfigureAwait(true);
            await FlushAsync().ConfigureAwait(true);
            Assert.That(context.Created.Single().Object.Kind, Is.EqualTo((PluginKind)kind));
            Assert.That(context.Model.SelectedTab, Is.SameAs(context.Created[0].Object));
            Assert.That(DesktopInteraction.Control<ScrollViewer>(window, "WelcomePanel").IsVisible, Is.False);
            Assert.That(DesktopInteraction.Control<ContentControl>(window, "DocumentHost").IsVisible, Is.True);
            MenuItem actions = DesktopInteraction.Control<MenuItem>(window, "MenuActiveToolActions");
            Assert.That(actions.IsEnabled, Is.True);
            Assert.That(actions.Items.OfType<MenuItem>().Single().Header, Is.EqualTo("Owned action"));
            actions.Items.OfType<MenuItem>().Single().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(context.ToolActions, Is.EqualTo(1));
            Assert.That(context.Connection.Discoveries, Is.Empty);
            await context.Model.CloseTabCommand.ExecuteAsync(context.Model.SelectedTab).ConfigureAwait(true);
            await FlushAsync().ConfigureAwait(true);
            Assert.That(context.Released, Is.EqualTo(1));
            Assert.That(context.Model.Tabs, Is.Empty);
            Assert.That(actions.IsEnabled, Is.False);
            Assert.That(DesktopInteraction.Control<ScrollViewer>(window, "WelcomePanel").IsVisible, Is.True);
        });
    }

    [Test]
    public Task RegistryShortcutsCycleRenameAndCloseTheSelectedDocument()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ShellContext();
            MainWindow window = context.Show();
            IPlugin first = await context.Model.OpenToolAsync(PluginKind.Historian).ConfigureAwait(true);
            IPlugin second = await context.Model.OpenToolAsync(PluginKind.Performance).ConfigureAwait(true);
            await FlushAsync().ConfigureAwait(true);
            Assert.That(context.Model.SelectedTab, Is.SameAs(second));
            var activations = new List<string>();
            context.Model.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.SelectedTab))
                {
                    activations.Add(context.Model.SelectedTab?.Title ?? "(none)");
                }
            };
            Press(window, Key.Tab, KeyModifiers.Control);
            Assert.That(context.Model.SelectedTab, Is.SameAs(first),
                "Immediate activations: " + string.Join(", ", activations));
            await FlushAsync().ConfigureAwait(true);
            Assert.That(context.Model.SelectedTab, Is.SameAs(first),
                "After dispatcher drain: " + string.Join(", ", activations));
            Press(window, Key.F2);
            Assert.That(first.IsRenaming, Is.True);
            first.Title = "Renamed history";
            first.IsRenaming = false;
            Press(window, Key.Tab, KeyModifiers.Control | KeyModifiers.Shift);
            await FlushAsync().ConfigureAwait(true);
            Assert.That(context.Model.SelectedTab, Is.SameAs(second));
            Press(window, Key.W, KeyModifiers.Control);
            await FlushAsync().ConfigureAwait(true);
            Assert.That(context.Model.Tabs.Single(), Is.SameAs(first));
            Assert.That(context.Model.SelectedTab!.Title, Is.EqualTo("Renamed history"));
            Assert.That(context.Released, Is.EqualTo(1));
        });
    }

    [TestCase(Key.None)]
    [TestCase(Key.LeftCtrl)]
    [TestCase(Key.LeftShift)]
    [TestCase(Key.LeftAlt)]
    [TestCase(Key.F12)]
    public Task UnmappedKeysDoNotDispatchACommand(Key key)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ShellContext();
            MainWindow window = context.Show();
            var input = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = window };
            window.RaiseEvent(input);
            Assert.That(input.Handled, Is.False);
            Assert.That(context.Model.Tabs, Is.Empty);
            Assert.That(context.Connection.Discoveries, Is.Empty);
        });
    }

    [Test]
    public Task ClosingWaitsForDocumentCleanupBeforeClosingTheMainWindow()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ShellContext();
            MainWindow window = context.Show();
            IPlugin document = await context.Model.OpenToolAsync(PluginKind.Historian).ConfigureAwait(true);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock.Get(document).Setup(plugin => plugin.DisposeAsync()).Returns(() =>
            {
                entered.TrySetResult();
                return new ValueTask(release.Task);
            });
            try
            {
                window.Close();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                Assert.That(window.IsVisible, Is.True);
                Assert.That(window.IsClosingRequested, Is.True);
                release.SetResult();
                await window.DisposeAsync().ConfigureAwait(true);
                await FlushAsync().ConfigureAwait(true);
                Assert.That(window.IsVisible, Is.False);
            }
            finally
            {
                release.TrySetResult();
            }
        });
    }

    [Test]
    public Task HelpAndConnectionSettingsRemainLocalAndUseTheSharedCatalog()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ShellContext();
            MainWindow window = context.Show();
            Window help = await DesktopInteraction.OpenedAsync<Window>(() => Press(window, Key.F1)).ConfigureAwait(true);
            Assert.That(help.Owner, Is.SameAs(window));
            Assert.That(((TextBlock)((ScrollViewer)help.Content!).Content!).Text,
                Does.Contain("Ctrl+Tab").And.Contain("Ctrl+N").And.Contain("Ctrl+W"));
            help.Close();
            var settings = DesktopInteraction.Control<Button>(window, "ConnectionSettingsButton");
            DesktopInteraction.Click(settings);
            MenuItem[] options = ((MenuFlyout)settings.Flyout!).Items.OfType<MenuItem>().ToArray();
            Assert.That(options.Select(option => option.Header), Is.EqualTo(s_connectionOptions));
            Assert.That(options[0].IsEnabled, Is.True);
            Assert.That(options[2].IsEnabled, Is.False);
            bool initial = context.Model.UseChannelV2Engine;
            options[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(context.Model.UseChannelV2Engine, Is.EqualTo(!initial));
            Assert.That(context.Connection.Discoveries, Is.Empty);
            MenuItem tools = DesktopInteraction.Control<MenuItem>(window, "MenuOpenTool");
            int catalogItems = tools.Items.OfType<MenuItem>().Sum(group => group.Items.Count);
            Assert.That(catalogItems, Is.EqualTo(PluginRegistry.All.Count));
        });
    }

    private static bool Visible(ShellContext context, string panel)
    {
        return panel switch
        {
            "AddressSpace" => context.Model.IsAddressSpaceVisible,
            "Filters" => context.Model.Browser.ShowFilters,
            "Diagnostics" => DesktopInteraction.Control<DiagnosticsView>(context.Window!, "DiagnosticsPanel").IsVisible,
            _ => DesktopInteraction.Control<Border>(context.Window!, "LogPanel").IsVisible
        };
    }

    private static void Press(MainWindow window, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var input = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = window
        };
        window.RaiseEvent(input);
        Assert.That(input.Handled, Is.True);
    }

    private static async Task FlushAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private sealed class ShellContext : IAsyncDisposable
    {
        public ShellContext()
        {
            Root = Directory.CreateTempSubdirectory("UaLensShell").FullName;
            FavoritesPath = Path.Combine(Root, "favorites.json");
            var factory = new Mock<IPluginFactory>(MockBehavior.Strict);
            factory.Setup(value => value.Create(It.IsAny<PluginKind>(), It.IsAny<PluginHost>(),
                It.IsAny<Func<PluginHost, IPlugin>>())).Returns(
                (PluginKind kind, PluginHost _, Func<PluginHost, IPlugin> _) =>
                {
                    var plugin = new Mock<IPlugin>();
                    plugin.SetupGet(value => value.Kind).Returns(kind);
                    plugin.SetupProperty(value => value.Title, kind.ToString());
                    plugin.SetupProperty(value => value.IsRenaming);
                    plugin.SetupGet(value => value.Status).Returns("Owned document");
                    var action = new MenuItem { Header = "Owned action" };
                    action.Click += (_, _) => ToolActions++;
                    plugin.Setup(value => value.ContributeMenuItems()).Returns([action]);
                    plugin.Setup(value => value.DisposeAsync()).Returns(() =>
                    {
                        Released++;
                        return ValueTask.CompletedTask;
                    });
                    plugin.As<IWorkspaceDocument>().Setup(value => value.OnConnectionStateChangedAsync(
                        It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
                    Created.Add(plugin);
                    return plugin.Object;
                });
            Model = new MainViewModel(
                Connection.Telemetry, Connection.Connection,
                capabilities: new Mock<ICapabilityService>().Object,
                pluginFactory: factory.Object);
        }

        public string Root { get; }
        public string FavoritesPath { get; }
        public DesktopConnectionContext Connection { get; } = new();
        public MainViewModel Model { get; }
        public MainWindow? Window { get; private set; }
        public List<Mock<IPlugin>> Created { get; } = [];
        public int Released { get; private set; }
        public int ToolActions { get; private set; }

        public MainWindow Show()
        {
            Window = new MainWindow(Model, new AppearancePreferences(Path.Combine(Root, "theme.json")), FavoritesPath)
            {
                ShowInTaskbar = false
            };
            Window.Show(DesktopInteraction.Owner);
            Window.UpdateLayout();
            return Window;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Window is not null)
                {
                    await Window.DisposeAsync().ConfigureAwait(true);
                    Window.Close();
                }
                await Model.DisposeAsync().ConfigureAwait(true);
            }
            finally
            {
                await Connection.DisposeAsync().ConfigureAwait(true);
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private static readonly string[] s_favorites = ["opc.tcp://one.test:4840/Line", "opc.tcp://two.test:4840/Line"];
    private static readonly string[] s_history = ["Favourites", "opc.tcp://one.test:4840/Line", "opc.tcp://two.test:4840/Line"];
    private static readonly string[] s_afterRemove = ["opc.tcp://two.test:4840/Line"];
    private static readonly string[] s_afterAdd =
    [
        "opc.tcp://new.example.test:4840/Line", "opc.tcp://one.test:4840/Line", "opc.tcp://two.test:4840/Line"
    ];
    private static readonly string[] s_connectionOptions =
    [
        "Transport / reverse listener / application identity…", "Use ChannelV2 engine", "Change user…", "Reconnect",
        "Publish pipeline…", "Preferred locales…"
    ];
}
