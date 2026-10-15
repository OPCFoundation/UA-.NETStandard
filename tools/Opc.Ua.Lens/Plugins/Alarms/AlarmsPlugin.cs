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
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opc.Ua;
using UaLens.Storage;
using UaLens.ViewModels;

namespace UaLens.Plugins.Alarms;

/// <summary>
/// Condition-aware Observe document. Construction/restore creates no controls or
/// listeners; observation and every operator mutation require explicit intent.
/// </summary>
internal sealed partial class AlarmsPlugin : ObservableObject, IPlugin, IWorkspaceState
{
    public AlarmsPlugin(PluginHost host, IAlarmBackend? backend = null)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_workspace = new AlarmWorkspace(backend ?? new StackAlarmBackend(host), host.Telemetry);
        m_title = string.Create(CultureInfo.InvariantCulture, $"Alarms {Interlocked.Increment(ref s_number)}");
        if (host.Workspace.SelectedNode is { NodeClass: NodeClass.Object or NodeClass.View } node &&
            !node.NodeId.IsNull)
        {
            ExpandedNodeId source = host.Session is { } session
                ? NodeId.ToExpandedNodeId(node.NodeId, session.NamespaceUris)
                : new ExpandedNodeId(node.NodeId);
            string sourceId = source.ToString();
            if (sourceId.Length <= AlarmLimits.IdentifierLength &&
                (node.NodeId.NamespaceIndex == 0 || !string.IsNullOrEmpty(source.NamespaceUri)))
            {
                string sourceName = node.Text.Length <= 256 ? node.Text : node.Text[..256];
                m_source = new AlarmSource(sourceId, sourceName);
                SourceId = sourceId;
                SourceName = sourceName;
                ActionStatus = "Address-space source selected. Select Observe to verify event support and create " +
                    "the subscription; nothing has started.";
            }
        }
    }

    public PluginKind Kind => PluginKind.Alarms;

    public bool SupportsDuplicate => false;

    public bool HasSelection => SelectedCondition is not null;

    public bool CanObserve => !IsBusy && !IsOffline && !m_closed;

    public bool CanConfigure => !IsBusy && !m_closed;

    public bool CanRefresh => !IsBusy && !IsOffline && IsObserving && !m_closed;

    public bool CanOperate => CanRefresh && SelectedCondition is { IsStale: false };

    public bool CanExecuteAdvanced => CanOperate && SelectedOperation is { CanInvoke: true };

    public bool HasDialogResponses => SelectedCondition?.Condition.Responses.Count > 0;

    public List<string> DialogResponses => SelectedCondition?.Condition.Responses.ToList() ?? [];

    public string SelectedOperationReason => SelectedOperation?.Reason ??
        "Check operations to inspect instance methods and permission evidence. Unknown is not supported.";

    Control? IPlugin.View
    {
        get
        {
            if (m_closed)
            {
                return null;
            }
            if (m_view is null)
            {
                m_view = new AlarmsView { DataContext = this };
                m_renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                m_renderTimer.Tick += OnRenderTick;
                m_renderTimer.Start();
            }
            return m_view;
        }
    }

    Control? IPlugin.HeaderToolbar => null;

    public void OnActivated()
    {
        if (m_closed)
        {
            return;
        }
        RenderSnapshot();
        m_renderTimer?.Start();
    }

    public void OnDeactivated()
    {
        m_renderTimer?.Stop();
    }

    public IReadOnlyList<MenuItem> ContributeMenuItems()
    {
        return [];
    }

    public async Task OnConnectionStateChangedAsync(CancellationToken cancellationToken)
    {
        if (m_closed)
        {
            return;
        }
        IsOffline = !m_host.Connection.IsConnected;
        Operations = [];
        SelectedOperation = null;
        if (m_host.Session is null)
        {
            await m_workspace.StopAsync().ConfigureAwait(true);
        }
        else if (m_observationRequested && !IsOffline)
        {
            await PresentAsync("Reattach alarms", () => m_workspace.StartAsync(
                m_source, m_appliedInterval, m_host.Connection.Snapshot.Generation, cancellationToken))
                .ConfigureAwait(true);
        }
        RenderSnapshot();
    }

    /// <summary>
    /// Seeds a selected address-space source without starting observation. A live
    /// namespace table is required to persist a nonzero namespace index portably.
    /// </summary>
    public async Task SeedSourceAsync(NodeId nodeId, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string id = nodeId.ToString();
        if (m_host.Session is { } session)
        {
            id = NodeId.ToExpandedNodeId(nodeId, session.NamespaceUris).ToString();
        }
        var source = new AlarmSource(id, name);
        AlarmsStateCodec.Validate(new AlarmsDocumentState(
            AlarmsStateCodec.CurrentVersion, Title, id, name, PublishingInterval, RetainedOnly));
        m_observationRequested = false;
        await m_workspace.ResetAsync().ConfigureAwait(true);
        m_source = source;
        SourceId = id;
        SourceName = name;
        Operations = [];
        SelectedCondition = null;
        ActionStatus = "Source selected. Select Observe to create an event subscription; no operator action was sent.";
        RenderSnapshot();
    }

    public JsonElement CaptureState()
    {
        AlarmSource source = NormalizeSource();
        return AlarmsStateCodec.Capture(new AlarmsDocumentState(
            AlarmsStateCodec.CurrentVersion,
            Title,
            source.TargetId,
            source.Name,
            PublishingInterval,
            RetainedOnly));
    }

    public async Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
    {
        AlarmsDocumentState restored = AlarmsStateCodec.Restore(state);
        cancellationToken.ThrowIfCancellationRequested();
        m_observationRequested = false;
        await m_workspace.ResetAsync().ConfigureAwait(true);
        Title = restored.Title;
        SourceId = restored.SourceId;
        SourceName = restored.SourceName;
        PublishingInterval = restored.PublishingInterval;
        RetainedOnly = restored.RetainedOnly;
        m_source = new AlarmSource(SourceId, SourceName);
        m_appliedInterval = TimeSpan.FromMilliseconds(PublishingInterval);
        Comment = string.Empty;
        ShelvingMilliseconds = 60000;
        ResponseIndex = 0;
        SelectedCondition = null;
        Operations = [];
        SelectedOperation = null;
        Conditions = [];
        History = [];
        ActionStatus = "Configuration restored offline. Select Observe; no listener or operator command was restored.";
        RenderSnapshot();
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(m_disposal ??= DisposeCoreAsync());
    }

    partial void OnSelectedConditionChanged(AlarmRow? value)
    {
        if (m_rendering)
        {
            return;
        }
        UpdateSelection(value);
    }

    partial void OnRetainedOnlyChanged(bool value)
    {
        m_renderedRevision = -1;
        RenderSnapshot();
    }

    [RelayCommand(CanExecute = nameof(CanObserve))]
    private Task ObserveAsync(CancellationToken cancellationToken)
    {
        return PresentAsync("Observe", async () =>
        {
            AlarmSource source = NormalizeSource();
            AlarmsStateCodec.Validate(new AlarmsDocumentState(
                AlarmsStateCodec.CurrentVersion, Title, source.TargetId, source.Name,
                PublishingInterval, RetainedOnly));
            m_source = source;
            SourceId = source.TargetId;
            m_appliedInterval = TimeSpan.FromMilliseconds(PublishingInterval);
            m_observationRequested = true;
            await m_workspace.StartAsync(
                source, m_appliedInterval, m_host.Connection.Snapshot.Generation, cancellationToken)
                .ConfigureAwait(true);
            ActionStatus = "Observing. Acknowledge, confirm, comment and advanced commands are always explicit.";
        });
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        m_observationRequested = false;
        ObserveCommand.Cancel();
        RefreshCommand.Cancel();
        CheckOperationsCommand.Cancel();
        AcknowledgeCommand.Cancel();
        ConfirmCommand.Cancel();
        AddCommentCommand.Cancel();
        ExecuteAdvancedCommand.Cancel();
        await PresentAsync("Stop", async () =>
        {
            await m_workspace.StopAsync().ConfigureAwait(true);
            ActionStatus = "Observation stopped. Previous branches are stale; no command will be replayed.";
        }).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync(CancellationToken cancellationToken)
    {
        return PresentAsync("ConditionRefresh", async () =>
        {
            await m_workspace.RefreshAsync(cancellationToken).ConfigureAwait(true);
            ActionStatus = "ConditionRefresh processed. Only its start/end marker status above indicates completeness.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanObserve))]
    private Task UseSelectedSourceAsync(CancellationToken cancellationToken)
    {
        return PresentAsync("Select source", async () =>
        {
            NodeViewModel? node = m_host.Workspace.SelectedNode;
            if (node is null || node.NodeClass is not (NodeClass.Object or NodeClass.View))
            {
                throw new InvalidOperationException(
                    "Select an event-emitting Object or View in the address-space panel.");
            }
            await SeedSourceAsync(node.NodeId, node.Text, cancellationToken).ConfigureAwait(true);
        });
    }

    [RelayCommand(CanExecute = nameof(CanConfigure))]
    private Task UseServerSourceAsync(CancellationToken cancellationToken)
    {
        return PresentAsync("Select Server", () => SeedSourceAsync(ObjectIds.Server, "Server", cancellationToken));
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task CheckOperationsAsync(CancellationToken cancellationToken)
    {
        return PresentAsync("Check operations", async () =>
        {
            AlarmRow selected = SelectedCondition ?? throw new InvalidOperationException("Select a condition branch.");
            ArrayOf<AlarmOperation> operations = await m_workspace
                .InspectAsync(selected.Condition.Key, cancellationToken).ConfigureAwait(true);
            if (SelectedCondition is not { } current ||
                current.Condition.Key != selected.Condition.Key ||
                current.Condition.EventId != selected.Condition.EventId)
            {
                return;
            }
            Operations = operations.ToList().FindAll(static operation =>
                operation.Kind is not (AlarmOperationKind.Acknowledge or
                    AlarmOperationKind.Confirm or AlarmOperationKind.AddComment));
            SelectedOperation = Operations.Find(static operation => operation.CanInvoke)
                ?? (Operations.Count == 0 ? null : Operations[0]);
            ActionStatus = "Availability checked for this selection. Permissions are rechecked on every command.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task AcknowledgeAsync(CancellationToken cancellationToken)
    {
        return ExecuteOperatorAsync(AlarmOperationKind.Acknowledge, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task ConfirmAsync(CancellationToken cancellationToken)
    {
        return ExecuteOperatorAsync(AlarmOperationKind.Confirm, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task AddCommentAsync(CancellationToken cancellationToken)
    {
        return ExecuteOperatorAsync(AlarmOperationKind.AddComment, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAdvanced))]
    private Task ExecuteAdvancedAsync(CancellationToken cancellationToken)
    {
        return SelectedOperation is { } operation
            ? ExecuteOperatorAsync(operation.Kind, cancellationToken)
            : Task.CompletedTask;
    }

    private Task ExecuteOperatorAsync(AlarmOperationKind operation, CancellationToken cancellationToken)
    {
        return PresentAsync(operation.ToString(), async () =>
        {
            AlarmRow selected = SelectedCondition ?? throw new InvalidOperationException("Select a condition branch.");
            AlarmCommandResult result = await m_workspace.ExecuteAsync(
                selected.Condition.Key, operation, Comment, ShelvingMilliseconds, ResponseIndex,
                selected.Condition.EventId, cancellationToken)
                .ConfigureAwait(true);
            ActionStatus = result.Detail;
        });
    }

    private AlarmSource NormalizeSource()
    {
        if (string.IsNullOrWhiteSpace(SourceId) || SourceId.Length > AlarmLimits.IdentifierLength ||
            !ExpandedNodeId.TryParse(SourceId, out ExpandedNodeId source) || source.IsNull || source.ServerIndex != 0)
        {
            throw new ArgumentException("Enter a valid local event source NodeId or namespace-URI ExpandedNodeId.");
        }
        if (source.NamespaceIndex != 0 && string.IsNullOrEmpty(source.NamespaceUri) && m_host.Session is { } session)
        {
            NodeId local = ExpandedNodeId.ToNodeId(source, session.NamespaceUris);
            source = NodeId.ToExpandedNodeId(local, session.NamespaceUris);
        }
        return new AlarmSource(source.ToString(), SourceName);
    }

    private async Task PresentAsync(string action, Func<Task> operation)
    {
        m_presentCount++;
        IsBusy = true;
        ActionStatus = action + "…";
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            ActionStatus = action + " canceled. Any server-side command outcome may be unknown; nothing was replayed.";
        }
        catch (ServiceResultException exception)
        {
            m_host.Log.AlarmActionFailed(exception, action);
            ActionStatus = AlarmCommandResult.FromServiceResult(exception).Detail;
        }
        catch (NotSupportedException exception)
        {
            m_host.Log.AlarmActionFailed(exception, action);
            ActionStatus = "Unsupported: " + AlarmLimits.Text(exception.Message);
        }
        catch (TimeoutException exception)
        {
            m_host.Log.AlarmActionFailed(exception, action);
            ActionStatus = "Timed out: " + AlarmLimits.Text(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            m_host.Log.AlarmActionFailed(exception, action);
            ActionStatus = "Unavailable: " + AlarmLimits.Text(exception.Message);
        }
        catch (ArgumentException exception)
        {
            ActionStatus = AlarmLimits.Text(exception.Message);
        }
        catch (JsonException exception)
        {
            ActionStatus = AlarmLimits.Text(exception.Message);
        }
        finally
        {
            IsBusy = --m_presentCount != 0;
            if (!m_closed)
            {
                RenderSnapshot();
            }
        }
    }

    private void OnRenderTick(object? sender, EventArgs args)
    {
        RenderSnapshot();
    }

    private void RenderSnapshot()
    {
        if (m_closed)
        {
            return;
        }
        AlarmSnapshot snapshot = m_workspace.Snapshot();
        if (m_renderedEpoch != snapshot.ObservationEpoch)
        {
            m_renderedEpoch = snapshot.ObservationEpoch;
            Operations = [];
            SelectedOperation = null;
        }
        IsOffline = !m_host.Connection.IsConnected;
        IsObserving = snapshot.IsObserving;
        RefreshStatus = $"{snapshot.RefreshState}: {snapshot.RefreshDetail}";
        AlarmStreamHealth? health = m_workspace.Health;
        SubscriptionStatus = health is null
            ? "Offline — no alarm subscription."
            : string.Create(CultureInfo.InvariantCulture,
                $"{health.Detail} Revised interval {health.PublishingInterval.TotalMilliseconds:0.##} ms · " +
                $"missing {health.MissingMessages} · republish attempts {health.RepublishRequests}");
        Status = string.Create(CultureInfo.InvariantCulture,
            $"{snapshot.Conditions.Count}/{AlarmLimits.Conditions} branches · {snapshot.ReceivedEvents} events · " +
            $"{snapshot.DroppedUpdates} loss/gap/invalid signals · {snapshot.EvictedConditions} evictions · " +
            $"history bounded to {AlarmLimits.History}");
        if (m_renderedRevision == snapshot.Revision)
        {
            return;
        }
        m_renderedRevision = snapshot.Revision;
        AlarmRow? selected = SelectedCondition;
        m_rendering = true;
        try
        {
            Conditions = RetainedOnly
                ? snapshot.Conditions.ToList().FindAll(static row => row.Condition.Retain)
                : snapshot.Conditions.ToList();
            History = snapshot.History.ToList();
            SelectedCondition = selected is null
                ? null
                : Conditions.Find(row => row.Condition.Key == selected.Condition.Key);
        }
        finally
        {
            m_rendering = false;
        }
        UpdateSelection(SelectedCondition);
    }

    private void UpdateSelection(AlarmRow? row)
    {
        if (row is null || row.Condition.Key != m_selectedKey || row.Condition.EventId != m_selectedEvent)
        {
            Operations = [];
            SelectedOperation = null;
            ResponseIndex = 0;
        }
        m_selectedKey = row?.Condition.Key ?? default;
        m_selectedEvent = row?.Condition.EventId ?? default;
    }

    private async Task DisposeCoreAsync()
    {
        m_closed = true;
        m_observationRequested = false;
        if (m_renderTimer is not null)
        {
            m_renderTimer.Stop();
            m_renderTimer.Tick -= OnRenderTick;
        }
        await m_workspace.DisposeAsync().ConfigureAwait(true);
    }

    private static int s_number;
    private readonly PluginHost m_host;
    private readonly AlarmWorkspace m_workspace;
    private AlarmSource m_source = new("i=2253", "Server");
    private TimeSpan m_appliedInterval = TimeSpan.FromMilliseconds(250);
    private AlarmsView? m_view;
    private DispatcherTimer? m_renderTimer;
    private Task? m_disposal;
    private bool m_closed;
    private bool m_observationRequested;
    private bool m_rendering;
    private long m_renderedRevision = -1;
    private long m_renderedEpoch = -1;
    private int m_presentCount;
    private AlarmKey m_selectedKey;
    private ByteString m_selectedEvent;

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private string m_sourceId = "i=2253";

    [ObservableProperty]
    private string m_sourceName = "Server";

    [ObservableProperty]
    private double m_publishingInterval = 250;

    [ObservableProperty]
    private bool m_retainedOnly = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanObserve), nameof(CanConfigure), nameof(CanRefresh),
        nameof(CanOperate), nameof(CanExecuteAdvanced))]
    [NotifyCanExecuteChangedFor(nameof(ObserveCommand), nameof(UseServerSourceCommand),
        nameof(UseSelectedSourceCommand), nameof(RefreshCommand),
        nameof(CheckOperationsCommand), nameof(AcknowledgeCommand), nameof(ConfirmCommand),
        nameof(AddCommentCommand), nameof(ExecuteAdvancedCommand))]
    private bool m_isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanObserve), nameof(CanRefresh), nameof(CanOperate), nameof(CanExecuteAdvanced))]
    [NotifyCanExecuteChangedFor(nameof(ObserveCommand), nameof(UseSelectedSourceCommand), nameof(RefreshCommand),
        nameof(CheckOperationsCommand), nameof(AcknowledgeCommand), nameof(ConfirmCommand),
        nameof(AddCommentCommand), nameof(ExecuteAdvancedCommand))]
    private bool m_isOffline = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefresh), nameof(CanOperate), nameof(CanExecuteAdvanced))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(CheckOperationsCommand), nameof(AcknowledgeCommand),
        nameof(ConfirmCommand), nameof(AddCommentCommand), nameof(ExecuteAdvancedCommand))]
    private bool m_isObserving;

    [ObservableProperty]
    private List<AlarmRow> m_conditions = [];

    [ObservableProperty]
    private List<AlarmHistoryEntry> m_history = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanOperate), nameof(CanExecuteAdvanced),
        nameof(HasDialogResponses), nameof(DialogResponses))]
    [NotifyCanExecuteChangedFor(nameof(CheckOperationsCommand), nameof(AcknowledgeCommand), nameof(ConfirmCommand),
        nameof(AddCommentCommand), nameof(ExecuteAdvancedCommand))]
    private AlarmRow? m_selectedCondition;

    [ObservableProperty]
    private List<AlarmOperation> m_operations = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecuteAdvanced), nameof(SelectedOperationReason))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteAdvancedCommand))]
    private AlarmOperation? m_selectedOperation;

    [ObservableProperty]
    private string m_comment = string.Empty;

    [ObservableProperty]
    private double m_shelvingMilliseconds = 60000;

    [ObservableProperty]
    private int m_responseIndex;

    [ObservableProperty]
    private string m_actionStatus = "Select an event source and Observe. Operator commands are never automatic.";

    [ObservableProperty]
    private string m_refreshStatus = "No complete retained-condition refresh has been received.";

    [ObservableProperty]
    private string m_subscriptionStatus = "Offline — no alarm subscription.";

    [ObservableProperty]
    private string m_status = "No alarm observation started.";
}
