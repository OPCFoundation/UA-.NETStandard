// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Security
{
    /// <summary>
    /// Implemented by types that have knowledge of an application configuration.
    /// </summary>
    public interface ISecurityConfigurationManager
    {
        /// <summary>
        /// Exports the security configuration for an application identified by a file or url.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <returns>The security configuration.</returns>
        SecuredApplication ReadConfiguration(string filePath);

        /// <summary>
        /// Updates the security configuration for an application identified by a file or url.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <param name="configuration">The configuration.</param>
        void WriteConfiguration(string filePath, SecuredApplication configuration);
    }

    /// <summary>
    /// A class used to create instances of ISecurityConfigurationManager.
    /// </summary>
    public static class SecurityConfigurationManagerFactory
    {
        /// <summary>
        /// Returns an instance of the type identified by the assembly qualified name.
        /// </summary>
        /// <param name="typeName">Name of the type.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <returns>The new instance.</returns>
        /// <exception cref="ServiceResultException"></exception>
        [RequiresUnreferencedCode(
            "Uses Type.GetType to load types by name.")]
        [RequiresDynamicCode(
            "Uses Type.GetType to load types by name.")]
        public static ISecurityConfigurationManager CreateInstance(string typeName, ITelemetryContext telemetry)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return new SecurityConfigurationManager(telemetry);
            }

            Type type =
                Type.GetType(typeName)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "Cannot load type: {0}",
                    typeName);

            if (Activator.CreateInstance(type, telemetry) is not ISecurityConfigurationManager configuration)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "Type does not support the ISecurityConfigurationManager interface: {0}",
                    typeName);
            }

            return configuration;
        }
    }
}
