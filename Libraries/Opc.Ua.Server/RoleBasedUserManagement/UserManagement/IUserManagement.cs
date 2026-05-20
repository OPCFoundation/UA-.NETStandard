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

namespace Opc.Ua.Server.UserManagement
{
    /// <summary>
    /// Event payload raised by <see cref="IUserManagement.UserDeactivated"/>
    /// when a <c>ModifyUser(Disabled=true)</c> or <c>RemoveUser</c> mutator
    /// completes. The binding layer subscribes to this event to close all
    /// active sessions associated with the user per Part 18 §5.2.6 / §5.2.7.
    /// </summary>
    public sealed class UserDeactivatedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes the event payload.
        /// </summary>
        public UserDeactivatedEventArgs(string userName)
        {
            UserName = userName;
        }

        /// <summary>The user that was disabled or removed.</summary>
        public string UserName { get; }
    }

    /// <summary>
    /// Server-side facade over the OPC UA Part 18 §5 user-management model.
    /// Backs the standard <c>ServerConfiguration.UserManagement</c> object
    /// (NodeId i=24290) and is bound to its <c>AddUser</c>, <c>ModifyUser</c>,
    /// <c>RemoveUser</c> and <c>ChangePassword</c> methods by
    /// <see cref="UserManagementBinding"/>.
    /// </summary>
    /// <remarks>
    /// The default implementation <see cref="UserManagement"/> wraps the
    /// existing <see cref="Opc.Ua.Server.UserDatabase.IUserDatabase"/> for
    /// credential persistence and stores the user metadata
    /// (<see cref="Opc.Ua.UserConfigurationMask"/> + description) in memory.
    /// Integrators that need to persist metadata across server restarts can
    /// implement this interface directly and inject the instance via
    /// <see cref="IServerInternal.SetUserManagement"/>.
    /// </remarks>
    public interface IUserManagement
    {
        /// <summary>
        /// Allowed password length per Part 18 §5.2.1 (mandatory).
        /// </summary>
        Range PasswordLength { get; }

        /// <summary>
        /// Password feature requirements per Part 18 §5.2.1 (mandatory).
        /// </summary>
        PasswordOptionsMask PasswordOptions { get; }

        /// <summary>
        /// Optional human-readable password restrictions per Part 18 §5.2.1.
        /// </summary>
        LocalizedText? PasswordRestrictions { get; }

        /// <summary>
        /// Read-only snapshot of all configured users.
        /// </summary>
        IReadOnlyList<UserManagementDataType> SnapshotUsers();

        /// <summary>
        /// Adds a user per Part 18 §5.2.5.
        /// </summary>
        /// <returns>
        /// <c>Good</c> on success;
        /// <c>Bad_AlreadyExists</c> if the user already exists;
        /// <c>Bad_OutOfRange</c> if the password violates <see cref="PasswordLength"/>
        /// or the character-class requirements in <see cref="PasswordOptions"/>;
        /// <c>Bad_NotSupported</c> if <paramref name="userConfiguration"/> has
        /// flags the server does not support;
        /// <c>Bad_ConfigurationError</c> if <paramref name="userConfiguration"/>
        /// combines incompatible flags (e.g. MustChangePassword + NoChangeByUser).
        /// </returns>
        ServiceResult AddUser(
            string userName,
            string password,
            UserConfigurationMask userConfiguration,
            string description);

        /// <summary>
        /// Modifies a user per Part 18 §5.2.6. <paramref name="callingUserName"/>
        /// is the user name of the calling session, used to enforce the
        /// <c>Bad_InvalidSelfReference</c> rule when the modify operation
        /// disables the calling user.
        /// </summary>
        /// <param name="userName">Name of the user to modify.</param>
        /// <param name="modifyPassword">Whether <paramref name="password"/> is meaningful.</param>
        /// <param name="password">New password (ignored when <paramref name="modifyPassword"/> is false).</param>
        /// <param name="modifyUserConfiguration">Whether <paramref name="userConfiguration"/> is meaningful.</param>
        /// <param name="userConfiguration">New configuration mask (ignored when <paramref name="modifyUserConfiguration"/> is false).</param>
        /// <param name="modifyDescription">Whether <paramref name="description"/> is meaningful.</param>
        /// <param name="description">New description (ignored when <paramref name="modifyDescription"/> is false).</param>
        /// <param name="callingUserName">The user name of the calling session, or <c>null</c> for non-user callers.</param>
        ServiceResult ModifyUser(
            string userName,
            bool modifyPassword,
            string password,
            bool modifyUserConfiguration,
            UserConfigurationMask userConfiguration,
            bool modifyDescription,
            string description,
            string? callingUserName);

        /// <summary>
        /// Removes a user per Part 18 §5.2.7. <paramref name="callingUserName"/>
        /// is used to enforce the <c>Bad_InvalidSelfReference</c> rule.
        /// </summary>
        /// <param name="userName">Name of the user to remove.</param>
        /// <param name="callingUserName">The user name of the calling session, or <c>null</c> for non-user callers.</param>
        ServiceResult RemoveUser(string userName, string? callingUserName);

        /// <summary>
        /// Changes a user's password per Part 18 §5.2.8. Self-service only —
        /// the binding ensures <paramref name="userName"/> matches the calling
        /// session's USERNAME identity token.
        /// </summary>
        ServiceResult ChangePassword(string userName, string oldPassword, string newPassword);

        /// <summary>
        /// Returns <c>true</c> if the user exists and has the
        /// <see cref="UserConfigurationMask.MustChangePassword"/> bit set.
        /// Used by the <c>ActivateSession</c> flow to grant only the
        /// <c>Anonymous</c> role until the user changes their password.
        /// </summary>
        bool MustChangePassword(string userName);

        /// <summary>
        /// Returns <c>true</c> if the user exists and is not disabled.
        /// </summary>
        bool IsUserActive(string userName);

        /// <summary>
        /// Raised after a <c>ModifyUser(Disabled=true)</c> or
        /// <c>RemoveUser</c> succeeds so the binding layer can close any
        /// active sessions for that user per Part 18 §5.2.6 / §5.2.7.
        /// </summary>
        event EventHandler<UserDeactivatedEventArgs>? UserDeactivated;
    }
}
