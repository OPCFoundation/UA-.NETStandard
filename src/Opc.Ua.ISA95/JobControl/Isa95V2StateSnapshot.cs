// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.JobControl
{
    /// <summary>
    /// An owned copy of the V2 state array as received.
    /// </summary>
    internal sealed class Isa95V2StateSnapshot
    {
        internal Isa95V2StateSnapshot(
            ArrayOf<V2.ISA95StateDataType> source,
            Isa95JobCanonicalState state,
            ushort namespaceIndex)
        {
            State = state;
            NamespaceIndex = namespaceIndex;
            m_entries = Copy(source, namespaceIndex, namespaceIndex);
        }

        internal Isa95JobCanonicalState State { get; }
        internal ushort NamespaceIndex { get; }

        internal ArrayOf<V2.ISA95StateDataType> ToArray(ushort namespaceIndex)
        {
            return Copy(m_entries, NamespaceIndex, namespaceIndex);
        }

        private static ArrayOf<V2.ISA95StateDataType> Copy(
            ArrayOf<V2.ISA95StateDataType> source,
            ushort fromIndex,
            ushort toIndex)
        {
            if (source.IsNull)
            {
                return default;
            }
            var copy = new V2.ISA95StateDataType[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                V2.ISA95StateDataType entry = source[i];
                copy[i] = new V2.ISA95StateDataType
                {
                    BrowsePath = CopyPath(entry.BrowsePath, fromIndex, toIndex),
                    StateNumber = entry.StateNumber,
                    StateText = entry.StateText
                };
            }
            return copy.ToArrayOf();
        }

        private static RelativePath CopyPath(RelativePath path, ushort fromIndex, ushort toIndex)
        {
            if (path == null)
            {
                return null!;
            }
            if (path.Elements.IsNull)
            {
                return new RelativePath { Elements = default };
            }
            var elements = new RelativePathElement[path.Elements.Count];
            for (int i = 0; i < elements.Length; i++)
            {
                RelativePathElement element = path.Elements[i];
                QualifiedName name = element.TargetName;
                NodeId reference = element.ReferenceTypeId;
                if (fromIndex != toIndex)
                {
                    if ((name.NamespaceIndex == fromIndex || name.NamespaceIndex == 0) &&
                        Isa95V2StateMachine.IsSubstateMachine(name.Name))
                    {
                        name = new QualifiedName(name.Name, toIndex);
                    }
                    else if (name.NamespaceIndex != 0)
                    {
                        throw new InvalidOperationException("The state path uses a namespace that cannot be remapped.");
                    }
                    if (reference.NamespaceIndex != 0)
                    {
                        throw new InvalidOperationException("The state path uses a custom reference that cannot be remapped.");
                    }
                }
                elements[i] = new RelativePathElement
                {
                    TargetName = name,
                    ReferenceTypeId = reference,
                    IsInverse = element.IsInverse,
                    IncludeSubtypes = element.IncludeSubtypes
                };
            }
            return new RelativePath { Elements = elements.ToArrayOf() };
        }

        private readonly ArrayOf<V2.ISA95StateDataType> m_entries;
    }
}
