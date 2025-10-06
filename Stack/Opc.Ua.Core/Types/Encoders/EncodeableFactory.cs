/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Registry of encodeable objects based on the type id to be used
    /// in encoders and decoders.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This registry is used to store and retrieve underlying OPC UA
    /// system types.
    /// <br/></para>
    /// <para>
    /// You can manually add types using the <see cref="Builder"/>
    /// property exposed mutator. You can also import all types from
    /// a specified assembly. Once the types exist within the registry,
    /// these types can then be easily queried.
    /// <br/></para>
    /// </remarks>
    public sealed class EncodeableFactory : IEncodeableFactory
    {
        /// <summary>
        /// The default factory for the process.
        /// </summary>
        [Obsolete("Obtain a factory from a context or use EncodeableFactory.Create()")]
        public static EncodeableFactory GlobalFactory { get; } = new();

        /// <summary>
        /// Create single instance of the encodeable factory.
        /// </summary>
        private EncodeableFactory()
        {
            m_encodeableTypes = FrozenDictionary<ExpandedNodeId, Type>.Empty;
        }

        /// <summary>
        /// Clone the encodeable factory.
        /// </summary>
        private EncodeableFactory(EncodeableFactory factory)
        {
            m_encodeableTypes = factory.m_encodeableTypes;
        }

        /// <summary>
        /// Create single instance of the encodeable factory.
        /// </summary>
        private EncodeableFactory(FrozenDictionary<ExpandedNodeId, Type> encodeableTypes)
        {
            m_encodeableTypes = encodeableTypes;
        }

        /// <summary>
        /// Create a new encodeble factory initialized with all known types.
        /// </summary>
        /// <returns></returns>
        public static IEncodeableFactory Create()
        {
            return new EncodeableFactory(Root);
        }

        /// <inheritdoc/>
        public IEncodeableFactoryBuilder Builder => new EncodeableFactoryBuilder(this);

        /// <inheritdoc/>
        public IEnumerable<ExpandedNodeId> KnownTypes => m_encodeableTypes.Keys;

        /// <inheritdoc/>
        public bool TryGetSystemType(
            ExpandedNodeId typeId,
            [NotNullWhen(true)] out Type? systemType)
        {
            if (NodeId.IsNull(typeId))
            {
                systemType = null;
                return false;
            }
            return m_encodeableTypes.TryGetValue(typeId, out systemType);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new EncodeableFactory(m_encodeableTypes);
        }

        /// <summary>
        /// Returns the xml qualified name for the specified system type id.
        /// </summary>
        [Obsolete("Use TypeInfo.GetXmlName(Type) instead.")]
        public static XmlQualifiedName GetXmlName(Type systemType)
        {
            return TypeInfo.GetXmlName(systemType);
        }

        /// <summary>
        /// Returns the xml qualified name for the specified object.
        /// </summary>
        [Obsolete("Use TypeInfo.GetXmlName(object, IServiceMessageContext) instead.")]
        public static XmlQualifiedName GetXmlName(
            object value,
            IServiceMessageContext context)
        {
            return TypeInfo.GetXmlName(value, context);
        }

        /// <summary>
        /// Factory mutator
        /// </summary>
        private sealed class EncodeableFactoryBuilder : IEncodeableFactoryBuilder
        {
            /// <summary>
            /// Create mutator based on existing factory
            /// </summary>
            public EncodeableFactoryBuilder(EncodeableFactory factory)
            {
                m_factory = factory;
            }

            /// <inheritdoc/>
            public IEncodeableFactoryBuilder AddEncodeableType(
                Type systemType)
            {
                AddEncodeableType(systemType, null);
                return this;
            }

            /// <inheritdoc/>
            public IEncodeableFactoryBuilder AddEncodeableType(
                ExpandedNodeId encodingId,
                Type systemType)
            {
                if (!NodeId.IsNull(encodingId))
                {
                    m_encodeableTypes[encodingId] = systemType;
                }
                return this;
            }

            /// <inheritdoc/>
            public IEncodeableFactoryBuilder AddEncodeableTypes(Assembly assembly)
            {
                if (assembly == null)
                {
                    return this;
                }

                Type[] systemTypes = assembly.GetExportedTypes();
                var unboundTypeIds = new Dictionary<string, ExpandedNodeId?>();

                const string jsonEncodingSuffix = "_Encoding_DefaultJson";

                for (int ii = 0; ii < systemTypes.Length; ii++)
                {
                    if (systemTypes[ii].Name != "ObjectIds")
                    {
                        continue;
                    }

                    foreach (
                        FieldInfo field in systemTypes[ii].GetFields(
                            BindingFlags.Static | BindingFlags.Public))
                    {
                        if (field.Name.EndsWith(
                            jsonEncodingSuffix, StringComparison.Ordinal))
                        {
                            try
                            {
                                string name = field.Name[..^jsonEncodingSuffix.Length];
                                object? value = field.GetValue(null);

                                if (value is NodeId nodeId)
                                {
                                    unboundTypeIds[name] = new ExpandedNodeId(nodeId);
                                }
                                else
                                {
                                    unboundTypeIds[name] = (ExpandedNodeId?)value;
                                }
                            }
                            catch
                            {
                                // ignore errors.
                            }
                        }
                    }
                }

                for (int ii = 0; ii < systemTypes.Length; ii++)
                {
                    if (systemTypes[ii].GetTypeInfo().IsAbstract)
                    {
                        continue;
                    }

                    AddEncodeableType(systemTypes[ii], unboundTypeIds);
                }

                // only needed while adding assembly types
                unboundTypeIds.Clear();
                return this;
            }

            /// <inheritdoc/>
            public bool TryGetSystemType(
                ExpandedNodeId typeId,
                [NotNullWhen(true)] out Type? systemType)
            {
                if (NodeId.IsNull(typeId))
                {
                    systemType = null;
                    return false;
                }
                return m_encodeableTypes.TryGetValue(typeId, out systemType) ||
                    m_factory.TryGetSystemType(typeId, out systemType);
            }

            /// <summary>
            /// Build the factory. Returns the original factory if nothing changed.
            /// Uses a lock free algorithm to update the factory which could be
            /// rather heavy in case of multiple threads updating the factory at
            /// the same time. We assume this is a rare case.
            /// </summary>
            /// <returns></returns>
            public void Commit()
            {
                if (m_encodeableTypes.Count == 0)
                {
                    return;
                }
                FrozenDictionary<ExpandedNodeId, Type> current;
                FrozenDictionary<ExpandedNodeId, Type> replacement;
                do
                {
                    current = m_factory.m_encodeableTypes;
                    if (current.Count == 0)
                    {
                        // If empty just replace with a frozen copy
                        replacement = m_encodeableTypes.ToFrozenDictionary();
                    }
                    else
                    {
                        // Merge changes over the current state
                        var encodeableTypes = current
                            .ToDictionary(k => k.Key, v => v.Value);
                        foreach (KeyValuePair<ExpandedNodeId, Type> item in
                            m_encodeableTypes)
                        {
                            encodeableTypes[item.Key] = item.Value;
                        }
                        // Re-freeze
                        replacement = encodeableTypes.ToFrozenDictionary();
                    }
                }
                while (Interlocked.CompareExchange(ref m_factory.m_encodeableTypes, replacement,
                    current) != current);
                m_encodeableTypes.Clear();
            }

            /// <summary>
            /// Adds an extension type to the factory.
            /// </summary>
            /// <param name="systemType">The underlying system type to add to the factory</param>
            /// <param name="unboundTypeIds">A dictionary of unbound typeIds, e.g. JSON type ids
            /// referenced by object name.</param>
            private void AddEncodeableType(Type systemType,
                Dictionary<string, ExpandedNodeId?>? unboundTypeIds)
            {
                if (systemType == null)
                {
                    return;
                }

                if (systemType.GetTypeInfo().IsAbstract)
                {
                    return;
                }

                if (!typeof(IEncodeable).GetTypeInfo().IsAssignableFrom(
                    systemType.GetTypeInfo()))
                {
                    return;
                }

                if (Activator.CreateInstance(systemType) is not IEncodeable encodeable)
                {
                    return;
                }

                ExpandedNodeId nodeId = encodeable.TypeId;

                if (!NodeId.IsNull(nodeId))
                {
                    // check for default namespace.
                    if (nodeId.NamespaceUri == Namespaces.OpcUa)
                    {
                        nodeId = new ExpandedNodeId(nodeId.InnerNodeId);
                    }

                    m_encodeableTypes[nodeId] = systemType;
                }

                nodeId = encodeable.BinaryEncodingId;

                if (!NodeId.IsNull(nodeId))
                {
                    // check for default namespace.
                    if (nodeId.NamespaceUri == Namespaces.OpcUa)
                    {
                        nodeId = new ExpandedNodeId(nodeId.InnerNodeId);
                    }

                    m_encodeableTypes[nodeId] = systemType;
                }

                try
                {
                    nodeId = encodeable.XmlEncodingId;
                }
                catch (NotSupportedException)
                {
                    nodeId = ExpandedNodeId.Null;
                }

                if (!NodeId.IsNull(nodeId))
                {
                    // check for default namespace.
                    if (nodeId.NamespaceUri == Namespaces.OpcUa)
                    {
                        nodeId = new ExpandedNodeId(nodeId.InnerNodeId);
                    }

                    m_encodeableTypes[nodeId] = systemType;
                }

                if (encodeable is IJsonEncodeable jsonEncodeable)
                {
                    try
                    {
                        nodeId = jsonEncodeable.JsonEncodingId;
                    }
                    catch (NotSupportedException)
                    {
                        nodeId = ExpandedNodeId.Null;
                    }

                    if (!NodeId.IsNull(nodeId))
                    {
                        // check for default namespace.
                        if (nodeId.NamespaceUri == Namespaces.OpcUa)
                        {
                            nodeId = new ExpandedNodeId(nodeId.InnerNodeId);
                        }

                        m_encodeableTypes[nodeId] = systemType;
                    }
                }
                else if (unboundTypeIds != null &&
                    unboundTypeIds.TryGetValue(systemType.Name,
                        out ExpandedNodeId? jsonEncodingId) &&
                    jsonEncodingId != null)
                {
                    m_encodeableTypes[jsonEncodingId] = systemType;
                }
            }

            private readonly EncodeableFactory m_factory;
            private readonly Dictionary<ExpandedNodeId, Type> m_encodeableTypes = [];
        }

        /// <summary>
        /// Create default factory which contains all known encodeable types.
        /// </summary>
        private static EncodeableFactory Root
        {
            get
            {
                var factory = new EncodeableFactory();
                factory.Builder
                    .AddEncodeableTypes(typeof(EncodeableFactory).Assembly)
                    .Commit();
                return factory;
            }
        }

        /// <summary>
        /// Frozen dictionary perform well for > 100 items with hits
        /// Lower sizes perform well in case of misses (no type found).
        /// The default size of the root factory is 1.5k entries.
        /// We assume most factories will be larger. More benchmarking
        /// must be done and improvements can be made to ensure
        /// ExpandedNodeId produces a good hash.
        /// </summary>
        private FrozenDictionary<ExpandedNodeId, Type> m_encodeableTypes;
    }
}
