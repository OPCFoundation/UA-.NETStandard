/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Security
{
    /// <summary>
    /// Implemented by types that have knownledge of an application configuration.
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

    #region SecurityConfigurationManagerFactory Class
    /// <summary>
    /// A class used to create instances of ISecurityConfigurationManager.
    /// </summary>
    public static class SecurityConfigurationManagerFactory
    {
        /// <summary>
        /// Returns an instance of the type identified by the assembly qualified name.
        /// </summary>
        /// <param name="typeName">Name of the type.</param>
        /// <returns>The new instance.</returns>
        public static ISecurityConfigurationManager CreateInstance(string typeName)
        {
            if (String.IsNullOrEmpty(typeName))
            {
                return new SecurityConfigurationManager();
            }

            Type type = Type.GetType(typeName);

            if (type == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "Cannot load type: {0}",
                    typeName);
            }

            ISecurityConfigurationManager configuration = Activator.CreateInstance(type) as ISecurityConfigurationManager;

            if (configuration == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "Type does not support the ISecurityConfigurationManager interface: {0}",
                    typeName);
            }

            return configuration;
        }
    }
    #endregion
}
