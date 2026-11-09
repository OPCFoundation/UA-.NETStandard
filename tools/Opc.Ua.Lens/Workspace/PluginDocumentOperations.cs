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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Plugins.EventView;
using UaLens.Plugins.Gds;
using UaLens.Plugins.GdsDiscovery;
using UaLens.Plugins.GdsManagement;
using UaLens.Plugins.Performance;
using UaLens.Plugins.SubscriptionBench;
using UaLens.Subscriptions;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Workspace;

/// <summary>
/// Existing tool adapters for the document module: configuration, seeding and primary
/// subscription binding. No view construction or protocol implementation lives here.
/// </summary>
internal sealed class PluginDocumentOperations
{
    public PluginDocumentOperations(ConnectionService connection)
    {
        m_connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public DocumentRestore<IPlugin> Create(
        PluginHost host,
        PluginKind kind,
        ToolSeed? seed = null,
        SubscriptionDocumentState? subscription = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        PluginRegistration registration = PluginRegistry.For(kind);
        return new DocumentRestore<IPlugin>(
            () =>
            {
                if (seed?.RegisteredApp is { } registered)
                {
                    host.Workspace.CurrentRegisteredApp = registered;
                }
                if (seed?.DiscoveryEndpoint is { } endpoint)
                {
                    host.Workspace.EndpointUrl = endpoint.EndpointUrl ?? string.Empty;
                }
                return registration.Factory(host);
            },
            async (document, cancellationToken) =>
            {
                if (document is SubscriptionViewModel monitor && subscription is not null)
                {
                    await subscription.ApplyToAsync(monitor, cancellationToken).ConfigureAwait(true);
                }
                PendingSeed pending = m_seeds.GetValue(document, static _ => new PendingSeed());
                if (seed is not null)
                {
                    pending.Add(seed);
                }
                if (seed?.DiscoveryEndpoint is { } discovery)
                {
                    switch (document)
                    {
                        case GdsDiscoveryPlugin browser:
                            browser.LocalMachineUrl = discovery.EndpointUrl ?? string.Empty;
                            await browser.AddCustomEndpointAsync().ConfigureAwait(true);
                            break;
                        case GdsManagementPlugin management:
                            management.EndpointUrl = discovery.EndpointUrl ?? string.Empty;
                            break;
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
            });
    }

    public async Task SynchronizeConnectionAsync(IPlugin document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        PendingSeed pending = m_seeds.GetValue(document, static _ => new PendingSeed());
        if (document is SubscriptionViewModel monitor)
        {
            await SynchronizeSubscriptionAsync(monitor, pending, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await ((IWorkspaceDocument)document)
                .OnConnectionStateChangedAsync(cancellationToken).ConfigureAwait(true);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (document is SubscriptionBenchPlugin bench)
        {
            for (int i = 0; i < pending.BenchNodes.Count;)
            {
                NodeViewModel node = pending.BenchNodes[i];
                if (node.NodeClass != NodeClass.Variable && !m_connection.IsConnected)
                {
                    i++;
                    continue;
                }
                await bench.SeedFromNodeAsync(node.NodeId, node.NodeClass, node.Text).ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
                pending.BenchNodes.RemoveAt(i);
            }
        }
        if (!m_connection.IsConnected)
        {
            return;
        }
        if (document is EventViewPlugin events)
        {
            while (pending.EventSources.Count > 0)
            {
                NodeViewModel source = pending.EventSources[0];
                await events.SeedSourceAsync(source.NodeId, source.Text).ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
                pending.EventSources.RemoveAt(0);
            }
        }
        if (pending.PickPerformanceTarget && document is PerformancePlugin performance
            && performance.PickTargetCommand.CanExecute(null))
        {
            await performance.PickTargetCommand.ExecuteAsync(null).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            pending.PickPerformanceTarget = false;
        }
    }

    public async Task SeedExistingAsync(
        IPlugin document,
        ToolSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(seed);
        m_seeds.GetValue(document, static _ => new PendingSeed()).Add(seed);
        await SynchronizeConnectionAsync(document, cancellationToken).ConfigureAwait(true);
    }

    private async Task SynchronizeSubscriptionAsync(
        SubscriptionViewModel document,
        PendingSeed state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long generation = m_connection.Snapshot.Generation;
        if (document.Adapter is not null
            && (m_connection.CurrentSession is null || state.SubscriptionGeneration != generation))
        {
            await document.DetachAdapterAsync().ConfigureAwait(true);
            state.SubscriptionGeneration = -1;
        }
        if (m_connection.CurrentSession is null)
        {
            return;
        }
        if (document.IsBound)
        {
            return;
        }
        if (!m_connection.IsConnected
            && !await WaitForConnectionAvailabilityAsync(generation, cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        ISubscriptionAdapter adapter = m_connection.CreateAdapter(trackLifetime: false);
        bool attached = false;
        try
        {
            await document.AttachAdapterAsync(adapter, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            state.SubscriptionGeneration = generation;
            attached = true;
        }
        finally
        {
            if (!attached || cancellationToken.IsCancellationRequested || m_connection.CurrentSession is null
                || m_connection.Snapshot.Generation != generation)
            {
                await document.DetachAdapterAsync().ConfigureAwait(true);
                state.SubscriptionGeneration = -1;
            }
        }
    }

    private async Task<bool> WaitForConnectionAvailabilityAsync(long generation, CancellationToken cancellationToken)
    {
        var available = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStateChanged()
        {
            if (m_connection.CurrentSession is null || m_connection.Snapshot.Generation != generation)
            {
                available.TrySetResult(false);
            }
            else if (m_connection.IsConnected)
            {
                available.TrySetResult(true);
            }
        }

        m_connection.StateChanged += OnStateChanged;
        try
        {
            OnStateChanged();
            return await available.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            m_connection.StateChanged -= OnStateChanged;
        }
    }

    private sealed class PendingSeed
    {
        public List<NodeViewModel> BenchNodes { get; } = [];
        public List<NodeViewModel> EventSources { get; } = [];
        public bool PickPerformanceTarget { get; set; }
        public bool WasNotified { get; set; }
        public long SubscriptionGeneration { get; set; } = -1;

        public void Add(ToolSeed seed)
        {
            if (seed.BenchNode is { } bench)
            {
                BenchNodes.Add(bench);
            }
            if (seed.EventSource is { } source)
            {
                EventSources.Add(source);
            }
            PickPerformanceTarget |= seed.PickPerformanceTarget;
        }
    }

    private readonly ConnectionService m_connection;
    private readonly ConditionalWeakTable<IPlugin, PendingSeed> m_seeds = new();
}

/// <summary>
/// Explicit user-requested context for a newly opened tool. These values neither
/// contain credentials nor implicitly authorize a workload or a connection.
/// </summary>
internal sealed record ToolSeed(
    NodeViewModel? EventSource = null,
    bool PickPerformanceTarget = false,
    RegisteredApplicationContext? RegisteredApp = null,
    EndpointDescription? DiscoveryEndpoint = null,
    NodeViewModel? BenchNode = null);

/// <summary>
/// Local monitor intent, used for duplication and legacy import independently of
/// server handles. Applying it does not connect or create a live subscription.
/// </summary>
internal sealed record SubscriptionDocumentState(
    string Title,
    SubscriptionConfig Subscription,
    ArrayOf<MonitoredItemConfig> Items,
    AnimationMode AnimationMode,
    double AnimationTimeScale,
    bool ShowResourceOverlay,
    bool ShowItemStatusGrid = true,
    bool ShowLegend = false,
    bool ShowXAxis = false,
    bool ShowYAxis = false,
    int DisplayModeIndex = 0)
{
    public static SubscriptionDocumentState Capture(SubscriptionViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new SubscriptionDocumentState(
            document.Title,
            document.Subscription,
            [.. document.Items],
            document.AnimationMode,
            document.AnimationTimeScale,
            document.ShowResourceOverlay,
            document.ShowItemStatusGrid,
            document.ShowLegend,
            document.ShowXAxis,
            document.ShowYAxis,
            document.DisplayModeIndex);
    }

    public static SubscriptionDocumentState Import(SessionFile.TabSnapshot tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!Enum.TryParse(tab.AnimationMode, ignoreCase: true, out AnimationMode mode))
        {
            throw new FormatException($"Unknown monitor visualization '{tab.AnimationMode}'.");
        }
        if (!double.IsFinite(tab.PublishingInterval.Milliseconds) || tab.PublishingInterval.Milliseconds < 0
            || !double.IsFinite(tab.AnimationTimeScale) || tab.AnimationTimeScale is < 0.125 or > 8
            || tab.DisplayModeIndex is < 0 or > 6 || tab.Items is null)
        {
            throw new FormatException("The monitor has invalid publishing, visualization or item settings.");
        }
        foreach (SessionFile.ItemSnapshot item in tab.Items)
        {
            if (!double.IsFinite(item.SamplingInterval.Milliseconds) || item.SamplingInterval.Milliseconds < -1
                || !Enum.IsDefined((MonitoringMode)item.MonitoringMode)
                || item.Filter is { } filter && (!Enum.IsDefined(filter.Trigger)
                    || filter.DeadbandType > (uint)DeadbandType.Percent || !double.IsFinite(filter.DeadbandValue)
                    || filter.DeadbandValue < 0
                    || filter.DeadbandType == (uint)DeadbandType.Percent && filter.DeadbandValue > 100))
            {
                throw new FormatException("A monitored item has invalid sampling, mode or filter settings.");
            }
        }
        return new SubscriptionDocumentState(
            tab.Title,
            new SubscriptionConfig
            {
                PublishingInterval = tab.PublishingInterval.ToTimeSpan(),
                LifetimeCount = tab.LifetimeCount,
                KeepAliveCount = tab.KeepAliveCount,
                MaxNotificationsPerPublish = tab.MaxNotificationsPerPublish,
                Priority = tab.Priority,
                PublishingEnabled = tab.PublishingEnabled,
                MinPublishRequestCount = tab.MinPublishRequestCount,
                MaxPublishRequestCount = tab.MaxPublishRequestCount
            },
            [.. tab.Items.Select(item => new MonitoredItemConfig
            {
                DisplayName = item.DisplayName,
                NodeId = NodeId.Parse(item.NodeId),
                AttributeId = item.AttributeId,
                SamplingInterval = item.SamplingInterval.ToTimeSpan(),
                QueueSize = item.QueueSize,
                DiscardOldest = item.DiscardOldest,
                MonitoringMode = (MonitoringMode)item.MonitoringMode,
                IsEvent = item.IsEvent,
                DataChangeFilter = item.Filter is { } filter ? new DataChangeFilter
                {
                    Trigger = filter.Trigger,
                    DeadbandType = filter.DeadbandType,
                    DeadbandValue = filter.DeadbandValue
                } : null
            })],
            mode,
            tab.AnimationTimeScale,
            tab.ShowResourceOverlay,
            tab.ShowItemStatusGrid,
            tab.ShowLegend,
            tab.ShowXAxis,
            tab.ShowYAxis,
            tab.DisplayModeIndex);
    }

    public SessionFile.TabSnapshot Export()
    {
        return new SessionFile.TabSnapshot
        {
            Title = Title,
            PublishingInterval = SessionFile.TimeSpanMs.From(Subscription.PublishingInterval),
            LifetimeCount = Subscription.LifetimeCount,
            KeepAliveCount = Subscription.KeepAliveCount,
            MaxNotificationsPerPublish = Subscription.MaxNotificationsPerPublish,
            Priority = Subscription.Priority,
            PublishingEnabled = Subscription.PublishingEnabled,
            MinPublishRequestCount = Subscription.MinPublishRequestCount,
            MaxPublishRequestCount = Subscription.MaxPublishRequestCount,
            AnimationMode = AnimationMode.ToString(),
            AnimationTimeScale = AnimationTimeScale,
            ShowResourceOverlay = ShowResourceOverlay,
            ShowItemStatusGrid = ShowItemStatusGrid,
            ShowLegend = ShowLegend,
            ShowXAxis = ShowXAxis,
            ShowYAxis = ShowYAxis,
            DisplayModeIndex = DisplayModeIndex,
            Items = Items.ConvertAll(item => new SessionFile.ItemSnapshot
            {
                DisplayName = item.DisplayName,
                NodeId = item.NodeId.ToString(),
                AttributeId = item.AttributeId,
                SamplingInterval = SessionFile.TimeSpanMs.From(item.SamplingInterval),
                QueueSize = item.QueueSize,
                DiscardOldest = item.DiscardOldest,
                MonitoringMode = (byte)item.MonitoringMode,
                IsEvent = item.IsEvent,
                Filter = item.DataChangeFilter is { } filter ? new SessionFile.FilterSnapshot
                {
                    Trigger = filter.Trigger,
                    DeadbandType = filter.DeadbandType,
                    DeadbandValue = filter.DeadbandValue
                } : null
            }).ToList()
        };
    }

    public async Task ApplyToAsync(
        SubscriptionViewModel document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (document.IsBound)
        {
            throw new InvalidOperationException("Local configuration must be applied before binding a subscription.");
        }
        document.Title = Title;
        document.Subscription = Subscription;
        document.AnimationMode = AnimationMode;
        document.AnimationTimeScale = AnimationTimeScale;
        document.ShowResourceOverlay = ShowResourceOverlay;
        document.ShowItemStatusGrid = ShowItemStatusGrid;
        document.ShowLegend = ShowLegend;
        document.ShowXAxis = ShowXAxis;
        document.ShowYAxis = ShowYAxis;
        document.DisplayModeIndex = DisplayModeIndex;
        document.Items.Clear();
        for (int index = 0; index < Items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await document.AddItemCommand.ExecuteAsync(Items[index] with { Id = 0 }).ConfigureAwait(true);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}
