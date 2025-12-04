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
using System.Security.Cryptography;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Represents a security token associate with a channel.
    /// </summary>
    public sealed class ChannelToken : IDisposable
    {
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
                if (disposing)
                {
                    Utils.SilentDispose(ClientHmac);
                    Utils.SilentDispose(ServerHmac);
                    Utils.SilentDispose(ClientEncryptor);
                    Utils.SilentDispose(ServerEncryptor);
                }
                ClientHmac = null;
                ServerHmac = null;
                ClientEncryptor = null;
                ServerEncryptor = null;
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
        public byte[] ClientSigningKey { get; set; }

        /// <summary>
        /// The key used to encrypt messages sent by the client.
        /// </summary>
        public byte[] ClientEncryptingKey { get; set; }

        /// <summary>
        /// The initialization vector by the client when encrypting a message.
        /// </summary>
        public byte[] ClientInitializationVector { get; set; }

        /// <summary>
        /// The key used to sign messages sent by the server.
        /// </summary>
        public byte[] ServerSigningKey { get; set; }

        /// <summary>
        /// The key used to encrypt messages sent by the server.
        /// </summary>
        public byte[] ServerEncryptingKey { get; set; }

        /// <summary>
        /// The initialization vector by the server when encrypting a message.
        /// </summary>
        public byte[] ServerInitializationVector { get; set; }

        /// <summary>
        /// The SymmetricAlgorithm object used by the client to encrypt messages.
        /// </summary>
        public SymmetricAlgorithm ClientEncryptor { get; set; }

        /// <summary>
        /// The SymmetricAlgorithm object used by the server to encrypt messages.
        /// </summary>
        public SymmetricAlgorithm ServerEncryptor { get; set; }

        /// <summary>
        /// The HMAC object used by the client to sign messages.
        /// </summary>
        public HMAC ClientHmac { get; set; }

        /// <summary>
        /// The HMAC object used by the server to sign messages.
        /// </summary>
        public HMAC ServerHmac { get; set; }

        private bool m_disposed;
    }
}
