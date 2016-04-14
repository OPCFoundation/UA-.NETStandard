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
using System.Xml;
using System.IO;
using System.Reflection;
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;

namespace MemoryBuffer
{
    /// <summary>
    /// A class to browse the references for a memory buffer. 
    /// </summary>
    public class MemoryBufferBrowser : NodeBrowser
    {
        #region Constructors
        /// <summary>
        /// Creates a new browser object with a set of filters.
        /// </summary>
        public MemoryBufferBrowser(
            ISystemContext context,
            ViewDescription view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection browseDirection,
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly,
            MemoryBufferState buffer)
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
            m_buffer = buffer;
            m_stage = Stage.Begin;
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Returns the next reference.
        /// </summary>
        /// <returns></returns>
        public override IReference Next()
        {
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
                    m_stage = Stage.Components;
                    m_position = 0;
                }

                // don't start browsing huge number of references when only internal references are requested.
                if (InternalOnly)
                {
                    return null;
                }
                
                // enumerate components.
                if (m_stage == Stage.Components)
                {
                    if (IsRequired(ReferenceTypeIds.HasComponent, false))
                    {
                        reference = NextChild();

                        if (reference != null)
                        {
                            return reference;
                        }
                    }

                    m_stage = Stage.ModelParents;
                    m_position = 0;
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
            MemoryTagState tag = null;

            // check if a specific browse name is requested.
            if (!QualifiedName.IsNull(base.BrowseName))
            {
                // check if match found previously.
                if (m_position == UInt32.MaxValue)
                {
                    return null;
                }

                // browse name must be qualified by the correct namespace.
                if (m_buffer.TypeDefinitionId.NamespaceIndex != base.BrowseName.NamespaceIndex)
                {
                    return null;
                }

                string name = base.BrowseName.Name;

                for (int ii = 0; ii < name.Length; ii++)
                {
                    if ("0123456789ABCDEF".IndexOf(name[ii]) == -1)
                    {
                        return null;
                    }
                }

                m_position = Convert.ToUInt32(name, 16);
                
                // check for memory overflow.
                if (m_position >= m_buffer.SizeInBytes.Value)
                {
                    return null;
                }

                tag = new MemoryTagState(m_buffer, m_position);
                m_position = UInt32.MaxValue;
            }

            // return the child at the next position.
            else
            {
                if (m_position >= m_buffer.SizeInBytes.Value)
                {
                    return null;
                }

                tag = new MemoryTagState(m_buffer, m_position);
                m_position += m_buffer.ElementSize;

                // check for memory overflow.
                if (m_position >= m_buffer.SizeInBytes.Value)
                {
                    return null;
                }
            }

            return new NodeStateReference(ReferenceTypeIds.HasComponent, false, tag);
        }
        #endregion

        #region Stage Enumeration
        /// <summary>
        /// The stages available in a browse operation.
        /// </summary>
        private enum Stage
        {
            Begin,
            Components,
            ModelParents,
            Done
        }
        #endregion

        #region Private Fields
        private Stage m_stage;
        private uint m_position;
        private MemoryBufferState m_buffer;
        #endregion
    }
}
