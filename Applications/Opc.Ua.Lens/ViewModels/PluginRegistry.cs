/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace UaLens.ViewModels;

/// <summary>
/// Metadata + factory entry for a single <see cref="IPlugin"/>
/// kind.  Surfaced via <see cref="PluginRegistry.All"/> to drive the
/// Tabs → New menu and the kind-keyed factory dispatch.
/// </summary>
internal sealed class PluginRegistration
{
    public required PluginKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public required string Glyph { get; init; }
    public required string Description { get; init; }
    public required string MenuHeader { get; init; }

    public required Func<MainViewModel, IPlugin> Factory { get; init; }
}

/// <summary>
/// Static registry of all addable tab-application kinds.  The order
/// here is the order in which entries appear in the Tabs → New menu.
/// </summary>
internal static class PluginRegistry
{
    public static IReadOnlyList<PluginRegistration> All { get; } =
    [
        new PluginRegistration
        {
            Kind = PluginKind.Subscription,
            DisplayName = "Subscription",
            Glyph = "⚡",
            Description = "Live notification view for OPC UA subscriptions: monitored items, animation canvas, signal/histogram/heatmap modes.",
            MenuHeader = "Subscri_ption",
            Factory = _ => throw new System.NotImplementedException(
                "Subscription factory wired by MainViewModel in Phase 2.")
        },
        new PluginRegistration
        {
            Kind = PluginKind.GdsPush,
            DisplayName = "GDS Push",
            Glyph = "🛡",
            Description = "Server certificate and trust-list management via the OPC UA GDS Push API.",
            MenuHeader = "GDS _Push",
            Factory = vm => new UaLens.Plugins.GdsPush.GdsPushPlugin(vm.CreatePluginHost())
        },
        new PluginRegistration
        {
            Kind = PluginKind.GdsManagement,
            DisplayName = "GDS Management",
            Glyph = "🏛",
            Description = "Connect to a Global Discovery Server to manage server registrations, certificate groups, and trust lists.",
            MenuHeader = "GDS _Management",
            Factory = vm => new UaLens.Plugins.GdsManagement.GdsManagementPlugin(vm.CreatePluginHost())
        },
        new PluginRegistration
        {
            Kind = PluginKind.Performance,
            DisplayName = "Performance",
            Glyph = "📊",
            Description = "Synthetic write / call benchmarks against the connected session — throughput and latency percentiles.",
            MenuHeader = "_Performance",
            Factory = vm => new UaLens.Plugins.Performance.PerformancePlugin(vm.CreatePluginHost())
        },
        new PluginRegistration
        {
            Kind = PluginKind.EventView,
            DisplayName = "Event View",
            Glyph = "🔔",
            Description = "Rich event-details viewer — subscribes to event sources and shows event-field trees alongside the event log.",
            MenuHeader = "E_vents",
            Factory = vm => new UaLens.Plugins.EventView.EventViewPlugin(vm.CreatePluginHost())
        },
        new PluginRegistration
        {
            Kind = PluginKind.Historian,
            DisplayName = "Historian",
            Glyph = "📈",
            Description = "History read (raw / processed / at-time) and history update for variables.",
            MenuHeader = "_Historian",
            Factory = vm => new UaLens.Plugins.Historian.HistorianPlugin(vm.CreatePluginHost())
        },
        new PluginRegistration
        {
            Kind = PluginKind.FileSystem,
            DisplayName = "File System",
            Glyph = "📁",
            Description = "Browse FileType / FileDirectoryType objects like Windows Explorer.",
            MenuHeader = "_File System",
            Factory = _ => new StubPlugin(PluginKind.FileSystem)
        }
    ];

    public static PluginRegistration For(PluginKind kind)
    {
        foreach (PluginRegistration r in All)
        {
            if (r.Kind == kind)
            {
                return r;
            }
        }
        throw new ArgumentOutOfRangeException(nameof(kind), kind, "No registration.");
    }
}
