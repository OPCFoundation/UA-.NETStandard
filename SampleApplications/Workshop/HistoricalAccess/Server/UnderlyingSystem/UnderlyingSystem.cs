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
using Opc.Ua.Server;

namespace Quickstarts.HistoricalAccessServer
{
    /// <summary>
    /// Provides access to the system which stores the data.
    /// </summary>
    public class UnderlyingSystem
    {
        /// <summary>
        /// Constructs a new system.
        /// </summary>
        public UnderlyingSystem(HistoricalAccessServerConfiguration configuration, ushort namespaceIndex)
        {
            m_configuration = configuration;
            m_namespaceIndex = namespaceIndex;
        }

        /// <summary>
        /// Returns a folder object for the specified node.
        /// </summary>
        public ArchiveFolderState GetFolderState(ISystemContext context, string rootId)
        {
            StringBuilder path = new StringBuilder();
            path.Append(m_configuration.ArchiveRoot);
            path.Append('/');
            path.Append(rootId);

            ArchiveFolder folder = new ArchiveFolder(rootId, new DirectoryInfo(path.ToString()));
            return new ArchiveFolderState(context, folder, m_namespaceIndex);
        }

        /// <summary>
        /// Returns a item object for the specified node.
        /// </summary>
        public ArchiveItemState GetItemState(ISystemContext context, ParsedNodeId parsedNodeId)
        {
            if (parsedNodeId.RootType != NodeTypes.Item)
            {
                return null;
            }

            StringBuilder path = new StringBuilder();
            path.Append(m_configuration.ArchiveRoot);
            path.Append('/');
            path.Append(parsedNodeId.RootId);

            ArchiveItem item = new ArchiveItem(parsedNodeId.RootId, new FileInfo(path.ToString()));

            return new ArchiveItemState(context, item, m_namespaceIndex);
        }

        private ushort m_namespaceIndex;
        private HistoricalAccessServerConfiguration m_configuration;
    }
}
