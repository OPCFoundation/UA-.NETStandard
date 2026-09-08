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
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using UaLens.Subscriptions;
using UaLens.ViewModels;

namespace UaLens.Views;

/// <summary>
/// Owns node interaction: selection to inspector, address-space context-menu
/// dispatch, the primary selected-node actions, and every node/document dialog.
/// No protocol state or document ownership lives here; it drives existing models.
/// </summary>
internal sealed class NodeInteractionController
{
    public NodeInteractionController(MainWindow window, MainViewModel viewModel, ILogger log)
    {
        m_window = window ?? throw new ArgumentNullException(nameof(window));
        m_vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        m_log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public void Attach()
    {
        var tree = m_window.RequiredControl<AddressSpaceView>("LiveTree");
        tree.NodeSelected += OnNodeSelected;
        tree.ContextMenuPolicy = new ContextMenuPolicy(m_vm);
        tree.AddItemRequested += async _ => await MonitorSelectedAsync().ConfigureAwait(true);
        tree.AddRecursivelyRequested += async _ => await AddRecursivelyAsync().ConfigureAwait(true);
        tree.CallMethodRequested += async n => await CallMethodAsync(n).ConfigureAwait(true);
        tree.WriteValueRequested += async n => await WriteValueAsync(n).ConfigureAwait(true);
        tree.ReadHistoryRequested += async _ =>
            await m_vm.AddPluginAsync(PluginKind.Historian).ConfigureAwait(true);
        tree.ShowEventsRequested += async n =>
            await m_vm.AddPluginAsync(PluginKind.EventView, seedEventSource: n).ConfigureAwait(true);
        tree.PerfRequested += async _ =>
            await m_vm.AddPluginAsync(PluginKind.Performance, seedPickTarget: true).ConfigureAwait(true);
        tree.AddToBenchRequested += async n =>
            await m_vm.AddPluginAsync(PluginKind.SubscriptionBench, seedBenchNode: n).ConfigureAwait(true);
        tree.ExportValueRequested += async n => await ExportValueAsync(n).ConfigureAwait(true);
        tree.FindByPathRequested += OnFindByPath;
        tree.ViewNodeStateRequested += OnViewNodeState;

        m_window.RequiredControl<Button>("NodeMonitorBtn").Click +=
            async (_, _) => await MonitorSelectedAsync().ConfigureAwait(true);
        m_window.RequiredControl<Button>("NodeWriteBtn").Click +=
            async (_, _) => await WriteSelectedAsync().ConfigureAwait(true);
        m_window.RequiredControl<Button>("NodeCallBtn").Click +=
            async (_, _) => await CallSelectedAsync().ConfigureAwait(true);
        m_window.RequiredControl<Button>("NodeEventsBtn").Click +=
            async (_, _) => await ShowEventsSelectedAsync().ConfigureAwait(true);
        m_window.RequiredControl<Button>("NodeHistoryBtn").Click +=
            async (_, _) => await m_vm.AddPluginAsync(PluginKind.Historian).ConfigureAwait(true);
        m_window.RequiredControl<Button>("NodeInspectBtn").Click +=
            (_, _) => m_vm.CycleAttributesPanelMode();

        m_window.RequiredControl<MenuItem>("MenuExport").Click +=
            async (_, _) => await ExportNodeSetAsync().ConfigureAwait(true);
        m_window.RequiredControl<MenuItem>("MenuExportTab").Click +=
            async (_, _) => await ExportDocumentDataAsync().ConfigureAwait(true);

        m_vm.PropertyChanged += OnViewModelChanged;
        UpdateNodeActions();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CanAddSelectedItem)
            or nameof(MainViewModel.CanWriteVariable) or nameof(MainViewModel.CanCallMethod)
            or nameof(MainViewModel.SelectionHasEvents) or nameof(MainViewModel.IsConnected)
            or nameof(MainViewModel.SelectedNode))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(UpdateNodeActions);
        }
    }

    private void UpdateNodeActions()
    {
        bool connected = m_vm.IsConnected;
        NodeViewModel? node = m_vm.SelectedNode;
        bool isVariable = node is { NodeClass: NodeClass.Variable };

        var history = m_window.RequiredControl<Button>("NodeHistoryBtn");
        history.IsEnabled = connected && isVariable;

        ToolTip.SetTip(m_window.RequiredControl<Button>("NodeMonitorBtn"), m_vm.CanAddSelectedItem
            ? "Add the selected node to a monitor."
            : "Connect, then select a variable or an object that emits events.");
        ToolTip.SetTip(m_window.RequiredControl<Button>("NodeWriteBtn"), m_vm.CanWriteVariable
            ? "Write a value to the selected variable."
            : "Select a writable variable while connected.");
        ToolTip.SetTip(m_window.RequiredControl<Button>("NodeCallBtn"), m_vm.CanCallMethod
            ? "Call the selected method."
            : "Select a method while connected.");
        ToolTip.SetTip(m_window.RequiredControl<Button>("NodeEventsBtn"), m_vm.SelectionHasEvents
            ? "Open an event view for the selected source."
            : "Select an object that emits events while connected.");
        ToolTip.SetTip(history, history.IsEnabled
            ? "Read history for the selected variable."
            : "Select a variable while connected to read its history.");
        ToolTip.SetTip(m_window.RequiredControl<Button>("NodeInspectBtn"),
            "Cycle the contextual inspector (attributes / references).");
    }

    private void OnNodeSelected(NodeViewModel node)
    {
        _ = LoadSelectionAsync(node);
    }

    private async Task LoadSelectionAsync(NodeViewModel node)
    {
        try
        {
            Task attrs = m_vm.Attributes.LoadAsync(node.NodeId, node.NodeClass);
            Task refs = m_vm.References.LoadAsync(node.NodeId, node.NodeClass);
            Task sel = m_vm.UpdateSelectionAsync(node);
            await Task.WhenAll(attrs, refs, sel).ConfigureAwait(true);
        }
        catch (Exception error) when (error is ServiceResultException or OperationCanceledException)
        {
            MainWindowLog.NodeSelectionFailed(m_log, node.NodeId, error);
        }
    }

    private async Task<SubscriptionViewModel?> EnsureMonitorAsync()
    {
        SubscriptionViewModel? tab = m_vm.SelectedSubscriptionTab;
        if (tab is not null)
        {
            return tab;
        }
        await m_vm.AddTabCommand.ExecuteAsync(null).ConfigureAwait(true);
        return m_vm.SelectedSubscriptionTab;
    }

    private async Task MonitorSelectedAsync()
    {
        if (!m_vm.IsConnected || !m_vm.CanAddSelectedItem || m_vm.SelectedNode is not { } node)
        {
            return;
        }
        SubscriptionViewModel? tab = await EnsureMonitorAsync().ConfigureAwait(true);
        if (tab is null)
        {
            return;
        }
        if (node.NodeClass == NodeClass.Variable)
        {
            var dlg = new AddItemDialog(node, isEvent: false);
            MonitoredItemConfig? r = await dlg.ShowDialog<MonitoredItemConfig?>(m_window).ConfigureAwait(true);
            if (r is not null)
            {
                await tab.AddItemCommand.ExecuteAsync(r).ConfigureAwait(true);
            }
            return;
        }
        if (node.NodeClass != NodeClass.Object)
        {
            return;
        }
        if (m_vm.SelectionHasEvents && !m_vm.SelectionHasVariables)
        {
            var dlg = new AddItemDialog(node, isEvent: true);
            MonitoredItemConfig? r = await dlg.ShowDialog<MonitoredItemConfig?>(m_window).ConfigureAwait(true);
            if (r is not null)
            {
                await tab.AddItemCommand.ExecuteAsync(r).ConfigureAwait(true);
            }
            return;
        }
        if (m_vm.SelectionHasVariables && !m_vm.SelectionHasEvents)
        {
            var dlg = new AddItemDialog(node, isEvent: false);
            MonitoredItemConfig? r = await dlg.ShowDialog<MonitoredItemConfig?>(m_window).ConfigureAwait(true);
            if (r is not null)
            {
                await BulkAddVariablesAsync(tab, r.SamplingInterval).ConfigureAwait(true);
            }
            return;
        }
        if (m_vm.SelectionHasEvents && m_vm.SelectionHasVariables)
        {
            byte? notifier = await m_vm.Browser.GetEventNotifierAsync(node.NodeId).ConfigureAwait(true);
            var dlg = new AddObjectChildrenDialog(node, notifier, m_vm.SelectionVariables.Count);
            ObjectAddDecision? d = await dlg.ShowDialog<ObjectAddDecision?>(m_window).ConfigureAwait(true);
            if (d is not { } dec)
            {
                return;
            }
            var sampling = TimeSpan.FromMilliseconds(dec.SamplingIntervalMs);
            if (dec.Mode is ObjectAddMode.EventsOnly or ObjectAddMode.Both)
            {
                await tab.AddItemCommand.ExecuteAsync(new MonitoredItemConfig
                {
                    DisplayName = "event:" + node.NodeId,
                    NodeId = node.NodeId,
                    AttributeId = Attributes.EventNotifier,
                    SamplingInterval = sampling,
                    QueueSize = 100u,
                    DiscardOldest = true,
                    IsEvent = true,
                    MonitoringMode = MonitoringMode.Reporting
                }).ConfigureAwait(true);
            }
            if (dec.Mode is ObjectAddMode.VariablesOnly or ObjectAddMode.Both)
            {
                await BulkAddVariablesAsync(tab, sampling).ConfigureAwait(true);
            }
        }
    }

    private async Task BulkAddVariablesAsync(SubscriptionViewModel tab, TimeSpan sampling)
    {
        ArrayOf<(NodeId NodeId, string DisplayName)> variables = m_vm.SelectionVariables;
        for (int i = 0; i < variables.Count; i++)
        {
            (NodeId nodeId, string displayName) = variables[i];
            await tab.AddItemCommand.ExecuteAsync(new MonitoredItemConfig
            {
                DisplayName = "value:" + displayName,
                NodeId = nodeId,
                AttributeId = Attributes.Value,
                SamplingInterval = sampling,
                QueueSize = 1u,
                DiscardOldest = true,
                IsEvent = false,
                MonitoringMode = MonitoringMode.Reporting
            }).ConfigureAwait(true);
        }
    }

    private async Task AddRecursivelyAsync()
    {
        if (!m_vm.IsConnected)
        {
            m_vm.ConnectionStatus = "Connect first.";
            return;
        }
        if (m_vm.SelectedNode is not { } selected || m_vm.Connection.Session is not { } session)
        {
            m_vm.ConnectionStatus = "Pick a node in the address space.";
            return;
        }
        SubscriptionViewModel? tab = await EnsureMonitorAsync().ConfigureAwait(true);
        if (tab is null)
        {
            return;
        }
        var dlg = new RecursiveAddDialog($"{selected.NodeId}  ({selected.NodeClass})");
        RecursiveAddOptions? opts = await dlg.ShowDialog<RecursiveAddOptions?>(m_window).ConfigureAwait(true);
        if (opts is null)
        {
            return;
        }
        try
        {
            m_vm.ConnectionStatus = "Browsing subtree…";
            (List<(NodeId NodeId, string DisplayName)> variables,
                List<(NodeId NodeId, string DisplayName)> eventEmitters) = await BrowseSubtreeAsync(
                    session, selected.NodeId, opts.MaxDepth, opts.MaxItems).ConfigureAwait(true);

            int addedVars = 0, addedEvents = 0;
            if (opts.IncludeVariables)
            {
                foreach ((NodeId nodeId, string displayName) in variables)
                {
                    if (addedVars + addedEvents >= opts.MaxItems)
                    {
                        break;
                    }
                    await tab.AddItemCommand.ExecuteAsync(new MonitoredItemConfig
                    {
                        DisplayName = "value:" + displayName,
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        SamplingInterval = opts.SamplingInterval,
                        QueueSize = 1u,
                        DiscardOldest = true,
                        IsEvent = false,
                        MonitoringMode = MonitoringMode.Reporting
                    }).ConfigureAwait(true);
                    addedVars++;
                }
            }
            if (opts.IncludeEvents)
            {
                foreach ((NodeId nodeId, string displayName) in eventEmitters)
                {
                    if (addedVars + addedEvents >= opts.MaxItems)
                    {
                        break;
                    }
                    await tab.AddItemCommand.ExecuteAsync(new MonitoredItemConfig
                    {
                        DisplayName = "event:" + displayName,
                        NodeId = nodeId,
                        AttributeId = Attributes.EventNotifier,
                        SamplingInterval = opts.SamplingInterval,
                        QueueSize = 100u,
                        DiscardOldest = true,
                        IsEvent = true,
                        MonitoringMode = MonitoringMode.Reporting
                    }).ConfigureAwait(true);
                    addedEvents++;
                }
            }
            m_vm.ConnectionStatus = $"Added {addedVars} variables + {addedEvents} events recursively.";
        }
        catch (Exception error) when (error is ServiceResultException or InvalidOperationException)
        {
            m_vm.ConnectionStatus = $"Add recursively failed: {error.Message}";
            MainWindowLog.NodeActionFailed(m_log, "add-recursively", error);
        }
    }

    private async Task WriteSelectedAsync()
    {
        if (m_vm.SelectedNode is { } node)
        {
            await WriteValueAsync(node).ConfigureAwait(true);
        }
    }

    private async Task CallSelectedAsync()
    {
        if (m_vm.SelectedNode is { } node)
        {
            await CallMethodAsync(node).ConfigureAwait(true);
        }
    }

    private async Task ShowEventsSelectedAsync()
    {
        if (m_vm.SelectedNode is { } node && m_vm.SelectionHasEvents)
        {
            await m_vm.AddPluginAsync(PluginKind.EventView, seedEventSource: node).ConfigureAwait(true);
        }
    }

    private async Task CallMethodAsync(NodeViewModel node)
    {
        if (m_vm.Connection.Session is not { } session)
        {
            return;
        }
        var dlg = new MethodCallDialog(node, session);
        await dlg.ShowDialog(m_window).ConfigureAwait(true);
    }

    private async Task WriteValueAsync(NodeViewModel node)
    {
        if (m_vm.Connection.Session is not { } session)
        {
            return;
        }
        var dlg = new WriteValueDialog(node, session);
        await dlg.ShowDialog(m_window).ConfigureAwait(true);
    }

    private async Task ExportValueAsync(NodeViewModel node)
    {
        if (m_vm.Connection.Session is not { } session)
        {
            m_vm.ConnectionStatus = "Connect to export a value.";
            return;
        }
        try
        {
            ArrayOf<ReadValueId> ids =
            [
                new ReadValueId { NodeId = node.NodeId, AttributeId = Attributes.Value }
            ];
            ReadResponse rr = await session
                .ReadAsync(null, 0, TimestampsToReturn.Both, ids, CancellationToken.None)
                .ConfigureAwait(true);
            if (rr.Results.Count == 0)
            {
                m_vm.ConnectionStatus = "Read returned no rows.";
                return;
            }
            DataValue dv = rr.Results[0];
            string safeName = SanitiseFileName(node.Text);
            (IStorageFile? file, UaLens.Connection.EncodingFormat fmt) =
                await EncodedValueIO.SaveAsync(m_window, safeName).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }
            byte[] bytes = UaLens.Connection.DataValueCodec.EncodeDataValue(dv, fmt, session.MessageContext);
            System.IO.Stream output = await file.OpenWriteAsync().ConfigureAwait(true);
            await using (output.ConfigureAwait(true))
            {
                await output.WriteAsync(bytes).ConfigureAwait(true);
            }
            m_vm.ConnectionStatus = $"Exported value to {file.Name} ({fmt}).";
        }
        catch (Exception error) when (error is ServiceResultException or System.IO.IOException
            or UnauthorizedAccessException or InvalidOperationException)
        {
            m_vm.ConnectionStatus = $"Export failed: {error.Message}";
            MainWindowLog.NodeActionFailed(m_log, "export-value", error);
        }
    }

    private void OnFindByPath(NodeViewModel? node)
    {
        var dlg = new FindNodeDialog(m_vm.Browser, node?.NodeId);
        dlg.Show(m_window);
    }

    private void OnViewNodeState(NodeViewModel node)
    {
        var dlg = new ViewNodeStateDialog(m_vm.Browser, m_vm.Connection, node.NodeId);
        dlg.Show(m_window);
    }

    private async Task ExportNodeSetAsync()
    {
        if (m_vm.Connection.Session is not { } session)
        {
            m_vm.ConnectionStatus = "Connect to a server before exporting NodeSet2.";
            return;
        }
        try
        {
            var nsDlg = new NodeSetExportDialog(session.NamespaceUris);
            IReadOnlyList<string>? picked =
                await nsDlg.ShowDialog<IReadOnlyList<string>?>(m_window).ConfigureAwait(true);
            if (picked is null)
            {
                return;
            }
            IStorageFile? file = await m_window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export server address space as NodeSet2",
                DefaultExtension = "xml",
                SuggestedFileName = $"nodeset2-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xml",
                FileTypeChoices = [new FilePickerFileType("OPC UA NodeSet2 XML") { Patterns = s_xmlPatterns }]
            }).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }
            string path = file.Path.LocalPath;
            m_vm.ConnectionStatus = $"Exporting NodeSet2 to {System.IO.Path.GetFileName(path)}…";
            var exporter = new UaLens.Connection.NodeSetExporter(m_vm.Telemetry);
            int count = await exporter.ExportAsync(session, path, namespaceFilter: picked).ConfigureAwait(true);
            m_vm.ConnectionStatus = $"Exported {count} nodes to {System.IO.Path.GetFileName(path)}.";
        }
        catch (Exception error) when (error is ServiceResultException or System.IO.IOException
            or UnauthorizedAccessException or InvalidOperationException)
        {
            m_vm.ConnectionStatus = $"Export failed: {error.Message}";
            MainWindowLog.NodeActionFailed(m_log, "export-nodeset", error);
        }
    }

    private async Task ExportDocumentDataAsync()
    {
        if (m_vm.SelectedTab is not SubscriptionViewModel tab)
        {
            m_vm.ConnectionStatus = "Select a monitor document to export.";
            return;
        }
        try
        {
            IStorageFile? file = await m_window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export document notification history",
                DefaultExtension = "csv",
                SuggestedFileName = $"document-{tab.Title}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv",
                FileTypeChoices =
                [
                    new FilePickerFileType("CSV") { Patterns = s_csvPatterns },
                    new FilePickerFileType("JSON") { Patterns = s_jsonPatterns }
                ]
            }).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }
            string path = file.Path.LocalPath;
            var names = new Dictionary<int, string>();
            foreach (MonitoredItemConfig item in tab.Items)
            {
                names[item.Id] = item.DisplayName ?? string.Empty;
            }
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                await tab.Recorder.ExportJsonAsync(path, names).ConfigureAwait(true);
            }
            else
            {
                await tab.Recorder.ExportCsvAsync(path, names).ConfigureAwait(true);
            }
            m_vm.ConnectionStatus = $"Exported document data to {System.IO.Path.GetFileName(path)}.";
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException
            or InvalidOperationException)
        {
            m_vm.ConnectionStatus = $"Export document data failed: {error.Message}";
            MainWindowLog.NodeActionFailed(m_log, "export-document", error);
        }
    }

    private static string SanitiseFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "value";
        }
        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        return sb.ToString();
    }

    private static async Task<(
        List<(NodeId NodeId, string DisplayName)> Variables,
        List<(NodeId NodeId, string DisplayName)> EventEmitters)>
        BrowseSubtreeAsync(Opc.Ua.Client.ManagedSession session, NodeId startNode, int maxDepth, int maxItems)
    {
        var variables = new List<(NodeId, string)>();
        var objectCandidates = new List<(NodeId NodeId, string Name)>();
        var visited = new HashSet<ExpandedNodeId>();
        var frontier = new ArrayOf<ExpandedNodeId>(
            new[] { NodeId.ToExpandedNodeId(startNode, session.NamespaceUris) });
        ArrayOf<NodeId> hierRefs = new(new[] { ReferenceTypeIds.HierarchicalReferences });

        for (int depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
        {
            ArrayOf<INode> hits = await session.NodeCache
                .FindReferencesAsync(frontier, hierRefs, isInverse: false, includeSubtypes: true)
                .ConfigureAwait(true);
            var next = new List<ExpandedNodeId>();
            foreach (INode n in hits)
            {
                if (!visited.Add(n.NodeId))
                {
                    continue;
                }
                if (variables.Count + objectCandidates.Count >= maxItems)
                {
                    break;
                }
                NodeId localId = ExpandedNodeId.ToNodeId(n.NodeId, session.NamespaceUris);
                string name = !n.DisplayName.IsNull && !string.IsNullOrEmpty(n.DisplayName.Text)
                    ? n.DisplayName.Text!
                    : (n.BrowseName.Name ?? localId.ToString() ?? string.Empty);
                switch (n.NodeClass)
                {
                    case NodeClass.Variable:
                        variables.Add((localId, name));
                        next.Add(n.NodeId);
                        break;
                    case NodeClass.Object:
                    case NodeClass.View:
                        objectCandidates.Add((localId, name));
                        next.Add(n.NodeId);
                        break;
                    case NodeClass.ObjectType:
                    case NodeClass.VariableType:
                        next.Add(n.NodeId);
                        break;
                }
            }
            if (variables.Count + objectCandidates.Count >= maxItems)
            {
                break;
            }
            frontier = new ArrayOf<ExpandedNodeId>(next.ToArray());
        }

        List<(NodeId, string)> eventEmitters =
            await FilterEventEmittersAsync(session, objectCandidates).ConfigureAwait(true);
        return (variables, eventEmitters);
    }

    private static async Task<List<(NodeId NodeId, string DisplayName)>> FilterEventEmittersAsync(
        Opc.Ua.Client.ManagedSession session, List<(NodeId NodeId, string Name)> candidates)
    {
        var emitters = new List<(NodeId, string)>();
        if (candidates.Count == 0)
        {
            return emitters;
        }
        var rvids = new ReadValueId[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            rvids[i] = new ReadValueId { NodeId = candidates[i].NodeId, AttributeId = Attributes.EventNotifier };
        }
        var ids = new ArrayOf<ReadValueId>(rvids);
        ReadResponse resp = await session.ReadAsync(
            null, 0, TimestampsToReturn.Neither, ids, CancellationToken.None).ConfigureAwait(true);
        int n = Math.Min(resp.Results.Count, candidates.Count);
        for (int i = 0; i < n; i++)
        {
            DataValue dv = resp.Results[i];
            if (StatusCode.IsBad(dv.StatusCode) || !dv.WrappedValue.TryGetValue(out byte mask))
            {
                continue;
            }
            if ((mask & EventNotifiers.SubscribeToEvents) != 0)
            {
                emitters.Add(candidates[i]);
            }
        }
        return emitters;
    }

    private static readonly string[] s_csvPatterns = ["*.csv"];
    private static readonly string[] s_jsonPatterns = ["*.json"];
    private static readonly string[] s_xmlPatterns = ["*.xml"];

    private readonly MainWindow m_window;
    private readonly MainViewModel m_vm;
    private readonly ILogger m_log;
}

/// <summary>
/// Bridges <see cref="MainViewModel"/> selection flags to the address-space
/// context-menu's per-entry availability, keeping <see cref="AddressSpaceView"/>
/// decoupled from the main view-model.
/// </summary>
internal sealed class ContextMenuPolicy : IContextMenuPolicy
{
    public ContextMenuPolicy(MainViewModel viewModel)
    {
        m_vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public ContextMenuVisibility Inspect(NodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return NodeActionRules.Evaluate(
            m_vm.IsConnected,
            node.NodeClass,
            m_vm.CanAddSelectedItem,
            m_vm.CanCallMethod,
            m_vm.CanWriteVariable,
            m_vm.SelectionHasEvents);
    }

    private readonly MainViewModel m_vm;
}
