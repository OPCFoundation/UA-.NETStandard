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
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Identity;
using UaLens.Capabilities;
using UaLens.Connection;
using UaLens.Diagnostics;
using UaLens.Storage;
using UaLens.Subscriptions;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.ViewModels;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class MainViewModelLifecycleTests
{
    [TestCase(NodeClass.Variable, true, false, "Variable (read-only) · ns=2;s=Selection")]
    [TestCase(NodeClass.Method, false, true, "Method · ns=2;s=Selection")]
    [TestCase(NodeClass.Object, false, false, "ns=2;s=Selection · no events · 0 child variables")]
    [TestCase(NodeClass.DataType, false, false, "DataType nodes cannot be subscribed.")]
    public Task OfflineSelectionReplacesPreviousActionCapabilitiesWithoutOpeningDocuments(
        NodeClass nodeClass, bool add, bool call, string status)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new ConnectionScenario();
            await using MainViewModel model = scenario.CreateModel();
            var previous = new NodeViewModel(model.Browser, NodeId.Null, new NodeId("Previous", 2),
                "Previous method", NodeClass.Method);
            await model.UpdateSelectionAsync(previous).ConfigureAwait(true);
            Assert.That(model.CanCallMethod, Is.True);
            var next = new NodeViewModel(model.Browser, NodeId.Null, new NodeId("Selection", 2), "Selection", nodeClass);

            await model.UpdateSelectionAsync(next).ConfigureAwait(true);

            Assert.That(model.SelectedNode, Is.SameAs(next));
            Assert.That(model.CanAddSelectedItem, Is.EqualTo(add));
            Assert.That(model.CanCallMethod, Is.EqualTo(call));
            Assert.That(model.CanWriteVariable, Is.False);
            Assert.That(model.SelectedItemIsEvent, Is.False);
            Assert.That(model.SelectionHasEvents, Is.False);
            Assert.That(model.SelectionHasVariables, Is.False);
            Assert.That(model.SelectionVariables.IsEmpty, Is.True);
            Assert.That(model.SelectedItemStatus, Is.EqualTo(status));
            Assert.That(model.Tabs, Is.Empty);
            Assert.That(scenario.Sessions, Is.Empty);
            await model.UpdateSelectionAsync(null).ConfigureAwait(true);
            Assert.That(model.SelectedNode, Is.Null);
            Assert.That(model.CanAddSelectedItem, Is.False);
            Assert.That(model.CanCallMethod, Is.False);
            Assert.That(model.SelectedItemStatus, Is.EqualTo("Pick a Variable, or an Object that emits events."));
        });
    }

    [Test]
    public Task TransportAvailabilityDoesNotDuplicateDocumentAttachmentButNewGenerationDoes()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new ConnectionScenario();
            await using MainViewModel model = scenario.CreateModel();
            var contexts = new List<(long Generation, ISession? Session)>();
            var document = new LifecycleDocument("History")
            {
                Changed = _ =>
                {
                    contexts.Add((scenario.Context.Connection.Snapshot.Generation,
                        scenario.Context.Connection.CurrentSession));
                    return Task.CompletedTask;
                }
            };
            await model.Workspace.OpenAsync(() => document).ConfigureAwait(true);
            await scenario.ConnectAsync().ConfigureAwait(true);
            LifecycleSession first = scenario.Sessions.Single();
            int connectedDeliveries = contexts.Count;
            await DesktopInteraction.ModelChangedAsync(model, () => !model.IsConnected, () =>
            {
                first.Transition(ConnectionPhase.Reconnecting);
                return Task.CompletedTask;
            }).ConfigureAwait(true);
            Assert.That(model.ConnectionStatus, Is.EqualTo("Reconnecting…"));
            Assert.That(contexts, Has.Count.EqualTo(connectedDeliveries));
            Assert.That(first.DisposeCount, Is.Zero);
            await DesktopInteraction.ModelChangedAsync(model, () => model.IsConnected, () =>
            {
                first.Transition(ConnectionPhase.Connected);
                return Task.CompletedTask;
            }).ConfigureAwait(true);
            Assert.That(contexts, Has.Count.EqualTo(connectedDeliveries));
            Assert.That(model.SelectedTab, Is.SameAs(document));

            await model.ToggleEngineCommand.ExecuteAsync(null).ConfigureAwait(true);

            Assert.That(scenario.Sessions, Has.Count.EqualTo(2));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(contexts.Skip(connectedDeliveries).Select(value => value.Session),
                Is.EqualTo(new ISession?[] { null, scenario.Sessions[1].Session }));
            Assert.That(contexts[^1].Generation, Is.EqualTo(2));
            Assert.That(model.Engine, Is.EqualTo(SubscriptionEngineKind.Classic));
            Assert.That(model.EngineButtonText, Is.EqualTo("Engine: Classic"));
            Assert.That(model.SelectedTab, Is.SameAs(document));
            Assert.That(document.DisposeCount, Is.Zero);
        });
    }

    [Test]
    public Task SupersededAvailabilityDeliveryCannotPublishAnOldSessionGeneration()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new ConnectionScenario();
            var dispatcher = new ControlledWorkspaceDispatcher();
            await using MainViewModel model = scenario.CreateModel(dispatcher);
            var document = new LifecycleDocument("Owned document");
            await model.Workspace.OpenAsync(() => document).ConfigureAwait(true);
            await scenario.ConnectAsync().ConfigureAwait(true);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            ControlledWorkspaceDispatcher.PausedInvocation paused = dispatcher.PauseNext();
            try
            {
                scenario.Sessions[0].Transition(ConnectionPhase.Reconnecting);
                await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
                await scenario.Context.Connection.ReconnectAsync(SubscriptionEngineKind.Classic).ConfigureAwait(true);
                Assert.That(scenario.Context.Connection.Snapshot.Generation, Is.EqualTo(2));
                Assert.That(model.ConnectionStatus, Is.EqualTo("Connected — Classic"));
                var statuses = new List<string>();
                model.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.ConnectionStatus))
                    {
                        statuses.Add(model.ConnectionStatus);
                    }
                };

                await paused.ResumeAsync().ConfigureAwait(true);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                Assert.That(statuses, Does.Not.Contain("Reconnecting…"));
                Assert.That(model.ConnectionStatus, Is.EqualTo("Connected — Classic"));
                Assert.That(model.IsConnected, Is.True);
                Assert.That(model.Tabs.Single(), Is.SameAs(document));
                Assert.That(scenario.Sessions[0].DisposeCount, Is.EqualTo(1));
                Assert.That(scenario.Sessions[1].DisposeCount, Is.Zero);
            }
            finally
            {
                paused.Release.TrySetResult();
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task DisconnectDeliversEveryDocumentBeforeSessionDestructionAndReportsFailures(bool firstFails)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new ConnectionScenario();
            await using MainViewModel model = scenario.CreateModel();
            var first = new LifecycleDocument("First");
            var second = new LifecycleDocument("Second");
            await model.Workspace.OpenAsync(() => first).ConfigureAwait(true);
            await model.Workspace.OpenAsync(() => second).ConfigureAwait(true);
            await scenario.ConnectAsync().ConfigureAwait(true);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new IOException("First document release failed");
            first.Changed = async token =>
            {
                Assert.That(scenario.Context.Connection.CurrentSession, Is.Null);
                Assert.That(token.IsCancellationRequested, Is.False);
                scenario.Order.Add("first release started");
                entered.TrySetResult();
                await release.Task.ConfigureAwait(true);
                scenario.Order.Add("first release finished");
                if (firstFails)
                {
                    throw failure;
                }
            };
            second.Changed = _ =>
            {
                scenario.Order.Add("second released");
                return Task.CompletedTask;
            };
            scenario.Order.Clear();
            Task disconnect = model.ConnectCommand.ExecuteAsync(null);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
                Assert.That(disconnect.IsCompleted, Is.False);
                Assert.That(scenario.Sessions[0].DisposeCount, Is.Zero);
                Assert.That(scenario.Context.ConfigurationsDisposed, Is.Zero);
                Assert.That(scenario.Order, Is.EqualTo(s_disconnectDeliversEveryDocumentBeforeSessionDestructionAndRepExpected));
                release.SetResult();
                await disconnect.ConfigureAwait(true);

                Assert.That(scenario.Order,
                    Is.EqualTo(s_disconnectDeliversEveryDocumentBeforeSessionDestructionAndRepExpected2));
                Assert.That(scenario.Sessions[0].DisposeCount, Is.EqualTo(1));
                Assert.That(scenario.Context.ConfigurationsDisposed, Is.EqualTo(1));
                Assert.That(model.Tabs, Is.EqualTo(new IPlugin[] { first, second }));
                Assert.That(first.DisposeCount, Is.Zero);
                Assert.That(second.DisposeCount, Is.Zero);
                if (firstFails)
                {
                    Assert.That(model.OperationError, Does.StartWith("Connection failed:")
                        .And.Contain("First document release failed"));
                    Assert.That(model.ConnectionStatus, Is.EqualTo(model.OperationError));
                }
                else
                {
                    Assert.That(model.OperationError, Is.Null);
                    Assert.That(model.ConnectionStatus, Is.EqualTo("Disconnected"));
                    Assert.That(model.SelectedNode, Is.Null);
                }
            }
            finally
            {
                release.TrySetResult();
                await disconnect.ConfigureAwait(true);
            }
        });
    }

    [TestCase("aggregate")]
    [TestCase("timeout")]
    [TestCase("canceled")]
    public Task CommandFailuresReportEveryCauseAndCancelWithoutMislabelingIt(string kind)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new ConnectionScenario();
            await using MainViewModel model = scenario.CreateModel();
            Exception failure = kind switch
            {
                "aggregate" => new AggregateException(
                    new IOException("first failure"), new AggregateException(new InvalidOperationException("second failure"))),
                "timeout" => new TimeoutException("credential flow timed out"),
                _ => new OperationCanceledException()
            };
            int calls = 0;
            model.ConnectRequestedAsync = _ =>
            {
                calls++;
                return Task.FromException(failure);
            };

            await model.ConnectCommand.ExecuteAsync(null).ConfigureAwait(true);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(model.ConnectionStatus, Is.EqualTo(kind switch
            {
                "aggregate" => "Connection failed: first failure; second failure",
                "timeout" => "Connection failed: credential flow timed out",
                _ => "Connection cancelled."
            }));
            Assert.That(model.OperationError, kind == "canceled" ? Is.Null : Is.EqualTo(model.ConnectionStatus));
            Assert.That(scenario.Sessions, Is.Empty);
            Assert.That(model.ConnectCommand.IsRunning, Is.False);
            model.ConnectRequestedAsync = null;
            await model.ConnectCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.That(model.OperationError, Is.Null);
            Assert.That(model.ConnectionStatus,
                Is.EqualTo("Select an endpoint, security policy and identity using Connect."));
        });
    }

    [Test]
    public Task PublishingSettingsSurviveReconnectAndOfflineEngineToggleChangesOnlyIntent()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new ConnectionScenario();
            await using MainViewModel model = scenario.CreateModel();
            await model.Workspace.OpenAsync(() => new LifecycleDocument("Preserved")).ConfigureAwait(true);
            Assert.That(() => model.ConfigurePublishingPipeline(new SessionPublishingSettings(3, 9)),
                Throws.InvalidOperationException.With.Message.EqualTo("Connect before changing session publishing settings."));
            await scenario.ConnectAsync().ConfigureAwait(true);
            var settings = new SessionPublishingSettings(5, 11);
            model.ConfigurePublishingPipeline(settings);
            Assert.That(scenario.Sessions[0].Protocol.Object.MinPublishRequestCount, Is.EqualTo(5));
            Assert.That(scenario.Sessions[0].Protocol.Object.MaxPublishRequestCount, Is.EqualTo(11));

            await model.ToggleEngineCommand.ExecuteAsync(null).ConfigureAwait(true);

            Assert.That(model.PublishingPipeline, Is.SameAs(settings));
            Assert.That(scenario.Sessions[1].Protocol.Object.MinPublishRequestCount, Is.EqualTo(5));
            Assert.That(scenario.Sessions[1].Protocol.Object.MaxPublishRequestCount, Is.EqualTo(11));
            await model.ConnectCommand.ExecuteAsync(null).ConfigureAwait(true);
            await model.ToggleEngineCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.That(model.Engine, Is.EqualTo(SubscriptionEngineKind.ChannelV2));
            Assert.That(model.UseChannelV2Engine, Is.True);
            Assert.That(model.PublishingPipeline, Is.SameAs(settings));
            Assert.That(scenario.Sessions, Has.Count.EqualTo(2));
            Assert.That(model.SnapshotSession().PublishingPipeline, Is.SameAs(settings));
            Assert.That(model.ConnectionStatus,
                Is.EqualTo("Engine selected. Use Connect to confirm the connection profile and credentials."));
        });
    }

    [Test]
    public Task ResourceStartupRunsOnceAndTransfersOnlyACompletedHostToTheModel()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new ConnectionScenario();
            var resource = new ControlledResource();
            await using MainViewModel model = scenario.CreateModel(startResource: resource.StartAsync);
            Task first = model.StartResourceMonitoringAsync();
            Task second = model.StartResourceMonitoringAsync();
            Assert.That(second, Is.SameAs(first));
            Assert.That(first.IsCompleted, Is.False);
            Assert.That(model.ResourceMonitor, Is.Null);
            Assert.That(resource.Starts, Is.EqualTo(1));
            resource.Release.SetResult();

            await first.ConfigureAwait(true);

            Assert.That(model.ResourceMonitor, Is.SameAs(resource.Owner));
            Assert.That(model.StartResourceMonitoringAsync(), Is.SameAs(first));
            Assert.That(model.ResourceSample.Cpu, Is.NaN);
            Assert.That(resource.Samples, Is.Zero);
            Assert.That(resource.Disposals, Is.Zero);
            await model.DisposeAsync().ConfigureAwait(true);
            Assert.That(resource.Stops, Is.EqualTo(1));
            Assert.That(resource.Disposals, Is.EqualTo(1));
            Assert.That(model.ResourceMonitor, Is.Null);
            Assert.That(() => model.StartResourceMonitoringAsync(), Throws.InstanceOf<ObjectDisposedException>());
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task ResourceStartupCancellationOrFailureIsOwnedUntilCleanupFinishes(bool failStart)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new ConnectionScenario();
            var resource = new ControlledResource();
            var failure = new IOException("metrics startup failed");
            var model = scenario.CreateModel(startResource: resource.StartAsync);
            Task startup = model.StartResourceMonitoringAsync();
            if (failStart)
            {
                resource.Release.SetException(failure);
                await Assert.ThatAsync(() => startup, Throws.Exception.SameAs(failure)).ConfigureAwait(true);
                Assert.That(model.ResourceStatus, Is.EqualTo("Resource monitoring unavailable: metrics startup failed"));
                Assert.That(model.StartResourceMonitoringAsync(), Is.SameAs(startup));
                await model.DisposeAsync().ConfigureAwait(true);
            }
            else
            {
                Task disposal = model.DisposeAsync().AsTask();
                Assert.That(resource.Token.IsCancellationRequested, Is.True);
                Assert.That(disposal.IsCompleted, Is.False);
                Assert.That(resource.Disposals, Is.Zero);
                resource.Release.SetResult();
                await Assert.ThatAsync(() => startup, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(true);
                await disposal.ConfigureAwait(true);
            }
            Assert.That(resource.Starts, Is.EqualTo(1));
            Assert.That(resource.Stops, Is.EqualTo(1));
            Assert.That(resource.Disposals, Is.EqualTo(1));
            Assert.That(model.ResourceMonitor, Is.Null);
            Assert.That(resource.Samples, Is.Zero);
            await model.DisposeAsync().ConfigureAwait(true);
            Assert.That(resource.Disposals, Is.EqualTo(1));
        });
    }

    [Test]
    public Task DisposeIsIdempotentAndContinuesAfterEarlierDocumentCleanupFailure()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new ConnectionScenario();
            var resource = new ControlledResource();
            var model = scenario.CreateModel(startResource: resource.StartAsync);
            var failure = new IOException("document cleanup failed");
            var first = new LifecycleDocument("Failing") { DisposeFailure = failure };
            var second = new LifecycleDocument("Later");
            await model.Workspace.OpenAsync(() => first).ConfigureAwait(true);
            await model.Workspace.OpenAsync(() => second).ConfigureAwait(true);
            await scenario.ConnectAsync().ConfigureAwait(true);
            resource.Release.SetResult();
            await model.StartResourceMonitoringAsync().ConfigureAwait(true);

            await Assert.ThatAsync(async () => await model.DisposeAsync().ConfigureAwait(true),
                Throws.Exception.SameAs(failure)).ConfigureAwait(true);
            await Assert.ThatAsync(async () => await model.DisposeAsync().ConfigureAwait(true),
                Throws.Exception.SameAs(failure)).ConfigureAwait(true);

            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
            Assert.That(model.Workspace.IsClosing, Is.True);
            Assert.That(model.Tabs, Is.Empty);
            Assert.That(scenario.Sessions.Single().DisposeCount, Is.EqualTo(1));
            Assert.That(scenario.Context.ConfigurationsDisposed, Is.EqualTo(1));
            Assert.That(resource.Stops, Is.EqualTo(1));
            Assert.That(resource.Disposals, Is.EqualTo(1));
            Assert.That(model.ToggleEngineCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public Task SessionSnapshotCapturesExistingDocumentOrderAndSelectionWithoutCreatingTools()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new ConnectionScenario();
            await using MainViewModel model = scenario.CreateModel();
            var first = new LifecycleDocument("Boiler history") { State = JsonSerializer.SerializeToElement(new { tag = 17 }) };
            var second = new LifecycleDocument("Pressure history") { State = JsonSerializer.SerializeToElement(new { tag = 29 }) };
            await model.Workspace.OpenAsync(() => first).ConfigureAwait(true);
            await model.Workspace.OpenAsync(() => second).ConfigureAwait(true);
            model.SelectedTab = first;
            model.EndpointUrl = "opc.tcp://saved.invalid:4840";
            model.IsAddressSpaceVisible = false;
            model.CycleAttributesPanelMode();
            model.CycleAttributesPanelMode();

            SessionFile snapshot = model.SnapshotSession();

            Assert.That(snapshot.Version, Is.EqualTo("2"));
            Assert.That(snapshot.EndpointUrl, Is.EqualTo("opc.tcp://saved.invalid:4840"));
            Assert.That(snapshot.Documents.Select(document => document.Title),
                Is.EqualTo(s_sessionSnapshotCapturesExistingDocumentOrderAndSelectionWithoExpected));
            Assert.That(snapshot.Documents.Select(document => document.Settings.GetProperty("tag").GetInt32()),
                Is.EqualTo(s_sessionSnapshotCapturesExistingDocumentOrderAndSelectionWithoExpected2));
            Assert.That(snapshot.SelectedDocument, Is.Zero);
            Assert.That(snapshot.ShowAddressSpace, Is.False);
            Assert.That(snapshot.Inspector, Is.EqualTo(SidePanelMode.AttrsAndRefs));
            Assert.That(model.ShowAttributes, Is.True);
            Assert.That(model.ShowReferences, Is.True);
            Assert.That(first.Captures, Is.EqualTo(1));
            Assert.That(second.Captures, Is.EqualTo(1));
            Assert.That(scenario.Sessions, Is.Empty);
            Assert.That(scenario.ResourceRequests, Is.Zero);
        });
    }

    private sealed class ConnectionScenario : IAsyncDisposable
    {
        public ConnectionScenario()
        {
            Endpoint = new EndpointDescription("opc.tcp://controlled.invalid:4840")
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport,
                Server = new ApplicationDescription { ApplicationUri = "urn:unit:test:lifecycle" },
                UserIdentityTokens = [new UserTokenPolicy(UserTokenType.Anonymous) { PolicyId = "anonymous" }]
            };
            Profile = ConnectionProfile.Create(
                Endpoint, Endpoint.UserIdentityTokens[0], SubscriptionEngineKind.ChannelV2);
            Context.DiscoverAsync = (_, _) => Task.FromResult<ArrayOf<EndpointDescription>>([Endpoint]);
            Context.Backend.Setup(backend => backend.ConnectAsync(It.IsAny<ApplicationConfiguration>(),
                It.IsAny<EndpointDescription>(), It.IsAny<ConnectionProfile>(), It.IsAny<IClientIdentityProvider>(),
                It.IsAny<CancellationToken>())).Returns((ApplicationConfiguration _, EndpointDescription endpoint,
                    ConnectionProfile _, IClientIdentityProvider _, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Assert.That(endpoint, Is.Not.SameAs(Endpoint));
                    Assert.That(endpoint.IsEqual(Endpoint), Is.True);
                    var connection = new LifecycleSession(Sessions.Count + 1, Order);
                    Sessions.Add(connection);
                    return Task.FromResult<IConnectionSession>(connection);
                });
        }

        public DesktopConnectionContext Context { get; } = new();
        public List<LifecycleSession> Sessions { get; } = [];
        public List<string> Order { get; } = [];
        public EndpointDescription Endpoint { get; }
        public ConnectionProfile Profile { get; }
        public int ResourceRequests { get; private set; }

        public MainViewModel CreateModel(
            IWorkspaceDispatcher? dispatcher = null,
            Func<CancellationToken, Task<ResourceMonitorHost>>? startResource = null)
        {
            dispatcher ??= new AvaloniaWorkspaceDispatcher();
            var operations = new PluginDocumentOperations(Context.Connection);
            var workspace = new DocumentWorkspace<IPlugin>(
                NullLogger.Instance, dispatcher, operations.SynchronizeConnectionAsync);
            return new MainViewModel(
                Context.Telemetry, Context.Connection, workspace, new CommandRegistry(), operations, dispatcher,
                startResource ?? (_ =>
                {
                    ResourceRequests++;
                    return Task.FromException<ResourceMonitorHost>(new InvalidOperationException("Unexpected monitor."));
                }), new Mock<ICapabilityService>().Object);
        }

        public Task ConnectAsync() => Context.Connection.ConnectAsync(Profile);
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class LifecycleSession : IConnectionSession
    {
        private readonly int m_number;
        private readonly List<string> m_order;
        public LifecycleSession(int number, List<string> order)
        {
            m_number = number;
            m_order = order;
            Protocol.SetupGet(session => session.Connected).Returns(() => State.Phase == ConnectionPhase.Connected);
            Protocol.SetupGet(session => session.SessionId).Returns(new NodeId((uint)number));
            Protocol.SetupGet(session => session.NamespaceUris).Returns(new NamespaceTable());
            Protocol.SetupProperty(session => session.MinPublishRequestCount, 2);
            Protocol.SetupProperty(session => session.MaxPublishRequestCount, 15);
        }

        public Mock<ISession> Protocol { get; } = new();
        public ISession Session => Protocol.Object;
        public ConnectionSessionState State { get; private set; } = new(ConnectionPhase.Connected);
        public int DisposeCount { get; private set; }
        public event Action<IConnectionSession, ConnectionSessionState>? StateChanged;
        public void Transition(ConnectionPhase phase)
        {
            State = new ConnectionSessionState(phase);
            StateChanged?.Invoke(this, State);
        }
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            m_order.Add($"session {m_number} disposed");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LifecycleDocument : ObservableObject, IPlugin, IWorkspaceState
    {
        public LifecycleDocument(string title)
        {
            Title = title;
        }

        public string Title { get; set; }
        public bool IsRenaming { get; set; }
        public PluginKind Kind => PluginKind.Historian;
        public Control? View => null;
        public Control? HeaderToolbar => null;
        public string Status => Title;
        public bool SupportsDuplicate => false;
        public int DisposeCount { get; private set; }
        public int Captures { get; private set; }
        public Exception? DisposeFailure { get; init; }
        public JsonElement State { get; init; } = JsonSerializer.SerializeToElement(new { retained = true });
        public Func<CancellationToken, Task> Changed { get; set; } = _ => Task.CompletedTask;
        public void OnActivated() { }
        public void OnDeactivated() { }
        public IReadOnlyList<MenuItem> ContributeMenuItems() => [];
        public Task OnConnectionStateChangedAsync(CancellationToken cancellationToken) => Changed(cancellationToken);
        public JsonElement CaptureState()
        {
            Captures++;
            return State;
        }
        public Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Existing lifecycle documents are not factory-created.");
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return DisposeFailure is { } failure ? ValueTask.FromException(failure) : ValueTask.CompletedTask;
        }
    }

    private sealed class ControlledResource
    {
        public ControlledResource()
        {
            m_host.Setup(host => host.StartAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken token) =>
            {
                Starts++;
                Token = token;
                return Release.Task;
            });
            m_host.Setup(host => host.StopAsync(It.IsAny<CancellationToken>())).Returns(() =>
            {
                Stops++;
                return Task.CompletedTask;
            });
            m_host.As<IAsyncDisposable>().Setup(host => host.DisposeAsync()).Returns(() =>
            {
                Disposals++;
                return ValueTask.CompletedTask;
            });
        }

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public ResourceMonitorHost? Owner { get; private set; }
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public int Disposals { get; private set; }
        public int Samples { get; private set; }
        private readonly Mock<IHost> m_host = new(MockBehavior.Strict);

        public async Task<ResourceMonitorHost> StartAsync(CancellationToken token)
        {
            Owner = await ResourceMonitorHost.StartAsync(m_host.Object, NullLogger.Instance, () =>
            {
                Samples++;
                return (23.5, 128);
            }, token).ConfigureAwait(false);
            return Owner;
        }
    }

    private static readonly string[] s_disconnectDeliversEveryDocumentBeforeSessionDestructionAndRepExpected =
    [
        "first release started",
    ];
    private static readonly string[] s_disconnectDeliversEveryDocumentBeforeSessionDestructionAndRepExpected2 =
    [
        "first release started",
        "first release finished",
        "second released",
        "session 1 disposed",
    ];
    private static readonly string[] s_sessionSnapshotCapturesExistingDocumentOrderAndSelectionWithoExpected =
    [
        "Boiler history",
        "Pressure history",
    ];
    private static readonly int[] s_sessionSnapshotCapturesExistingDocumentOrderAndSelectionWithoExpected2 =
    [
        17,
        29,
    ];
}
