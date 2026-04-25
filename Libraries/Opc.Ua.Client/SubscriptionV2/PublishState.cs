#if OPCUA_CLIENT_V2
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

namespace Opc.Ua.Client.Subscriptions
{
    using System;

    /// <summary>
    /// Flags indicating the publish state.
    /// </summary>
    [Flags]
    public enum PublishState
    {
        /// <summary>
        /// The publish state has not changed.
        /// </summary>
        None = 0,

        /// <summary>
        /// A keep alive message was received.
        /// </summary>
        KeepAlive = 1 << 1,

        /// <summary>
        /// A republish for a missing message was issued.
        /// </summary>
        Republish = 1 << 2,

        /// <summary>
        /// The publishing stopped.
        /// </summary>
        Stopped = 1 << 3,

        /// <summary>
        /// The publishing recovered.
        /// </summary>
        Recovered = 1 << 4,

        /// <summary>
        /// The subscription timed out on the
        /// server and was closed
        /// </summary>
        Timeout = 1 << 5,

        /// <summary>
        /// The Subscription was transferred
        /// to another session.
        /// </summary>
        Transferred = 1 << 6,

        /// <summary>
        /// Subscription closed on the client
        /// </summary>
        Completed = 1 << 7,
    }
}
#endif
