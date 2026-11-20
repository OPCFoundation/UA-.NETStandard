/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
