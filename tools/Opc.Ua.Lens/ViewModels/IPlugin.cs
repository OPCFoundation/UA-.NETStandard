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

using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using UaLens.Workspace;

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
    SubscriptionBench,
    Alarms,
    Models,
    Continuity,
    PubSub,
    Companions
}

/// <summary>
/// Desktop presentation of a workspace document. The workspace exclusively owns
/// lifecycle, cancellation, selection and disposal; a window only presents it.
/// </summary>
internal interface IPlugin : IWorkspaceDocument, INotifyPropertyChanged
{
    /// <summary>
    /// The document kind, used for catalog metadata and factory dispatch.
    /// </summary>
    PluginKind Kind { get; }

    /// <summary>
    /// True while the user is editing this tab's title inline.  Drives a
    /// TextBlock/TextBox visual swap in the TabStrip DataTemplate.
    /// </summary>
    bool IsRenaming { get; set; }

    /// <summary>
    /// The document-owned body, including the normal subscription document view.
    /// A nonvisual adapter may return null; the shell must not supply a shared
    /// subscription renderer or become a notification-stream owner.
    /// </summary>
    Control? View { get; }

    /// <summary>
    /// Optional document actions rendered above the body. Returns null when
    /// actions are embedded in the document view or no separate toolbar is needed.
    /// </summary>
    Control? HeaderToolbar { get; }

    /// <summary>
    /// Single-line status text for the bottom of the document.
    /// </summary>
    string Status { get; }

    /// <summary>
    /// True if this kind supports the Tabs → Duplicate Active Tab
    /// command.  Greys the menu entry out when false.
    /// </summary>
    bool SupportsDuplicate { get; }

    /// <summary>
    /// Transitional adapter for existing tool menus. New shared actions belong in
    /// the command registry rather than another permanent tool-command framework.
    /// </summary>
    IReadOnlyList<MenuItem> ContributeMenuItems();

    /// <summary>
    /// Transitional synchronous notification for tools whose connection hook only
    /// refreshes local state. Tools doing asynchronous work implement
    /// <see cref="IWorkspaceDocument.OnConnectionStateChangedAsync"/> instead.
    /// </summary>
    void OnConnectionStateChanged()
    {
    }

    Task IWorkspaceDocument.OnConnectionStateChangedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OnConnectionStateChanged();
        return Task.CompletedTask;
    }
}
