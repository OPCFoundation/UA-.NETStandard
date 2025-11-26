/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
