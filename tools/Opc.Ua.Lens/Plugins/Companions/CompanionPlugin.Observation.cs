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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.Alarms;
using UaLens.Plugins.Companions.Providers;
using UaLens.Plugins.Historian;
using UaLens.Subscriptions;
using UaLens.ViewModels;

namespace UaLens.Plugins.Companions
{
    internal sealed partial class CompanionPlugin
    {
        /// <summary>
        /// Gets or sets the displayed source value selected for a cooperating tool.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OpenMonitorCommand), nameof(OpenHistoryCommand))]
        public partial CompanionValue? SelectedValue { get; set; }

        /// <summary>
        /// Gets whether this document can open its scale-specific PackML root in the generic tool.
        /// </summary>
        public bool ShowPackMlAction => Kind == PluginKind.Weighing;

        private bool CanOpenObservation()
        {
            return !m_closing &&
                !IsBusy &&
                !IsOffline &&
                SelectedValue is { SourceNodeId.IsNull: false } value &&
                Values.Contains(value) &&
                m_valueBinding is not null &&
                ReferenceEquals(m_valueSession, m_host.Session);
        }

        private bool CanExportSnapshot()
        {
            return !m_closing &&
                !IsBusy &&
                Values.Count > 0 &&
                SelectedTarget is not null &&
                m_valueBinding is not null;
        }

        private bool CanOpenAlarms()
        {
            return !m_closing && !IsBusy && !IsOffline && SelectedTarget is not null && m_valueBinding is not null;
        }

        private bool CanOpenPackMl() => ShowPackMlAction && CanOpenAlarms();

        [RelayCommand(CanExecute = nameof(CanOpenPackMl))]
        private Task OpenPackMlAsync(CancellationToken cancellationToken)
        {
            return PresentOperationAsync("Open PackML", async () =>
            {
                CompanionTarget target = SelectedTarget ?? throw new InvalidOperationException("Inspect a scale first.");
                ISession session = m_valueSession ?? throw new InvalidOperationException("Connect the scale first.");
                CompanionOperationDraft binding = m_valueBinding ??
                    throw new InvalidOperationException("Inspect the current scale first.");
                void RequireCurrent()
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (m_closing ||
                        SelectedTarget != target ||
                        !ReferenceEquals(session, m_host.Session) ||
                        !binding.MatchesSession(session))
                    {
                        throw new InvalidOperationException("The selected scale changed. Inspect again.");
                    }
                }
                RequireCurrent();
                var context = new CompanionContext(session, m_host.Telemetry);
                string name = target.TypeName == "Scale system"
                    ? Opc.Ua.Scales.BrowseNames.SystemState
                    : Opc.Ua.Scales.BrowseNames.State;
                NodeId root = await IndustrialCompanionAccess.ResolveChildAsync(
                    context, target.NodeId, Opc.Ua.Scales.Namespaces.Scales, name, false, cancellationToken)
                    .ConfigureAwait(true);
                RequireCurrent();
                IPlugin document = await m_host.Workspace.OpenToolAsync(
                    PluginKind.PackML, cancellationToken: cancellationToken).ConfigureAwait(true);
                RequireCurrent();
                if (document is not CompanionPlugin { Kind: PluginKind.PackML } packml)
                {
                    throw new InvalidOperationException("The configured PackML tool cannot accept this source.");
                }
                JsonElement state = JsonSerializer.SerializeToElement(
                    new CompanionDocumentState(1, "packml", NodeId.ToExpandedNodeId(root, session.NamespaceUris).ToString()),
                    CompanionJsonContext.Default.CompanionDocumentState);
                await packml.RestoreStateAsync(state, cancellationToken).ConfigureAwait(true);
                Status = "PackML source selected. Discover and inspect there; no command or observation was started.";
            });
        }

        [RelayCommand(CanExecute = nameof(CanOpenAlarms))]
        private Task OpenAlarmsAsync(CancellationToken cancellationToken)
        {
            return PresentOperationAsync("Open conditions", async () =>
            {
                CompanionTarget target = SelectedTarget ?? throw new InvalidOperationException("Inspect an instance.");
                ISession session = m_valueSession ?? throw new InvalidOperationException("Inspect a connected instance.");
                CompanionOperationDraft binding = m_valueBinding ??
                    throw new InvalidOperationException("Inspect the current target first.");
                void RequireCurrent()
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (m_closing ||
                        SelectedTarget != target ||
                        !ReferenceEquals(session, m_host.Session) ||
                        !binding.MatchesSession(session))
                    {
                        throw new InvalidOperationException("The selected event source changed. Inspect again.");
                    }
                }
                RequireCurrent();
                ReadResponse response = await session.ReadAsync(
                    null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = target.NodeId, AttributeId = Attributes.EventNotifier }],
                    cancellationToken).ConfigureAwait(true);
                RequireCurrent();
                if (response.ResponseHeader is null || response.Results.Count != 1)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, "The event-source read is incomplete.");
                }
                if (!StatusCode.IsGood(response.ResponseHeader.ServiceResult) ||
                    !StatusCode.IsGood(response.Results[0].StatusCode))
                {
                    throw new ServiceResultException(!StatusCode.IsGood(response.ResponseHeader.ServiceResult)
                        ? response.ResponseHeader.ServiceResult : response.Results[0].StatusCode);
                }
                if (!response.Results[0].WrappedValue.TryGetValue(out byte notifier) ||
                    (notifier & EventNotifiers.SubscribeToEvents) == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported, "This instance is not an event source.");
                }
                IPlugin document = await m_host.Workspace.OpenToolAsync(
                    PluginKind.Alarms, cancellationToken: cancellationToken).ConfigureAwait(true);
                RequireCurrent();
                if (document is not AlarmsPlugin alarms)
                {
                    throw new InvalidOperationException("The configured Alarms tool does not accept an event source.");
                }
                await alarms.SeedSourceAsync(target.NodeId, target.DisplayName, cancellationToken).ConfigureAwait(true);
                Status = "Condition source selected in Alarms. Select Observe there; no acknowledgement was sent.";
            });
        }

        [RelayCommand(CanExecute = nameof(CanOpenObservation))]
        private Task OpenMonitorAsync(CancellationToken cancellationToken)
        {
            return PresentOperationAsync("Open Monitor", async () =>
            {
                CompanionValue value = SelectedValue ?? throw new InvalidOperationException("Select a source value.");
                RequireCurrentValue(value);
                IPlugin document = await m_host.Workspace.OpenToolAsync(
                    PluginKind.Subscription, cancellationToken: cancellationToken).ConfigureAwait(true);
                RequireCurrentValue(value);
                if (document is not SubscriptionViewModel monitor)
                {
                    throw new InvalidOperationException("The configured Monitor does not accept source variables.");
                }
                await monitor.AddItemAsync(new MonitoredItemConfig
                {
                    NodeId = value.SourceNodeId,
                    DisplayName = value.Name
                }).ConfigureAwait(true);
                Status = "Source added to Monitor. Stop or close that document to end its observation.";
            });
        }

        [RelayCommand(CanExecute = nameof(CanOpenObservation))]
        private Task OpenHistoryAsync(CancellationToken cancellationToken)
        {
            return PresentOperationAsync("Open History", async () =>
            {
                CompanionValue value = SelectedValue ?? throw new InvalidOperationException("Select a source value.");
                RequireCurrentValue(value);
                IPlugin document = await m_host.Workspace.OpenToolAsync(
                    PluginKind.Historian, cancellationToken: cancellationToken).ConfigureAwait(true);
                RequireCurrentValue(value);
                if (document is not HistorianPlugin history)
                {
                    throw new InvalidOperationException("The configured Historian does not accept source variables.");
                }
                history.TargetNodeId = value.SourceNodeId;
                history.TargetDisplayName = value.Name;
                Status = "Source selected in Historian. Choose the time range there; no history read was started.";
            });
        }

        [RelayCommand(CanExecute = nameof(CanExportSnapshot))]
        private Task ExportSnapshotAsync(CancellationToken cancellationToken)
        {
            return PresentOperationAsync("Export displayed snapshot", async () =>
            {
                if (TopLevel.GetTopLevel(m_view) is not { } owner ||
                    m_valueSession is not { } session ||
                    m_valueBinding is not { } binding)
                {
                    throw new InvalidOperationException("Open and inspect the connected tool before exporting.");
                }
                int version = m_selectionVersion;
                void RequireCurrent()
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (m_closing ||
                        version != m_selectionVersion ||
                        !ReferenceEquals(session, m_host.Session) ||
                        !binding.MatchesSession(session))
                    {
                        throw new InvalidOperationException("The displayed snapshot belongs to an obsolete selection.");
                    }
                }
                RequireCurrent();
                using IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export displayed values to a new local JSON file",
                    SuggestedFileName = "ualens-snapshot.json",
                    FileTypeChoices = [new FilePickerFileType("JSON snapshot") { Patterns = ["*.json"] }]
                }).ConfigureAwait(true);
                RequireCurrent();
                if (file is null)
                {
                    Status = "Snapshot export canceled.";
                    return;
                }
                string path = file.TryGetLocalPath() ??
                    throw new InvalidOperationException("Choose a local file; remote destinations are not supported.");
                await CompanionSnapshotExporter.WriteAsync(
                    path, binding.Target, [.. Values], session.NamespaceUris, RequireCurrent, cancellationToken)
                    .ConfigureAwait(true);
                Status = "Displayed values and source identities exported. No task authorization was saved.";
            });
        }

        private void RequireCurrentValue(CompanionValue value)
        {
            if (m_closing ||
                value.SourceNodeId.IsNull ||
                !Values.Contains(value) ||
                m_valueSession is not { } session ||
                !ReferenceEquals(session, m_host.Session) ||
                m_valueBinding is null ||
                !m_valueBinding.MatchesSession(session))
            {
                throw new InvalidOperationException("Inspect the current target before opening its source values.");
            }
        }

        private Task StopObservationAsync()
        {
            OpenMonitorCommand.Cancel();
            OpenHistoryCommand.Cancel();
            OpenAlarmsCommand.Cancel();
            OpenPackMlCommand.Cancel();
            ExportSnapshotCommand.Cancel();
            return Task.WhenAll(
                OpenMonitorCommand.ExecutionTask ?? Task.CompletedTask,
                OpenHistoryCommand.ExecutionTask ?? Task.CompletedTask,
                OpenAlarmsCommand.ExecutionTask ?? Task.CompletedTask,
                OpenPackMlCommand.ExecutionTask ?? Task.CompletedTask,
                ExportSnapshotCommand.ExecutionTask ?? Task.CompletedTask);
        }

        private ISession? m_valueSession;
        private CompanionOperationDraft? m_valueBinding;
    }
}
