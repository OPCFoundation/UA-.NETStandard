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
    /// Snapshot of a topology element's lock state at a point in time.
    /// </summary>
    /// <param name="Locked">
    /// <see langword="true"/> when the element is currently locked.
    /// </param>
    /// <param name="LockingClient">
    /// Free-text identifier supplied by the client when calling
    /// <c>InitLock</c>; empty string when not locked.
    /// </param>
    /// <param name="LockingUser">
    /// User identity that owns the lock (typically the session's
    /// <see cref="ISystemContext.UserId"/>); empty when not locked.
    /// </param>
    /// <param name="RemainingLockTimeSeconds">
    /// Seconds remaining before the lock auto-expires. Zero when not
    /// locked.
    /// </param>
    public sealed record LockState(
        bool Locked,
        string LockingClient,
        string LockingUser,
        double RemainingLockTimeSeconds);
}
