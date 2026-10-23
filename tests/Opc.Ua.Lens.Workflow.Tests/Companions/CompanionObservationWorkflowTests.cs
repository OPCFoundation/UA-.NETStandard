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
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Alarms;
using UaLens.Plugins.Companions.Providers;
using UaLens.Plugins.Historian;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    [Platform("Win,Linux")]
    [Category("LensDesktopMainline")]
    [NonParallelizable]
    public sealed class CompanionObservationWorkflowTests
    {
        [Test]
        public async Task WeighingOpensTheGenericPackMlDocumentWithOnlyItsSourceConfigurationAsync()
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    CompanionPlugin weighing = await OpenDocumentAsync(context, weighing: true).ConfigureAwait(true);
                    var packml = new CompanionPlugin(
                        context.Host, providers: [new PackMlCompanionProvider()], kind: PluginKind.PackML);
                    await using (weighing.ConfigureAwait(true))
                    await using (packml.ConfigureAwait(true))
                    {
                        NodeId root = new(771u, 2);
                        context.Translate = (paths, _) =>
                        {
                            Assert.That(paths, Has.Count.EqualTo(1));
                            Assert.That(paths[0].StartingNode, Is.EqualTo(weighing.SelectedTarget!.NodeId));
                            Assert.That(paths[0].RelativePath.Elements[0].TargetName.Name,
                                Is.EqualTo(Opc.Ua.Scales.BrowseNames.SystemState));
                            return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                                new TranslateBrowsePathsToNodeIdsResponse
                                {
                                    ResponseHeader = new ResponseHeader(),
                                    Results = [new BrowsePathResult
                                    {
                                        Targets = [new BrowsePathTarget
                                        {
                                            TargetId = new ExpandedNodeId(root), RemainingPathIndex = uint.MaxValue
                                        }]
                                    }]
                                });
                        };
                        context.Workspace.Setup(workspace => workspace.OpenToolAsync(
                            PluginKind.PackML, null, It.IsAny<CancellationToken>())).ReturnsAsync(packml);
                        Assert.That(weighing.ShowPackMlAction, Is.True);
                        Assert.That(weighing.OpenPackMlCommand.CanExecute(null), Is.True);
                        await weighing.OpenPackMlCommand.ExecuteAsync(null).ConfigureAwait(true);
                        Assert.That(packml.CaptureState().GetProperty("TargetId").GetString(),
                            Is.EqualTo("nsu=urn:fixture:application;i=771"));
                        Assert.That(packml.Targets, Is.Empty);
                        Assert.That(packml.Values, Is.Empty);
                        Assert.That(packml.PreparedOperation, Is.Null);
                        Assert.That(packml.ConfirmDeployment, Is.False);
                        Assert.That(weighing.Status, Does.Contain("no command or observation"));
                    }
                }
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task ConditionsSelectsTheExactAssetInAlarmsWithoutStartingObservationAsync()
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    CompanionPlugin document = await OpenDocumentAsync(context).ConfigureAwait(true);
                    var alarms = new AlarmsPlugin(context.Host);
                    await using (document.ConfigureAwait(true))
                    await using (alarms.ConfigureAwait(true))
                    {
                        context.Read = (reads, _) =>
                        {
                            Assert.That(reads, Has.Count.EqualTo(1));
                            Assert.That(reads[0].NodeId, Is.EqualTo(document.SelectedTarget!.NodeId));
                            Assert.That(reads[0].AttributeId, Is.EqualTo(Attributes.EventNotifier));
                            return new ValueTask<ReadResponse>(new ReadResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = [new DataValue(Variant.From((byte)EventNotifiers.SubscribeToEvents))]
                            });
                        };
                        context.Workspace.Setup(workspace => workspace.OpenToolAsync(
                            PluginKind.Alarms, null, It.IsAny<CancellationToken>())).ReturnsAsync(alarms);
                        await document.OpenAlarmsCommand.ExecuteAsync(null).ConfigureAwait(true);
                        Assert.That(alarms.SourceId, Is.EqualTo("nsu=urn:fixture:application;s=pump"));
                        Assert.That(alarms.SourceName, Is.EqualTo("Cooling pump"));
                        Assert.That(alarms.ActionStatus, Does.Contain("no operator action"));
                        Assert.That(document.Status, Does.Contain("no acknowledgement"));
                        Assert.That(context.Reads, Has.Count.EqualTo(1));
                    }
                }
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task NonEventSourceCannotOpenAnAlarmsDocumentAsync()
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    CompanionPlugin document = await OpenDocumentAsync(context).ConfigureAwait(true);
                    await using (document.ConfigureAwait(true))
                    {
                        context.Read = (_, _) => new ValueTask<ReadResponse>(new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [new DataValue(Variant.From((byte)0))]
                        });
                        await document.OpenAlarmsCommand.ExecuteAsync(null).ConfigureAwait(true);
                        Assert.That(document.Status, Does.Contain("BadNotSupported"));
                        context.Workspace.Verify(workspace => workspace.OpenToolAsync(
                            It.IsAny<PluginKind>(), It.IsAny<EndpointDescription?>(), It.IsAny<CancellationToken>()),
                            Times.Never);
                    }
                }
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task SelectedSourceOpensExistingMonitorAndHistoryWithoutOwningTheConnectionAsync()
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    CompanionPlugin document = await OpenDocumentAsync(context).ConfigureAwait(true);
                    var monitor = new SubscriptionViewModel(
                        "Source monitor", null, context.Host.Log, InlineWorkspaceDispatcher.Instance);
                    var history = new HistorianPlugin(context.Host, InlineWorkspaceDispatcher.Instance);
                    await using (document.ConfigureAwait(true))
                    await using (monitor.ConfigureAwait(true))
                    await using (history.ConfigureAwait(true))
                    {
                        context.Workspace.Setup(workspace => workspace.OpenToolAsync(
                            PluginKind.Subscription, null, It.IsAny<CancellationToken>())).ReturnsAsync(monitor);
                        context.Workspace.Setup(workspace => workspace.OpenToolAsync(
                            PluginKind.Historian, null, It.IsAny<CancellationToken>())).ReturnsAsync(history);
                        var view = (Control)((IPlugin)document).View!;
                        DesktopInteraction.Owner.Width = 980;
                        DesktopInteraction.Owner.Height = 640;
                        DesktopInteraction.Owner.Content = view;
                        DesktopInteraction.Owner.UpdateLayout();
                        ListBox values = view.GetLogicalDescendants().OfType<ListBox>().Single(control =>
                            AutomationProperties.GetName(control) == "Inspected source values");
                        Assert.That(document.OpenMonitorCommand.CanExecute(null), Is.False);
                        Assert.That(document.OpenHistoryCommand.CanExecute(null), Is.False);
                        values.SelectedItem = document.Values[0];
                        Assert.That(document.SelectedValue, Is.SameAs(document.Values[0]));
                        Assert.That(document.OpenMonitorCommand.CanExecute(null), Is.True);
                        Assert.That(document.ExportSnapshotCommand.CanExecute(null), Is.True);

                        await document.OpenMonitorCommand.ExecuteAsync(null).ConfigureAwait(true);
                        await document.OpenHistoryCommand.ExecuteAsync(null).ConfigureAwait(true);

                        Assert.That(monitor.Items, Has.Count.EqualTo(1));
                        Assert.That(monitor.Items[0].NodeId, Is.EqualTo(new NodeId("flow", 2)));
                        Assert.That(monitor.Items[0].DisplayName, Is.EqualTo("Mass flow"));
                        Assert.That(history.TargetNodeId, Is.EqualTo(new NodeId("flow", 2)));
                        Assert.That(history.TargetDisplayName, Is.EqualTo("Mass flow"));
                        Assert.That(context.Reads, Is.Empty);
                        Assert.That(document.Status, Does.Contain("no history read"));
                        Assert.That(view.GetLogicalDescendants().OfType<Expander>().Single().IsExpanded, Is.False);
                        Assert.That(context.Desktop.Connection.IsConnected, Is.True);
                    }
                    Assert.That(context.Desktop.Connection.IsConnected, Is.True,
                        "Closing cooperating documents must not dispose the borrowed primary session.");
                }
            }).ConfigureAwait(false);
        }

        [TestCase("target")]
        [TestCase("identity")]
        [TestCase("namespace")]
        public async Task RebindingDuringToolOpeningCannotAttachAnObsoleteSourceAsync(string change)
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    CompanionPlugin document = await OpenDocumentAsync(context).ConfigureAwait(true);
                    var monitor = new SubscriptionViewModel(
                        "Source monitor", null, context.Host.Log, InlineWorkspaceDispatcher.Instance);
                    await using (document.ConfigureAwait(true))
                    await using (monitor.ConfigureAwait(true))
                    {
                        document.SelectedValue = document.Values[0];
                        context.Workspace.Setup(workspace => workspace.OpenToolAsync(
                            PluginKind.Subscription, null, It.IsAny<CancellationToken>()))
                            .Returns(() =>
                            {
                                switch (change)
                                {
                                    case "target":
                                        document.SelectedTarget =
                                            new CompanionTarget("pumps", new NodeId("other", 2), "Other", "Pump");
                                        break;
                                    case "identity":
                                        context.Session.SetupGet(session => session.Identity)
                                            .Returns(new UserIdentity());
                                        break;
                                    case "namespace":
                                        context.Messages.NamespaceUris.GetIndexOrAppend("urn:fixture:replacement");
                                        break;
                                    default:
                                        throw new AssertionException("Unknown test partition.");
                                }
                                return Task.FromResult<IPlugin>(monitor);
                            });

                        await document.OpenMonitorCommand.ExecuteAsync(null).ConfigureAwait(true);

                        Assert.That(monitor.Items, Is.Empty);
                        Assert.That(document.Status, Does.Contain("Inspect the current target"));
                        Assert.That(context.Desktop.Connection.IsConnected, Is.True);
                    }
                }
            }).ConfigureAwait(false);
        }

        [TestCase("close")]
        [TestCase("stop")]
        [TestCase("connection")]
        public async Task ClosingDocumentCancelsAndDrainsPendingSourceOpeningAsync(string transition)
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    CompanionPlugin document = await OpenDocumentAsync(context).ConfigureAwait(true);
                    var monitor = new SubscriptionViewModel(
                        "Pending source", null, context.Host.Log, InlineWorkspaceDispatcher.Instance);
                    await using (monitor.ConfigureAwait(true))
                    {
                        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        context.Workspace.Setup(workspace => workspace.OpenToolAsync(
                            PluginKind.Subscription, null, It.IsAny<CancellationToken>()))
                            .Returns(async (PluginKind _, EndpointDescription? _, CancellationToken token) =>
                            {
                                entered.SetResult();
                                try
                                {
                                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(true);
                                }
                                finally
                                {
                                    canceled.TrySetResult();
                                    await release.Task.ConfigureAwait(true);
                                }
                                return monitor;
                            });
                        document.SelectedValue = document.Values[0];
                        Task opening = document.OpenMonitorCommand.ExecuteAsync(null);
                        Task closing = Task.CompletedTask;
                        try
                        {
                            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                            closing = transition switch
                            {
                                "close" => document.DisposeAsync().AsTask(),
                                "stop" => document.CancelCommand.ExecuteAsync(null),
                                "connection" => document.OnConnectionStateChangedAsync(CancellationToken.None),
                                _ => throw new AssertionException("Unknown lifecycle transition.")
                            };
                            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                            Assert.That(closing.IsCompleted, Is.False);
                            release.SetResult();
                            await closing.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                            await opening.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                            Assert.That(monitor.Items, Is.Empty);
                            Assert.That(document.OpenMonitorCommand.CanExecute(null), Is.False);
                            Assert.That(context.Desktop.Connection.IsConnected, Is.True);
                        }
                        finally
                        {
                            document.OpenMonitorCommand.Cancel();
                            release.TrySetResult();
                            await Task.WhenAll(opening, closing).WaitAsync(TimeSpan.FromSeconds(5))
                                .ConfigureAwait(true);
                            await document.DisposeAsync().ConfigureAwait(true);
                        }
                    }
                }
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task ChangingTheSelectedTargetClearsSourceActionsAndExportAsync()
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    CompanionPlugin document = await OpenDocumentAsync(context).ConfigureAwait(true);
                    await using (document.ConfigureAwait(true))
                    {
                        document.SelectedValue = document.Values[0];
                        Assert.That(document.OpenMonitorCommand.CanExecute(null), Is.True);
                        document.SelectedTarget = null;
                        Assert.That(document.SelectedValue, Is.Null);
                        Assert.That(document.Values, Is.Empty);
                        Assert.That(document.OpenMonitorCommand.CanExecute(null), Is.False);
                        Assert.That(document.OpenHistoryCommand.CanExecute(null), Is.False);
                        Assert.That(document.ExportSnapshotCommand.CanExecute(null), Is.False);
                        context.Workspace.Verify(workspace => workspace.OpenToolAsync(
                            It.IsAny<PluginKind>(), It.IsAny<EndpointDescription?>(), It.IsAny<CancellationToken>()),
                            Times.Never);
                    }
                }
            }).ConfigureAwait(false);
        }

        private static async Task<CompanionPlugin> OpenDocumentAsync(
            ConnectedProtocolContext context, bool weighing = false)
        {
            context.Session.SetupGet(session => session.Identity).Returns(new UserIdentity());
            if (weighing)
            {
                context.Messages.NamespaceUris.GetIndexOrAppend(Opc.Ua.Scales.Namespaces.Scales);
            }
            await context.ConnectAsync().ConfigureAwait(true);
            string providerId = weighing ? "weighing" : "pumps";
            var target = new CompanionTarget(providerId, new NodeId("pump", 2), "Cooling pump",
                weighing ? "Scale system" : "Pump");
            var provider = new Mock<ICompanionProvider>(MockBehavior.Strict);
            provider.SetupGet(item => item.Descriptor)
                .Returns(new CompanionDescriptor(providerId, "Domain", "urn:fixture:protocol", "Controlled fixture"));
            provider.Setup(item => item.DiscoverAsync(It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<CompanionTarget>)[target]);
            provider.Setup(item => item.InspectAsync(
                It.IsAny<CompanionContext>(), target, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CompanionInspection(
                    [new CompanionValue("Mass flow", Variant.From(12.5))
                    {
                        SourceNodeId = new NodeId("flow", 2),
                        EngineeringMetadata = "kg/s"
                    }],
                    [], "One observed source"));
            var document = new CompanionPlugin(context.Host, providers: [provider.Object],
                kind: weighing ? PluginKind.Weighing : PluginKind.Pumps);
            await document.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
            await document.DiscoverCommand.ExecuteAsync(null).ConfigureAwait(true);
            await document.InspectCommand.ExecuteAsync(null).ConfigureAwait(true);
            return document;
        }
    }
}
