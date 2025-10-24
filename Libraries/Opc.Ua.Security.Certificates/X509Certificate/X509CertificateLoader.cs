/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
        public static X509Certificate2 LoadCertificate(byte[] data)
        {
            return new X509Certificate2(data);
        }

#if NET6_0_OR_GREATER
        /// <summary>
        ///   Initializes a new instance of the <see cref="X509Certificate2"/> class from certificate data.
        /// </summary>
        public static X509Certificate2 LoadCertificate(ReadOnlySpan<byte> rawData)
        {
            return new X509Certificate2(rawData);
        }
#endif

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
