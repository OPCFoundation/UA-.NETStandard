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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Machinery;
using UaLens.Storage;
using UaLens.Tests.Administration;
using UaLens.Tests.Desktop;
using UaLens.Themes;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    [Platform("Win,Linux")]
    [Category("LensDesktopMainline")]
    [NonParallelizable]
    public sealed class MachineryNavigationTests
    {
        [Test]
        public async Task SelectedItemInspectionIsExplicitAndKeepsMachineAndSectionAsync()
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    await context.ConnectAsync().ConfigureAwait(true);
                    var source = new ControlledSource();
                    var document = new MachineryPlugin(context.Host, source);
                    await using (document.ConfigureAwait(true))
                    {
                        await document.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
                        DesktopInteraction.Owner.Content = document.View;
                        await ReadyAsync(document, "Overview ready", () => document.DiscoverCommand.ExecuteAsync(null))
                            .ConfigureAwait(true);
                        await ReadyAsync(document, "Energy ready", () =>
                        {
                            document.SelectedSection = document.Sections[3];
                            return Task.CompletedTask;
                        }).ConfigureAwait(true);
                        MachineryMachine machine = document.SelectedMachine!;
                        document.SelectedItem = document.Items[3];
                        Assert.That(source.Inspections, Is.Empty);
                        await ReadyAsync(document, "Details: Energy 3", () =>
                        {
                            document.InspectSelectionCommand.Execute(null);
                            return Task.CompletedTask;
                        }).ConfigureAwait(true);
                        Assert.That(source.Inspections, Has.Count.EqualTo(1));
                        Assert.That(document.IsShowingDetails, Is.True);
                        Assert.That(document.Overview.Single().Value, Is.EqualTo(Variant.From("Energy 3")));
                        Assert.That(source.Reads, Has.Count.EqualTo(2));
                        await ReadyAsync(document, "Energy ready", () =>
                        {
                            document.BackToListCommand.Execute(null);
                            return Task.CompletedTask;
                        }).ConfigureAwait(true);
                        Assert.That(document.IsShowingDetails, Is.False);
                        Assert.That(document.SelectedMachine, Is.SameAs(machine));
                        Assert.That(document.SelectedSection!.Section, Is.EqualTo(MachinerySection.Energy));
                    }
                }
            }).ConfigureAwait(false);
        }

        [TestCase(false, 1.0)]
        [TestCase(true, 1.0)]
        [TestCase(false, 1.5)]
        [TestCase(true, 2.0)]
        public async Task MinimumShellKeepsMachineAndSectionUsableWithoutLoadingInactivePartsAsync(
            bool panels, double captureScale)
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    await context.ConnectAsync().ConfigureAwait(true);
                    using var files = new TemporaryCertificateStores();
                    var model = new MainViewModel(context.Desktop.Telemetry, context.Desktop.Connection);
                    await using (model.ConfigureAwait(true))
                    {
                        var window = new MainWindow(
                            model, new AppearancePreferences(Path.Combine(files.Root, "appearance.json")),
                            favoritesPath: Path.Combine(files.Root, "favorites.json"),
                            inspectorPreferences: new InspectorPreferences(Path.Combine(files.Root, "inspector.json")))
                        {
                            Width = 980,
                            Height = 640
                        };
                        await using (window.ConfigureAwait(true))
                        {
                            var source = new ControlledSource();
                            var document = new MachineryPlugin(model.CreatePluginHost(), source);
                            _ = await model.Workspace.OpenAsync(() => document).ConfigureAwait(true);
                            model.IsAddressSpaceVisible = panels;
                            model.SetInspectorVisibility(panels, panels);
                            window.Show(DesktopInteraction.Owner);
                            await ReadyAsync(document, "Overview ready",
                                () => document.DiscoverCommand.ExecuteAsync(null)).ConfigureAwait(true);
                            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
                            Control view = document.View;
                            ComboBox machine = Named<ComboBox>(view, "Machine");
                            ComboBox sections = Named<ComboBox>(view, "Machinery section");
                            AssertVisibleInside(view, machine);
                            AssertVisibleInside(view, sections);
                            Assert.That(machine.IsEffectivelyEnabled, Is.True);
                            Assert.That(sections.IsEffectivelyEnabled, Is.True);
                            Assert.That(sections.Items, Has.Count.EqualTo(5));
                            Assert.That(sections.SelectedItem, Is.SameAs(document.Sections[0]));
                            Assert.That(source.Reads, Has.Count.EqualTo(1));
                            Assert.That(source.Reads[0].Section, Is.EqualTo(MachinerySection.Overview));
                            Assert.That(document.Overview, Has.Count.EqualTo(3));
                            Assert.That(view.GetLogicalDescendants().OfType<Expander>()
                                .All(expander => !expander.IsExpanded), Is.True);
                            Assert.That(sections.Focus(), Is.True);
                            await ReadyAsync(document, "ProcessValues ready", async () =>
                            {
                                sections.RaiseEvent(new KeyEventArgs
                                {
                                    RoutedEvent = InputElement.KeyDownEvent,
                                    Key = Key.F4,
                                    Source = sections
                                });
                                await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
                                Assert.That(sections.IsDropDownOpen, Is.True);
                                InputElement focused = window.FocusManager?.GetFocusedElement() as InputElement ??
                                    throw new AssertionException("The open section selector has no keyboard focus.");
                                focused.RaiseEvent(new KeyEventArgs
                                {
                                    RoutedEvent = InputElement.KeyDownEvent,
                                    Key = Key.Down,
                                    Source = focused
                                });
                                InputElement commit = window.FocusManager?.GetFocusedElement() as InputElement ??
                                    throw new AssertionException("The section choice lost keyboard focus.");
                                commit.RaiseEvent(new KeyEventArgs
                                {
                                    RoutedEvent = InputElement.KeyDownEvent,
                                    Key = Key.Enter,
                                    Source = commit
                                });
                                Assert.That(document.SelectedSection!.Section,
                                    Is.EqualTo(MachinerySection.ProcessValues),
                                    $"Selector index={sections.SelectedIndex}; open={sections.IsDropDownOpen}.");
                            }).ConfigureAwait(true);
                            Assert.That(document.SelectedSection!.Section, Is.EqualTo(MachinerySection.ProcessValues));
                            Assert.That(document.SelectedMachine, Is.SameAs(source.Machines[0]));
                            Assert.That(window.FocusManager?.GetFocusedElement(), Is.SameAs(sections));
                            Assert.That(source.Reads.Select(read => read.Section),
                                Is.EqualTo([MachinerySection.Overview, MachinerySection.ProcessValues]));
                            document.SelectedItem = document.Items[0];
                            await ReadyAsync(document, "Details: ProcessValues 0", () =>
                            {
                                document.InspectSelectionCommand.Execute(null);
                                return Task.CompletedTask;
                            }).ConfigureAwait(true);
                            Button actions = Named<Button>(view, "Selected Machinery actions");
                            Assert.That(actions.Flyout!.IsOpen, Is.False);
                            Assert.That(actions.Focus(), Is.True);
                            actions.Flyout.ShowAt(actions);
                            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
                            var actionPanel = (Control)((Flyout)actions.Flyout).Content!;
                            TextBox destination = Named<TextBox>(actionPanel, "Machinery export destination");
                            Assert.That(destination.Focus(), Is.True);
                            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
                            AssertVisibleInside(view, actions);
                            AssertVisibleInside(actionPanel, destination);
                            Assert.That(Named<ListBox>(view, "Selected Machinery item details").Bounds.Height,
                                Is.GreaterThan(0));
                            Assert.That(document.ActiveTaskDialog, Is.Null);
                            actions.Flyout.Hide();
                            var pixelSize = new PixelSize(
                                Math.Max(1, (int)Math.Ceiling(view.Bounds.Width * captureScale)),
                                Math.Max(1, (int)Math.Ceiling(view.Bounds.Height * captureScale)));
                            using var bitmap = new RenderTargetBitmap(
                                pixelSize, new Avalonia.Vector(96 * captureScale, 96 * captureScale));
                            bitmap.Render(view);
                            Assert.That(bitmap.PixelSize, Is.EqualTo(pixelSize));
                        }
                    }
                }
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task SlowInactiveReadCannotFreezeNavigationOrOverwriteTheNewSectionAsync()
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    await context.ConnectAsync().ConfigureAwait(true);
                    var source = new ControlledSource();
                    var document = new MachineryPlugin(context.Host, source);
                    await using (document.ConfigureAwait(true))
                    {
                        await document.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
                        await ReadyAsync(document, "Overview ready", () => document.DiscoverCommand.ExecuteAsync(null))
                            .ConfigureAwait(true);
                        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var slow = new TaskCompletionSource<MachineryPage>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        CancellationToken oldToken = default;
                        source.Read = (section, page, token) =>
                        {
                            if (section == MachinerySection.Energy)
                            {
                                oldToken = token;
                                entered.TrySetResult();
                                return slow.Task;
                            }
                            return Task.FromResult(ControlledSource.Page(section, page));
                        };
                        try
                        {
                            document.SelectedSection = document.Sections[3];
                            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                            await ReadyAsync(document, "Results ready", () =>
                            {
                                document.SelectedSection = document.Sections[4];
                                return Task.CompletedTask;
                            }).ConfigureAwait(true);
                            Assert.That(oldToken.IsCancellationRequested, Is.True);
                            Assert.That(document.Items[0].Name, Does.StartWith("Results"));
                            slow.SetResult(ControlledSource.Page(MachinerySection.Energy, 0));
                            await document.StopCommand.ExecuteAsync(null).ConfigureAwait(true);
                            Assert.That(document.SelectedSection!.Section, Is.EqualTo(MachinerySection.Results));
                            Assert.That(document.Items[0].Name, Does.StartWith("Results"));
                        }
                        finally
                        {
                            slow.TrySetResult(ControlledSource.Page(MachinerySection.Energy, 0));
                        }
                    }
                }
            }).ConfigureAwait(false);
        }

        [TestCase("all", (int)MachinerySection.Energy)]
        [TestCase("basic", (int)MachinerySection.ProcessValues)]
        [TestCase("jobs", (int)MachinerySection.Jobs)]
        [TestCase("results", (int)MachinerySection.Results)]
        public async Task OptionalPartsKeepOneMachineUsableAndListsRemainPagedAsync(string profile, int section)
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    await context.ConnectAsync().ConfigureAwait(true);
                    var source = new ControlledSource { Profile = profile };
                    var document = new MachineryPlugin(context.Host, source);
                    await using (document.ConfigureAwait(true))
                    {
                        await document.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
                        await ReadyAsync(document, "Overview ready", () => document.DiscoverCommand.ExecuteAsync(null))
                            .ConfigureAwait(true);
                        string expected = profile == "basic" ? "Not exposed" : ((MachinerySection)section) + " ready";
                        await ReadyAsync(document, expected,
                            () =>
                            {
                                document.SelectedSection = document.Sections[section];
                                return Task.CompletedTask;
                            }).ConfigureAwait(true);
                        if (profile == "basic")
                        {
                            Assert.That(document.Items, Is.Empty);
                            Assert.That(document.Status, Is.EqualTo("Not exposed"));
                        }
                        else
                        {
                            Assert.That(document.Items, Has.Count.EqualTo(32));
                            Assert.That(document.HasMore, Is.True);
                            await ReadyAsync(document, ((MachinerySection)section) + " ready", () =>
                            {
                                document.NextPageCommand.Execute(null);
                                return Task.CompletedTask;
                            }).ConfigureAwait(true);
                            Assert.That(source.Reads[^1].Page, Is.EqualTo(1));
                            Assert.That(document.Items, Has.Count.EqualTo(32));
                            Assert.That(document.Items[0].Name, Does.EndWith("32"));
                        }
                    }
                }
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task ClosingWaitsForCanceledReadsAndPreventsNewSectionWorkAsync()
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    await context.ConnectAsync().ConfigureAwait(true);
                    var source = new ControlledSource();
                    var document = new MachineryPlugin(context.Host, source);
                    await document.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
                    await ReadyAsync(document, "Overview ready", () => document.DiscoverCommand.ExecuteAsync(null))
                        .ConfigureAwait(true);
                    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    source.Read = async (section, page, token) =>
                    {
                        entered.TrySetResult();
                        try
                        {
                            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(true);
                        }
                        finally
                        {
                            canceled.TrySetResult();
                            await release.Task.ConfigureAwait(true);
                        }
                        return ControlledSource.Page(section, page);
                    };
                    try
                    {
                        document.SelectedSection = document.Sections[3];
                        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                        Task closing = document.DisposeAsync().AsTask();
                        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                        Assert.That(closing.IsCompleted, Is.False);
                        int count = source.Reads.Count;
                        document.SelectedSection = document.Sections[4];
                        Assert.That(source.Reads, Has.Count.EqualTo(count));
                        release.SetResult();
                        await closing.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                        Assert.That(document.Items, Is.Empty);
                        Assert.That(document.RefreshCommand.CanExecute(null), Is.False);
                        Assert.That(context.Desktop.Connection.IsConnected, Is.True);
                    }
                    finally
                    {
                        release.TrySetResult();
                        await document.DisposeAsync().ConfigureAwait(true);
                    }
                }
            }).ConfigureAwait(false);
        }

        [TestCase("absent")]
        [TestCase("denied")]
        [TestCase("too-many")]
        public async Task UnavailableSectionDoesNotDisableNavigationOrOtherPartsAsync(string kind)
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    await context.ConnectAsync().ConfigureAwait(true);
                    var source = new ControlledSource();
                    var document = new MachineryPlugin(context.Host, source);
                    await using (document.ConfigureAwait(true))
                    {
                        await document.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
                        await ReadyAsync(document, "Overview ready", () => document.DiscoverCommand.ExecuteAsync(null))
                            .ConfigureAwait(true);
                        source.Read = (section, page, _) => section != MachinerySection.Energy
                            ? Task.FromResult(ControlledSource.Page(section, page))
                            : kind switch
                            {
                                "absent" => Task.FromResult(new MachineryPage([], [], false, "Energy not exposed")),
                                "denied" => Task.FromException<MachineryPage>(
                                    new ServiceResultException(StatusCodes.BadUserAccessDenied)),
                                _ => Task.FromResult(new MachineryPage([],
                                    Enumerable.Range(0, 33).Select(index =>
                                        new MachineryItem(new NodeId((uint)index + 1), "Meter", "Value", string.Empty))
                                    .ToArrayOf(),
                                    false, "Invalid oversized page"))
                            };
                        await DesktopInteraction.ModelChangedAsync(document,
                            () => !document.IsLoading &&
                                (document.Status == "Energy not exposed" ||
                                    document.Status.StartsWith("Section unavailable", StringComparison.Ordinal)),
                            () =>
                            {
                                document.SelectedSection = document.Sections[3];
                                return Task.CompletedTask;
                            }).ConfigureAwait(true);
                        Assert.That(document.Items, Is.Empty);
                        if (kind == "denied")
                        {
                            Assert.That(document.Status, Does.Contain("BadUserAccessDenied"));
                        }
                        await ReadyAsync(document, "Jobs ready", () =>
                        {
                            document.SelectedSection = document.Sections[2];
                            return Task.CompletedTask;
                        }).ConfigureAwait(true);
                        Assert.That(document.Items, Has.Count.EqualTo(32));
                        Assert.That(document.SelectedMachine, Is.SameAs(source.Machines[0]));
                    }
                }
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task SearchPreservesSelectionAndRestoreStartsNoReadAsync()
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    await context.ConnectAsync().ConfigureAwait(true);
                    var source = new ControlledSource();
                    var document = new MachineryPlugin(context.Host, source);
                    await using (document.ConfigureAwait(true))
                    {
                        await document.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
                        DesktopInteraction.Owner.Content = document.View;
                        await ReadyAsync(document, "Overview ready", () => document.DiscoverCommand.ExecuteAsync(null))
                            .ConfigureAwait(true);
                        document.SearchText = "Second";
                        Assert.That(document.SelectedMachine, Is.SameAs(source.Machines[0]));
                        Assert.That(source.Reads, Has.Count.EqualTo(1));
                        JsonElement state = document.CaptureState();
                        int before = source.Reads.Count;
                        await document.RestoreStateAsync(state).ConfigureAwait(true);
                        Assert.That(source.Reads, Has.Count.EqualTo(before));
                        Assert.That(document.SelectedMachine, Is.Null);
                        Assert.That(document.Items, Is.Empty);
                        Assert.That(document.Status, Does.Contain("no section or operation"));
                    }
                }
            }).ConfigureAwait(false);
        }

        private static Task ReadyAsync(MachineryPlugin document, string status, Func<Task> action)
        {
            return DesktopInteraction.ModelChangedAsync(document,
                () => document.Status == status && !document.IsLoading, action);
        }

        private static T Named<T>(Control view, string name) where T : Control
        {
            return view.GetLogicalDescendants().OfType<T>()
                .Single(control => AutomationProperties.GetName(control) == name);
        }

        private static void AssertVisibleInside(Control view, Control control)
        {
            Assert.That(control.IsEffectivelyVisible, Is.True);
            Assert.That(control.Bounds.Width, Is.GreaterThan(0));
            Point point = control.TranslatePoint(default, view) ??
                throw new AssertionException("The control is not attached to its view.");
            Assert.That(point.X, Is.GreaterThanOrEqualTo(-1));
            Assert.That(point.Y, Is.GreaterThanOrEqualTo(-1));
            Assert.That(point.X + control.Bounds.Width, Is.LessThanOrEqualTo(view.Bounds.Width + 1));
            Assert.That(point.Y + control.Bounds.Height, Is.LessThanOrEqualTo(view.Bounds.Height + 1));
        }

        private sealed class ControlledSource : IMachineryDataSource
        {
            public ArrayOf<MachineryMachine> Machines { get; } =
                [new(new NodeId("first", 2), "First machine", "Machine"),
                    new(new NodeId("second", 2), "Second machine", "Machine")];

            public List<(MachinerySection Section, int Page)> Reads { get; } = [];
            public List<MachineryItem> Inspections { get; } = [];
            public string Profile { get; init; } = "all";
            public Func<MachinerySection, int, CancellationToken, Task<MachineryPage>>? Read { get; set; }

            public Task<ArrayOf<MachineryMachine>> DiscoverAsync(
                CompanionContext context, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(Machines);
            }

            public Task<MachineryPage> ReadAsync(
                CompanionContext context, MachineryMachine machine, MachinerySection section,
                int pageIndex, int pageSize, CancellationToken cancellationToken)
            {
                Assert.That(pageSize, Is.EqualTo(32));
                Reads.Add((section, pageIndex));
                if (Read is not null)
                {
                    return Read(section, pageIndex, cancellationToken);
                }
                bool exposed = section == MachinerySection.Overview ||
                    Profile == "all" ||
                    (Profile == "jobs" && section == MachinerySection.Jobs) ||
                    (Profile == "results" && section == MachinerySection.Results);
                return Task.FromResult(exposed
                    ? Page(section, pageIndex)
                    : new MachineryPage([], [], false, "Not exposed"));
            }

            public Task<ArrayOf<CompanionValue>> InspectAsync(
                CompanionContext context, MachineryMachine machine, MachinerySection section,
                MachineryItem item, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Inspections.Add(item);
                return Task.FromResult<ArrayOf<CompanionValue>>(
                    [new("Selected item", Variant.From(item.Name)) { SourceNodeId = item.NodeId }]);
            }

            public static MachineryPage Page(MachinerySection section, int page)
            {
                return section == MachinerySection.Overview
                    ? new MachineryPage(
                        [new("Identity", Variant.From("Machine")), new("Health", Variant.From("Good")),
                            new("State", Variant.From("Ready"))],
                        [], false, "Overview ready")
                    : new MachineryPage([], Enumerable.Range(page * 32, 32)
                        .Select(index => new MachineryItem(new NodeId((uint)index + 1, 2), section + " " + index,
                            "Selected-section data", "Technical evidence")).ToArrayOf(), true, section + " ready");
            }
        }
    }
}
