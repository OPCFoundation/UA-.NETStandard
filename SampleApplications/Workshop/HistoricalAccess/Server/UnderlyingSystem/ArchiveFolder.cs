/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Text;
using System.IO;
using Opc.Ua;

namespace Quickstarts.HistoricalAccessServer
{
    /// <summary>
    /// Stores the metadata for a node representing a folder on a file system.
    /// </summary>
    public class ArchiveFolder
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public ArchiveFolder(string uniquePath, DirectoryInfo folder)
        {
            m_uniquePath = uniquePath;
            m_directoryInfo = folder;
        }

        /// <summary>
        /// Returns the child folders.
        /// </summary>
        public ArchiveFolder[] GetChildFolders()
        {
            List<ArchiveFolder> folders = new List<ArchiveFolder>();

            if (!m_directoryInfo.Exists)
            {
                return folders.ToArray();
            }

            foreach (DirectoryInfo directory in m_directoryInfo.GetDirectories())
            {
                StringBuilder buffer = new StringBuilder(m_uniquePath);
                buffer.Append('/');
                buffer.Append(directory.Name);
                folders.Add(new ArchiveFolder(buffer.ToString(), directory));
            }

            return folders.ToArray();
        }

        /// <summary>
        /// Returns the child folders.
        /// </summary>
        public ArchiveItem[] GetItems()
        {
            List<ArchiveItem> items = new List<ArchiveItem>();

            if (!m_directoryInfo.Exists)
            {
                return items.ToArray();
            }

            foreach (FileInfo file in m_directoryInfo.GetFiles("*.csv"))
            {
                StringBuilder buffer = new StringBuilder(m_uniquePath);
                buffer.Append('/');
                buffer.Append(file.Name);
                items.Add(new ArchiveItem(buffer.ToString(), file));
            }

            return items.ToArray();
        }

        /// <summary>
        /// Returns the parent folder.
        /// </summary>
        public ArchiveFolder GetParentFolder()
        {
            string parentPath = String.Empty;

            if (!m_directoryInfo.Exists)
            {
                return null;
            }

            int index = m_uniquePath.LastIndexOf('/');

            if (index > 0)
            {
                parentPath = m_uniquePath.Substring(0, index);
            }

            return new ArchiveFolder(parentPath, m_directoryInfo.Parent);
        }

        /// <summary>
        /// The unique path to the folder in the archive.
        /// </summary>
        public string UniquePath
        {
            get { return m_uniquePath; }
        }

        /// <summary>
        /// A name for the folder.
        /// </summary>
        public string Name
        {
            get { return m_directoryInfo.Name; }
        }

        /// <summary>
        /// The physical folder in the archive.
        /// </summary>
        public DirectoryInfo DirectoryInfo
        {
            get { return m_directoryInfo; }
        }

        private string m_uniquePath;
        private DirectoryInfo m_directoryInfo;
    }
}
