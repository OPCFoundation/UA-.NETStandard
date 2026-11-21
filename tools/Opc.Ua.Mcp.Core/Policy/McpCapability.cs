// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Host-granted operations independent of MCP catalogue selection or remote server permissions.
    /// </summary>
    public enum McpCapability
    {
        /// <summary>
        /// Mutating GDS application registrations.
        /// </summary>
        DirectoryAdministration,

        /// <summary>
        /// Issuing, revoking, staging or applying certificates and trust lists.
        /// </summary>
        CertificateAdministration,

        /// <summary>
        /// Issuing, collecting, rotating or revoking protected credentials and tokens.
        /// </summary>
        CredentialIssuance,

        /// <summary>
        /// Registering or removing device onboarding tickets.
        /// </summary>
        Onboarding,

        /// <summary>
        /// Modifying role mappings, permissions, users or passwords.
        /// </summary>
        IdentityAdministration,

        /// <summary>
        /// Starting and operating local PubSub transports.
        /// </summary>
        PubSubRuntime,

        /// <summary>
        /// Modifying remote PubSub configuration and component state.
        /// </summary>
        PubSubConfiguration,

        /// <summary>
        /// Starting PubSub adapters and declarative bridges.
        /// </summary>
        PubSubBridge,

        /// <summary>
        /// Managing PubSub security groups and protected key material.
        /// </summary>
        PubSubSecurity,

        /// <summary>
        /// Opening WoT protocol runtimes and reading or observing their data.
        /// </summary>
        WotRuntime,

        /// <summary>
        /// Writing through an explicitly authorized WoT property form.
        /// </summary>
        WotWrite,

        /// <summary>
        /// Invoking an explicitly authorized WoT action form.
        /// </summary>
        WotInvoke
    }
}
