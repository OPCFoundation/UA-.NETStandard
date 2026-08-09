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
using System.Collections.Generic;
using System.Security.Cryptography;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;
using Net.Pkcs11Interop.HighLevelAPI.MechanismParams;

namespace Opc.Ua.Security.Pkcs11
{
    /// <summary>
    /// Owns the connection to one PKCS#11 token and performs the operations on
    /// it that a private key needs.
    /// </summary>
    /// <remarks>
    /// A PKCS#11 session is a stateful, single threaded resource: a sign is two
    /// calls to the module, and interleaving another operation between them
    /// corrupts both. Every operation here is therefore serialised. The stack
    /// only reaches a private key on the cold path - opening a secure channel,
    /// activating a session, issuing a certificate - so serialising costs
    /// nothing that matters.
    /// <para>
    /// This type is internal. The private key is surfaced to the stack as an
    /// ordinary <see cref="RSA"/> or <see cref="ECDsa"/>, which is what every
    /// consumer already accepts.
    /// </para>
    /// </remarks>
    internal sealed class Pkcs11Token : IDisposable
    {
        /// <summary>
        /// Opens and logs in to the token described by the options.
        /// </summary>
        /// <param name="options">Identifies the module, token and PIN.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="options"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The module path is missing.
        /// </exception>
        /// <exception cref="CryptographicException">
        /// No matching token was present.
        /// </exception>
        public Pkcs11Token(Pkcs11TokenOptions options)
            : this(options, DefaultPkcs11LibraryLoader.Instance)
        {
        }

        /// <summary>
        /// Opens and logs in to the token, binding the module through a loader.
        /// </summary>
        /// <param name="options">Identifies the module, token and PIN.</param>
        /// <param name="loader">Binds the PKCS#11 module.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="options"/> or <paramref name="loader"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The module path is missing.
        /// </exception>
        /// <exception cref="CryptographicException">
        /// No matching token was present.
        /// </exception>
        /// <remarks>
        /// The loader is the one place a native module is bound, which is what
        /// makes everything below it reachable without one. The default performs
        /// exactly the load the token would otherwise do inline.
        /// </remarks>
        public Pkcs11Token(Pkcs11TokenOptions options, IPkcs11LibraryLoader loader)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));

            if (loader == null)
            {
                throw new ArgumentNullException(nameof(loader));
            }

            if (string.IsNullOrEmpty(options.ModulePath))
            {
                throw new ArgumentException(
                    "A PKCS#11 module path is required. Set ModulePath, or add " +
                    "'module-path' to the pkcs11: URI.",
                    nameof(options));
            }

            m_factories = new Pkcs11InteropFactories();

            m_library = loader.Load(m_factories, options.ModulePath!);

            try
            {
                ISlot slot = FindSlot(options);

                m_session = slot.OpenSession(SessionType.ReadWrite);

                string? pin = options.GetPin();

                if (!string.IsNullOrEmpty(pin))
                {
                    m_session.Login(CKU.CKU_USER, pin);
                    m_loggedIn = true;
                }
            }
            catch
            {
                m_session?.Dispose();
                m_library.Dispose();
                throw;
            }
        }

        /// <summary>
        /// The options this token was opened with.
        /// </summary>
        public Pkcs11TokenOptions Options { get; }

        /// <summary>
        /// Finds every certificate object on the token, subject to any object
        /// label or id filter in the options.
        /// </summary>
        /// <returns>The DER encoded certificates found.</returns>
        public IReadOnlyList<byte[]> FindCertificates()
        {
            lock (m_lock)
            {
                ThrowIfDisposed();

                List<IObjectAttribute> search =
                [
                    m_factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE)
                ];

                AddObjectFilters(search);

                var certificates = new List<byte[]>();

                foreach (IObjectHandle handle in m_session.FindAllObjects(search))
                {
                    List<IObjectAttribute> values = m_session.GetAttributeValue(
                        handle,
                        [(ulong)CKA.CKA_VALUE]);

                    byte[] encoded = values[0].GetValueAsByteArray();

                    if (encoded is { Length: > 0 })
                    {
                        certificates.Add(encoded);
                    }
                }

                return certificates;
            }
        }

        /// <summary>
        /// Finds the private key whose CKA_ID matches the given certificate.
        /// </summary>
        /// <param name="keyType">The key type to look for.</param>
        /// <param name="ckaId">
        /// The CKA_ID to match, or an empty value to fall back to the options.
        /// </param>
        /// <returns>The key handle, or <c>null</c> when there is no match.</returns>
        public IObjectHandle? FindPrivateKey(CKK keyType, byte[]? ckaId)
        {
            lock (m_lock)
            {
                ThrowIfDisposed();

                List<IObjectAttribute> search =
                [
                    m_factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY),
                    m_factories.ObjectAttributeFactory.Create(CKA.CKA_KEY_TYPE, keyType)
                ];

                if (ckaId is { Length: > 0 })
                {
                    search.Add(m_factories.ObjectAttributeFactory.Create(CKA.CKA_ID, ckaId));
                }
                else
                {
                    AddObjectFilters(search);
                }

                List<IObjectHandle> handles = m_session.FindAllObjects(search);

                return handles.Count > 0 ? handles[0] : null;
            }
        }

        /// <summary>
        /// Reads the CKA_ID of an object.
        /// </summary>
        /// <param name="handle">The object to read.</param>
        /// <returns>The CKA_ID, which may be empty.</returns>
        public byte[] GetObjectId(IObjectHandle handle)
        {
            lock (m_lock)
            {
                ThrowIfDisposed();

                List<IObjectAttribute> values = m_session.GetAttributeValue(
                    handle,
                    [(ulong)CKA.CKA_ID]);

                return values[0].GetValueAsByteArray() ?? [];
            }
        }

        /// <summary>
        /// Finds the certificate objects together with their CKA_ID.
        /// </summary>
        /// <returns>
        /// Pairs of DER encoded certificate and the CKA_ID that links it to a key.
        /// </returns>
        public IReadOnlyList<KeyValuePair<byte[], byte[]>> FindCertificatesWithIds()
        {
            lock (m_lock)
            {
                ThrowIfDisposed();

                List<IObjectAttribute> search =
                [
                    m_factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE)
                ];

                AddObjectFilters(search);

                var results = new List<KeyValuePair<byte[], byte[]>>();

                foreach (IObjectHandle handle in m_session.FindAllObjects(search))
                {
                    List<IObjectAttribute> values = m_session.GetAttributeValue(
                        handle,
                        [(ulong)CKA.CKA_VALUE, (ulong)CKA.CKA_ID]);

                    byte[] encoded = values[0].GetValueAsByteArray();

                    if (encoded is { Length: > 0 })
                    {
                        results.Add(new KeyValuePair<byte[], byte[]>(
                            encoded,
                            values[1].GetValueAsByteArray() ?? []));
                    }
                }

                return results;
            }
        }

        /// <summary>
        /// Signs with a key that never leaves the token.
        /// </summary>
        /// <param name="mechanism">The signing mechanism to use.</param>
        /// <param name="key">The private key handle.</param>
        /// <param name="data">The data to sign.</param>
        /// <returns>The signature.</returns>
        public byte[] Sign(IMechanism mechanism, IObjectHandle key, byte[] data)
        {
            lock (m_lock)
            {
                ThrowIfDisposed();
                return m_session.Sign(mechanism, key, data);
            }
        }

        /// <summary>
        /// Decrypts with a key that never leaves the token.
        /// </summary>
        /// <param name="mechanism">The decryption mechanism to use.</param>
        /// <param name="key">The private key handle.</param>
        /// <param name="data">The data to decrypt.</param>
        /// <returns>The plaintext.</returns>
        public byte[] Decrypt(IMechanism mechanism, IObjectHandle key, byte[] data)
        {
            lock (m_lock)
            {
                ThrowIfDisposed();
                return m_session.Decrypt(mechanism, key, data);
            }
        }

        /// <summary>
        /// Creates a mechanism from the token's factories.
        /// </summary>
        /// <param name="type">The mechanism type.</param>
        /// <returns>The mechanism.</returns>
        public IMechanism CreateMechanism(CKM type)
        {
            return m_factories.MechanismFactory.Create(type);
        }

        /// <summary>
        /// Creates a mechanism with parameters from the token's factories.
        /// </summary>
        /// <param name="type">The mechanism type.</param>
        /// <param name="parameters">The mechanism parameters.</param>
        /// <returns>The mechanism.</returns>
        public IMechanism CreateMechanism(CKM type, IMechanismParams parameters)
        {
            return m_factories.MechanismFactory.Create(type, parameters);
        }

        /// <summary>
        /// Creates RSA-PSS mechanism parameters.
        /// </summary>
        /// <param name="hashAlgorithm">The PKCS#11 hash mechanism.</param>
        /// <param name="maskGenerationFunction">The MGF1 variant.</param>
        /// <param name="saltLength">The salt length in bytes.</param>
        /// <returns>The parameters.</returns>
        public IMechanismParams CreatePssParams(
            CKM hashAlgorithm,
            CKG maskGenerationFunction,
            ulong saltLength)
        {
            return m_factories.MechanismParamsFactory.CreateCkRsaPkcsPssParams(
                (ulong)hashAlgorithm,
                (ulong)maskGenerationFunction,
                saltLength);
        }

        /// <summary>
        /// Creates RSA-OAEP mechanism parameters.
        /// </summary>
        /// <param name="hashAlgorithm">The PKCS#11 hash mechanism.</param>
        /// <param name="maskGenerationFunction">The MGF1 variant.</param>
        /// <returns>The parameters.</returns>
        public IMechanismParams CreateOaepParams(
            CKM hashAlgorithm,
            CKG maskGenerationFunction)
        {
            return m_factories.MechanismParamsFactory.CreateCkRsaPkcsOaepParams(
                (ulong)hashAlgorithm,
                (ulong)maskGenerationFunction,
                (ulong)CKZ.CKZ_DATA_SPECIFIED,
                null);
        }

        /// <summary>
        /// Takes an additional reference so a key handed out by this token
        /// outlives the store that opened it.
        /// </summary>
        /// <returns>This token.</returns>
        /// <remarks>
        /// A caller may dispose the store as soon as it has the certificate -
        /// <c>CertificateIdentifierResolver</c> does exactly that - while the
        /// <see cref="Pkcs11Rsa"/> or <see cref="Pkcs11ECDsa"/> attached to that
        /// certificate still has to sign with it. The session therefore closes
        /// when the last of them is released, not when the first holder lets go.
        /// </remarks>
        public Pkcs11Token AddRef()
        {
            lock (m_lock)
            {
                ThrowIfDisposed();
                m_references++;
                return this;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lock)
            {
                if (m_disposed || --m_references > 0)
                {
                    return;
                }

                m_disposed = true;

                try
                {
                    if (m_loggedIn)
                    {
                        m_session.Logout();
                    }
                }
                catch (Pkcs11Exception)
                {
                    // The session may already be gone, for example because the
                    // token was removed. Nothing useful can be done about it.
                }

                m_session.Dispose();
                m_library.Dispose();
            }
        }

        private void AddObjectFilters(List<IObjectAttribute> search)
        {
            if (!string.IsNullOrEmpty(Options.ObjectLabel))
            {
                search.Add(m_factories.ObjectAttributeFactory.Create(
                    CKA.CKA_LABEL,
                    Options.ObjectLabel));
            }

            if (!Options.ObjectId.IsNull && Options.ObjectId.Length > 0)
            {
                search.Add(m_factories.ObjectAttributeFactory.Create(
                    CKA.CKA_ID,
                    Options.ObjectId.ToArray()));
            }
        }

        private ISlot FindSlot(Pkcs11TokenOptions options)
        {
            List<ISlot> slots = m_library.GetSlotList(SlotsType.WithTokenPresent);

            foreach (ISlot slot in slots)
            {
                if (options.SlotId.HasValue && slot.SlotId != options.SlotId.Value)
                {
                    continue;
                }

                ITokenInfo info = slot.GetTokenInfo();

                if (!string.IsNullOrEmpty(options.TokenLabel) &&
                    !string.Equals(
                        info.Label?.Trim(),
                        options.TokenLabel,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(options.TokenSerial) &&
                    !string.Equals(
                        info.SerialNumber?.Trim(),
                        options.TokenSerial,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                return slot;
            }

            throw new CryptographicException(
                $"No PKCS#11 token matched (label='{options.TokenLabel}', " +
                $"serial='{options.TokenSerial}', slot={options.SlotId}) in module " +
                $"'{options.ModulePath}'.");
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(Pkcs11Token));
            }
        }

        private readonly System.Threading.Lock m_lock = new();
        private readonly Pkcs11InteropFactories m_factories;
        private readonly IPkcs11Library m_library;
        private readonly ISession m_session = null!;
        private readonly bool m_loggedIn;
        private int m_references = 1;
        private bool m_disposed;
    }
}
