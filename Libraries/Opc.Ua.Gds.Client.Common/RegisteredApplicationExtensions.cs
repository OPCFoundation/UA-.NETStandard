/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Gds.Client
{
    public partial class RegisteredApplication
    {
        [System.Xml.Serialization.XmlIgnore]
        public string ApplicationId { get; set; }

        /// <summary>
        /// Gets the name of the HTTPS domain for the application.
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
                        return url.DnsSafeHost
                            .Replace("localhost", Utils.GetHostName(), StringComparison.Ordinal);
                    }
                }
            }

            return null;
        }

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
            else if (privateKeyFormats == null || !privateKeyFormats.Contains("PFX"))
            {
                privateKeyFormat = "PEM";
            }

            return privateKeyFormat;
        }

        public List<string> GetDomainNames(X509Certificate2 certificate)
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
                        string name = new Uri(discoveryUrl).DnsSafeHost;

                        if (name == "localhost")
                        {
                            name = Utils.GetHostName();
                        }

                        bool found = false;

                        //domainNames.Any(n => String.Compare(n, name, StringComparison.OrdinalIgnoreCase) == 0);
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

            if (domainNames != null && domainNames.Count > 0)
            {
                return domainNames;
            }

            if (certificate != null)
            {
                IList<string> names = X509Utils.GetDomainsFromCertificate(certificate);

                if (names != null && names.Count > 0)
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

                if (names != null)
                {
                    domainNames.AddRange(names);
                    return domainNames;
                }
            }

            domainNames.Add(Utils.GetHostName());
            return domainNames;
        }
    }
}
