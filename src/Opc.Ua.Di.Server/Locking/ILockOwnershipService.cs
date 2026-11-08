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

namespace Opc.Ua.Di.Server.Locking
{
    /// <summary>
    /// Optional capability for an <see cref="ILockService"/> to authorize access
    /// using the calling session's ownership of an unexpired lock.
    /// </summary>
    /// <remarks>
    /// Consumers requiring exclusive access must deny access when the configured
    /// lock service does not implement this capability. A successful check is a
    /// snapshot, not a reservation for a subsequent operation.
    /// </remarks>
    public interface ILockOwnershipService
    {
        /// <summary>
        /// Returns whether the calling session owns the element's unexpired lock,
        /// without changing the lock or its timeout. Returns false when no session
        /// identifier is available, the lock has expired, or its session has closed.
        /// User names and client-supplied context strings do not establish ownership.
        /// </summary>
        bool IsLockOwner(ISystemContext context, NodeId elementId);
    }
}
