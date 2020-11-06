/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Opc.Ua.Security.Certificates.X509
{
    public static class X509Utils
    {

        public static T FindExtension<T>(X509Certificate2 certificate) where T : X509Extension
        {
            // search known custom extensions
            if (typeof(T) == typeof(X509AuthorityKeyIdentifierExtension))
            {
                var extension = certificate.Extensions.Cast<X509Extension>().Where(e => (
                    e.Oid.Value == X509AuthorityKeyIdentifierExtension.AuthorityKeyIdentifierOid ||
                    e.Oid.Value == X509AuthorityKeyIdentifierExtension.AuthorityKeyIdentifier2Oid)
                ).FirstOrDefault();
                if (extension != null)
                {
                    return new X509AuthorityKeyIdentifierExtension(extension, extension.Critical) as T;
                }
            }

            if (typeof(T) == typeof(X509SubjectAltNameExtension))
            {
                var extension = certificate.Extensions.Cast<X509Extension>().Where(e => (
                    e.Oid.Value == X509SubjectAltNameExtension.SubjectAltNameOid ||
                    e.Oid.Value == X509SubjectAltNameExtension.SubjectAltName2Oid)
                ).FirstOrDefault();
                if (extension != null)
                {
                    return new X509SubjectAltNameExtension(extension, extension.Critical) as T;
                }
            }

            // search builtin extension
            for (int ii = 0; ii < certificate.Extensions.Count; ii++)
            {
                T extension = certificate.Extensions[ii] as T;
                if (extension != null)
                {
                    return extension;
                }
            }

            return null;
        }


        /// <summary>
        /// Extracts the DNS names specified in the certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <returns>The DNS names.</returns>
        public static IList<string> GetDomainsFromCertficate(X509Certificate2 certificate)
        {
            List<string> dnsNames = new List<string>();

            // extracts the domain from the subject name.
            List<string> fields = ParseDistinguishedName(certificate.Subject);

            StringBuilder builder = new StringBuilder();

            for (int ii = 0; ii < fields.Count; ii++)
            {
                if (fields[ii].StartsWith("DC="))
                {
                    if (builder.Length > 0)
                    {
                        builder.Append('.');
                    }

                    builder.Append(fields[ii].Substring(3));
                }
            }

            if (builder.Length > 0)
            {
                dnsNames.Add(builder.ToString().ToUpperInvariant());
            }

            // extract the alternate domains from the subject alternate name extension.
            X509SubjectAltNameExtension alternateName = null;

            foreach (X509Extension extension in certificate.Extensions)
            {
                if (extension.Oid.Value == X509SubjectAltNameExtension.SubjectAltNameOid || extension.Oid.Value == X509SubjectAltNameExtension.SubjectAltName2Oid)
                {
                    alternateName = new X509SubjectAltNameExtension(extension, extension.Critical);
                    break;
                }
            }

            if (alternateName != null)
            {
                for (int ii = 0; ii < alternateName.DomainNames.Count; ii++)
                {
                    string hostname = alternateName.DomainNames[ii];

                    // do not add duplicates to the list.
                    bool found = false;

                    for (int jj = 0; jj < dnsNames.Count; jj++)
                    {
                        if (String.Compare(dnsNames[jj], hostname, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        dnsNames.Add(hostname.ToUpperInvariant());
                    }
                }

                for (int ii = 0; ii < alternateName.IPAddresses.Count; ii++)
                {
                    string ipAddress = alternateName.IPAddresses[ii];

                    if (!dnsNames.Contains(ipAddress))
                    {
                        dnsNames.Add(ipAddress);
                    }
                }
            }

            // return the list.
            return dnsNames;
        }

        /// <summary>
        /// Extracts the application URI specified in the certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <returns>The application URI.</returns>
        public static string GetApplicationUriFromCertificate(X509Certificate2 certificate)
        {
            // extract the alternate domains from the subject alternate name extension.
            X509SubjectAltNameExtension alternateName = X509Utils.FindExtension<X509SubjectAltNameExtension>(certificate);

            // get the application uri.
            if (alternateName != null && alternateName.Uris.Count > 0)
            {
                return alternateName.Uris[0];
            }

            return string.Empty;
        }

        /// <summary>
        /// Check if certificate has an application urn.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <returns>true if the application URI starts with urn: </returns>
        public static bool HasApplicationURN(X509Certificate2 certificate)
        {
            // extract the alternate domains from the subject alternate name extension.
            X509SubjectAltNameExtension alternateName = X509Utils.FindExtension<X509SubjectAltNameExtension>(certificate);

            // find the application urn.
            if (alternateName != null && alternateName.Uris.Count > 0)
            {
                string urn = "urn:";
                for (int i = 0; i < alternateName.Uris.Count; i++)
                {
                    if (string.Compare(alternateName.Uris[i], 0, urn, 0, urn.Length, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Checks that the domain in the URL provided matches one of the domains in the certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <param name="endpointUrl">The endpoint url to verify.</param>
        /// <returns>True if the certificate matches the url.</returns>
        public static bool DoesUrlMatchCertificate(X509Certificate2 certificate, Uri endpointUrl)
        {
            if (endpointUrl == null || certificate == null)
            {
                return false;
            }

            IList<string> domainNames = GetDomainsFromCertficate(certificate);

            for (int jj = 0; jj < domainNames.Count; jj++)
            {
                if (String.Compare(domainNames[jj], endpointUrl.DnsSafeHost, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Compares two distinguished names.
        /// </summary>
        public static bool CompareDistinguishedName(string name1, string name2)
        {
            // check for simple equality.
            if (String.Compare(name1, name2, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }

            // parse the names.
            List<string> fields1 = ParseDistinguishedName(name1);
            List<string> fields2 = ParseDistinguishedName(name2);

            // can't be equal if the number of fields is different.
            if (fields1.Count != fields2.Count)
            {
                return false;
            }

            // sort to ensure similar entries are compared
            fields1.Sort(StringComparer.OrdinalIgnoreCase);
            fields2.Sort(StringComparer.OrdinalIgnoreCase);

            // compare each.
            for (int ii = 0; ii < fields1.Count; ii++)
            {
                if (String.Compare(fields1[ii], fields2[ii], StringComparison.OrdinalIgnoreCase) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Compares two distinguished names.
        /// </summary>
        public static bool CompareDistinguishedName(X509Certificate2 certificate, List<string> parsedName)
        {
            // can't compare if the number of fields is 0.
            if (parsedName.Count == 0)
            {
                return false;
            }

            // parse the names.
            List<string> certificateName = ParseDistinguishedName(certificate.Subject);

            // can't be equal if the number of fields is different.
            if (parsedName.Count != certificateName.Count)
            {
                return false;
            }

            // sort to ensure similar entries are compared
            parsedName.Sort(StringComparer.OrdinalIgnoreCase);
            certificateName.Sort(StringComparer.OrdinalIgnoreCase);

            // compare each entry
            for (int ii = 0; ii < parsedName.Count; ii++)
            {
                if (String.Compare(parsedName[ii], certificateName[ii], StringComparison.OrdinalIgnoreCase) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Parses a distingushed name.
        /// </summary>
        public static List<string> ParseDistinguishedName(string name)
        {
            List<string> fields = new List<string>();

            if (String.IsNullOrEmpty(name))
            {
                return fields;
            }

            // determine the delimiter used.
            char delimiter = ',';
            bool found = false;
            bool quoted = false;

            for (int ii = name.Length - 1; ii >= 0; ii--)
            {
                char ch = name[ii];

                if (ch == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (!quoted && ch == '=')
                {
                    ii--;

                    while (ii >= 0 && Char.IsWhiteSpace(name[ii])) ii--;
                    while (ii >= 0 && (Char.IsLetterOrDigit(name[ii]) || name[ii] == '.')) ii--;
                    while (ii >= 0 && Char.IsWhiteSpace(name[ii])) ii--;

                    if (ii >= 0)
                    {
                        delimiter = name[ii];
                    }

                    break;
                }
            }

            StringBuilder buffer = new StringBuilder();

            string key = null;
            string value = null;
            found = false;

            for (int ii = 0; ii < name.Length; ii++)
            {
                while (ii < name.Length && Char.IsWhiteSpace(name[ii])) ii++;

                if (ii >= name.Length)
                {
                    break;
                }

                char ch = name[ii];

                if (found)
                {
                    char end = delimiter;

                    if (ii < name.Length && name[ii] == '"')
                    {
                        ii++;
                        end = '"';
                    }

                    while (ii < name.Length)
                    {
                        ch = name[ii];

                        if (ch == end)
                        {
                            while (ii < name.Length && name[ii] != delimiter) ii++;
                            break;
                        }

                        buffer.Append(ch);
                        ii++;
                    }

                    value = buffer.ToString().TrimEnd();
                    found = false;

                    buffer.Length = 0;
                    buffer.Append(key);
                    buffer.Append('=');

                    if (value.IndexOfAny(new char[] { '/', ',', '=' }) != -1)
                    {
                        if (value.Length > 0 && value[0] != '"')
                        {
                            buffer.Append('"');
                        }

                        buffer.Append(value);

                        if (value.Length > 0 && value[value.Length - 1] != '"')
                        {
                            buffer.Append('"');
                        }
                    }
                    else
                    {
                        buffer.Append(value);
                    }

                    fields.Add(buffer.ToString());
                    buffer.Length = 0;
                }

                else
                {
                    while (ii < name.Length)
                    {
                        ch = name[ii];

                        if (ch == '=')
                        {
                            break;
                        }

                        buffer.Append(ch);
                        ii++;
                    }

                    key = buffer.ToString().TrimEnd().ToUpperInvariant();
                    buffer.Length = 0;
                    found = true;
                }
            }

            return fields;
        }

    }
}
