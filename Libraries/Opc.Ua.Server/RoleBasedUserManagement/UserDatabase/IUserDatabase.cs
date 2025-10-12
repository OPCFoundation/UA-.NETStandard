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
    /// An abstract interface to the user database which stores logins with associated roles.
    /// </summary>
    public interface IUserDatabase
    {
        /// <summary>
        /// Create or update user password or roles.
        /// </summary>
        /// <param name="userName">The username</param>
        /// <param name="password">The password</param>
        /// <param name="roles">The roles assigned to the new user</param>
        /// <returns>true if registration was successful</returns>
        bool CreateUser(string userName, ReadOnlySpan<byte> password, ICollection<Role> roles);

        /// <summary>
        /// Delete existing user.
        /// </summary>
        /// <param name="userName">The user to delete</param>
        /// <returns>true if successfully removed.</returns>
        bool DeleteUser(string userName);

        /// <summary>
        /// Checks if the provided user credentials pass for a user.
        /// </summary>
        /// <param name="userName">The username</param>
        /// <param name="password">The password</param>
        /// <returns>true if userName + PW combination is correct.</returns>
        bool CheckCredentials(string userName, ReadOnlySpan<byte> password);

        /// <summary>
        /// Returns the roles assigned to the user.
        /// </summary>
        /// <param name="userName">The username</param>
        /// <returns>the Role of the provided users</returns>
        /// <exception cref="ArgumentException">When the user is not found</exception>
        ICollection<Role> GetUserRoles(string userName);

        /// <summary>
        /// Changes the password of an existing users.
        /// </summary>
        /// <returns>true if change was successfull</returns>
        bool ChangePassword(
            string userName,
            ReadOnlySpan<byte> oldPassword,
            ReadOnlySpan<byte> newPassword);
    }
}
