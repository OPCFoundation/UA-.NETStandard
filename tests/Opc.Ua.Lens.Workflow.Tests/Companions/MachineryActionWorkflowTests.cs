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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Machinery;
using UaLens.Plugins.Historian;
using UaLens.Subscriptions;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;
using UaLens.Workspace;
using Jobs = Opc.Ua.Machinery.Jobs;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    [Platform("Win,Linux")]
    [Category("LensDesktopMainline")]
    [NonParallelizable]
    public sealed class MachineryActionWorkflowTests
    {
        [Test]
        public async Task SelectedSourceUsesExistingMonitorAndHistorianWithNoHiddenActivityAsync()
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    var source = new ActionSource();
                    MachineryPlugin document = new MachineryPluginFactory(source).Create(context.Host);
                    var monitor = new SubscriptionViewModel(
                        "Selected source", null, context.Host.Log, InlineWorkspaceDispatcher.Instance);
                    var history = new HistorianPlugin(context.Host, InlineWorkspaceDispatcher.Instance);
                    await using (document.ConfigureAwait(true))
                    await using (monitor.ConfigureAwait(true))
                    await using (history.ConfigureAwait(true))
                    {
                        await OpenDetailsAsync(context, document, MachinerySection.Energy).ConfigureAwait(true);
                        Assert.That(document.ActiveTaskDialog, Is.Null);
                        Assert.That(document.OpenMonitorCommand.CanExecute(null), Is.False);
                        Assert.That(document.OpenHistoryCommand.CanExecute(null), Is.False);
                        Assert.That(document.ExportSnapshotCommand.CanExecute(null), Is.False);
                        Assert.That(context.Reads, Is.Empty);
                        Assert.That(source.Inspections, Is.EqualTo(1));
                        ListBox values = Named<ListBox>(document.View, "Selected Machinery item details");
                        values.SelectedItem = document.Overview[0];
                        Assert.That(document.SelectedValue, Is.SameAs(document.Overview[0]));
                        context.Workspace.Setup(workspace => workspace.OpenToolAsync(
                            PluginKind.Subscription, null, It.IsAny<CancellationToken>())).ReturnsAsync(monitor);
                        context.Workspace.Setup(workspace => workspace.OpenToolAsync(
                            PluginKind.Historian, null, It.IsAny<CancellationToken>())).ReturnsAsync(history);
                        await document.OpenMonitorCommand.ExecuteAsync(null).ConfigureAwait(true);
                        await document.OpenHistoryCommand.ExecuteAsync(null).ConfigureAwait(true);
                        Assert.That(monitor.Items, Has.Count.EqualTo(1));
                        Assert.That(monitor.Items[0].NodeId, Is.EqualTo(source.Signal));
                        Assert.That(monitor.Items[0].DisplayName, Is.EqualTo("Consumption"));
                        Assert.That(history.TargetNodeId, Is.EqualTo(source.Signal));
                        Assert.That(history.TargetDisplayName, Is.EqualTo("Consumption"));
                        Assert.That(document.Status, Does.Contain("no history read"));
                        Assert.That(source.Inspections, Is.EqualTo(1));
                        Assert.That(context.Reads, Is.Empty);
                        Assert.That(document.View.GetLogicalDescendants().OfType<Expander>()
                            .All(expander => !expander.IsExpanded), Is.True);
                        await document.StopCommand.ExecuteAsync(null).ConfigureAwait(true);
                        Assert.That(document.OpenMonitorCommand.CanExecute(null), Is.False);
                        Assert.That(monitor.Items, Has.Count.EqualTo(1),
                            "A completed handoff is owned by Monitor, not the stopped Machinery document.");
                    }
                    Assert.That(context.Desktop.Connection.IsConnected, Is.True);
                }
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task CanceledLateMonitorAddRemovesOnlyItsOwnedItemAndDrainsTheHandoffAsync()
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    var source = new ActionSource();
                    var document = new MachineryPlugin(context.Host, source);
                    var adapter = new ControlledSubscriptionAdapter();
                    var monitor = new SubscriptionViewModel(
                        "Existing monitor", adapter.Object, context.Host.Log, InlineWorkspaceDispatcher.Instance);
                    await using (document.ConfigureAwait(true))
                    await using (monitor.ConfigureAwait(true))
                    {
                        await monitor.AddItemAsync(new MonitoredItemConfig
                        {
                            NodeId = new NodeId("unrelated", 2),
                            DisplayName = "Keep"
                        }).ConfigureAwait(true);
                        MonitoredItemConfig keep = monitor.Items.Single();
                        await OpenDetailsAsync(context, document, MachinerySection.Energy).ConfigureAwait(true);
                        document.SelectedValue = document.Overview[0];
                        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        adapter.Mock.Setup(value => value.AddItemAsync(
                            It.IsAny<MonitoredItemConfig>(), It.IsAny<CancellationToken>()))
                            .Returns(async (MonitoredItemConfig config, CancellationToken _) =>
                            {
                                adapter.Items.Add(config);
                                entered.TrySetResult();
                                await release.Task.ConfigureAwait(true);
                                return config.Id;
                            });
                        context.Workspace.Setup(workspace => workspace.OpenToolAsync(
                            PluginKind.Subscription, null, It.IsAny<CancellationToken>())).ReturnsAsync(monitor);
                        Task work = document.OpenMonitorCommand.ExecuteAsync(null);
                        Task stopping;
                        int addedId;
                        try
                        {
                            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                            addedId = monitor.Items.Single(item => item.NodeId == source.Signal).Id;
                            stopping = document.StopCommand.ExecuteAsync(null);
                            Assert.That(stopping.IsCompleted, Is.False);
                        }
                        finally
                        {
                            release.TrySetResult();
                        }
                        await work.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                        await stopping.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                        Assert.That(monitor.Items.Single(), Is.SameAs(keep));
                        Assert.That(adapter.Items.Single(), Is.EqualTo(keep));
                        Assert.That(adapter.Removed.Single(), Is.EqualTo(addedId));
                        Assert.That(adapter.DisposeCount, Is.Zero);
                        Assert.That(context.Desktop.Connection.IsConnected, Is.True);
                    }
                }
            }).ConfigureAwait(false);
        }

        [TestCase("field")]
        [TestCase("section")]
        [TestCase("peer")]
        [TestCase("identity")]
        [TestCase("namespace")]
        public async Task ChangingContextWhileOpeningCannotAttachAnObsoleteSourceAsync(string change)
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    var document = new MachineryPlugin(context.Host, new ActionSource());
                    var monitor = new SubscriptionViewModel(
                        "Pending", null, context.Host.Log, InlineWorkspaceDispatcher.Instance);
                    await using (document.ConfigureAwait(true))
                    await using (monitor.ConfigureAwait(true))
                    {
                        await OpenDetailsAsync(context, document, MachinerySection.Energy).ConfigureAwait(true);
                        document.SelectedValue = document.Overview[0];
                        context.Workspace.Setup(workspace => workspace.OpenToolAsync(
                            PluginKind.Subscription, null, It.IsAny<CancellationToken>())).Returns(() =>
                            {
                                switch (change)
                                {
                                    case "field":
                                        document.SelectedValue = document.Overview[1];
                                        break;
                                    case "section":
                                        document.SelectedSection = document.Sections[2];
                                        break;
                                    case "peer":
                                        context.Endpoint.ServerCertificate = ByteString.From("changed-peer"u8);
                                        break;
                                    case "identity":
                                        context.Session.SetupGet(session => session.Identity)
                                            .Returns(new UserIdentity());
                                        break;
                                    case "namespace":
                                        context.Messages.NamespaceUris.GetIndexOrAppend("urn:changed");
                                        break;
                                    default:
                                        throw new AssertionException("Unknown freshness partition.");
                                }
                                return Task.FromResult<IPlugin>(monitor);
                            });
                        await document.OpenMonitorCommand.ExecuteAsync(null).ConfigureAwait(true);
                        Assert.That(monitor.Items, Is.Empty);
                        Assert.That(context.Desktop.Connection.IsConnected, Is.True);
                    }
                }
            }).ConfigureAwait(false);
        }

        [TestCase("stop")]
        [TestCase("close")]
        [TestCase("rebind")]
        [TestCase("section")]
        [TestCase("machine")]
        [TestCase("field")]
        [TestCase("destination")]
        public async Task ParentTransitionsCancelAndDrainPendingSourceHandoffsAsync(string transition)
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    var source = new ActionSource();
                    var document = new MachineryPlugin(context.Host, source);
                    await using (document.ConfigureAwait(true))
                    {
                        await OpenDetailsAsync(context, document, MachinerySection.Energy).ConfigureAwait(true);
                        document.SelectedValue = document.Overview[0];
                        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var cleaning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        context.Workspace.Setup(workspace => workspace.OpenToolAsync(
                            PluginKind.Subscription, null, It.IsAny<CancellationToken>()))
                            .Returns(async (PluginKind _, EndpointDescription? _, CancellationToken token) =>
                            {
                                entered.TrySetResult();
                                try
                                {
                                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(true);
                                }
                                finally
                                {
                                    cleaning.TrySetResult();
                                    await release.Task.ConfigureAwait(true);
                                }
                                throw new AssertionException("Canceled tool opening cannot return a document.");
                            });
                        Task work = document.OpenMonitorCommand.ExecuteAsync(null);
                        Task ending = Task.CompletedTask;
                        try
                        {
                            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                            int reads = source.Reads;
                            switch (transition)
                            {
                                case "stop":
                                    ending = document.StopCommand.ExecuteAsync(null);
                                    break;
                                case "close":
                                    ending = document.DisposeAsync().AsTask();
                                    break;
                                case "rebind":
                                    ending = document.OnConnectionStateChangedAsync(CancellationToken.None);
                                    break;
                                case "section":
                                    document.SelectedSection = document.Sections[2];
                                    break;
                                case "machine":
                                    document.SelectedMachine = new MachineryMachine(
                                        new NodeId("second", 2), "Second", "Machine");
                                    break;
                                case "field":
                                    document.SelectedValue = document.Overview[1];
                                    break;
                                case "destination":
                                    document.ActionDestination = "changed";
                                    break;
                                default:
                                    throw new AssertionException("Unknown lifecycle partition.");
                            }
                            await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                            Assert.That(work.IsCompleted, Is.False);
                            Assert.That(document.IsActionBusy, Is.True);
                            Assert.That(source.Reads, Is.EqualTo(reads),
                                "A new section must wait for canceled selected-action cleanup.");
                            if (transition is "stop" or "close" or "rebind")
                            {
                                Assert.That(ending.IsCompleted, Is.False);
                            }
                        }
                        finally
                        {
                            release.TrySetResult();
                        }
                        await work.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                        await ending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                        Assert.That(document.IsActionBusy, Is.False);
                        Assert.That(document.Status, Does.Not.Contain("Source added"));
                        Assert.That(context.Desktop.Connection.IsConnected, Is.True);
                    }
                }
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task DisplaySnapshotRequiresAnExplicitNewPathAndNeverSavesTaskStateAsync()
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    var document = new MachineryPlugin(context.Host, new ActionSource());
                    await using (document.ConfigureAwait(true))
                    {
                        await OpenDetailsAsync(context, document, MachinerySection.Energy).ConfigureAwait(true);
                        string folder = Path.Combine(
                            Environment.CurrentDirectory, ".ualens-snapshot-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(folder);
                        try
                        {
                            document.ActionDestination = Path.Combine(folder, "selected.json");
                            await document.ExportSnapshotCommand.ExecuteAsync(null).ConfigureAwait(true);
                            Assert.That(document.Status, Does.Contain("No task authorization"));
                            using var json = JsonDocument.Parse(
                                await File.ReadAllTextAsync(document.ActionDestination).ConfigureAwait(true));
                            Assert.That(json.RootElement.GetProperty("representation").GetString(),
                                Is.EqualTo("display-snapshot"));
                            JsonElement first = json.RootElement.GetProperty("values")[0];
                            Assert.That(first.GetProperty("sourceNodeId").GetString(),
                                Is.EqualTo("nsu=urn:fixture:application;s=signal"));
                            Assert.That(first.GetProperty("engineeringMetadata").GetString(), Is.EqualTo("kWh"));
                            Assert.That(first.GetProperty("statusCode").GetUInt32(),
                                Is.EqualTo((uint)StatusCodes.Good));
                            Assert.That(first.GetProperty("sourceTimestamp").GetDateTime(),
                                Is.EqualTo(ActionSource.SourceTime));
                            string saved = document.CaptureState().GetRawText();
                            Assert.That(saved, Does.Not.Contain("selected.json").And.Not.Contain("authorization"));
                            await document.ExportSnapshotCommand.ExecuteAsync(null).ConfigureAwait(true);
                            Assert.That(document.Status, Does.Contain("never overwritten"));
                            Assert.That(Directory.EnumerateFiles(folder), Has.Exactly(1).Items);
                            Assert.That(context.Reads, Is.Empty);
                        }
                        finally
                        {
                            Directory.Delete(folder, recursive: true);
                        }
                    }
                }
            }).ConfigureAwait(false);
        }

        [TestCase("stop")]
        [TestCase("section")]
        public async Task CancelingResultActionWaitsForTransferCleanupAndLeavesNoLocalFileAsync(string transition)
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    var source = new ActionSource();
                    var results = new Mock<IMachineryResultSource>(MockBehavior.Strict);
                    results.Setup(value => value.ReadResultIdAsync(
                        It.IsAny<CompanionContext>(), new NodeId("item", 2), It.IsAny<CancellationToken>()))
                        .ReturnsAsync("result-one");
                    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var cleaning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    results.Setup(value => value.DownloadAsync(
                        It.IsAny<CompanionContext>(), new NodeId("machine", 2), "result-one", It.IsAny<Stream>(),
                        CompanionLocalExport.MaximumBytes, It.IsAny<CancellationToken>()))
                        .Returns(async (
                            CompanionContext _, NodeId _, string _, Stream buffer, long _, CancellationToken token) =>
                        {
                            await buffer.WriteAsync(new byte[] { 1, 2, 3 }, token).ConfigureAwait(false);
                            entered.TrySetResult();
                            try
                            {
                                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                            }
                            finally
                            {
                                cleaning.TrySetResult();
                                await release.Task.ConfigureAwait(false);
                            }
                            return 3L;
                        });
                    var document = new MachineryPlugin(context.Host, source, results.Object);
                    await using (document.ConfigureAwait(true))
                    {
                        await OpenDetailsAsync(context, document, MachinerySection.Results).ConfigureAwait(true);
                        string folder = Path.Combine(
                            Environment.CurrentDirectory, ".ualens-result-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(folder);
                        try
                        {
                            document.ActionDestination = Path.Combine(folder, "selected.bin");
                            Assert.That(document.DownloadResultCommand.CanExecute(null), Is.True);
                            Task work = document.DownloadResultCommand.ExecuteAsync(null);
                            Task stopping = Task.CompletedTask;
                            try
                            {
                                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                                if (transition == "stop")
                                {
                                    stopping = document.StopCommand.ExecuteAsync(null);
                                }
                                else
                                {
                                    document.SelectedSection = document.Sections[3];
                                }
                                await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                                Assert.That(work.IsCompleted, Is.False);
                                Assert.That(Directory.EnumerateFiles(folder), Is.Empty);
                            }
                            finally
                            {
                                release.TrySetResult();
                            }
                            await work.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                            await stopping.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                            Assert.That(Directory.EnumerateFiles(folder), Is.Empty);
                            Assert.That(document.Status, Does.Not.Contain("result exported"));
                        }
                        finally
                        {
                            Directory.Delete(folder, recursive: true);
                        }
                    }
                }
            }).ConfigureAwait(false);
        }

        [TestCase("close")]
        [TestCase("stop")]
        [TestCase("rebind")]
        [TestCase("section")]
        public async Task TransientJobWindowCarriesContextButNoPermissionAndDrainsOnParentChangeAsync(string transition)
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    var source = new ActionSource();
                    Mock<IPreparedCompanionProvider> jobs = ConfigureJobs(context, source);
                    var document = new MachineryPlugin(context.Host, source, jobs: jobs.Object);
                    await using (document.ConfigureAwait(true))
                    {
                        await OpenDetailsAsync(context, document, MachinerySection.Jobs).ConfigureAwait(true);
                        Assert.That(document.ActiveTaskDialog, Is.Null);
                        Task work = Task.CompletedTask;
                        MachineryTaskDialog dialog = await DesktopInteraction.OpenedAsync<MachineryTaskDialog>(() =>
                            work = document.OpenJobTaskCommand.ExecuteAsync(null)).ConfigureAwait(true);
                        Assert.That(dialog.TryGetPlatformHandle()?.Handle, Is.Not.Null.And.Not.EqualTo(IntPtr.Zero));
                        Assert.That(dialog.TaskModel.SelectedTarget!.NodeId, Is.EqualTo(new NodeId("control", 2)));
                        Assert.That(dialog.TaskModel.PreparedOperation, Is.Null);
                        Assert.That(dialog.TaskModel.ConfirmDeployment, Is.False);
                        Assert.That(dialog.TaskModel.ConfirmLocalSample, Is.False);
                        dialog.TaskModel.SelectedOperation = dialog.TaskModel.Operations
                            .Single(operation => operation.Id == "start-job");
                        Assert.That(dialog.TaskModel.InputFields.Single().Text, Is.EqualTo("selected-job"));
                        Assert.That(dialog.TaskModel.PreparedOperation, Is.Null);
                        Assert.That(dialog.TaskModel.RunTaskCommand.CanExecute(null), Is.False);
                        Assert.That(dialog.GetLogicalDescendants().OfType<Expander>()
                            .All(expander => !expander.IsExpanded), Is.True);
                        context.Workspace.Verify(workspace => workspace.OpenToolAsync(
                            It.IsAny<PluginKind>(), It.IsAny<EndpointDescription?>(), It.IsAny<CancellationToken>()),
                            Times.Never);
                        Task ending = Task.CompletedTask;
                        switch (transition)
                        {
                            case "close":
                                ending = document.DisposeAsync().AsTask();
                                break;
                            case "stop":
                                ending = document.StopCommand.ExecuteAsync(null);
                                break;
                            case "rebind":
                                ending = document.OnConnectionStateChangedAsync(CancellationToken.None);
                                break;
                            case "section":
                                document.SelectedSection = document.Sections[3];
                                break;
                            default:
                                throw new AssertionException("Unknown parent transition.");
                        }
                        await work.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                        await ending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                        Assert.That(dialog.IsVisible, Is.False);
                        Assert.That(document.ActiveTaskDialog, Is.Null);
                        jobs.Verify(provider => provider.PrepareInputAsync(
                            It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(), It.IsAny<string>(),
                            It.IsAny<ArrayOf<CompanionValue>>(), It.IsAny<CancellationToken>()), Times.Never);
                        Assert.That(context.Desktop.Connection.IsConnected, Is.True);
                    }
                }
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task TaskWindowEscapeReturnsToTheSelectedItemWithoutSavingASecondDocumentAsync()
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    var source = new ActionSource();
                    var document = new MachineryPlugin(
                        context.Host, source, jobs: ConfigureJobs(context, source).Object);
                    await using (document.ConfigureAwait(true))
                    {
                        await OpenDetailsAsync(context, document, MachinerySection.Jobs).ConfigureAwait(true);
                        MachineryMachine machine = document.SelectedMachine!;
                        Task work = Task.CompletedTask;
                        MachineryTaskDialog dialog = await DesktopInteraction.OpenedAsync<MachineryTaskDialog>(() =>
                            work = document.OpenJobTaskCommand.ExecuteAsync(null)).ConfigureAwait(true);
                        dialog.RaiseEvent(new KeyEventArgs
                        {
                            RoutedEvent = InputElement.KeyDownEvent,
                            Key = Key.Escape,
                            Source = dialog
                        });
                        await work.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                        Assert.That(document.SelectedMachine, Is.SameAs(machine));
                        Assert.That(document.IsShowingDetails, Is.True);
                        Assert.That(document.OpenJobTaskCommand.CanExecute(null), Is.False);
                        Assert.That(document.Status, Does.Contain("Refresh this item explicitly"));
                        JsonElement saved = document.CaptureState();
                        Assert.That(saved.GetProperty("Section").GetInt32(), Is.EqualTo((int)MachinerySection.Jobs));
                        Assert.That(saved.TryGetProperty("ProviderId", out _), Is.False);
                    }
                }
            }).ConfigureAwait(false);
        }

        private static Mock<IPreparedCompanionProvider> ConfigureJobs(
            ConnectedProtocolContext context, ActionSource source)
        {
            context.Messages.NamespaceUris.GetIndexOrAppend(Jobs.Namespaces.MachineryJobs);
            context.Messages.NamespaceUris.GetIndexOrAppend(V2.Namespaces.ISA95JobControlV2);
            context.Translate = (paths, token) =>
            {
                token.ThrowIfCancellationRequested();
                NodeId id = paths[0].RelativePath.Elements[0].TargetName.Name switch
                {
                    "JobManagement" => new NodeId("management", 2),
                    "JobOrderControl" => new NodeId("control", 2),
                    "JobOrderList" => source.JobList,
                    _ => throw new AssertionException("Unexpected path.")
                };
                return ValueTask.FromResult(new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowsePathResult
                    {
                        Targets = [new BrowsePathTarget
                        {
                            TargetId = new ExpandedNodeId(id), RemainingPathIndex = uint.MaxValue
                        }]
                    }]
                });
            };
            var jobs = new Mock<IPreparedCompanionProvider>(MockBehavior.Strict);
            jobs.SetupGet(provider => provider.Descriptor).Returns(
                new CompanionDescriptor("isa95", "Existing ISA-95", V2.Namespaces.ISA95JobControlV2, "Existing tasks"));
            var target = new CompanionTarget(
                "isa95", new NodeId("control", 2), "Selected control", "V2 order receiver");
            jobs.Setup(provider => provider.DiscoverAsync(It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([target]);
            jobs.Setup(provider => provider.InspectAsync(
                It.IsAny<CompanionContext>(), target, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CompanionInspection([],
                [
                    new("refresh", "Refresh", CompanionOperationSafety.ReadOnly),
                    new("start-job", "Start existing job", CompanionOperationSafety.DeploymentMutation)
                    {
                        Inputs = [new("jobOrderId", "Job ID", BuiltInType.String, "Exact job ID")]
                    }
                ], "No job started."));
            return jobs;
        }

        private static async Task OpenDetailsAsync(
            ConnectedProtocolContext context, MachineryPlugin document, MachinerySection section)
        {
            await context.ConnectAsync().ConfigureAwait(true);
            await document.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
            DesktopInteraction.Owner.Width = 980;
            DesktopInteraction.Owner.Height = 640;
            DesktopInteraction.Owner.Content = document.View;
            await ReadyAsync(document, "overview", () => document.DiscoverCommand.ExecuteAsync(null))
                .ConfigureAwait(true);
            await ReadyAsync(document, "list", () =>
            {
                document.SelectedSection = document.Sections[(int)section];
                return Task.CompletedTask;
            }).ConfigureAwait(true);
            document.SelectedItem = document.Items[0];
            await ReadyAsync(document, "Details: selected", () =>
            {
                document.InspectSelectionCommand.Execute(null);
                return Task.CompletedTask;
            }).ConfigureAwait(true);
            DesktopInteraction.Owner.UpdateLayout();
        }

        private static Task ReadyAsync(MachineryPlugin document, string status, Func<Task> action)
        {
            return DesktopInteraction.ModelChangedAsync(
                document, () => document.Status == status && !document.IsLoading, action);
        }

        private static T Named<T>(Control view, string name) where T : Control
        {
            return view.GetLogicalDescendants().OfType<T>()
                .Single(control => AutomationProperties.GetName(control) == name);
        }

        private sealed class ActionSource : IMachineryDataSource
        {
            public NodeId Signal { get; } = new("signal", 2);
            public NodeId JobList { get; } = new("jobs", 2);
            public int Inspections { get; private set; }
            public int Reads { get; private set; }

            public Task<ArrayOf<MachineryMachine>> DiscoverAsync(
                CompanionContext context, CancellationToken cancellationToken)
            {
                return Task.FromResult<ArrayOf<MachineryMachine>>([new(new NodeId("machine", 2), "Press", "Machine")]);
            }

            public Task<MachineryPage> ReadAsync(
                CompanionContext context, MachineryMachine machine, MachinerySection section,
                int pageIndex, int pageSize, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Reads++;
                return Task.FromResult(section == MachinerySection.Overview
                    ? new MachineryPage([new("Identity", Variant.From("Press"))], [], false, "overview")
                    : new MachineryPage([],
                        [new(section == MachinerySection.Jobs ? JobList : new NodeId("item", 2),
                            "selected", "Selected item", string.Empty)
                        {
                            SourceNodeId = section == MachinerySection.Jobs ? JobList : Signal,
                            ItemId = section == MachinerySection.Jobs ? "selected-job" : null
                        }], false, "list"));
            }

            public Task<ArrayOf<CompanionValue>> InspectAsync(
                CompanionContext context, MachineryMachine machine, MachinerySection section,
                MachineryItem item, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Inspections++;
                if (section == MachinerySection.Results)
                {
                    return Task.FromResult<ArrayOf<CompanionValue>>(
                    [
                        new("Published result", Variant.FromStructure(new Opc.Ua.Machinery.Result.ResultDataType
                        {
                            ResultMetaData = new Opc.Ua.Machinery.Result.ResultMetaDataType { ResultId = "result-one" }
                        })) { SourceNodeId = item.NodeId },
                        new("Result ID", Variant.From("result-one"))
                    ]);
                }
                return Task.FromResult<ArrayOf<CompanionValue>>(
                [
                    new("Consumption", Variant.From(42.0))
                    {
                        SourceNodeId = item.SourceNodeId,
                        SourceTimestamp = SourceTime,
                        EngineeringMetadata = "kWh"
                    },
                    new("Other source", Variant.From(8.0)) { SourceNodeId = new NodeId("other", 2) }
                ]);
            }

            public static readonly DateTime SourceTime = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        }
    }
}
