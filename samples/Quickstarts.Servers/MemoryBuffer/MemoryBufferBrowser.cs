// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Opc.Ua;

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
            : base(
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
        public override IReference? Next()
        {
            // enumerate pre-defined references.
            // always call first to ensure any pushed-back references are returned first.
            IReference? reference = base.Next();

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

        /// <summary>
        /// Returns the next child.
        /// </summary>
        private NodeStateReference? NextChild()
        {
            MemoryTagState? tag;

            // check if a specific browse name is requested.
            if (!BrowseName.IsNull)
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

                string? name = BrowseName.Name;

                for (int ii = 0; ii < name!.Length; ii++)
                {
                    if (!"0123456789ABCDEF".Contains(name[ii], StringComparison.Ordinal))
                    {
                        return null;
                    }
                }

                m_position = Convert.ToUInt32(name, 16);

                // check for memory overflow.
                if (m_position >= m_buffer!.SizeInBytes!.Value)
                {
                    return null;
                }

                tag = new MemoryTagState(m_buffer, m_position);
                m_position = uint.MaxValue;
            }
            // return the child at the next position.
            else
            {
                if (m_position >= m_buffer!.SizeInBytes!.Value)
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
