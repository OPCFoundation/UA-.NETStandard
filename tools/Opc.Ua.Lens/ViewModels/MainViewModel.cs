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
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using UaLens.Capabilities;
using UaLens.Connection;
using UaLens.Diagnostics;
using UaLens.Plugins.Gds;
using UaLens.Plugins.SubscriptionBench;
using UaLens.Subscriptions;
using UaLens.Telemetry;
using UaLens.Storage;
using UaLens.Workspace;

namespace UaLens.ViewModels;

/// <summary>
/// Visibility of the contextual Attributes and References panels.
/// </summary>
internal enum SidePanelMode
{
    None,
    AttrsOnly,
    AttrsAndRefs,
    RefsOnly
}

/// <summary>
/// Binding facade for the desktop. Document membership and lifecycle belong to
/// the document workspace; the connection module owns connection policy and sessions.
/// </summary>
internal sealed partial class MainViewModel : ObservableObject, IPluginWorkspace, IAsyncDisposable
{
    public MainViewModel()
        : this(new AppTelemetryContext(new LogRingBuffer(capacity: 4096)))
    {
    }

    public MainViewModel(
        AppTelemetryContext telemetry,
        ConnectionService? connection = null,
        DocumentWorkspace<IPlugin>? workspace = null,
        CommandRegistry? commands = null,
        PluginDocumentOperations? documentOperations = null,
        IWorkspaceDispatcher? dispatcher = null,
        Func<CancellationToken, Task<ResourceMonitorHost>>? startResourceMonitor = null,
        ICapabilityService? capabilities = null,
        IPluginFactory? pluginFactory = null)
    {
        Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        m_logBuffer = telemetry.Buffer;
        m_log = telemetry.CreateLogger("Main");
        m_dispatcher = dispatcher ?? new AvaloniaWorkspaceDispatcher();
        m_startResourceMonitor = startResourceMonitor
            ?? (cancellationToken => ResourceMonitorHost.StartAsync(telemetry, cancellationToken));
        Connection = connection ?? new ConnectionService(telemetry, new PublishLogObserver());
        m_workspaceProfile = Connection.IsConnected ? Connection.Profile : null;
        PublishLog = Connection.PublishLog ?? new PublishLogObserver();
        Browser = new BrowserViewModel(telemetry, Connection);
        Attributes = new NodeAttributesViewModel(telemetry, Connection);
        References = new ReferencesViewModel(telemetry, Connection);
        m_documentOperations = documentOperations ?? new PluginDocumentOperations(Connection);
        Workspace = workspace ?? new DocumentWorkspace<IPlugin>(
            m_log, m_dispatcher, m_documentOperations.SynchronizeConnectionAsync);
        Commands = commands ?? new CommandRegistry();
        m_pluginHost = new PluginHost(this, Connection, Browser, telemetry, capabilities, pluginFactory);

        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
        Tabs.CollectionChanged += OnDocumentsChanged;
        Connection.StateChanged += OnConnectionStateChanged;
        Connection.ConnectionChangedAsync += OnConnectionChangedAsync;
        RegisterCommands();

        m_logPump = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => PumpLog());
        m_resourcePump = new DispatcherTimer(
            TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => PumpResources());
        m_logPump.Start();
        m_resourcePump.Start();
        RefreshConnectionPresentation(Connection.Snapshot);
        MainViewModelLog.Started(m_log);
    }

    public AppTelemetryContext Telemetry { get; }
    public ConnectionService Connection { get; }
    public BrowserViewModel Browser { get; }
    public NodeAttributesViewModel Attributes { get; }
    public ReferencesViewModel References { get; }
    public PublishLogObserver PublishLog { get; }
    public DocumentWorkspace<IPlugin> Workspace { get; }
    public CommandRegistry Commands { get; }

    /// <summary>
    /// Read-only live collection retained for existing desktop bindings.
    /// </summary>
    public DocumentCollection<IPlugin> Tabs => Workspace.Documents;

    public IPlugin? SelectedTab
    {
        get => Workspace.ActiveDocument;
        set => Workspace.Activate(value);
    }

    public IPlugin? ActiveDocument => Workspace.ActiveDocument;
    public SubscriptionViewModel? SelectedSubscriptionTab => SelectedTab as SubscriptionViewModel;
    public bool IsSubscriptionTabActive => SelectedTab is SubscriptionViewModel;
    public bool IsCustomTabActive => SelectedTab is not null and not SubscriptionViewModel;
    public bool HasAnySubscriptionTab => Tabs.OfType<SubscriptionViewModel>().Any();
    public ObservableCollection<string> LogLines { get; } = new();
    public NodeViewModel? SelectedNode => m_selectedNode;
    public ArrayOf<(NodeId NodeId, string DisplayName)> SelectionVariables { get; private set; } = [];
    public ResourceMonitorHost? ResourceMonitor { get; set; }
    public (double Cpu, double MemMiB) ResourceSample
        => ResourceMonitor?.LastSample ?? (double.NaN, double.NaN);

    /// <summary>
    /// Desktop adapter for the explicit endpoint, security and identity selection flow.
    /// No fallback silently chooses an anonymous or unsecured connection.
    /// </summary>
    public Func<CancellationToken, Task>? ConnectRequestedAsync { get; set; }

    public ConnectionProfile? RestoredConnectionProfile { get; private set; }

    public string? OperationError
    {
        get => m_operationError;
        private set => SetProperty(ref m_operationError, value);
    }

    public SessionPublishingSettings? PublishingPipeline { get; private set; }

    public void ConfigurePublishingPipeline(SessionPublishingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Apply(Connection.CurrentSession
            ?? throw new InvalidOperationException("Connect before changing session publishing settings."));
        PublishingPipeline = settings;
    }

    public bool ShowAttributes
        => AttributesPanelMode is SidePanelMode.AttrsOnly or SidePanelMode.AttrsAndRefs;
    public bool ShowReferences
        => AttributesPanelMode is SidePanelMode.AttrsAndRefs or SidePanelMode.RefsOnly;

    public bool UseChannelV2Engine
    {
        get => Engine == SubscriptionEngineKind.ChannelV2;
        set
        {
            if (value == UseChannelV2Engine)
            {
                return;
            }
            if (ToggleEngineCommand.CanExecute(null))
            {
                ToggleEngineCommand.Execute(null);
                return;
            }
            OnPropertyChanged(nameof(UseChannelV2Engine));
        }
    }

    private bool CanChangeEngine => m_disposal is null
        && Connection.Snapshot.Phase != ConnectionPhase.Connecting;

    public PluginHost CreatePluginHost() => m_pluginHost;

    public Task<IPlugin> OpenToolAsync(
        PluginKind kind,
        EndpointDescription? discoveryEndpoint = null,
        CancellationToken cancellationToken = default)
    {
        return OpenDocumentAsync(
            kind,
            discoveryEndpoint is null ? null : new ToolSeed(DiscoveryEndpoint: discoveryEndpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Compatibility facade for existing node and GDS actions. Seeding is awaited
    /// as document-owned work, including reuse of a running bench document.
    /// </summary>
    public async Task AddPluginAsync(
        PluginKind kind,
        NodeViewModel? seedEventSource = null,
        bool seedPickTarget = false,
        RegisteredApplicationContext? seedRegisteredApp = null,
        EndpointDescription? seedDiscoveryEndpoint = null,
        NodeViewModel? seedBenchNode = null)
    {
        var seed = new ToolSeed(
            seedEventSource, seedPickTarget, seedRegisteredApp, seedDiscoveryEndpoint, seedBenchNode);
        if (kind == PluginKind.SubscriptionBench && seedBenchNode is not null
            && Tabs.OfType<SubscriptionBenchPlugin>().LastOrDefault() is { } existing)
        {
            Workspace.Activate(existing);
            await Workspace.ConfigureAsync(
                existing,
                (document, token) => m_documentOperations.SeedExistingAsync(document, seed, token),
                m_lifetime.Token).ConfigureAwait(true);
            return;
        }
        await OpenDocumentAsync(kind, seed, cancellationToken: m_lifetime.Token).ConfigureAwait(true);
    }

    public async Task<SubscriptionViewModel?> DuplicateTabAsync(SubscriptionViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!Tabs.Contains(source))
        {
            throw new ArgumentException("The source document is not open.", nameof(source));
        }
        SubscriptionDocumentState snapshot = SubscriptionDocumentState.Capture(source)
            with { Title = $"{source.Title} (copy)" };
        IPlugin copy = await OpenDocumentAsync(
            PluginKind.Subscription, subscription: snapshot, cancellationToken: m_lifetime.Token).ConfigureAwait(true);
        return (SubscriptionViewModel)copy;
    }

    public SessionFile SnapshotSession()
    {
        var file = new SessionFile
        {
            Version = "2",
            EndpointUrl = EndpointUrl,
            Engine = Engine.ToString(),
            Profile = m_workspaceProfile,
            PublishingPipeline = Connection.CurrentSession is { } session
                ? new SessionPublishingSettings(session.MinPublishRequestCount, session.MaxPublishRequestCount)
                : PublishingPipeline,
            ShowAddressSpace = IsAddressSpaceVisible,
            Inspector = AttributesPanelMode
        };
        for (int index = 0; index < Tabs.Count; index++)
        {
            IPlugin document = Tabs[index];
            if (document is not IWorkspaceState state)
            {
                throw new NotSupportedException($"The {document.Kind} tool cannot save its configuration.");
            }
            file.Documents.Add(new SessionFile.DocumentSnapshot
            {
                Kind = document.Kind.ToString(),
                Title = document.Title,
                Settings = state.CaptureState()
            });
            if (ReferenceEquals(document, SelectedTab))
            {
                file.SelectedDocument = index;
            }
        }
        file.Validate();
        return file;
    }

    /// <summary>
    /// Imports legacy subscription configuration while disconnected. Legacy files
    /// contain no security or identity intent and therefore never authorize a reconnect.
    /// </summary>
    public async Task LoadSessionAsync(SessionFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        file.Validate();
        if (file.Version == "2")
        {
            await RestoreWorkspaceAsync(file).ConfigureAwait(true);
            return;
        }
        if (!Enum.TryParse(file.Engine, ignoreCase: true, out SubscriptionEngineKind engine)
            || !Enum.IsDefined(engine))
        {
            throw new FormatException($"Unknown subscription engine '{file.Engine}'.");
        }
        ArrayOf<SubscriptionDocumentState> snapshots =
            [.. file.Tabs.Select(SubscriptionDocumentState.Import)];
        await Connection.DisconnectAsync().ConfigureAwait(true);
        await SynchronizeConnectionAsync(m_lifetime.Token).ConfigureAwait(true);
        ArrayOf<DocumentRestore<IPlugin>> documents = snapshots.ConvertAll(
            snapshot => m_documentOperations.Create(
                m_pluginHost, PluginKind.Subscription, subscription: snapshot));
        await Workspace.RestoreAsync(documents, cancellationToken: m_lifetime.Token, onCommitted: () =>
        {
            EndpointUrl = file.EndpointUrl;
            Engine = engine;
            RestoredConnectionProfile = null;
            m_workspaceProfile = null;
            PublishingPipeline = null;
        }).ConfigureAwait(true);
        ConnectionStatus = "Workspace restored offline. Select an endpoint, security policy and identity to connect.";
        MainViewModelLog.LegacyWorkspaceRestored(m_log, documents.Count);
    }

    private async Task RestoreWorkspaceAsync(SessionFile file)
    {
        var prepared = new List<DocumentRestore<IPlugin>>(file.Documents.Count);
        foreach (SessionFile.DocumentSnapshot snapshot in file.Documents)
        {
            PluginKind kind = Enum.Parse<PluginKind>(snapshot.Kind);
            PluginRegistration registration = PluginRegistry.For(kind);
            prepared.Add(new DocumentRestore<IPlugin>(
                () => registration.Factory(m_pluginHost),
                async (document, cancellationToken) =>
                {
                    if (document is not IWorkspaceState state)
                    {
                        throw new NotSupportedException($"The {kind} tool cannot restore its configuration.");
                    }
                    await state.RestoreStateAsync(snapshot.Settings, cancellationToken).ConfigureAwait(true);
                    document.Title = snapshot.Title;
                }));
        }
        await Connection.DisconnectAsync().ConfigureAwait(true);
        await SynchronizeConnectionAsync(m_lifetime.Token).ConfigureAwait(true);
        SubscriptionEngineKind engine = file.Profile?.Engine
            ?? Enum.Parse<SubscriptionEngineKind>(file.Engine, ignoreCase: true);
        await Workspace.RestoreAsync([.. prepared], Math.Max(0, file.SelectedDocument), () =>
        {
            EndpointUrl = file.Profile?.EndpointUrl ?? file.EndpointUrl;
            Engine = engine;
            RestoredConnectionProfile = file.Profile;
            m_workspaceProfile = file.Profile;
            PublishingPipeline = file.PublishingPipeline;
            IsAddressSpaceVisible = file.ShowAddressSpace;
            AttributesPanelMode = file.Inspector;
            OnPropertyChanged(nameof(RestoredConnectionProfile));
        }, m_lifetime.Token).ConfigureAwait(true);
        ConnectionStatus = file.Profile is null
            ? "Workspace restored offline. Select security and identity before connecting."
            : $"Workspace restored offline: {file.Profile.SecurityMode}, {file.Profile.IdentityType}. " +
                "Connect to confirm the saved profile; workloads remain stopped.";
    }

    public void CycleAttributesPanelMode()
    {
        AttributesPanelMode = AttributesPanelMode switch
        {
            SidePanelMode.None => SidePanelMode.AttrsOnly,
            SidePanelMode.AttrsOnly => SidePanelMode.AttrsAndRefs,
            SidePanelMode.AttrsAndRefs => SidePanelMode.RefsOnly,
            _ => SidePanelMode.None
        };
    }

    /// <summary>
    /// Starts optional monitoring after the desktop dispatcher is running. Startup
    /// remains owned even if the window closes before the host finishes starting.
    /// </summary>
    public Task StartResourceMonitoringAsync()
    {
        lock (m_lifecycle)
        {
            ObjectDisposedException.ThrowIf(m_disposal is not null, this);
            m_resourceStartup ??= ResourceMonitor is null ? StartResourceMonitoringCoreAsync() : Task.CompletedTask;
            return m_resourceStartup;
        }
    }

    /// <summary>
    /// Queues the current connection snapshot and awaits document synchronization.
    /// New notifications cancel older deliveries; shutdown drains the complete queue.
    /// </summary>
    public Task SynchronizeConnectionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (m_lifecycle)
        {
            ObjectDisposedException.ThrowIf(m_disposal is not null, this);
            if (m_isReleasingConnection)
            {
                return m_disconnectWork.WaitAsync(cancellationToken);
            }
            if (m_isApplyingConnectionChange)
            {
                return m_connectionWork.WaitAsync(cancellationToken);
            }
            ConnectionSnapshot snapshot = Connection.Snapshot;
            if (snapshot == m_queuedConnectionSnapshot
                && !m_connectionWork.IsCanceled && !m_connectionWork.IsFaulted)
            {
                return m_connectionWork.WaitAsync(cancellationToken);
            }
            m_connectionWork = CreateConnectionDelivery(snapshot, cancellationToken);
            return m_connectionWork;
        }
    }

    public Task UpdateSelectionAsync(NodeViewModel? node)
    {
        m_dispatcher.VerifyAccess();
        m_selectedNode = node;
        OnPropertyChanged(nameof(SelectedNode));
        Task previous = m_selectionWork;
        CancellationTokenSource? previousSource = m_selectionCancellation;
        Task cancellation = previousSource?.CancelAsync() ?? Task.CompletedTask;
        m_selectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime.Token);
        m_selectionWork = UpdateSelectionCoreAsync(
            node, previous, cancellation, previousSource, m_selectionCancellation.Token);
        return m_selectionWork;
    }

    public async ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null;
        Task disposal;
        lock (m_lifecycle)
        {
            if (m_disposal is null)
            {
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                m_disposal = completion.Task;
            }
            disposal = m_disposal;
        }
        if (completion is not null)
        {
            try
            {
                try
                {
                    await m_dispatcher.InvokeAsync(DisposeCoreAsync).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await m_pluginHost.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        m_selectionCancellation?.Dispose();
                        m_connectionCancellation?.Dispose();
                        m_lifetime.Dispose();
                    }
                }
                completion.TrySetResult();
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
                await disposal.ConfigureAwait(false);
                throw;
            }
        }
        await disposal.ConfigureAwait(false);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            OperationError = null;
            await ConnectCoreAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (Connection.Snapshot.Phase != ConnectionPhase.Failed)
        {
            ConnectionStatus = "Connection cancelled.";
        }
        catch (Exception error) when (IsConnectionFailure(error))
        {
            ReportConnectionFailure("Connection", error);
        }
    }

    private async Task ConnectCoreAsync(CancellationToken cancellationToken)
    {
        if (Connection.Snapshot.Phase == ConnectionPhase.Connecting)
        {
            await Connection.CancelAsync().ConfigureAwait(true);
            return;
        }
        if (Connection.CurrentSession is not null)
        {
            await Connection.DisconnectAsync().ConfigureAwait(true);
            return;
        }
        if (ConnectRequestedAsync is null)
        {
            ConnectionStatus = "Select an endpoint, security policy and identity using Connect.";
            return;
        }
        await ConnectRequestedAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanChangeEngine))]
    private async Task ToggleEngineAsync()
    {
        try
        {
            OperationError = null;
            await ToggleEngineCoreAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (Connection.Snapshot.Phase != ConnectionPhase.Failed)
        {
            ConnectionStatus = "Engine change cancelled.";
        }
        catch (Exception error) when (IsConnectionFailure(error))
        {
            ReportConnectionFailure("Engine change", error);
        }
    }

    private async Task ToggleEngineCoreAsync()
    {
        if (!CanChangeEngine)
        {
            return;
        }
        SubscriptionEngineKind requested = Engine == SubscriptionEngineKind.ChannelV2
            ? SubscriptionEngineKind.Classic
            : SubscriptionEngineKind.ChannelV2;
        if (Connection.CurrentSession is not null)
        {
            await Connection.ReconnectAsync(requested, m_lifetime.Token).ConfigureAwait(true);
            await SynchronizeConnectionAsync(m_lifetime.Token).ConfigureAwait(true);
            Engine = requested;
            return;
        }
        Engine = requested;
        ConnectionStatus = "Engine selected. Use Connect to confirm the connection profile and credentials.";
    }

    private void ReportConnectionFailure(string action, Exception error)
    {
        string details = error is AggregateException aggregate
            ? string.Join("; ", aggregate.Flatten().InnerExceptions.Select(inner => inner.Message))
            : error.Message;
        OperationError = $"{action} failed: {details}";
        ConnectionStatus = OperationError;
        MainViewModelLog.OperationFailed(m_log, error);
    }

    private static bool IsConnectionFailure(Exception error)
        => error is AggregateException or ServiceResultException or InvalidOperationException
            or ArgumentException or System.IO.IOException or UnauthorizedAccessException
            or TimeoutException or System.Net.Sockets.SocketException or OperationCanceledException
            or System.Security.Cryptography.CryptographicException or NotSupportedException;

    [RelayCommand]
    private async Task AddTabAsync()
    {
        SubscriptionDocumentState? inherited = SelectedSubscriptionTab is { } previous
            ? SubscriptionDocumentState.Capture(previous) with
            {
                Title = $"Monitor {Tabs.Count + 1}",
                Subscription = new SubscriptionConfig(),
                Items = []
            }
            : null;
        await OpenDocumentAsync(
            PluginKind.Subscription, subscription: inherited, cancellationToken: m_lifetime.Token).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task CloseTabAsync(IPlugin? tab)
        => tab is null ? Task.CompletedTask : Workspace.CloseAsync(tab);

    private Task<IPlugin> OpenDocumentAsync(
        PluginKind kind,
        ToolSeed? seed = null,
        SubscriptionDocumentState? subscription = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(m_disposal is not null, this);
        DocumentRestore<IPlugin> request = m_documentOperations.Create(m_pluginHost, kind, seed, subscription);
        return Workspace.OpenAsync(request.Create, request.ConfigureAsync, cancellationToken);
    }

    private void RegisterCommands()
    {
        var commands = new List<CommandDescriptor>
        {
            new(
                "connection.connect", CommandScope.Connection, "Connect or disconnect", "Ctrl+N",
                ConnectCommand, () => !Workspace.IsClosing, allowInTextInput: true),
            new(
                "connection.cancel", CommandScope.Connection, "Cancel connection", "Escape",
                new AsyncRelayCommand(async () =>
                {
                    ConnectCommand.Cancel();
                    await Connection.CancelAsync().ConfigureAwait(true);
                }),
                () => !Workspace.IsClosing
                    && (ConnectCommand.IsRunning || Connection.Snapshot.Phase == ConnectionPhase.Connecting)),
            new(
                "connection.change-engine", CommandScope.Connection, "Change subscription engine", null,
                ToggleEngineCommand, () => !Workspace.IsClosing),
            new(
                "document.close", CommandScope.Document, "Close document", "Ctrl+W",
                new AsyncRelayCommand(() => CloseTabAsync(SelectedTab)), () => SelectedTab is not null),
            new(
                "document.rename", CommandScope.Document, "Rename document", "F2",
                new RelayCommand(() =>
                {
                    if (SelectedTab is { } document)
                    {
                        document.IsRenaming = true;
                    }
                }),
                () => SelectedTab is not null),
            new(
                "document.duplicate", CommandScope.Document, "Duplicate monitor", "Ctrl+Alt+D",
                new AsyncRelayCommand(async () =>
                {
                    if (SelectedSubscriptionTab is { } document)
                    {
                        await DuplicateTabAsync(document).ConfigureAwait(true);
                    }
                }),
                () => SelectedSubscriptionTab is not null),
            new(
                "document.next", CommandScope.Document, "Next document", "Ctrl+Tab",
                new RelayCommand(() => SelectRelativeDocument(1)), () => Tabs.Count > 1),
            new(
                "document.previous", CommandScope.Document, "Previous document", "Ctrl+Shift+Tab",
                new RelayCommand(() => SelectRelativeDocument(-1)), () => Tabs.Count > 1),
            new(
                "view.cycle-inspector", CommandScope.Application, "Cycle contextual inspector", "Ctrl+Alt+I",
                new RelayCommand(CycleAttributesPanelMode))
        };
        foreach (PluginRegistration registration in PluginRegistry.All)
        {
            PluginKind kind = registration.Kind;
            commands.Add(new CommandDescriptor(
                registration.CommandId,
                CommandScope.Application,
                $"Open {registration.DisplayName}",
                registration.InputGesture,
                new AsyncRelayCommand(async () => await OpenToolAsync(kind).ConfigureAwait(true)),
                () => !Workspace.IsClosing));
        }
        Commands.Register([.. commands]);
    }

    private void SelectRelativeDocument(int direction)
    {
        if (Tabs.Count > 0)
        {
            int index = SelectedTab is { } selected ? Tabs.IndexOf(selected) : 0;
            Workspace.Activate(Tabs[(index + direction + Tabs.Count) % Tabs.Count]);
        }
    }

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        m_hasPresentedDocument |= Tabs.Count > 0;
        OnPropertyChanged(nameof(HasAnySubscriptionTab));
        Commands.Refresh();
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Workspace.ActiveDocument))
        {
            Connection.Adapter = SelectedSubscriptionTab?.Adapter;
            OnPropertyChanged(nameof(SelectedTab));
            OnPropertyChanged(nameof(ActiveDocument));
            OnPropertyChanged(nameof(SelectedSubscriptionTab));
            OnPropertyChanged(nameof(IsSubscriptionTabActive));
            OnPropertyChanged(nameof(IsCustomTabActive));
        }
        else if (e.PropertyName == nameof(Workspace.LastError) && Workspace.LastError is { } error)
        {
            ConnectionStatus = error;
        }
        Commands.Refresh();
    }

    private void OnConnectionStateChanged()
    {
        lock (m_lifecycle)
        {
            ConnectionSnapshot snapshot = Connection.Snapshot;
            if (m_disposal is null)
            {
                m_availabilityWork = PresentConnectionAvailabilityAsync(snapshot, m_availabilityWork);
            }
        }
    }

    private async Task PresentConnectionAvailabilityAsync(ConnectionSnapshot snapshot, Task previous)
    {
        await Task.Yield();
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        try
        {
            await m_dispatcher.InvokeAsync(() =>
            {
                if (snapshot == Connection.Snapshot)
                {
                    RefreshConnectionPresentation(snapshot);
                    Commands.Refresh();
                }
                return Task.CompletedTask;
            }, m_lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (m_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            MainViewModelLog.OperationFailed(m_log, error);
            throw;
        }
    }

    private Task OnConnectionChangedAsync(CancellationToken cancellationToken)
    {
        lock (m_lifecycle)
        {
            ConnectionSnapshot snapshot = Connection.Snapshot;
            if (Connection.CurrentSession is not null)
            {
                ObjectDisposedException.ThrowIf(m_disposal is not null, this);
                PublishingPipeline?.Apply(Connection.CurrentSession);
                RestoredConnectionProfile = null;
                m_workspaceProfile = snapshot.Profile;
                m_isApplyingConnectionChange = true;
                m_connectionWork = CreateConnectionDelivery(snapshot, cancellationToken);
                return CompleteConnectionChangeAsync(m_connectionWork);
            }
            if (!m_isReleasingConnection)
            {
                m_isReleasingConnection = true;
                Task cancellation = m_connectionCancellation?.CancelAsync() ?? Task.CompletedTask;
                m_disconnectWork = ReleaseConnectionDocumentsAsync(
                    snapshot, m_connectionWork, cancellation, cancellationToken);
                m_connectionWork = m_disconnectWork;
                m_queuedConnectionSnapshot = snapshot;
            }
            return m_disconnectWork;
        }
    }

    private async Task CompleteConnectionChangeAsync(Task delivery)
    {
        try
        {
            await delivery.ConfigureAwait(false);
        }
        finally
        {
            lock (m_lifecycle)
            {
                m_isApplyingConnectionChange = false;
            }
        }
    }

    private async Task ReleaseConnectionDocumentsAsync(
        ConnectionSnapshot snapshot,
        Task pending,
        Task cancellation,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            try
            {
                try
                {
                    await cancellation.ConfigureAwait(false);
                }
                finally
                {
                    await pending.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
            }
            finally
            {
                if (Workspace.IsClosing)
                {
                    await Workspace.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    try
                    {
                        await m_dispatcher.InvokeAsync(() =>
                        {
                            RefreshConnectionPresentation(snapshot);
                            Commands.Refresh();
                            return Task.CompletedTask;
                        }, cancellationToken).ConfigureAwait(false);
                        await Workspace.ReleaseConnectionAsync(
                            m_documentOperations.SynchronizeConnectionAsync,
                            cancellationToken).ConfigureAwait(false);
                        await m_dispatcher.InvokeAsync(async () =>
                        {
                            await UpdateSelectionAsync(null).ConfigureAwait(true);
                            Attributes.Clear();
                            References.Clear();
                            Connection.Adapter = null;
                            Commands.Refresh();
                        }, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception error) when (Workspace.IsClosing
                        && error is OperationCanceledException or ObjectDisposedException)
                    {
                        await Workspace.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            lock (m_lifecycle)
            {
                m_isReleasingConnection = false;
            }
        }
    }

    private Task CreateConnectionDelivery(ConnectionSnapshot snapshot, CancellationToken cancellationToken)
    {
        m_queuedConnectionSnapshot = snapshot;
        CancellationTokenSource? previousSource = m_connectionCancellation;
        Task cancellation = previousSource?.CancelAsync() ?? Task.CompletedTask;
        m_connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            m_lifetime.Token, cancellationToken);
        return DeliverConnectionSnapshotAsync(
            snapshot, Connection.CurrentSession, m_connectionWork, cancellation,
            previousSource, m_connectionCancellation.Token);
    }

    private async Task DeliverConnectionSnapshotAsync(
        ConnectionSnapshot snapshot,
        Opc.Ua.Client.ISession? expectedSession,
        Task previous,
        Task previousCancellation,
        CancellationTokenSource? previousSource,
        CancellationToken cancellationToken)
    {
        // Publish this delivery's task before callbacks can re-enter the facade.
        await Task.Yield();
        try
        {
            try
            {
                try
                {
                    await previousCancellation.ConfigureAwait(false);
                }
                finally
                {
                    await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
            }
            finally
            {
                previousSource?.Dispose();
            }
            cancellationToken.ThrowIfCancellationRequested();
            await m_dispatcher.InvokeAsync(async () =>
            {
                if (snapshot.Generation != Connection.Snapshot.Generation
                    || !ReferenceEquals(expectedSession, Connection.CurrentSession))
                {
                    return;
                }
                RefreshConnectionPresentation(Connection.Snapshot);
                await Workspace.SynchronizeConnectionAsync(cancellationToken).ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
                if (snapshot.Generation != Connection.Snapshot.Generation
                    || !ReferenceEquals(expectedSession, Connection.CurrentSession))
                {
                    return;
                }
                if (Connection.IsConnected && !m_hasPresentedDocument && Tabs.Count == 0)
                {
                    await OpenDocumentAsync(
                        PluginKind.Subscription, cancellationToken: m_lifetime.Token).ConfigureAwait(true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (Connection.CurrentSession is null)
                {
                    await UpdateSelectionAsync(null).ConfigureAwait(true);
                    Attributes.Clear();
                    References.Clear();
                }
                Connection.Adapter = SelectedSubscriptionTab?.Adapter;
                Commands.Refresh();
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            MainViewModelLog.OperationFailed(m_log, error);
            await m_dispatcher.InvokeAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested && snapshot == Connection.Snapshot)
                {
                    ConnectionStatus = $"Document connection update failed: {error.Message}";
                }
                return Task.CompletedTask;
            }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private void RefreshConnectionPresentation(ConnectionSnapshot snapshot)
    {
        IsConnected = snapshot.IsConnected && Connection.IsConnected;
        ConnectionStatus = snapshot.Phase switch
        {
            ConnectionPhase.Connecting => "Connecting…",
            ConnectionPhase.Connected => $"Connected — {snapshot.Profile?.Engine ?? Engine}",
            ConnectionPhase.Reconnecting => "Reconnecting…",
            ConnectionPhase.Failed => $"Connection failed: {snapshot.Error}",
            _ => "Disconnected"
        };
        if (IsConnected)
        {
            Engine = snapshot.Profile?.Engine ?? Connection.Engine;
        }
        EngineButtonText = $"Engine: {Engine}";
    }

    private async Task StartResourceMonitoringCoreAsync()
    {
        ResourceMonitorHost? monitor = null;
        try
        {
            monitor = await m_startResourceMonitor(m_lifetime.Token).ConfigureAwait(true);
            m_lifetime.Token.ThrowIfCancellationRequested();
            ResourceMonitor = monitor;
            monitor = null;
            OnPropertyChanged(nameof(ResourceMonitor));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ResourceStatus = $"Resource monitoring unavailable: {error.Message}";
            MainViewModelLog.ResourceMonitoringFailed(m_log, error);
            throw;
        }
        finally
        {
            if (monitor is not null)
            {
                await monitor.DisposeAsync().ConfigureAwait(true);
            }
        }
    }

    private void PumpResources()
    {
        if (ResourceMonitor is not { } monitor)
        {
            return;
        }
        try
        {
            ResourceStatus = monitor.Sample();
            OnPropertyChanged(nameof(ResourceSample));
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception
            or NotSupportedException or AggregateException)
        {
            m_resourcePump.Stop();
            ResourceStatus = $"Resource monitoring failed: {error.Message}";
            OnPropertyChanged(nameof(ResourceSample));
            MainViewModelLog.ResourceMonitoringFailed(m_log, error);
        }
    }

    private async Task UpdateSelectionCoreAsync(
        NodeViewModel? node,
        Task previous,
        Task previousCancellation,
        CancellationTokenSource? previousSource,
        CancellationToken cancellationToken)
    {
        try
        {
            try
            {
                await previousCancellation.ConfigureAwait(true);
            }
            finally
            {
                await previous.ConfigureAwait(
                    ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
            }
        }
        finally
        {
            previousSource?.Dispose();
        }
        if (cancellationToken.IsCancellationRequested || !ReferenceEquals(node, m_selectedNode))
        {
            return;
        }
        CanAddSelectedItem = false;
        SelectedItemIsEvent = false;
        SelectionHasEvents = false;
        SelectionHasVariables = false;
        CanCallMethod = false;
        CanWriteVariable = false;
        SelectionVariables = [];
        SelectedItemStatus = "Pick a Variable, or an Object that emits events.";
        if (node is null)
        {
            return;
        }

        if (node.NodeClass == NodeClass.Variable)
        {
            CanAddSelectedItem = true;
            SelectedItemStatus = "Variable · probing AccessLevel…";
            try
            {
                if (Connection.Session is { } session)
                {
                    ReadResponse response = await session.ReadAsync(
                        null, 0, TimestampsToReturn.Neither,
                        [new ReadValueId { NodeId = node.NodeId, AttributeId = Opc.Ua.Attributes.AccessLevel }],
                        cancellationToken).ConfigureAwait(true);
                    if (cancellationToken.IsCancellationRequested || !ReferenceEquals(node, m_selectedNode))
                    {
                        return;
                    }
                    if (response.Results.Count > 0
                        && !StatusCode.IsBad(response.Results[0].StatusCode)
                        && response.Results[0].WrappedValue.TryGetValue(out byte access))
                    {
                        CanWriteVariable = (access & AccessLevels.CurrentWrite) != 0;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ServiceResultException error)
            {
                MainViewModelLog.SelectionReadFailed(m_log, node.NodeId, error);
            }
            if (!cancellationToken.IsCancellationRequested && ReferenceEquals(node, m_selectedNode))
            {
                SelectedItemStatus = CanWriteVariable
                    ? $"Variable (writable) · {node.NodeId}"
                    : $"Variable (read-only) · {node.NodeId}";
            }
            return;
        }

        if (node.NodeClass == NodeClass.Method)
        {
            CanCallMethod = true;
            SelectedItemStatus = $"Method · {node.NodeId}";
            return;
        }

        if (node.NodeClass == NodeClass.Object)
        {
            SelectedItemStatus = $"Probing {node.NodeId}…";
            try
            {
                Task<byte?> notifierTask = Browser.GetEventNotifierAsync(node.NodeId, cancellationToken);
                Task<IReadOnlyList<(NodeId, string)>> variablesTask =
                    Browser.GetChildVariablesAsync(node.NodeId, cancellationToken);
                await Task.WhenAll(notifierTask, variablesTask).ConfigureAwait(true);
                byte? notifier = await notifierTask.ConfigureAwait(true);
                IReadOnlyList<(NodeId, string)> variables = await variablesTask.ConfigureAwait(true);
                if (cancellationToken.IsCancellationRequested || !ReferenceEquals(node, m_selectedNode))
                {
                    return;
                }
                SelectionHasEvents = notifier is { } bits && (bits & EventNotifiers.SubscribeToEvents) != 0;
                SelectionHasVariables = variables.Count > 0;
                SelectionVariables = [.. variables];
                SelectedItemIsEvent = SelectionHasEvents && !SelectionHasVariables;
                CanAddSelectedItem = SelectionHasEvents || SelectionHasVariables;
                string events = SelectionHasEvents ? "events" : "no events";
                SelectedItemStatus = $"{node.NodeId} · {events} · {variables.Count} child variables";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            return;
        }

        SelectedItemStatus = $"{node.NodeClass} nodes cannot be subscribed.";
    }

    private void PumpLog()
    {
        long total = m_logBuffer.TotalWritten;
        if (total == m_lastLogIndex)
        {
            return;
        }
        List<LogEntry> snapshot = m_logBuffer.SnapshotList();
        long start = Math.Max(m_lastLogIndex, total - snapshot.Count);
        int skip = (int)(start - (total - snapshot.Count));
        for (int i = skip; i < snapshot.Count; i++)
        {
            LogEntry entry = snapshot[i];
            LogLines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0:HH:mm:ss.fff} {1,-5} {2}: {3}",
                entry.TimestampUtc.ToLocalTime(), LevelTag(entry.Level), entry.Category, entry.Message));
            while (LogLines.Count > 1000)
            {
                LogLines.RemoveAt(0);
            }
        }
        m_lastLogIndex = total;
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "err ",
        LogLevel.Critical => "crit",
        _ => string.Empty
    };

    private async Task DisposeCoreAsync()
    {
        Connection.StateChanged -= OnConnectionStateChanged;
        Workspace.PropertyChanged -= OnWorkspacePropertyChanged;
        Tabs.CollectionChanged -= OnDocumentsChanged;
        m_logPump.Stop();
        m_resourcePump.Stop();
        ConnectCommand.Cancel();
        try
        {
            try
            {
                await m_lifetime.CancelAsync().ConfigureAwait(true);
            }
            finally
            {
                try
                {
                    await m_selectionWork.ConfigureAwait(
                        ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
                }
                finally
                {
                    try
                    {
                        await m_availabilityWork.ConfigureAwait(
                            ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
                    }
                    finally
                    {
                        await m_connectionWork.ConfigureAwait(
                            ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
                    }
                }
            }
        }
        finally
        {
            try
            {
                await Workspace.DisposeAsync().ConfigureAwait(true);
            }
            finally
            {
                try
                {
                    Connection.ConnectionChangedAsync -= OnConnectionChangedAsync;
                    await Connection.DisposeAsync().ConfigureAwait(true);
                }
                finally
                {
                    try
                    {
                        if (m_resourceStartup is { } startup)
                        {
                            // Startup errors have already surfaced to the desktop adapter.
                            await startup.ConfigureAwait(
                                ConfigureAwaitOptions.ContinueOnCapturedContext
                                    | ConfigureAwaitOptions.SuppressThrowing);
                        }
                        if (ResourceMonitor is { } monitor)
                        {
                            ResourceMonitor = null;
                            await monitor.DisposeAsync().ConfigureAwait(true);
                        }
                    }
                    finally
                    {
                        Attributes.Dispose();
                        References.Dispose();
                    }
                }
            }
        }
    }

    partial void OnAttributesPanelModeChanged(SidePanelMode value)
    {
        OnPropertyChanged(nameof(ShowAttributes));
        OnPropertyChanged(nameof(ShowReferences));
    }

    partial void OnEngineChanged(SubscriptionEngineKind value)
    {
        EngineButtonText = $"Engine: {value}";
        OnPropertyChanged(nameof(UseChannelV2Engine));
    }

    private readonly LogRingBuffer m_logBuffer;
    private readonly ILogger m_log;
    private readonly IWorkspaceDispatcher m_dispatcher;
    private readonly Func<CancellationToken, Task<ResourceMonitorHost>> m_startResourceMonitor;
    private readonly PluginDocumentOperations m_documentOperations;
    private readonly PluginHost m_pluginHost;
    private readonly DispatcherTimer m_logPump;
    private readonly DispatcherTimer m_resourcePump;
    private readonly CancellationTokenSource m_lifetime = new();
    private readonly System.Threading.Lock m_lifecycle = new();
    private long m_lastLogIndex;
    private bool m_hasPresentedDocument;
    private bool m_isReleasingConnection;
    private bool m_isApplyingConnectionChange;
    private NodeViewModel? m_selectedNode;
    private CancellationTokenSource? m_selectionCancellation;
    private CancellationTokenSource? m_connectionCancellation;
    private ConnectionSnapshot? m_queuedConnectionSnapshot;
    private ConnectionProfile? m_workspaceProfile;
    private string? m_operationError;
    private Task m_selectionWork = Task.CompletedTask;
    private Task m_connectionWork = Task.CompletedTask;
    private Task m_availabilityWork = Task.CompletedTask;
    private Task m_disconnectWork = Task.CompletedTask;
    private Task? m_resourceStartup;
    private Task? m_disposal;

    [ObservableProperty]
    private string m_endpointUrl = "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";

    [ObservableProperty]
    private SubscriptionEngineKind m_engine = SubscriptionEngineKind.ChannelV2;

    [ObservableProperty]
    private string m_connectionStatus = "Disconnected";

    [ObservableProperty]
    private bool m_isConnected;

    [ObservableProperty]
    private bool m_isAddressSpaceVisible = true;

    [ObservableProperty]
    private string m_engineButtonText = "Engine: ChannelV2";

    [ObservableProperty]
    private SidePanelMode m_attributesPanelMode = SidePanelMode.None;

    [ObservableProperty]
    private RegisteredApplicationContext? m_currentRegisteredApp;

    [ObservableProperty]
    private bool m_canAddSelectedItem;

    [ObservableProperty]
    private bool m_selectedItemIsEvent;

    [ObservableProperty]
    private string m_selectedItemStatus = "Pick a Variable, or an Object that emits events.";

    [ObservableProperty]
    private bool m_selectionHasEvents;

    [ObservableProperty]
    private bool m_selectionHasVariables;

    [ObservableProperty]
    private bool m_canCallMethod;

    [ObservableProperty]
    private bool m_canWriteVariable;

    [ObservableProperty]
    private string m_resourceStatus = "CPU --   Mem --";
}

internal static partial class MainViewModelLog
{
    [LoggerMessage(EventId = UaLensEventIds.MainStarted, Level = LogLevel.Information, Message = "UaLens started.")]
    public static partial void Started(ILogger logger);

    [LoggerMessage(
        EventId = UaLensEventIds.MainSelectionReadFailed,
        Level = LogLevel.Debug,
        Message = "AccessLevel read failed for {NodeId}.")]
    public static partial void SelectionReadFailed(ILogger logger, NodeId nodeId, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.MainLegacyWorkspaceRestored,
        Level = LogLevel.Information,
        Message = "Imported {Count} legacy documents offline; explicit connection selection is required.")]
    public static partial void LegacyWorkspaceRestored(ILogger logger, int count);

    [LoggerMessage(
        EventId = UaLensEventIds.MainResourceMonitoringFailed,
        Level = LogLevel.Error,
        Message = "Resource monitoring failed.")]
    public static partial void ResourceMonitoringFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.MainOperationFailed,
        Level = LogLevel.Error,
        Message = "Document connection update failed.")]
    public static partial void OperationFailed(ILogger logger, Exception exception);
}
