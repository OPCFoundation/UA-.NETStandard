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

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Stable event identifiers for the independently authored sample.
    /// </summary>
    internal static class RecyclingServerEventIds
    {
        /// <summary>Durable command application.</summary>
        internal const int CommandCommitted = 1;

        /// <summary>Persistence failure requiring recovery.</summary>
        internal const int PersistenceFailed = 2;

        /// <summary>View connection unavailable.</summary>
        internal const int ViewUnavailable = 3;

        /// <summary>Bounded membership contention or previous-lease wait without session replacement.</summary>
        internal const int MembershipPending = 4;

        /// <summary>Explicit, non-retriable membership authorization/protocol failure.</summary>
        internal const int MembershipRejected = 5;

        /// <summary>Transient writer admission rejection while pacing the coordinator.</summary>
        internal const int CoordinatorTickDeferred = 6;
    }
}
