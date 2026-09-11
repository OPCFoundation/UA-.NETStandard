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
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client.UserManagement;
using UaLens.ViewModels;

namespace UaLens.Plugins.UserManagement;

/// <summary>
/// Tab-hosted plug-in that wraps
/// <see cref="UserManagementClient"/> to administer the connected
/// server's user accounts via Part 18 §5.2 (User Management). Surfaces
/// Add / Modify / Remove / Change-password commands on a DataGrid of
/// every user reported by the server, plus a read-out of the server's
/// password restrictions (length range + free-form description).
/// </summary>
/// <remarks>
/// Auto-refreshes the user list whenever the host's
/// <see cref="UaLens.Connection.ConnectionService.Session"/> changes
/// to a non-<c>null</c> value (re-connect, reconnect after drop, etc.).
/// All mutator commands require <c>SecurityAdmin</c> + a
/// <c>SignAndEncrypt</c> channel on the server side — the plug-in
/// surfaces server-reported errors via the status line.
/// </remarks>
internal sealed partial class UserManagementPlugin : ObservableObject, IPlugin
{
    private static int s_nextNumber;

    private readonly PluginHost m_host;
    private readonly ILogger m_log;
    private UserManagementView? m_view;

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private string m_status = "● Not connected";

    [ObservableProperty]
    private UserVm? m_selectedUser;

    [ObservableProperty]
    private string m_passwordRestrictionsText = "(unknown — connect and refresh)";

    /// <summary>
    /// Users currently reported by the server.
    /// </summary>
    public ObservableCollection<UserVm> Users { get; } = new();

    public UserManagementPlugin(PluginHost host)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_log = host.Log;
        int n = Interlocked.Increment(ref s_nextNumber);
        m_title = string.Create(CultureInfo.InvariantCulture, $"User Management {n}");

        // Session-dependent initialization runs in OnConnectionStateChangedAsync,
        // which the workspace delivers on open and on every connection transition.
        // The document owns no fire-and-forget work in its constructor.
    }

    public PluginKind Kind => PluginKind.UserManagement;

    Control? IPlugin.View => m_view ??= new UserManagementView { DataContext = this };

    Control? IPlugin.HeaderToolbar => null;

    public bool SupportsDuplicate => false;

    public void OnActivated() { }
    public void OnDeactivated() { }

    public IReadOnlyList<MenuItem> ContributeMenuItems()
    {
        // The Modify / Remove commands fall back to SelectedUser when
        // their parameter is null, so the menu entries can simply omit
        // an explicit CommandParameter and still target whatever row is
        // currently selected.
        return [
            CreateMenuItem("_Refresh",            RefreshCommand),
            CreateMenuItem("_Add User…",          AddUserCommand),
            CreateMenuItem("_Modify User…",       ModifyUserCommand),
            CreateMenuItem("Re_move User",        RemoveUserCommand),
            CreateMenuItem("_Change Password…",   ChangePasswordCommand),
        ];
    }

    private static MenuItem CreateMenuItem(string header, System.Windows.Input.ICommand cmd)
        => new() { Header = header, Command = cmd };

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Refreshes the grid whenever the host's connection state changes. The
    /// workspace awaits this on open and on every transition, and cancels it on
    /// disconnect or close, so no fire-and-forget dispatch is required here.
    /// </summary>
    public async Task OnConnectionStateChangedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (m_host.Connection.CurrentSession is null)
        {
            Users.Clear();
            SelectedUser = null;
            PasswordRestrictionsText = "(unknown — connect and refresh)";
            Status = "● Not connected";
            return;
        }
        await RefreshCoreAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Read the server's user list and password-restrictions text,
    /// repopulating <see cref="Users"/> and
    /// <see cref="PasswordRestrictionsText"/>.
    /// </summary>
    [RelayCommand]
    public Task RefreshAsync() => RefreshCoreAsync(CancellationToken.None);

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        UserManagementClient? client = TryCreateClient();
        if (client is null)
        {
            return;
        }

        try
        {
            ArrayOf<UserManagementUser> users = await client
                .ListUsersAsync(cancellationToken)
                .ConfigureAwait(true);

            LocalizedText restrictions = LocalizedText.Null;
            try
            {
                restrictions = await client
                    .ReadPasswordRestrictionsAsync(cancellationToken)
                    .ConfigureAwait(true) ?? LocalizedText.Null;
            }
            catch (Exception ex)
            {
                // Optional property — not fatal.
                m_log.UserReadPasswordRestrictionsSkipped(ex, Title);
            }

            string restrictionsText = !restrictions.IsNullOrEmpty
                ? restrictions.Text ?? string.Empty
                : "(server did not expose PasswordRestrictions)";

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Users.Clear();
                foreach (UserManagementUser u in users)
                {
                    Users.Add(new UserVm(u));
                }
                PasswordRestrictionsText = restrictionsText;
                Status = string.Format(CultureInfo.InvariantCulture,
                    "● {0} user(s)", Users.Count);
            });
        }
        catch (Exception ex)
        {
            m_log.UserRefreshFailed(ex, Title);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Status = $"● Refresh failed: {ex.Message}";
            });
        }
    }

    /// <summary>
    /// Show <see cref="AddUserDialog"/> and dispatch AddUser on OK.
    /// </summary>
    [RelayCommand]
    public async Task AddUserAsync()
    {
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }
        UserManagementClient? client = TryCreateClient();
        if (client is null)
        {
            return;
        }

        var dlg = new AddUserDialog();
        object? result = await dlg.ShowDialog<object?>(owner).ConfigureAwait(true);
        if (result is not AddUserDialogResult r)
        {
            return;
        }

        try
        {
            await client.AddUserAsync(
                r.UserName, r.Password, r.Config, r.Description,
                CancellationToken.None).ConfigureAwait(true);
            m_log.UserAddSucceeded(Title, r.UserName);
            await RefreshAsync().ConfigureAwait(true);
            Status = $"● Added user '{r.UserName}'.";
        }
        catch (Exception ex)
        {
            m_log.UserAddFailed(ex, Title);
            Status = $"● Add user failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Show <see cref="ModifyUserDialog"/> for the given user and dispatch ModifyUser.
    /// </summary>
    [RelayCommand]
    public async Task ModifyUserAsync(UserVm? user)
    {
        UserVm? target = user ?? SelectedUser;
        if (target is null)
        {
            Status = "● Pick a user first.";
            return;
        }
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }
        UserManagementClient? client = TryCreateClient();
        if (client is null)
        {
            return;
        }

        var dlg = new ModifyUserDialog(target.UserName, target.UserConfiguration);
        object? result = await dlg.ShowDialog<object?>(owner).ConfigureAwait(true);
        if (result is not ModifyUserDialogResult r)
        {
            return;
        }

        try
        {
            await client.ModifyUserAsync(
                target.UserName,
                newPassword: r.NewPassword,
                userConfiguration: r.Config,
                description: r.Description,
                CancellationToken.None).ConfigureAwait(true);
            m_log.UserModifySucceeded(Title, target.UserName);
            await RefreshAsync().ConfigureAwait(true);
            Status = $"● Modified user '{target.UserName}'.";
        }
        catch (Exception ex)
        {
            m_log.UserModifyFailed(ex, Title);
            Status = $"● Modify user failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Confirm with the user, then dispatch RemoveUser for the given
    /// account (or the currently-selected one when none is supplied).
    /// </summary>
    [RelayCommand]
    public async Task RemoveUserAsync(UserVm? user)
    {
        UserVm? target = user ?? SelectedUser;
        if (target is null)
        {
            Status = "● Pick a user first.";
            return;
        }
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }
        UserManagementClient? client = TryCreateClient();
        if (client is null)
        {
            return;
        }

        bool confirmed = await ConfirmAsync(owner,
            "Remove user",
            $"Remove user '{target.UserName}' from the server?\n\nThis cannot be undone.")
            .ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        try
        {
            await client.RemoveUserAsync(target.UserName, CancellationToken.None)
                .ConfigureAwait(true);
            m_log.UserRemoveSucceeded(Title, target.UserName);
            await RefreshAsync().ConfigureAwait(true);
            Status = $"● Removed user '{target.UserName}'.";
        }
        catch (Exception ex)
        {
            m_log.UserRemoveFailed(ex, Title);
            Status = $"● Remove user failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Show <see cref="ChangePasswordDialog"/> and dispatch
    /// ChangePassword for the currently-authenticated session user.
    /// </summary>
    [RelayCommand]
    public async Task ChangePasswordAsync()
    {
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }
        UserManagementClient? client = TryCreateClient();
        if (client is null)
        {
            return;
        }

        var dlg = new ChangePasswordDialog();
        object? result = await dlg.ShowDialog<object?>(owner).ConfigureAwait(true);
        if (result is not ChangePasswordDialogResult r)
        {
            return;
        }

        try
        {
            await client.ChangePasswordAsync(r.OldPassword, r.NewPassword,
                CancellationToken.None).ConfigureAwait(false);
            m_log.UserChangePasswordSucceeded(Title);
            Status = "● Password changed for current session user.";
        }
        catch (Exception ex)
        {
            m_log.UserChangePasswordFailed(ex, Title);
            Status = $"● Change password failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Build a <see cref="UserManagementClient"/> bound to the current
    /// session, or return <c>null</c> + update <see cref="Status"/> when
    /// the host has no live session.
    /// </summary>
    private UserManagementClient? TryCreateClient()
    {
        var session = m_host.Connection.CurrentSession;
        if (session is null)
        {
            Status = "● Not connected — connect a session first.";
            return null;
        }
        try
        {
            return new UserManagementClient(session);
        }
        catch (Exception ex)
        {
            m_log.UserClientConstructionFailed(ex, Title);
            Status = $"● Client construction failed: {ex.Message}";
            return null;
        }
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

    /// <summary>
    /// Show a tiny "Yes / Cancel" confirmation popup themed against
    /// the app's dynamic resources.  Returns true on Yes, false on
    /// Cancel / close.
    /// </summary>
    private static async Task<bool> ConfirmAsync(Window owner, string title, string message)
    {
        var yes = new Button
        {
            Content = "Yes",
            IsDefault = true,
            Width = 100
        };
        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            Width = 100,
            Margin = new Avalonia.Thickness(8, 0, 0, 0)
        };
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 12, 0, 0),
            Children = { yes, cancel }
        };
        var body = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        var grid = new DockPanel
        {
            Margin = new Avalonia.Thickness(16),
            LastChildFill = true
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        grid.Children.Add(buttons);
        grid.Children.Add(body);

        var window = new Window
        {
            Title = title,
            Width = 420,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = grid
        };

        // Theme via DynamicResource so the confirm popup respects the
        // active theme (light / dark).
        if (Avalonia.Application.Current?.FindResource("AppBg") is Avalonia.Media.IBrush bg)
        {
            window.Background = bg;
        }
        if (Avalonia.Application.Current?.FindResource("TextPrimary") is Avalonia.Media.IBrush fg)
        {
            window.Foreground = fg;
        }

        yes.Click += (_, _) => window.Close(true);
        cancel.Click += (_, _) => window.Close(false);

        object? result = await window.ShowDialog<object?>(owner).ConfigureAwait(true);
        return result is bool b && b;
    }
}
