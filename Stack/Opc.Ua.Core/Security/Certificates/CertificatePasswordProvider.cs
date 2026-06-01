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
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// An interface for a password provider for certificate private keys.
    /// </summary>
    public interface ICertificatePasswordProvider
    {
        /// <summary>
        /// Return the password for a certificate private key.
        /// </summary>
        /// <param name="certificateIdentifier">The certificate identifier for which the password is needed.</param>
        char[] GetPassword(CertificateIdentifier certificateIdentifier);
    }

    /// <summary>
    /// The default certificate password provider implementation.
    /// </summary>
    /// <remarks>
    /// Internally the password bytes are stored in an
    /// <see cref="ISecretRegistry"/> (defaulting to a per-instance
    /// <see cref="InMemorySecretStore"/>) under an opaque
    /// <see cref="SecretIdentifier"/>. The legacy
    /// <see cref="ICertificatePasswordProvider.GetPassword"/> contract
    /// is preserved: callers receive a fresh <c>char[]</c> they may
    /// zero after use. Future stores (DPAPI, Kubernetes, Key Vault)
    /// can be plugged in via the
    /// <see cref="CertificatePasswordProvider(ISecretRegistry, SecretIdentifier)"/>
    /// ctor without touching this class.
    /// </remarks>
    public class CertificatePasswordProvider : ICertificatePasswordProvider
    {
        private const string kDefaultSecretName = "default";

        private readonly ISecretRegistry m_registry;
        private readonly SecretIdentifier m_id;

        /// <summary>
        /// Default constructor — empty password.
        /// </summary>
        public CertificatePasswordProvider()
        {
            (m_registry, m_id) = CreateInMemoryRegistry(passwordBytes: null);
        }

        /// <summary>
        /// Constructor which takes a raw or UTF8 encoded password. If not utf8
        /// the buffer is assumed raw token and will be base64 encoded.
        /// </summary>
        /// <param name="password">The raw password.</param>
        /// <param name="isUtf8String">Whether the password is utf8 string</param>
        public CertificatePasswordProvider(byte[] password, bool isUtf8String = true)
        {
            byte[] passwordBytes;
            if (password == null)
            {
                passwordBytes = [];
            }
            else if (isUtf8String)
            {
                // Already UTF-8; persist verbatim.
                passwordBytes = (byte[])password.Clone();
            }
            else
            {
                // Treat the input as raw bytes and base64-encode for storage.
                char[] charToken = new char[password.Length * 3];
                int length = Convert.ToBase64CharArray(
                    password,
                    0,
                    password.Length,
                    charToken,
                    0,
                    Base64FormattingOptions.None);
                passwordBytes = Encoding.UTF8.GetBytes(charToken, 0, length);
                Array.Clear(charToken, 0, charToken.Length);
            }

            (m_registry, m_id) = CreateInMemoryRegistry(passwordBytes);
        }

        /// <summary>
        /// Constructor which takes a password string
        /// </summary>
        /// <param name="password"></param>
        public CertificatePasswordProvider(ReadOnlySpan<char> password)
        {
            byte[]? passwordBytes;
            if (!password.IsEmpty && !password.IsWhiteSpace())
            {
                passwordBytes = Encoding.UTF8.GetBytes(password.ToArray());
            }
            else
            {
                passwordBytes = null;
            }

            (m_registry, m_id) = CreateInMemoryRegistry(passwordBytes);
        }

        /// <summary>
        /// Advanced constructor that resolves the password from an existing
        /// <see cref="ISecretRegistry"/> via a caller-supplied
        /// <see cref="SecretIdentifier"/>. Use this overload to plug in a
        /// custom store (e.g. a DPAPI or Key Vault backed
        /// <see cref="ISecretStore"/>) without copying password bytes
        /// through the legacy ctors.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="registry"/> or <paramref name="id"/> is
        /// <c>null</c>.
        /// </exception>
        public CertificatePasswordProvider(ISecretRegistry registry, SecretIdentifier id)
        {
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_id = id ?? throw new ArgumentNullException(nameof(id));
        }

        /// <summary>
        /// Return the password used for the certificate.
        /// </summary>
        public char[] GetPassword(CertificateIdentifier certificateIdentifier)
        {
            using ISecret? secret = m_registry.TryGet(m_id);
            if (secret == null || secret.Bytes.IsEmpty)
            {
                return [];
            }

            return Encoding.UTF8.GetChars(secret.Bytes.ToArray());
        }

        /// <summary>
        /// Builds a per-instance in-memory store + registry pair holding
        /// <paramref name="passwordBytes"/> (if non-null/non-empty)
        /// under <see cref="kDefaultSecretName"/>.
        /// </summary>
        private static (ISecretRegistry registry, SecretIdentifier id) CreateInMemoryRegistry(
            byte[]? passwordBytes)
        {
            var store = new InMemorySecretStore();
            var registry = new SecretRegistry(store);
            var id = new SecretIdentifier(
                kDefaultSecretName,
                InMemorySecretStore.DefaultStoreType);

            if (passwordBytes != null && passwordBytes.Length > 0)
            {
                // The store hands out per-call ISecret views over a
                // private byte[] copy; SetAsync on InMemorySecretStore
                // completes synchronously. Asserting the sync completion
                // keeps this constructor genuinely non-blocking: any
                // pluggable ISecretStore that needs to await must be
                // initialised via a separate async factory.
                System.Threading.Tasks.ValueTask vt = store.SetAsync(id, passwordBytes);
                if (!vt.IsCompletedSuccessfully)
                {
                    throw new InvalidOperationException(
                        "InMemorySecretStore.SetAsync did not complete synchronously; " +
                        "asynchronous secret stores require an async CertificatePasswordProvider factory.");
                }
            }

            return (registry, id);
        }
    }
}
