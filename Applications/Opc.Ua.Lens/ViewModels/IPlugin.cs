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
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace UaLens.ViewModels;

/// <summary>
/// The kind of "sub-application" hosted in a tab.  Each value has a
/// corresponding <see cref="PluginRegistration"/> entry in
/// <see cref="PluginRegistry.All"/> that provides the factory + display
/// metadata for the Tabs → New menu.
/// </summary>
internal enum PluginKind
{
    Subscription,
    GdsPush,
    GdsManagement,
    GdsDiscovery,
    Performance,
    EventView,
    Historian,
    FileSystem,
    CertificateManager,
    RoleManagement,
    UserManagement,
    SubscriptionBench
}

/// <summary>
/// Common contract for everything that can be hosted as a tab in the
/// main window's tab strip.  Each tab owns its own <see cref="View"/>
/// (the body) and optionally a <see cref="HeaderToolbar"/> (a strip of
/// controls rendered above the chart panel), surfaces a single
/// <see cref="Status"/> line for the bottom of the window, and may
/// contribute top-level menus that are dynamically injected whenever
/// at least one tab of that kind is open.
/// </summary>
internal interface IPlugin : INotifyPropertyChanged, IAsyncDisposable
{
    /// <summary>The tab's kind, drives menu grouping + factory dispatch.</summary>
    PluginKind Kind { get; }

    /// <summary>Editable tab header text.  Setting raises PropertyChanged.</summary>
    string Title { get; set; }

    /// <summary>
    /// True while the user is editing this tab's title inline.  Drives a
    /// TextBlock/TextBox visual swap in the TabStrip DataTemplate.
    /// </summary>
    bool IsRenaming { get; set; }

    /// <summary>
    /// The main body view for this tab, hosted in the right pane.  Return
    /// <c>null</c> to fall back to the shared Subscription chart panel
    /// (currently the only kind that does this — non-Subscription kinds
    /// must return a non-null view).
    /// </summary>
    Control? View { get; }

    /// <summary>
    /// Optional per-app toolbar rendered above the body and the tab strip
    /// (e.g. for Subscription this hosts the View combo, ± scale, and
    /// Add/Remove/Settings buttons).  Returns null when this kind reuses
    /// the shared Subscription toolbar or has no toolbar.
    /// </summary>
    Control? HeaderToolbar { get; }

    /// <summary>Single-line status text for the bottom of the right pane.</summary>
    string Status { get; }

    /// <summary>
    /// True if this kind supports the Tabs → Duplicate Active Tab
    /// command.  Greys the menu entry out when false.
    /// </summary>
    bool SupportsDuplicate { get; }

    /// <summary>
    /// Top-level menu items this app contributes when at least one tab
    /// of its kind is active.  Called once when the FIRST tab of the
    /// kind appears; the resulting MenuItems are inserted into the main
    /// menu and removed when the LAST tab of the kind closes.  Actions
    /// inside the menu target the currently-selected tab of the same
    /// kind (or grey out when the active selected tab is a different
    /// kind).
    /// </summary>
    IReadOnlyList<MenuItem> ContributeMenuItems();

    /// <summary>Called when this tab becomes the active selection.</summary>
    void OnActivated();

    /// <summary>Called when this tab is no longer the active selection.</summary>
    void OnDeactivated();
}
