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
using Opc.Ua.Client;

namespace Opc.Ua.Di.Client
{
    /// <summary>
    /// Client-side wrapper for the OPC 10000-100 §10.5 locking service.
    /// Composes (does <em>not</em> inherit) the source-generated
    /// <see cref="Opc.Ua.Di.LockingServicesTypeClient"/> proxy so that
    /// the four lock methods round-trip through the same typed OPC UA
    /// client surface as the rest of the Di model, eliminating the
    /// hand-rolled <c>CallMethodRequest</c> + <c>NodeId.Create</c>
    /// boilerplate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each method targets a single <c>TopologyElementType.Lock</c>
    /// instance (a child of a device, sub-component, or topology
    /// element). The instance NodeId is supplied at construction time;
    /// pass the <c>Lock</c> child NodeId resolved via browse, not the
    /// owning device NodeId.
    /// </para>
    /// </remarks>
    public sealed class DiLockClient
    {
        private LockingServicesTypeClient? m_proxy;

        /// <summary>
        /// Creates a new lock client rooted at the supplied
        /// <c>LockingServicesType</c> instance.
        /// </summary>
        public DiLockClient(
            ISession session,
            NodeId lockNodeId,
            ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (lockNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Lock NodeId is required.", nameof(lockNodeId));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            Session = session;
            LockNodeId = lockNodeId;
            Telemetry = telemetry;
        }

        /// <summary>
        /// The owning session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>The NodeId of the <c>Lock</c> instance.</summary>
        public NodeId LockNodeId { get; }

        /// <summary>
        /// Telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        private LockingServicesTypeClient Proxy => m_proxy ??= new LockingServicesTypeClient(
                    Session, LockNodeId, Telemetry);

        /// <summary>
        /// Calls <c>InitLock</c> on the server. Returns the status code
        /// reported by the device (see
        /// <c>Opc.Ua.Di.Server.Locking.LockStatus</c>): 0 = OK,
        /// 1 = already locked, 2 = could not lock.
        /// </summary>
        public ValueTask<int> InitLockAsync(string context, CancellationToken ct = default)
        {
            return Proxy.InitLockAsync(context ?? string.Empty, ct);
        }

        /// <summary>
        /// Calls <c>RenewLock</c>. 0 = OK, 1 = not locked, 2 = wrong client.
        /// </summary>
        public ValueTask<int> RenewLockAsync(CancellationToken ct = default)
        {
            return Proxy.RenewLockAsync(ct);
        }

        /// <summary>
        /// Calls <c>ExitLock</c>. 0 = OK, 1 = not locked, 2 = wrong client.
        /// </summary>
        public ValueTask<int> ExitLockAsync(CancellationToken ct = default)
        {
            return Proxy.ExitLockAsync(ct);
        }

        /// <summary>
        /// Calls <c>BreakLock</c>. 0 = OK, 1 = not locked.
        /// </summary>
        public ValueTask<int> BreakLockAsync(CancellationToken ct = default)
        {
            return Proxy.BreakLockAsync(ct);
        }
    }
}
