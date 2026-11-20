// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
