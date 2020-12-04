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
using System.Security.Cryptography.X509Certificates;
using System.Collections.Generic;
using System.Linq;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Represents a revoked certificate in the
    /// revoked certificates sequence of a CRL.
    /// </summary>
    /// <remarks>
    /// CRL fields -- https://tools.ietf.org/html/rfc5280#section-5.1
    /// 
    ///    ...
    ///    revokedCertificates     SEQUENCE OF SEQUENCE  {
    ///        userCertificate         CertificateSerialNumber,
    ///        revocationDate          Time,
    ///        crlEntryExtensions      Extensions OPTIONAL
    ///                              -- if present, version MUST be v2
    ///                            }  OPTIONAL,
    ///   ...
    ///</remarks>
    public class RevokedCertificate
    {
        /// <summary>
        /// Construct revoked certificate with serialnumber,
        /// actual UTC time and the CRL reason.
        /// </summary>
        /// <param name="serialNumber">The serial number</param>
        /// <param name="crlReason">The reason for revocation</param>
        public RevokedCertificate(string serialNumber, CRLReason crlReason)
            : this(serialNumber)
        {
            CrlEntryExtensions.Add(X509Extensions.BuildX509CRLReason(crlReason));
        }

        /// <summary>
        /// Construct revoked certificate with serialnumber,
        /// actual UTC time and the CRL reason.
        /// </summary>
        /// <param name="serialNumber">The serial number</param>
        /// <param name="crlReason">The reason for revocation</param>
        public RevokedCertificate(byte[] serialNumber, CRLReason crlReason)
            : this(serialNumber)
        {
            if (crlReason != CRLReason.Unspecified)
            {
                CrlEntryExtensions.Add(X509Extensions.BuildX509CRLReason(crlReason));
            }
        }

        /// <summary>
        /// Construct minimal revoked certificate
        /// with serialnumber and actual UTC time.
        /// </summary>
        /// <param name="serialNumber"></param>
        public RevokedCertificate(string serialNumber) : this()
        {
            UserCertificate = serialNumber.FromHexString().Reverse().ToArray();
        }

        /// <summary>
        /// Construct minimal revoked certificate
        /// with serialnumber and actual UTC time.
        /// </summary>
        /// <param name="serialNumber"></param>
        public RevokedCertificate(byte[] serialNumber) : this()
        {
            UserCertificate = serialNumber;
        }

        private RevokedCertificate()
        {
            RevocationDate = DateTime.UtcNow;
            CrlEntryExtensions = new X509ExtensionCollection();
        }

        /// <summary>
        /// The serial number of the revoked certificate as
        /// big endian hex string.
        /// </summary>
        public string SerialNumber => UserCertificate.ToHexString(true);

        /// <summary>
        /// The serial number of the revoked user certificate
        /// as a little endian byte array.
        /// </summary>
        public byte[] UserCertificate { get; }

        /// <summary>
        /// The UTC time of the revocation event.
        /// </summary>
        public DateTime RevocationDate { get; set; }

        /// <summary>
        /// The list of crl entry extensions.
        /// </summary>
        public X509ExtensionCollection CrlEntryExtensions { get; }
    }
}
