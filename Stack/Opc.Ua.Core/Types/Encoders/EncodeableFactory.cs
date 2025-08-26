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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Creates encodeable objects based on the type id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This factory is used to store and retrieve underlying OPC UA system types.
    /// <br/></para>
    /// <para>
    /// You can manually add types. You can also import all types from a specified assembly.
    /// Once the types exist within the factory, these types can be then easily queried.
    /// <br/></para>
    /// </remarks>
    public class EncodeableFactory : IEncodeableFactory, IDisposable
    {
        /// <summary>
        /// Creates a factory initialized with the types in the core library.
        /// </summary>
        public EncodeableFactory()
        {
            m_encodeableTypes = [];
            AddEncodeableTypes(GetType().GetTypeInfo().Assembly);

#if DEBUG
            InstanceId = Interlocked.Increment(ref s_globalInstanceCount);
#endif
        }

        /// <summary>
        /// Creates a factory which is marked as shared and initialized with the types in the core library.
        /// </summary>
        public EncodeableFactory(bool shared)
        {
            m_encodeableTypes = [];
            AddEncodeableTypes(Utils.DefaultOpcUaCoreAssemblyFullName);

#if DEBUG
            InstanceId = Interlocked.Increment(ref s_globalInstanceCount);
            m_shared = true;
#endif
        }

        /// <summary>
        /// Creates a factory by copying the table from another factory.
        /// </summary>
        public EncodeableFactory(IEncodeableFactory factory)
        {
            m_encodeableTypes = [];

#if DEBUG
            InstanceId = Interlocked.Increment(ref s_globalInstanceCount);
#endif
            if (factory != null)
            {
                m_encodeableTypes = ((EncodeableFactory)factory.Clone()).m_encodeableTypes;
            }
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_readerWriterLockSlim?.Dispose();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Loads the types from an assembly.
        /// </summary>
        private void AddEncodeableTypes(string assemblyName)
        {
            try
            {
                var an = new AssemblyName(assemblyName);
                var assembly = Assembly.Load(an);
                AddEncodeableTypes(assembly);
            }
            catch (Exception)
            {
                Utils.LogError("Could not load encodeable types from assembly: {0}", assemblyName);
            }
        }

        /// <summary>
        /// Adds an extension type to the factory.
        /// </summary>
        /// <param name="systemType">The underlying system type to add to the factory</param>
        /// <param name="unboundTypeIds">A dictionary of unbound typeIds, e.g. JSON type ids referenced by object name.</param>
        private void AddEncodeableType(
            Type systemType,
            Dictionary<string, ExpandedNodeId> unboundTypeIds)
        {
            if (systemType == null)
            {
                return;
            }

            if (!typeof(IEncodeable).GetTypeInfo().IsAssignableFrom(systemType.GetTypeInfo()))
            {
                return;
            }

            if (Activator.CreateInstance(systemType) is not IEncodeable encodeable)
            {
                return;
            }

#if DEBUG
            if (m_shared)
            {
                Utils.LogTrace(
                    "WARNING: Adding type '{0}' to shared Factory #{1}.",
                    systemType.Name,
                    InstanceId);
            }
#endif

            // assume write lock
            Debug.Assert(m_readerWriterLockSlim.IsWriteLockHeld);

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
                unboundTypeIds.TryGetValue(systemType.Name, out ExpandedNodeId jsonEncodingId))
            {
                m_encodeableTypes[jsonEncodingId] = systemType;
            }
        }

        /// <summary>
        /// The default factory for the process.
        /// </summary>
        /// <remarks>
        /// The default factory for the process.
        /// </remarks>
        public static EncodeableFactory GlobalFactory { get; } = new EncodeableFactory();

        /// <summary>
        /// Returns the xml qualified name for the specified system type id.
        /// </summary>
        /// <remarks>
        /// Returns the xml qualified name for the specified system type id.
        /// </remarks>
        /// <param name="systemType">The underlying type to query and return the Xml qualified name of</param>
        public static XmlQualifiedName GetXmlName(Type systemType)
        {
            if (systemType == null)
            {
                return null;
            }

            object[] attributes =
            [
                .. systemType.GetTypeInfo().GetCustomAttributes(typeof(DataContractAttribute), true)
            ];

            if (attributes != null)
            {
                for (int ii = 0; ii < attributes.Length; ii++)
                {
                    if (attributes[ii] is DataContractAttribute contract)
                    {
                        if (string.IsNullOrEmpty(contract.Name))
                        {
                            return new XmlQualifiedName(systemType.Name, contract.Namespace);
                        }

                        return new XmlQualifiedName(contract.Name, contract.Namespace);
                    }
                }
            }

            attributes =
            [
                .. systemType.GetTypeInfo()
                    .GetCustomAttributes(typeof(CollectionDataContractAttribute), true)
            ];

            if (attributes != null)
            {
                for (int ii = 0; ii < attributes.Length; ii++)
                {
                    if (attributes[ii] is CollectionDataContractAttribute contract)
                    {
                        if (string.IsNullOrEmpty(contract.Name))
                        {
                            return new XmlQualifiedName(systemType.Name, contract.Namespace);
                        }

                        return new XmlQualifiedName(contract.Name, contract.Namespace);
                    }
                }
            }

            if (systemType == typeof(byte[]))
            {
                return new XmlQualifiedName("ByteString");
            }

            return new XmlQualifiedName(systemType.FullName);
        }

        /// <summary>
        /// Returns the xml qualified name for the specified object.
        /// </summary>
        /// <remarks>
        /// Returns the xml qualified name for the specified object.
        /// </remarks>
        /// <param name="value">The object to query and return the Xml qualified name of</param>
        /// <param name="context">Context</param>
        public static XmlQualifiedName GetXmlName(object value, IServiceMessageContext context)
        {
            if (value is IDynamicComplexTypeInstance xmlEncodeable)
            {
                XmlQualifiedName xmlName = xmlEncodeable.GetXmlName(context);
                if (xmlName != null)
                {
                    return xmlName;
                }
            }
            return GetXmlName(value?.GetType());
        }

        /// <summary>
        /// Returns a unique identifier for the table instance. Used to debug problems with shared tables.
        /// </summary>
        public int InstanceId
        {
#if DEBUG
            get;
#else
            get => 0;
#endif
        }

        /// <summary>
        /// Adds an extension type to the factory.
        /// </summary>
        /// <param name="systemType">The underlying system type to add to the factory</param>
        public void AddEncodeableType(Type systemType)
        {
            m_readerWriterLockSlim.EnterWriteLock();
            try
            {
                AddEncodeableType(systemType, null);
            }
            finally
            {
                m_readerWriterLockSlim.ExitWriteLock();
            }
        }

        /// <summary>
        /// Associates an encodeable type with an encoding id.
        /// </summary>
        /// <param name="encodingId">A NodeId for a Data Type Encoding node</param>
        /// <param name="systemType">The system type to use for the specified encoding.</param>
        public void AddEncodeableType(ExpandedNodeId encodingId, Type systemType)
        {
            if (systemType != null && !NodeId.IsNull(encodingId))
            {
#if DEBUG
                if (m_shared)
                {
                    Utils.LogWarning(
                        "WARNING: Adding type '{0}' to shared Factory #{1}.",
                        systemType.Name,
                        InstanceId);
                }
#endif
                m_readerWriterLockSlim.EnterWriteLock();
                try
                {
                    m_encodeableTypes[encodingId] = systemType;
                }
                finally
                {
                    m_readerWriterLockSlim.ExitWriteLock();
                }
            }
        }

        /// <summary>
        /// Adds all encodeable types exported from an assembly to the factory.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Adds all encodeable types exported from an assembly to the factory.
        /// <br/></para>
        /// <para>
        /// This method uses reflection on the specified assembly to export all of the
        /// types the assembly exposes, and automatically adds all types that implement
        /// the <see cref="IEncodeable"/> interface, to the factory.
        /// <br/></para>
        /// </remarks>
        /// <param name="assembly">The assembly containing the types to add to the factory</param>
        public void AddEncodeableTypes(Assembly assembly)
        {
            if (assembly != null)
            {
#if DEBUG
                if (m_shared)
                {
                    Utils.LogWarning(
                        "WARNING: Adding types from assembly '{0}' to shared Factory #{1}.",
                        assembly.FullName,
                        InstanceId);
                }
#endif

                m_readerWriterLockSlim.EnterWriteLock();
                try
                {
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
                            if (field.Name.EndsWith(jsonEncodingSuffix, StringComparison.Ordinal))
                            {
                                try
                                {
                                    string name = field.Name[..^jsonEncodingSuffix.Length];
                                    object value = field.GetValue(null);

                                    if (value is NodeId nodeId)
                                    {
                                        unboundTypeIds[name] = new ExpandedNodeId(nodeId);
                                    }
                                    else
                                    {
                                        unboundTypeIds[name] = (ExpandedNodeId)value;
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
                finally
                {
                    m_readerWriterLockSlim.ExitWriteLock();
                }
            }
        }

        /// <summary>
        /// Adds an enumerable of extension types to the factory.
        /// </summary>
        /// <param name="systemTypes">The underlying system types to add to the factory</param>
        public void AddEncodeableTypes(IEnumerable<Type> systemTypes)
        {
            m_readerWriterLockSlim.EnterWriteLock();
            try
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
            finally
            {
                m_readerWriterLockSlim.ExitWriteLock();
            }
        }

        /// <summary>
        /// Returns the system type for the specified type id.
        /// </summary>
        /// <remarks>
        /// Returns the system type for the specified type id.
        /// </remarks>
        /// <param name="typeId">The type id to return the system-type of</param>
        public Type GetSystemType(ExpandedNodeId typeId)
        {
            m_readerWriterLockSlim.EnterReadLock();
            try
            {
                if (NodeId.IsNull(typeId) ||
                    !m_encodeableTypes.TryGetValue(typeId, out Type systemType))
                {
                    return null;
                }

                return systemType;
            }
            finally
            {
                m_readerWriterLockSlim.ExitReadLock();
            }
        }

        /// <summary>
        /// The dictionary of encodeabe types.
        /// </summary>
        public IReadOnlyDictionary<ExpandedNodeId, Type> EncodeableTypes => m_encodeableTypes;

        /// <inheritdoc/>
        public object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new EncodeableFactory(null);

            m_readerWriterLockSlim.EnterReadLock();
            try
            {
                foreach (KeyValuePair<ExpandedNodeId, Type> current in m_encodeableTypes)
                {
                    clone.m_encodeableTypes.Add(current.Key, current.Value);
                }
            }
            finally
            {
                m_readerWriterLockSlim.ExitReadLock();
            }

            return clone;
        }

        private readonly ReaderWriterLockSlim m_readerWriterLockSlim = new();
        private readonly Dictionary<ExpandedNodeId, Type> m_encodeableTypes;
#if DEBUG
        private readonly bool m_shared;
        private static int s_globalInstanceCount;
#endif
    }
}
