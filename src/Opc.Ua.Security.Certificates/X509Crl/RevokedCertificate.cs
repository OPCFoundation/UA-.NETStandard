// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Represents a revoked certificate in the
    /// revoked certificates sequence of a CRL.
    /// </summary>
    /// <remarks>
    /// <para>CRL fields -- https://tools.ietf.org/html/rfc5280#section-5.1</para>
    /// <para>
    ///    ...
    ///    revokedCertificates     SEQUENCE OF SEQUENCE  {
    ///        userCertificate         CertificateSerialNumber,
    ///        revocationDate          Time,
    ///        crlEntryExtensions      Extensions OPTIONAL
    ///                              -- if present, version MUST be v2
    ///                            }  OPTIONAL,
    ///   ...
    /// </para>
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
            CrlEntryExtensions.Add(crlReason.BuildX509CRLReason());
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
                CrlEntryExtensions.Add(crlReason.BuildX509CRLReason());
            }
        }

        /// <summary>
        /// Construct minimal revoked certificate
        /// with serialnumber and actual UTC time.
        /// </summary>
        public RevokedCertificate(string serialNumber)
            : this()
        {
            UserCertificate = [.. ((IEnumerable<byte>)(serialNumber.FromHexString() ?? [])).Reverse()];
        }

        /// <summary>
        /// Construct minimal revoked certificate
        /// with serialnumber and actual UTC time.
        /// </summary>
        public RevokedCertificate(byte[] serialNumber)
            : this()
        {
            UserCertificate = serialNumber;
        }

        private RevokedCertificate()
        {
            RevocationDate = DateTime.UtcNow;
            CrlEntryExtensions = [];
            UserCertificate = null!;
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
        public byte[] UserCertificate { get; } = null!;

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
