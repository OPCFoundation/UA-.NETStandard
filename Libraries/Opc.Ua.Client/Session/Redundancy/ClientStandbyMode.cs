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

namespace Opc.Ua.Client.Redundancy
{
    /// <summary>
    /// Standby behavior of a follower replica in a client replica set, mirroring
    /// the server-side redundancy modes.
    /// </summary>
    public enum ClientStandbyMode
    {
        /// <summary>
        /// Followers run leader election only and connect + activate the session
        /// on promotion. Lowest cost, highest failover latency.
        /// </summary>
        Cold,

        /// <summary>
        /// Followers keep a connected session but create no subscriptions; the
        /// promoted leader creates subscriptions on promotion.
        /// </summary>
        Warm,

        /// <summary>
        /// Followers keep a connected session with subscriptions sampling-only
        /// (publishing disabled); the promoted leader enables publishing. Lowest
        /// failover latency.
        /// </summary>
        Hot
    }
}
