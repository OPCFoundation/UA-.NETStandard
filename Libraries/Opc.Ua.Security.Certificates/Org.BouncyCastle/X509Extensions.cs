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

#if !NETSTANDARD2_1 && !NET472_OR_GREATER && !NET5_0_OR_GREATER

using System;
using System.Collections.Generic;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using X509Extension = System.Security.Cryptography.X509Certificates.X509Extension;

namespace Opc.Ua.Security.Certificates.BouncyCastle
{
    /// <summary>
    /// Helper functions for X509 extensions using Org.BouncyCastle.
    /// </summary>
    public static class X509Extensions
    {
        /// <summary>
        /// Build the Subject Alternate Name.
        /// </summary>
        public static X509Extension BuildSubjectAltNameExtension(IList<string> uris, IList<string> domainNames, IList<string> ipAddresses)
        {
            // subject alternate name
            var generalNames = new List<GeneralName>();
            foreach (var uri in uris)
            {
                generalNames.Add(new GeneralName(GeneralName.UniformResourceIdentifier, uri));
            }
            generalNames.AddRange(CreateSubjectAlternateNameDomains(domainNames));
            generalNames.AddRange(CreateSubjectAlternateNameDomains(ipAddresses));
            var rawData = new DerOctetString(new GeneralNames(generalNames.ToArray()).GetDerEncoded()).GetOctets();
            return new X509Extension(Org.BouncyCastle.Asn1.X509.X509Extensions.SubjectAlternativeName.Id, rawData, false);
        }

        /// <summary>
        /// helper to build alternate name domains list for certs.
        /// </summary>
        public static List<GeneralName> CreateSubjectAlternateNameDomains(IList<String> domainNames)
        {
            // subject alternate name
            var generalNames = new List<GeneralName>();
            for (int i = 0; i < domainNames.Count; i++)
            {
                int domainType = GeneralName.OtherName;
                switch (Uri.CheckHostName(domainNames[i]))
                {
                    case UriHostNameType.Dns: domainType = GeneralName.DnsName; break;
                    case UriHostNameType.IPv4:
                    case UriHostNameType.IPv6: domainType = GeneralName.IPAddress; break;
                    default: continue;
                }
                generalNames.Add(new GeneralName(domainType, domainNames[i]));
            }
            return generalNames;
        }
    }
}
#endif
