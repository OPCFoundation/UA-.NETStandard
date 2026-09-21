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

namespace Opc.Ua.Server.UserDatabase
{
    /// <summary>
    /// Stores user credentials and their assigned roles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations store configuration flags and descriptions together with credentials. A persistent
    /// store must commit each mutation as one transaction; rejection or failure must leave the live and
    /// persisted records unchanged.
    /// </para>
    /// <para>
    /// <see cref="LinqUserDatabase"/> provides in-memory transactions.
    /// <see cref="JsonUserDatabase"/> also persists each transaction through one atomic file replacement.
    /// </para>
    /// </remarks>
    public interface IUserDatabase
    {
        /// <summary>
        /// Creates a user, or updates an existing user's password and roles.
        /// </summary>
        /// <param name="userName">The non-empty user name.</param>
        /// <param name="password">The non-empty UTF-8 encoded password.</param>
        /// <param name="roles">The roles to assign, replacing any previous assignments.</param>
        /// <returns>
        /// <c>true</c> if a user was created. The built-in stores return <c>false</c> after updating an existing user.
        /// A store may also return <c>false</c> to reject an operation.
        /// </returns>
        /// <remarks>
        /// This legacy method can change an existing record even when it returns <c>false</c>.
        /// Use <see cref="CreateUser(string, ReadOnlySpan{byte}, ArrayOf{Role}, UserConfigurationMask, string)"/>
        /// for create-only behavior with metadata.
        /// </remarks>
        /// <exception cref="ArgumentException">The user name or password is empty.</exception>
        bool CreateUser(string userName, ReadOnlySpan<byte> password, ICollection<Role> roles);

        /// <summary>
        /// Deletes an existing user and its stored credentials and roles.
        /// </summary>
        /// <param name="userName">The non-empty name of the user to delete.</param>
        /// <returns>
        /// <c>true</c> if removed; <c>false</c> if the user was not found or the store rejected deletion.
        /// </returns>
        /// <exception cref="ArgumentException">The user name is empty.</exception>
        bool DeleteUser(string userName);

        /// <summary>
        /// Checks a supplied password against the user's stored credential.
        /// </summary>
        /// <param name="userName">The non-empty user name.</param>
        /// <param name="password">The non-empty UTF-8 encoded password to check.</param>
        /// <returns><c>true</c> if the user exists and the password matches; otherwise <c>false</c>.</returns>
        /// <remarks>
        /// Credential verification alone does not enforce disabled-user or password-change requirements.
        /// The authentication layer must also apply the user's configuration flags.
        /// </remarks>
        /// <exception cref="ArgumentException">The user name or password is empty.</exception>
        bool CheckCredentials(string userName, ReadOnlySpan<byte> password);

        /// <summary>
        /// Returns the roles assigned to the user.
        /// </summary>
        /// <param name="userName">The non-empty name of an existing user.</param>
        /// <returns>The roles assigned to that user.</returns>
        /// <exception cref="ArgumentException">The user name is empty or the user was not found.</exception>
        ICollection<Role> GetUserRoles(string userName);

        /// <summary>
        /// Returns a snapshot of all users stored in the database.
        /// </summary>
        /// <returns>The current user names and any stored configuration flags and descriptions.</returns>
        IReadOnlyList<UserManagementDataType> GetUsers();

        /// <summary>
        /// Changes an existing user's password after verifying the current password.
        /// </summary>
        /// <param name="userName">The non-empty name of an existing user.</param>
        /// <param name="oldPassword">The current non-empty UTF-8 encoded password.</param>
        /// <param name="newPassword">The replacement non-empty UTF-8 encoded password.</param>
        /// <returns>
        /// <c>true</c> if changed; <c>false</c> if the user was not found, the old password did not match,
        /// or the store rejected the change.
        /// </returns>
        /// <remarks>
        /// Implementations must clear
        /// <see cref="UserConfigurationMask.MustChangePassword"/> in the same transaction as the password change.
        /// </remarks>
        /// <exception cref="ArgumentException">The user name or either password is empty.</exception>
        bool ChangePassword(
            string userName,
            ReadOnlySpan<byte> oldPassword,
            ReadOnlySpan<byte> newPassword);

        /// <summary>
        /// Creates a user with credentials, roles, configuration flags, and description in one transaction.
        /// </summary>
        /// <param name="userName">The name of the new user.</param>
        /// <param name="password">The UTF-8 encoded password.</param>
        /// <param name="roles">The roles assigned to the new user.</param>
        /// <param name="userConfiguration">The initial configuration flags.</param>
        /// <param name="description">The initial user description.</param>
        /// <returns>
        /// <c>true</c> if created; <c>false</c> if the user exists or the store rejects creation.
        /// A <c>false</c> result leaves all records unchanged.
        /// </returns>
        /// <remarks>
        /// A persistent store must commit the complete record in one write. A rejected or failed write must
        /// leave both the live record and the persisted record unchanged.
        /// </remarks>
        /// <exception cref="ArgumentException">The user name or password is empty.</exception>
        bool CreateUser(
            string userName,
            ReadOnlySpan<byte> password,
            ArrayOf<Role> roles,
            UserConfigurationMask userConfiguration,
            string description);

        /// <summary>
        /// Resets a user's password, configuration flags, and description in one transaction.
        /// </summary>
        /// <param name="userName">The name of the existing user.</param>
        /// <param name="newPassword">The new UTF-8 encoded password.</param>
        /// <param name="userConfiguration">The configuration flags to store with the new password.</param>
        /// <param name="description">The description to store with the new password.</param>
        /// <returns>
        /// <c>true</c> if committed; <c>false</c> if the user is missing or the store rejects the reset.
        /// </returns>
        /// <remarks>
        /// Preserve the user's identity and roles. On rejection or failure, preserve the actual previous password
        /// verifier and metadata in both memory and persistent storage. Do not delete and recreate the user.
        /// The caller must authorize the administrative reset.
        /// </remarks>
        /// <exception cref="ArgumentException">The user name or new password is empty.</exception>
        bool ResetPassword(
            string userName,
            ReadOnlySpan<byte> newPassword,
            UserConfigurationMask userConfiguration,
            string description);

        /// <summary>
        /// Commits the configuration flags and description for an existing user.
        /// </summary>
        /// <param name="userName">The non-empty name of an existing user.</param>
        /// <param name="userConfiguration">The complete configuration mask to store.</param>
        /// <param name="description">The replacement description.</param>
        /// <returns>
        /// <c>true</c> if committed; <c>false</c> if the user is missing or the store rejects the update.
        /// </returns>
        /// <remarks>
        /// Preserve the user's identity, password verifier, and roles. A rejected or failed write must also
        /// preserve the previous flags and description, both in memory and in persistent storage.
        /// </remarks>
        /// <exception cref="ArgumentException">The user name is empty.</exception>
        bool UpdateUserMetadata(
            string userName,
            UserConfigurationMask userConfiguration,
            string description);
    }
}