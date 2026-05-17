/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
            Factory = vm => new UaLens.Plugins.FileSystem.FileSystemPlugin(vm.CreatePluginHost())
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
