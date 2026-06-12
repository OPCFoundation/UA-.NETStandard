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
using System.Formats.Asn1;
using System.IO;
using System.Security.Cryptography;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Fuzzing code for PKCS#10 CSR decoding.
    /// </summary>
    public static partial class FuzzableCode
    {
        public static void AflfuzzPkcs10CertificationRequest(Stream stream)
        {
            FuzzPkcs10CertificationRequestCore(ReadAllBytes(stream));
        }

        public static void LibfuzzPkcs10CertificationRequest(ReadOnlySpan<byte> input)
        {
            FuzzPkcs10CertificationRequestCore(input.ToArray());
        }

        internal static void FuzzPkcs10CertificationRequestCore(byte[] input)
        {
            try
            {
                var request = new Pkcs10CertificationRequest(input);
                _ = request.Subject;
                _ = request.SubjectPublicKeyInfo;
                _ = request.Attributes;
                _ = request.GetCertificationRequestInfo();
                _ = request.Verify();
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
            catch (AsnContentException)
            {
            }
            catch (NotSupportedException)
            {
                // Pkcs10CertificationRequest.Verify() deliberately rethrows
                // NotSupportedException to inform the caller when the key
                // algorithm is unsupported or platform-specific support is
                // missing (e.g. ECDSA CSR verification on .NET Framework 4.8
                // and .NET Standard 2.x throws because ImportSubjectPublicKeyInfo
                // is unavailable). Treat as a parser-rejected input rather than
                // a fuzz finding.
            }
        }
    }
}
