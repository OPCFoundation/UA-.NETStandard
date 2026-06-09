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
    /// Fuzzing code for X509 CRL decoding.
    /// </summary>
    public static partial class FuzzableCode
    {
        /// <summary>
        /// The X509 CRL fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzX509CRL(Stream stream)
        {
            FuzzX509CRLCore(ReadAllBytes(stream));
        }

        /// <summary>
        /// The X509 CRL fuzz target for libFuzzer.
        /// </summary>
        public static void LibfuzzX509CRL(ReadOnlySpan<byte> input)
        {
            FuzzX509CRLCore(input.ToArray());
        }

        internal static void FuzzX509CRLCore(byte[] input)
        {
            try
            {
                var crl = new X509CRL(input);
                _ = crl.Issuer;
                _ = crl.IssuerName;
                _ = crl.ThisUpdate;
                _ = crl.NextUpdate;
                _ = crl.HashAlgorithmName;
                _ = crl.RawData;
                foreach (RevokedCertificate revokedCertificate in crl.RevokedCertificates)
                {
                    _ = revokedCertificate.SerialNumber;
                    _ = revokedCertificate.RevocationDate;
                    foreach (var extension in revokedCertificate.CrlEntryExtensions)
                    {
                        _ = extension.Format(false);
                    }
                }
                foreach (var extension in crl.CrlExtensions)
                {
                    _ = extension.Format(false);
                }
                _ = crl.ToString();
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
        }
    }
}
