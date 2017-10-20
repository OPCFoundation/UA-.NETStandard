/* ========================================================================
 * Copyright (c) 2005-2017 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;



namespace Opc.Ua.Gds.Server.Database
{
    [Serializable]
    public abstract class ApplicationsDatabaseBase : IApplicationsDatabase
    {
        #region IApplicationsDatabase Members
        public virtual void Initialize()
        {
        }

        public ushort NamespaceIndex { get; set; }

        public virtual NodeId RegisterApplication(
            ApplicationRecordDataType application
            )
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            if (application.ApplicationUri == null)
            {
                throw new ArgumentNullException("ApplicationUri");
            }

            if (!Uri.IsWellFormedUriString(application.ApplicationUri, UriKind.Absolute))
            {
                throw new ArgumentException(application.ApplicationUri + " is not a valid URI.", "ApplicationUri");
            }

            if (application.ApplicationType < ApplicationType.Server || application.ApplicationType > ApplicationType.DiscoveryServer)
            {
                throw new ArgumentException(application.ApplicationType.ToString() + " is not a valid ApplicationType.", "ApplicationType");
            }

            if (application.ApplicationNames == null || application.ApplicationNames.Count == 0 || LocalizedText.IsNullOrEmpty(application.ApplicationNames[0]))
            {
                throw new ArgumentException("At least one ApplicationName must be provided.", "ApplicationNames");
            }

            if (String.IsNullOrEmpty(application.ProductUri))
            {
                throw new ArgumentException("A ProductUri must be provided.", "ProductUri");
            }

            if (!Uri.IsWellFormedUriString(application.ProductUri, UriKind.Absolute))
            {
                throw new ArgumentException(application.ProductUri + " is not a valid URI.", "ProductUri");
            }

            if (application.DiscoveryUrls != null)
            {
                foreach (var discoveryUrl in application.DiscoveryUrls)
                {
                    if (String.IsNullOrEmpty(discoveryUrl))
                    {
                        continue;
                    }

                    if (!Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                    {
                        throw new ArgumentException(discoveryUrl + " is not a valid URL.", "DiscoveryUrls");
                    }
                }
            }

            if (application.ApplicationType != ApplicationType.Client)
            {
                if (application.DiscoveryUrls == null || application.DiscoveryUrls.Count == 0)
                {
                    throw new ArgumentException("At least one DiscoveryUrl must be provided.", "DiscoveryUrls");
                }
            }
            else
            {
                if (application.DiscoveryUrls != null && application.DiscoveryUrls.Count > 0)
                {
                    throw new ArgumentException("DiscoveryUrls must not be specified for clients.", "DiscoveryUrls");
                }
            }

            if (application.ApplicationType != ApplicationType.Client)
            {
                if (application.ServerCapabilities == null || application.ServerCapabilities.Count == 0)
                {
                    throw new ArgumentException("At least one Server Capability must be provided.", "ServerCapabilities");
                }
            }

            Guid applicationId = Guid.Empty;
            if (!NodeId.IsNull(application.ApplicationId))
            {
                if (application.ApplicationId.IdType != IdType.Guid)
                {
                    throw new ArgumentException("The ApplicationId to does refer to a existing record.", "ApplicationId");
                }

                applicationId = (Guid)application.ApplicationId.Identifier;
            }

            return new NodeId(applicationId, NamespaceIndex);
        }

        public virtual NodeId CreateCertificateRequest(
            NodeId applicationId,
            byte[] certificate,
            byte[] privateKey,
            string authorityId)
        {
            Guid id = GetNodeIdGuid(applicationId);
            return new NodeId(Guid.Empty, NamespaceIndex);
        }

        public virtual void ApproveCertificateRequest(NodeId requestId, bool isRejected)
        {
            Guid id = GetNodeIdGuid(requestId);
        }

        public virtual bool CompleteCertificateRequest(
            NodeId applicationId,
            NodeId requestId,
            out byte[] certificate,
            out byte[] privateKey)
        {
            certificate = null;
            privateKey = null;
            Guid appId = GetNodeIdGuid(applicationId);
            Guid reqId = GetNodeIdGuid(requestId);
            return false;
        }

        public virtual void UnregisterApplication(
            NodeId applicationId,
            out byte[] certificate,
            out byte[] httpsCertificate)
        {
            certificate = null;
            httpsCertificate = null;
            Guid id = GetNodeIdGuid(applicationId);
        }

        public virtual ApplicationRecordDataType GetApplication(
            NodeId applicationId
            )
        {
            Guid id = GetNodeIdGuid(applicationId);
            return null;
        }

        public virtual ApplicationRecordDataType[] FindApplications(
            string applicationUri
            )
        {
            return null;
        }

        public virtual ServerOnNetwork[] QueryServers(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            string[] serverCapabilities,
            out DateTime lastCounterResetTime)
        {
            lastCounterResetTime = DateTime.MinValue;
            return null;
        }

        public virtual bool SetApplicationCertificate(
            NodeId applicationId,
            byte[] certificate,
            bool isHttpsCertificate)
        {
            Guid id = GetNodeIdGuid(applicationId);
            return false;
        }

        public virtual bool SetApplicationTrustLists(
            NodeId applicationId,
            NodeId trustListId,
            NodeId httpsTrustListId)
        {
            Guid id = GetNodeIdGuid(applicationId);
            if (NodeId.IsNull(applicationId))
            {
                throw new ArgumentNullException(nameof(applicationId));
            }

            return false;
        }
        #endregion
        #region Public Menbers
        /// <summary>
        /// Returns true if the target string matches the UA pattern string. 
        /// The pattern string may include UA wildcards %_\[]!
        /// </summary>
        /// <param name="target">String to check for a pattern match.</param>
        /// <param name="pattern">Pattern to match with the target string.</param>
        /// <returns>true if the target string matches the pattern, otherwise false.</returns>
        public static bool Match(string target, string pattern)
        {
            if (String.IsNullOrEmpty(target))
            {
                return false;
            }

            if (String.IsNullOrEmpty(pattern))
            {
                return true;
            }

            var tokens = Parse(pattern);

            int targetIndex = 0;

            for (int ii = 0; ii < tokens.Count; ii++)
            {
                targetIndex = Match(target, targetIndex, tokens, ref ii);

                if (targetIndex < 0)
                {
                    return false;
                }
            }

            if (targetIndex < target.Length)
            {
                return false;
            }

            return true;
        }
        public string ServerCapabilities(ApplicationRecordDataType application)
        {
            if (application.ApplicationType != ApplicationType.Client)
            {
                if (application.ServerCapabilities == null || application.ServerCapabilities.Count == 0)
                {
                    throw new ArgumentException("At least one Server Capability must be provided.", "ServerCapabilities");
                }
            }

            StringBuilder capabilities = new StringBuilder();
            if (application.ServerCapabilities != null)
            {
                foreach (var capability in application.ServerCapabilities)
                {
                    if (String.IsNullOrEmpty(capability))
                    {
                        continue;
                    }

                    if (capabilities.Length > 0)
                    {
                        capabilities.Append(',');
                    }

                    capabilities.Append(capability);
                }
            }

            return capabilities.ToString();
        }
        public Guid GetNodeIdGuid(
            NodeId nodeId
            )
        {
            if (NodeId.IsNull(nodeId))
            {
                throw new ArgumentNullException(nameof(nodeId));
            }

            if (nodeId.IdType != IdType.Guid || NamespaceIndex != nodeId.NamespaceIndex)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }

            Guid? id = nodeId.Identifier as Guid?;

            if (id == null)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            else
            {
                return (Guid)id;
            }
        }
        #endregion
        #region Private Members
        private static List<string> Parse(string pattern)
        {
            List<string> tokens = new List<string>();

            int ii = 0;
            var buffer = new System.Text.StringBuilder();

            while (ii < pattern.Length)
            {
                char ch = pattern[ii];

                if (ch == '\\')
                {
                    ii++;

                    if (ii >= pattern.Length)
                    {
                        break;
                    }

                    buffer.Append(pattern[ii]);
                    ii++;
                    continue;
                }

                if (ch == '_')
                {
                    if (buffer.Length > 0)
                    {
                        tokens.Add(buffer.ToString());
                        buffer.Length = 0;
                    }

                    tokens.Add("_");
                    ii++;
                    continue;
                }

                if (ch == '%')
                {
                    if (buffer.Length > 0)
                    {
                        tokens.Add(buffer.ToString());
                        buffer.Length = 0;
                    }

                    tokens.Add("%");
                    ii++;

                    while (ii < pattern.Length && pattern[ii] == '%')
                    {
                        ii++;
                    }

                    continue;
                }

                if (ch == '[')
                {
                    if (buffer.Length > 0)
                    {
                        tokens.Add(buffer.ToString());
                        buffer.Length = 0;
                    }

                    buffer.Append(ch);
                    ii++;

                    int start = 0;
                    int end = 0;
                    while (ii < pattern.Length && pattern[ii] != ']')
                    {
                        if (pattern[ii] == '-' && ii > 0 && ii < pattern.Length - 1)
                        {
                            start = Convert.ToInt32(pattern[ii - 1]) + 1;
                            end = Convert.ToInt32(pattern[ii + 1]);

                            while (start < end)
                            {
                                buffer.Append(Convert.ToChar(start));
                                start++;
                            }

                            buffer.Append(Convert.ToChar(end));
                            ii += 2;
                            continue;
                        }

                        buffer.Append(pattern[ii]);
                        ii++;
                    }

                    buffer.Append("]");
                    tokens.Add(buffer.ToString());
                    buffer.Length = 0;

                    ii++;
                    continue;
                }

                buffer.Append(ch);
                ii++;
            }

            if (buffer.Length > 0)
            {
                tokens.Add(buffer.ToString());
                buffer.Length = 0;
            }

            return tokens;
        }

        private static int SkipToNext(string target, int targetIndex, IList<string> tokens, ref int tokenIndex)
        {
            if (targetIndex >= target.Length - 1)
            {
                return targetIndex + 1;
            }

            if (tokenIndex >= tokens.Count - 1)
            {
                return target.Length + 1;
            }


            if (!tokens[tokenIndex + 1].StartsWith("[^", StringComparison.Ordinal))
            {
                int nextTokenIndex = tokenIndex + 1;

                // skip over unmatched chars.
                while (targetIndex < target.Length && Match(target, targetIndex, tokens, ref nextTokenIndex) < 0)
                {
                    targetIndex++;
                    nextTokenIndex = tokenIndex + 1;
                }

                nextTokenIndex = tokenIndex + 1;

                // skip over duplicate matches.
                while (targetIndex < target.Length && Match(target, targetIndex, tokens, ref nextTokenIndex) >= 0)
                {
                    targetIndex++;
                    nextTokenIndex = tokenIndex + 1;
                }

                // return last match.
                if (targetIndex <= target.Length)
                {
                    return targetIndex - 1;
                }
            }
            else
            {
                int start = targetIndex;
                int nextTokenIndex = tokenIndex + 1;

                // skip over matches.
                while (targetIndex < target.Length && Match(target, targetIndex, tokens, ref nextTokenIndex) >= 0)
                {
                    targetIndex++;
                    nextTokenIndex = tokenIndex + 1;
                }

                // no match in string.
                if (targetIndex < target.Length)
                {
                    return -1;
                }

                // try the next token.
                if (tokenIndex >= tokens.Count - 2)
                {
                    return target.Length + 1;
                }

                tokenIndex++;

                return SkipToNext(target, start, tokens, ref tokenIndex);
            }

            return -1;
        }

        private static int Match(string target, int targetIndex, IList<string> tokens, ref int tokenIndex)
        {
            if (tokens == null || tokenIndex < 0 || tokenIndex >= tokens.Count)
            {
                return -1;
            }

            if (target == null || targetIndex < 0 || targetIndex >= target.Length)
            {
                if (tokens[tokenIndex] == "%" && tokenIndex == tokens.Count - 1)
                {
                    return targetIndex;
                }

                return -1;
            }

            string token = tokens[tokenIndex];

            if (token == "_")
            {
                if (targetIndex >= target.Length)
                {
                    return -1;
                }

                return targetIndex + 1;
            }

            if (token == "%")
            {
                return SkipToNext(target, targetIndex, tokens, ref tokenIndex);
            }

            if (token.StartsWith("[", StringComparison.Ordinal))
            {
                bool inverse = false;
                bool match = false;

                for (int ii = 1; ii < token.Length - 1; ii++)
                {
                    if (token[ii] == '^')
                    {
                        inverse = true;
                        continue;
                    }

                    if (!inverse && target[targetIndex] == token[ii])
                    {
                        return targetIndex + 1;
                    }

                    match |= (inverse && target[targetIndex] == token[ii]);
                }

                if (inverse && !match)
                {
                    return targetIndex + 1;
                }

                return -1;
            }

            if (target.Substring(targetIndex).StartsWith(token, StringComparison.Ordinal))
            {
                return targetIndex + token.Length;
            }

            return -1;
        }
        #endregion
    }
}
