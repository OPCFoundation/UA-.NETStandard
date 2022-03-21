using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Configure the Pem Resolver
    /// </summary>
    public class InvokePemResolver
    {
        static IPemResolver _pemResolverService;

        /// <summary>
        /// Sets the Pem Resolver implementation
        /// </summary>
        /// <param name="pemService">The Pem Resolver implementation</param>
        public static void SetPemResolver(IPemResolver pemService)
        {
            _pemResolverService = pemService;
        }

        /// <summary>
        /// Gets the Pem Resolver
        /// </summary>
        /// <returns>The Pem Resolver</returns>
        public static IPemResolver GetPemResolver()
        {
            return _pemResolverService;
        }

        /// <summary>
        /// Load unencrypted/encrypted private key from pem file
        /// </summary>
        /// <param name="publicKeyfile"></param>
        /// <param name="privateKeyFile"></param>
        /// <param name="password"></param>
        /// <returns>Certificate with the private key</returns>
        public X509Certificate2 LoadPrivateKeyFromPem(FileInfo publicKeyfile, FileInfo privateKeyFile, string password = null)
        {
            return _pemResolverService.LoadPrivateKeyFromPem(publicKeyfile, privateKeyFile, password);
        }
    }
}
