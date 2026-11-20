/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Performs the symmetric encryption and message authentication a secure
    /// channel applies to every message.
    /// </summary>
    /// <remarks>
    /// Unlike the asymmetric operations, which are already expressed by
    /// <see cref="System.Security.Cryptography.RSA"/> and
    /// <see cref="System.Security.Cryptography.ECDsa"/>, the platform offers no
    /// abstraction that covers the block cipher, the authenticated cipher and the
    /// message authentication code together. This interface therefore declares
    /// operations, where <see cref="ICryptoProvider"/> only declares capability.
    /// <para>
    /// It exists for one consumer: a validated cryptographic module that must
    /// perform <em>every</em> operation rather than only the asymmetric ones.
    /// Hardware offload is explicitly not the consumer, because a device round
    /// trip per message would destroy throughput.
    /// </para>
    /// <para>
    /// This is the hottest path in the stack. Resolve once, where the channel
    /// token is computed, and hold the result; never consult the registry per
    /// message. When nothing is registered the channel calls the platform
    /// directly, with no interface dispatch at all.
    /// </para>
    /// <para>
    /// Every method takes spans and writes into a caller-owned destination, so an
    /// implementation allocates nothing per message. <c>input</c> and
    /// <c>output</c> may refer to exactly the same memory, which is what the
    /// channel does when it encrypts a chunk in place; they must not partially
    /// overlap.
    /// </para>
    /// </remarks>
    public interface ISymmetricCryptoProvider
    {
        /// <summary>
        /// Whether this provider can encrypt with an algorithm.
        /// </summary>
        /// <param name="algorithm">The algorithm being requested.</param>
        /// <returns>
        /// <c>true</c> when the algorithm is supported. A provider that returns
        /// <c>false</c> is bypassed in favour of the platform, so a partial
        /// implementation is legitimate.
        /// </returns>
        bool Supports(SymmetricEncryptionAlgorithm algorithm);

        /// <summary>
        /// Whether this provider can sign with an algorithm.
        /// </summary>
        /// <param name="algorithm">The algorithm being requested.</param>
        /// <returns>
        /// <c>true</c> when the algorithm is supported. A provider that returns
        /// <c>false</c> is bypassed in favour of the platform.
        /// </returns>
        bool Supports(SymmetricSignatureAlgorithm algorithm);

        /// <summary>
        /// Encrypts whole blocks with a block cipher, without padding.
        /// </summary>
        /// <param name="algorithm">The cipher to apply.</param>
        /// <param name="key">The encryption key.</param>
        /// <param name="iv">The initialization vector.</param>
        /// <param name="plaintext">
        /// The plain text, whose length is a whole number of blocks. Padding is
        /// the protocol's concern and has already been applied.
        /// </param>
        /// <param name="ciphertext">
        /// The destination, at least as long as <paramref name="plaintext"/>. May
        /// be the same memory.
        /// </param>
        void Encrypt(
            SymmetricEncryptionAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> iv,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext);

        /// <summary>
        /// Decrypts whole blocks with a block cipher, without padding.
        /// </summary>
        /// <param name="algorithm">The cipher to apply.</param>
        /// <param name="key">The decryption key.</param>
        /// <param name="iv">The initialization vector.</param>
        /// <param name="ciphertext">
        /// The cipher text, whose length is a whole number of blocks.
        /// </param>
        /// <param name="plaintext">
        /// The destination, at least as long as <paramref name="ciphertext"/>.
        /// May be the same memory.
        /// </param>
        void Decrypt(
            SymmetricEncryptionAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> iv,
            ReadOnlySpan<byte> ciphertext,
            Span<byte> plaintext);

        /// <summary>
        /// Encrypts and authenticates with an authenticated cipher.
        /// </summary>
        /// <param name="algorithm">The cipher to apply.</param>
        /// <param name="key">The encryption key.</param>
        /// <param name="nonce">The nonce, which must not repeat under one key.</param>
        /// <param name="plaintext">The plain text.</param>
        /// <param name="ciphertext">
        /// The destination, at least as long as <paramref name="plaintext"/>. May
        /// be the same memory.
        /// </param>
        /// <param name="tag">The destination for the authentication tag.</param>
        /// <param name="associatedData">
        /// Data authenticated but not encrypted. The channel binds the message
        /// header here.
        /// </param>
        void EncryptAuthenticated(
            SymmetricEncryptionAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext,
            Span<byte> tag,
            ReadOnlySpan<byte> associatedData);

        /// <summary>
        /// Verifies and decrypts with an authenticated cipher.
        /// </summary>
        /// <param name="algorithm">The cipher to apply.</param>
        /// <param name="key">The decryption key.</param>
        /// <param name="nonce">The nonce the message was encrypted with.</param>
        /// <param name="ciphertext">The cipher text.</param>
        /// <param name="tag">The authentication tag to verify.</param>
        /// <param name="plaintext">
        /// The destination, at least as long as <paramref name="ciphertext"/>.
        /// May be the same memory. Its contents are undefined when the tag does
        /// not verify.
        /// </param>
        /// <param name="associatedData">
        /// Data authenticated but not encrypted, as supplied when encrypting.
        /// </param>
        /// <returns>
        /// <c>false</c> when the tag does not verify. An implementation returns
        /// rather than throws, so the channel reports a protocol error instead of
        /// a cryptographic one.
        /// </returns>
        bool DecryptAuthenticated(
            SymmetricEncryptionAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> ciphertext,
            ReadOnlySpan<byte> tag,
            Span<byte> plaintext,
            ReadOnlySpan<byte> associatedData);

        /// <summary>
        /// The length, in bytes, of a signature produced by an algorithm.
        /// </summary>
        /// <param name="algorithm">The algorithm being requested.</param>
        /// <returns>
        /// The full length of the message authentication code, before any
        /// truncation the security policy applies.
        /// </returns>
        int GetSignatureLength(SymmetricSignatureAlgorithm algorithm);

        /// <summary>
        /// Computes a message authentication code.
        /// </summary>
        /// <param name="algorithm">The algorithm to apply.</param>
        /// <param name="key">The signing key.</param>
        /// <param name="data">The data to authenticate.</param>
        /// <param name="signature">
        /// The destination, at least
        /// <see cref="GetSignatureLength"/> bytes long.
        /// </param>
        void Sign(
            SymmetricSignatureAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            Span<byte> signature);

        /// <summary>
        /// Verifies a message authentication code.
        /// </summary>
        /// <param name="algorithm">The algorithm to apply.</param>
        /// <param name="key">The signing key.</param>
        /// <param name="data">The data that was authenticated.</param>
        /// <param name="signature">The signature to check.</param>
        /// <returns><c>true</c> when the signature is valid.</returns>
        /// <remarks>
        /// An implementation must compare in time independent of the contents of
        /// <paramref name="signature"/>.
        /// </remarks>
        bool Verify(
            SymmetricSignatureAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> signature);
    }
}
