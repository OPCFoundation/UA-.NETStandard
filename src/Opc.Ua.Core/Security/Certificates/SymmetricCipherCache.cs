// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// The kinds of keyed cipher a <see cref="SymmetricCipherCache"/> retains.
    /// </summary>
    internal enum SymmetricCipherSlot
    {
        /// <summary>
        /// A CBC encryptor keyed with the key and the IV.
        /// </summary>
        CbcEncryptor = 0,

        /// <summary>
        /// A CBC decryptor keyed with the key and the IV.
        /// </summary>
        CbcDecryptor = 1,

        /// <summary>
        /// An authenticated cipher (AES-GCM or ChaCha20-Poly1305) keyed with the key.
        /// </summary>
        Aead = 2
    }

    /// <summary>
    /// A keyed cipher and the key material it was created with.
    /// </summary>
    internal sealed class CachedSymmetricCipher : IDisposable
    {
        /// <summary>
        /// Wraps a keyed cipher.
        /// </summary>
        /// <param name="cipher">The cipher, an ICryptoTransform or an AEAD instance.</param>
        /// <param name="key">The key the cipher was created with.</param>
        /// <param name="iv">The IV the cipher was created with, if it is bound to one.</param>
        /// <param name="owner">An object the cipher depends on, disposed with it.</param>
        public CachedSymmetricCipher(IDisposable cipher, byte[] key, byte[]? iv, IDisposable? owner = null)
        {
            Cipher = cipher;
            Key = key;
            Iv = iv;
            m_owner = owner;
        }

        /// <summary>
        /// The keyed cipher.
        /// </summary>
        public IDisposable Cipher { get; }

        /// <summary>
        /// The key the cipher was created with.
        /// </summary>
        public byte[] Key { get; }

        /// <summary>
        /// The IV the cipher was created with, if it is bound to one.
        /// </summary>
        public byte[]? Iv { get; }

        /// <summary>
        /// Whether the cipher was created for this key and IV.
        /// </summary>
        /// <remarks>
        /// Compares references: a channel token keeps its key and IV arrays
        /// unchanged for its lifetime, so a different array means different key
        /// material. A caller passing equal copies only loses the reuse.
        /// </remarks>
        public bool Matches(byte[] key, byte[]? iv)
        {
            return ReferenceEquals(Key, key) && ReferenceEquals(Iv, iv);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Cipher.Dispose();
            m_owner?.Dispose();
        }

        private readonly IDisposable? m_owner;
    }

    /// <summary>
    /// Retains keyed symmetric ciphers for one key (one channel token and direction)
    /// without sharing a cipher between concurrent transforms.
    /// </summary>
    /// <remarks>
    /// Keying a cipher (BCryptImportKey on Windows, a cipher context on OpenSSL) costs
    /// more than encrypting a small message, which the one-shot APIs do per call. A
    /// cipher is taken out of the cache while it is in use and put back afterwards,
    /// so a concurrent caller creates its own rather than sharing it. A channel
    /// encrypts and decrypts one message at a time per direction, so in practice one
    /// cipher per slot is reused. Borrowed ciphers are disposed on return if shutdown
    /// has begun.
    /// </remarks>
    internal sealed class SymmetricCipherCache : IDisposable
    {
        /// <summary>
        /// Closes the cache and disposes the ciphers not currently owned by a transform.
        /// </summary>
        public void Dispose()
        {
            Volatile.Write(ref m_disposed, true);
            for (int ii = 0; ii < m_slots.Length; ii++)
            {
                Interlocked.Exchange(ref m_slots[ii], null)?.Dispose();
            }
        }

        /// <summary>
        /// Takes exclusive ownership of the cached cipher in a slot, if there is one
        /// for this key and IV. A cipher for other key material is disposed.
        /// </summary>
        internal CachedSymmetricCipher? Take(SymmetricCipherSlot slot, byte[] key, byte[]? iv)
        {
            CachedSymmetricCipher? cipher = Interlocked.Exchange(ref m_slots[(int)slot], null);
            if (cipher != null && !cipher.Matches(key, iv))
            {
                cipher.Dispose();
                return null;
            }
            return cipher;
        }

        /// <summary>
        /// Returns a cipher to a slot, disposing any surplus cipher or a return after shutdown.
        /// </summary>
        internal void Return(SymmetricCipherSlot slot, CachedSymmetricCipher cipher)
        {
            if (Volatile.Read(ref m_disposed))
            {
                cipher.Dispose();
                return;
            }

            Interlocked.Exchange(ref m_slots[(int)slot], cipher)?.Dispose();

            // Disposal may have drained the cache between the first check and the exchange.
            if (Volatile.Read(ref m_disposed))
            {
                Interlocked.Exchange(ref m_slots[(int)slot], null)?.Dispose();
            }
        }

        private readonly CachedSymmetricCipher?[] m_slots = new CachedSymmetricCipher?[3];
        private bool m_disposed;
    }
}
