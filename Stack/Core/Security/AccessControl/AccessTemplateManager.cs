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
        /// Sets the permissions to match the template on the specified directory.
        /// </summary>
        public void SetPermissions(string template, Uri url, bool exactMatch)
        {
            if (url == null)
            {
                throw new ArgumentException("Target URI is not valid.", "target");
            }

            string filePath = Utils.GetAbsoluteFilePath(m_directory.FullName + "\\" + template + m_FileExtension, false, false, false);

            // nothing more to do if no file.
            if (filePath == null)
            {
                return;
            }

            string urlMask = null;

            if (!exactMatch)
            {
                urlMask = url.Scheme;
                urlMask += "://+:";
                urlMask += url.Port;
                urlMask += url.PathAndQuery;

                if (!urlMask.EndsWith("/"))
                {
                    urlMask += "/";
                }
            }
            else
            {
                urlMask = url.ToString();
            }

            FileInfo templateFile = new FileInfo(filePath);
            List<HttpAccessRule> httpRules = new List<HttpAccessRule>();

            HttpAccessRule.SetAccessRules(urlMask, httpRules, true);
        }

        /// <summary>
        /// Deletes an access template file.
        /// </summary>
        public static void DeleteTemplate(string directory, string templateName)
        {
            string filePath = directory + " \\" + templateName + m_FileExtension;
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
