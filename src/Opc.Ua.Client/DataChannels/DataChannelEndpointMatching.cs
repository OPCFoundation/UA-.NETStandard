/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using Opc.Ua.Bindings;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Client.DataChannels
{
    /// <summary>
    /// Prevents CreateSession endpoint matching from changing an explicitly selected data carrier.
    /// </summary>
    internal static class DataChannelEndpointMatching
    {
        /// <summary>
        /// Preserves ordinary endpoint matching, but allows Sessions only on the exact shared UASC resource/profile.
        /// </summary>
        internal static bool CanMatch(
            EndpointDescription expected,
            EndpointDescription actual,
            Uri expectedUrl,
            Uri actualUrl)
        {
            bool expectedFlow = IsFlow(expected.TransportProfileUri) || IsUascFlowResource(expectedUrl);
            bool actualFlow = IsFlow(actual.TransportProfileUri) || IsUascFlowResource(actualUrl);
            if (!expectedFlow && !actualFlow)
            {
                return true;
            }
            return expected.TransportProfileUri is DataChannelProfiles.SharedUasc or DataChannelProfiles.PubSubUasc &&
                actual.TransportProfileUri == expected.TransportProfileUri &&
                UascDataChannelEndpoint.GetResourceProfile(expectedUrl.AbsolutePath) == expected.TransportProfileUri &&
                UascDataChannelEndpoint.GetResourceProfile(actualUrl.AbsolutePath) == actual.TransportProfileUri &&
                expectedUrl.Query.Length == 0 && actualUrl.Query.Length == 0;
        }

        /// <summary>
        /// Recognizes only the explicit version-one data profiles.
        /// </summary>
        private static bool IsFlow(string? profile)
        {
            return profile is DataChannelProfiles.DedicatedUasc or DataChannelProfiles.SharedUasc or
                DataChannelProfiles.PubSubUasc or DataChannelProfiles.Quic or DataChannelProfiles.WebTransport;
        }

        /// <summary>
        /// Does not reinterpret an ordinary HTTPS resource as UASC.
        /// </summary>
        private static bool IsUascFlowResource(Uri uri)
        {
            return uri.Scheme is Utils.UriSchemeOpcTcp or Utils.UriSchemeOpcWss &&
                UascDataChannelEndpoint.IsFlowResource(uri);
        }
    }
}
