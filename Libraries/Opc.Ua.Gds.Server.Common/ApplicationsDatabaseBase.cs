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

                if (application.ServerCapabilities == null || application.ServerCapabilities.Count == 0)
                {
                    application.ServerCapabilities = new StringCollection() { "NA" };
                }
            }
            else
            {
                if (application.DiscoveryUrls != null && application.DiscoveryUrls.Count > 0)
                {
                    throw new ArgumentException("DiscoveryUrls must not be specified for clients.", "DiscoveryUrls");
                }
            }

            NodeId nodeId = new NodeId();
            if (!NodeId.IsNull(application.ApplicationId))
            {
                // verify node integrity
                switch (application.ApplicationId.IdType)
                {
                    case IdType.Guid:
                        nodeId = new NodeId((Guid)application.ApplicationId.Identifier, NamespaceIndex);
                        break;
                    case IdType.String:
                        nodeId = new NodeId((string)application.ApplicationId.Identifier, NamespaceIndex);
                        break;
                    default:
                        throw new ArgumentException("The ApplicationId has invalid type {0}", application.ApplicationId.ToString());
                }
            }

            return nodeId;
        }

        public virtual void UnregisterApplication(NodeId applicationId)
        {
            ValidateApplicationNodeId(applicationId);
        }

        public virtual ApplicationRecordDataType GetApplication(
            NodeId applicationId
            )
        {
            ValidateApplicationNodeId(applicationId);
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

        public virtual ApplicationDescription[] QueryApplications(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            uint applicationType,
            string productUri,
            string[] serverCapabilities,
            out DateTime lastCounterResetTime,
            out uint nextRecordId
            )
        {
            lastCounterResetTime = DateTime.MinValue;
            nextRecordId = 0;
            return null;
        }

        public virtual bool SetApplicationCertificate(
            NodeId applicationId,
            string certificateType,
            byte[] certificate
            )
        {
            ValidateApplicationNodeId(applicationId);
            return false;
        }

        public virtual bool GetApplicationCertificate(
            NodeId applicationId,
            string certificateTypeId,
            out byte[] certificate)
        {
            certificate = null;
            ValidateApplicationNodeId(applicationId);
            return false;
        }

        public virtual bool SetApplicationTrustLists(
            NodeId applicationId,
            string certificateTypeId,
            string trustListId)
        {
            ValidateApplicationNodeId(applicationId);
            return false;
        }
        public virtual bool GetApplicationTrustLists(
            NodeId applicationId,
            string certificateTypeId,
            out string trustListId)
        {
            trustListId = null;
            ValidateApplicationNodeId(applicationId);
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

        /// <summary>
        /// Returns true if the pattern string contains a UA pattern. 
        /// The pattern string may include UA wildcards %_\[]!
        /// </summary>

        public static bool IsMatchPattern(string pattern)
        {
            var patternChars = new char[] { '%', '_', '\\', '[', ']', '!' };
            if (String.IsNullOrEmpty(pattern))
            {
                return false;
            }

            foreach (var patternChar in patternChars)
            {
                if (pattern.Contains(patternChar))
                {
                    return true;
                }
            }
            return false;
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
                application.ServerCapabilities.Sort();
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

        protected Guid GetNodeIdGuid(
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
            return (Guid)id;
        }

        protected string GetNodeIdString(
            NodeId nodeId
            )
        {
            if (NodeId.IsNull(nodeId))
            {
                return null;
            }

            if (nodeId.IdType != IdType.String || NamespaceIndex != nodeId.NamespaceIndex)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }

            string id = nodeId.Identifier as string;

            if (id == null)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            return id;
        }

        protected void ValidateApplicationNodeId(
            NodeId nodeId
            )
        {
            if (NodeId.IsNull(nodeId))
            {
                throw new ArgumentNullException(nameof(nodeId));
            }

            if ((nodeId.IdType != IdType.Guid && nodeId.IdType != IdType.String) ||
                NamespaceIndex != nodeId.NamespaceIndex)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }

            if (nodeId.IdType == IdType.Guid)
            {
                // test if identifier is a valid Guid
                Guid? id = nodeId.Identifier as Guid?;

                if (id == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }
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
