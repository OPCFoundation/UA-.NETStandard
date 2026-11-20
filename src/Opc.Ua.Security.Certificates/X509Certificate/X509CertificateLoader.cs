// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#if !NET9_0_OR_GREATER

namespace System.Security.Cryptography.X509Certificates
{
    /// <summary>
    /// A helper to support the .NET 9 certificate loader primitives on older .NET versions.
    /// </summary>
    public static class X509CertificateLoader
    {
        /// <summary>
        ///   Initializes a new instance of the <see cref="X509Certificate2"/> class from certificate data.
        /// </summary>
        /// <remarks>
        ///   Like the .NET 9 loader, only a single X.509 certificate (DER or PEM) is
        ///   accepted. Other formats that the <see cref="X509Certificate2"/>
        ///   constructor detects from the content, such as PKCS#12 and PKCS#7, are
        ///   rejected before the constructor runs.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> is <c>null</c>.</exception>
        /// <exception cref="CryptographicException">The data is not an X.509 certificate.</exception>
        public static X509Certificate2 LoadCertificate(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            if (!IsDerCertificate(data))
            {
                // Empty data and a DER blob of another shape are rejected without
                // probing them (GetCertContentType throws ArgumentException for empty
                // data); any other encoding (PEM) must be identified as a single
                // certificate.
                ThrowIfNotCertificate(data.Length == 0 || IsDerEncoded(data)
                    ? X509ContentType.Unknown
                    : X509Certificate2.GetCertContentType(data));
            }
            return new X509Certificate2(data);
        }

#if NET6_0_OR_GREATER
        /// <summary>
        ///   Initializes a new instance of the <see cref="X509Certificate2"/> class from certificate data.
        /// </summary>
        /// <remarks>
        ///   Like the .NET 9 loader, only a single X.509 certificate (DER or PEM) is
        ///   accepted. Other formats that the <see cref="X509Certificate2"/>
        ///   constructor detects from the content, such as PKCS#12 and PKCS#7, are
        ///   rejected before the constructor runs.
        /// </remarks>
        /// <exception cref="CryptographicException">The data is not an X.509 certificate.</exception>
        public static X509Certificate2 LoadCertificate(ReadOnlySpan<byte> rawData)
        {
            if (!IsDerCertificate(rawData))
            {
                ThrowIfNotCertificate(rawData.IsEmpty || IsDerEncoded(rawData)
                    ? X509ContentType.Unknown
                    : X509Certificate2.GetCertContentType(rawData));
            }
            return new X509Certificate2(rawData);
        }
#endif

        /// <summary>
        ///   Whether the data has the DER shape of an X.509 certificate: a SEQUENCE
        ///   with a definite length that covers the data and starts with the
        ///   tbsCertificate SEQUENCE. PKCS#12 (version INTEGER first) and PKCS#7
        ///   (content type OID first) do not have this shape. A PEM encoded
        ///   certificate does not either and is checked by content type instead.
        /// </summary>
        private static bool IsDerCertificate(ReadOnlySpan<byte> data)
        {
            const byte kSequence = 0x30;
            if (data.Length < 2 || data[0] != kSequence)
            {
                return false;
            }

            int offset = 2;
            int length = data[1];
            if (length > 0x80)
            {
                int lengthBytes = length & 0x7F;
                if (lengthBytes > 4 || data.Length < 2 + lengthBytes)
                {
                    return false;
                }
                length = 0;
                for (int i = 0; i < lengthBytes; i++)
                {
                    length = (length << 8) | data[2 + i];
                }
                offset += lengthBytes;
                if (length < 0)
                {
                    return false;
                }
            }
            else if (length == 0x80)
            {
                // indefinite length is BER, not DER.
                return false;
            }

            return length <= data.Length - offset &&
                length > 0 &&
                data[offset] == kSequence;
        }

        /// <summary>
        ///   Whether the data starts like a DER SEQUENCE.
        /// </summary>
        private static bool IsDerEncoded(ReadOnlySpan<byte> data)
        {
            return data.Length > 0 && data[0] == 0x30;
        }

        private static void ThrowIfNotCertificate(X509ContentType contentType)
        {
            if (contentType != X509ContentType.Cert)
            {
                throw new CryptographicException(
                    "The provided data is not a single X.509 certificate.");
            }
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref="X509Certificate2"/> class from certificate file.
        /// </summary>
        public static X509Certificate2 LoadCertificateFromFile(string path)
        {
            return new X509Certificate2(path);
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref="X509Certificate2"/> class from Pfx data.
        /// </summary>
        public static X509Certificate2 LoadPkcs12(
            byte[] data,
            ReadOnlySpan<char> password,
            X509KeyStorageFlags keyStorageFlags = X509KeyStorageFlags.DefaultKeySet)
        {
#if NETFRAMEWORK
            if (password.IsEmpty)
            {
                return new X509Certificate2(data, string.Empty, keyStorageFlags);
            }

            using var passwordString = new SecureString();
            foreach (char c in password)
            {
                passwordString.AppendChar(c);
            }
            passwordString.MakeReadOnly();

            return new X509Certificate2(data, passwordString, keyStorageFlags);
#else
#if NET6_0_OR_GREATER
            return new X509Certificate2(data, password, keyStorageFlags);
#else
            // .NET Standard does not support ReadOnlySpan<char> for password,
            // creates a string artifact of the password in memory.
            return new X509Certificate2(data, password.ToString(), keyStorageFlags);
#endif
#endif
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref="X509Certificate2"/> class from Pfx file.
        /// </summary>
        public static X509Certificate2 LoadPkcs12FromFile(
            string filename,
            ReadOnlySpan<char> password,
            X509KeyStorageFlags keyStorageFlags = X509KeyStorageFlags.DefaultKeySet)
        {
#if NETFRAMEWORK
            if (password.IsEmpty)
            {
                return new X509Certificate2(filename, string.Empty, keyStorageFlags);
            }

            using var passwordString = new SecureString();
            foreach (char c in password)
            {
                passwordString.AppendChar(c);
            }
            passwordString.MakeReadOnly();

            return new X509Certificate2(filename, passwordString, keyStorageFlags);
#else
#if NET6_0_OR_GREATER
            return new X509Certificate2(filename, password, keyStorageFlags);
#else
            // .NET Standard does not support ReadOnlySpan<char> for password,
            // creates a string artifact of the password in memory.
            return new X509Certificate2(filename, password.ToString(), keyStorageFlags);
#endif
#endif
        }
    }
}
#endif
