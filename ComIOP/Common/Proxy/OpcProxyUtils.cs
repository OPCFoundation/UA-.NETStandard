/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using Opc.Ua;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Opc.Ua.Com
{
    /// <summary>
    /// A helper class for COpcProxyUtils.cpp
    /// </summary>
    public class ProxyUtils
    {
        /// <summary>
        /// Synchronous helper implementation of CheckApplicationInstanceCertificate for C++ Proxy
        /// </summary>
        public static void CheckApplicationInstanceCertificate(ApplicationConfiguration configuration)
        {
            // create a default certificate id none specified.
            CertificateIdentifier id = configuration.SecurityConfiguration.ApplicationCertificate;

            if (id == null)
            {
                id = new CertificateIdentifier();
                id.StoreType = Utils.DefaultStoreType;
                id.StorePath = Utils.DefaultStorePath;
                id.SubjectName = configuration.ApplicationName;
            }

            // check for certificate with a private key.
            X509Certificate2 certificate = id.Find(true).Result;

            if (certificate != null)
            {
                return;
            }

            // construct the subject name from the 
            List<string> hostNames = new List<string>();
            hostNames.Add(Utils.GetHostName());

            string commonName = Utils.Format("CN={0}", configuration.ApplicationName);
            string domainName = Utils.Format("DC={0}", hostNames[0]);
            string subjectName = Utils.Format("{0}, {1}", commonName, domainName);

            // create a new certificate with a new public key pair.
            certificate = CertificateFactory.CreateCertificate(
                id.StoreType,
                id.StorePath,
                null,
                configuration.ApplicationUri,
                configuration.ApplicationName,
                subjectName,
                hostNames,
                2048,
                DateTime.UtcNow - TimeSpan.FromHours(1),
                120,
                256,
                false,
                null,
                null);

            // update and save the configuration file.
            id.Certificate = certificate;
            configuration.SaveToFile(configuration.SourceFilePath);

            // add certificate to the trusted peer store so other applications will trust it.
            ICertificateStore store = configuration.SecurityConfiguration.TrustedPeerCertificates.OpenStore();

            try
            {
                    X509Certificate2Collection certificateCollection = store.FindByThumbprint(certificate.Thumbprint).Result;
                    if (certificateCollection != null)
                    {
                        store.Add(certificateCollection[0]).Wait();
                    }
            }
            finally
            {
                store.Close();
            }

            // tell the certificate validator about the new certificate.
            configuration.CertificateValidator.Update(configuration.SecurityConfiguration).Wait();

        }

        /// <summary>
        /// Synchronous helper implementation of ApplicationConfiguration.Load for C++ Proxy
        /// </summary>
        public static ApplicationConfiguration ApplicationConfigurationLoad(string sectionName, ApplicationType applicationType)
        {
            ApplicationConfiguration config = null;
            config = ApplicationConfiguration.Load(sectionName, applicationType).Result;
            return config;
        }

    }
}