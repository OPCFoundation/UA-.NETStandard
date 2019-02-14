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
using System.Xml;
using System.IO;
using System.Reflection;
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.HistoricalAccessServer
{
    /// <summary>
    /// Browses the references for an archive folder.
    /// </summary>
    public class ArchiveFolderBrowser : NodeBrowser
    {
        #region Constructors
        /// <summary>
        /// Creates a new browser object with a set of filters.
        /// </summary>
        /// <param name="context">The system context to use.</param>
        /// <param name="view">The view which may restrict the set of references/nodes found.</param>
        /// <param name="referenceType">The type of references being followed.</param>
        /// <param name="includeSubtypes">Whether subtypes of the reference type are followed.</param>
        /// <param name="browseDirection">Which way the references are being followed.</param>
        /// <param name="browseName">The browse name of a specific target (used when translating browse paths).</param>
        /// <param name="additionalReferences">Any additional references that should be included.</param>
        /// <param name="internalOnly">If true the browser should not making blocking calls to external systems.</param>
        /// <param name="source">The segment being accessed.</param>
        public ArchiveFolderBrowser(
            ISystemContext context,
            ViewDescription view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection browseDirection,
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly,
            ArchiveFolderState source)
        :
            base(
                context,
                view,
                referenceType,
                includeSubtypes,
                browseDirection,
                browseName,
                additionalReferences,
                internalOnly)
        {
            m_source = source;
            m_stage = Stage.Begin;
        }
        #endregion
        
        #region Overridden Methods
        /// <summary>
        /// Returns the next reference.
        /// </summary>
        /// <returns>The next reference that meets the browse criteria.</returns>
        public override IReference Next()
        {
            UnderlyingSystem system = (UnderlyingSystem)this.SystemContext.SystemHandle;

            lock (DataLock)
            {
                IReference reference = null;

                // enumerate pre-defined references.
                // always call first to ensure any pushed-back references are returned first.
                reference = base.Next();

                if (reference != null)
                {
                    return reference;
                }

                if (m_stage == Stage.Begin)
                {
                    m_folders = m_source.ArchiveFolder.GetChildFolders();
                    m_stage = Stage.Folders;
                    m_position = 0;
                }

                // don't start browsing huge number of references when only internal references are requested.
                if (InternalOnly)
                {
                    return null;
                }
                
                // enumerate folders.
                if (m_stage == Stage.Folders)
                {
                    if (IsRequired(ReferenceTypeIds.Organizes, false))
                    {
                        reference = NextChild();

                        if (reference != null)
                        {
                            return reference;
                        }
                    }

                    m_items = m_source.ArchiveFolder.GetItems();
                    m_stage = Stage.Items;
                    m_position = 0;
                }
                
                // enumerate items.
                if (m_stage == Stage.Items)
                {
                    if (IsRequired(ReferenceTypeIds.Organizes, false))
                    {
                        reference = NextChild();

                        if (reference != null)
                        {
                            return reference;
                        }

                        m_stage = Stage.Parents;
                        m_position = 0;
                    }
                }

                // enumerate parents.
                if (m_stage == Stage.Parents)
                {
                    if (IsRequired(ReferenceTypeIds.Organizes, true))
                    {
                        reference = NextChild();

                        if (reference != null)
                        {
                            return reference;
                        }

                        m_stage = Stage.Done;
                        m_position = 0;
                    }
                }

                // all done.
                return null;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Returns the next child.
        /// </summary>
        private IReference NextChild()
        {
            UnderlyingSystem system = (UnderlyingSystem)this.SystemContext.SystemHandle;

            NodeId targetId = null;

            // check if a specific browse name is requested.
            if (!QualifiedName.IsNull(base.BrowseName))
            {
                // check if match found previously.
                if (m_position == Int32.MaxValue)
                {
                    return null;
                }

                // browse name must be qualified by the correct namespace.
                if (m_source.BrowseName.NamespaceIndex != base.BrowseName.NamespaceIndex)
                {
                    return null;
                }

                // look for matching folder.
                if (m_stage == Stage.Folders && m_folders != null)
                {
                    for (int ii = 0; ii < m_folders.Length; ii++)
                    {
                        if (base.BrowseName.Name == m_folders[ii].Name)
                        {
                            targetId = ArchiveFolderState.ConstructId(m_folders[ii].UniquePath, m_source.NodeId.NamespaceIndex);
                            break;
                        }
                    }
                }

                // look for matching item.
                else if (m_stage == Stage.Items && m_items != null)
                {
                    for (int ii = 0; ii < m_items.Length; ii++)
                    {
                        if (base.BrowseName.Name == m_items[ii].Name)
                        {
                            targetId = ArchiveItemState.ConstructId(m_items[ii].UniquePath, m_source.NodeId.NamespaceIndex);
                            break;
                        }
                    }
                }

                // look for matching parent.
                else if (m_stage == Stage.Parents)
                {
                    ArchiveFolder parent = m_source.ArchiveFolder.GetParentFolder();

                    if (base.BrowseName.Name == parent.Name)
                    {
                        targetId = ArchiveFolderState.ConstructId(parent.UniquePath, m_source.NodeId.NamespaceIndex);
                    }
                }

                m_position = Int32.MaxValue;
            }

            // return the child at the next position.
            else
            {
                // look for next folder.
                if (m_stage == Stage.Folders && m_folders != null)
                {
                    if (m_position >= m_folders.Length)
                    {
                        return null;
                    }

                    targetId = ArchiveFolderState.ConstructId(m_folders[m_position++].UniquePath, m_source.NodeId.NamespaceIndex);
                }

                // look for next item.
                else if (m_stage == Stage.Items && m_items != null)
                {
                    if (m_position >= m_items.Length)
                    {
                        return null;
                    }

                    targetId = ArchiveItemState.ConstructId(m_items[m_position++].UniquePath, m_source.NodeId.NamespaceIndex);
                }

                // look for matching parent.
                else if (m_stage == Stage.Parents)
                {
                    ArchiveFolder parent = m_source.ArchiveFolder.GetParentFolder();

                    if (parent != null)
                    {
                        targetId = ArchiveFolderState.ConstructId(parent.UniquePath, m_source.NodeId.NamespaceIndex);
                    }
                }
            }

            // create reference.
            if (targetId != null)
            {
                return new NodeStateReference(ReferenceTypeIds.Organizes, false, targetId);
            }

            return null;
        }
        #endregion

        #region Stage Enumeration
        /// <summary>
        /// The stages available in a browse operation.
        /// </summary>
        private enum Stage
        {
            Begin,
            Folders,
            Items,
            Parents,
            Done
        }
        #endregion

        #region Private Fields
        private Stage m_stage;
        private int m_position;
        private ArchiveFolderState m_source;
        private ArchiveFolder[] m_folders;
        private ArchiveItem[] m_items;
        #endregion
    }
}
