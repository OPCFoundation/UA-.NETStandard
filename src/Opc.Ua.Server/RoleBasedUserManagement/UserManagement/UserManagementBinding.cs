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
    /// every active session and its subscriptions through the node manager's
    /// server to satisfy Part 18 §5.2.6 / §5.2.7.
    /// </para>
    /// </remarks>
    public sealed class UserManagementBinding : IDisposable, IAsyncDisposable
    {
        private UserManagementBinding(
            IUserManagement userManagement,
            ISessionManager? sessionManager,
            IServerInternal server,
            ITelemetryContext? telemetry)
        {
            m_userManagement = userManagement;
            m_sessionManager = sessionManager;
            m_server = server;
            m_logger = telemetry?.CreateLogger<UserManagementBinding>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<UserManagementBinding>.Instance;
        }

        /// <summary>
        /// Resolves the typed <see cref="UserManagementState"/> in the
        /// <paramref name="nodeManager"/>'s predefined nodes, wires every
        /// typed <c>OnCallAsync</c> delegate to <paramref name="userManagement"/>,
        /// keeps the <c>Users</c> property in sync, and closes sessions on
        /// <see cref="IUserManagement.UserDeactivated"/>. Returns the binding
        /// instance so the caller can asynchronously dispose it on server shutdown, or
        /// <c>null</c> if the standard <c>UserManagement</c> object is not
        /// present in the address space.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="nodeManager"/> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="userManagement"/> is null.
        /// </exception>
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
                new NodeId(Objects.UserManagement));
            if (state == null)
            {
                return null;
            }

            var binding = new UserManagementBinding(
                userManagement,
                sessionManager,
                nodeManager.Server,
                nodeManager.Server?.Telemetry);
            binding.Initialize(state);
            return binding;
        }

        /// <summary>
        /// Stops accepting notifications and user changes without blocking.
        /// Use <see cref="DisposeAsync"/> to drain already accepted teardown work.
        /// </summary>
        public void Dispose()
        {
            bool unsubscribe;
            lock (m_deactivationLock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                unsubscribe = m_activeUserChanges == 0;
            }
            if (unsubscribe)
            {
                m_userManagement.UserDeactivated -= OnUserDeactivated;
            }
        }

        /// <summary>
        /// Stops accepting work and awaits all accepted user changes and session teardown.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            Dispose();
            Task userChanges;
            lock (m_deactivationLock)
            {
                userChanges = m_userChangesDrained?.Task ?? Task.CompletedTask;
            }
            await userChanges.ConfigureAwait(false);

            Task deactivations;
            lock (m_deactivationLock)
            {
                deactivations = m_pendingDeactivations;
            }
            await deactivations.ConfigureAwait(false);
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
            m_state.Users?.Value = ArrayOf.Wrapped(
                    System.Linq.Enumerable.ToArray(m_userManagement.SnapshotUsers()));
            m_state.PasswordLength?.Value = m_userManagement.PasswordLength;
            m_state.PasswordOptions?.Value = (uint)m_userManagement.PasswordOptions;
            m_state.PasswordRestrictions?.Value = m_userManagement.PasswordRestrictions
                ?? LocalizedText.Null;
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

            result.ServiceResult = await ExecuteUserChangeAsync(() =>
                m_userManagement.ModifyUser(
                    userName,
                    modifyPassword,
                    password,
                    modifyUserConfiguration,
                    (UserConfigurationMask)userConfiguration,
                    modifyDescription,
                    description,
                    RoleAuthorizationGate.GetUserIdentity(context)?.DisplayName)).ConfigureAwait(false);
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

            result.ServiceResult = await ExecuteUserChangeAsync(() =>
                m_userManagement.RemoveUser(
                    userName, RoleAuthorizationGate.GetUserIdentity(context)?.DisplayName)).ConfigureAwait(false);
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

        private async ValueTask<ServiceResult> ExecuteUserChangeAsync(Func<ServiceResult> change)
        {
            lock (m_deactivationLock)
            {
                if (m_disposed)
                {
                    return new ServiceResult(StatusCodes.BadShutdown);
                }
                if (m_activeUserChanges++ == 0)
                {
                    m_userChangesDrained =
                        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            List<Task<ServiceResult>> deactivations = [];
            List<Task<ServiceResult>>? previous = m_currentUserChange.Value;
            m_currentUserChange.Value = deactivations;
            try
            {
                ServiceResult result;
                ServiceResult[] closeResults;
                try
                {
                    result = change();
                    if (ServiceResult.IsGood(result))
                    {
                        SyncProperties();
                    }
                }
                finally
                {
                    m_currentUserChange.Value = previous;
                    Task<ServiceResult>[] pending;
                    lock (m_deactivationLock)
                    {
                        pending = deactivations.ToArray();
                    }
                    closeResults = await Task.WhenAll(pending).ConfigureAwait(false);
                }

                if (ServiceResult.IsGood(result))
                {
                    foreach (ServiceResult closeResult in closeResults)
                    {
                        if (ServiceResult.IsBad(closeResult))
                        {
                            return closeResult;
                        }
                    }
                }
                return result;
            }
            finally
            {
                TaskCompletionSource<bool>? drained = null;
                bool unsubscribe = false;
                lock (m_deactivationLock)
                {
                    if (--m_activeUserChanges == 0)
                    {
                        drained = m_userChangesDrained;
                        unsubscribe = m_disposed;
                    }
                }
                if (unsubscribe)
                {
                    m_userManagement.UserDeactivated -= OnUserDeactivated;
                }
                drained?.TrySetResult(true);
            }
        }

        private void OnUserDeactivated(object? sender, UserDeactivatedEventArgs e)
        {
            ISessionManager? sessionManager = m_sessionManager ?? m_server.SessionManager;
            if (sessionManager == null || string.IsNullOrEmpty(e.UserName))
            {
                return;
            }
            lock (m_deactivationLock)
            {
                List<Task<ServiceResult>>? userChange = m_currentUserChange.Value;
                if (m_disposed && userChange == null)
                {
                    return;
                }
                Task<ServiceResult> pending = CloseUserSessionsAsync(
                    m_pendingDeactivations, sessionManager, e.UserName);
                m_pendingDeactivations = pending;
                userChange?.Add(pending);
            }
        }

        private async Task<ServiceResult> CloseUserSessionsAsync(
            Task previous,
            ISessionManager sessionManager,
            string userName)
        {
            // Event delivery is synchronous; do not enter server teardown under the queue lock.
            await Task.Yield();
            await previous.ConfigureAwait(false);
            ServiceResult result = ServiceResult.Good;
            try
            {
                foreach (ISession session in sessionManager.GetSessions())
                {
                    if (string.Equals(session.Identity?.DisplayName, userName, StringComparison.Ordinal))
                    {
                        try
                        {
                            await m_server.CloseSessionAsync(
                                null!, session.Id, deleteSubscriptions: true, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            m_logger.FailedToCloseSessionSessionIdForDeactivated(ex, session.Id, userName);
                            result = new ServiceResult(StatusCodes.BadUnexpectedError);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.ErrorClosingSessionsForDeactivatedUserUserName(ex, userName);
                result = new ServiceResult(StatusCodes.BadUnexpectedError);
            }
            return result;
        }

        private readonly IUserManagement m_userManagement;
        private readonly ISessionManager? m_sessionManager;
        private readonly IServerInternal m_server;
        private readonly ILogger m_logger;
        private readonly Lock m_deactivationLock = new();
        private readonly AsyncLocal<List<Task<ServiceResult>>?> m_currentUserChange = new();
        private UserManagementState? m_state;
        private Task m_pendingDeactivations = Task.CompletedTask;
        private TaskCompletionSource<bool>? m_userChangesDrained;
        private int m_activeUserChanges;
        private bool m_disposed;
    }

    /// <summary>
    /// Source-generated log messages for UserManagementBinding.
    /// </summary>
    internal static partial class UserManagementBindingLog
    {
        [LoggerMessage(EventId = ServerEventIds.UserManagementBinding + 0, Level = LogLevel.Warning,
            Message = "Failed to close session {SessionId} for deactivated user {UserName}.")]
        public static partial void FailedToCloseSessionSessionIdForDeactivated(
            this ILogger logger,
            Exception ex,
            NodeId sessionId,
            string userName);

        [LoggerMessage(EventId = ServerEventIds.UserManagementBinding + 1, Level = LogLevel.Error,
            Message = "Error closing sessions for deactivated user {UserName}.")]
        public static partial void ErrorClosingSessionsForDeactivatedUserUserName(
            this ILogger logger,
            Exception ex,
            string userName);
    }

}
