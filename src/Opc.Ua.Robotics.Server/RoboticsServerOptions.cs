// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Configures the stock Robotics server hosting integration.
    /// </summary>
    public sealed class RoboticsServerOptions
    {
        /// <summary>
        /// Default application-owned namespace for Robotics instances.
        /// </summary>
        public const string DefaultInstanceNamespaceUri =
            "urn:opcua-netstandard:robotics:instances";

        /// <summary>
        /// Gets or sets the application-owned namespace URI used for
        /// dynamically created Robotics instances.
        /// </summary>
        /// <remarks>
        /// This namespace is application-specific and is not an OPC Foundation
        /// companion-specification or configured model-provider namespace.
        /// </remarks>
        public string InstanceNamespaceUri { get; set; } = DefaultInstanceNamespaceUri;

        /// <summary>
        /// Validates the configured namespace URI.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <see cref="InstanceNamespaceUri"/> is empty or is not an absolute URI.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// <see cref="InstanceNamespaceUri"/> is a standard model namespace.
        /// </exception>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(InstanceNamespaceUri))
            {
                throw new ArgumentException(
                    "RoboticsServerOptions.InstanceNamespaceUri must not be empty.",
                    nameof(InstanceNamespaceUri));
            }

            if (!Uri.TryCreate(InstanceNamespaceUri, UriKind.Absolute, out Uri? uri) ||
                string.IsNullOrEmpty(uri.Scheme))
            {
                throw new ArgumentException(
                    "RoboticsServerOptions.InstanceNamespaceUri must be an absolute URI or URN.",
                    nameof(InstanceNamespaceUri));
            }

            if (InstanceNamespaceUri == global::Opc.Ua.Namespaces.OpcUa ||
                InstanceNamespaceUri == global::Opc.Ua.Di.Server.DiNodeManager.DiNamespaceUri ||
                InstanceNamespaceUri == global::Opc.Ua.IA.Namespaces.IA ||
                InstanceNamespaceUri == global::Opc.Ua.Robotics.Namespaces.Robotics)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "RoboticsServerOptions.InstanceNamespaceUri '{0}' is a model namespace. " +
                    "Configure a distinct application-owned namespace for Robotics instances.",
                    InstanceNamespaceUri);
            }
        }
    }
}
