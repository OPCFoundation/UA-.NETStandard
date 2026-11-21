// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Options for the standalone Robot Intent node manager.
    /// </summary>
    public sealed class RobotIntentServerOptions
    {
        /// <summary>
        /// Gets or sets the application-owned namespace used for instances.
        /// </summary>
        public string InstanceNamespaceUri { get; set; } = "urn:opcfoundation:robot-intent:instances";

        /// <summary>
        /// Gets or sets the specification version reported on Server/RobotIntent.
        /// </summary>
        public string SpecificationVersion { get; set; } = "0.1.0";

        /// <summary>
        /// Validates the option values.
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(InstanceNamespaceUri))
            {
                throw new ArgumentException(
                    "A non-empty Robot Intent instance namespace URI is required.",
                    nameof(InstanceNamespaceUri));
            }
            if (string.IsNullOrWhiteSpace(SpecificationVersion))
            {
                throw new ArgumentException(
                    "A non-empty Robot Intent specification version is required.",
                    nameof(SpecificationVersion));
            }
        }
    }
}
