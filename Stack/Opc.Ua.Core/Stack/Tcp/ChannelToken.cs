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
using System.Security.Cryptography;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Represents a security token associate with a channel.
    /// </summary>
    public sealed class ChannelToken : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Creates an object with default values.
        /// </summary>
        public ChannelToken()
        {
        }
        #endregion

        #region IDisposable
        /// <summary>
        /// The private version of the Dispose.
        /// </summary>
        private void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    Utils.SilentDispose(m_clientHmac);
                    Utils.SilentDispose(m_serverHmac);
                    Utils.SilentDispose(m_clientEncryptor);
                    Utils.SilentDispose(m_serverEncryptor);
                }
                m_clientHmac = null;
                m_serverHmac = null;
                m_clientEncryptor = null;
                m_serverEncryptor = null;
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
        #endregion

        #region Public Properties
        /// <summary>
        /// The id assigned to the channel that the token belongs to.
        /// </summary>
        public uint ChannelId
        {
            get => m_channelId;
            set => m_channelId = value;
        }

        /// <summary>
        /// The id assigned to the token.
        /// </summary>
        public uint TokenId
        {
            get => m_tokenId;
            set => m_tokenId = value;
        }

        /// <summary>
        /// When the token was created by the server (refers to the server's clock).
        /// </summary>
        public DateTime CreatedAt
        {
            get => m_createdAt;
            set => m_createdAt = value;
        }

        /// <summary>
        /// When the token was created (refers to the local tick count).
        /// Used for calculation of renewals. Uses <see cref="Opc.Ua.HiResClock.TickCount"/>.
        /// </summary>
        public int CreatedAtTickCount
        {
            get => m_createdAtTickCount;
            set => m_createdAtTickCount = value;
        }

        /// <summary>
        /// The lifetime of the token in milliseconds.
        /// </summary>
        public int Lifetime
        {
            get => m_lifetime;
            set => m_lifetime = value;
        }

        /// <summary>
        /// Whether the token has expired.
        /// </summary>
        public bool Expired
        {
            get
            {
                return (HiResClock.TickCount - m_createdAtTickCount) > m_lifetime;
            }
        }

        /// <summary>
        /// Whether the token should be activated in case a new one is already created.
        /// </summary>
        public bool ActivationRequired
        {
            get
            {
                return (HiResClock.TickCount - m_createdAtTickCount) > (int)Math.Round(m_lifetime * TcpMessageLimits.TokenActivationPeriod);
            }
        }

        /// <summary>
        /// The nonce provided by the client.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] ClientNonce
        {
            get => m_clientNonce;
            set => m_clientNonce = value;
        }

        /// <summary>
        /// The nonce provided by the server.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] ServerNonce
        {
            get => m_serverNonce;
            set => m_serverNonce = value;
        }

        /// <summary>
        /// The key used to sign messages sent by the client.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] ClientSigningKey
        {
            get => m_clientSigningKey;
            set => m_clientSigningKey = value;
        }

        /// <summary>
        /// The key used to encrypt messages sent by the client.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] ClientEncryptingKey
        {
            get => m_clientEncryptingKey;
            set => m_clientEncryptingKey = value;
        }

        /// <summary>
        /// The initialization vector by the client when encrypting a message.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] ClientInitializationVector
        {
            get => m_clientInitializationVector;
            set => m_clientInitializationVector = value;
        }

        /// <summary>
        /// The key used to sign messages sent by the server.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] ServerSigningKey
        {
            get => m_serverSigningKey;
            set => m_serverSigningKey = value;
        }

        /// <summary>
        /// The key used to encrypt messages sent by the server.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] ServerEncryptingKey
        {
            get => m_serverEncryptingKey;
            set => m_serverEncryptingKey = value;
        }

        /// <summary>
        /// The initialization vector by the server when encrypting a message.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] ServerInitializationVector
        {
            get => m_serverInitializationVector;
            set => m_serverInitializationVector = value;
        }

        /// <summary>
        /// The SymmetricAlgorithm object used by the client to encrypt messages.
        /// </summary>
        public SymmetricAlgorithm ClientEncryptor
        {
            get => m_clientEncryptor;
            set => m_clientEncryptor = value;
        }

        /// <summary>
        /// The SymmetricAlgorithm object used by the server to encrypt messages.
        /// </summary>
        public SymmetricAlgorithm ServerEncryptor
        {
            get => m_serverEncryptor;
            set => m_serverEncryptor = value;
        }

        /// <summary>
        /// The HMAC object used by the client to sign messages.
        /// </summary>
        public HMAC ClientHmac
        {
            get => m_clientHmac;
            set => m_clientHmac = value;
        }

        /// <summary>
        /// The HMAC object used by the server to sign messages.
        /// </summary>
        public HMAC ServerHmac
        {
            get => m_serverHmac;
            set => m_serverHmac = value;
        }
        #endregion

        #region Private Fields
        private uint m_channelId;
        private uint m_tokenId;
        private DateTime m_createdAt;
        private int m_createdAtTickCount;
        private int m_lifetime;
        private byte[] m_clientNonce;
        private byte[] m_serverNonce;
        private byte[] m_clientSigningKey;
        private byte[] m_clientEncryptingKey;
        private byte[] m_clientInitializationVector;
        private byte[] m_serverSigningKey;
        private byte[] m_serverEncryptingKey;
        private byte[] m_serverInitializationVector;
        private HMAC m_clientHmac;
        private HMAC m_serverHmac;
        private SymmetricAlgorithm m_clientEncryptor;
        private SymmetricAlgorithm m_serverEncryptor;
        private bool m_disposed;
        #endregion
    }
}
