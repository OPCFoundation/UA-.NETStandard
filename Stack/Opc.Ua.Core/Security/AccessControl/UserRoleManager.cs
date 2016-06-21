/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
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
            string filePath = directory + Path.DirectorySeparatorChar + roleName + m_FileExtension;
            AccessTemplateManager.DeleteFile(filePath);
        }

        private const string m_FileExtension = ".access";
        private DirectoryInfo m_directory;
    }
}
