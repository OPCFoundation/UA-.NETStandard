// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Binds the intended redundancy member to the certificate authenticated by UA Secure Conversation.
    /// </summary>
    internal static class ServerPeerIdentity
    {
        /// <summary>
        /// Requires the endpoint metadata and authenticated channel certificate to identify the expected peer.
        /// </summary>
        public static void Validate(ITransportChannel channel, ConfiguredEndpoint endpoint, string expectedUri)
        {
            ValidateEndpoint(endpoint, expectedUri);
            EndpointDescription selected = endpoint.Description;
            EndpointDescription negotiated = channel.EndpointDescription;
            string profile = TransportProfileIdentity.GetEffective(
                selected.TransportProfileUri, selected.EndpointUrl ?? string.Empty);
            string channelProfile = TransportProfileIdentity.GetEffective(
                negotiated.TransportProfileUri, negotiated.EndpointUrl ?? string.Empty);
            if (profile is not (Profiles.UaTcpTransport or Profiles.UaWssTransport) ||
                !string.Equals(profile, channelProfile, StringComparison.Ordinal) ||
                selected.SecurityMode is not (MessageSecurityMode.Sign or MessageSecurityMode.SignAndEncrypt) ||
                selected.SecurityPolicyUri is null or SecurityPolicies.None ||
                selected.SecurityMode != negotiated.SecurityMode ||
                !string.Equals(selected.SecurityPolicyUri, negotiated.SecurityPolicyUri, StringComparison.Ordinal) ||
                channel.ServerChannelCertificate is not { Length: > 0 } peerCertificate)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "A redundancy peer requires a certificate-authenticated UA SecureChannel.");
            }

            using var peer = Certificate.FromRawData(peerCertificate);
            if (!X509Utils.CompareApplicationUriWithCertificate(peer, expectedUri))
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateUriInvalid,
                    "The authenticated peer certificate does not identify the expected redundancy member.");
            }

            if (!Session.IsSameServerCertificate(
                    selected.ServerCertificate, peerCertificate.ToByteString(), channel.MessageContext.Telemetry))
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid,
                    "The endpoint certificate differs from the authenticated redundancy peer certificate.");
            }
        }

        /// <summary>
        /// Checks that discovery and reverse routing metadata agree with the intended peer before using either.
        /// </summary>
        public static void ValidateEndpoint(ConfiguredEndpoint endpoint, string expectedUri)
        {
            string? routingUri = endpoint.ReverseConnect?.ServerUri;
            if (!Uri.TryCreate(expectedUri, UriKind.Absolute, out _) ||
                !string.Equals(endpoint.Description.Server?.ApplicationUri, expectedUri, StringComparison.Ordinal) ||
                (!string.IsNullOrEmpty(routingUri) &&
                    !string.Equals(routingUri, expectedUri, StringComparison.Ordinal)))
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateUriInvalid,
                    "The endpoint does not identify the expected redundancy member.");
            }
        }
    }
}
