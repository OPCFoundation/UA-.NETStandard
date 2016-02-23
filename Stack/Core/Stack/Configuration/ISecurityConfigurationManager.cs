/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

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
