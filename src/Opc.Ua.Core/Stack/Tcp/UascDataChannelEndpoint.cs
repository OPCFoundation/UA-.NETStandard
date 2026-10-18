/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.DataChannels;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Selects flow dispatch from configured endpoint evidence, never from payload bytes.
    /// </summary>
    internal static class UascDataChannelEndpoint
    {
        /// <summary>
        /// Gets the explicitly selected UASC profile, if any.
        /// </summary>
        internal static DataChannelCarrierKind? GetKind(string? profile)
        {
            return profile switch
            {
                DataChannelProfiles.DedicatedUasc => DataChannelCarrierKind.DedicatedUasc,
                DataChannelProfiles.SharedUasc => DataChannelCarrierKind.SharedUasc,
                _ => null
            };
        }

        /// <summary>
        /// Gets the profile selected by an exact version-one endpoint resource.
        /// </summary>
        internal static DataChannelCarrierKind? GetResourceKind(Uri endpoint)
        {
            return GetResourceKind(endpoint.AbsolutePath);
        }

        /// <summary>
        /// Gets the profile selected by an HTTP endpoint resource.
        /// </summary>
        internal static DataChannelCarrierKind? GetResourceKind(string resource)
        {
            return resource switch
            {
                DataChannelProfiles.DedicatedResource => DataChannelCarrierKind.DedicatedUasc,
                DataChannelProfiles.SharedResource => DataChannelCarrierKind.SharedUasc,
                _ => null
            };
        }

        /// <summary>
        /// Identifies reserved resources so unsupported versions cannot fall back to Services.
        /// </summary>
        internal static bool IsFlowResource(Uri endpoint)
        {
            return IsFlowResource(endpoint.AbsolutePath);
        }

        /// <summary>
        /// Identifies the reserved flow resource namespace before HTTP application dispatch.
        /// </summary>
        internal static bool IsFlowResource(string resource)
        {
            return resource.Equals("/flow", StringComparison.OrdinalIgnoreCase) ||
                resource.StartsWith("/flow/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Validates the endpoint/profile/security combination before any connection is opened.
        /// </summary>
        internal static DataChannelCarrierKind? Validate(
            Uri url,
            EndpointDescription? endpoint,
            IDataChannelContextFactory? factory,
            bool allowIntegrity)
        {
            DataChannelCarrierKind? kind = GetKind(endpoint?.TransportProfileUri);
            if (!kind.HasValue)
            {
                if (IsFlowResource(url) ||
                    endpoint?.TransportProfileUri is DataChannelProfiles.Quic or DataChannelProfiles.WebTransport)
                {
                    throw Unsupported();
                }
                return null;
            }

            if (factory == null ||
                url.Scheme is not (Utils.UriSchemeOpcTcp or Utils.UriSchemeOpcWss) ||
                GetResourceKind(url) != kind ||
                url.Query.Length != 0 || url.Fragment.Length != 0 || url.UserInfo.Length != 0 ||
                !Uri.TryCreate(endpoint!.EndpointUrl, UriKind.Absolute, out Uri? described) ||
                described.Scheme != url.Scheme ||
                GetResourceKind(described) != kind ||
                described.Query.Length != 0 || described.Fragment.Length != 0 || described.UserInfo.Length != 0)
            {
                throw Unsupported();
            }

            ValidateProtection(endpoint!.SecurityMode, endpoint.SecurityPolicyUri, allowIntegrity);
            return kind;
        }

        /// <summary>
        /// Refuses unsecured or implicitly weakened UASC protection.
        /// </summary>
        internal static void ValidateProtection(
            MessageSecurityMode mode,
            string? policy,
            bool allowIntegrity)
        {
            if (mode != MessageSecurityMode.SignAndEncrypt &&
                !(mode == MessageSecurityMode.Sign && allowIntegrity))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityModeRejected,
                    "Flow endpoints require confidential protection unless integrity is explicitly permitted.");
            }
            if (string.IsNullOrEmpty(policy) || policy == SecurityPolicies.None)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Flow endpoints require an authenticated UASC security policy.");
            }
        }

        /// <summary>
        /// Validates every advertised UASC flow configuration before accepting connections.
        /// </summary>
        internal static void ValidateListener(TransportListenerSettings settings)
        {
            foreach (EndpointDescription endpoint in settings.Descriptions ?? [])
            {
                if (GetKind(endpoint.TransportProfileUri).HasValue)
                {
                    if (!Uri.TryCreate(endpoint.EndpointUrl, UriKind.Absolute, out Uri? url))
                    {
                        throw Unsupported();
                    }
                    Validate(url, endpoint, settings.DataChannels, settings.AllowDataChannelIntegrity);
                }
            }
        }

        /// <summary>
        /// Binds application identity to validated URI subject-alternative names.
        /// </summary>
        internal static string GetApplicationUri(Certificate certificate, string? expectedApplicationUri)
        {
            IReadOnlyList<string> uris = X509Utils.GetApplicationUrisFromCertificate(certificate);
            if (!string.IsNullOrEmpty(expectedApplicationUri))
            {
                foreach (string uri in uris)
                {
                    if (uri.Equals(expectedApplicationUri, StringComparison.Ordinal))
                    {
                        return uri;
                    }
                }
            }
            else if (uris.Count == 1)
            {
                return uris[0];
            }

            throw new ServiceResultException(
                StatusCodes.BadCertificateUriInvalid,
                "The authenticated certificate does not identify the configured flow application.");
        }

        /// <summary>
        /// Creates the explicit unsupported-profile error used without protocol fallback.
        /// </summary>
        internal static ServiceResultException Unsupported()
        {
            return new ServiceResultException(
                StatusCodes.BadNotSupported,
                "The selected UASC flow profile, endpoint resource or context provider is unavailable.");
        }
    }
}
