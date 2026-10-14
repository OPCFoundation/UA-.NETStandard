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

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Shares exact parameter descriptions with the reflection-free replacement schemas.
    /// </summary>
    internal static class GdsToolDescriptions
    {
        /// <summary>
        /// Describes selection of an existing authorized session.
        /// </summary>
        internal const string SessionName =
            "Existing authorized SignAndEncrypt session name; omit only when exactly one session is connected. " +
            "This operation never creates a connection.";

        /// <summary>
        /// Describes the browse root used for typed discovery.
        /// </summary>
        internal const string RootNodeId =
            "Browse root NodeId in the selected session; default 'i=85' is the Objects folder.";

        /// <summary>
        /// Describes local paging without implying a stable server cursor.
        /// </summary>
        internal const string Offset =
            "Zero-based skip count in the current live result (0..1000000); not a snapshot or server record cursor.";

        /// <summary>
        /// Describes the bounded page size.
        /// </summary>
        internal const string MaxResults = "Maximum entries returned per page (1..500; default 100).";

        /// <summary>
        /// Describes an application directory instance.
        /// </summary>
        internal const string DirectoryNodeId =
            "NodeId of a discovered DirectoryType instance or subtype in the selected session, " +
            "e.g. 'ns=2;s=Directory'; not the type's NodeId.";

        /// <summary>
        /// Describes a pull certificate directory instance.
        /// </summary>
        internal const string CertificateDirectoryNodeId =
            "NodeId of a discovered CertificateDirectoryType instance or subtype in the selected session; " +
            "use the actual instance id, not its type id.";

        /// <summary>
        /// Describes a server-assigned application registration identity.
        /// </summary>
        internal const string ApplicationId =
            "Server-assigned application registration NodeId returned by gds_register_application, " +
            "e.g. 'ns=2;s=PlantClient'; not the application URI.";

        /// <summary>
        /// Describes explicit or protocol-default certificate group selection.
        /// </summary>
        internal const string CertificateGroupId =
            "Certificate group NodeId advertised by the target; omit or pass an empty string for its protocol default.";

        /// <summary>
        /// Describes the certificate type selected within an advertised group.
        /// </summary>
        internal const string CertificateTypeId =
            "Certificate type NodeId from certificate_inspect_group's CertificateTypes; not a group or certificate id.";

        /// <summary>
        /// Describes the push-management root.
        /// </summary>
        internal const string ConfigurationNodeId =
            "NodeId of the actual ServerConfigurationType instance or subtype, e.g. 'i=12637'; " +
            "not a certificate group.";

        /// <summary>
        /// Describes the remote trust-list file identity.
        /// </summary>
        internal const string TrustListNodeId =
            "NodeId of the actual TrustListType instance returned by group inspection or certificate_get_trust_list.";

        /// <summary>
        /// Describes a confined public certificate input file.
        /// </summary>
        internal const string PublicCertificatePath =
            "Existing public X.509 DER certificate file beneath TransferRoot, e.g. 'server.der'; " +
            "private-key files are forbidden.";

        /// <summary>
        /// Describes a service issuing credentials for non-UA resources.
        /// </summary>
        internal const string KeyCredentialServiceNodeId =
            "NodeId of a discovered KeyCredentialServiceType instance or subtype in the selected session.";

        /// <summary>
        /// Describes a modern authorization service instance.
        /// </summary>
        internal const string AuthorizationServiceNodeId =
            "NodeId of a discovered AuthorizationServiceType instance or subtype in the selected session.";

        /// <summary>
        /// Describes the registrar administration object rather than its parent.
        /// </summary>
        internal const string RegistrarNodeId =
            "NodeId of the DeviceRegistrarAdminType administration instance or subtype; " +
            "not the enclosing device registrar or the type id.";

        /// <summary>
        /// Describes the localized names promoted to a root tool argument.
        /// </summary>
        internal const string ApplicationNames =
            "1..100 localized application display names with explicit text and optional locale, " +
            "e.g. [{\"text\":\"Plant client\",\"locale\":\"en-US\"}].";

        /// <summary>
        /// Describes capability identifiers in registration and query records.
        /// </summary>
        internal const string ServerCapabilities =
            "Up to 100 server capability identifiers, e.g. [\"DA\"]; queries require all listed capabilities.";

        /// <summary>
        /// Describes stored discovery URLs without implying network access.
        /// </summary>
        internal const string DiscoveryUrls =
            "Up to 100 discovery endpoint URLs, e.g. [\"opc.tcp://plant:4840\"]; " +
            "registration stores these values without connecting.";

        /// <summary>
        /// Describes alternative names for a remotely issued certificate.
        /// </summary>
        internal const string DomainNames =
            "Up to 100 DNS names or IP addresses requested as certificate subject alternative names, " +
            "e.g. [\"plant.example\"].";

        /// <summary>
        /// Describes DNS alternative names for a self-signed certificate.
        /// </summary>
        internal const string DnsNames =
            "Up to 100 DNS names for the self-signed certificate's subject alternative names, " +
            "e.g. [\"plant.example\"].";

        /// <summary>
        /// Describes IP alternative names for a self-signed certificate.
        /// </summary>
        internal const string IpAddresses =
            "Up to 100 IP address strings for certificate subject alternative names, e.g. [\"192.0.2.10\"].";

        /// <summary>
        /// Describes the bounded public issuer chain used for staging.
        /// </summary>
        internal const string IssuerCertificatePaths =
            "Up to 32 public issuer DER certificate paths beneath TransferRoot, " +
            "e.g. [\"issuer.der\"]; never private-key files.";

        /// <summary>
        /// Describes role identities sent to a KeyCredential service.
        /// </summary>
        internal const string KeyCredentialRoles =
            "Up to 100 requested role NodeId strings, e.g. [\"ns=2;s=Operator\"]; " +
            "not role names or granted-role claims. The service decides which roles to grant.";

        /// <summary>
        /// Describes role names sent to an AuthorizationService.
        /// </summary>
        internal const string AuthorizationRoles =
            "Up to 100 requested role names advertised in SupportedRoles, e.g. [\"Operator\"]; " +
            "not NodeIds or granted-role claims.";

        /// <summary>
        /// Describes protected ticket references and their registrar binding.
        /// </summary>
        internal const string TicketReferences =
            "1..100 authorized alias:... or owner-bound issued:... ticket references, e.g. [\"alias:ticket-a\"]. " +
            "References must match this registrar's resource; never supply encoded ticket bytes or file paths.";
    }
}
