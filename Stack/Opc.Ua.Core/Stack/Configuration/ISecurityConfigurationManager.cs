/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System;

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
