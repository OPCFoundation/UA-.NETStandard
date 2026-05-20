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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.UserManagement
{
    /// <summary>
    /// Binds the standard <c>ServerConfiguration.UserManagement</c> object
    /// (NodeId i=24290) to an <see cref="IUserManagement"/> implementation
    /// using the source-generated typed proxies (<see cref="UserManagementState"/>,
    /// <see cref="AddUserMethodState"/>, <see cref="ModifyUserMethodState"/>,
    /// <see cref="RemoveUserMethodState"/>, <see cref="ChangePasswordMethodState"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every typed method state's <c>OnCallAsync</c> delegate is wired through
    /// <see cref="RoleAuthorizationGate.CheckAdmin"/> for the admin methods
    /// (<c>AddUser</c>, <c>ModifyUser</c>, <c>RemoveUser</c>) and
    /// <see cref="RoleAuthorizationGate.CheckSelfUserName"/> for
    /// <c>ChangePassword</c> per Part 18 §5.2.
    /// </para>
    /// <para>
    /// On <see cref="IUserManagement.UserDeactivated"/>, the binding closes
    /// every active session for that user via the supplied
    /// <see cref="ISessionManager"/> to satisfy Part 18 §5.2.6 / §5.2.7.
    /// </para>
    /// </remarks>
    public sealed class UserManagementBinding : IDisposable
    {
        private readonly IUserManagement m_userManagement;
        private readonly ISessionManager? m_sessionManager;
        private readonly ILogger m_logger;
        private UserManagementState? m_state;
        private bool m_disposed;

        private UserManagementBinding(
            IUserManagement userManagement,
            ISessionManager? sessionManager,
            ITelemetryContext? telemetry)
        {
            m_userManagement = userManagement;
            m_sessionManager = sessionManager;
            m_logger = telemetry?.CreateLogger<UserManagementBinding>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<UserManagementBinding>.Instance;
        }

        /// <summary>
        /// Resolves the typed <see cref="UserManagementState"/> in the
        /// <paramref name="nodeManager"/>'s predefined nodes, wires every
        /// typed <c>OnCallAsync</c> delegate to <paramref name="userManagement"/>,
        /// keeps the <c>Users</c> property in sync, and closes sessions on
        /// <see cref="IUserManagement.UserDeactivated"/>. Returns the binding
        /// instance so the caller can dispose it on server shutdown, or
        /// <c>null</c> if the standard <c>UserManagement</c> object is not
        /// present in the address space.
        /// </summary>
        public static UserManagementBinding? Bind(
            AsyncCustomNodeManager nodeManager,
            IUserManagement userManagement,
            ISessionManager? sessionManager)
        {
            if (nodeManager == null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }
            if (userManagement == null)
            {
                throw new ArgumentNullException(nameof(userManagement));
            }

            UserManagementState? state = nodeManager.FindPredefinedNode<UserManagementState>(
                new NodeId(Opc.Ua.Objects.UserManagement));
            if (state == null)
            {
                return null;
            }

            var binding = new UserManagementBinding(
                userManagement,
                sessionManager,
                (nodeManager.Server as IServerInternal)?.Telemetry);
            binding.Initialize(state);
            return binding;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            m_userManagement.UserDeactivated -= OnUserDeactivated;
        }

        private void Initialize(UserManagementState state)
        {
            m_state = state;

            if (state.AddUser != null)
            {
                state.AddUser.OnCall = null;
                state.AddUser.OnCallAsync = OnAddUserAsync;
            }
            if (state.ModifyUser != null)
            {
                state.ModifyUser.OnCall = null;
                state.ModifyUser.OnCallAsync = OnModifyUserAsync;
            }
            if (state.RemoveUser != null)
            {
                state.RemoveUser.OnCall = null;
                state.RemoveUser.OnCallAsync = OnRemoveUserAsync;
            }
            if (state.ChangePassword != null)
            {
                state.ChangePassword.OnCall = null;
                state.ChangePassword.OnCallAsync = OnChangePasswordAsync;
            }

            SyncProperties();
            m_userManagement.UserDeactivated += OnUserDeactivated;
        }

        private void SyncProperties()
        {
            if (m_state == null)
            {
                return;
            }
            if (m_state.Users != null)
            {
                m_state.Users.Value = ArrayOf.Wrapped(
                    System.Linq.Enumerable.ToArray(m_userManagement.SnapshotUsers()));
            }
            if (m_state.PasswordLength != null)
            {
                m_state.PasswordLength.Value = m_userManagement.PasswordLength;
            }
            if (m_state.PasswordOptions != null)
            {
                m_state.PasswordOptions.Value = (uint)m_userManagement.PasswordOptions;
            }
            if (m_state.PasswordRestrictions != null)
            {
                m_state.PasswordRestrictions.Value = m_userManagement.PasswordRestrictions
                    ?? LocalizedText.Null;
            }
        }

        private async ValueTask<AddUserMethodStateResult> OnAddUserAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string userName,
            string password,
            uint userConfiguration,
            string description,
            CancellationToken cancellationToken)
        {
            await Task.Yield();

            var result = new AddUserMethodStateResult();
            ServiceResult auth = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(auth))
            {
                result.ServiceResult = auth;
                return result;
            }

            result.ServiceResult = m_userManagement.AddUser(
                userName, password, (UserConfigurationMask)userConfiguration, description);
            if (ServiceResult.IsGood(result.ServiceResult))
            {
                SyncProperties();
            }
            return result;
        }

        private async ValueTask<ModifyUserMethodStateResult> OnModifyUserAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string userName,
            bool modifyPassword,
            string password,
            bool modifyUserConfiguration,
            uint userConfiguration,
            bool modifyDescription,
            string description,
            CancellationToken cancellationToken)
        {
            await Task.Yield();

            var result = new ModifyUserMethodStateResult();
            ServiceResult auth = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(auth))
            {
                result.ServiceResult = auth;
                return result;
            }

            string? callingUserName = RoleAuthorizationGate.GetUserIdentity(context)?.DisplayName;
            result.ServiceResult = m_userManagement.ModifyUser(
                userName,
                modifyPassword,
                password,
                modifyUserConfiguration,
                (UserConfigurationMask)userConfiguration,
                modifyDescription,
                description,
                callingUserName);
            if (ServiceResult.IsGood(result.ServiceResult))
            {
                SyncProperties();
            }
            return result;
        }

        private async ValueTask<RemoveUserMethodStateResult> OnRemoveUserAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string userName,
            CancellationToken cancellationToken)
        {
            await Task.Yield();

            var result = new RemoveUserMethodStateResult();
            ServiceResult auth = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(auth))
            {
                result.ServiceResult = auth;
                return result;
            }

            string? callingUserName = RoleAuthorizationGate.GetUserIdentity(context)?.DisplayName;
            result.ServiceResult = m_userManagement.RemoveUser(userName, callingUserName);
            if (ServiceResult.IsGood(result.ServiceResult))
            {
                SyncProperties();
            }
            return result;
        }

        private async ValueTask<ChangePasswordMethodStateResult> OnChangePasswordAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string oldPassword,
            string newPassword,
            CancellationToken cancellationToken)
        {
            await Task.Yield();

            var result = new ChangePasswordMethodStateResult();
            IUserIdentity? identity = RoleAuthorizationGate.GetUserIdentity(context);
            if (identity == null || string.IsNullOrEmpty(identity.DisplayName))
            {
                result.ServiceResult = new ServiceResult(StatusCodes.BadInvalidState,
                    new LocalizedText("ChangePassword requires a USERNAME identity token."));
                return result;
            }
            ServiceResult auth = RoleAuthorizationGate.CheckSelfUserName(context, identity.DisplayName);
            if (ServiceResult.IsBad(auth))
            {
                result.ServiceResult = auth;
                return result;
            }

            result.ServiceResult = m_userManagement.ChangePassword(
                identity.DisplayName, oldPassword, newPassword);
            return result;
        }

        private void OnUserDeactivated(object? sender, UserDeactivatedEventArgs e)
        {
            if (m_disposed || m_sessionManager == null || string.IsNullOrEmpty(e.UserName))
            {
                return;
            }
            try
            {
                System.Collections.Generic.IList<ISession> sessions = m_sessionManager.GetSessions();
                foreach (ISession session in sessions)
                {
                    if (string.Equals(session.Identity?.DisplayName, e.UserName, StringComparison.Ordinal))
                    {
                        try
                        {
                            session.Dispose();
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogWarning(ex,
                                "Failed to close session {SessionId} for deactivated user {UserName}.",
                                session.Id, e.UserName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex,
                    "Error closing sessions for deactivated user {UserName}.", e.UserName);
            }
        }
    }
}
