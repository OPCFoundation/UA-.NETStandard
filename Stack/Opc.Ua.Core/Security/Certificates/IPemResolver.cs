using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// An abstract interface for the Pem Resolver.
    /// </summary>
    public interface IPemResolver
    {
        /// <summary>
        /// Load unencrypted/encrypted private key from pem file
        /// </summary>
        /// <param name="publicKeyfile">The public key file info</param>
        /// <param name="privateKeyFile">The private key file info</param>
        /// <param name="password">The password for the certificate</param>
        /// <returns>Certificate with the private key</returns>
        X509Certificate2 LoadPrivateKeyFromPem(FileInfo publicKeyfile, FileInfo privateKeyFile, string password = null);
    }
}
