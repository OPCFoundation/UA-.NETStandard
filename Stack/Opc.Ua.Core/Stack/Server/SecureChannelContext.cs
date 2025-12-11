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

using System;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// Stores context information for the current secure channel.
    /// </summary>
    public class SecureChannelContext
    {
        /// <summary>
        /// Initializes a new instance with the specified property values.
        /// </summary>
        /// <param name="secureChannelId">The secure channel identifier.</param>
        /// <param name="endpointDescription">The endpoint description.</param>
        /// <param name="messageEncoding">The message encoding.</param>
        /// <param name="secureChannelHash">The unique hash for the secure channel calculated during channel creation.</param>
        /// <param name="sessionActivationSecret">A secret used to re-activate sessions on a new secure channel.</param>
        /// <param name="clientChannelCertificate">The client certificate used to establsih the secure channel.</param>
        /// <param name="serverChannelCertificate">The server certificate used to establsih the secure channel.</param>
        public SecureChannelContext(
            string secureChannelId,
            EndpointDescription endpointDescription,
            RequestEncoding messageEncoding,
            byte[] clientChannelCertificate,
            byte[] serverChannelCertificate,
            byte[] secureChannelHash = null,
            byte[] sessionActivationSecret = null)
        {
            SecureChannelId = secureChannelId;
            EndpointDescription = endpointDescription;
            MessageEncoding = messageEncoding;
            ClientChannelCertificate = clientChannelCertificate;
            ServerChannelCertificate = serverChannelCertificate;
            SecureChannelHash = secureChannelHash;
            SessionActivationSecret = sessionActivationSecret;
        }

        /// <summary>
        /// TThe unique identifier for the secure channel.
        /// </summary>
        /// <value>The secure channel identifier.</value>
        public string SecureChannelId { get; }

        /// <summary>
        /// The description of the endpoint used with the channel.
        /// </summary>
        /// <value>The endpoint description.</value>
        public EndpointDescription EndpointDescription { get; }

        /// <summary>
        /// The encoding used with the channel.
        /// </summary>
        /// <value>The message encoding.</value>
        public RequestEncoding MessageEncoding { get; }

        /// <summary>
        /// The unique hash for the secure channel calculated during channel creation.
        /// </summary>
        public byte[] SecureChannelHash { get; }

        /// <summary>
        /// A secret used to re-activate sessions on a new secure channel.
        /// </summary>
        public byte[] SessionActivationSecret { get; }

        /// <summary>
        /// The client certificate used to establsih the secure channel.
        /// </summary>
        public byte[] ClientChannelCertificate { get; }

        /// <summary>
        /// The server certificate used to establsih the secure channel.
        /// </summary>
        public byte[] ServerChannelCertificate { get; }

        /// <summary>
        /// The active secure channel for the thread.
        /// </summary>
        /// <value>The current secure channel context.</value>
        [Obsolete("Pass SecureChannelContext explicitly instead.")]
        public static SecureChannelContext Current
        {
            get => s_dataslot.Value;
            set => s_dataslot.Value = value;
        }

        private static readonly AsyncLocal<SecureChannelContext> s_dataslot = new();
    }

    /// <summary>
    /// The message encoding used with a request.
    /// </summary>
    public enum RequestEncoding
    {
        /// <summary>
        /// The request used the UA binary encoding.
        /// </summary>
        Binary,

        /// <summary>
        /// The request used the UA XML encoding.
        /// </summary>
        Xml,

        /// <summary>
        /// The request used the UA JSON encoding.
        /// </summary>
        Json
    }
}
