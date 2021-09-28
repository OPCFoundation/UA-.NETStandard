/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// EventArgs class for RawData message received event
    /// </summary>
    public class RawDataReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// Get/Set flag that indicates if the RawData message is handled and shall not be decoded by the PubSub library
        /// </summary>
        public bool Handled { get; set; }

        /// <summary>
        /// Get/Set the message bytes
        /// </summary>
        public byte[] Message { get; set; }

        /// <summary>
        /// Get/Set the message Source
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Get/Set the TransportProtocol for the message that was received
        /// </summary>
        public TransportProtocol TransportProtocol { get; set; }

        /// <summary>
        /// Get/Set the current MessageMapping for the message that was received
        /// </summary>
        public MessageMapping MessageMapping { get; set; }

        /// <summary>
        /// Get/Set the PubSubConnection Configuration object for the connection that received this message
        /// </summary>
        public PubSubConnectionDataType PubSubConnectionConfiguration { get; set; }
    }
}
