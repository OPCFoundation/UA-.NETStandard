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
    /// OPC 10000-100 §10.5 lock-method status codes returned through the
    /// <c>InitLock</c> / <c>RenewLock</c> / <c>ExitLock</c> /
    /// <c>BreakLock</c> output arguments.
    /// </summary>
    public static class LockStatus
    {
        /// <summary>
        /// The operation completed successfully.
        /// </summary>
        public const int Ok = 0;

        /// <summary>
        /// The element is already locked by another client (returned
        /// from <c>InitLock</c>).
        /// </summary>
        public const int AlreadyLocked = 1;

        /// <summary>
        /// The element could not be locked (returned from
        /// <c>InitLock</c>).
        /// </summary>
        public const int CouldNotLock = 2;

        /// <summary>
        /// The element is not currently locked (returned from
        /// <c>RenewLock</c>, <c>ExitLock</c>, or <c>BreakLock</c>).
        /// </summary>
        public const int NotLocked = 1;

        /// <summary>
        /// The calling client does not own the lock (returned from
        /// <c>RenewLock</c> / <c>ExitLock</c>).
        /// </summary>
        public const int WrongClient = 2;
    }
}
