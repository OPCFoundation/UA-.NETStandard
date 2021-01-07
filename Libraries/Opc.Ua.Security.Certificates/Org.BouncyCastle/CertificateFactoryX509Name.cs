/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
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

#if !NETSTANDARD2_1 && !NET472

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
        /// Create the X509Name from a distinguished name.
        /// </summary>
        /// <param name="distinguishedName">The distinguished name.</param>
        public CertificateFactoryX509Name(string distinguishedName) :
            base(true, ConvertToX509Name(distinguishedName))
        {
        }

        /// <summary>
        /// Create the X509Name from a distinguished name.
        /// </summary>
        /// <param name="reverse">Reverse the order of the names.</param>
        /// <param name="distinguishedName">The distinguished name.</param>
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
