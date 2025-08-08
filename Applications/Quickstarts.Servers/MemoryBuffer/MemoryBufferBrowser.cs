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
using System.IO;
using System.Reflection;
using System.Threading;
using System.Xml;
using Opc.Ua;
using Opc.Ua.Server;

namespace MemoryBuffer
{
    /// <summary>
    /// A class to browse the references for a memory buffer.
    /// </summary>
    public class MemoryBufferBrowser : NodeBrowser
    {
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

        /// <summary>
        /// Returns the next reference.
        /// </summary>
        /// <returns></returns>
        public override IReference Next()
        {
            lock (DataLock)
            {
                // enumerate pre-defined references.
                // always call first to ensure any pushed-back references are returned first.
                IReference reference = base.Next();

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

        /// <summary>
        /// Returns the next child.
        /// </summary>
        private NodeStateReference NextChild()
        {
            MemoryTagState tag = null;

            // check if a specific browse name is requested.
            if (!QualifiedName.IsNull(BrowseName))
            {
                // check if match found previously.
                if (m_position == uint.MaxValue)
                {
                    return null;
                }

                // browse name must be qualified by the correct namespace.
                if (m_buffer.TypeDefinitionId.NamespaceIndex != BrowseName.NamespaceIndex)
                {
                    return null;
                }

                string name = BrowseName.Name;

                for (int ii = 0; ii < name.Length; ii++)
                {
                    if (!"0123456789ABCDEF".Contains(name[ii], StringComparison.Ordinal))
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
                m_position = uint.MaxValue;
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

        private Stage m_stage;
        private uint m_position;
        private readonly MemoryBufferState m_buffer;
    }
}
