/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Threading.Tasks;

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
            get { return m_signedCertificate;  } 
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
