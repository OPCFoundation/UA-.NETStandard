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
using System.Text;
using Opc.Ua.Security;
using Opc.Ua.Types;

namespace Opc.Ua.Gds.Server.Database
{
    [Serializable]
    public abstract class ApplicationsDatabaseBase : IApplicationsDatabase
    {
        public virtual void Initialize()
        {
        }

        public ushort NamespaceIndex { get; set; }

        public virtual NodeId UpdateApplication(ApplicationRecordDataType application)
        {
            return ValidateApplication(application);
        }

        public virtual NodeId RegisterApplication(ApplicationRecordDataType application)
        {
            return ValidateApplication(application);
        }

        private static NodeId ValidateApplication(ApplicationRecordDataType application)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            if (application.ApplicationUri == null)
            {
                throw new ArgumentNullException(nameof(application), "ApplicationUri is null");
            }

            if (!Uri.IsWellFormedUriString(application.ApplicationUri, UriKind.Absolute))
            {
                throw new ArgumentException(
                    application.ApplicationUri + " is not a valid URI.",
                    nameof(application));
            }

            if (application
                .ApplicationType is < ApplicationType.Server or > ApplicationType.DiscoveryServer)
            {
                throw new ArgumentException(
                    application.ApplicationType + " is not a valid ApplicationType.",
                    nameof(application));
            }

            if (application.ApplicationNames.IsEmpty ||
                application.ApplicationNames[0].IsNullOrEmpty)
            {
                throw new ArgumentException(
                    "At least one ApplicationName must be provided.",
                    nameof(application));
            }

            if (string.IsNullOrEmpty(application.ProductUri))
            {
                throw new ArgumentException("A ProductUri must be provided.", nameof(application));
            }

            if (!Uri.IsWellFormedUriString(application.ProductUri, UriKind.Absolute))
            {
                throw new ArgumentException(
                    application.ProductUri + " is not a valid URI.",
                    nameof(application));
            }

            foreach (string discoveryUrl in application.DiscoveryUrls)
            {
                if (string.IsNullOrEmpty(discoveryUrl))
                {
                    continue;
                }

                // Reverse-connect URLs use the "rcp+" prefix per OPC 10000-12
                // §6.5.5; the underlying scheme is otherwise a normal URI.
                string urlForValidation = discoveryUrl.StartsWith(
                    s_reverseConnectPrefix,
                    StringComparison.Ordinal)
                    ? discoveryUrl[s_reverseConnectPrefix.Length..]
                    : discoveryUrl;

                if (!Uri.IsWellFormedUriString(urlForValidation, UriKind.Absolute))
                {
                    throw new ArgumentException(
                        discoveryUrl + " is not a valid URL.",
                        nameof(application));
                }
            }

            if (application.ApplicationType is ApplicationType.Server or ApplicationType.DiscoveryServer)
            {
                if (application.DiscoveryUrls.IsEmpty)
                {
                    throw new ArgumentException(
                        "At least one DiscoveryUrl must be provided.",
                        nameof(application));
                }

                if (application.ServerCapabilities.IsEmpty)
                {
                    // Per OPC UA Part 12, ServerCapabilities may be empty
                    // (a Server that does not advertise any specific capability).
                    // Older implementations of this library mandated at least one
                    // entry; per OPC UA conformance tests this is too strict.
                }

                // Servers do not register reverse-connect listening URLs
                // here; those belong to a Client or ClientAndServer entry.
                foreach (string discoveryUrl in application.DiscoveryUrls)
                {
                    if (!string.IsNullOrEmpty(discoveryUrl) &&
                        discoveryUrl.StartsWith(s_reverseConnectPrefix, StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            discoveryUrl +
                            $" uses the '{s_reverseConnectPrefix}' prefix which is only valid for Clients or ClientAndServer applications.",
                            nameof(application));
                    }
                }
            }
            else if (application.ApplicationType == ApplicationType.ClientAndServer)
            {
                // ClientAndServer must always expose at least one
                // non-reverse-connect DiscoveryUrl and one ServerCapability
                // for its Server side.
                if (application.DiscoveryUrls.IsEmpty)
                {
                    throw new ArgumentException(
                        "At least one DiscoveryUrl must be provided.",
                        nameof(application));
                }

                if (application.ServerCapabilities.IsEmpty)
                {
                    throw new ArgumentException(
                        "At least one ServerCapability must be provided.",
                       nameof(application));
                }

                bool hasServerUrl = false;
                bool hasReverseUrl = false;
                foreach (string discoveryUrl in application.DiscoveryUrls)
                {
                    if (string.IsNullOrEmpty(discoveryUrl))
                    {
                        continue;
                    }
                    if (discoveryUrl.StartsWith(s_reverseConnectPrefix, StringComparison.Ordinal))
                    {
                        hasReverseUrl = true;
                    }
                    else
                    {
                        hasServerUrl = true;
                    }
                }

                if (!hasServerUrl)
                {
                    throw new ArgumentException(
                        "A ClientAndServer must register at least one Server (non reverse-connect) DiscoveryUrl.",
                        nameof(application));
                }

                if (hasReverseUrl &&
                    !application.ServerCapabilities.Contains(s_reverseConnectCapability))
                {
                    throw new ArgumentException(
                        $"A ClientAndServer with reverse-connect DiscoveryUrls shall include the '{s_reverseConnectCapability}' ServerCapability.",
                        nameof(application));
                }
            }
            else if (application.ApplicationType == ApplicationType.Client)
            {
                // Per OPC 10000-12 §6.5.5 a Client may register
                // DiscoveryUrls when it supports reverse connect. In that
                // case all DiscoveryUrls shall begin with the rcp+ prefix
                // and ServerCapabilities shall include the RCP identifier.
                if (!application.DiscoveryUrls.IsEmpty)
                {
                    foreach (string discoveryUrl in application.DiscoveryUrls)
                    {
                        if (string.IsNullOrEmpty(discoveryUrl))
                        {
                            continue;
                        }

                        if (!discoveryUrl.StartsWith(s_reverseConnectPrefix, StringComparison.Ordinal))
                        {
                            throw new ArgumentException(
                                "Clients can only register DiscoveryUrls when they support reverse connect; " +
                                $"all URLs must start with the '{s_reverseConnectPrefix}' prefix.",
                                nameof(application));
                        }
                    }

                    if (!application.ServerCapabilities.Contains(s_reverseConnectCapability))
                    {
                        throw new ArgumentException(
                            $"Clients with reverse-connect DiscoveryUrls shall include the '{s_reverseConnectCapability}' ServerCapability.",
                            nameof(application));
                    }
                }
            }

            return default;
        }

        // OPC 10000-12 §6.5.5: reverse-connect DiscoveryUrls are prefixed
        // with "rcp+"; reverse-connect capability identifier is "RCP".
        private const string s_reverseConnectPrefix = "rcp+";
        private const string s_reverseConnectCapability = "RCP";

        public virtual void UnregisterApplication(NodeId applicationId)
        {
            ValidateApplicationNodeId(applicationId);
        }

        public virtual ApplicationRecordDataType? GetApplication(NodeId applicationId)
        {
            ValidateApplicationNodeId(applicationId);
            return null;
        }

        public virtual ApplicationRecordDataType[]? FindApplications(string applicationUri)
        {
            // Per OPC UA Part 12 the applicationUri filter is optional;
            // an empty or null filter returns all registered Applications.
            return null;
        }

        public virtual ServerOnNetwork[]? QueryServers(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            ArrayOf<string> serverCapabilities,
            out DateTimeUtc lastCounterResetTime)
        {
            lastCounterResetTime = DateTimeUtc.MinValue;

            if (serverCapabilities.Contains("NA", StringComparer.OrdinalIgnoreCase) &&
                serverCapabilities.Count > 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument);
            }

            return null;
        }

        public virtual ApplicationDescription[]? QueryApplications(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            uint applicationType,
            string productUri,
            ArrayOf<string> serverCapabilities,
            out DateTimeUtc lastCounterResetTime,
            out uint nextRecordId)
        {
            lastCounterResetTime = DateTimeUtc.MinValue;
            nextRecordId = 0;

            // applicationType filter values per OPC UA Part 12 §6.3.10 / Part 4:
            //   0 = ALL, 1 = SERVER, 2 = CLIENT, 3 = DISCOVERY_SERVER.
            // Anything outside this range is invalid.
            if (applicationType > 3)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument);
            }

            if (serverCapabilities.Contains("NA", StringComparer.OrdinalIgnoreCase) &&
                serverCapabilities.Count > 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument);
            }

            return null;
        }

        public virtual bool SetApplicationCertificate(
            NodeId applicationId,
            string certificateTypeId,
            ByteString certificate)
        {
            ValidateApplicationNodeId(applicationId);
            return false;
        }

        public virtual bool GetApplicationCertificate(
            NodeId applicationId,
            string certificateTypeId,
            out ByteString certificate)
        {
            certificate = default;
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
            out string? trustListId)
        {
            trustListId = null;
            ValidateApplicationNodeId(applicationId);
            return false;
        }

        /// <summary>
        /// Returns true if the target string matches the UA pattern string.
        /// The pattern string may include UA wildcards %_\[]!
        /// </summary>
        /// <param name="target">String to check for a pattern match.</param>
        /// <param name="pattern">Pattern to match with the target string.</param>
        /// <returns>true if the target string matches the pattern, otherwise false.</returns>
        public static bool Match(string? target, string pattern)
        {
            if (target == null || target.Length == 0)
            {
                return false;
            }

            if (string.IsNullOrEmpty(pattern))
            {
                return true;
            }

            List<string> tokens = Parse(pattern);

            int targetIndex = 0;

            for (int ii = 0; ii < tokens.Count; ii++)
            {
                targetIndex = Match(target, targetIndex, tokens, ref ii);

                if (targetIndex < 0)
                {
                    return false;
                }
            }

            return targetIndex >= target.Length;
        }

        /// <summary>
        /// Returns true if the pattern string contains a UA pattern.
        /// The pattern string may include UA wildcards %_\[]!
        /// </summary>
        public static bool IsMatchPattern(string pattern)
        {
            char[] patternChars = ['%', '_', '\\', '[', ']', '!'];
            if (string.IsNullOrEmpty(pattern))
            {
                return false;
            }

            foreach (char patternChar in patternChars)
            {
                if (pattern.Contains(patternChar, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public string ServerCapabilities(ApplicationRecordDataType application)
        {
            if (application.ServerCapabilities.IsEmpty)
            {
                // Per OPC UA Part 12, ServerCapabilities may be empty.
                // Returning an empty string means "no specific capability advertised".
                return string.Empty;
            }
            var uniqueCapabilities = application.ServerCapabilities.ToList();
            var capabilities = new StringBuilder();
            uniqueCapabilities.Sort();
            foreach (string capability in uniqueCapabilities)
            {
                if (string.IsNullOrEmpty(capability))
                {
                    continue;
                }

                if (capabilities.Length > 0)
                {
                    capabilities.Append(',');
                }
                capabilities.Append(capability);
            }
            return capabilities.ToString();
        }

        protected Guid GetNodeIdGuid(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotFound);
            }

            if (NamespaceIndex != nodeId.NamespaceIndex ||
                !nodeId.TryGetValue(out Guid id))
            {
                throw new ServiceResultException(StatusCodes.BadNotFound);
            }

            return id;
        }

        protected string? GetNodeIdString(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                return null;
            }

            if (NamespaceIndex != nodeId.NamespaceIndex)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }

            if (!nodeId.TryGetValue(out string id))
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            return id;
        }

        protected void ValidateApplicationNodeId(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotFound);
            }

            if ((nodeId.IdType != IdType.Guid && nodeId.IdType != IdType.String) ||
                NamespaceIndex != nodeId.NamespaceIndex)
            {
                throw new ServiceResultException(StatusCodes.BadNotFound);
            }
        }

        private static List<string> Parse(string pattern)
        {
            var tokens = new List<string>();

            int ii = 0;
            var buffer = new StringBuilder();

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
                    while (ii < pattern.Length && pattern[ii] != ']')
                    {
                        if (pattern[ii] == '-' && ii > 0 && ii < pattern.Length - 1)
                        {
                            int start = Convert.ToInt32(pattern[ii - 1]) + 1;
                            int end = Convert.ToInt32(pattern[ii + 1]);

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

                    buffer.Append(']');
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

        private static int SkipToNext(
            string target,
            int targetIndex,
            IList<string> tokens,
            ref int tokenIndex)
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
                while (targetIndex < target.Length &&
                    Match(target, targetIndex, tokens, ref nextTokenIndex) < 0)
                {
                    targetIndex++;
                    nextTokenIndex = tokenIndex + 1;
                }

                nextTokenIndex = tokenIndex + 1;

                // skip over duplicate matches.
                while (targetIndex < target.Length &&
                    Match(target, targetIndex, tokens, ref nextTokenIndex) >= 0)
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
                while (targetIndex < target.Length &&
                    Match(target, targetIndex, tokens, ref nextTokenIndex) >= 0)
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

        private static int Match(
            string target,
            int targetIndex,
            IList<string> tokens,
            ref int tokenIndex)
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

            if (token.StartsWith('[') &&
                token.EndsWith(']') &&
                token.Length > 1)
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

                    match |= inverse && target[targetIndex] == token[ii];
                }

                if (inverse && !match)
                {
                    return targetIndex + 1;
                }

                return -1;
            }

            if (target[targetIndex..].StartsWith(token, StringComparison.Ordinal))
            {
                return targetIndex + token.Length;
            }

            return -1;
        }
    }
}
