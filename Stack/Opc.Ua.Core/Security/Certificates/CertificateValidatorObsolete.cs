/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// Extension methods for ICertificateValidator.
    /// </summary>
    public static class CertificateValidatorObsolete
    {
        /// <summary>
        /// Validates a certificate.
        /// </summary>
        [Obsolete("Use ValidateAsync")]
        public static void Validate(this ICertificateValidator validator, X509Certificate2 certificate)
        {
            validator.ValidateAsync(certificate, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Validates a certificate chain.
        /// </summary>
        [Obsolete("Use ValidateAsync")]
        public static void Validate(this ICertificateValidator validator, X509Certificate2Collection certificateChain)
        {
            validator.ValidateAsync(certificateChain, CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
