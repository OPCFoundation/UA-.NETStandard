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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.UserManagement
{
    /// <summary>
    /// Strongly-typed client over the OPC UA Part 18 §5.2 user-management
    /// methods on the standard
    /// <c>ServerConfiguration.UserManagement</c> object (NodeId
    /// <c>i=24290</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wraps the source-generated
    /// <c>Opc.Ua.UserManagementTypeClient</c> proxy emitted by
    /// <c>Opc.Ua.SourceGeneration</c> for every standard
    /// <c>ObjectType</c> in the core NodeSet, providing the typed
    /// <c>AddUser</c> / <c>ModifyUser</c> / <c>RemoveUser</c> /
    /// <c>ChangePassword</c> wrappers plus an ergonomic
    /// <c>ListUsersAsync</c> helper that reads the
    /// <c>Users</c> property.
    /// </para>
    /// <para>
    /// All mutator operations require the calling session to hold the
    /// <c>SecurityAdmin</c> role and to use a <c>SignAndEncrypt</c>
    /// secure channel; the only exception is
    /// <see cref="ChangePasswordAsync"/>, which may also be called as
    /// the currently-authenticated user (Part 18 §5.2.6).
    /// </para>
    /// </remarks>
    public interface IUserManagementClient
    {
        /// <summary>
        /// Reads the <c>Users</c> property and returns a snapshot of
        /// every user known to the server.
        /// </summary>
        ValueTask<IReadOnlyList<UserManagementUser>> ListUsersAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Invokes <c>AddUser</c> per OPC UA Part 18 §5.2.3.
        /// </summary>
        /// <param name="userName">The user's logon name.</param>
        /// <param name="password">The user's initial password.</param>
        /// <param name="userConfiguration">The user's configuration flags (default 0).</param>
        /// <param name="description">Optional description; pass <c>null</c> for none.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask AddUserAsync(
            string userName,
            string password,
            UserConfigurationMask userConfiguration = 0,
            string? description = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Invokes <c>ModifyUser</c> per OPC UA Part 18 §5.2.4. Pass
        /// <c>null</c> for any of <paramref name="newPassword"/>,
        /// <paramref name="userConfiguration"/>, or
        /// <paramref name="description"/> to leave the corresponding
        /// field unchanged on the server.
        /// </summary>
        ValueTask ModifyUserAsync(
            string userName,
            string? newPassword = null,
            UserConfigurationMask? userConfiguration = null,
            string? description = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Invokes <c>RemoveUser</c> per OPC UA Part 18 §5.2.5.
        /// </summary>
        ValueTask RemoveUserAsync(
            string userName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Invokes <c>ChangePassword</c> per OPC UA Part 18 §5.2.6.
        /// The call is dispatched against the currently-authenticated
        /// user; the server resolves the user-name from the session.
        /// </summary>
        ValueTask ChangePasswordAsync(
            string oldPassword,
            string newPassword,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the standard <c>PasswordLength</c> property (allowed
        /// <see cref="Range"/> of password lengths).
        /// </summary>
        ValueTask<Range> ReadPasswordLengthAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the standard <c>PasswordOptions</c> property
        /// (<see cref="PasswordOptionsMask"/> of supported features).
        /// </summary>
        ValueTask<PasswordOptionsMask> ReadPasswordOptionsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the optional <c>PasswordRestrictions</c> property
        /// (free-form human-readable description). Returns
        /// <c>null</c> when the server does not expose the property.
        /// </summary>
        ValueTask<LocalizedText?> ReadPasswordRestrictionsAsync(
            CancellationToken cancellationToken = default);
    }
}
