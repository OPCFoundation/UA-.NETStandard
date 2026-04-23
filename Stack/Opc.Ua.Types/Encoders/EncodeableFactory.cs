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

#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// <para>
    /// Registry of encodeable type activators that can be retrieved
    /// using the type id or encoding ids in encoders and decoders.
    /// Used to register source generated and custom (hand crafted)
    /// types.
    /// <br/></para>
    /// <para>
    /// You can manually add types using the <see cref="Builder"/>
    /// property exposed mutator. You can also import all types from
    /// a specified assembly when you are not required to be trimmable
    /// or NativeAOT compliant.
    /// <br/></para>
    /// <para>
    /// Once the types exist within the registry they can be consumed
    /// by encoders and decoders.
    /// <br/></para>
    /// </summary>
    public sealed class EncodeableFactory : IEncodeableFactory
    {
        /// <summary>
        /// Create an empty instance of the encodeable factory.
        /// </summary>
        internal EncodeableFactory()
        {
#pragma warning disable IDE0301 // Simplify collection initialization
            m_encodeableTypes = FrozenDictionary<ExpandedNodeId, IEncodeableType>.Empty;
            m_enumeratedTypes = FrozenDictionary<ExpandedNodeId, IEnumeratedType>.Empty;
            m_xmlNameToType = FrozenDictionary<XmlQualifiedName, IType>.Empty;
#pragma warning restore IDE0301 // Simplify collection initialization
        }

        /// <summary>
        /// Clone the encodeable factory.
        /// </summary>
        private EncodeableFactory(EncodeableFactory factory)
        {
            m_encodeableTypes = factory.m_encodeableTypes;
            m_enumeratedTypes = factory.m_enumeratedTypes;
            m_xmlNameToType = factory.m_xmlNameToType;
        }

        /// <inheritdoc/>
        public IEncodeableFactoryBuilder Builder => new EncodeableFactoryBuilder(this);

        /// <inheritdoc/>
        public IEnumerable<ExpandedNodeId> KnownTypeIds
            => m_encodeableTypes.Keys.Concat(m_enumeratedTypes.Keys);

        /// <inheritdoc/>
        public bool TryGetEncodeableType(
            ExpandedNodeId typeId,
            [NotNullWhen(true)] out IEncodeableType? encodeableType)
        {
            if (typeId.IsNull)
            {
                encodeableType = null;
                return false;
            }
            return m_encodeableTypes.TryGetValue(typeId, out encodeableType);
        }

        /// <inheritdoc/>
        public bool TryGetEnumeratedType(
            ExpandedNodeId typeId,
            [NotNullWhen(true)] out IEnumeratedType? enumeratedType)
        {
            if (typeId.IsNull)
            {
                enumeratedType = null;
                return false;
            }
            return m_enumeratedTypes.TryGetValue(typeId, out enumeratedType);
        }

        /// <inheritdoc/>
        public bool TryGetType(
            XmlQualifiedName xmlName,
            [NotNullWhen(true)] out IType? type)
        {
            if (xmlName == null)
            {
                type = null;
                return false;
            }
            return m_xmlNameToType.TryGetValue(xmlName, out type);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return Fork();
        }

        /// <summary>
        /// Create a fork of the factory which can be independently modified.
        /// The new factory will share the same internal dictionaries until
        /// a modification is made
        /// </summary>
        /// <returns></returns>
        public EncodeableFactory Fork()
        {
            return new EncodeableFactory(this);
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
                [DynamicallyAccessedMembers(
                    DynamicallyAccessedMemberTypes.PublicConstructors)]
                Type systemType)
            {
                AddType(systemType, null);
                return this;
            }

            /// <inheritdoc/>
            public IEncodeableFactoryBuilder AddEncodeableType(
                IEncodeableType type)
            {
                if (type == null)
                {
                    throw new ArgumentNullException(nameof(type));
                }
                AddEncodeableType(type, null);
                m_xmlNameToType[type.XmlName] = type;
                return this;
            }

            /// <inheritdoc/>
            public IEncodeableFactoryBuilder AddEnumeratedType(
                IEnumeratedType type)
            {
                if (type == null)
                {
                    throw new ArgumentNullException(nameof(type));
                }
                if (DataTypeAttribute.TryGetTypeIdsFromType(
                    type.Type,
                    out ExpandedNodeId typeId,
                    out ExpandedNodeId binaryEncodingId,
                    out ExpandedNodeId xmlEncodingId,
                    out ExpandedNodeId jsonEncodingId))
                {
                    if (!typeId.IsNull)
                    {
                        m_enumeratedTypes[typeId] = type;
                    }
                    if (!binaryEncodingId.IsNull)
                    {
                        m_enumeratedTypes[binaryEncodingId] = type;
                    }
                    if (!xmlEncodingId.IsNull)
                    {
                        m_enumeratedTypes[xmlEncodingId] = type;
                    }
                    if (!jsonEncodingId.IsNull)
                    {
                        m_enumeratedTypes[jsonEncodingId] = type;
                    }
                }
                m_xmlNameToType[type.XmlName] = type;
                return this;
            }

            /// <inheritdoc/>
            public IEncodeableFactoryBuilder AddEncodeableType(
                ExpandedNodeId encodingId,
                IEncodeableType type)
            {
                if (!encodingId.IsNull)
                {
                    m_encodeableTypes[encodingId] = type ??
                        throw new ArgumentNullException(nameof(type));
                    m_xmlNameToType[type.XmlName] = type;
                }
                return this;
            }

            /// <inheritdoc/>
            public IEncodeableFactoryBuilder AddEnumeratedType(
                ExpandedNodeId encodingId,
                IEnumeratedType type)
            {
                if (!encodingId.IsNull)
                {
                    m_enumeratedTypes[encodingId] = type ??
                        throw new ArgumentNullException(nameof(type));
                    m_xmlNameToType[type.XmlName] = type;
                }
                return this;
            }

            /// <inheritdoc/>
            public IEncodeableFactoryBuilder AddType(
                ExpandedNodeId encodingId,
                [DynamicallyAccessedMembers(
                    DynamicallyAccessedMemberTypes.PublicConstructors)] Type systemType)
            {
                if (!encodingId.IsNull)
                {
                    IType? type = ReflectionBasedType.From(systemType, encodingId);
                    switch (type)
                    {
                        case IEncodeableType encodeableType:
                            m_encodeableTypes[encodingId] = encodeableType;
                            break;
                        case IEnumeratedType enumeratedType:
                            m_enumeratedTypes[encodingId] = enumeratedType;
                            break;
                        case null:
                            return this;
                    }
                    m_xmlNameToType[type.XmlName] = type;
                }
                return this;
            }

            /// <inheritdoc/>
            [RequiresUnreferencedCode("Scans assembly types via reflection.")]
            public IEncodeableFactoryBuilder AddEncodeableTypes(Assembly assembly)
            {
                if (assembly == null)
                {
                    return this;
                }

                Type[] systemTypes = assembly.GetExportedTypes();
                var unboundTypeIds = new Dictionary<string, ExpandedNodeId>();

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
                                else if (value is ExpandedNodeId expandedNodeId)
                                {
                                    unboundTypeIds[name] = expandedNodeId;
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

                    AddType(systemTypes[ii], unboundTypeIds);
                }

                // only needed while adding assembly types
                unboundTypeIds.Clear();
                return this;
            }

            /// <inheritdoc/>
            public bool TryGetEncodeableType(
                ExpandedNodeId typeId,
                [NotNullWhen(true)] out IEncodeableType? encodeableType)
            {
                if (typeId.IsNull)
                {
                    encodeableType = null;
                    return false;
                }
                return m_encodeableTypes.TryGetValue(typeId, out encodeableType) ||
                    m_factory.TryGetEncodeableType(typeId, out encodeableType);
            }

            /// <inheritdoc/>
            public bool TryGetEnumeratedType(
                ExpandedNodeId typeId,
                [NotNullWhen(true)] out IEnumeratedType? enumeratedType)
            {
                if (typeId.IsNull)
                {
                    enumeratedType = null;
                    return false;
                }
                return m_enumeratedTypes.TryGetValue(typeId, out enumeratedType) ||
                    m_factory.TryGetEnumeratedType(typeId, out enumeratedType);
            }

            /// <inheritdoc/>
            public bool TryGetType(
                XmlQualifiedName xmlName,
                [NotNullWhen(true)] out IType? type)
            {
                if (xmlName == null)
                {
                    type = null;
                    return false;
                }
                return m_xmlNameToType.TryGetValue(xmlName, out type) ||
                    m_factory.TryGetType(xmlName, out type);
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
                CompareExchange(ref m_factory.m_encodeableTypes, m_encodeableTypes);
                CompareExchange(ref m_factory.m_enumeratedTypes, m_enumeratedTypes);
                CompareExchange(ref m_factory.m_xmlNameToType, m_xmlNameToType);
            }

            internal static void CompareExchange<TKey, TValue>(
                ref FrozenDictionary<TKey, TValue> target,
                Dictionary<TKey, TValue> dictionary)
                where TKey : notnull
            {
                if (dictionary.Count == 0)
                {
                    return;
                }
                FrozenDictionary<TKey, TValue> current;
                FrozenDictionary<TKey, TValue> replacement;
                do
                {
                    current = target;
                    if (current.Count == 0)
                    {
                        // If empty just replace with a frozen copy
                        replacement = dictionary.ToFrozenDictionary();
                    }
                    else
                    {
                        // Merge changes over the current state
                        var encodeableTypes = current
                            .ToDictionary(k => k.Key, v => v.Value);
                        foreach (KeyValuePair<TKey, TValue> item in dictionary)
                        {
                            encodeableTypes[item.Key] = item.Value;
                        }
                        // Re-freeze
                        replacement = encodeableTypes.ToFrozenDictionary();
                    }
                }
                while (Interlocked.CompareExchange(
                    ref target,
                    replacement,
                    current) != current);
                dictionary.Clear();
            }

            /// <summary>
            /// Adds an extension type to the factory.
            /// </summary>
            /// <param name="systemType">The underlying system type to add to the factory</param>
            /// <param name="unboundTypeIds">A dictionary of unbound typeIds, e.g. JSON type ids
            /// referenced by object name.</param>
            private void AddType(
                [DynamicallyAccessedMembers(
                    DynamicallyAccessedMemberTypes.PublicConstructors)]
                Type systemType,
                Dictionary<string, ExpandedNodeId>? unboundTypeIds)
            {
                switch (ReflectionBasedType.From(systemType))
                {
                    case IEncodeableType encodeableType:
                        AddEncodeableType(encodeableType, unboundTypeIds);
                        break;
                    case IEnumeratedType enumeratedType:
                        AddEnumeratedType(enumeratedType);
                        break;
                }
            }

            /// <summary>
            /// Adds an encodeable type to the factory.
            /// </summary>
            /// <param name="encodeableType">The encodeable type to add to the factory</param>
            /// <param name="unboundTypeIds">A dictionary of unbound typeIds, e.g. JSON type ids
            /// referenced by object name.</param>
            /// <exception cref="InvalidOperationException"></exception>
            private void AddEncodeableType(
                IEncodeableType encodeableType,
                Dictionary<string, ExpandedNodeId>? unboundTypeIds)
            {
                if (DataTypeAttribute.TryGetTypeIdsFromType(
                    encodeableType.Type,
                    out ExpandedNodeId typeId,
                    out ExpandedNodeId binaryEncodingId,
                    out ExpandedNodeId xmlEncodingId,
                    out ExpandedNodeId jsonEncodingId) &&
                    !typeId.IsNull &&
                    !binaryEncodingId.IsNull &&
                    !xmlEncodingId.IsNull)
                {
                    m_encodeableTypes[Fix(typeId)] = encodeableType;
                    m_encodeableTypes[Fix(binaryEncodingId)] = encodeableType;
                    m_encodeableTypes[Fix(xmlEncodingId)] = encodeableType;
                    if (!jsonEncodingId.IsNull ||
                        (unboundTypeIds != null &&
                            unboundTypeIds.TryGetValue(
                                encodeableType.Type.Name,
                                out jsonEncodingId) &&
                            !jsonEncodingId.IsNull))
                    {
                        m_encodeableTypes[Fix(jsonEncodingId)] = encodeableType;
                    }
                    return;
                    // Else fallback to creating the type
                }

                IEncodeable encodeable = encodeableType.CreateInstance() ??
                    throw new InvalidOperationException(
                        $"Encodeable type {encodeableType} cannot create instance");
                ExpandedNodeId nodeId = encodeable.TypeId;
                if (!nodeId.IsNull)
                {
                    m_encodeableTypes[Fix(nodeId)] = encodeableType;
                    if (encodeableType is
                        ReflectionBasedType.ReflectionBasedEncodeableType
                            reflectionBasedType)
                    {
                        reflectionBasedType.TypeId = Fix(nodeId);
                    }
                }

                nodeId = encodeable.BinaryEncodingId;
                if (!nodeId.IsNull)
                {
                    m_encodeableTypes[Fix(nodeId)] = encodeableType;
                }
                try
                {
                    nodeId = encodeable.XmlEncodingId;
                }
                catch (NotSupportedException)
                {
                    nodeId = ExpandedNodeId.Null;
                }
                if (!nodeId.IsNull)
                {
                    m_encodeableTypes[Fix(nodeId)] = encodeableType;
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
                    if (!nodeId.IsNull)
                    {
                        m_encodeableTypes[Fix(nodeId)] = encodeableType;
                    }
                }
                else if (unboundTypeIds != null &&
                    unboundTypeIds.TryGetValue(
                        encodeableType.Type.Name,
                        out jsonEncodingId) &&
                    !jsonEncodingId.IsNull)
                {
                    m_encodeableTypes[jsonEncodingId] = encodeableType;
                }

                static ExpandedNodeId Fix(ExpandedNodeId nodeId)
                {
                    // check for default namespace.
                    if (nodeId.NamespaceUri == Types.Namespaces.OpcUa)
                    {
                        return new ExpandedNodeId(nodeId.InnerNodeId);
                    }
                    return nodeId;
                }
            }

            private readonly EncodeableFactory m_factory;
            private readonly Dictionary<XmlQualifiedName, IType> m_xmlNameToType = [];
            private readonly Dictionary<ExpandedNodeId, IEncodeableType> m_encodeableTypes = [];
            private readonly Dictionary<ExpandedNodeId, IEnumeratedType> m_enumeratedTypes = [];
        }

        /// <summary>
        /// The root encodeable factory (Only to be used by Opc.Ua assembly)
        /// </summary>
        internal static Lazy<EncodeableFactory> Root { get; }
            = new(() => new EncodeableFactory());

        /// <summary>
        /// <para>
        /// Frozen dictionary perform well for > 100 items with hits.
        /// Lower sizes perform even better in case of misses. The
        /// default size of the root factory is 1.5k entries. We assume
        /// most factories will be larger.
        /// </para>
        /// <para>
        /// Lookup of one existing item and one that does not exist
        /// in the root encodeablefactory shows 15-20% improvements
        /// in lookup performance and slightly lower allocation on
        /// .NET 9.0 which match other public benchmarks when the key
        /// of the frozen dictionary is a reference type. Note that
        /// past implementation's use of reader/writer lock is not
        /// factored in.
        /// </para>
        /// <para>
        /// | Method     | Mean     | Ratio | Alloc Ratio |
        /// |----------- |---------:|------:|------------:|
        /// | Dictionary | 720.6 us |  1.00 |        1.00 |
        /// | Frozen     | 621.1 us |  0.86 |        0.94 |
        /// </para>
        /// </summary>
        private FrozenDictionary<ExpandedNodeId, IEncodeableType> m_encodeableTypes;
        private FrozenDictionary<ExpandedNodeId, IEnumeratedType> m_enumeratedTypes;
        private FrozenDictionary<XmlQualifiedName, IType> m_xmlNameToType;
    }

    /// <summary>
    /// Default reflection based implementation of an encodeable types.
    /// </summary>
    public abstract class ReflectionBasedType : IType
    {
        /// <summary>
        /// Create reflection based type
        /// </summary>
        /// <param name="type"></param>
        protected ReflectionBasedType(
            [DynamicallyAccessedMembers(
                DynamicallyAccessedMemberTypes.PublicConstructors)]
            Type type)
        {
            m_type = type;
        }

        /// <inheritdoc/>
        public Type Type => m_type;

        /// <inheritdoc/>
        public XmlQualifiedName XmlName => field ??= GetXmlName();

        /// <summary>
        /// Create type wrapper from system type.
        /// </summary>
        public static IType? From(
            [DynamicallyAccessedMembers(
                DynamicallyAccessedMemberTypes.PublicConstructors)]
            Type? systemType,
            ExpandedNodeId typeId = default)
        {
            if (systemType == null)
            {
                return null;
            }
            System.Reflection.TypeInfo typeInfo = systemType.GetTypeInfo();
            if (typeInfo.IsEnum)
            {
                return new ReflectionBasedEnumeratedType(systemType);
            }
            else if (
                !typeInfo.IsAbstract &&
                typeof(IEncodeable).GetTypeInfo().IsAssignableFrom(typeInfo) &&
                typeInfo.GetConstructor([]) != null)
            {
                return new ReflectionBasedEncodeableType(systemType)
                {
                    TypeId = typeId
                };
            }
            else
            {
                return null;
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return m_type.FullName ?? m_type.Name;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return m_type.Equals((obj as IEncodeableType)?.Type);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return m_type.GetHashCode();
        }

        /// <summary>
        /// Get lazy as it is only needed for XML encoding and we want to
        /// avoid the overhead of reflection on registration if not needed
        /// </summary>
        /// <returns></returns>
        private XmlQualifiedName GetXmlName()
        {
            foreach (DataContractAttribute contract in
                m_type.GetTypeInfo().GetCustomAttributes<DataContractAttribute>(true))
            {
                if (string.IsNullOrEmpty(contract.Name))
                {
                    return new XmlQualifiedName(m_type.Name, contract.Namespace);
                }
                return new XmlQualifiedName(contract.Name, contract.Namespace);
            }
            return new XmlQualifiedName(m_type.FullName);
        }

        /// <summary>
        /// Default reflection based implementation of an enumerated types.
        /// </summary>
        internal sealed class ReflectionBasedEnumeratedType :
            ReflectionBasedType,
            IEnumeratedType
        {
            /// <summary>
            /// Create enumerated type
            /// </summary>
            internal ReflectionBasedEnumeratedType(
                [DynamicallyAccessedMembers(
                    DynamicallyAccessedMemberTypes.PublicConstructors)]
                Type type)
                : base(type)
            {
            }

            /// <inheritdoc/>
            [UnconditionalSuppressMessage("AOT", "IL3050", Justification =
                "On platforms without dynamic code support this method returns 0.")]
            public EnumValue Default =>
#if NET8_0_OR_GREATER
                !RuntimeFeature.IsDynamicCodeSupported ?
                    new EnumValue(0, Type) :
#endif
                    EnumValue.GetDefault(Type);

            /// <inheritdoc/>
            public bool TryGetSymbol(int value, out string? symbol)
            {
                object enumValue = EnumHelper.Int32ToEnum(value, Type);
                symbol = Enum.GetName(Type, enumValue);
                return symbol != null;
            }

            /// <inheritdoc/>
            public bool TryGetValue(string symbol, out int value)
            {
                try
                {
                    object enumValue = Enum.Parse(Type, symbol);
                    value = EnumHelper.EnumToInt32(enumValue, Type);
                    return true;
                }
                catch
                {
                    value = default;
                    return false;
                }
            }
        }

        /// <summary>
        /// Default reflection based implementation of an encodeable types.
        /// </summary>
        internal sealed class ReflectionBasedEncodeableType :
            ReflectionBasedType,
            IEncodeableType
        {
            /// <summary>
            /// Create encodeable type
            /// </summary>
            internal ReflectionBasedEncodeableType(
                [DynamicallyAccessedMembers(
                DynamicallyAccessedMemberTypes.PublicConstructors)]
            Type type)
                : base(type)
            {
            }

            /// <summary>
            /// Type id the encodeable type is registered for
            /// </summary>
            internal ExpandedNodeId TypeId { get; set; }

            /// <inheritdoc/>
            public IEncodeable CreateInstance()
            {
                if (Activator.CreateInstance(m_type) is not IEncodeable encodeable)
                {
                    throw new InvalidOperationException(
                        $"Cannot create instance of type {m_type.FullName ?? m_type.Name}");
                }
                if (encodeable is IDynamicComplexTypeInstance dynamicInstance)
                {
                    dynamicInstance.TypeId = TypeId;
                }
                return encodeable;
            }
        }

        /// <summary>
        /// Type that can be insnatiated via public constructor
        /// </summary>
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        protected readonly Type m_type;
    }
}
