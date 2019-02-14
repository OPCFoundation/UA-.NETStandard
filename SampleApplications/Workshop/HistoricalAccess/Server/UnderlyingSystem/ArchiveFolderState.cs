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
    /// Stores the metadata for a node representing a folder on a file system.
    /// </summary>
    public class ArchiveFolderState : Opc.Ua.FolderState
    {
        /// <summary>
        /// Creates a new instance of a folder.
        /// </summary>
        public ArchiveFolderState(ISystemContext context, ArchiveFolder folder, ushort namespaceIndex)
        : 
            base(null)
        {
            m_archiveFolder = folder;

            this.TypeDefinitionId = ObjectTypeIds.FolderType;
            this.SymbolicName = folder.Name;
            this.NodeId = ConstructId(folder.UniquePath, namespaceIndex);
            this.BrowseName = new QualifiedName(folder.Name, namespaceIndex);
            this.DisplayName = new LocalizedText(this.BrowseName.Name);
            this.Description = null;
            this.WriteMask = 0;
            this.UserWriteMask = 0;
            this.EventNotifier = EventNotifiers.None;
        }

        /// <summary>
        /// Constructs a node identifier for a folder object.
        /// </summary>
        public static NodeId ConstructId(string filePath, ushort namespaceIndex)
        {
            ParsedNodeId parsedNodeId = new ParsedNodeId();

            parsedNodeId.RootId = filePath;
            parsedNodeId.NamespaceIndex = namespaceIndex;
            parsedNodeId.RootType = NodeTypes.Folder;

            return parsedNodeId.Construct();
        }

        /// <summary>
        /// The physical folder referenced by the node.
        /// </summary>
        public ArchiveFolder ArchiveFolder
        {
            get { return m_archiveFolder; }
        }

        /// <summary>
        /// Creates a browser that explores the structure of the block.
        /// </summary>
        public override INodeBrowser CreateBrowser(
            ISystemContext context,
            ViewDescription view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection browseDirection,
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly)
        {
            NodeBrowser browser = new ArchiveFolderBrowser(
                context,
                view,
                referenceType,
                includeSubtypes,
                browseDirection,
                browseName,
                additionalReferences,
                internalOnly,
                this);

            PopulateBrowser(context, browser);

            return browser;
        }

        private ArchiveFolder m_archiveFolder;
    }
}
