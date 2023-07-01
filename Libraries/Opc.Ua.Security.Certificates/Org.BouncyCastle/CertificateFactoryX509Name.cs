/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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

#if !NETSTANDARD2_1 && !NET472_OR_GREATER && !NET5_0_OR_GREATER

using System;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;

namespace Opc.Ua.Security.Certificates.BouncyCastle
{
    /// <summary>
    /// A converter class to create a X509Name object 
    /// from a X509Certificate subject.
    /// </summary>
    /// <remarks>
    /// Handles subtle differences in the string representation
    /// of the .NET and the Bouncy Castle implementation.
    /// </remarks>
    public class CertificateFactoryX509Name : X509Name
    {
        /// <summary>
        /// Create the X509Name from a X500DistinguishedName
        /// ASN.1 encoded distinguished name.
        /// </summary>
        /// <param name="distinguishedName">The distinguished name.</param>
        public CertificateFactoryX509Name(X500DistinguishedName distinguishedName) :
            base((Asn1Sequence)Asn1Object.FromByteArray(distinguishedName.RawData))
        {
        }

        /// <summary>
        /// Create the X509Name from a distinguished name.
        /// </summary>
        /// <param name="distinguishedName">The distinguished name.</param>
        [Obsolete("Use constructor with X500DistinguishedName instead.")]
        public CertificateFactoryX509Name(string distinguishedName) :
            base(true, ConvertToX509Name(distinguishedName))
        {
        }

        /// <summary>
        /// Create the X509Name from a distinguished name.
        /// </summary>
        /// <param name="reverse">Reverse the order of the names.</param>
        /// <param name="distinguishedName">The distinguished name.</param>
        [Obsolete("Use constructor with X500DistinguishedName instead.")]
        public CertificateFactoryX509Name(bool reverse, string distinguishedName) :
            base(reverse, ConvertToX509Name(distinguishedName))
        {
        }

        private static string ConvertToX509Name(string distinguishedName)
        {
            // convert from X509Certificate to bouncy castle DN entries
            return distinguishedName.Replace("S=", "ST=");
        }
    }
}
#endif
