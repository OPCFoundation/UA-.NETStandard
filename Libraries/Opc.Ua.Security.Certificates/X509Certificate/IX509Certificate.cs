/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Security.Certificates
{

    /// <summary>
    /// Properties of a X.509v3 certificate.
    /// </summary>
    public interface IX509Certificate
    {
        /// <summary>
        /// The subject distinguished name from a certificate.
        /// </summary>
        X500DistinguishedName SubjectName { get; }

        /// <summary>
        /// The distinguished name of the certificate issuer.
        /// </summary>
        X500DistinguishedName IssuerName { get; }

        /// <summary>
        /// The date in UTC time on which a certificate becomes valid.
        /// </summary>
        DateTime NotBefore { get; }

        /// <summary>
        /// The date in UTC time after which a certificate is no longer valid.
        /// </summary>
        DateTime NotAfter { get; }

        /// <summary>
        /// The serial number of the certificate
        /// as a big-endian hexadecimal string.
        /// </summary>
        string SerialNumber { get; }

        /// <summary>
        /// The serial number of the certificate
        /// as an array of bytes in little-endian order.
        /// </summary>
        byte[] GetSerialNumber();

        /// <summary>
        /// The hash algorithm used to create the signature.
        /// </summary>
        HashAlgorithmName HashAlgorithmName { get; }

        /// <summary>
        /// A collection of X509 extensions.
        /// </summary>
        X509ExtensionCollection Extensions { get; }
    }
}

