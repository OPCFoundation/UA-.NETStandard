// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Resolves transport profile identities consistently from explicit profiles or endpoint URL schemes.
    /// </summary>
    internal static class TransportProfileIdentity
    {
        /// <summary>
        /// Normalizes an explicit profile or infers one from the URL scheme and requested message encoding.
        /// </summary>
        public static string GetEffective(string? profile, string endpointUrl, bool binaryEncoding = true)
        {
            if (!string.IsNullOrEmpty(profile))
            {
                return Profiles.NormalizeUri(profile);
            }
            if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out Uri? uri))
            {
                return string.Empty;
            }
            return uri.Scheme switch
            {
                Utils.UriSchemeOpcTcp => Profiles.UaTcpTransport,
                Utils.UriSchemeHttps or Utils.UriSchemeOpcHttps =>
                    binaryEncoding ? Profiles.HttpsBinaryTransport : Profiles.HttpsJsonTransport,
                Utils.UriSchemeOpcHttpsWebApi => Profiles.HttpsOpenApiTransport,
                Utils.UriSchemeOpcWss =>
                    binaryEncoding ? Profiles.UaWssTransport : Profiles.UaWssJsonTransport,
                Utils.UriSchemeOpcWssOpenApi => Profiles.WssOpenApiTransport,
                _ => string.Empty
            };
        }

        /// <summary>
        /// Determines whether a profile selects one of the supported HTTPS transports.
        /// </summary>
        public static bool IsHttps(string? profile)
        {
            return profile is Profiles.HttpsBinaryTransport or Profiles.HttpsJsonTransport or Profiles.HttpsOpenApiTransport;
        }
    }
}
