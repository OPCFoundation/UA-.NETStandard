// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Microsoft.Extensions.Compliance.Classification;

namespace Opc.Ua.Redaction
{
    /// <summary>
    /// The data classification taxonomy of the OPC UA stack.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Log message parameters that hold sensitive data are annotated with one of the
    /// attributes of this taxonomy (for example <see cref="NetworkAddressAttribute"/>).
    /// An application decides per classification what happens to the value by registering
    /// redactors (<c>Microsoft.Extensions.Compliance.Redaction</c>) and enabling redaction on
    /// its logging pipeline (<c>Microsoft.Extensions.Telemetry</c>, <c>EnableRedaction()</c>).
    /// </para>
    /// <para>
    /// Classified values are only written by that redaction-aware pipeline: with
    /// <c>EnableOpcUaRedaction()</c> (or <c>EnableRedaction()</c>) and no redactor registered
    /// they are logged unchanged, while a plain <c>ILoggerFactory</c> without it logs
    /// <c>(null)</c> in their place. <c>AddOpcUa().AddLogging(...)</c> and
    /// <c>DefaultTelemetry.Create(...)</c> enable the pipeline themselves.
    /// </para>
    /// </remarks>
    public static class OpcUaDataClassifications
    {
        /// <summary>
        /// The name of the taxonomy.
        /// </summary>
        public const string TaxonomyName = "OpcUa";

        /// <summary>
        /// A network address: an endpoint, discovery, proxy or reverse connect URL,
        /// a host or domain name, an IP or MAC address, a socket end point or a port.
        /// </summary>
        public static DataClassification NetworkAddress => new(TaxonomyName, nameof(NetworkAddress));

        /// <summary>
        /// The identity of an application instance: a certificate subject, issuer,
        /// thumbprint or serial number, or an application URI.
        /// </summary>
        public static DataClassification ApplicationIdentity => new(TaxonomyName, nameof(ApplicationIdentity));

        /// <summary>
        /// The identity of a user: a user name, an identity token subject or claim,
        /// or an identity mapping criterion.
        /// </summary>
        public static DataClassification UserIdentity => new(TaxonomyName, nameof(UserIdentity));

        /// <summary>
        /// A path in a local file system or certificate store, which can contain
        /// user, host or directory names.
        /// </summary>
        public static DataClassification FilePath => new(TaxonomyName, nameof(FilePath));

        /// <summary>
        /// Free text that can embed any of the other classifications, such as an
        /// exception message or diagnostic text received from a peer.
        /// </summary>
        public static DataClassification SensitiveText => new(TaxonomyName, nameof(SensitiveText));
    }
}
