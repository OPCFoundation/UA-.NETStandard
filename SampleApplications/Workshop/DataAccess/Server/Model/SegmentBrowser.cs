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

namespace Quickstarts.DataAccessServer
{
    /// <summary>
    /// Browses the children of a segment.
    /// </summary>
    public class SegmentBrowser : NodeBrowser
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
        public SegmentBrowser(
            ISystemContext context,
            ViewDescription view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection browseDirection,
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly,
            SegmentState source)
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
                    m_segments = system.FindSegments(m_source.SegmentPath);
                    m_stage = Stage.Segments;
                    m_position = 0;
                }

                // don't start browsing huge number of references when only internal references are requested.
                if (InternalOnly)
                {
                    return null;
                }
                
                // enumerate segments.
                if (m_stage == Stage.Segments)
                {
                    if (IsRequired(ReferenceTypeIds.Organizes, false))
                    {
                        reference = NextChild();

                        if (reference != null)
                        {
                            return reference;
                        }
                    }

                    m_blocks = system.FindBlocks(m_source.SegmentPath);
                    m_stage = Stage.Blocks;
                    m_position = 0;
                }
                
                // enumerate blocks.
                if (m_stage == Stage.Blocks)
                {
                    if (IsRequired(ReferenceTypeIds.Organizes, false))
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

                // look for matching segment.
                if (m_stage == Stage.Segments && m_segments != null)
                {
                    for (int ii = 0; ii < m_segments.Count; ii++)
                    {
                        if (base.BrowseName.Name == m_segments[ii].Name)
                        {
                            targetId = ModelUtils.ConstructIdForSegment(m_segments[ii].Id, m_source.NodeId.NamespaceIndex);
                            break;
                        }
                    }
                }

                // look for matching block.
                if (m_stage == Stage.Blocks && m_blocks != null)
                {
                    for (int ii = 0; ii < m_blocks.Count; ii++)
                    {
                        UnderlyingSystemBlock block = system.FindBlock(m_blocks[ii]);

                        if (block != null && base.BrowseName.Name == block.Name)
                        {
                            targetId = ModelUtils.ConstructIdForBlock(m_blocks[ii], m_source.NodeId.NamespaceIndex);
                            break;
                        }
                    }
                }

                m_position = Int32.MaxValue;
            }

            // return the child at the next position.
            else
            {
                // look for next segment.
                if (m_stage == Stage.Segments && m_segments != null)
                {
                    if (m_position >= m_segments.Count)
                    {
                        return null;
                    }

                    targetId = ModelUtils.ConstructIdForSegment(m_segments[m_position++].Id, m_source.NodeId.NamespaceIndex);
                }

                // look for next block.
                else if (m_stage == Stage.Blocks && m_blocks != null)
                {
                    if (m_position >= m_blocks.Count)
                    {
                        return null;
                    }

                    targetId = ModelUtils.ConstructIdForBlock(m_blocks[m_position++], m_source.NodeId.NamespaceIndex);
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
            Segments,
            Blocks,
            Done
        }
        #endregion

        #region Private Fields
        private Stage m_stage;
        private int m_position;
        private SegmentState m_source;
        private IList<UnderlyingSystemSegment> m_segments;
        private IList<string> m_blocks;
        #endregion
    }
}
