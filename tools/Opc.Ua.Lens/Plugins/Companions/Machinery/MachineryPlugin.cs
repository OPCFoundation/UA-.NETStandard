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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opc.Ua;
using UaLens.Plugins.Companions.Providers;
using UaLens.Storage;
using UaLens.ViewModels;

namespace UaLens.Plugins.Companions.Machinery
{
    /// <summary>
    /// One machine and one active section, with cancellable lazy reads and no hidden domain workloads.
    /// </summary>
    internal sealed partial class MachineryPlugin : ObservableObject, IPlugin, IWorkspaceState
    {
        /// <summary>
        /// Creates a disconnected document without accessing the source.
        /// </summary>
        public MachineryPlugin(
            PluginHost host, IMachineryDataSource? source = null, IMachineryResultSource? results = null,
            ICompanionDeploymentPolicy? deploymentPolicy = null, TimeProvider? timeProvider = null,
            IPreparedCompanionProvider? jobs = null)
        {
            m_host = host ?? throw new ArgumentNullException(nameof(host));
            m_source = source ?? new MachineryDataSource();
            m_results = results ?? new MachineryResultSource();
            m_time = timeProvider ?? TimeProvider.System;
            m_policy = deploymentPolicy ?? new ConfiguredCompanionDeploymentPolicy(timeProvider: m_time);
            m_jobs = jobs ?? new Isa95CompanionProvider();
            if (m_jobs.Descriptor.Id != "isa95")
            {
                throw new ArgumentException(
                    "Machinery job navigation requires the existing ISA-95 provider.", nameof(jobs));
            }
            Title = "Machinery " + Interlocked.Increment(ref s_number);
            SelectedSection = Sections[0];
        }

        /// <inheritdoc/>
        public PluginKind Kind => PluginKind.Machinery;

        /// <inheritdoc/>
        public bool SupportsDuplicate => false;

        /// <inheritdoc/>
        public Control View => m_view ??= new MachineryView { DataContext = this };

        /// <inheritdoc/>
        public Control? HeaderToolbar => null;

        /// <summary>
        /// Gets the compact section choices, not simultaneously running tabs.
        /// </summary>
        public ObservableCollection<MachinerySectionChoice> Sections { get; } =
        [
            new(MachinerySection.Overview, "Overview"),
            new(MachinerySection.ProcessValues, "Process values"),
            new(MachinerySection.Jobs, "Jobs"),
            new(MachinerySection.Energy, "Energy"),
            new(MachinerySection.Results, "Results")
        ];

        /// <summary>
        /// Gets the filtered machine selector without altering the selected machine.
        /// </summary>
        public ObservableCollection<MachineryMachine> Machines { get; } = [];

        /// <summary>
        /// Gets only the displayed page of the active section.
        /// </summary>
        public ObservableCollection<MachineryItem> Items { get; } = [];

        /// <summary>
        /// Gets the small Overview field set.
        /// </summary>
        public ObservableCollection<CompanionValue> Overview { get; } = [];

        /// <summary>
        /// Gets whether compact identity fields are the active view.
        /// </summary>
        public bool IsOverview => IsShowingDetails || ShowOverview;

        /// <summary>
        /// Gets whether the compact default Overview is visible.
        /// </summary>
        public bool ShowOverview => !IsShowingDetails && SelectedSection?.Section == MachinerySection.Overview;

        /// <summary>
        /// Gets whether an explicitly selected item is being inspected.
        /// </summary>
        public bool IsShowingDetails => m_detailSelection is not null;

        /// <summary>
        /// Gets the visible page ordinal.
        /// </summary>
        public string PageLabel => $"Page {m_pageIndex + 1}";

        /// <summary>
        /// Gets raw identifiers only for the optional details expander.
        /// </summary>
        public string TechnicalDetails => (m_detailSelection ?? SelectedItem) is { } item
            ? $"{item.NodeId}\n{item.Details}"
            : SelectedMachine is { } machine ? $"{machine.Kind}\n{machine.NodeId}" : "No selection.";

        /// <inheritdoc/>
        public void OnActivated()
        {
        }

        /// <inheritdoc/>
        public void OnDeactivated()
        {
        }

        /// <inheritdoc/>
        public IReadOnlyList<MenuItem> ContributeMenuItems()
        {
            return [];
        }

        /// <inheritdoc/>
        public async Task OnConnectionStateChangedAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            m_resetting++;
            NotifyAvailability();
            try
            {
                await StopReadsAsync().ConfigureAwait(true);
                SelectedMachine = null;
                Machines.Clear();
                m_allMachines = [];
                ClearPage();
                IsOffline = m_host.Session is null;
                Status = IsOffline
                    ? "Connect, then discover machines."
                    : "Connected. Select Discover to load machines.";
            }
            finally
            {
                m_resetting--;
                NotifyAvailability();
            }
        }

        /// <inheritdoc/>
        public JsonElement CaptureState()
        {
            string? machine = SelectedMachine is { } selected && m_host.Session is { } session
                ? NodeId.ToExpandedNodeId(selected.NodeId, session.NamespaceUris).ToString()
                : m_restoredMachine;
            return JsonSerializer.SerializeToElement(new MachineryDocumentState(
                1, machine, SelectedSection?.Section ?? MachinerySection.Overview, SearchText),
                MachineryJsonContext.Default.MachineryDocumentState);
        }

        /// <inheritdoc/>
        public async Task RestoreStateAsync(JsonElement state, CancellationToken cancellationToken = default)
        {
            MachineryDocumentState saved = state.Deserialize(MachineryJsonContext.Default.MachineryDocumentState) ??
                throw new JsonException("The Machinery document configuration is missing.");
            if (saved.Version != 1 ||
                !Enum.IsDefined(saved.Section) ||
                saved.Search is null ||
                saved.Search.Length > 256 ||
                saved.MachineId is { Length: > 4096 } ||
                (saved.MachineId is not null && !ExpandedNodeId.TryParse(saved.MachineId, out _)))
            {
                throw new JsonException("The Machinery document configuration is invalid.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            m_resetting++;
            NotifyAvailability();
            try
            {
                await StopReadsAsync().ConfigureAwait(true);
                SelectedMachine = null;
                SelectedSection = Sections.ToList().Single(section => section.Section == saved.Section);
                SearchText = saved.Search;
                m_restoredMachine = saved.MachineId;
                m_allMachines = [];
                Machines.Clear();
                ClearPage();
                Status = "Configuration restored. Select Discover; no section or operation was started.";
            }
            finally
            {
                m_resetting--;
                NotifyAvailability();
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return new(m_disposal ??= DisposeCoreAsync());
        }

        private async Task DisposeCoreAsync()
        {
            m_closed = true;
            IsOffline = true;
            await StopReadsAsync().ConfigureAwait(true);
            if (m_taskDialog is not null)
            {
                await m_taskDialog.DisposeAsync().ConfigureAwait(true);
                m_taskDialog = null;
            }
            m_actionCancellation?.Dispose();
            m_actionCancellation = null;
            m_current?.Dispose();
            m_current = null;
            m_slots.Dispose();
        }

        partial void OnSelectedMachineChanged(MachineryMachine? value)
        {
            SetDetailSelection(null);
            m_pageIndex = 0;
            OnPropertyChanged(nameof(TechnicalDetails));
            if (value is not null && m_host.Session is { } session)
            {
                m_restoredMachine = NodeId.ToExpandedNodeId(value.NodeId, session.NamespaceUris).ToString();
            }
            RequestPage();
        }

        partial void OnSelectedSectionChanged(MachinerySectionChoice? value)
        {
            SetDetailSelection(null);
            m_pageIndex = 0;
            RequestPage();
        }

        partial void OnSelectedItemChanged(MachineryItem? value)
        {
            OnPropertyChanged(nameof(TechnicalDetails));
            InspectSelectionCommand.NotifyCanExecuteChanged();
        }

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        private bool CanDiscover()
        {
            return !m_closed && m_resetting == 0 && !IsOffline && !IsDiscovering;
        }

        private bool CanRead()
        {
            return !m_closed &&
                m_resetting == 0 &&
                !IsOffline &&
                SelectedMachine is not null &&
                SelectedSection is not null;
        }

        private bool CanNext()
        {
            return CanRead() && !IsShowingDetails && !IsLoading && HasMore && m_pageIndex < 1023;
        }

        private bool CanPrevious()
        {
            return CanRead() && !IsShowingDetails && !IsLoading && m_pageIndex > 0;
        }

        private bool CanInspectSelection()
        {
            return CanRead() &&
                !IsLoading &&
                !IsShowingDetails &&
                (ShowOverview || SelectedItem is not null);
        }

        private bool CanBackToList()
        {
            return CanRead() && IsShowingDetails;
        }

        [RelayCommand(CanExecute = nameof(CanInspectSelection))]
        private void InspectSelection()
        {
            MachineryMachine machine = SelectedMachine ??
                throw new InvalidOperationException("Select a machine first.");
            MachineryItem item = ShowOverview
                ? new MachineryItem(machine.NodeId, machine.Name, "Machine details", machine.Kind)
                : SelectedItem ?? throw new InvalidOperationException("Select an item first.");
            SetDetailSelection(item);
            RequestPage();
        }

        [RelayCommand(CanExecute = nameof(CanBackToList))]
        private void BackToList()
        {
            SetDetailSelection(null);
            RequestPage();
        }

        private void SetDetailSelection(MachineryItem? item)
        {
            m_detailSelection = item;
            OnPropertyChanged(nameof(IsShowingDetails));
            OnPropertyChanged(nameof(ShowOverview));
            OnPropertyChanged(nameof(IsOverview));
            OnPropertyChanged(nameof(TechnicalDetails));
            InspectSelectionCommand.NotifyCanExecuteChanged();
            BackToListCommand.NotifyCanExecuteChanged();
            NotifyActions();
        }

        [RelayCommand(CanExecute = nameof(CanDiscover))]
        private async Task DiscoverAsync(CancellationToken cancellationToken)
        {
            await StopReadsAsync(cancelDiscovery: false).ConfigureAwait(true);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            m_current = cancellation;
            long version = ++m_version;
            IsDiscovering = true;
            Status = "Discovering machines...";
            try
            {
                var context = new CompanionContext(m_host.Session ??
                    throw new InvalidOperationException("Connect before discovery."), m_host.Telemetry);
                ArrayOf<MachineryMachine> machines = await m_source.DiscoverAsync(context, cancellation.Token)
                    .ConfigureAwait(true);
                cancellation.Token.ThrowIfCancellationRequested();
                if (version != m_version || !ReferenceEquals(context.Session, m_host.Session) || m_closed)
                {
                    return;
                }
                if (machines.Count > 128 ||
                    machines.ToList().Any(machine => machine.NodeId.IsNull) ||
                    machines.ToList().Select(machine => machine.NodeId).Distinct().Count() != machines.Count)
                {
                    throw new InvalidOperationException("Machine discovery returned invalid or unbounded identities.");
                }
                m_allMachines = machines;
                ApplyFilter();
                MachineryMachine? selected = m_restoredMachine is null ? machines.ToList().FirstOrDefault() :
                    machines.ToList().SingleOrDefault(machine =>
                        NodeId.ToExpandedNodeId(machine.NodeId, context.Session.NamespaceUris).ToString() ==
                            m_restoredMachine);
                SelectedMachine = selected;
                if (selected is null)
                {
                    Status = machines.Count == 0
                        ? "No machines were exposed."
                        : "The saved machine is no longer exposed.";
                }
            }
            catch (OperationCanceledException)
            {
                if (version == m_version)
                {
                    Status = "Discovery canceled.";
                }
            }
            catch (Exception error) when (IsExpected(error))
            {
                CompanionPluginLog.OperationFailed(m_host.Log, "Machinery discovery", error);
                if (version == m_version)
                {
                    Status = "Discovery failed: " + error.Message;
                }
            }
            finally
            {
                if (ReferenceEquals(m_current, cancellation))
                {
                    m_current = null;
                }
                IsDiscovering = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanRead))]
        private void Refresh()
        {
            RequestPage();
        }

        [RelayCommand(CanExecute = nameof(CanNext))]
        private void NextPage()
        {
            m_pageIndex++;
            RequestPage();
        }

        [RelayCommand(CanExecute = nameof(CanPrevious))]
        private void PreviousPage()
        {
            m_pageIndex--;
            RequestPage();
        }

        [RelayCommand]
        private async Task StopAsync()
        {
            m_resetting++;
            NotifyAvailability();
            try
            {
                await StopReadsAsync().ConfigureAwait(true);
                Status = "Stopped. The machine and section remain selected; refresh explicitly to read again.";
            }
            finally
            {
                m_resetting--;
                NotifyAvailability();
            }
        }

        private void RequestPage()
        {
            m_version++;
            m_current?.Cancel();
            Task actions = StopActionsAsync();
            bool canRead = CanRead();
            IsLoading = canRead;
            if (canRead)
            {
                Status = "Loading " + SelectedSection!.Label + "...";
            }
            ClearPage();
            if (!canRead)
            {
                return;
            }
            var context = new CompanionContext(m_host.Session ??
                throw new InvalidOperationException("Connect before reading a section."), m_host.Telemetry);
            var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            m_current = cancellation;
            Task read = ReadPageAsync(
                context, SelectedMachine!, SelectedSection!.Section, m_pageIndex, m_version,
                m_detailSelection, actions, cancellation);
            m_reads.Add(read);
            _ = RetireReadAsync(read);
        }

        private async Task ReadPageAsync(
            CompanionContext context, MachineryMachine machine, MachinerySection section,
            int pageIndex, long version, MachineryItem? detail, Task actions, CancellationTokenSource cancellation)
        {
            await Task.Yield();
            bool entered = false;
            try
            {
                await actions.ConfigureAwait(true);
                cancellation.Token.ThrowIfCancellationRequested();
                if (version != m_version || m_closed)
                {
                    return;
                }
                var binding = new CompanionOperationDraft(
                    new CompanionTarget("machinery", machine.NodeId, machine.Name, machine.Kind),
                    new CompanionOperation("read-section", "Read section", CompanionOperationSafety.ReadOnly),
                    null, context.Session, DateTimeOffset.MaxValue);
                ByteString peer = context.Session.Endpoint.ServerCertificate.Copy();
                IServiceMessageContext messages = context.Session.MessageContext;
                await m_slots.WaitAsync(cancellation.Token).ConfigureAwait(true);
                entered = true;
                if (!binding.MatchesSession(context.Session))
                {
                    throw new OperationCanceledException("The Machinery read belongs to an obsolete session.");
                }
                MachineryPage page;
                if (detail is null)
                {
                    page = await m_source.ReadAsync(
                        context, machine, section, pageIndex, PageSize, cancellation.Token).ConfigureAwait(true);
                }
                else
                {
                    ArrayOf<CompanionValue> values = await m_source.InspectAsync(
                        context, machine, section, detail, cancellation.Token).ConfigureAwait(true);
                    page = new MachineryPage(values, [], false, "Details: " + detail.Name);
                }
                cancellation.Token.ThrowIfCancellationRequested();
                if (version != m_version ||
                    m_closed ||
                    !ReferenceEquals(context.Session, m_host.Session) ||
                    !binding.MatchesSession(context.Session) ||
                    peer != context.Session.Endpoint.ServerCertificate ||
                    !ReferenceEquals(messages, context.Session.MessageContext))
                {
                    return;
                }
                ArgumentNullException.ThrowIfNull(page);
                if (page.Items.Count > PageSize ||
                    page.Overview.Count > (detail is null ? 16 : context.MaxFields) ||
                    string.IsNullOrWhiteSpace(page.Status) ||
                    page.Status.Length > 1024 ||
                    page.Items.ToList().Any(item => item is null ||
                        item.Name.Length > 256 ||
                        item.Summary.Length > 4096 ||
                        item.Details.Length > 8192) ||
                    page.Overview.ToList().Any(value => value is null ||
                        value.Name.Length > 256 ||
                        value.Text.Length > 4096))
                {
                    throw new InvalidOperationException("The section exceeded its bounded presentation contract.");
                }
                foreach (CompanionValue field in page.Overview)
                {
                    Overview.Add(field);
                }
                foreach (MachineryItem item in page.Items)
                {
                    Items.Add(item);
                }
                if (detail is not null)
                {
                    m_detailSession = context.Session;
                    m_detailBinding = binding;
                    m_detailPeer = peer;
                    m_detailMessageContext = messages;
                    m_detailVersion = version;
                    m_detailValues = page.Overview;
                }
                HasMore = page.HasMore;
                Status = page.Status;
            }
            catch (OperationCanceledException)
            {
                if (version == m_version)
                {
                    Status = "Section read canceled.";
                }
            }
            catch (Exception error) when (IsExpected(error))
            {
                CompanionPluginLog.OperationFailed(m_host.Log, "Machinery section", error);
                if (version == m_version)
                {
                    Status = "Section unavailable: " + error.Message;
                }
            }
            finally
            {
                if (entered)
                {
                    m_slots.Release();
                }
                if (ReferenceEquals(m_current, cancellation))
                {
                    m_current = null;
                }
                cancellation.Dispose();
                if (version == m_version)
                {
                    IsLoading = false;
                }
            }
        }

        private async Task RetireReadAsync(Task read)
        {
            try
            {
                await read.ConfigureAwait(true);
            }
            finally
            {
                m_reads.Remove(read);
            }
        }

        private async Task StopReadsAsync(bool cancelDiscovery = true)
        {
            m_version++;
            Task actions = StopActionsAsync();
            ClearActionSelection();
            if (cancelDiscovery)
            {
                DiscoverCommand.Cancel();
            }
            if (m_current is { } current)
            {
                await current.CancelAsync().ConfigureAwait(true);
            }
            if (cancelDiscovery && DiscoverCommand.ExecutionTask is { } discovery)
            {
                await discovery.ConfigureAwait(true);
            }
            Task[] reads = [.. m_reads];
            await Task.WhenAll(Task.WhenAll(reads), actions).ConfigureAwait(true);
            IsLoading = false;
        }

        private void ClearPage()
        {
            ClearActionSelection();
            Items.Clear();
            Overview.Clear();
            SelectedItem = null;
            HasMore = false;
            OnPropertyChanged(nameof(PageLabel));
            NextPageCommand.NotifyCanExecuteChanged();
            PreviousPageCommand.NotifyCanExecuteChanged();
            InspectSelectionCommand.NotifyCanExecuteChanged();
            BackToListCommand.NotifyCanExecuteChanged();
            NotifyActions();
        }

        private void NotifyAvailability()
        {
            DiscoverCommand.NotifyCanExecuteChanged();
            RefreshCommand.NotifyCanExecuteChanged();
            NextPageCommand.NotifyCanExecuteChanged();
            PreviousPageCommand.NotifyCanExecuteChanged();
            InspectSelectionCommand.NotifyCanExecuteChanged();
            BackToListCommand.NotifyCanExecuteChanged();
            NotifyActions();
        }

        private void ApplyFilter()
        {
            List<MachineryMachine> filtered = [.. m_allMachines.ToList()
                .Where(machine => machine.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    ReferenceEquals(machine, SelectedMachine))];
            for (int index = Machines.Count - 1; index >= 0; index--)
            {
                if (!filtered.Contains(Machines[index]))
                {
                    Machines.RemoveAt(index);
                }
            }
            for (int index = 0; index < filtered.Count; index++)
            {
                int existing = Machines.IndexOf(filtered[index]);
                if (existing < 0)
                {
                    Machines.Insert(index, filtered[index]);
                }
                else if (existing != index)
                {
                    Machines.Move(existing, index);
                }
            }
        }

        private static bool IsExpected(Exception error)
        {
            return error is ServiceResultException or ArgumentException or
            InvalidOperationException or IOException or TimeoutException or UnauthorizedAccessException or
            AggregateException;
        }

        [ObservableProperty]
        public partial string Title { get; set; }

        [ObservableProperty]
        public partial bool IsRenaming { get; set; }

        [ObservableProperty]
        public partial string Status { get; set; } = "Connect, then discover machines.";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DiscoverCommand), nameof(RefreshCommand), nameof(NextPageCommand),
            nameof(PreviousPageCommand))]
        public partial bool IsOffline { get; set; } = true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DiscoverCommand))]
        public partial bool IsDiscovering { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(NextPageCommand), nameof(PreviousPageCommand),
            nameof(InspectSelectionCommand))]
        public partial bool IsLoading { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
        public partial bool HasMore { get; set; }

        [ObservableProperty]
        public partial string SearchText { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(NextPageCommand), nameof(PreviousPageCommand))]
        public partial MachineryMachine? SelectedMachine { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsOverview), nameof(ShowOverview))]
        public partial MachinerySectionChoice? SelectedSection { get; set; }

        [ObservableProperty]
        public partial MachineryItem? SelectedItem { get; set; }

        private readonly PluginHost m_host;
        private readonly IMachineryDataSource m_source;
        private readonly SemaphoreSlim m_slots = new(2, 2);
        private readonly HashSet<Task> m_reads = [];
        private ArrayOf<MachineryMachine> m_allMachines;
        private MachineryItem? m_detailSelection;
        private CancellationTokenSource? m_current;
        private MachineryView? m_view;
        private string? m_restoredMachine;
        private long m_version;
        private int m_pageIndex;
        private int m_resetting;
        private bool m_closed;
        private Task? m_disposal;
        private const int PageSize = 32;
        private static int s_number;
    }

    /// <summary>
    /// Read-only navigation intent; it contains no task, permission or session state.
    /// </summary>
    internal sealed record MachineryDocumentState(
        int Version, string? MachineId, MachinerySection Section, string Search);

    [JsonSerializable(typeof(MachineryDocumentState))]
    internal sealed partial class MachineryJsonContext : JsonSerializerContext;
}
