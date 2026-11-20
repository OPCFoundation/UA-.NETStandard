// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.Server;

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Applies the common power-control role policy and optional DI ownership check.
    /// </summary>
    internal static class EcmPowerAccess
    {
        /// <summary>
        /// Authorizes control without acquiring or renewing a DI lock.
        /// </summary>
        public static async ValueTask<bool> IsAuthorizedAsync(
            ISystemContext caller,
            NodeId resource,
            Func<ISystemContext, CancellationToken, ValueTask<bool>>? authorize,
            ILockService? exclusiveAccess,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool authorized;
            if (authorize != null)
            {
                authorized = await authorize(caller, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                IUserIdentity? identity = RoleAuthorizationGate.GetUserIdentity(caller);
                authorized = identity != null &&
                    (identity.GrantedRoleIds.Contains(Opc.Ua.ObjectIds.WellKnownRole_Operator) ||
                    identity.GrantedRoleIds.Contains(Opc.Ua.ObjectIds.WellKnownRole_Engineer) ||
                    identity.GrantedRoleIds.Contains(Opc.Ua.ObjectIds.WellKnownRole_Supervisor));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return authorized && (exclusiveAccess == null ||
                exclusiveAccess is ILockOwnershipService ownership && ownership.IsLockOwner(caller, resource));
        }
    }
}
