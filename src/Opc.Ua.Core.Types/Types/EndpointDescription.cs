/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Describes how to connect to an endpoint.
    /// </summary>
    public partial class EndpointDescription
    {
        /// <summary>
        /// Creates an endpoint configuration from a url.
        /// </summary>
        public EndpointDescription(string url)
            : this()
        {
            var parsedUrl = new UriBuilder(url);

            if (parsedUrl.Scheme == Uri.UriSchemeHttps &&
                !parsedUrl.Path.EndsWith(
                    DiscoverySuffix,
                    StringComparison.OrdinalIgnoreCase))
            {
                parsedUrl.Path += DiscoverySuffix;
            }

            Server.DiscoveryUrls = Server.DiscoveryUrls.AddItem(parsedUrl.ToString());

            EndpointUrl = url;
            Server.ApplicationUri = url;
            Server.ApplicationName = new LocalizedText(url);
            SecurityMode = MessageSecurityMode.None;
            // The "None" SecurityPolicies URI literal; SecurityPolicies is in Opc.Ua.Core
            // which Opc.Ua does not reference, so we hardcode the value here.
            SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#None";
        }

        /// <summary>
        /// A discovery suffix that may be appended to the discovery url of https endpoints.
        /// </summary>
        public static readonly string DiscoverySuffix = "/discovery";

        /// <summary>
        /// The proxy url to use when connecting to the endpoint.
        /// </summary>
        public Uri? ProxyUrl { get; set; }

        /// <summary>
        /// The discovery endpoints this description was selected from, set by
        /// endpoint selection. Not encoded; a clone keeps the reference, so a
        /// configured endpoint created from a copy still compares the list.
        /// </summary>
        internal ArrayOf<EndpointDescription> SelectionEndpoints { get; set; }
    }
}
