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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using UaLens.Storage;
using UaLens.ViewModels;

namespace UaLens.Plugins.Companions;

/// <summary>
/// Guided companion tasks in one ordinary workspace document. Discovery and
/// execution are explicit, and sample-operation arming is never persisted.
/// </summary>
internal sealed partial class CompanionPlugin : ObservableObject, IPlugin, IWorkspaceState
{
    public CompanionPlugin(
        PluginHost host,
        CompanionWorkspace? workspace = null,
        ArrayOf<ICompanionProvider> providers = default)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_workspace = workspace ?? new CompanionWorkspace(providers, host.Telemetry);
        Providers = [.. m_workspace.Providers];
        m_title = string.Create(CultureInfo.InvariantCulture, $"Companions {Interlocked.Increment(ref s_number)}");
        m_selectedProvider = Providers.FirstOrDefault();
    }

    public PluginKind Kind => PluginKind.Companions;

    public bool SupportsDuplicate => false;

    public ObservableCollection<CompanionDescriptor> Providers { get; }

    public ObservableCollection<CompanionTarget> Targets { get; } = [];

    public ObservableCollection<CompanionValue> Values { get; } = [];

    public ObservableCollection<CompanionOperation> Operations { get; } = [];

    Control? IPlugin.View => m_view ??= new CompanionView { DataContext = this };

    Control? IPlugin.HeaderToolbar => null;

    public void OnActivated()
    {
    }

    public void OnDeactivated()
    {
    }

    public IReadOnlyList<MenuItem> ContributeMenuItems()
    {
        return [];
    }

    public async Task OnConnectionStateChangedAsync(CancellationToken cancellationToken)
    {
        m_selectionVersion++;
        ConfirmLocalSample = false;
        await m_workspace.BindAsync(m_host.Session, cancellationToken).ConfigureAwait(true);
        IsOffline = m_host.Session is null;
        ClearResults();
        Status = IsOffline
            ? "Connect the primary server, then discover supported companion instances."
            : "Connected. Discovery is read-only; choose a model and select Discover.";
    }

    public JsonElement CaptureState()
    {
        string? targetId = SelectedTarget is { } target && m_host.Session is { } session
            ? NodeId.ToExpandedNodeId(target.NodeId, session.NamespaceUris).ToString()
            : m_restoredTarget;
        return JsonSerializer.SerializeToElement(
            new CompanionDocumentState(1, SelectedProvider?.Id, targetId),
            CompanionJsonContext.Default.CompanionDocumentState);
    }

    public async Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
    {
        CompanionDocumentState saved = state.Deserialize(CompanionJsonContext.Default.CompanionDocumentState)
            ?? throw new JsonException("The companion document configuration is missing.");
        if (saved.Version != 1 || saved.TargetId?.Length > 4096)
        {
            throw new JsonException("The companion document configuration is not supported.");
        }
        CompanionDescriptor? provider = saved.ProviderId is null
            ? Providers.FirstOrDefault()
            : Providers.FirstOrDefault(item => item.Id == saved.ProviderId);
        if (saved.ProviderId is not null && provider is null)
        {
            throw new JsonException("The workspace requires a companion provider that is not registered.");
        }
        if (!string.IsNullOrEmpty(saved.TargetId) && !ExpandedNodeId.TryParse(saved.TargetId, out _))
        {
            throw new JsonException("The saved companion target is not a valid portable node identifier.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        await m_workspace.CancelAsync().ConfigureAwait(true);
        SelectedProvider = provider;
        m_restoredTarget = saved.TargetId;
        ConfirmLocalSample = false;
        OperationInput = string.Empty;
        ClearResults();
        Status = "Configuration restored. Select Discover; no task or workload has been started.";
    }

    public ValueTask DisposeAsync()
    {
        return m_workspace.DisposeAsync();
    }

    partial void OnSelectedProviderChanged(CompanionDescriptor? value)
    {
        m_selectionVersion++;
        m_restoredTarget = null;
        ClearResults();
    }

    partial void OnSelectedTargetChanged(CompanionTarget? value)
    {
        m_selectionVersion++;
        if (value is not null)
        {
            m_restoredTarget = null;
        }
        Values.Clear();
        Operations.Clear();
        SelectedOperation = null;
        OperationInput = string.Empty;
    }

    private bool CanDiscover()
    {
        return !IsBusy && !IsOffline && SelectedProvider is not null;
    }

    private bool CanInspect()
    {
        return !IsBusy && !IsOffline && SelectedTarget is not null;
    }

    private bool CanRunTask()
    {
        return CanInspect() && SelectedOperation is not null;
    }

    [RelayCommand(CanExecute = nameof(CanDiscover))]
    private Task DiscoverAsync(CancellationToken cancellationToken)
    {
        return PresentOperationAsync("Discover", async () =>
        {
            CompanionDescriptor provider = SelectedProvider ??
                throw new InvalidOperationException("Select a companion model first.");
            int version = m_selectionVersion;
            ArrayOf<CompanionTarget> targets = await m_workspace.DiscoverAsync(provider.Id, cancellationToken)
                .ConfigureAwait(true);
            if (version != m_selectionVersion)
            {
                return;
            }
            Targets.Clear();
            foreach (CompanionTarget target in targets)
            {
                Targets.Add(target);
            }
            CompanionTarget? selected = targets.Count == 0 ? null : targets[0];
            bool restoredMissing = false;
            if (m_host.Session is { } session && m_restoredTarget is { } restored)
            {
                selected = null;
                for (int index = 0; index < targets.Count; index++)
                {
                    if (NodeId.ToExpandedNodeId(targets[index].NodeId, session.NamespaceUris).ToString() == restored)
                    {
                        selected = targets[index];
                        break;
                    }
                }
                restoredMissing = selected is null;
            }
            SelectedTarget = selected;
            Status = restoredMissing
                ? "The saved target was not found in this bounded discovery. Select another instance or refresh later."
                : targets.Count >= 128
                ? "Showing the first 128 instances. Additional instances may exist."
                : string.Create(CultureInfo.InvariantCulture, $"Found {targets.Count} typed instances.");
        });
    }

    [RelayCommand(CanExecute = nameof(CanInspect))]
    private Task InspectAsync(CancellationToken cancellationToken)
    {
        return PresentOperationAsync("Inspect", async () =>
        {
            CompanionTarget target = SelectedTarget ?? throw new InvalidOperationException("Select an instance.");
            int version = m_selectionVersion;
            CompanionInspection inspection = await m_workspace.InspectAsync(target, cancellationToken)
                .ConfigureAwait(true);
            if (version != m_selectionVersion)
            {
                return;
            }
            SetValues(inspection.Values);
            Operations.Clear();
            foreach (CompanionOperation operation in inspection.Operations)
            {
                Operations.Add(operation);
            }
            SelectedOperation = Operations.FirstOrDefault();
            Status = inspection.Summary;
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunTask))]
    private Task RunTaskAsync(CancellationToken cancellationToken)
    {
        return PresentOperationAsync("Run task", async () =>
        {
            CompanionTarget target = SelectedTarget ?? throw new InvalidOperationException("Select an instance.");
            CompanionOperation operation = SelectedOperation ??
                throw new InvalidOperationException("Inspect the instance and select an operation.");
            int version = m_selectionVersion;
            CompanionOperationResult result = await m_workspace.ExecuteAsync(
                target, operation.Id, OperationInput, ConfirmLocalSample, cancellationToken).ConfigureAwait(true);
            if (version == m_selectionVersion)
            {
                SetValues(result.Values);
                Status = result.Summary;
            }
        });
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await m_workspace.CancelAsync().ConfigureAwait(true);
        m_selectionVersion++;
        ClearResults();
        Status = "Stopped. Discover again before starting another task.";
    }

    private async Task PresentOperationAsync(string operation, Func<Task> run)
    {
        IsBusy = true;
        Status = operation + "...";
        try
        {
            await run().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Status = operation + " canceled.";
        }
        catch (Exception exception) when (exception is ServiceResultException or ArgumentException or
            InvalidOperationException or IOException or NotSupportedException or TimeoutException or
            JsonException or UnauthorizedAccessException)
        {
            CompanionPluginLog.OperationFailed(m_host.Log, operation, exception);
            Status = exception is ServiceResultException service
                ? $"{operation}: {service.StatusCode}"
                : $"{operation}: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetValues(ArrayOf<CompanionValue> values)
    {
        Values.Clear();
        foreach (CompanionValue value in values)
        {
            Values.Add(value);
        }
    }

    private void ClearResults()
    {
        Targets.Clear();
        SelectedTarget = null;
        Values.Clear();
        Operations.Clear();
        SelectedOperation = null;
    }

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private string m_status = "Connect the primary server, then discover supported companion instances.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DiscoverCommand), nameof(InspectCommand), nameof(RunTaskCommand))]
    private bool m_isOffline = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DiscoverCommand), nameof(InspectCommand), nameof(RunTaskCommand))]
    private bool m_isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DiscoverCommand))]
    private CompanionDescriptor? m_selectedProvider;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InspectCommand), nameof(RunTaskCommand))]
    private CompanionTarget? m_selectedTarget;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunTaskCommand))]
    private CompanionOperation? m_selectedOperation;

    [ObservableProperty]
    private string m_operationInput = string.Empty;

    [ObservableProperty]
    private bool m_confirmLocalSample;

    private readonly PluginHost m_host;
    private readonly CompanionWorkspace m_workspace;
    private CompanionView? m_view;
    private string? m_restoredTarget;
    private int m_selectionVersion;
    private static int s_number;
}

internal sealed record CompanionDocumentState(int Version, string? ProviderId, string? TargetId);

[JsonSerializable(typeof(CompanionDocumentState))]
internal sealed partial class CompanionJsonContext : JsonSerializerContext;

internal static partial class CompanionPluginLog
{
    [LoggerMessage(EventId = UaLensEventIds.CompanionPluginBase + 1, Level = LogLevel.Error,
        Message = "Companion task {Operation} failed.")]
    public static partial void OperationFailed(ILogger logger, string operation, Exception exception);
}
