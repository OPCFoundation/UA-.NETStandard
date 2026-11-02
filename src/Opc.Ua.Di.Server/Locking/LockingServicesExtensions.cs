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
using Opc.Ua.Server;

namespace Opc.Ua.Di.Server.Locking
{
    /// <summary>
    /// Convenience extension methods that wire an <see cref="ILockService"/>
    /// to the OPC UA <see cref="LockingServicesState"/> objects emitted
    /// by the source generator for every <c>TopologyElementType.Lock</c>
    /// child.
    /// </summary>
    public static class LockingServicesExtensions
    {
        /// <summary>
        /// Routes the <c>InitLock</c>, <c>RenewLock</c>, <c>ExitLock</c>
        /// and <c>BreakLock</c> method calls on
        /// <paramref name="lockState"/> through
        /// <paramref name="service"/>, and binds authoritative reads for the
        /// <c>Locked</c> / <c>LockingClient</c> / <c>LockingUser</c> /
        /// <c>RemainingLockTime</c> property values from the current
        /// lock state. Method completion publishes the resulting properties.
        /// Schedule <see cref="RefreshLockStateAsync"/> to publish expiry and session-close changes.
        /// </summary>
        /// <param name="lockState">The <see cref="LockingServicesState"/>
        /// instance (typically the <c>Lock</c> child of a
        /// <see cref="TopologyElementState"/>).</param>
        /// <param name="elementId">
        /// The NodeId of the topology element whose lock state is
        /// represented by <paramref name="lockState"/>. Used as the
        /// dictionary key inside the service.
        /// </param>
        /// <param name="service">The lock service instance.</param>
        /// <exception cref="ArgumentNullException"><paramref name="lockState"/> is <c>null</c>.</exception>
        public static void BindToLockService(
            this LockingServicesState lockState,
            NodeId elementId,
            ILockService service)
        {
            if (lockState == null)
            {
                throw new ArgumentNullException(nameof(lockState));
            }
            if (elementId.IsNull)
            {
                throw new ArgumentNullException(nameof(elementId));
            }
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }
            BindRead(lockState.Locked, service, elementId, static state => new Variant(state.Locked));
            BindRead(lockState.LockingClient, service, elementId, static state => new Variant(state.LockingClient));
            BindRead(lockState.LockingUser, service, elementId, static state => new Variant(state.LockingUser));
            BindRead(lockState.RemainingLockTime, service, elementId,
                static state => new Variant(state.RemainingLockTimeSeconds * 1000));
            lockState.InitLock?.OnCall = (ctx, _method,
                                              _objectId, context,
                                              ref initLockStatus) =>
                {
                    initLockStatus = service.InitLock(ctx, elementId, context);
                    Refresh(lockState, service, elementId, ctx);
                    return ServiceResult.Good;
                };

            lockState.RenewLock?.OnCall = (ctx, _method,
                                               _objectId,
                                               ref renewLockStatus) =>
                {
                    renewLockStatus = service.RenewLock(ctx, elementId);
                    Refresh(lockState, service, elementId, ctx);
                    return ServiceResult.Good;
                };

            lockState.ExitLock?.OnCall = (ctx, _method,
                                              _objectId,
                                              ref exitLockStatus) =>
                {
                    exitLockStatus = service.ExitLock(ctx, elementId);
                    Refresh(lockState, service, elementId, ctx);
                    return ServiceResult.Good;
                };

            lockState.BreakLock?.OnCall = (ctx, _method,
                                               _objectId,
                                               ref breakLockStatus) =>
                {
                    ServiceResult authorization = RoleAuthorizationGate.CheckAdmin(ctx);
                    if (ServiceResult.IsBad(authorization))
                    {
                        return authorization;
                    }
                    breakLockStatus = service.BreakLock(ctx, elementId);
                    Refresh(lockState, service, elementId, ctx);
                    return ServiceResult.Good;
                };
        }

        /// <summary>
        /// Publishes lease expiry or session-close changes for monitored properties without renewing the lease.
        /// The address-space owner schedules this alongside its other acquisitions.
        /// </summary>
        public static ValueTask RefreshLockStateAsync(
            this LockingServicesState lockState,
            ILockService service,
            NodeId elementId,
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            if (lockState == null)
            {
                throw new ArgumentNullException(nameof(lockState));
            }
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }
            if (elementId.IsNull)
            {
                throw new ArgumentNullException(nameof(elementId));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            cancellationToken.ThrowIfCancellationRequested();
            Refresh(lockState, service, elementId, context);
            return default;
        }

        /// <summary>
        /// Reads the authoritative lease without renewing it, including expiry and session-close changes.
        /// </summary>
        private static void BindRead(
            BaseVariableState? property,
            ILockService service,
            NodeId elementId,
            Func<LockState, Variant> select)
        {
            if (property == null)
            {
                return;
            }
            property.OnSimpleReadValueAsync = (_, _, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return new ValueTask<AttributeSimpleReadResult>(
                    new AttributeSimpleReadResult(ServiceResult.Good, select(service.GetState(elementId))));
            };
        }

        /// <summary>
        /// Publishes a method's resulting lease state without requiring a second client read.
        /// </summary>
        private static void Refresh(
            LockingServicesState node,
            ILockService service,
            NodeId elementId,
            ISystemContext context)
        {
            if (node.Locked == null && node.LockingClient == null &&
                node.LockingUser == null && node.RemainingLockTime == null)
            {
                return;
            }
            LockState state = service.GetState(elementId);
            if (node.Locked != null)
            {
                node.Locked.Value = state.Locked;
            }
            if (node.LockingClient != null)
            {
                node.LockingClient.Value = state.LockingClient;
            }
            if (node.LockingUser != null)
            {
                node.LockingUser.Value = state.LockingUser;
            }
            if (node.RemainingLockTime != null)
            {
                node.RemainingLockTime.Value = state.RemainingLockTimeSeconds * 1000;
            }
            node.ClearChangeMasks(context, true);
        }
    }
}
