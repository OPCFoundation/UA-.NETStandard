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
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Fuzzing code for PEM import.
    /// </summary>
    public static partial class FuzzableCode
    {
        /// <summary>
        /// AOT consideration: keep the fuzz surface on public certificate APIs; net48 uses
        /// the BouncyCastle PEMReader implementation behind the same byte-array call shape.
        /// </summary>
        /// <param name="text"></param>
        public static void AflfuzzPemImportCertificate(string text)
        {
            FuzzPemImportCertificateCore(Encoding.UTF8.GetBytes(text));
        }

        public static void LibfuzzPemImportCertificate(ReadOnlySpan<byte> input)
        {
            FuzzPemImportCertificateCore(input.ToArray());
        }

        public static void AflfuzzPemImportPrivateKey(string text)
        {
            FuzzPemImportPrivateKeyCore(Encoding.UTF8.GetBytes(text));
        }

        public static void LibfuzzPemImportPrivateKey(ReadOnlySpan<byte> input)
        {
            FuzzPemImportPrivateKeyCore(input.ToArray());
        }

        internal static void FuzzPemImportCertificateCore(byte[] input)
        {
            try
            {
                X509Certificate2Collection certificates = PEMReader.ImportPublicKeysFromPEM(input);
                _ = certificates.Count;
            }
            catch (CryptographicException)
            {
            }
            catch (ArgumentException)
            {
            }
            catch (FormatException)
            {
            }
        }

        internal static void FuzzPemImportPrivateKeyCore(byte[] input)
        {
            try
            {
                _ = PEMReader.ContainsPrivateKey(input);
                using RSA privateKey = PEMReader.ImportRsaPrivateKeyFromPEM(input, default);
                _ = privateKey.KeySize;
            }
            catch (CryptographicException)
            {
            }
            catch (ArgumentException)
            {
            }
            catch (FormatException)
            {
            }
        }
    }
}
