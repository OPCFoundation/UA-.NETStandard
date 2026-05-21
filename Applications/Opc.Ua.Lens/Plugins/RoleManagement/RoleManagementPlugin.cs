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
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Roles;
using UaLens.ViewModels;

namespace UaLens.Plugins.RoleManagement;

/// <summary>
/// Tab-hosted role-management plug-in.  Wraps
/// <see cref="RoleManagementClient"/> to surface the connected server's
/// <c>RoleSet</c> in a workbench tab:
/// <list type="bullet">
///   <item>Left pane — list of roles exposed by the server.</item>
///   <item>Right pane — tabbed detail panel showing the selected role's
///   identity mappings, applications, endpoints, and custom-config flag,
///   each editable via per-tab buttons and modal dialogs.</item>
/// </list>
/// The plug-in is connect-aware: it observes
/// <see cref="UaLens.Connection.ConnectionService.StateChanged"/> and
/// auto-refreshes the role list once a live session is available.  All
/// mutator operations require the calling session to hold the
/// <c>SecurityAdmin</c> role and use a SignAndEncrypt secure channel —
/// failures surface as a status-line error rather than crashing the tab.
/// </summary>
internal sealed partial class RoleManagementPlugin : ObservableObject, IPlugin
{
    private static readonly Dictionary<PluginKind, int> s_perKindCounter = new();

    private readonly PluginHost m_host;
    private readonly ILogger m_log;
    private RoleManagementView? m_view;

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private string m_status = "● Not connected";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveRoleCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddIdentityCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddApplicationCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddEndpointCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleApplicationsExcludeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleEndpointsExcludeCommand))]
    private RoleVm? m_selectedRole;

    /// <summary>Roles loaded from the server (left pane).</summary>
    public ObservableCollection<RoleVm> Roles { get; } = new();

    /// <summary>True when the host has a live session we can call against.</summary>
    public bool IsConnected => m_host.Connection.Session is { Connected: true };

    /// <summary>True when a role row is currently selected.</summary>
    public bool HasSelectedRole => SelectedRole is not null;

    public RoleManagementPlugin(PluginHost host)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_log = host.Log;
        int n;
        lock (s_perKindCounter)
        {
            s_perKindCounter.TryGetValue(PluginKind.RoleManagement, out int prev);
            n = prev + 1;
            s_perKindCounter[PluginKind.RoleManagement] = n;
        }
        m_title = string.Create(CultureInfo.InvariantCulture, $"Role Management {n}");

        m_host.Connection.StateChanged += OnConnectionStateChanged;
        // Auto-refresh once if we are already connected at construction time.
        if (IsConnected)
        {
            _ = Dispatcher.UIThread.InvokeAsync(RefreshAsync);
        }
        else
        {
            UpdateStatus();
        }
    }

    // ----- IPlugin -----

    public PluginKind Kind => PluginKind.RoleManagement;

    Control? IPlugin.View => m_view ??= new RoleManagementView { DataContext = this };

    Control? IPlugin.HeaderToolbar => null;

    public bool SupportsDuplicate => false;

    public void OnActivated() { }
    public void OnDeactivated() { }

    public IReadOnlyList<MenuItem> ContributeMenuItems()
    {
        return [
            CreateMenuItem("_Refresh", RefreshCommand),
            CreateMenuItem("_Add Role…", AddRoleCommand),
            CreateMenuItem("Re_move Role", RemoveRoleCommand),
            CreateMenuItem("Add _Identity…", AddIdentityCommand),
            CreateMenuItem("Add A_pplication…", AddApplicationCommand),
            CreateMenuItem("Add _Endpoint…", AddEndpointCommand),
        ];
    }

    private static MenuItem CreateMenuItem(string header, System.Windows.Input.ICommand cmd)
        => new() { Header = header, Command = cmd };

    public ValueTask DisposeAsync()
    {
        try
        {
            m_host.Connection.StateChanged -= OnConnectionStateChanged;
        }
        catch
        {
            // tolerate detach failures
        }
        return ValueTask.CompletedTask;
    }

    // ----- Connection-state plumbing -----

    private void OnConnectionStateChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsConnected));
            UpdateStatus();
            if (IsConnected && Roles.Count == 0)
            {
                _ = RefreshAsync();
            }
            else if (!IsConnected)
            {
                Roles.Clear();
                SelectedRole = null;
            }
        });
    }

    partial void OnSelectedRoleChanged(RoleVm? value)
    {
        OnPropertyChanged(nameof(HasSelectedRole));
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (!IsConnected)
        {
            Status = "● Not connected";
            return;
        }
        string sel = SelectedRole is { } r
            ? string.Format(CultureInfo.InvariantCulture, " · selected: {0}", r.DisplayName)
            : string.Empty;
        Status = string.Format(CultureInfo.InvariantCulture,
            "● {0} role(s){1}", Roles.Count, sel);
    }

    // ----- Client helper -----

    private bool TryGetClient(out RoleManagementClient? client)
    {
        if (m_host.Connection.Session is not { Connected: true } session)
        {
            client = null;
            Status = "● Not connected";
            return false;
        }
        client = new RoleManagementClient(session);
        return true;
    }

    // ----- Commands -----

    /// <summary>Refresh the role list from the server.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!TryGetClient(out RoleManagementClient? client) || client is null)
        {
            return;
        }
        try
        {
            IReadOnlyList<RoleInfo> roles = await client
                .ListRolesAsync()
                .ConfigureAwait(true);
            NodeId? prev = SelectedRole?.RoleId;
            Roles.Clear();
            foreach (RoleInfo info in roles)
            {
                Roles.Add(new RoleVm(info));
            }
            // Re-select previously selected role by NodeId, if it still exists.
            if (prev is NodeId prevId)
            {
                foreach (RoleVm vm in Roles)
                {
                    if (vm.RoleId.Equals(prevId))
                    {
                        SelectedRole = vm;
                        break;
                    }
                }
            }
            UpdateStatus();
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Role Management tab {Title} ListRoles failed.", Title);
            Status = $"● List roles failed: {ex.Message}";
        }
    }

    /// <summary>Prompt the user for a role name + namespace and add it.</summary>
    [RelayCommand]
    public async Task AddRoleAsync()
    {
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }
        if (!TryGetClient(out RoleManagementClient? client) || client is null)
        {
            return;
        }

        AddRoleDialog.Result? result;
        try
        {
            var dlg = new AddRoleDialog();
            result = await dlg.ShowDialog<AddRoleDialog.Result?>(owner).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = $"● Add role dialog failed: {ex.Message}";
            m_log.LogWarning(ex, "Role Management tab {Title} AddRole dialog failed.", Title);
            return;
        }
        if (result is null)
        {
            return;
        }

        try
        {
            NodeId id = await client
                .AddRoleAsync(result.Name, result.NamespaceUri)
                .ConfigureAwait(true);
            m_log.LogInformation("Role Management tab {Title}: added role {Name} → {Id}.",
                Title, result.Name, id);
            await RefreshAsync().ConfigureAwait(true);
            Status = $"● Added role {result.Name}.";
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Role Management tab {Title} AddRole({Name}) failed.",
                Title, result.Name);
            Status = $"● Add role failed: {ex.Message}";
        }
    }

    /// <summary>Remove the selected role after a confirmation prompt.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedRole))]
    public async Task RemoveRoleAsync(RoleVm? r)
    {
        RoleVm? role = r ?? SelectedRole;
        if (role is null)
        {
            return;
        }
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }
        if (!TryGetClient(out RoleManagementClient? client) || client is null)
        {
            return;
        }

        bool? confirmed;
        try
        {
            var confirm = new ConfirmDialog(
                "Remove role",
                string.Format(CultureInfo.InvariantCulture,
                    "Remove role '{0}'?\nThis cannot be undone.", role.DisplayName),
                okText: "Remove");
            confirmed = await confirm.ShowDialog<bool?>(owner).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = $"● Confirm dialog failed: {ex.Message}";
            return;
        }
        if (confirmed is not true)
        {
            return;
        }

        try
        {
            await client.RemoveRoleAsync(role.RoleId).ConfigureAwait(true);
            m_log.LogInformation("Role Management tab {Title}: removed role {Name} ({Id}).",
                Title, role.DisplayName, role.RoleId);
            await RefreshAsync().ConfigureAwait(true);
            Status = $"● Removed role {role.DisplayName}.";
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Role Management tab {Title} RemoveRole({Name}) failed.",
                Title, role.DisplayName);
            Status = $"● Remove role failed: {ex.Message}";
        }
    }

    /// <summary>Show <see cref="AddIdentityDialog"/> and add the chosen mapping to the selected role.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedRole))]
    public async Task AddIdentityAsync()
    {
        RoleVm? role = SelectedRole;
        if (role is null)
        {
            return;
        }
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }
        if (!TryGetClient(out RoleManagementClient? client) || client is null)
        {
            return;
        }

        IdentityMappingRuleType? rule;
        try
        {
            var dlg = new AddIdentityDialog();
            rule = await dlg.ShowDialog<IdentityMappingRuleType?>(owner).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = $"● Add identity dialog failed: {ex.Message}";
            return;
        }
        if (rule is null)
        {
            return;
        }

        try
        {
            await client.AddIdentityAsync(role.RoleId, rule).ConfigureAwait(true);
            await RefreshSelectedRoleAsync(client, role).ConfigureAwait(true);
            Status = $"● Added identity {rule.CriteriaType} → {role.DisplayName}.";
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Role Management tab {Title} AddIdentity failed.", Title);
            Status = $"● Add identity failed: {ex.Message}";
        }
    }

    /// <summary>Remove a specific identity mapping from the selected role.</summary>
    [RelayCommand]
    public async Task RemoveIdentityAsync(IdentityMappingRuleType? r)
    {
        RoleVm? role = SelectedRole;
        if (role is null || r is null)
        {
            return;
        }
        if (!TryGetClient(out RoleManagementClient? client) || client is null)
        {
            return;
        }
        try
        {
            await client.RemoveIdentityAsync(role.RoleId, r).ConfigureAwait(true);
            await RefreshSelectedRoleAsync(client, role).ConfigureAwait(true);
            Status = $"● Removed identity {r.CriteriaType} from {role.DisplayName}.";
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Role Management tab {Title} RemoveIdentity failed.", Title);
            Status = $"● Remove identity failed: {ex.Message}";
        }
    }

    /// <summary>Prompt for an ApplicationUri and add it to the selected role.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedRole))]
    public async Task AddApplicationAsync()
    {
        RoleVm? role = SelectedRole;
        if (role is null)
        {
            return;
        }
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }
        if (!TryGetClient(out RoleManagementClient? client) || client is null)
        {
            return;
        }

        string? appUri;
        try
        {
            var dlg = new AddApplicationUriDialog();
            appUri = await dlg.ShowDialog<string?>(owner).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = $"● Add application dialog failed: {ex.Message}";
            return;
        }
        if (string.IsNullOrWhiteSpace(appUri))
        {
            return;
        }

        try
        {
            await client.AddApplicationAsync(role.RoleId, appUri).ConfigureAwait(true);
            await RefreshSelectedRoleAsync(client, role).ConfigureAwait(true);
            Status = $"● Added application {appUri} → {role.DisplayName}.";
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Role Management tab {Title} AddApplication failed.", Title);
            Status = $"● Add application failed: {ex.Message}";
        }
    }

    /// <summary>Remove a specific ApplicationUri from the selected role.</summary>
    [RelayCommand]
    public async Task RemoveApplicationAsync(string? appUri)
    {
        RoleVm? role = SelectedRole;
        if (role is null || string.IsNullOrEmpty(appUri))
        {
            return;
        }
        if (!TryGetClient(out RoleManagementClient? client) || client is null)
        {
            return;
        }
        try
        {
            await client.RemoveApplicationAsync(role.RoleId, appUri).ConfigureAwait(true);
            await RefreshSelectedRoleAsync(client, role).ConfigureAwait(true);
            Status = $"● Removed application {appUri} from {role.DisplayName}.";
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Role Management tab {Title} RemoveApplication failed.", Title);
            Status = $"● Remove application failed: {ex.Message}";
        }
    }

    /// <summary>Flip the <c>ApplicationsExclude</c> flag on the selected role.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedRole))]
    public async Task ToggleApplicationsExcludeAsync()
    {
        RoleVm? role = SelectedRole;
        if (role is null)
        {
            return;
        }
        if (!TryGetClient(out RoleManagementClient? client) || client is null)
        {
            return;
        }
        bool target = !role.ApplicationsExclude;
        try
        {
            await client.SetApplicationsExcludeAsync(role.RoleId, target).ConfigureAwait(true);
            role.ApplicationsExclude = target;
            Status = $"● ApplicationsExclude = {target} on {role.DisplayName}.";
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Role Management tab {Title} SetApplicationsExclude failed.", Title);
            Status = $"● SetApplicationsExclude failed: {ex.Message}";
        }
    }

    /// <summary>Show <see cref="AddEndpointDialog"/> and add the chosen endpoint to the selected role.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedRole))]
    public async Task AddEndpointAsync()
    {
        RoleVm? role = SelectedRole;
        if (role is null)
        {
            return;
        }
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }
        if (!TryGetClient(out RoleManagementClient? client) || client is null)
        {
            return;
        }

        EndpointType? endpoint;
        try
        {
            var dlg = new AddEndpointDialog();
            endpoint = await dlg.ShowDialog<EndpointType?>(owner).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = $"● Add endpoint dialog failed: {ex.Message}";
            return;
        }
        if (endpoint is null)
        {
            return;
        }

        try
        {
            await client.AddEndpointAsync(role.RoleId, endpoint).ConfigureAwait(true);
            await RefreshSelectedRoleAsync(client, role).ConfigureAwait(true);
            Status = $"● Added endpoint {endpoint.EndpointUrl} → {role.DisplayName}.";
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Role Management tab {Title} AddEndpoint failed.", Title);
            Status = $"● Add endpoint failed: {ex.Message}";
        }
    }

    /// <summary>Remove a specific endpoint from the selected role.</summary>
    [RelayCommand]
    public async Task RemoveEndpointAsync(EndpointType? ep)
    {
        RoleVm? role = SelectedRole;
        if (role is null || ep is null)
        {
            return;
        }
        if (!TryGetClient(out RoleManagementClient? client) || client is null)
        {
            return;
        }
        try
        {
            await client.RemoveEndpointAsync(role.RoleId, ep).ConfigureAwait(true);
            await RefreshSelectedRoleAsync(client, role).ConfigureAwait(true);
            Status = $"● Removed endpoint {ep.EndpointUrl} from {role.DisplayName}.";
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Role Management tab {Title} RemoveEndpoint failed.", Title);
            Status = $"● Remove endpoint failed: {ex.Message}";
        }
    }

    /// <summary>Flip the <c>EndpointsExclude</c> flag on the selected role.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedRole))]
    public async Task ToggleEndpointsExcludeAsync()
    {
        RoleVm? role = SelectedRole;
        if (role is null)
        {
            return;
        }
        if (!TryGetClient(out RoleManagementClient? client) || client is null)
        {
            return;
        }
        bool target = !role.EndpointsExclude;
        try
        {
            await client.SetEndpointsExcludeAsync(role.RoleId, target).ConfigureAwait(true);
            role.EndpointsExclude = target;
            Status = $"● EndpointsExclude = {target} on {role.DisplayName}.";
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Role Management tab {Title} SetEndpointsExclude failed.", Title);
            Status = $"● SetEndpointsExclude failed: {ex.Message}";
        }
    }

    /// <summary>Set the <c>CustomConfiguration</c> flag from the view-model state.</summary>
    [RelayCommand]
    public async Task SetCustomConfigurationAsync(bool value)
    {
        RoleVm? role = SelectedRole;
        if (role is null)
        {
            return;
        }
        if (!TryGetClient(out RoleManagementClient? client) || client is null)
        {
            return;
        }
        try
        {
            await client.SetCustomConfigurationAsync(role.RoleId, value).ConfigureAwait(true);
            role.CustomConfiguration = value;
            Status = $"● CustomConfiguration = {value} on {role.DisplayName}.";
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Role Management tab {Title} SetCustomConfiguration failed.", Title);
            Status = $"● SetCustomConfiguration failed: {ex.Message}";
        }
    }

    // ----- Helpers -----

    private static async Task RefreshSelectedRoleAsync(RoleManagementClient client, RoleVm role)
    {
        RoleInfo info = await client.ReadRoleAsync(role.RoleId).ConfigureAwait(true);
        role.UpdateFrom(info);
    }

    private static Window? GetOwnerWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desk)
        {
            return desk.MainWindow;
        }
        return null;
    }
}
