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
using System.Collections.Generic;
using System.IO;

namespace Opc.Ua
{
    /// <summary>
    /// Manages a set of user roles.
    /// </summary>
    public class UserRoleManager
    {
        /// <summary>
        /// Initializes the manager to use the specified directory.
        /// </summary>
        public UserRoleManager(string directory)
        {
            if (directory == null)
            {
                throw new ArgumentNullException("directory");
            }

            directory = Utils.GetAbsoluteDirectoryPath(directory, false, false, false);

            if (directory == null)
            {
                throw new ArgumentException("Specified user user role directory does not exist.", "directory");
            }

            m_directory = new DirectoryInfo(directory);
        }

        /// <summary>
        /// Enumerates the available roles.
        /// </summary>
        public string[] EnumerateRoles()
        {
            List<string> templates = new List<string>();

            foreach (FileInfo file in m_directory.GetFiles("*" + m_FileExtension))
            {
                templates.Add(file.Name.Substring(0, file.Name.Length - file.Extension.Length));
            }

            return templates.ToArray();
        }

        /// <summary>
        /// Returns true if the current Windows user has access to the the specified template.
        /// </summary>
        public bool HasAccess(string template)
        {
            string filePath = Utils.GetAbsoluteFilePath(m_directory.FullName + template + m_FileExtension, false, false, false);

            // nothing more to do if no file.
            if (filePath == null)
            {
                return false;
            }

            // check if account has access to semaphore file.
            try
            {
                using (Stream ostrm = File.OpenRead(filePath))
                {
                    ostrm.Dispose();
                }

                // access granted.
                return true;
            }
            catch (Exception)
            {
                // no access or no file.
            }

            return false;
        }

        /// <summary>
        /// Deletes a user role file.
        /// </summary>
        public static void DeleteRole(string directory, string roleName)
        {
            string filePath = directory + " \\" + roleName + m_FileExtension;
            AccessTemplateManager.DeleteFile(filePath);
        }

        private const string m_FileExtension = ".access";
        private DirectoryInfo m_directory;
    }
}
