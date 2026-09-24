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

        /// <summary>
        /// OPC 10000-12 §6.5.5: reverse-connect DiscoveryUrls are prefixed
        /// with "rcp+"; reverse-connect capability identifier is "RCP".
        /// </summary>
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
            // OPC 10000-12 §6.5.4: at most the one application with this
            // ApplicationUri; the node manager rejects an empty ApplicationUri.
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
            ValidateQueryServersArguments(applicationName, applicationUri, productUri, serverCapabilities);
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
            ValidateQueryApplicationsArguments(
                applicationName,
                applicationUri,
                applicationType,
                productUri,
                serverCapabilities);
            return null;
        }

        /// <summary>
        /// Validates the QueryServers arguments and parses the Like filters.
        /// </summary>
        /// <returns>The parsed ApplicationName, ApplicationUri and ProductUri
        /// filters; <c>null</c> for an empty filter.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidArgument"/> for an invalid argument.
        /// </exception>
        protected static (LikePattern? ApplicationName, LikePattern? ApplicationUri, LikePattern? ProductUri)
            ValidateQueryServersArguments(
                string? applicationName,
                string? applicationUri,
                string? productUri,
                ArrayOf<string> serverCapabilities)
        {
            // NA cannot be used in combination with any other capability
            // (OPC 10000-12 Annex D).
            if (serverCapabilities.Contains("NA", StringComparer.OrdinalIgnoreCase) &&
                serverCapabilities.Count > 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument);
            }

            return (
                ParseMatchPattern(applicationName),
                ParseMatchPattern(applicationUri),
                ParseMatchPattern(productUri));
        }

        /// <summary>
        /// Validates the QueryApplications arguments and parses the Like filters.
        /// </summary>
        /// <returns>The parsed ApplicationName, ApplicationUri and ProductUri
        /// filters; <c>null</c> for an empty filter.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidArgument"/> for an invalid argument.
        /// </exception>
        protected static (LikePattern? ApplicationName, LikePattern? ApplicationUri, LikePattern? ProductUri)
            ValidateQueryApplicationsArguments(
                string? applicationName,
                string? applicationUri,
                uint applicationType,
                string? productUri,
                ArrayOf<string> serverCapabilities)
        {
            // applicationType is a mask (OPC 10000-12 §6.5.10): 0 = all,
            // 0x1 = Servers, 0x2 = Clients. Other bits are invalid.
            if (applicationType > 3)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument);
            }

            return ValidateQueryServersArguments(
                applicationName,
                applicationUri,
                productUri,
                serverCapabilities);
        }

        /// <summary>
        /// Parses an optional QueryApplications/QueryServers filter. The
        /// ApplicationName, ApplicationUri and ProductUri filters use the Like
        /// syntax of OPC 10000-4 §7.7.3 and are not used if empty
        /// (OPC 10000-12 §6.5.10, §6.5.11).
        /// </summary>
        /// <returns>The parsed pattern, or <c>null</c> for an empty filter.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidArgument"/> if the filter is not a
        /// valid search string.
        /// </exception>
        protected static LikePattern? ParseMatchPattern(string? pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return null;
            }

            if (!LikePattern.TryParse(pattern, out LikePattern? likePattern))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    $"'{pattern}' is not a valid search pattern.");
            }

            return likePattern;
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
        /// The pattern uses the Like syntax of OPC 10000-4 §7.7.3
        /// (<c>%</c>, <c>_</c>, <c>\</c>, <c>[]</c> and <c>[^]</c>).
        /// </summary>
        /// <param name="target">String to check for a pattern match.</param>
        /// <param name="pattern">Pattern to match with the target string.</param>
        /// <returns>true if the target string matches the pattern, otherwise false.
        /// An empty pattern matches every non-empty target; an invalid pattern
        /// matches nothing.</returns>
        public static bool Match(string? target, string pattern)
        {
            if (string.IsNullOrEmpty(target))
            {
                return false;
            }

            if (string.IsNullOrEmpty(pattern))
            {
                return true;
            }

            return LikePattern.IsMatch(target, pattern);
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
    }
}
