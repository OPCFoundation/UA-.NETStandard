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
        /// <paramref name="service"/>, and seeds the
        /// <c>Locked</c> / <c>LockingClient</c> / <c>LockingUser</c> /
        /// <c>RemainingLockTime</c> property values from the current
        /// lock state.
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
        public static void BindToLockService(
            this LockingServicesState lockState,
            NodeId elementId,
            ILockService service)
        {
            if (lockState == null) { throw new ArgumentNullException(nameof(lockState)); }
            if (elementId.IsNull) { throw new ArgumentNullException(nameof(elementId)); }
            if (service == null) { throw new ArgumentNullException(nameof(service)); }

            if (lockState.InitLock != null)
            {
                lockState.InitLock.OnCall = (ISystemContext ctx, MethodState _method,
                                              NodeId _objectId, string context,
                                              ref int initLockStatus) =>
                {
                    initLockStatus = service.InitLock(ctx, elementId, context);
                    return ServiceResult.Good;
                };
            }

            if (lockState.RenewLock != null)
            {
                lockState.RenewLock.OnCall = (ISystemContext ctx, MethodState _method,
                                               NodeId _objectId,
                                               ref int renewLockStatus) =>
                {
                    renewLockStatus = service.RenewLock(ctx, elementId);
                    return ServiceResult.Good;
                };
            }

            if (lockState.ExitLock != null)
            {
                lockState.ExitLock.OnCall = (ISystemContext ctx, MethodState _method,
                                              NodeId _objectId,
                                              ref int exitLockStatus) =>
                {
                    exitLockStatus = service.ExitLock(ctx, elementId);
                    return ServiceResult.Good;
                };
            }

            if (lockState.BreakLock != null)
            {
                lockState.BreakLock.OnCall = (ISystemContext ctx, MethodState _method,
                                               NodeId _objectId,
                                               ref int breakLockStatus) =>
                {
                    breakLockStatus = service.BreakLock(ctx, elementId);
                    return ServiceResult.Good;
                };
            }
        }
    }
}
