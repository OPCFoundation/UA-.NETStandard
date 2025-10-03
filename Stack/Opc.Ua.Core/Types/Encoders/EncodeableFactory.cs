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
using System.Linq;
using System.Reflection;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// The encodeable factory is now considered legacy and should not be
    /// used anymore. Instead use the EncodeableRegistry directly. This
    /// implementation wraps an encodable registry but at the expense that
    /// the builder pattern is used very inefficiently.
    /// </summary>
    public class EncodeableFactory : IEncodeableFactory
    {
        /// <summary>
        /// Creates a root factory initialized with the types in the core library.
        /// </summary>
        public EncodeableFactory()
            : this (EncodeableRegistry.Default)
        {
        }

        /// <summary>
        /// Creates a factory by copying the table from another factory.
        /// </summary>
        public EncodeableFactory(IEncodeableFactory factory)
            : this (factory.AsRegistry())
        {
        }

        /// <summary>
        /// Creates a factory from a registry
        /// </summary>
        private EncodeableFactory(IImmutableEncodeableDictionary registry)
        {
            m_registry = registry;
        }

        /// <summary>
        /// Adapt the registry as a factory.
        /// </summary>
        /// <param name="registry"></param>
        /// <returns></returns>
        public static EncodeableFactory From(IImmutableEncodeableDictionary registry)
        {
            return new EncodeableFactory(registry);
        }

        /// <inheritdoc/>
        public void AddEncodeableType(Type systemType)
        {
            m_registry = m_registry
                .Update(builder => builder.AddEncodeableType(systemType));
        }

        /// <inheritdoc/>
        public void AddEncodeableType(ExpandedNodeId encodingId, Type systemType)
        {
            m_registry = m_registry
                .Update(builder => builder.AddEncodeableType(encodingId, systemType));
        }

        /// <inheritdoc/>
        public void AddEncodeableTypes(Assembly assembly)
        {
            m_registry = m_registry
                .Update(builder => builder.AddEncodeableTypes(assembly));
        }

        /// <inheritdoc/>
        public void AddEncodeableTypes(IEnumerable<Type> systemTypes)
        {
            m_registry = m_registry
                .Update(builder => builder.AddEncodeableTypes(systemTypes));
        }

        /// <inheritdoc/>
        public Type? GetSystemType(ExpandedNodeId typeId)
        {
            return m_registry.GetSystemType(typeId);
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<ExpandedNodeId, Type> EncodeableTypes
            => m_registry.EncodeableTypes;

        /// <inheritdoc/>
        public object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new EncodeableFactory(this);
        }

        /// <summary>
        /// The default factory for the process.
        /// </summary>
        [Obsolete("Create a new root factory or clone an existing one")]
        public static EncodeableFactory GlobalFactory { get; } = new();

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
        public static XmlQualifiedName GetXmlName(object value, IServiceMessageContext context)
        {
            return TypeInfo.GetXmlName(value, context);
        }

        /// <summary>
        /// Get access to the internal registry implementation
        /// </summary>
        /// <returns></returns>
        internal IImmutableEncodeableDictionary GetRegistry()
        {
            return m_registry;
        }

        private IImmutableEncodeableDictionary m_registry;
    }

    /// <summary>
    /// Registers encodeable objects based on the type id to be used in encoders
    /// and decoders.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This registry is used to store and retrieve underlying OPC UA system types.
    /// <br/></para>
    /// <para>
    /// You can manually add types. You can also import all types from a specified
    /// assembly. Once the types exist within the registry, these types can be then
    /// easily queried.
    /// <br/></para>
    /// </remarks>
    public sealed class EncodeableRegistry : IImmutableEncodeableDictionary
    {
        /// <summary>
        /// Encodeable registry filled with all known encodeable types.
        /// </summary>
        public static IImmutableEncodeableDictionary Default { get; } = CreateDefaultRegistry();

        /// <summary>
        /// Create single instance of the encodeable registry.
        /// </summary>
        private EncodeableRegistry()
        {
            m_encodeableTypes = FrozenDictionary<ExpandedNodeId, Type>.Empty;
        }

        /// <summary>
        /// Create single instance of the encodeable registry.
        /// </summary>
        private EncodeableRegistry(FrozenDictionary<ExpandedNodeId, Type> encodeableTypes)
        {
            m_encodeableTypes = encodeableTypes;
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<ExpandedNodeId, Type> EncodeableTypes => m_encodeableTypes;

        /// <inheritdoc/>
        public bool TryGetSystemType(ExpandedNodeId typeId, [NotNullWhen(true)] out Type? type)
        {
            return m_encodeableTypes.TryGetValue(typeId, out type);
        }

        /// <inheritdoc/>
        public IImmutableEncodeableDictionary Update(Action<IImmutableEncodeableDictionaryBuilder> builder)
        {
            var registryBuilder = new EncodeableRegistryBuilder(this);
            builder(registryBuilder);
            return registryBuilder.Build();
        }

        /// <summary>
        /// Registry builder
        /// </summary>
        private sealed class EncodeableRegistryBuilder : IImmutableEncodeableDictionaryBuilder
        {
            /// <summary>
            /// Create builder based on existing registry
            /// </summary>
            public EncodeableRegistryBuilder(EncodeableRegistry registry)
            {
                m_registry = registry;
            }

            /// <inheritdoc/>
            public void AddEncodeableType(Type systemType)
            {
                AddEncodeableType(systemType, null);
            }

            /// <inheritdoc/>
            public void AddEncodeableType(ExpandedNodeId encodingId, Type systemType)
            {
                m_encodeableTypes[encodingId] = systemType;
            }

            /// <inheritdoc/>
            public void AddEncodeableTypes(Assembly assembly)
            {
                if (assembly == null)
                {
                    return;
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
                        if (field.Name.EndsWith(jsonEncodingSuffix, StringComparison.Ordinal))
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
            }

            /// <inheritdoc/>
            public void AddEncodeableTypes(IEnumerable<Type> systemTypes)
            {
                foreach (Type type in systemTypes)
                {
                    if (type.GetTypeInfo().IsAbstract)
                    {
                        continue;
                    }

                    AddEncodeableType(type, null);
                }
            }

            /// <summary>
            /// Build the registry. Returns the original registry if nothing changed.
            /// </summary>
            /// <returns></returns>
            internal IImmutableEncodeableDictionary Build()
            {
                if (m_encodeableTypes.Count == 0)
                {
                    return m_registry;
                }
                if (m_registry.m_encodeableTypes.Count == 0)
                {
                    return new EncodeableRegistry(m_encodeableTypes.ToFrozenDictionary());
                }
                var content = m_encodeableTypes.ToDictionary(k => k.Key, v => v.Value);
                foreach (KeyValuePair<ExpandedNodeId, Type> item in m_registry.m_encodeableTypes)
                {
                    content[item.Key] = item.Value;
                }
                return new EncodeableRegistry(content.ToFrozenDictionary());
            }

            /// <summary>
            /// Adds an extension type to the registry.
            /// </summary>
            /// <param name="systemType">The underlying system type to add to the registry</param>
            /// <param name="unboundTypeIds">A dictionary of unbound typeIds, e.g. JSON type ids
            /// referenced by object name.</param>
            private void AddEncodeableType(
                Type systemType,
                Dictionary<string, ExpandedNodeId?>? unboundTypeIds)
            {
                if (systemType == null)
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

            private readonly EncodeableRegistry m_registry;
            private readonly Dictionary<ExpandedNodeId, Type> m_encodeableTypes = [];
        }

        /// <summary>
        /// Create a registry from a dictionary of encodeable types.
        /// </summary>
        /// <param name="encodeableTypes"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        internal static IImmutableEncodeableDictionary From(IReadOnlyDictionary<ExpandedNodeId, Type> encodeableTypes)
        {
            return new EncodeableRegistry(encodeableTypes.ToFrozenDictionary());
        }

        /// <summary>
        /// Create default registry which contains all known encodeable types.
        /// </summary>
        private static IImmutableEncodeableDictionary CreateDefaultRegistry()
        {
            var builder = new EncodeableRegistryBuilder(new EncodeableRegistry());
            builder.AddEncodeableTypes(typeof(EncodeableRegistry).Assembly);
            return builder.Build();
        }

        private readonly FrozenDictionary<ExpandedNodeId, Type> m_encodeableTypes;
    }
}
