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
using Avalonia.Platform.Storage;
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
        ArrayOf<ICompanionProvider> providers = default,
        TimeProvider? timeProvider = null)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_workspace = workspace ?? new CompanionWorkspace(providers, host.Telemetry, timeProvider);
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

    public ObservableCollection<CompanionInputEditor> InputFields { get; } = [];

    public string PreparationSummary => PreparedOperation?.Summary ??
        "Prepare the selected task to review its target and effect. No operation is executed by preparation.";

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
        InvalidatePreparation();
    }

    partial void OnSelectedOperationChanged(CompanionOperation? value)
    {
        m_selectionVersion++;
        OperationInput = string.Empty;
        InputFields.Clear();
        if (value is not null)
        {
            foreach (CompanionInputDefinition field in value.Inputs)
            {
                InputFields.Add(new CompanionInputEditor(field, InputChanged, PickPackageAsync));
            }
        }
        InvalidatePreparation();
    }

    private void InputChanged()
    {
        m_selectionVersion++;
        InvalidatePreparation();
    }

    private async Task<string?> PickPackageAsync()
    {
        string? path = null;
        await PresentOperationAsync("Select package", async () =>
        {
            IStorageProvider storage = TopLevel.GetTopLevel(m_view)?.StorageProvider ??
                throw new InvalidOperationException("Open the Companion Tasks view to choose a package file.");
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select a local software package",
                AllowMultiple = false
            }).ConfigureAwait(true);
            if (files.Count == 0)
            {
                return;
            }
            using IStorageFile file = files[0];
            path = file.TryGetLocalPath() ??
                throw new InvalidOperationException("The selected file must have an absolute local path.");
            Status = "Package path selected. Enter its independently supplied SHA-256 before preparing.";
        }).ConfigureAwait(true);
        return path;
    }

    partial void OnOperationInputChanged(string value)
    {
        m_selectionVersion++;
        InvalidatePreparation();
    }

    private void InvalidatePreparation(bool discardWorkspace = true)
    {
        PreparedOperation = null;
        ConfirmLocalSample = false;
        if (discardWorkspace)
        {
            m_workspace.DiscardPreparation();
        }
    }

    private bool CanDiscover()
    {
        return !IsBusy && !IsOffline && SelectedProvider is not null;
    }

    private bool CanInspect()
    {
        return !IsBusy && !IsOffline && SelectedTarget is not null;
    }

    private bool CanPrepareTask()
    {
        return CanInspect() && SelectedOperation is not null;
    }

    private bool CanRunTask()
    {
        return CanPrepareTask() && PreparedOperation is { } prepared &&
            (prepared.Operation.Safety != CompanionOperationSafety.SampleMutation || ConfirmLocalSample);
    }

    [RelayCommand(CanExecute = nameof(CanDiscover))]
    private Task DiscoverAsync(CancellationToken cancellationToken)
    {
        return PresentOperationAsync("Discover", async () =>
        {
            InvalidatePreparation();
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
            InvalidatePreparation();
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

    [RelayCommand(CanExecute = nameof(CanPrepareTask))]
    private Task PrepareTaskAsync(CancellationToken cancellationToken)
    {
        return PresentOperationAsync("Prepare task", async () =>
        {
            CompanionTarget target = SelectedTarget ?? throw new InvalidOperationException("Select an instance.");
            CompanionOperation operation = SelectedOperation ??
                throw new InvalidOperationException("Inspect the instance and select an operation.");
            int version = m_selectionVersion;
            InvalidatePreparation();
            CompanionOperationDraft draft = operation.HasTypedInput
                ? await m_workspace.PrepareTaskAsync(
                    target, operation.Id, new ArrayOf<CompanionValue>(
                        InputFields.Select(field => field.Capture()).ToArray()), cancellationToken).ConfigureAwait(true)
                : await m_workspace.PrepareAsync(
                    target, operation.Id, OperationInput, cancellationToken).ConfigureAwait(true);
            if (version == m_selectionVersion)
            {
                PreparedOperation = draft;
                Status = "Prepared for one execution. Review the target and effect, then explicitly Run.";
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunTask))]
    private Task RunTaskAsync(CancellationToken cancellationToken)
    {
        return PresentOperationAsync("Run task", async () =>
        {
            CompanionOperationDraft draft = PreparedOperation ??
                throw new InvalidOperationException("Prepare the selected operation before running it.");
            bool confirmed = ConfirmLocalSample;
            int version = m_selectionVersion;
            int presentationVersion = m_presentationVersion;
            InvalidatePreparation(discardWorkspace: false);
            var progress = new Progress<CompanionTaskProgress>(update =>
            {
                if (IsBusy && version == m_selectionVersion && presentationVersion == m_presentationVersion)
                {
                    Status = update.Percent is { } percent
                        ? $"{update.Phase} ({percent}%)"
                        : update.Phase;
                }
            });
            CompanionOperationResult result = await m_workspace.ExecuteTaskAsync(
                draft, confirmed, progress, cancellationToken).ConfigureAwait(true);
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
        m_presentationVersion++;
        IsBusy = true;
        Status = operation + "...";
        try
        {
            await run().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            InvalidatePreparation();
            Status = operation + " canceled.";
        }
        catch (Exception exception) when (exception is ServiceResultException or ArgumentException or
            InvalidOperationException or IOException or NotSupportedException or TimeoutException or
            JsonException or UnauthorizedAccessException)
        {
            InvalidatePreparation();
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
        InvalidatePreparation();
    }

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private string m_status = "Connect the primary server, then discover supported companion instances.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(
        nameof(DiscoverCommand), nameof(InspectCommand), nameof(PrepareTaskCommand), nameof(RunTaskCommand))]
    private bool m_isOffline = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(
        nameof(DiscoverCommand), nameof(InspectCommand), nameof(PrepareTaskCommand), nameof(RunTaskCommand))]
    private bool m_isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DiscoverCommand))]
    private CompanionDescriptor? m_selectedProvider;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InspectCommand), nameof(PrepareTaskCommand), nameof(RunTaskCommand))]
    private CompanionTarget? m_selectedTarget;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrepareTaskCommand), nameof(RunTaskCommand))]
    private CompanionOperation? m_selectedOperation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreparationSummary))]
    [NotifyCanExecuteChangedFor(nameof(RunTaskCommand))]
    private CompanionOperationDraft? m_preparedOperation;

    [ObservableProperty]
    private string m_operationInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunTaskCommand))]
    private bool m_confirmLocalSample;

    private readonly PluginHost m_host;
    private readonly CompanionWorkspace m_workspace;
    private CompanionView? m_view;
    private string? m_restoredTarget;
    private int m_selectionVersion;
    private int m_presentationVersion;
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
