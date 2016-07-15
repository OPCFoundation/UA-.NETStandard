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
using Opc.Ua.Configuration;

namespace Opc.Ua
{
    /// <summary>
    /// Manages a set of user access permission templates.
    /// </summary>
    public class AccessTemplateManager
    {
        /// <summary>
        /// Initializes the manager to use the specified directory.
        /// </summary>
        public AccessTemplateManager(string directory)
        {
            if (directory == null)
            {
                throw new ArgumentNullException("directory");
            }

            directory = Utils.GetAbsoluteDirectoryPath(directory, false, false, false);

            if (directory == null)
            {
                throw new ArgumentException("Specified user template directory does not exist.", "directory");
            }

            m_directory = new DirectoryInfo(directory);
        }

        /// <summary>
        /// Enumerates the available templates.
        /// </summary>
        public string[] EnumerateTemplates()
        {
            List<string> templates = new List<string>();

            foreach (FileInfo file in m_directory.GetFiles("*" + m_FileExtension))
            {
                templates.Add(file.Name.Substring(0, file.Name.Length - file.Extension.Length));
            }

            return templates.ToArray();
        }

        /// <summary>
        /// Deletes an access template file.
        /// </summary>
        public static void DeleteTemplate(string directory, string templateName)
        {
            string filePath = directory + Path.DirectorySeparatorChar + templateName + m_FileExtension;
            AccessTemplateManager.DeleteFile(filePath);
        }

        /// <summary>
        /// Deletes an access template file.
        /// </summary>
        internal static void DeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
                // ignore errors.
            }
        }

        private const string m_FileExtension = ".access";
        private DirectoryInfo m_directory;
    }
}
