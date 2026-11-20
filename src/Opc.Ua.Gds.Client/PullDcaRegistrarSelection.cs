// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Selects one operator-chosen pull registrar from configured device candidates.
    /// Selection does not discover endpoints, establish certificate trust, or connect to a registrar.
    /// </summary>
    public static class PullDcaRegistrarSelection
    {
        /// <summary>
        /// Requires the selected endpoint to be advertised by exactly one configured registrar with the expected
        /// application URI. Registrar and certificate-directory identifiers are supplied by the host after it
        /// resolves the remote model; they are not inferred from discovery metadata.
        /// </summary>
        public static PullDcaRegistrar SelectConfigured(
            ArrayOf<ApplicationDescription> candidates,
            string expectedApplicationUri,
            Uri selectedEndpointUrl,
            NodeId registrarId,
            NodeId certificateDirectoryId)
        {
            if (registrarId.IsNull || certificateDirectoryId.IsNull)
            {
                throw new ArgumentException("Resolved registrar and certificate-directory identifiers are required.");
            }
            RequireConfiguredEndpoint(candidates, expectedApplicationUri, selectedEndpointUrl);
            return new PullDcaRegistrar(selectedEndpointUrl, registrarId, certificateDirectoryId);
        }

        /// <summary>
        /// Checks the operator-selected endpoint before opening a connection, without requiring remote NodeIds.
        /// Exactly one configured discovery entry must advertise this endpoint for the expected application URI.
        /// This check does not establish certificate trust or authenticate the remote application.
        /// </summary>
        public static void RequireConfiguredEndpoint(
            ArrayOf<ApplicationDescription> candidates,
            string expectedApplicationUri,
            Uri selectedEndpointUrl)
        {
            if (expectedApplicationUri == null)
            {
                throw new ArgumentNullException(nameof(expectedApplicationUri));
            }
            if (selectedEndpointUrl == null)
            {
                throw new ArgumentNullException(nameof(selectedEndpointUrl));
            }
            if (!Uri.TryCreate(expectedApplicationUri, UriKind.Absolute, out _) ||
                !selectedEndpointUrl.IsAbsoluteUri)
            {
                throw new ArgumentException("An absolute registrar application URI and endpoint are required.");
            }

            int matches = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                ApplicationDescription candidate = candidates[i] ??
                    throw new ServiceResultException(StatusCodes.BadConfigurationError);
                if (!string.Equals(candidate.ApplicationUri, expectedApplicationUri, StringComparison.Ordinal))
                {
                    continue;
                }
                if (candidate.ApplicationType is not (ApplicationType.Server or ApplicationType.ClientAndServer))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "A configured pull registrar must expose a server.");
                }

                for (int j = 0; j < candidate.DiscoveryUrls.Count; j++)
                {
                    string endpoint = candidate.DiscoveryUrls[j];
                    if (Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? configuredEndpoint) &&
                        configuredEndpoint.Equals(selectedEndpointUrl))
                    {
                        matches++;
                    }
                }
            }

            if (matches != 1)
            {
                throw new ServiceResultException(
                    matches == 0 ? StatusCodes.BadNotFound : StatusCodes.BadInvalidArgument,
                    matches == 0
                        ? "The selected endpoint is not a configured candidate for this registrar."
                        : "The selected registrar endpoint is ambiguous in the configured candidates.");
            }
        }
    }
}
