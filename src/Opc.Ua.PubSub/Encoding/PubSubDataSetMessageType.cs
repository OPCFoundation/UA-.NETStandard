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

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Kind of a single DataSetMessage. Selected by the JSON
    /// <c>MessageType</c> field and the UADP DataSetFlags2
    /// message-type bits, common to both mappings.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.3">
    /// Part 14 §7.2.5.3 JSON DataSetMessage</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.4">
    /// Part 14 §7.2.4.5.4 UADP DataSetMessage header / DataSetFlags2</see>.
    /// </remarks>
    public enum PubSubDataSetMessageType
    {
        /// <summary>
        /// Key-frame: every configured field is present. Emitted
        /// periodically (see DataSetWriter <c>KeyFrameCount</c>) and
        /// after subscriber reconnect so receivers can rebuild a
        /// complete snapshot.
        /// </summary>
        KeyFrame,

        /// <summary>
        /// Delta-frame: only fields whose value or status changed since
        /// the last KeyFrame are present.
        /// </summary>
        DeltaFrame,

        /// <summary>
        /// Event: payload carries one OPC UA Event (Part 5 EventType
        /// instance) instead of variable values.
        /// </summary>
        Event,

        /// <summary>
        /// KeepAlive: no field payload; emitted at the configured
        /// <c>KeepAliveTime</c> to refresh subscriber receive-timeout
        /// timers.
        /// </summary>
        KeepAlive
    }
}
