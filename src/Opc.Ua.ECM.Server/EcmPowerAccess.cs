/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
