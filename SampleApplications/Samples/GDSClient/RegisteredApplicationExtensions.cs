using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Opc.Ua.Gds
{
    public partial class RegisteredApplication
    {
        [System.Xml.Serialization.XmlIgnore()]
        public string ApplicationId { get; set; }

        /// <summary>
        /// Gets the name of the HTTPS domain for the application.
        /// </summary>
        /// <returns></returns>
        public string GetHttpsDomainName()
        {
            if (this.DiscoveryUrl != null)
            {
                foreach (string disoveryUrl in this.DiscoveryUrl)
                {
                    if (Uri.IsWellFormedUriString(disoveryUrl, UriKind.Absolute))
                    {
                        Uri url = new Uri(disoveryUrl);
                        return url.DnsSafeHost.Replace("localhost", System.Net.Dns.GetHostName());
                    }
                }
            }

            return null;
        }
    }
}
