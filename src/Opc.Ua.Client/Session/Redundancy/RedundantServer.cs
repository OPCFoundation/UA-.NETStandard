// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Client
{
    /// <summary>
    /// Information about a Server in a <c>RedundantServerSet</c>.
    /// </summary>
    /// <remarks>
    /// Corresponds to the OPC UA <c>RedundantServerDataType</c> defined in OPC 10000-5 §12.5 and used by
    /// OPC 10000-4 §6.6.2 server redundancy.
    /// </remarks>
    public sealed class RedundantServer
    {
        /// <summary>
        /// The ServerUri that identifies the Server.
        /// </summary>
        public string ServerUri { get; init; } = string.Empty;

        /// <summary>
        /// <c>ServiceLevel</c> (0–255, higher is better; see OPC 10000-4 §6.6.2.4.2 Table 105).
        /// </summary>
        public byte ServiceLevel { get; init; }

        /// <summary>
        /// Gets a value indicating whether <see cref="ServiceLevel"/> was reported for the Server, either in
        /// the <c>RedundantServerArray</c> of the Server in use or by reading the Server's own
        /// <c>ServiceLevel</c>.
        /// </summary>
        /// <remarks>
        /// A Server known only from <c>ServerUriArray</c> reports <see cref="ServiceLevels.NoData"/> and
        /// <see langword="false"/> until its <c>ServiceLevel</c> is read, see
        /// <see cref="ServerRedundancyOptions.ProbeMemberServiceLevel"/>.
        /// </remarks>
        public bool ServiceLevelKnown { get; init; } = true;

        /// <summary>
        /// Whether the health and state were read directly over an authenticated peer session.
        /// </summary>
        internal bool ServiceLevelProbed { get; init; }

        /// <summary>
        /// The current state of the Server.
        /// </summary>
        public ServerState ServerState { get; init; }

        /// <summary>
        /// The resolved endpoint for this redundant server.
        /// </summary>
        public ConfiguredEndpoint? Endpoint { get; init; }

        /// <summary>
        /// The error that prevented resolving <see cref="Endpoint"/> or reading the Server's
        /// <c>ServiceLevel</c> during the last refresh, or <see langword="null"/> when no attempt failed.
        /// </summary>
        public ServiceResult? ResolutionError { get; init; }
    }
}
