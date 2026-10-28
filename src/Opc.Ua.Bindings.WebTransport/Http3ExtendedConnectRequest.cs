// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Collections.Generic;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Represents a validated WebTransport Extended CONNECT request over HTTP/3.
    /// </summary>
    internal readonly record struct Http3ExtendedConnectRequest(string Authority, string Path, string? Origin);

    /// <summary>
    /// Validates the HTTP/3 pseudo-header and connection-specific rules for a WebTransport CONNECT request.
    /// </summary>
    internal static class Http3ExtendedConnectRequestParser
    {
        /// <summary>
        /// Validates a decoded field section without normalizing its pseudo-header values.
        /// </summary>
        internal static Http3ExtendedConnectRequestStatus TryParse(
            ReadOnlySpan<QpackHeaderField> fields,
            out Http3ExtendedConnectRequest request)
        {
            string? method = null;
            string? protocol = null;
            string? scheme = null;
            string? authority = null;
            string? path = null;
            string? origin = null;
            bool capsuleProtocolSeen = false;
            HashSet<string> seenPseudoHeaders = new(StringComparer.Ordinal);
            bool regularHeadersStarted = false;

            foreach (QpackHeaderField field in fields)
            {
                if (field.Name.Length > 0 && field.Name[0] == ':')
                {
                    if (regularHeadersStarted)
                    {
                        request = default;
                        return Http3ExtendedConnectRequestStatus.PseudoHeaderAfterRegularHeader;
                    }
                    if (!seenPseudoHeaders.Add(field.Name))
                    {
                        request = default;
                        return Http3ExtendedConnectRequestStatus.DuplicatePseudoHeader;
                    }

                    switch (field.Name)
                    {
                        case ":method":
                            method = field.Value;
                            break;
                        case ":protocol":
                            protocol = field.Value;
                            break;
                        case ":scheme":
                            scheme = field.Value;
                            break;
                        case ":authority":
                            authority = field.Value;
                            break;
                        case ":path":
                            path = field.Value;
                            break;
                        default:
                            request = default;
                            return Http3ExtendedConnectRequestStatus.UnknownPseudoHeader;
                    }
                    continue;
                }

                regularHeadersStarted = true;
                if (IsConnectionSpecificHeader(field.Name) ||
                    field.Name == "te" && field.Value != "trailers")
                {
                    request = default;
                    return Http3ExtendedConnectRequestStatus.ConnectionSpecificHeader;
                }
                if (field.Name == "origin")
                {
                    if (origin != null)
                    {
                        request = default;
                        return Http3ExtendedConnectRequestStatus.DuplicateOrigin;
                    }
                    origin = field.Value;
                }
                else if (field.Name == "capsule-protocol")
                {
                    if (capsuleProtocolSeen)
                    {
                        request = default;
                        return Http3ExtendedConnectRequestStatus.DuplicateCapsuleProtocol;
                    }
                    capsuleProtocolSeen = true;
                    if (field.Value != "?1")
                    {
                        request = default;
                        return Http3ExtendedConnectRequestStatus.InvalidCapsuleProtocol;
                    }
                }
            }

            if (method == null || protocol == null || scheme == null || authority == null || path == null)
            {
                request = default;
                return Http3ExtendedConnectRequestStatus.MissingRequiredPseudoHeader;
            }
            if (method != "CONNECT")
            {
                request = default;
                return Http3ExtendedConnectRequestStatus.InvalidMethod;
            }
            if (protocol != "webtransport")
            {
                request = default;
                return Http3ExtendedConnectRequestStatus.InvalidProtocol;
            }
            if (scheme != "https")
            {
                request = default;
                return Http3ExtendedConnectRequestStatus.InvalidScheme;
            }
            if (authority.Length == 0 || path.Length == 0 || path[0] != '/')
            {
                request = default;
                return Http3ExtendedConnectRequestStatus.MissingRequiredPseudoHeader;
            }
            if (!capsuleProtocolSeen)
            {
                request = default;
                return Http3ExtendedConnectRequestStatus.MissingCapsuleProtocol;
            }

            request = new Http3ExtendedConnectRequest(authority, path, origin);
            return Http3ExtendedConnectRequestStatus.Success;
        }

        private static bool IsConnectionSpecificHeader(string name)
        {
            return name is "connection" or "keep-alive" or "proxy-connection" or "transfer-encoding" or "upgrade";
        }
    }

    /// <summary>
    /// Describes the result of validating a WebTransport Extended CONNECT request.
    /// </summary>
    internal enum Http3ExtendedConnectRequestStatus
    {
        /// <summary>
        /// The request contains the required WebTransport pseudo-headers.
        /// </summary>
        Success,

        /// <summary>
        /// The method is not CONNECT.
        /// </summary>
        InvalidMethod,

        /// <summary>
        /// The extended CONNECT protocol token is not webtransport.
        /// </summary>
        InvalidProtocol,

        /// <summary>
        /// The scheme is not HTTPS.
        /// </summary>
        InvalidScheme,

        /// <summary>
        /// A required pseudo-header is missing or malformed.
        /// </summary>
        MissingRequiredPseudoHeader,

        /// <summary>
        /// A pseudo-header occurs after a regular header.
        /// </summary>
        PseudoHeaderAfterRegularHeader,

        /// <summary>
        /// A pseudo-header occurs more than once.
        /// </summary>
        DuplicatePseudoHeader,

        /// <summary>
        /// A pseudo-header is not defined for this request.
        /// </summary>
        UnknownPseudoHeader,

        /// <summary>
        /// The request contains a connection-specific HTTP/1.x header.
        /// </summary>
        ConnectionSpecificHeader,

        /// <summary>
        /// The request contains more than one Origin header.
        /// </summary>
        DuplicateOrigin,

        /// <summary>
        /// The request does not signal Capsule Protocol support.
        /// </summary>
        MissingCapsuleProtocol,

        /// <summary>
        /// The request contains more than one Capsule Protocol indicator.
        /// </summary>
        DuplicateCapsuleProtocol,

        /// <summary>
        /// The Capsule Protocol indicator is not exactly <c>?1</c>.
        /// </summary>
        InvalidCapsuleProtocol
    }
}
