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
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.Historian;
using UaLens.Subscriptions;
using UaLens.ViewModels;

namespace UaLens.Plugins.Companions.Machinery
{
    internal sealed partial class MachineryPlugin
    {
        /// <summary>
        /// Gets or sets the inspected source field selected for Monitor or History.
        /// </summary>
        [ObservableProperty]
        public partial CompanionValue? SelectedValue { get; set; }

        /// <summary>
        /// Gets or sets an explicit new local export destination, never persisted in document state.
        /// </summary>
        [ObservableProperty]
        public partial string ActionDestination { get; set; } = string.Empty;

        /// <summary>
        /// Gets whether owned selected-item work is still running or draining.
        /// </summary>
        [ObservableProperty]
        public partial bool IsActionBusy { get; private set; }

        /// <summary>
        /// Gets whether the selected details are a process value, without claiming a method is available.
        /// </summary>
        public bool ShowProcessTask => IsShowingDetails && SelectedSection?.Section == MachinerySection.ProcessValues;

        /// <summary>
        /// Gets whether the current details can navigate into the existing job workflow.
        /// </summary>
        public bool ShowJobTask => IsShowingDetails && SelectedSection?.Section == MachinerySection.Jobs;

        /// <summary>
        /// Gets whether the current details are a published-result slot.
        /// </summary>
        public bool ShowResultAction => IsShowingDetails && SelectedSection?.Section == MachinerySection.Results;

        /// <summary>
        /// Gets the currently owned transient task window, not a workspace document.
        /// </summary>
        internal MachineryTaskDialog? ActiveTaskDialog => m_taskDialog;

        partial void OnSelectedValueChanged(CompanionValue? value)
        {
            m_actionCancellation?.Cancel();
            NotifyActions();
        }

        partial void OnActionDestinationChanged(string value)
        {
            m_actionCancellation?.Cancel();
            NotifyActions();
        }

        partial void OnIsActionBusyChanged(bool value) => NotifyActions();

        partial void OnIsLoadingChanged(bool value) => NotifyActions();

        private bool CanAct()
        {
            return !IsActionBusy && !IsLoading && IsDetailCurrent();
        }

        private bool CanOpenSource()
        {
            return CanAct() &&
                SelectedValue is { SourceNodeId.IsNull: false } value &&
                m_detailValues.Contains(candidate => ReferenceEquals(candidate, value));
        }

        private bool CanExport()
        {
            return CanAct() &&
                m_detailValues.Count > 0 &&
                !string.IsNullOrWhiteSpace(ActionDestination);
        }

        private bool CanDownload()
        {
            if (!ShowResultAction || !CanExport())
            {
                return false;
            }
            try
            {
                _ = CaptureSelection().RequireResultId();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private bool CanOpenProcessTask()
        {
            return ShowProcessTask &&
                CanAct() &&
                m_detailSelection is { SourceNodeId.IsNull: false };
        }

        private bool CanOpenJobTask()
        {
            return ShowJobTask && CanAct();
        }

        [RelayCommand(CanExecute = nameof(CanOpenSource))]
        private Task OpenMonitorAsync(CancellationToken cancellationToken)
        {
            CompanionValue? field = SelectedValue;
            return RunActionAsync("Open Monitor", async (selection, token) =>
            {
                RequireField(selection, field, token);
                IPlugin opened = await m_host.Workspace.OpenToolAsync(
                    PluginKind.Subscription, cancellationToken: token).ConfigureAwait(true);
                RequireField(selection, field, token);
                if (opened is not SubscriptionViewModel monitor)
                {
                    throw new InvalidOperationException("The configured Monitor cannot accept a source variable.");
                }
                MonitoredItemConfig? added = null;
                void capture(object? _, NotifyCollectionChangedEventArgs args)
                {
                    if (added is null &&
                        args.Action == NotifyCollectionChangedAction.Add &&
                        args.NewItems is { Count: 1 } items &&
                        items[0] is MonitoredItemConfig item &&
                        item.NodeId == field!.SourceNodeId &&
                        item.DisplayName == field.Name)
                    {
                        added = item;
                    }
                }
                monitor.Items.CollectionChanged += capture;
                try
                {
                    await monitor.AddItemAsync(new MonitoredItemConfig
                    {
                        NodeId = field!.SourceNodeId,
                        DisplayName = field.Name
                    }).ConfigureAwait(true);
                    RequireField(selection, field, token);
                }
                catch (Exception failure) when (IsExpected(failure) || failure is OperationCanceledException)
                {
                    if (added is not null)
                    {
                        try
                        {
                            await monitor.RemoveItemAsync(added).ConfigureAwait(true);
                        }
                        catch (Exception cleanup) when (IsExpected(cleanup) || cleanup is OperationCanceledException)
                        {
                            throw new AggregateException(
                                "Source handoff and owned-item cleanup failed.", failure, cleanup);
                        }
                    }
                    throw;
                }
                finally
                {
                    monitor.Items.CollectionChanged -= capture;
                }
                Status = "Source added to Monitor. That document now owns observation; stop or close it there.";
            }, cancellationToken);
        }

        [RelayCommand(CanExecute = nameof(CanOpenSource))]
        private Task OpenHistoryAsync(CancellationToken cancellationToken)
        {
            CompanionValue? field = SelectedValue;
            return RunActionAsync("Open History", async (selection, token) =>
            {
                RequireField(selection, field, token);
                IPlugin opened = await m_host.Workspace.OpenToolAsync(
                    PluginKind.Historian, cancellationToken: token).ConfigureAwait(true);
                RequireField(selection, field, token);
                if (opened is not HistorianPlugin history)
                {
                    throw new InvalidOperationException("The configured Historian cannot accept a source variable.");
                }
                history.TargetNodeId = field!.SourceNodeId;
                history.TargetDisplayName = field.Name;
                Status = "Source selected in Historian. Choose a time range there; no history read was started.";
            }, cancellationToken);
        }

        [RelayCommand(CanExecute = nameof(CanExport))]
        private Task ExportSnapshotAsync(CancellationToken cancellationToken)
        {
            string path = ActionDestination;
            return RunActionAsync("Export displayed snapshot", async (selection, token) =>
            {
                void RequireCurrent()
                {
                    token.ThrowIfCancellationRequested();
                    selection.RequireCurrent();
                    RequireDestination(path);
                }
                RequireCurrent();
                await CompanionSnapshotExporter.WriteAsync(
                    path, selection.Target, selection.Values, selection.Context.Session.NamespaceUris,
                    RequireCurrent, token).ConfigureAwait(true);
                RequireCurrent();
                Status = "Displayed values exported to the new local JSON file. No task authorization was saved.";
            }, cancellationToken);
        }

        [RelayCommand(CanExecute = nameof(CanDownload))]
        private Task DownloadResultAsync(CancellationToken cancellationToken)
        {
            string path = ActionDestination;
            return RunActionAsync("Download selected result", async (selection, token) =>
            {
                RequireDestination(path);
                long count = await MachineryResultExport.WriteAsync(path, selection, m_results, token)
                    .ConfigureAwait(true);
                token.ThrowIfCancellationRequested();
                selection.RequireCurrent();
                RequireDestination(path);
                Status = $"Selected result exported ({count} bytes). " +
                    "No URI was fetched and no partial file was published.";
            }, cancellationToken);
        }

        [RelayCommand(CanExecute = nameof(CanOpenProcessTask))]
        private Task OpenProcessTaskAsync(CancellationToken cancellationToken)
        {
            return RunActionAsync("Open selected process task", (selection, token) =>
                OpenTaskAsync(selection,
                    new MachineryZeroPointProvider(selection, m_policy, m_time, token), token),
                cancellationToken, TimeSpan.FromMinutes(5));
        }

        [RelayCommand(CanExecute = nameof(CanOpenJobTask))]
        private Task OpenJobTaskAsync(CancellationToken cancellationToken)
        {
            return RunActionAsync("Open selected job workflow", (selection, token) =>
                OpenTaskAsync(selection, new MachineryJobTaskProvider(selection, m_jobs, token), token),
                cancellationToken, TimeSpan.FromMinutes(5));
        }

        private async Task OpenTaskAsync(
            MachineryActionSelection selection, IPreparedCompanionProvider provider,
            CancellationToken cancellationToken)
        {
            if (TopLevel.GetTopLevel(m_view) is not Window owner)
            {
                throw new InvalidOperationException("Open the Machinery view before reviewing a selected task.");
            }
            var task = new CompanionPlugin(m_host, providers: [provider],
                timeProvider: m_time, deploymentPolicy: m_policy, kind: PluginKind.Machinery,
                documentName: "Selected Machinery task");
            string context = selection.Section == MachinerySection.Jobs
                ? $"Selected job: {selection.Item.ItemId ?? "job-control source"}\n" +
                    $"JobOrderList: {selection.Item.NodeId}"
                : $"Selected process: {selection.Item.Name}\nSource: {selection.Item.SourceNodeId}";
            var dialog = new MachineryTaskDialog(task, context,
                selection.Section == MachinerySection.Jobs ? selection.Item.ItemId : null);
            m_taskDialog = dialog;
            OnPropertyChanged(nameof(ActiveTaskDialog));
            await using (task.ConfigureAwait(true))
            await using (dialog.ConfigureAwait(true))
            {
                await task.OnConnectionStateChangedAsync(cancellationToken).ConfigureAwait(true);
                selection.RequireCurrent();
                await task.DiscoverCommand.ExecuteAsync(null).ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
                selection.RequireCurrent();
                if (task.SelectedTarget is not null)
                {
                    await task.InspectCommand.ExecuteAsync(null).ConfigureAwait(true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                selection.RequireCurrent();
                await dialog.ShowOwnedAsync(owner, cancellationToken).ConfigureAwait(true);
            }
            selection.RequireCurrent();
            m_detailBinding = null;
            Status = "Task window closed. Refresh this item explicitly before acting on its values again.";
        }

        private Task RunActionAsync(
            string label, Func<MachineryActionSelection, CancellationToken, Task> action,
            CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            if (!m_actionWork.IsCompleted)
            {
                return Task.CompletedTask;
            }
            m_view?.HideActions();
            m_actionWork = RunActionCoreAsync(label, action, timeout ?? TimeSpan.FromSeconds(30), cancellationToken);
            return m_actionWork;
        }

        private async Task RunActionCoreAsync(
            string label, Func<MachineryActionSelection, CancellationToken, Task> action,
            TimeSpan timeout, CancellationToken cancellationToken)
        {
            long version = m_version;
            using var deadline = new CancellationTokenSource(timeout, m_time);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            m_actionCancellation = lifetime;
            IsActionBusy = true;
            try
            {
                MachineryActionSelection selection = CaptureSelection();
                await Task.Yield();
                lifetime.Token.ThrowIfCancellationRequested();
                selection.RequireCurrent();
                Status = label + "...";
                await action(selection, lifetime.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                if (version == m_version && !m_closed)
                {
                    Status = label + " canceled. No preparation is replayed.";
                }
            }
            catch (Exception error) when (IsExpected(error) || error is NotSupportedException)
            {
                CompanionPluginLog.OperationFailed(m_host.Log, label, error);
                if (version == m_version && !m_closed)
                {
                    Status = label + " unavailable: " + error.Message;
                }
            }
            finally
            {
                if (m_taskDialog is { } dialog)
                {
                    await dialog.DisposeAsync().ConfigureAwait(true);
                    m_taskDialog = null;
                    m_detailBinding = null;
                    OnPropertyChanged(nameof(ActiveTaskDialog));
                }
                if (ReferenceEquals(m_actionCancellation, lifetime))
                {
                    m_actionCancellation = null;
                }
                IsActionBusy = false;
            }
        }

        private bool IsDetailCurrent()
        {
            return !m_closed &&
                m_resetting == 0 &&
                !IsOffline &&
                IsShowingDetails &&
                m_detailBinding is { } binding &&
                m_detailSession is { } session &&
                ReferenceEquals(session, m_host.Session) &&
                m_detailVersion == m_version &&
                binding.MatchesSession(session) &&
                m_detailPeer == session.Endpoint.ServerCertificate &&
                ReferenceEquals(m_detailMessageContext, session.MessageContext);
        }

        private MachineryActionSelection CaptureSelection()
        {
            if (!IsDetailCurrent() ||
                IsLoading ||
                SelectedMachine is not { } machine ||
                SelectedSection is not { } section ||
                m_detailSelection is not { } item ||
                m_detailSession is not { } session)
            {
                throw new InvalidOperationException("Inspect the current selected Machinery item first.");
            }
            long version = m_version;
            CompanionOperationDraft binding = m_detailBinding!;
            var context = new CompanionContext(session, m_host.Telemetry);
            return new MachineryActionSelection(context, machine, section.Section, item, m_detailValues, () =>
            {
                if (!IsDetailCurrent() ||
                    version != m_version ||
                    !ReferenceEquals(binding, m_detailBinding) ||
                    !ReferenceEquals(machine, SelectedMachine) ||
                    !ReferenceEquals(section, SelectedSection) ||
                    !ReferenceEquals(item, m_detailSelection))
                {
                    throw new InvalidOperationException("The selected Machinery item changed. Inspect again.");
                }
            });
        }

        private void RequireField(
            MachineryActionSelection selection, CompanionValue? field, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            selection.RequireCurrent();
            if (field is null ||
                field.SourceNodeId.IsNull ||
                !ReferenceEquals(field, SelectedValue) ||
                !selection.Values.Contains(candidate => ReferenceEquals(candidate, field)))
            {
                throw new InvalidOperationException("Select a current inspected source field first.");
            }
        }

        private void RequireDestination(string path)
        {
            if (!string.Equals(ActionDestination, path, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The explicit export destination changed.");
            }
        }

        private async Task StopActionsAsync()
        {
            if (m_actionCancellation is { } cancellation)
            {
                await cancellation.CancelAsync().ConfigureAwait(true);
            }
            await m_actionWork.ConfigureAwait(true);
        }

        private void ClearActionSelection()
        {
            m_detailBinding = null;
            m_detailSession = null;
            m_detailValues = [];
            m_detailPeer = default;
            m_detailMessageContext = null;
            SelectedValue = null;
            NotifyActions();
        }

        private void NotifyActions()
        {
            OpenMonitorCommand.NotifyCanExecuteChanged();
            OpenHistoryCommand.NotifyCanExecuteChanged();
            ExportSnapshotCommand.NotifyCanExecuteChanged();
            DownloadResultCommand.NotifyCanExecuteChanged();
            OpenProcessTaskCommand.NotifyCanExecuteChanged();
            OpenJobTaskCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(ShowProcessTask));
            OnPropertyChanged(nameof(ShowJobTask));
            OnPropertyChanged(nameof(ShowResultAction));
        }

        private readonly IMachineryResultSource m_results;
        private readonly ICompanionDeploymentPolicy m_policy;
        private readonly TimeProvider m_time;
        private readonly IPreparedCompanionProvider m_jobs;
        private Task m_actionWork = Task.CompletedTask;
        private CancellationTokenSource? m_actionCancellation;
        private MachineryTaskDialog? m_taskDialog;
        private ISession? m_detailSession;
        private CompanionOperationDraft? m_detailBinding;
        private ArrayOf<CompanionValue> m_detailValues;
        private ByteString m_detailPeer;
        private IServiceMessageContext? m_detailMessageContext;
        private long m_detailVersion;
    }
}
