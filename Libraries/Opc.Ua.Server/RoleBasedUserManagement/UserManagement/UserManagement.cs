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
using System.Linq;
using System.Text;
using System.Threading;
using Opc.Ua.Server.UserDatabase;

namespace Opc.Ua.Server.UserManagement
{
    /// <summary>
    /// Default in-memory <see cref="IUserManagement"/> implementation. Wraps
    /// an <see cref="IUserDatabase"/> for credential persistence and keeps
    /// the per-user <see cref="UserConfigurationMask"/> and description in
    /// memory.
    /// </summary>
    /// <remarks>
    /// Per Part 18 §6.4 "the management of these Users is server-specific" —
    /// this default keeps the metadata in memory across the server lifetime.
    /// Integrators that need persistence of the metadata implement
    /// <see cref="IUserManagement"/> directly and inject the instance via
    /// <see cref="IServerInternal.SetUserManagement"/>.
    /// </remarks>
    public sealed class UserManagement : IUserManagement, IDisposable
    {
        private readonly IUserDatabase m_userDatabase;

        private readonly Dictionary<string, UserMetadata> m_metadata
            = new(StringComparer.Ordinal);

        private readonly ReaderWriterLockSlim m_lock = new(LockRecursionPolicy.NoRecursion);
        private bool m_disposed;

        /// <summary>
        /// Creates a new <see cref="UserManagement"/> backed by
        /// <paramref name="userDatabase"/>.
        /// </summary>
        /// <param name="userDatabase">The user-credential persistence store.</param>
        /// <param name="passwordLength">
        /// Allowed password length range; defaults to {Low = 8, High = 256}.
        /// </param>
        /// <param name="passwordOptions">
        /// Password requirements / supported features advertised to clients.
        /// Defaults to <see cref="PasswordOptionsMask.SupportDisableUser"/>
        /// plus <see cref="PasswordOptionsMask.SupportInitialPasswordChange"/>
        /// plus <see cref="PasswordOptionsMask.SupportDescriptionForUser"/>.
        /// </param>
        /// <param name="passwordRestrictions">
        /// Optional human-readable description of password restrictions.
        /// </param>
        public UserManagement(
            IUserDatabase userDatabase,
            Range? passwordLength = null,
            PasswordOptionsMask? passwordOptions = null,
            LocalizedText? passwordRestrictions = null)
        {
            m_userDatabase = userDatabase ?? throw new ArgumentNullException(nameof(userDatabase));
            PasswordLength = passwordLength ?? new Range { Low = 8, High = 256 };
            PasswordOptions = passwordOptions
                ?? PasswordOptionsMask.SupportDisableUser |
                    PasswordOptionsMask.SupportInitialPasswordChange |
                    PasswordOptionsMask.SupportDescriptionForUser;
            PasswordRestrictions = passwordRestrictions;
        }

        /// <inheritdoc/>
        public Range PasswordLength { get; }

        /// <inheritdoc/>
        public PasswordOptionsMask PasswordOptions { get; }

        /// <inheritdoc/>
        public LocalizedText? PasswordRestrictions { get; }

        /// <inheritdoc/>
        public event EventHandler<UserDeactivatedEventArgs>? UserDeactivated;

        /// <inheritdoc/>
        public IReadOnlyList<UserManagementDataType> SnapshotUsers()
        {
            m_lock.EnterReadLock();
            try
            {
                var list = new List<UserManagementDataType>(m_metadata.Count);
                foreach (KeyValuePair<string, UserMetadata> kv in m_metadata)
                {
                    list.Add(new UserManagementDataType
                    {
                        UserName = kv.Key,
                        UserConfiguration = (uint)kv.Value.Configuration,
                        Description = kv.Value.Description
                    });
                }
                return list;
            }
            finally
            {
                m_lock.ExitReadLock();
            }
        }

        /// <inheritdoc/>
        public ServiceResult AddUser(
            string userName,
            string password,
            UserConfigurationMask userConfiguration,
            string description)
        {
            if (string.IsNullOrEmpty(userName))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument,
                    new LocalizedText("UserName must be non-empty."));
            }

            ServiceResult configValidation = ValidateConfigurationFlags(userConfiguration);
            if (ServiceResult.IsBad(configValidation))
            {
                return configValidation;
            }

            ServiceResult passwordValidation = ValidatePassword(password);
            if (ServiceResult.IsBad(passwordValidation))
            {
                return passwordValidation;
            }

            m_lock.EnterWriteLock();
            try
            {
                if (m_metadata.ContainsKey(userName))
                {
                    return new ServiceResult(StatusCodes.BadAlreadyExists,
                        new LocalizedText($"User '{userName}' already exists."));
                }
                if (!m_userDatabase.CreateUser(userName, GetPasswordBytes(password), []))
                {
                    return new ServiceResult(StatusCodes.BadResourceUnavailable,
                        new LocalizedText("User-database rejected the create operation."));
                }
                m_metadata[userName] = new UserMetadata(userConfiguration, description ?? string.Empty);
            }
            finally
            {
                m_lock.ExitWriteLock();
            }

            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public ServiceResult ModifyUser(
            string userName,
            bool modifyPassword,
            string password,
            bool modifyUserConfiguration,
            UserConfigurationMask userConfiguration,
            bool modifyDescription,
            string description,
            string? callingUserName)
        {
            if (string.IsNullOrEmpty(userName))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            bool willDisable = false;
            bool disabled;

            m_lock.EnterWriteLock();
            try
            {
                if (!m_metadata.TryGetValue(userName, out UserMetadata? existing))
                {
                    return new ServiceResult(StatusCodes.BadNotFound,
                        new LocalizedText($"User '{userName}' not found."));
                }

                UserConfigurationMask effectiveConfig = modifyUserConfiguration
                    ? userConfiguration
                    : existing.Configuration;

                if (modifyUserConfiguration)
                {
                    ServiceResult configValidation = ValidateConfigurationFlags(userConfiguration);
                    if (ServiceResult.IsBad(configValidation))
                    {
                        return configValidation;
                    }

                    willDisable = (existing.Configuration & UserConfigurationMask.Disabled) == 0 &&
                        (userConfiguration & UserConfigurationMask.Disabled) != 0;

                    if (willDisable &&
                        callingUserName != null &&
                        string.Equals(callingUserName, userName, StringComparison.Ordinal))
                    {
                        return new ServiceResult(StatusCodes.BadInvalidSelfReference,
                            new LocalizedText("The user to disable is the calling session's user."));
                    }
                }

                if (modifyPassword)
                {
                    ServiceResult passwordValidation = ValidatePassword(password);
                    if (ServiceResult.IsBad(passwordValidation))
                    {
                        return passwordValidation;
                    }

                    // The IUserDatabase API only exposes ChangePassword which
                    // requires the old password — admin password resets are
                    // emulated by delete + recreate to keep the abstraction
                    // stable. The previously assigned roles are snapshotted
                    // and re-applied to preserve role assignments across the
                    // reset (Part 18 §5.2.6 does not mandate role removal on
                    // password change).
                    //
                    // KNOWN LIMITATION (see Docs/RoleBasedUserManagement.md):
                    // this two-step is not atomic — if CreateUser fails after
                    // DeleteUser succeeds, the user account is lost. The
                    // built-in LinqUserDatabase / JsonUserDatabase delete and
                    // create operations succeed unconditionally so this is
                    // only a concern for custom IUserDatabase implementations
                    // that may fail mid-reset.
                    ICollection<Role> preservedRoles = SnapshotUserRolesSafe(userName);
                    if (!m_userDatabase.DeleteUser(userName))
                    {
                        return new ServiceResult(StatusCodes.BadResourceUnavailable,
                            new LocalizedText("User-database rejected the delete during password reset."));
                    }
                    if (!m_userDatabase.CreateUser(userName, GetPasswordBytes(password), preservedRoles))
                    {
                        // User has been deleted but recreate failed — surface
                        // a clear error. There is no way to recover the
                        // original password from IUserDatabase.
                        m_metadata.Remove(userName);
                        return new ServiceResult(StatusCodes.BadResourceUnavailable,
                            new LocalizedText(
                                "User-database rejected the create during password reset; " +
                                "the user account has been removed."));
                    }
                }

                disabled = (effectiveConfig & UserConfigurationMask.Disabled) != 0;
                m_metadata[userName] = new UserMetadata(
                    effectiveConfig,
                    modifyDescription ? description ?? string.Empty : existing.Description);
            }
            finally
            {
                m_lock.ExitWriteLock();
            }

            if (willDisable && disabled)
            {
                RaiseUserDeactivated(userName);
            }
            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public ServiceResult RemoveUser(string userName, string? callingUserName)
        {
            if (string.IsNullOrEmpty(userName))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            if (callingUserName != null &&
                string.Equals(callingUserName, userName, StringComparison.Ordinal))
            {
                return new ServiceResult(StatusCodes.BadInvalidSelfReference,
                    new LocalizedText("The user to remove is the calling session's user."));
            }

            m_lock.EnterWriteLock();
            try
            {
                if (!m_metadata.TryGetValue(userName, out UserMetadata? existing))
                {
                    return new ServiceResult(StatusCodes.BadNotFound);
                }
                if ((existing.Configuration & UserConfigurationMask.NoDelete) != 0)
                {
                    return new ServiceResult(StatusCodes.BadNotSupported,
                        new LocalizedText($"User '{userName}' is marked NoDelete."));
                }
                if (!m_userDatabase.DeleteUser(userName))
                {
                    return new ServiceResult(StatusCodes.BadResourceUnavailable);
                }
                m_metadata.Remove(userName);
            }
            finally
            {
                m_lock.ExitWriteLock();
            }

            RaiseUserDeactivated(userName);
            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public ServiceResult ChangePassword(string userName, string oldPassword, string newPassword)
        {
            if (string.IsNullOrEmpty(userName))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            ServiceResult passwordValidation = ValidatePassword(newPassword);
            if (ServiceResult.IsBad(passwordValidation))
            {
                return passwordValidation;
            }

            if (string.Equals(oldPassword, newPassword, StringComparison.Ordinal))
            {
                return new ServiceResult(StatusCodes.BadAlreadyExists,
                    new LocalizedText("New password matches the old password."));
            }

            m_lock.EnterReadLock();
            try
            {
                if (!m_metadata.TryGetValue(userName, out UserMetadata? metadata))
                {
                    return new ServiceResult(StatusCodes.BadNotFound);
                }
                if ((metadata.Configuration & UserConfigurationMask.NoChangeByUser) != 0)
                {
                    return new ServiceResult(StatusCodes.BadNotSupported,
                        new LocalizedText($"User '{userName}' is marked NoChangeByUser."));
                }
            }
            finally
            {
                m_lock.ExitReadLock();
            }

            if (!m_userDatabase.ChangePassword(userName,
                    GetPasswordBytes(oldPassword), GetPasswordBytes(newPassword)))
            {
                return new ServiceResult(StatusCodes.BadIdentityTokenInvalid,
                    new LocalizedText("Old password does not match."));
            }

            m_lock.EnterWriteLock();
            try
            {
                if (m_metadata.TryGetValue(userName, out UserMetadata? current))
                {
                    // Successful change clears the MustChangePassword bit.
                    m_metadata[userName] = new UserMetadata(
                        current.Configuration & ~UserConfigurationMask.MustChangePassword,
                        current.Description);
                }
            }
            finally
            {
                m_lock.ExitWriteLock();
            }

            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public bool MustChangePassword(string userName)
        {
            if (string.IsNullOrEmpty(userName))
            {
                return false;
            }
            m_lock.EnterReadLock();
            try
            {
                return m_metadata.TryGetValue(userName, out UserMetadata? meta) &&
                    (meta.Configuration & UserConfigurationMask.MustChangePassword) != 0;
            }
            finally
            {
                m_lock.ExitReadLock();
            }
        }

        /// <inheritdoc/>
        public bool IsUserActive(string userName)
        {
            if (string.IsNullOrEmpty(userName))
            {
                return false;
            }
            m_lock.EnterReadLock();
            try
            {
                return m_metadata.TryGetValue(userName, out UserMetadata? meta) &&
                    (meta.Configuration & UserConfigurationMask.Disabled) == 0;
            }
            finally
            {
                m_lock.ExitReadLock();
            }
        }

        private ServiceResult ValidatePassword(string password)
        {
            password ??= string.Empty;
            int low = (int)PasswordLength.Low;
            int high = (int)PasswordLength.High;
            if (low > 0 && password.Length < low)
            {
                return new ServiceResult(StatusCodes.BadOutOfRange,
                    new LocalizedText($"Password must be at least {low} characters long."));
            }
            if (high > 0 && password.Length > high)
            {
                return new ServiceResult(StatusCodes.BadOutOfRange,
                    new LocalizedText($"Password must be at most {high} characters long."));
            }

            if ((PasswordOptions & PasswordOptionsMask.RequiresUpperCaseCharacters) != 0 &&
                !password.Any(char.IsUpper))
            {
                return new ServiceResult(StatusCodes.BadOutOfRange,
                    new LocalizedText("Password must contain at least one upper-case character."));
            }
            if ((PasswordOptions & PasswordOptionsMask.RequiresLowerCaseCharacters) != 0 &&
                !password.Any(char.IsLower))
            {
                return new ServiceResult(StatusCodes.BadOutOfRange,
                    new LocalizedText("Password must contain at least one lower-case character."));
            }
            if ((PasswordOptions & PasswordOptionsMask.RequiresDigitCharacters) != 0 &&
                !password.Any(char.IsDigit))
            {
                return new ServiceResult(StatusCodes.BadOutOfRange,
                    new LocalizedText("Password must contain at least one digit."));
            }
            if ((PasswordOptions & PasswordOptionsMask.RequiresSpecialCharacters) != 0 &&
                password.All(char.IsLetterOrDigit))
            {
                return new ServiceResult(StatusCodes.BadOutOfRange,
                    new LocalizedText("Password must contain at least one special character."));
            }
            return ServiceResult.Good;
        }

        private ServiceResult ValidateConfigurationFlags(UserConfigurationMask config)
        {
            if ((config & UserConfigurationMask.MustChangePassword) != 0 &&
                (config & UserConfigurationMask.NoChangeByUser) != 0)
            {
                return new ServiceResult(StatusCodes.BadConfigurationError,
                    new LocalizedText("MustChangePassword and NoChangeByUser are mutually exclusive."));
            }

            UserConfigurationMask supported =
                UserConfigurationMask.NoDelete |
                UserConfigurationMask.Disabled |
                UserConfigurationMask.NoChangeByUser |
                UserConfigurationMask.MustChangePassword;
            UserConfigurationMask unsupported = config & ~supported;
            if (unsupported != 0)
            {
                return new ServiceResult(StatusCodes.BadNotSupported,
                    new LocalizedText($"User configuration has unsupported flags: {unsupported}."));
            }

            // Check against this manager's PasswordOptions advertisement.
            if ((config & UserConfigurationMask.MustChangePassword) != 0 &&
                (PasswordOptions & PasswordOptionsMask.SupportInitialPasswordChange) == 0)
            {
                return new ServiceResult(StatusCodes.BadNotSupported,
                    new LocalizedText("Server does not support MustChangePassword."));
            }
            if ((config & UserConfigurationMask.Disabled) != 0 &&
                (PasswordOptions & PasswordOptionsMask.SupportDisableUser) == 0)
            {
                return new ServiceResult(StatusCodes.BadNotSupported,
                    new LocalizedText("Server does not support disabling users."));
            }
            if ((config & UserConfigurationMask.NoDelete) != 0 &&
                (PasswordOptions & PasswordOptionsMask.SupportDisableDeleteForUser) == 0)
            {
                return new ServiceResult(StatusCodes.BadNotSupported,
                    new LocalizedText("Server does not support NoDelete."));
            }
            if ((config & UserConfigurationMask.NoChangeByUser) != 0 &&
                (PasswordOptions & PasswordOptionsMask.SupportNoChangeForUser) == 0)
            {
                return new ServiceResult(StatusCodes.BadNotSupported,
                    new LocalizedText("Server does not support NoChangeByUser."));
            }
            return ServiceResult.Good;
        }

        private void RaiseUserDeactivated(string userName)
        {
            UserDeactivated?.Invoke(this, new UserDeactivatedEventArgs(userName));
        }

        private static byte[] GetPasswordBytes(string password)
        {
            return Encoding.UTF8.GetBytes(password ?? string.Empty);
        }

        /// <summary>
        /// Reads the current set of roles for <paramref name="userName"/>
        /// from the user database, swallowing the <see cref="ArgumentException"/>
        /// some implementations throw when the user is unknown and returning
        /// an empty collection in that case. Used to preserve roles across
        /// admin password resets (see <see cref="ModifyUser"/>).
        /// </summary>
        private ICollection<Role> SnapshotUserRolesSafe(string userName)
        {
            try
            {
                return m_userDatabase.GetUserRoles(userName) ?? [];
            }
            catch (ArgumentException)
            {
                return [];
            }
        }

        private sealed record UserMetadata(UserConfigurationMask Configuration, string Description);

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            m_lock.Dispose();
        }
    }
}
