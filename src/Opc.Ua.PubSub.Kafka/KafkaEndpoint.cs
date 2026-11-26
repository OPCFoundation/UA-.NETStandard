// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.Kafka
{
    /// <summary>
    /// Parsed Kafka endpoint produced by
    /// <see cref="KafkaEndpointParser"/>. Carries the normalised
    /// comma-separated bootstrap server list plus a flag selecting
    /// plaintext vs TLS so transport call sites do not re-parse the URL.
    /// </summary>
    /// <remarks>
    /// Implements the addressing surface of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>. Kafka clients
    /// connect to a list of bootstrap brokers rather than a single host,
    /// so the endpoint carries the full <c>host:port</c> list; the
    /// <c>kafka</c> / <c>kafkas</c> scheme is the only scheme-derived
    /// signal for TLS.
    /// </remarks>
    /// <param name="BootstrapServers">
    /// Normalised comma-separated <c>host:port</c> bootstrap server list.
    /// </param>
    /// <param name="UseTls">
    /// <see langword="true"/> when the URL scheme was <c>kafkas</c>.
    /// </param>
    public readonly record struct KafkaEndpoint(string BootstrapServers, bool UseTls);
}
