// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    using Opc.Ua.Client.Nodes;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Caches data type systems per endpoint.
    /// </summary>
    /// <remarks>
    /// Support for V1.03 dictionaries with the following known restrictions:
    /// - Only Binary and Xml type systems are currently supported.
    /// - Structured types are mapped to the V1.04 structured type definition.
    /// - Enumerated types are mapped to the V1.04 enum definition.
    /// - V1.04 OptionSet are not supported.
    /// - When a type is not found and a dictionary must be loaded the whole
    ///   dictionary is loaded and parsed and all types are added.
    /// </remarks>
    internal sealed class DataTypeSystemManager : IDataTypeSystemManager
    {
        /// <summary>
        /// Create the data type system cache
        /// </summary>
        /// <param name="nodeCache"></param>
        /// <param name="context"></param>
        /// <param name="loggerFactory"></param>
        public DataTypeSystemManager(INodeCache nodeCache,
            IServiceMessageContext context, ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            var binary = new Lazy<Task<DataTypeSystem>>(async () =>
            {
                var binary = new DefaultBinaryTypeSystem(nodeCache, context,
                    _loggerFactory.CreateLogger<DefaultBinaryTypeSystem>());
                await binary.LoadAsync(default).ConfigureAwait(false);
                return binary;
            }, LazyThreadSafetyMode.ExecutionAndPublication);
            var xml = new Lazy<Task<DataTypeSystem>>(async () =>
            {
                var xml = new DefaultXmlTypeSystem(nodeCache, context,
                    _loggerFactory.CreateLogger<DefaultXmlTypeSystem>());
                await xml.LoadAsync(default).ConfigureAwait(false);
                return xml;
            }, LazyThreadSafetyMode.ExecutionAndPublication);
            _systems.TryAdd((QualifiedName)BrowseNames.DefaultBinary, binary);
            _systems.TryAdd((QualifiedName)BrowseNames.DefaultXml, xml);
        }

        /// <inheritdoc/>
        public ValueTask<DictionaryDataTypeDefinition?> GetDataTypeDefinitionAsync(
            QualifiedName encoding, ExpandedNodeId dataTypeId, CancellationToken ct)
        {
            if (!_systems.TryGetValue(encoding, out var typeSystem))
            {
                throw new ServiceResultException(StatusCodes.BadEncodingError,
                    $"Unsupported encoding {encoding}.");
            }
            if (typeSystem.Value.IsCompletedSuccessfully)
            {
                return typeSystem.Value.Result.GetDataTypeDefinitionAsync(dataTypeId, ct);
            }
            return GetDataTypeDefinitionAsyncCore(typeSystem, dataTypeId, ct);
            static async ValueTask<DictionaryDataTypeDefinition?> GetDataTypeDefinitionAsyncCore(
                 Lazy<Task<DataTypeSystem>> typeSystem, ExpandedNodeId dataTypeId,
                 CancellationToken ct)
            {
                var ts = await typeSystem.Value.ConfigureAwait(false);
                return await ts.GetDataTypeDefinitionAsync(dataTypeId, ct).ConfigureAwait(false);
            }
        }

        private readonly ConcurrentDictionary<QualifiedName, Lazy<Task<DataTypeSystem>>> _systems = [];
        private readonly ILoggerFactory _loggerFactory;
    }
}
