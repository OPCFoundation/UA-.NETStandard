/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging;
using Opc.Ua.ComplexTypes;

namespace Opc.Ua.Server.ComplexTypes
{
    /// <summary>
    /// Registers custom Structure / Enumeration DataTypes on a hosted
    /// server's <see cref="IEncodeableFactory"/> so the server can encode
    /// and decode instances of those types on the wire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hosts typically feed this loader with definitions discovered while
    /// parsing a NodeSet2 (the parsed
    /// <see cref="Opc.Ua.Export.UANodeSet"/> exposes
    /// <c>DataTypeDefinition</c> for every custom DataType), or they
    /// build the definitions programmatically.
    /// </para>
    /// <para>
    /// The loader uses an injected <see cref="IComplexTypeFactory"/> to
    /// materialise runtime .NET types — either the AOT-friendly
    /// <c>DefaultComplexTypeFactory</c> from <c>Opc.Ua.ComplexTypes</c>
    /// or the Reflection.Emit-based <c>ComplexTypeBuilderFactory</c> when
    /// concrete .NET classes are required.
    /// </para>
    /// <para>
    /// Field DataTypes that are not yet known to the encodeable factory
    /// cause the owning structure to be deferred. Call
    /// <see cref="Flush"/> after registering a batch to retry deferred
    /// structures (multiple passes are made automatically until no
    /// further progress is possible).
    /// </para>
    /// </remarks>
    public class ServerComplexTypeSystem
    {
        private const int MaxLoopCount = 100;

        /// <summary>
        /// Initializes the type system bound to a hosted server's address
        /// space and a complex type factory.
        /// </summary>
        /// <param name="server">The hosted server.</param>
        /// <param name="factory">The complex type builder factory used to
        /// produce runtime types.</param>
        /// <param name="telemetry">The host's telemetry context.</param>
        /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
        public ServerComplexTypeSystem(
            IServerInternal server,
            IComplexTypeFactory factory,
            ITelemetryContext telemetry)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_logger = telemetry.CreateLogger<ServerComplexTypeSystem>();
        }

        /// <summary>
        /// Registers an enumeration DataType on the server's encodeable
        /// factory.
        /// </summary>
        /// <param name="dataTypeId">The DataType NodeId in the server's
        /// address space.</param>
        /// <param name="browseName">The DataType browse name.</param>
        /// <param name="enumDefinition">The enumeration definition.</param>
        /// <returns><c>true</c> when the runtime type was created and
        /// registered.</returns>
        /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
        public bool RegisterEnumeration(
            NodeId dataTypeId,
            QualifiedName browseName,
            EnumDefinition enumDefinition)
        {
            if (dataTypeId.IsNull)
            {
                throw new ArgumentException("DataType NodeId must not be null.", nameof(dataTypeId));
            }
            if (browseName.IsNull)
            {
                throw new ArgumentException("Browse name must not be null.", nameof(browseName));
            }
            if (enumDefinition is null)
            {
                throw new ArgumentNullException(nameof(enumDefinition));
            }

            IComplexTypeBuilder builder = m_factory.Create(
                GetNamespaceUri(dataTypeId.NamespaceIndex),
                dataTypeId.NamespaceIndex);
            IEnumeratedType? type = builder.AddEnumType(browseName, enumDefinition);
            if (type is null)
            {
                return false;
            }
            m_server.Factory.Builder
                .AddEnumeratedType(type)
                .Commit();
            return true;
        }

        /// <summary>
        /// Registers a structured DataType on the server's encodeable
        /// factory. If any field DataType is not yet resolvable, the
        /// structure is queued and retried on the next
        /// <see cref="Flush"/> call.
        /// </summary>
        /// <param name="dataTypeId">The DataType NodeId in the server's
        /// address space.</param>
        /// <param name="browseName">The DataType browse name.</param>
        /// <param name="structureDefinition">The structure definition.</param>
        /// <returns><c>true</c> when the structure was built and
        /// registered immediately; <c>false</c> when it was deferred.</returns>
        /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
        public bool RegisterStructure(
            NodeId dataTypeId,
            QualifiedName browseName,
            StructureDefinition structureDefinition)
        {
            if (dataTypeId.IsNull)
            {
                throw new ArgumentException("DataType NodeId must not be null.", nameof(dataTypeId));
            }
            if (browseName.IsNull)
            {
                throw new ArgumentException("Browse name must not be null.", nameof(browseName));
            }
            if (structureDefinition is null)
            {
                throw new ArgumentNullException(nameof(structureDefinition));
            }

            if (TryBuildStructure(dataTypeId, browseName, structureDefinition))
            {
                return true;
            }

            m_pending.Add((dataTypeId, browseName, structureDefinition));
            return false;
        }

        /// <summary>
        /// Retries any structured DataTypes that were deferred because
        /// their field DataTypes were not yet resolvable. Multiple passes
        /// are made until no further progress is possible.
        /// </summary>
        /// <returns>The number of pending structures still unresolved
        /// after all passes (zero on full success).</returns>
        public int Flush()
        {
            int previousCount;
            int iteration = 0;
            do
            {
                previousCount = m_pending.Count;
                for (int i = m_pending.Count - 1; i >= 0; i--)
                {
                    (NodeId nodeId, QualifiedName name, StructureDefinition def) = m_pending[i];
                    if (TryBuildStructure(nodeId, name, def))
                    {
                        m_pending.RemoveAt(i);
                    }
                }
                iteration++;
            }
            while (m_pending.Count > 0 &&
                   m_pending.Count < previousCount &&
                   iteration < MaxLoopCount);

            if (m_pending.Count > 0)
            {
                m_logger.LogWarning(
                    "{Remaining} custom structure DataType(s) could not be resolved " +
                    "(unresolved field DataTypes).",
                    m_pending.Count);
            }
            return m_pending.Count;
        }

        private bool TryBuildStructure(
            NodeId dataTypeId,
            QualifiedName browseName,
            StructureDefinition definition)
        {
            // Verify every field's DataType is already known. If not,
            // signal the caller to defer.
            foreach (StructureField field in definition.Fields)
            {
                if (ResolveFieldType(field) is null)
                {
                    return false;
                }
            }

            IComplexTypeBuilder builder = m_factory.Create(
                GetNamespaceUri(dataTypeId.NamespaceIndex),
                dataTypeId.NamespaceIndex);

            IComplexTypeFieldBuilder fieldBuilder = builder.AddStructuredType(
                browseName,
                definition);
            fieldBuilder.AddTypeIdAttribute(
                NodeId.ToExpandedNodeId(dataTypeId, m_server.NamespaceUris),
                ExpandedNodeId.Null,
                ExpandedNodeId.Null);

            bool allowSubTypes = definition.StructureType is
                StructureType.StructureWithSubtypedValues or
                StructureType.UnionWithSubtypedValues;
            int order = 1;
            foreach (StructureField field in definition.Fields)
            {
                IType? fieldType = ResolveFieldType(field);
                if (fieldType is null)
                {
                    // Race: lost a type between the pre-check and here. Defer.
                    return false;
                }
                fieldBuilder.AddField(field, fieldType, order, allowSubTypes);
                order++;
            }

            IEncodeableType created = fieldBuilder.CreateType();
            m_server.Factory.Builder
                .AddEncodeableType(created)
                .Commit();
            return true;
        }

        private IType? ResolveFieldType(StructureField field)
        {
            NodeId dataTypeId = field.DataType;
            if (dataTypeId.IsNull)
            {
                return null;
            }
            ExpandedNodeId expanded = NodeId.ToExpandedNodeId(
                dataTypeId,
                m_server.NamespaceUris);
            if (m_server.Factory.TryGetType(expanded, out IType? type))
            {
                return type;
            }
            return null;
        }

        private string GetNamespaceUri(ushort namespaceIndex)
            => m_server.NamespaceUris.GetString(namespaceIndex) ?? string.Empty;

        private readonly IServerInternal m_server;
        private readonly IComplexTypeFactory m_factory;
        private readonly ILogger m_logger;
        private readonly List<(NodeId NodeId, QualifiedName Name, StructureDefinition Definition)>
            m_pending = [];
    }
}
