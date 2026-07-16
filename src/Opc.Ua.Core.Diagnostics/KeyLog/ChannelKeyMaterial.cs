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

using System;
using System.Security.Cryptography;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.KeyLog
{
    /// <summary>
    /// Immutable snapshot of the key material associated with a single
    /// <see cref="ChannelToken"/> activation. Decoupled from
    /// <see cref="ChannelToken"/> so it can be serialized to and from a
    /// keylog file and used to drive an offline <c>OfflineSecureChannel</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The snapshot contains both the client- and server-side derived keys
    /// because we need to be able to decrypt traffic in both directions
    /// regardless of whether the tap that produced the snapshot was on
    /// the client or the server side.
    /// </para>
    /// <para>
    /// All byte arrays are defensive copies of the originals so the
    /// snapshot is safe to keep alive long after the originating
    /// <see cref="ChannelToken"/> has been disposed.
    /// </para>
    /// </remarks>
    public sealed class ChannelKeyMaterial : IDisposable
    {
        /// <summary>
        /// Constructs an immutable snapshot. Pass <c>null</c> for any
        /// key/iv/nonce that is not present (e.g. when the security mode is
        /// <see cref="MessageSecurityMode.None"/>).
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="securityPolicyUri"/> is <c>null</c>.
        /// </exception>
        public ChannelKeyMaterial(
            uint channelId,
            uint tokenId,
            string securityPolicyUri,
            MessageSecurityMode securityMode,
            DateTime createdAt,
            int lifetime,
            byte[]? clientNonce,
            byte[]? serverNonce,
            byte[]? clientSigningKey,
            byte[]? clientEncryptingKey,
            byte[]? clientInitializationVector,
            byte[]? serverSigningKey,
            byte[]? serverEncryptingKey,
            byte[]? serverInitializationVector)
        {
            ArgumentNullException.ThrowIfNull(securityPolicyUri);

            ChannelId = channelId;
            TokenId = tokenId;
            SecurityPolicyUri = securityPolicyUri;
            SecurityMode = securityMode;
            CreatedAt = createdAt;
            Lifetime = lifetime;
            ClientNonce = Copy(clientNonce);
            ServerNonce = Copy(serverNonce);
            ClientSigningKey = Copy(clientSigningKey);
            ClientEncryptingKey = Copy(clientEncryptingKey);
            ClientInitializationVector = Copy(clientInitializationVector);
            ServerSigningKey = Copy(serverSigningKey);
            ServerEncryptingKey = Copy(serverEncryptingKey);
            ServerInitializationVector = Copy(serverInitializationVector);
        }

        /// <summary>
        /// The OPC UA secure-channel id this token belongs to.
        /// </summary>
        public uint ChannelId { get; }

        /// <summary>
        /// The token id within the channel.
        /// </summary>
        public uint TokenId { get; }

        /// <summary>
        /// The security policy URI in effect for this token (e.g.
        /// <c>http://opcfoundation.org/UA/SecurityPolicy#Aes256_Sha256_RsaPss</c>).
        /// </summary>
        public string SecurityPolicyUri { get; }

        /// <summary>
        /// The message security mode in effect for this token.
        /// </summary>
        public MessageSecurityMode SecurityMode { get; }

        /// <summary>
        /// When the token was created (UTC, server clock).
        /// </summary>
        public DateTime CreatedAt { get; }

        /// <summary>
        /// The lifetime of the token in milliseconds.
        /// </summary>
        public int Lifetime { get; }

        /// <summary>
        /// The nonce provided by the client during OpenSecureChannel.
        /// </summary>
        public byte[]? ClientNonce { get; }

        /// <summary>
        /// The nonce provided by the server during OpenSecureChannel.
        /// </summary>
        public byte[]? ServerNonce { get; }

        /// <summary>
        /// The key used to sign messages sent by the client.
        /// </summary>
        public byte[]? ClientSigningKey { get; }

        /// <summary>
        /// The key used to encrypt messages sent by the client.
        /// </summary>
        public byte[]? ClientEncryptingKey { get; }

        /// <summary>
        /// The initialization vector used by the client when encrypting.
        /// </summary>
        public byte[]? ClientInitializationVector { get; }

        /// <summary>
        /// The key used to sign messages sent by the server.
        /// </summary>
        public byte[]? ServerSigningKey { get; }

        /// <summary>
        /// The key used to encrypt messages sent by the server.
        /// </summary>
        public byte[]? ServerEncryptingKey { get; }

        /// <summary>
        /// The initialization vector used by the server when encrypting.
        /// </summary>
        public byte[]? ServerInitializationVector { get; }

        /// <summary>
        /// Creates a snapshot from a live <see cref="ChannelToken"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="token"/> is <c>null</c>.
        /// </exception>
        public static ChannelKeyMaterial From(ChannelToken token)
        {
            ArgumentNullException.ThrowIfNull(token);

            return new ChannelKeyMaterial(
                channelId: token.ChannelId,
                tokenId: token.TokenId,
                securityPolicyUri: token.SecurityPolicy?.Uri ?? SecurityPolicies.None,
                securityMode: InferSecurityMode(token),
                createdAt: token.CreatedAt,
                lifetime: token.Lifetime,
                clientNonce: token.ClientNonce,
                serverNonce: token.ServerNonce,
                clientSigningKey: token.ClientSigningKey,
                clientEncryptingKey: token.ClientEncryptingKey,
                clientInitializationVector: token.ClientInitializationVector,
                serverSigningKey: token.ServerSigningKey,
                serverEncryptingKey: token.ServerEncryptingKey,
                serverInitializationVector: token.ServerInitializationVector);
        }

        /// <summary>
        /// Securely clears all key, nonce, and IV byte arrays held by
        /// this instance using
        /// <see cref="CryptographicOperations.ZeroMemory(Span{byte})"/>.
        /// </summary>
        /// <remarks>
        /// Call this as soon as the material is no longer needed. After
        /// disposal the properties continue to point to the (now zeroed)
        /// arrays; subsequent reads will see only zero bytes.
        /// </remarks>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            ZeroIfNotNull(ClientNonce);
            ZeroIfNotNull(ServerNonce);
            ZeroIfNotNull(ClientSigningKey);
            ZeroIfNotNull(ClientEncryptingKey);
            ZeroIfNotNull(ClientInitializationVector);
            ZeroIfNotNull(ServerSigningKey);
            ZeroIfNotNull(ServerEncryptingKey);
            ZeroIfNotNull(ServerInitializationVector);
            m_disposed = true;
            GC.SuppressFinalize(this);
        }

        private static byte[]? Copy(byte[]? value)
        {
            if (value is null)
            {
                return null;
            }
            byte[] copy = new byte[value.Length];
            Buffer.BlockCopy(value, 0, copy, 0, value.Length);
            return copy;
        }

        private static void ZeroIfNotNull(byte[]? buffer)
        {
            if (buffer is { Length: > 0 })
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }

        private static MessageSecurityMode InferSecurityMode(ChannelToken token)
        {
            // ChannelToken doesn't carry the security mode directly. We infer
            // it from the presence of derived encryption keys: None when no
            // signing key is present at all, Sign when only signing keys are
            // present, SignAndEncrypt when encryption keys are present.
            if (token.ClientSigningKey is null && token.ServerSigningKey is null)
            {
                return MessageSecurityMode.None;
            }
            if (token.ClientEncryptingKey is null && token.ServerEncryptingKey is null)
            {
                return MessageSecurityMode.Sign;
            }
            return MessageSecurityMode.SignAndEncrypt;
        }

        private bool m_disposed;

#if DEBUG
        /// <summary>
        /// Debug-only finalizer that warns through
        /// <see cref="System.Diagnostics.Trace.TraceWarning(string)"/> when a
        /// <see cref="ChannelKeyMaterial"/> instance is collected without
        /// <see cref="Dispose"/> having been called. Helps catch leaks
        /// in tests; the finalizer is a no-op in Release builds.
        /// </summary>
        ~ChannelKeyMaterial()
        {
            if (!m_disposed)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "ChannelKeyMaterial finalized without Dispose; " +
                    "key material may have persisted in heap. " +
                    "Always Dispose ChannelKeyMaterial instances when done.");
            }
        }
#endif
    }
}
