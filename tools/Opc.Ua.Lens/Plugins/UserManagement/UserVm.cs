// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Client.UserManagement;

namespace UaLens.Plugins.UserManagement;

/// <summary>
/// Immutable row view-model wrapping a single
/// <see cref="UserManagementUser"/> snapshot for display in the
/// <see cref="UserManagementPlugin"/>'s DataGrid.  Keeps the raw
/// <see cref="UserConfigurationMask"/> available so the Modify dialog
/// can seed the existing flag state when editing the user.
/// </summary>
internal sealed class UserVm
{
    public UserVm(UserManagementUser user)
    {
        UserName = user.UserName;
        Description = user.Description ?? string.Empty;
        IsActive = user.IsActive;
        MustChangePassword = user.MustChangePassword;
        NoDelete = user.NoDelete;
        NoChangeByUser = user.NoChangeByUser;
        UserConfiguration = user.UserConfiguration;
    }

    /// <summary>The user's logon name.</summary>
    public string UserName { get; }

    /// <summary>Optional free-form description from the server.</summary>
    public string Description { get; }

    /// <summary>True when the account is enabled (inverse of the Disabled flag).</summary>
    public bool IsActive { get; }

    /// <summary>True if the MustChangePassword flag is set.</summary>
    public bool MustChangePassword { get; }

    /// <summary>True if the NoDelete flag is set.</summary>
    public bool NoDelete { get; }

    /// <summary>True if the NoChangeByUser flag is set.</summary>
    public bool NoChangeByUser { get; }

    /// <summary>Raw configuration mask exactly as returned by the server.</summary>
    public UserConfigurationMask UserConfiguration { get; }

    /// <summary>Human-readable account state ("active" / "disabled").</summary>
    public string StateText => IsActive ? "active" : "disabled";

    /// <summary>
    /// Compact comma-separated list of every non-Disabled flag that is
    /// set, suitable for table display ("MustChangePassword, NoDelete").
    /// Returns an empty string when no flag is set.
    /// </summary>
    public string FlagsText
    {
        get
        {
            var parts = new List<string>(3);
            if (MustChangePassword)
            {
                parts.Add("MustChangePassword");
            }
            if (NoDelete)
            {
                parts.Add("NoDelete");
            }
            if (NoChangeByUser)
            {
                parts.Add("NoChangeByUser");
            }
            return string.Join(", ", parts);
        }
    }
}
