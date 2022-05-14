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

using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Configure the Pem Resolver
    /// </summary>
    public class InvokePemResolver : IPemResolver
    {
        private static IPemResolver s_pemResolverService;

        /// <summary>
        /// Sets the Pem Resolver implementation
        /// </summary>
        /// <param name="pemService">The Pem Resolver implementation</param>
        public static void SetPemResolver(IPemResolver pemService)
        {
            s_pemResolverService = pemService;
        }

        /// <summary>
        /// Gets the Pem Resolver
        /// </summary>
        /// <returns>The Pem Resolver</returns>
        public static IPemResolver GetPemResolver()
        {
            return s_pemResolverService;
        }

        /// <inheritdoc/>
        public bool Active => s_pemResolverService != null;

        /// <inheritdoc/>
        public X509Certificate2 LoadPrivateKeyFromPem(FileInfo publicKeyfile, FileInfo privateKeyFile, string password = null)
        {
            return s_pemResolverService?.LoadPrivateKeyFromPem(publicKeyfile, privateKeyFile, password);
        }
    }
}
