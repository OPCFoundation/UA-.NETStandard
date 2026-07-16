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
