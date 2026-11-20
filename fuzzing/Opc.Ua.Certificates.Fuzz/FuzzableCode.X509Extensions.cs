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
    /// Fuzzing code for X509 extension decoders.
    /// </summary>
    public static partial class FuzzableCode
    {
        public static void AflfuzzX509SubjectAltNameExtension(Stream stream)
        {
            FuzzX509SubjectAltNameExtensionCore(ReadAllBytes(stream));
        }

        public static void LibfuzzX509SubjectAltNameExtension(ReadOnlySpan<byte> input)
        {
            FuzzX509SubjectAltNameExtensionCore(input.ToArray());
        }

        public static void AflfuzzX509AuthorityKeyIdentifierExtension(Stream stream)
        {
            FuzzX509AuthorityKeyIdentifierExtensionCore(ReadAllBytes(stream));
        }

        public static void LibfuzzX509AuthorityKeyIdentifierExtension(ReadOnlySpan<byte> input)
        {
            FuzzX509AuthorityKeyIdentifierExtensionCore(input.ToArray());
        }

        public static void AflfuzzX509CrlNumberExtension(Stream stream)
        {
            FuzzX509CrlNumberExtensionCore(ReadAllBytes(stream));
        }

        public static void LibfuzzX509CrlNumberExtension(ReadOnlySpan<byte> input)
        {
            FuzzX509CrlNumberExtensionCore(input.ToArray());
        }

        internal static void FuzzX509SubjectAltNameExtensionCore(byte[] input)
        {
            try
            {
                var extension = new X509SubjectAltNameExtension(
                    X509SubjectAltNameExtension.SubjectAltName2Oid,
                    input,
                    false);
                _ = extension.Uris.Count;
                _ = extension.DomainNames.Count;
                _ = extension.IPAddresses.Count;
                _ = extension.Format(false);
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

        internal static void FuzzX509AuthorityKeyIdentifierExtensionCore(byte[] input)
        {
            try
            {
                var extension = new X509AuthorityKeyIdentifierExtension(
                    X509AuthorityKeyIdentifierExtension.AuthorityKeyIdentifier2Oid,
                    input,
                    false);
                _ = extension.GetKeyIdentifier();
                _ = extension.Issuer;
                _ = extension.Format(false);
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

        internal static void FuzzX509CrlNumberExtensionCore(byte[] input)
        {
            try
            {
                var extension = new X509CrlNumberExtension(
                    X509CrlNumberExtension.CrlNumberOid,
                    input,
                    false);
                _ = extension.CrlNumber;
                _ = extension.Format(false);
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
