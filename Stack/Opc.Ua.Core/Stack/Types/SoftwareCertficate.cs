/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// The SoftwareCertificate class.
    /// </summary>
    public partial class SoftwareCertificate
    {
        /// <summary>
        /// The SignedSoftwareCertificate that contains the SoftwareCertificate
        /// </summary>
        public X509Certificate2 SignedCertificate
        {
            get { return m_signedCertificate; }
            set { m_signedCertificate = value; }
        }

        private X509Certificate2 m_signedCertificate;

        /// <summary>
        /// Validates a software certificate.
        /// </summary>
        public static ServiceResult Validate(
            CertificateValidator validator,
            byte[] signedCertificate,
            out SoftwareCertificate softwareCertificate)
        {
            softwareCertificate = null;

            // validate the certificate.
            X509Certificate2 certificate = null;

            try
            {
                certificate = CertificateFactory.Create(signedCertificate, true);
                validator.Validate(certificate);
            }
            catch (Exception e)
            {
                return ServiceResult.Create(e, StatusCodes.BadDecodingError, "Could not decode software certificate body.");
            }


            // find the software certficate.
            byte[] encodedData = null;

            if (encodedData == null)
            {
                return ServiceResult.Create(StatusCodes.BadCertificateInvalid, "Could not find extension containing the software certficate.");
            }

            try
            {
                MemoryStream istrm = new MemoryStream(encodedData, false);
                DataContractSerializer serializer = new DataContractSerializer(typeof(SoftwareCertificate));
                softwareCertificate = (SoftwareCertificate)serializer.ReadObject(istrm);
                softwareCertificate.SignedCertificate = certificate;
            }
            catch (Exception e)
            {
                return ServiceResult.Create(e, StatusCodes.BadCertificateInvalid, "Certificate does not contain a valid SoftwareCertificate body.");
            }

            // certificate is valid.
            return ServiceResult.Good;
        }
    }
}
