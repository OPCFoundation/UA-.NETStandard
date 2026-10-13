/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.PubSub.Schema;
using Opc.Ua.Schema;
using Opc.Ua.Schema.Bsd;
using Opc.Ua.Schema.Json;
using Opc.Ua.Schema.OpenApi;
using Opc.Ua.Schema.Xsd;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Six bounded schema workflows over the SDK's definition, complex-type and schema providers.
    /// </summary>
    [McpServerToolType]
    public sealed class SchemaTools
    {
        /// <summary>
        /// Creates injectable schema tools; the local context and resolver need no active UA session.
        /// </summary>
        public SchemaTools(
            OpcUaSessionManager sessions, McpFileTransfers files, OpcUaMcpOptions shared, McpSchemaOptions options,
            IServiceMessageContext? localContext = null, IDataTypeDefinitionResolver? registered = null,
            ArrayOf<IUaSchemaGenerator> generators = default)
        {
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_files = files ?? throw new ArgumentNullException(nameof(files));
            m_shared = shared ?? throw new ArgumentNullException(nameof(shared));
            m_options = options?.Snapshot() ?? throw new ArgumentNullException(nameof(options));
            m_local = localContext ?? ServiceMessageContext.Create(sessions.Telemetry);
            m_registered = registered;
            m_generators = generators.IsEmpty
                ? [new XsdSchemaGenerator(), new BsdSchemaGenerator(), new JsonSchemaGenerator()]
                : generators;
        }

        /// <summary>
        /// Lists a bounded live page of registered or server-advertised DataTypes.
        /// </summary>
        [McpServerTool(Name = "schema_list_types", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Lists a bounded DataType page. Registered is local and defaults to the OPC UA namespace; " +
            "Session browses HasSubtype below rootTypeId. Offsets are live, not durable cursors. " +
            "Returns items, hasMore and nextOffset. Oversized traversal fails instead of claiming completeness.")]
        public Task<CallToolResult> ListTypesAsync(
            [Description("Source, namespace and live-page settings; e.g. source=Registered and maxResults=25.")]
            SchemaListInput input,
            [Description(SchemaMcpFilters.SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                ArgumentOutOfRangeException.ThrowIfNegative(input.Offset);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(input.Offset, m_options.MaxTypes);
                ArgumentOutOfRangeException.ThrowIfLessThan(input.MaxResults, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(input.MaxResults, Math.Min(500, m_options.MaxTypes));
                (ISession? session, IServiceMessageContext context) = Source(input.Source, sessionName);
                if (session is not null)
                {
                    return await McpCompanionTools.PageAsync(
                        SchemaTypeDiscovery.RemoteAsync(session, Parse(input.RootTypeId, context),
                            input.NamespaceUri, m_options, token),
                        reference => McpModelAccess.Reference(session, reference),
                        input.Offset, input.MaxResults, token).ConfigureAwait(false);
                }
                if (input.RootTypeId != "i=24")
                {
                    throw new ArgumentException("rootTypeId applies only to Session discovery.");
                }
                var items = new JsonArray();
                bool hasMore = false;
                int offset = 0;
                foreach (UaTypeDescription type in SchemaTypeDiscovery.Registered(
                    context, m_registered, input.NamespaceUri ?? Namespaces.OpcUa, m_options, token))
                {
                    if (offset++ < input.Offset)
                    {
                        continue;
                    }
                    if (items.Count == input.MaxResults)
                    {
                        hasMore = true;
                        break;
                    }
                    items.Add((JsonNode)TypeIdentity(type));
                }
                return new JsonObject
                {
                    ["items"] = items,
                    ["offset"] = input.Offset,
                    ["nextOffset"] = hasMore ? input.Offset + items.Count : null,
                    ["hasMore"] = hasMore,
                    ["consistency"] = "live"
                };
            }, ct);
        }

        /// <summary>
        /// Inspects one actual definition, retaining field ranks, OptionSet representation and attribute quality.
        /// </summary>
        [McpServerTool(Name = "schema_inspect_type", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Inspects one actual structure or enum definition without loading client types. " +
            "Returns qualified identity, the SDK-encoded definition and, for Session, attribute quality/timestamps. " +
            "Missing, uncertain or unsupported definitions are errors, never inferred successful schemas.")]
        public Task<CallToolResult> InspectTypeAsync(
            [Description("One DataType identifier and its source; e.g. typeId=i=296 with source=Registered.")]
            SchemaInspectInput input,
            [Description(SchemaMcpFilters.SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                (ISession? session, IServiceMessageContext context) = Source(input.Source, sessionName);
                NodeId id = Parse(input.TypeId, context);
                JsonObject? qualities = null;
                UaTypeDescription type;
                if (session is null)
                {
                    SchemaDefinitionSnapshot snapshot = Snapshot(context);
                    token.ThrowIfCancellationRequested();
                    type = snapshot.InspectRegistered(id);
                }
                else
                {
                    ArrayOf<DataValue> values = await SchemaTypeDiscovery.ReadAttributesAsync(session, id, token)
                        .ConfigureAwait(false);
                    qualities = new JsonObject
                    {
                        ["browseName"] = McpCompanionJson.DataValue(values[0].WithWrappedValue(Variant.Null), context),
                        ["dataTypeDefinition"] =
                            McpCompanionJson.DataValue(values[1].WithWrappedValue(Variant.Null), context),
                        ["isAbstract"] = McpCompanionJson.DataValue(values[2].WithWrappedValue(Variant.Null), context)
                    };
                    if (values.FindIndex(value => value.IsNotGood) >= 0)
                    {
                        return new JsonObject
                        {
                            ["error"] = true,
                            ["typeId"] = id.ToString(),
                            ["message"] = "A DataType attribute has non-good quality.",
                            ["attributes"] = qualities
                        };
                    }
                    (type, _) = await SchemaTypeDiscovery.DescribeAsync(session, id, values, m_options, token)
                        .ConfigureAwait(false);
                }
                JsonObject result = TypeIdentity(type);
                result["definition"] = McpCompanionJson.Encode(type.Definition, context);
                result["isStructureOptionSet"] = type.IsStructureOptionSet;
                result["attributes"] = qualities;
                return result;
            }, ct);
        }

        /// <summary>
        /// Explicitly loads only validated finite roots through the default AOT-safe complex-type factory.
        /// </summary>
        [McpServerTool(Name = "schema_load_types", ReadOnly = false, Destructive = false, Idempotent = true)]
        [Description("Explicitly registers a bounded DataType closure in the selected client session's factory. " +
            "Uses the SDK's AOT-safe default complex-type factory, not Reflection.Emit or host DLL loading. " +
            "No whole-server/dictionary fallback. Returns per-root loaded/status results and session identity; " +
            "successful client-side registrations remain on partial failure.")]
        public Task<CallToolResult> LoadTypesAsync(
            [Description("Explicit DataType roots, e.g. typeIds=[\"ns=2;s=Record\"]; " +
                "no automatic namespace-wide loading.")]
            SchemaLoadInput input,
            [Description(SchemaMcpFilters.SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                ArgumentOutOfRangeException.ThrowIfLessThan(input.TypeIds.Count, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(input.TypeIds.Count, m_options.MaxTypes);
                ISession session = m_sessions.GetSessionOrThrow(sessionName);
                ArrayOf<NodeId> roots = input.TypeIds.ConvertAll(value => Parse(value, session.MessageContext));
                if (roots.ToList().Distinct().Count() != roots.Count)
                {
                    throw new ArgumentException("Repeated type identifiers are not permitted.");
                }
                SchemaDefinitionSnapshot snapshot = Snapshot(session.MessageContext);
                await snapshot.LoadAsync(session, roots, token).ConfigureAwait(false);
                var resolver = new SchemaComplexTypeResolver(snapshot, session.Factory);
                await resolver.PrepareAsync(session, token).ConfigureAwait(false);
                using var loader = new ComplexTypeSystem(resolver, new DefaultComplexTypeFactory(),
                    m_sessions.Telemetry)
                {
                    DisableDataTypeDictionary = true
                };
                var results = new JsonArray();
                bool error = false;
                for (int index = 0; index < roots.Count; index++)
                {
                    NodeId root = roots[index];
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        IType? loaded = await loader.LoadTypeAsync(root, subTypes: false, throwOnError: true, token)
                            .ConfigureAwait(false);
                        error |= loaded is null;
                        results.Add((JsonNode)new JsonObject
                        {
                            ["typeId"] = root.ToString(),
                            ["loaded"] = loaded is not null,
                            ["statusCode"] = loaded is null ? "BadNotSupported" : "Good"
                        });
                    }
                    catch (ServiceResultException exception)
                    {
                        error = true;
                        results.Add((JsonNode)new JsonObject
                        {
                            ["typeId"] = root.ToString(),
                            ["loaded"] = false,
                            ["statusCodeValue"] = exception.StatusCode.Code,
                            ["message"] = exception.Message
                        });
                    }
                }
                return new JsonObject
                {
                    ["error"] = error,
                    ["results"] = results,
                    ["definitionCount"] = snapshot.Count,
                    ["sessionId"] = session.SessionId.ToString(),
                    ["factory"] = nameof(DefaultComplexTypeFactory),
                    ["scope"] = "client-side registration; successful roots and dependencies remain registered"
                };
            }, ct);
        }

        /// <summary>
        /// Generates one SDK schema after collecting and validating its complete bounded dependency closure.
        /// </summary>
        [McpServerTool(Name = "schema_generate", ReadOnly = false, Destructive = false, Idempotent = false)]
        [Description("Generates Xsd, Bsd, JsonCompact or JsonVerbose for a Type closure or its Namespace. " +
            "Writes a new root-confined artifact; no overwrite. Unsupported definitions/ranks fail explicitly. " +
            "Returns artifact path, bytes, SHA-256, format, namespace and definition count. " +
            "XML/BSD imports are references, not generated or fetched dependency files.")]
        public Task<CallToolResult> GenerateAsync(
            [Description("Root type and generation settings; e.g. typeId=i=296, format=JsonCompact and scope=Type.")]
            SchemaGenerateInput input,
            [Description(SchemaMcpFilters.OutputPathDescription)] string outputPath,
            [Description(SchemaMcpFilters.SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                Preflight(outputPath);
                if (!Enum.IsDefined(input.Format) || !Enum.IsDefined(input.Scope))
                {
                    throw new ArgumentException("The schema format or scope is not supported.");
                }
                (ISession? session, IServiceMessageContext context) = Source(input.Source, sessionName);
                NodeId id = Parse(input.TypeId, context);
                SchemaDefinitionSnapshot snapshot = Snapshot(context);
                ArrayOf<NodeId> roots = input.Scope == UaSchemaScope.Namespace
                    ? await NamespaceRootsAsync(session, context, id, token).ConfigureAwait(false)
                    : [id];
                await snapshot.LoadAsync(session, roots, token).ConfigureAwait(false);
                snapshot.ValidateFormat(input.Format);
                IUaSchema schema = Provider(snapshot).CreateSchema(Require(snapshot, id), input.Format, input.Scope);
                return await PublishAsync(schema, outputPath, snapshot.Count, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Generates PubSub payload, DataSetMessage, NetworkMessage or metadata-envelope JSON Schema.
        /// </summary>
        [McpServerTool(Name = "schema_generate_pubsub", ReadOnly = false, Destructive = false, Idempotent = false)]
        [Description("Generates an SDK PubSub JSON schema from typed inline fields OR a DataSetMetaData Variable. " +
            "All three numeric masks are explicit; unused masks must be zero. " +
            "Returns new TransferRoot artifact metadata, SHA-256, kind and definition count. " +
            "MetaData describes the SDK metadata-message envelope; its MetaData body remains generic.")]
        public Task<CallToolResult> GeneratePubSubAsync(
            [Description("Inline fields or metadataNodeId, plus kind and three explicit masks; e.g. kind=Payload.")]
            SchemaPubSubInput input,
            [Description(SchemaMcpFilters.OutputPathDescription)] string outputPath,
            [Description(SchemaMcpFilters.SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                Preflight(outputPath);
                ValidateMasks(input);
                if (input.Fields.Count > m_options.MaxFieldsPerType)
                {
                    throw SchemaDefinitionSnapshot.Limit("The DataSet exceeds the host field-count limit.");
                }
                bool remoteMetadata = !string.IsNullOrEmpty(input.MetadataNodeId);
                if (remoteMetadata && (!input.Fields.IsEmpty || input.Source != SchemaTypeSource.Session))
                {
                    throw new ArgumentException("MetadataNodeId requires Source=Session and no inline fields.");
                }
                (ISession? session, IServiceMessageContext context) = Source(input.Source, sessionName);
                DataSetMetaDataType metadata;
                if (remoteMetadata)
                {
                    DataValue value = await McpModelAccess.ReadDataValueAsync(
                        session!, Parse(input.MetadataNodeId!, context), token).ConfigureAwait(false);
                    if (value.IsNotGood)
                    {
                        return new JsonObject
                        {
                            ["error"] = true,
                            ["message"] = "The metadata Variable has non-good quality.",
                            ["dataValue"] = McpCompanionJson.DataValue(value.WithWrappedValue(Variant.Null), context)
                        };
                    }
                    if (!value.WrappedValue.TryGetValue(out ExtensionObject encoded) ||
                        !encoded.TryGetValue(out DataSetMetaDataType? supplied))
                    {
                        throw SchemaDefinitionSnapshot.Unsupported("The Variable does not contain DataSetMetaData.");
                    }
                    metadata = supplied;
                }
                else
                {
                    metadata = new DataSetMetaDataType
                    {
                        Name = input.Name,
                        Fields = input.Fields.ConvertAll(field => new FieldMetaData
                        {
                            Name = field.Name,
                            DataType = Parse(field.DataTypeId, context),
                            ValueRank = field.ValueRank
                        })
                    };
                }
                DataSetMetaDataType normalized = ValidateMetadata(metadata, context);
                SchemaDefinitionSnapshot snapshot = Snapshot(context);
                ArrayOf<NodeId> roots = [.. normalized.Fields.ToList().Select(field => field.DataType)
                    .Where(id => TypeInfo.GetBuiltInType(id) == BuiltInType.Null).Distinct()];
                await snapshot.LoadAsync(session, roots, token).ConfigureAwait(false);
                snapshot.ValidateFormat(input.Verbose ? UaSchemaFormat.JsonVerbose : UaSchemaFormat.JsonCompact);
                foreach (FieldMetaData field in normalized.Fields)
                {
                    if (field.BuiltInType != 0 &&
                        snapshot.Registry.TryResolve(field.DataType, out UaTypeDescription? description) &&
                        (description.Definition is StructureDefinition || description.IsStructureOptionSet) &&
                        field.BuiltInType != (byte)BuiltInType.ExtensionObject)
                    {
                        throw SchemaDefinitionSnapshot.Unsupported(
                            "A metadata BuiltInType contradicts its structure definition.");
                    }
                }
                if (roots.ToList().Select(id => Require(snapshot, id).Name)
                    .Distinct(StringComparer.Ordinal).Count() != roots.Count)
                {
                    throw SchemaDefinitionSnapshot.Unsupported(
                        "The PubSub provider cannot distinguish custom field types with the same schema name.");
                }
                if (roots.FindIndex(id => Require(snapshot, id).Name.Any(character =>
                    !char.IsLetterOrDigit(character) && character != '_')) >= 0)
                {
                    throw SchemaDefinitionSnapshot.Unsupported(
                        "PubSub custom type names must be letters, digits or underscores; " +
                        "the SDK normalizes other characters and can alias distinct definitions.");
                }
                var provider = new PubSubSchemaProvider(Provider(snapshot), snapshot.Resolver);
                var fieldMask = (DataSetFieldContentMask)input.FieldContentMask;
                var messageMask = (JsonDataSetMessageContentMask)input.MessageContentMask;
                var networkMask = (JsonNetworkMessageContentMask)input.NetworkContentMask;
                IUaSchema schema = input.Kind switch
                {
                    SchemaPubSubKind.Payload => provider.CreateDataSetSchema(normalized, fieldMask, input.Verbose),
                    SchemaPubSubKind.DataSetMessage =>
                        provider.CreateDataSetMessageSchema(normalized, messageMask, fieldMask, input.Verbose),
                    SchemaPubSubKind.NetworkMessage =>
                        provider.CreateNetworkMessageSchema(normalized, networkMask, messageMask, fieldMask,
                            input.Verbose),
                    SchemaPubSubKind.MetaData => provider.CreateMetaDataMessageSchema(normalized, input.Verbose),
                    _ => throw new ArgumentOutOfRangeException(nameof(input))
                };
                JsonObject result = await PublishAsync(schema, outputPath, snapshot.Count, token).ConfigureAwait(false);
                result["kind"] = input.Kind.ToString();
                result["metadataBodySpecialized"] = false;
                return result;
            }, ct);
        }

        /// <summary>
        /// Publishes the existing REST generator's OpenAPI document without establishing a UA session.
        /// </summary>
        [McpServerTool(Name = "schema_generate_openapi", ReadOnly = false, Destructive = false, Idempotent = false,
            OpenWorld = false)]
        [Description("Generates the existing OPC UA REST OpenAPI document for AllServices or Sessionless. " +
            "Optional component schemas use a prevalidated bounded standard-type closure. " +
            "serverUrl is document metadata only; no network access or UA connection. Never overwrites files. " +
            "Returns artifact path, bytes, SHA-256, service set and route count.")]
        public Task<CallToolResult> GenerateOpenApiAsync(
            [Description("REST document settings; e.g. serviceSet=Sessionless and includeSchemas=true.")]
            SchemaOpenApiInput input,
            [Description(SchemaMcpFilters.OutputPathDescription)] string outputPath,
            CancellationToken ct = default)
        {
            return ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                Preflight(outputPath);
                ArrayOf<WebApiServiceRoute> routes = WebApiServiceRoutes.GetRoutes(input.ServiceSet);
                SchemaDefinitionSnapshot snapshot = Snapshot(m_local);
                if (input.IncludeSchemas)
                {
                    var messageTypes = new HashSet<Type>();
                    foreach (WebApiServiceRoute route in routes)
                    {
                        messageTypes.Add(route.RequestType);
                        messageTypes.Add(route.ResponseType);
                    }
                    var roots = new HashSet<NodeId>();
                    int scanned = 0;
                    foreach (ExpandedNodeId candidate in m_local.Factory.KnownTypeIds)
                    {
                        token.ThrowIfCancellationRequested();
                        if (++scanned > 4096)
                        {
                            throw SchemaDefinitionSnapshot.Limit("The OpenAPI type identifier scan is too large.");
                        }
                        if (m_local.Factory.TryGetEncodeableType(candidate, out IEncodeableType? activator) &&
                            messageTypes.Remove(activator.Type))
                        {
                            roots.Add(SchemaTypeDiscovery.LocalId(
                                activator.CreateInstance().TypeId, m_local.NamespaceUris));
                            if (messageTypes.Count == 0)
                            {
                                break;
                            }
                        }
                    }
                    if (messageTypes.Count != 0)
                    {
                        throw SchemaDefinitionSnapshot.Unsupported(
                            "Registered REST request/response types are missing.");
                    }
                    await snapshot.LoadAsync(null, [.. roots], token).ConfigureAwait(false);
                }
                var generator = new WebApiOpenApiGenerator(snapshot.Resolver);
                JsonObject document = generator.Generate(input.ServiceSet, input.IncludeSchemas, input.ServerUrl);
                using var buffer = new McpBoundedMemoryStream(ByteLimit(), token);
                using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
                {
                    document.WriteTo(writer);
                }
                JsonObject artifact = await PublishBufferAsync(buffer, outputPath, token).ConfigureAwait(false);
                artifact["format"] = "OpenApi";
                artifact["mediaType"] = "application/json";
                artifact["definitionCount"] = snapshot.Count;
                artifact["serviceSet"] = input.ServiceSet.ToString();
                artifact["routeCount"] = routes.Count;
                return artifact;
            }, ct);
        }

        /// <summary>
        /// Applies the host deadline without turning caller cancellation into a misleading tool failure.
        /// </summary>
        private Task<CallToolResult> ExecuteAsync(
            Func<CancellationToken, ValueTask<JsonNode?>> operation, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async callerToken =>
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
                deadline.CancelAfter(m_options.OperationTimeout);
                try
                {
                    return await operation(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!callerToken.IsCancellationRequested &&
                    deadline.IsCancellationRequested)
                {
                    throw new TimeoutException("The schema operation exceeded the host deadline.");
                }
            }, ct);
        }

        /// <summary>
        /// Selects the current session once per operation, never reusing a proxy across session replacement.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentException"></exception>
        private (ISession? Session, IServiceMessageContext Context) Source(
            SchemaTypeSource source, string? sessionName)
        {
            if (source == SchemaTypeSource.Session)
            {
                ISession session = m_sessions.GetSessionOrThrow(sessionName);
                return (session, session.MessageContext);
            }
            if (source != SchemaTypeSource.Registered)
            {
                throw new ArgumentOutOfRangeException(nameof(source));
            }
            if (sessionName is not null)
            {
                throw new ArgumentException("A session name is only valid with Source=Session.");
            }
            return (null, m_local);
        }

        /// <summary>
        /// Resolves user identifiers without creating namespace mappings or loading code.
        /// </summary>
        private static NodeId Parse(string text, IServiceMessageContext context)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);
            return SchemaTypeDiscovery.LocalId(ExpandedNodeId.Parse(text), context.NamespaceUris);
        }

        /// <summary>
        /// Creates a finite registry with the host resolver used only while collecting definitions.
        /// </summary>
        private SchemaDefinitionSnapshot Snapshot(IServiceMessageContext context)
        {
            return new SchemaDefinitionSnapshot(context, m_options, m_registered);
        }

        /// <summary>
        /// Dispatches the shipped or explicitly host-injected SDK generators over only the bounded registry.
        /// </summary>
        private DefaultSchemaProvider Provider(SchemaDefinitionSnapshot snapshot)
        {
            return new DefaultSchemaProvider(snapshot.Resolver, m_generators.ToList());
        }

        /// <summary>
        /// Requires the selected root to have an actual supported definition.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static UaTypeDescription Require(SchemaDefinitionSnapshot snapshot, NodeId id)
        {
            return snapshot.Registry.TryResolve(id, out UaTypeDescription? type)
                ? type
                : throw SchemaDefinitionSnapshot.Unsupported("The selected root has no usable schema definition.");
        }

        /// <summary>
        /// Collects a complete bounded namespace before any provider begins namespace expansion.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<ArrayOf<NodeId>> NamespaceRootsAsync(
            ISession? session, IServiceMessageContext context, NodeId selected, CancellationToken ct)
        {
            string uri = context.NamespaceUris.GetString(selected.NamespaceIndex)!;
            var ids = new HashSet<NodeId> { selected };
            if (session is null)
            {
                foreach (UaTypeDescription type in SchemaTypeDiscovery.Registered(
                    context, m_registered, uri, m_options, ct))
                {
                    ids.Add(SchemaTypeDiscovery.LocalId(type.TypeId, context.NamespaceUris));
                    if (ids.Count > m_options.MaxTypes)
                    {
                        throw SchemaDefinitionSnapshot.Limit("The namespace exceeds the host type limit.");
                    }
                }
            }
            else
            {
                await foreach (ReferenceDescription reference in SchemaTypeDiscovery.RemoteAsync(
                    session, DataTypeIds.BaseDataType, uri, m_options, ct).ConfigureAwait(false))
                {
                    ids.Add(McpModelAccess.ModelNode(session, reference.NodeId));
                    if (ids.Count > m_options.MaxTypes)
                    {
                        throw SchemaDefinitionSnapshot.Limit("The namespace exceeds the host type limit.");
                    }
                }
            }
            return [.. ids];
        }

        /// <summary>
        /// Creates a qualified identity without treating a BrowseName namespace as a DataType namespace.
        /// </summary>
        private static JsonObject TypeIdentity(UaTypeDescription type)
        {
            return new JsonObject
            {
                ["typeId"] = type.TypeId.ToString(),
                ["namespaceUri"] = type.NamespaceUri,
                ["browseName"] = type.BrowseName.ToString(),
                ["name"] = type.Name,
                ["definitionKind"] = type.Definition switch
                {
                    EnumDefinition => "Enumeration",
                    StructureDefinition => "Structure",
                    _ => "Unsupported"
                }
            };
        }

        /// <summary>
        /// Validates the destination and both host budgets before any remote reads or expansion.
        /// </summary>
        private void Preflight(string outputPath)
        {
            m_files.ValidateDownloadPath(outputPath);
            _ = ByteLimit();
        }

        /// <summary>
        /// Captures the stricter host limit for in-memory serialization.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private long ByteLimit()
        {
            if (m_shared.MaxTransferBytes <= 0)
            {
                throw new InvalidOperationException("MaxTransferBytes must be positive.");
            }
            return Math.Min(m_options.MaxSchemaBytes, m_shared.MaxTransferBytes);
        }

        /// <summary>
        /// Serializes synchronously into a bounded buffer, then publishes asynchronously and atomically.
        /// </summary>
        private async ValueTask<JsonObject> PublishAsync(
            IUaSchema schema, string path, int count, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using var buffer = new McpBoundedMemoryStream(ByteLimit(), ct);
            schema.WriteTo(buffer);
            JsonObject result = await PublishBufferAsync(buffer, path, ct).ConfigureAwait(false);
            result["format"] = schema.Format.ToString();
            result["mediaType"] = schema.MediaType;
            result["targetNamespace"] = schema.TargetNamespace;
            result["definitionCount"] = count;
            result["dependencyArtifactsGenerated"] = false;
            return result;
        }

        /// <summary>
        /// Computes the digest before publication and leaves no partial artifact on cancellation or failure.
        /// </summary>
        private async ValueTask<JsonObject> PublishBufferAsync(
            McpBoundedMemoryStream buffer, string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string digest = Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
            buffer.Position = 0;
            JsonObject result = await m_files.DownloadAsync(
                path, (destination, token) => new ValueTask(buffer.CopyToAsync(destination, token)), ct)
                .ConfigureAwait(false);
            result["sha256"] = digest;
            return result;
        }

        /// <summary>
        /// Validates field shapes before the PubSub provider can silently fall back to unconstrained schemas.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private DataSetMetaDataType ValidateMetadata(
            DataSetMetaDataType metadata, IServiceMessageContext context)
        {
            if (metadata.Fields.Count > m_options.MaxFieldsPerType)
            {
                throw SchemaDefinitionSnapshot.Limit("The DataSet exceeds the host field-count limit.");
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            var fields = new List<FieldMetaData>();
            foreach (FieldMetaData field in metadata.Fields)
            {
                if (field is null ||
                    string.IsNullOrWhiteSpace(field.Name) ||
                    !names.Add(field.Name) ||
                    field.DataType.IsNull)
                {
                    throw SchemaDefinitionSnapshot.Unsupported(
                        "PubSub fields require unique names and DataType ids.");
                }
                SchemaDefinitionSnapshot.ValidateRank(field.ValueRank, m_options);
                if (field.DataType.NamespaceIndex != 0 &&
                    !metadata.Namespaces.IsEmpty &&
                    (field.DataType.NamespaceIndex >= metadata.Namespaces.Count ||
                        !string.Equals(metadata.Namespaces[field.DataType.NamespaceIndex],
                            context.NamespaceUris.GetString(field.DataType.NamespaceIndex), StringComparison.Ordinal)))
                {
                    throw SchemaDefinitionSnapshot.Unsupported(
                        "The metadata namespace table differs from the selected context. " +
                        "Use explicit inline fields with context-local DataType identifiers.");
                }
                if (field.ValueRank is ValueRanks.Any or ValueRanks.OneOrMoreDimensions)
                {
                    throw SchemaDefinitionSnapshot.Unsupported(
                        "The PubSub provider cannot fully describe an unconstrained-dimensional field.");
                }
                BuiltInType builtin = TypeInfo.GetBuiltInType(field.DataType);
                if (!Enum.IsDefined((BuiltInType)field.BuiltInType) ||
                    (builtin != BuiltInType.Null && field.BuiltInType != 0 && field.BuiltInType != (byte)builtin))
                {
                    throw SchemaDefinitionSnapshot.Unsupported("A metadata BuiltInType contradicts its DataType.");
                }
                fields.Add(new FieldMetaData
                {
                    Name = field.Name,
                    DataType = field.DataType,
                    ValueRank = field.ValueRank,
                    BuiltInType = field.BuiltInType == 0 ? (byte)builtin : field.BuiltInType
                });
            }
            return new DataSetMetaDataType { Name = metadata.Name, Fields = [.. fields] };
        }

        /// <summary>
        /// Accepts only mask bits the existing provider actually implements, with explicit unused-mask handling.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="ArgumentException"></exception>
        private static void ValidateMasks(SchemaPubSubInput input)
        {
            const uint fields = (uint)(DataSetFieldContentMask.RawData |
                DataSetFieldContentMask.StatusCode |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.ServerPicoSeconds);
            const uint messages = (uint)(JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.DataSetWriterName |
                JsonDataSetMessageContentMask.PublisherId |
                JsonDataSetMessageContentMask.WriterGroupName |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status |
                JsonDataSetMessageContentMask.MinorVersion);
            const uint networks = (uint)(JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.WriterGroupName |
                JsonNetworkMessageContentMask.DataSetClassId |
                JsonNetworkMessageContentMask.ReplyTo |
                JsonNetworkMessageContentMask.SingleDataSetMessage);
            if (!Enum.IsDefined(input.Kind) ||
                (input.FieldContentMask & ~fields) != 0 ||
                (input.MessageContentMask & ~messages) != 0 ||
                (input.NetworkContentMask & ~networks) != 0)
            {
                throw SchemaDefinitionSnapshot.Unsupported("The schema kind or content-mask bits are unsupported.");
            }
            if ((input.FieldContentMask & (uint)DataSetFieldContentMask.RawData) != 0 &&
                input.FieldContentMask != (uint)DataSetFieldContentMask.RawData)
            {
                throw new ArgumentException("RawData cannot be combined with DataValue quality/timestamp mask bits.");
            }
            if ((input.Kind is SchemaPubSubKind.Payload or SchemaPubSubKind.MetaData &&
                input.MessageContentMask != 0) ||
                (input.Kind != SchemaPubSubKind.NetworkMessage && input.NetworkContentMask != 0) ||
                (input.Kind == SchemaPubSubKind.MetaData && input.FieldContentMask != 0))
            {
                throw new ArgumentException("Masks unused by the selected schema kind must be explicitly zero.");
            }
        }

        /// <summary>Current-session lookup; no session is cached in these tools.</summary>
        private readonly OpcUaSessionManager m_sessions;

        /// <summary>Shared root-confined atomic file publication.</summary>
        private readonly McpFileTransfers m_files;

        /// <summary>The host's transfer policy, checked again at publication.</summary>
        private readonly OpcUaMcpOptions m_shared;

        /// <summary>Validated schema resource ceilings.</summary>
        private readonly McpSchemaOptions m_options;

        /// <summary>Local generated types and their namespace context.</summary>
        private readonly IServiceMessageContext m_local;

        /// <summary>Optional explicitly registered local definitions.</summary>
        private readonly IDataTypeDefinitionResolver? m_registered;

        /// <summary>Existing SDK generators, or explicit host replacements.</summary>
        private readonly ArrayOf<IUaSchemaGenerator> m_generators;
    }
}
