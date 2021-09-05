/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
    public class ChannelToken
    {
        #region Constructors
        /// <summary>
        /// Creates an object with default values.
        /// </summary>
        public ChannelToken()
        {
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The id assigned to the channel that the token belongs to.
        /// </summary>
        public uint ChannelId
        {
            get { return m_channelId; }
            set { m_channelId = value; }
        }

        /// <summary>
        /// The id assigned to the token.
        /// </summary>
        public uint TokenId
        {
            get { return m_tokenId; }
            set { m_tokenId = value; }
        }

        /// <summary>
        /// When the token was created by the server (refers to the server's clock).
        /// </summary>
        public DateTime CreatedAt
        {
            get { return m_createdAt; }
            set { m_createdAt = value; }
        }

        /// <summary>
        /// The lifetime of the token in milliseconds.
        /// </summary>
        public int Lifetime
        {
            get { return m_lifetime; }
            set { m_lifetime = value; }
        }

        /// <summary>
        /// Whether the token has expired.
        /// </summary>
        public bool Expired
        {
            get
            {
                if (DateTime.UtcNow > m_createdAt.AddMilliseconds(m_lifetime))
                {
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Whether the token should be activated in case a new one is already created.
        /// </summary>
        public bool ActivationRequired
        {
            get
            {
                if (DateTime.UtcNow > m_createdAt.AddMilliseconds(m_lifetime * TcpMessageLimits.TokenActivationPeriod))
                {
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// The nonce provided by the client.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] ClientNonce
        {
            get { return m_clientNonce; }
            set { m_clientNonce = value; }
        }

        /// <summary>
        /// The nonce provided by the server.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] ServerNonce
        {
            get { return m_serverNonce; }
            set { m_serverNonce = value; }
        }

        /// <summary>
        /// The key used to sign messages sent by the client.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] ClientSigningKey
        {
            get { return m_clientSigningKey; }
            set { m_clientSigningKey = value; }
        }

        /// <summary>
        /// The key used to encrypt messages sent by the client.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] ClientEncryptingKey
        {
            get { return m_clientEncryptingKey; }
            set { m_clientEncryptingKey = value; }
        }

        /// <summary>
        /// The initialization vector by the client when encrypting a message.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] ClientInitializationVector
        {
            get { return m_clientInitializationVector; }
            set { m_clientInitializationVector = value; }
        }

        /// <summary>
        /// The key used to sign messages sent by the server.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] ServerSigningKey
        {
            get { return m_serverSigningKey; }
            set { m_serverSigningKey = value; }
        }

        /// <summary>
        /// The key used to encrypt messages sent by the server.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] ServerEncryptingKey
        {
            get { return m_serverEncryptingKey; }
            set { m_serverEncryptingKey = value; }
        }

        /// <summary>
        /// The initialization vector by the server when encrypting a message.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] ServerInitializationVector
        {
            get { return m_serverInitializationVector; }
            set { m_serverInitializationVector = value; }
        }

        /// <summary>
        /// The SymmetricAlgorithm object used by the client to encrypt messages.
        /// </summary>
        public SymmetricAlgorithm ClientEncryptor
        {
            get { return m_clientEncryptor; }
            set { m_clientEncryptor = value; }
        }

        /// <summary>
        /// The SymmetricAlgorithm object used by the server to encrypt messages.
        /// </summary>
        public SymmetricAlgorithm ServerEncryptor
        {
            get { return m_serverEncryptor; }
            set { m_serverEncryptor = value; }
        }

        /// <summary>
        /// The HMAC object used by the client to sign messages.
        /// </summary>
        public HMAC ClientHmac
        {
            get { return m_clientHmac; }
            set { m_clientHmac = value; }
        }

        /// <summary>
        /// The HMAC object used by the server to sign messages.
        /// </summary>
        public HMAC ServerHmac
        {
            get { return m_serverHmac; }
            set { m_serverHmac = value; }
        }
        #endregion       

        #region Private Fields
        private uint m_channelId;
        private uint m_tokenId;
        private DateTime m_createdAt;
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
        #endregion
    }
}
