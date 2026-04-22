/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography.X509Certificates;
using System.Xml.Serialization;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Describes an application that may be registered with the GDS.
    /// </summary>
    /// <remarks>
    /// The XML wire format is preserved for backward compatibility with
    /// configuration files written by the legacy xsd-generated type. Property
    /// declaration order MUST match the original generated type so the
    /// <see cref="XmlSerializer"/> emits identical element ordering.
    /// </remarks>
    [Serializable]
    [XmlRoot(
        Namespace = "http://opcfoundation.org/schemas/GDS/RegisteredApplication.xsd",
        IsNullable = false)]
    [XmlType(Namespace = "http://opcfoundation.org/schemas/GDS/RegisteredApplication.xsd")]
    public sealed record RegisteredApplication
    {
        /// <summary>The application URI.</summary>
        public string ApplicationUri { get; set; }

        /// <summary>The human readable application name.</summary>
        public string ApplicationName { get; set; }

        /// <summary>The product URI.</summary>
        public string ProductUri { get; set; }

        /// <summary>The discovery URLs of the application.</summary>
        [XmlElement("DiscoveryUrl")]
        public string[] DiscoveryUrl { get; set; }

        /// <summary>The server capability identifiers.</summary>
        [XmlElement("ServerCapability")]
        public string[] ServerCapability { get; set; }

        /// <summary>Configuration file path.</summary>
        public string ConfigurationFile { get; set; }

        /// <summary>The base server URL.</summary>
        public string ServerUrl { get; set; }

        /// <summary>Path to the certificate store.</summary>
        public string CertificateStorePath { get; set; }

        /// <summary>Subject name for the application certificate.</summary>
        public string CertificateSubjectName { get; set; }

        /// <summary>Path to the public key portion of the application certificate.</summary>
        public string CertificatePublicKeyPath { get; set; }

        /// <summary>Path to the private key portion of the application certificate.</summary>
        public string CertificatePrivateKeyPath { get; set; }

        /// <summary>Path to the trust list store.</summary>
        public string TrustListStorePath { get; set; }

        /// <summary>Path to the issuer list store.</summary>
        public string IssuerListStorePath { get; set; }

        /// <summary>Path to the public key portion of the HTTPS certificate.</summary>
        public string HttpsCertificatePublicKeyPath { get; set; }

        /// <summary>Path to the private key portion of the HTTPS certificate.</summary>
        public string HttpsCertificatePrivateKeyPath { get; set; }

        /// <summary>Path to the HTTPS trust list store.</summary>
        public string HttpsTrustListStorePath { get; set; }

        /// <summary>Path to the HTTPS issuer list store.</summary>
        public string HttpsIssuerListStorePath { get; set; }

        /// <summary>Outstanding certificate request identifier.</summary>
        public string CertificateRequestId { get; set; }

        /// <summary>Comma separated list of additional domain names.</summary>
        public string Domains { get; set; }

        /// <summary>The registration kind.</summary>
        [XmlAttribute]
        public RegistrationType RegistrationType { get; set; }

        /// <summary>The application identifier assigned by the GDS.</summary>
        [XmlIgnore]
        public string ApplicationId { get; set; }

        /// <summary>
        /// Gets the host name to use as the HTTPS domain. Returns the IDN host
        /// of the first well formed discovery URL, with <c>localhost</c>
        /// replaced by the local host name.
        /// </summary>
        public string GetHttpsDomainName()
        {
            if (DiscoveryUrl != null)
            {
                foreach (string discoveryUrl in DiscoveryUrl)
                {
                    if (Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                    {
                        var url = new Uri(discoveryUrl);
                        return url.IdnHost
                            .Replace("localhost", Utils.GetHostName(), StringComparison.Ordinal);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves the private key format (<c>PFX</c> or <c>PEM</c>) to use
        /// based on the registration kind and the supplied set of formats
        /// supported by the server.
        /// </summary>
        public string GetPrivateKeyFormat(string[] privateKeyFormats = null)
        {
            string privateKeyFormat = "PFX";

            if (RegistrationType != RegistrationType.ServerPush)
            {
                if (!string.IsNullOrEmpty(CertificatePrivateKeyPath) &&
                    CertificatePrivateKeyPath.EndsWith("PEM", StringComparison.OrdinalIgnoreCase))
                {
                    privateKeyFormat = "PEM";
                }
            }
            else if (privateKeyFormats == null ||
                Array.IndexOf(privateKeyFormats, "PFX") < 0)
            {
                privateKeyFormat = "PEM";
            }

            return privateKeyFormat;
        }

        /// <summary>
        /// Returns the list of domain names to include in a certificate
        /// request for the application.
        /// </summary>
        public List<string> GetDomainNames(Certificate certificate)
        {
            var domainNames = new List<string>();

            if (!string.IsNullOrEmpty(Domains))
            {
                string[] domains = Domains.Split(',');
                var trimmedDomains = new List<string>();

                foreach (string domain in domains)
                {
                    string d = domain.Trim();

                    if (d.Length > 0)
                    {
                        trimmedDomains.Add(d);
                    }
                }

                if (trimmedDomains.Count > 0)
                {
                    return trimmedDomains;
                }
            }

            if (DiscoveryUrl != null)
            {
                foreach (string discoveryUrl in DiscoveryUrl)
                {
                    if (Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                    {
                        string name = new Uri(discoveryUrl).IdnHost;

                        if (name == "localhost")
                        {
                            name = Utils.GetHostName();
                        }

                        bool found = false;

                        foreach (string domainName in domainNames)
                        {
                            if (string.Equals(domainName, name, StringComparison.OrdinalIgnoreCase))
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            domainNames.Add(name);
                        }
                    }
                }
            }

            if (domainNames.Count > 0)
            {
                return domainNames;
            }

            if (certificate != null)
            {
                ArrayOf<string> names = X509Utils.GetDomainsFromCertificate(certificate);

                if (!names.IsEmpty)
                {
                    domainNames.AddRange(names);
                    return domainNames;
                }

                List<string> fields = X509Utils.ParseDistinguishedName(certificate.Subject);
                string name = null;

                foreach (string field in fields)
                {
                    if (field.StartsWith("DC=", StringComparison.Ordinal))
                    {
                        if (name != null)
                        {
                            name += ".";
                        }

                        name += field[3..];
                    }
                }

                if (!names.IsEmpty)
                {
                    domainNames.AddRange(names);
                    return domainNames;
                }
            }

            domainNames.Add(Utils.GetHostName());
            return domainNames;
        }
    }

    /// <summary>
    /// The registration kind for an application registered with the GDS.
    /// </summary>
    [Serializable]
    [XmlType(Namespace = "http://opcfoundation.org/schemas/GDS/RegisteredApplication.xsd")]
    public enum RegistrationType
    {
        /// <summary>The application is a client that pulls certificates.</summary>
        ClientPull,

        /// <summary>The application is a server that pulls certificates.</summary>
        ServerPull,

        /// <summary>The application is a server that has its certificates pushed.</summary>
        ServerPush
    }
}
