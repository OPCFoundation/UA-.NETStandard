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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Represents a security token associate with a channel.
    /// </summary>
    public sealed class ChannelToken : IDisposable
    {
        private bool m_disposed;

        /// <summary>
        /// Creates an object with default values.
        /// </summary>
        public ChannelToken()
        {
        }

        /// <summary>
        /// The private version of the Dispose.
        /// </summary>
        private void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                m_disposed = true;
            }
        }

#if DEBUG
        /// <summary>
        /// The finalizer is used to catch issues with the dispose.
        /// </summary>
        ~ChannelToken()
        {
            Dispose(disposing: false);
        }
#endif

        /// <summary>
        /// Disposes the channel tokens.
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// The id assigned to the channel that the token belongs to.
        /// </summary>
        public uint ChannelId { get; set; }

        /// <summary>
        /// The id assigned to the token.
        /// </summary>
        public uint TokenId { get; set; }

        /// <summary>
        /// When the token was created by the server (refers to the server's clock).
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// When the token was created (refers to the local tick count).
        /// Used for calculation of renewals. Uses <see cref="HiResClock.TickCount"/>.
        /// </summary>
        public int CreatedAtTickCount { get; set; }

        /// <summary>
        /// The lifetime of the token in milliseconds.
        /// </summary>
        public int Lifetime { get; set; }

        /// <summary>
        /// Whether the token has expired.
        /// </summary>
        public bool Expired => (HiResClock.TickCount - CreatedAtTickCount) > Lifetime;

        /// <summary>
        /// Whether the token should be activated in case a new one is already created.
        /// </summary>
        public bool ActivationRequired =>
            (HiResClock.TickCount - CreatedAtTickCount) >
            (int)Math.Round(Lifetime * TcpMessageLimits.TokenActivationPeriod);

        /// <summary>
        /// The SecurityPolicy used to encrypt and sign the messages.
        /// </summary>
        public SecurityPolicyInfo SecurityPolicy { get; set; }

        /// <summary>
        /// The secret used to compute the keys.
        /// </summary>
        internal byte[] Secret { get; set; }

        /// <summary>
        /// The previous server nonce used to compute the keys.
        /// </summary>
        internal byte[] PreviousSecret { get; set; }

        /// <summary>
        /// The nonce provided by the client.
        /// </summary>
        public byte[] ClientNonce { get; set; }

        /// <summary>
        /// The nonce provided by the server.
        /// </summary>
        public byte[] ServerNonce { get; set; }

        /// <summary>
        /// The key used to sign messages sent by the client.
        /// </summary>
        internal byte[] ClientSigningKey { get; set; }

        /// <summary>
        /// The key used to encrypt messages sent by the client.
        /// </summary>
        internal byte[] ClientEncryptingKey { get; set; }

        /// <summary>
        /// The initialization vector by the client when encrypting a message.
        /// </summary>
        internal byte[] ClientInitializationVector { get; set; }

        /// <summary>
        /// The key used to sign messages sent by the server.
        /// </summary>
        internal byte[] ServerSigningKey { get; set; }

        /// <summary>
        /// The key used to encrypt messages sent by the server.
        /// </summary>
        internal byte[] ServerEncryptingKey { get; set; }

        /// <summary>
        /// The initialization vector by the server when encrypting a message.
        /// </summary>
        internal byte[] ServerInitializationVector { get; set; }
    }
}
