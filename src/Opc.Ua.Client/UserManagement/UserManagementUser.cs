// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Client.UserManagement
{
    /// <summary>
    /// Immutable snapshot of a user as exposed by the standard
    /// <c>ServerConfiguration.UserManagement.Users</c> array per
    /// OPC UA Part 18 §5.2 (User Management). Returned from
    /// <see cref="IUserManagementClient.ListUsersAsync"/>.
    /// </summary>
    /// <remarks>
    /// Wraps the <see cref="UserManagementDataType"/> structure with
    /// convenience accessors that decompose
    /// <see cref="UserConfiguration"/> into the four boolean flags
    /// defined by <see cref="UserConfigurationMask"/>.
    /// </remarks>
    public sealed class UserManagementUser
    {
        /// <summary>
        /// Initialises the snapshot.
        /// </summary>
        public UserManagementUser(
            string userName,
            UserConfigurationMask userConfiguration,
            string? description)
        {
            UserName = userName;
            UserConfiguration = userConfiguration;
            Description = description;
        }

        /// <summary>
        /// The user's logon name.
        /// </summary>
        public string UserName { get; }

        /// <summary>
        /// Raw <see cref="UserConfigurationMask"/> from the server.
        /// Use <see cref="IsDisabled"/> / <see cref="MustChangePassword"/> /
        /// <see cref="NoDelete"/> / <see cref="NoChangeByUser"/> for
        /// individual flags.
        /// </summary>
        public UserConfigurationMask UserConfiguration { get; }

        /// <summary>
        /// Optional free-form description.
        /// </summary>
        public string? Description { get; }

        /// <summary>
        /// True if the <c>NoDelete</c> flag is set.
        /// </summary>
        public bool NoDelete
            => (UserConfiguration & UserConfigurationMask.NoDelete) != 0;

        /// <summary>
        /// True if the <c>Disabled</c> flag is set (the user account is inactive).
        /// </summary>
        public bool IsDisabled
            => (UserConfiguration & UserConfigurationMask.Disabled) != 0;

        /// <summary>
        /// Inverse of <see cref="IsDisabled"/>.
        /// </summary>
        public bool IsActive => !IsDisabled;

        /// <summary>
        /// True if the <c>NoChangeByUser</c> flag is set (user cannot change own password).
        /// </summary>
        public bool NoChangeByUser
            => (UserConfiguration & UserConfigurationMask.NoChangeByUser) != 0;

        /// <summary>
        /// True if the <c>MustChangePassword</c> flag is set.
        /// </summary>
        public bool MustChangePassword
            => (UserConfiguration & UserConfigurationMask.MustChangePassword) != 0;
    }
}
