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

namespace Opc.Ua.Client
{
    /// <summary>
    /// How an acknowledgement ended. Every acknowledgement reported to an
    /// <see cref="ISubscriptionAcknowledgementObserver"/> ends exactly once.
    /// </summary>
    public enum AcknowledgementDisposition
    {
        /// <summary>
        /// Not used.
        /// </summary>
        None = 0,

        /// <summary>
        /// The server confirmed the acknowledgement.
        /// </summary>
        Acknowledged = 1,

        /// <summary>
        /// The server no longer holds the message or rejected the acknowledgement, which is
        /// then not sent again. The status tells why.
        /// </summary>
        NotRetained = 2,

        /// <summary>
        /// The subscription that produced the message was deleted, recreated or completed,
        /// another subscription now owns its id, or the session ended.
        /// </summary>
        OwnerRetired = 3,

        /// <summary>
        /// The acknowledgement could not be sent or kept, for example because the
        /// subscription manager was disposed.
        /// </summary>
        Abandoned = 4
    }
}
