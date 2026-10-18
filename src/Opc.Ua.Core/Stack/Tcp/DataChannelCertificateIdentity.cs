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
using System.Security.Cryptography;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Produces consistent key evidence from an already validated application certificate.
    /// </summary>
    public static class DataChannelCertificateIdentity
    {
        /// <summary>
        /// Computes SHA-256 of the certificate's DER SubjectPublicKeyInfo.
        /// This identifies the key, not the certificate, and does not establish trust.
        /// </summary>
        public static ByteString GetKeyThumbprint(Certificate certificate)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            var certificateReader = new AsnReader(certificate.RawData, AsnEncodingRules.DER);
            AsnReader signedCertificate = certificateReader.ReadSequence();
            AsnReader fields = signedCertificate.ReadSequence();
            if (fields.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                fields.ReadEncodedValue();
            }
            fields.ReadIntegerBytes();
            fields.ReadEncodedValue(); // Signature algorithm.
            fields.ReadEncodedValue(); // Issuer.
            fields.ReadEncodedValue(); // Validity.
            fields.ReadEncodedValue(); // Subject.
            ReadOnlyMemory<byte> publicKeyInfo = fields.ReadEncodedValue();
#if NET6_0_OR_GREATER
            return new ByteString(SHA256.HashData(publicKeyInfo.Span));
#else
            using SHA256 sha256 = SHA256.Create();
            return new ByteString(sha256.ComputeHash(publicKeyInfo.ToArray()));
#endif
        }
    }
}
