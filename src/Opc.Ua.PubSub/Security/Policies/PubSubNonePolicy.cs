// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Security.Policies
{
    /// <summary>
    /// Pass-through implementation of <see cref="IPubSubSecurityPolicy"/>
    /// representing the absence of message-level security. Used when a
    /// SecurityGroup is configured with <c>SecurityMode = None</c> or
    /// when running interop tests against unsecured publishers.
    /// </summary>
    /// <remarks>
    /// Implements the <c>None</c> entry of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3.1">
    /// Part 14 §7.2.4.4.3.1 PubSub security policies</see>. All key,
    /// nonce and signature lengths are zero — the wrapper layer must
    /// not allocate a SecurityHeader when this policy is selected.
    /// </remarks>
    public sealed class PubSubNonePolicy : IPubSubSecurityPolicy
    {
        /// <summary>
        /// The platform-backed instance the registry publishes.
        /// </summary>
        internal static readonly PubSubNonePolicy Instance = new();

        private PubSubNonePolicy()
        {
        }

        /// <inheritdoc/>
        public string PolicyUri => PubSubSecurityPolicyUri.None;

        /// <inheritdoc/>
        public int SigningKeyLength => 0;

        /// <inheritdoc/>
        public int EncryptingKeyLength => 0;

        /// <inheritdoc/>
        public int NonceLength => 0;

        /// <inheritdoc/>
        public int SignatureLength => 0;

        /// <inheritdoc/>
        public void Sign(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> signingKey,
            Span<byte> signature)
        {
            if (signature.Length != 0)
            {
                throw new ArgumentException(
                    "None policy does not produce a signature; pass an empty span.",
                    nameof(signature));
            }
        }

        /// <inheritdoc/>
        public bool Verify(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> signature,
            ReadOnlySpan<byte> signingKey)
        {
            return signature.Length == 0;
        }

        /// <inheritdoc/>
        public void Encrypt(
            ReadOnlySpan<byte> plaintext,
            ReadOnlySpan<byte> encryptingKey,
            ReadOnlySpan<byte> nonce,
            Span<byte> ciphertext)
        {
            if (ciphertext.Length < plaintext.Length)
            {
                throw new ArgumentException(
                    "Ciphertext buffer is shorter than plaintext.",
                    nameof(ciphertext));
            }
            plaintext.CopyTo(ciphertext);
        }

        /// <inheritdoc/>
        public void Decrypt(
            ReadOnlySpan<byte> ciphertext,
            ReadOnlySpan<byte> encryptingKey,
            ReadOnlySpan<byte> nonce,
            Span<byte> plaintext)
        {
            if (plaintext.Length < ciphertext.Length)
            {
                throw new ArgumentException(
                    "Plaintext buffer is shorter than ciphertext.",
                    nameof(plaintext));
            }
            ciphertext.CopyTo(plaintext);
        }
    }
}
