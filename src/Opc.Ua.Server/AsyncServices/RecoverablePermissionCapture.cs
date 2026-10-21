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
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Captures successful ordinary permission checks within the operation's reserved message-sized budget.
    /// </summary>
    internal sealed class RecoverablePermissionCapture
    {
        /// <summary>
        /// Creates a bounded capture funded from the existing retained-input reservation.
        /// </summary>
        public RecoverablePermissionCapture(int byteBudget)
        {
            if (byteBudget < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteBudget));
            }
            m_remaining = byteBudget;
        }

        /// <summary>
        /// Records a target without changing execution when the retained authorization record is too large.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public void Record(NodeId nodeId, PermissionType permission)
        {
            if (nodeId.IsNull)
            {
                return;
            }
            lock (m_gate)
            {
                if (m_sealed)
                {
                    throw new InvalidOperationException("The original invocation's permission capture has ended.");
                }
                if (m_incomplete)
                {
                    return;
                }
                var check = new RecoverableNodePermission(nodeId, permission);
                if (m_permissions.Contains(check))
                {
                    return;
                }
                long size = 64;
                if (nodeId.TryGetValue(out string? text))
                {
                    size += Encoding.UTF8.GetByteCount(text);
                }
                else if (nodeId.TryGetValue(out ByteString bytes))
                {
                    size += bytes.Length;
                }
                if (size > m_remaining)
                {
                    m_incomplete = true;
                    return;
                }
                m_permissions.Add(check);
                m_remaining -= (int)size;
            }
        }

        /// <summary>
        /// Transfers the checks once, or rejects retention instead of silently omitting authority.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public ArrayOf<RecoverableNodePermission> Snapshot()
        {
            lock (m_gate)
            {
                if (m_incomplete)
                {
                    m_permissions = [];
                    throw new ServiceResultException(StatusCodes.BadResponseTooLarge);
                }
                if (m_sealed)
                {
                    throw new InvalidOperationException("The permission capture has already been transferred.");
                }
                var result = new RecoverableNodePermission[m_permissions.Count];
                int index = 0;
                foreach (RecoverableNodePermission permission in m_permissions)
                {
                    result[index++] = permission;
                }
                m_permissions = [];
                m_sealed = true;
                return result;
            }
        }

        private readonly Lock m_gate = new();
        private HashSet<RecoverableNodePermission> m_permissions = [];
        private int m_remaining;
        private bool m_incomplete;
        private bool m_sealed;
    }

    /// <summary>
    /// Identifies a current permission to recheck, not a cached grant or a reference to a disposed Session.
    /// </summary>
    /// <param name="NodeId">The target whose authority was consulted during original execution.</param>
    /// <param name="Permission">The required permissions; they must be evaluated anew for every collection.</param>
    internal readonly record struct RecoverableNodePermission(NodeId NodeId, PermissionType Permission);
}
