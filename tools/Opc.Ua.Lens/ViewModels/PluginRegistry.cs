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
using System.Threading;
using Opc.Ua;

namespace UaLens.ViewModels;

/// <summary>
/// Metadata + factory entry for a single <see cref="IPlugin"/>
/// kind.  Surfaced via <see cref="PluginRegistry.All"/> to drive the
/// Tabs → New menu and the kind-keyed factory dispatch.
/// </summary>
internal sealed record PluginRegistration
{
    public required string CommandId { get; init; }
    public required PluginKind Kind { get; init; }
    public required ToolGroup Group { get; init; }
    public required string DisplayName { get; init; }
    public required string Glyph { get; init; }
    public required string Description { get; init; }
    public required ToolConnectionScope ConnectionScope { get; init; }

    public string MenuHeader => DisplayName;

    public string GroupName => Group switch
    {
        ToolGroup.ExploreConnect => "Explore / Connect",
        ToolGroup.Observe => "Observe",
        ToolGroup.Administer => "Administer",
        ToolGroup.Diagnose => "Diagnose",
        _ => throw new InvalidOperationException("Unknown tool group.")
    };

    public string ConnectionRequirement => ConnectionScope switch
    {
        ToolConnectionScope.Local => "Available without a primary connection.",
        ToolConnectionScope.Primary => "Configure offline; connect the primary server to run.",
        ToolConnectionScope.Secondary => "Uses a suitable primary session or its own connection.",
        _ => throw new InvalidOperationException("Unknown connection scope.")
    };

    /// <summary>
    /// Optional keyboard shortcut (e.g. <c>"Ctrl+Shift+S"</c>) displayed
    /// next to the plug-in's entry in the Tabs → Add menu and registered
    /// as the actual hot-key.  <c>null</c> for plug-ins without a
    /// shortcut.
    /// </summary>
    public string? InputGesture { get; init; }

    public required Func<PluginHost, IPlugin> Factory { get; init; }
}

/// <summary>
/// Stable catalog groups, independent of menu or toolbar presentation.
/// </summary>
internal enum ToolGroup
{
    ExploreConnect,
    Observe,
    Administer,
    Diagnose
}

/// <summary>
/// Connection needed for live operations, not a restriction on opening the document.
/// </summary>
internal enum ToolConnectionScope
{
    Local,
    Primary,
    Secondary
}

/// <summary>
/// Static registry of all addable tab-application kinds.  The order
/// here is the order in which entries appear in the Tabs → New menu.
/// </summary>
internal static class PluginRegistry
{
    public static ArrayOf<PluginRegistration> All { get; } =
    [
        new PluginRegistration
        {
            CommandId = "tool.open.monitor",
            Kind = PluginKind.Subscription,
            Group = ToolGroup.Observe,
            DisplayName = "Monitor",
            Glyph = "M",
            Description = "Monitor values and events, with tables, charts and subscription settings.",
            ConnectionScope = ToolConnectionScope.Primary,
            InputGesture = "Ctrl+Shift+S",
            Factory = CreateSubscription
        },
        new PluginRegistration
        {
            CommandId = "tool.open.gds-push",
            Kind = PluginKind.GdsPush,
            Group = ToolGroup.Administer,
            DisplayName = "GDS Push",
            Glyph = "GP",
            Description = "Server certificate and trust-list management via the OPC UA GDS Push API.",
            ConnectionScope = ToolConnectionScope.Secondary,
            InputGesture = "Ctrl+Shift+P",
            Factory = host => new UaLens.Plugins.GdsPush.GdsPushPlugin(host)
        },
        new PluginRegistration
        {
            CommandId = "tool.open.gds-management",
            Kind = PluginKind.GdsManagement,
            Group = ToolGroup.Administer,
            DisplayName = "GDS Management",
            Glyph = "GM",
            Description = "Manage registrations, certificate groups and trust lists on a Global Discovery Server.",
            ConnectionScope = ToolConnectionScope.Secondary,
            InputGesture = "Ctrl+Shift+M",
            Factory = host => new UaLens.Plugins.GdsManagement.GdsManagementPlugin(host)
        },
        new PluginRegistration
        {
            CommandId = "tool.open.discovery",
            Kind = PluginKind.GdsDiscovery,
            Group = ToolGroup.ExploreConnect,
            DisplayName = "GDS Discovery",
            Glyph = "D",
            Description = "Discover OPC UA servers via LDS and GDS, plus saved Custom endpoints.",
            ConnectionScope = ToolConnectionScope.Local,
            InputGesture = "Ctrl+Shift+D",
            Factory = host => new UaLens.Plugins.GdsDiscovery.GdsDiscoveryPlugin(host)
        },
        new PluginRegistration
        {
            CommandId = "tool.open.performance",
            Kind = PluginKind.Performance,
            Group = ToolGroup.Diagnose,
            DisplayName = "Performance",
            Glyph = "P",
            Description = "Synthetic write / call benchmarks with throughput and latency percentiles.",
            ConnectionScope = ToolConnectionScope.Primary,
            InputGesture = "Ctrl+Shift+B",
            Factory = host => new UaLens.Plugins.Performance.PerformancePlugin(host)
        },
        new PluginRegistration
        {
            CommandId = "tool.open.events",
            Kind = PluginKind.EventView,
            Group = ToolGroup.Observe,
            DisplayName = "Event View",
            Glyph = "E",
            Description = "Subscribe to event sources and inspect event-field trees alongside the event log.",
            ConnectionScope = ToolConnectionScope.Primary,
            InputGesture = "Ctrl+Shift+V",
            Factory = host => new UaLens.Plugins.EventView.EventViewPlugin(host)
        },
        new PluginRegistration
        {
            CommandId = "tool.open.history",
            Kind = PluginKind.Historian,
            Group = ToolGroup.Observe,
            DisplayName = "Historian",
            Glyph = "H",
            Description = "History read (raw / processed / at-time) and history update for variables.",
            ConnectionScope = ToolConnectionScope.Primary,
            InputGesture = "Ctrl+Shift+H",
            Factory = host => new UaLens.Plugins.Historian.HistorianPlugin(host)
        },
        new PluginRegistration
        {
            CommandId = "tool.open.files",
            Kind = PluginKind.FileSystem,
            Group = ToolGroup.ExploreConnect,
            DisplayName = "File System",
            Glyph = "F",
            Description = "Browse FileType / FileDirectoryType objects like Windows Explorer.",
            ConnectionScope = ToolConnectionScope.Primary,
            InputGesture = "Ctrl+Shift+L",
            Factory = host => new UaLens.Plugins.FileSystem.FileSystemPlugin(host)
        },
        new PluginRegistration
        {
            CommandId = "tool.open.certificates",
            Kind = PluginKind.CertificateManager,
            Group = ToolGroup.Administer,
            DisplayName = "Certificate Manager",
            Glyph = "C",
            Description = "Manage local Application, TrustedPeer, TrustedIssuer and Rejected certificate stores.",
            ConnectionScope = ToolConnectionScope.Local,
            InputGesture = "Ctrl+Shift+C",
            Factory = host => new UaLens.Plugins.CertificateManager.CertificateManagerPlugin(host)
        },
        new PluginRegistration
        {
            CommandId = "tool.open.roles",
            Kind = PluginKind.RoleManagement,
            Group = ToolGroup.Administer,
            DisplayName = "Role Management",
            Glyph = "R",
            Description = "Manage server roles, identities, applications and endpoints per OPC UA Part 18.",
            ConnectionScope = ToolConnectionScope.Primary,
            InputGesture = "Ctrl+Alt+R",
            Factory = host => new UaLens.Plugins.RoleManagement.RoleManagementPlugin(host)
        },
        new PluginRegistration
        {
            CommandId = "tool.open.users",
            Kind = PluginKind.UserManagement,
            Group = ToolGroup.Administer,
            DisplayName = "User Management",
            Glyph = "U",
            Description = "Manage server user accounts (add/modify/remove/change password) per OPC UA Part 18.",
            ConnectionScope = ToolConnectionScope.Primary,
            InputGesture = "Ctrl+Shift+U",
            Factory = host => new UaLens.Plugins.UserManagement.UserManagementPlugin(host)
        },
        new PluginRegistration
        {
            CommandId = "tool.open.subscription-bench",
            Kind = PluginKind.SubscriptionBench,
            Group = ToolGroup.Diagnose,
            DisplayName = "Subscription Bench",
            Glyph = "SB",
            Description = "Scale-test the server's subscription pipeline: fill a subscription with N monitored "
                + "items and observe throughput, CPU/memory, and engine metrics.",
            ConnectionScope = ToolConnectionScope.Primary,
            InputGesture = "Ctrl+Shift+T",
            Factory = host => new UaLens.Plugins.SubscriptionBench.SubscriptionBenchPlugin(host)
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

    private static SubscriptionViewModel CreateSubscription(PluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new SubscriptionViewModel(
            $"Monitor {Interlocked.Increment(ref s_monitorNumber)}",
            adapter: null,
            host.Log);
    }

    private static int s_monitorNumber;
}
