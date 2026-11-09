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

#if NET10_0
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Schema;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Owns local schema definitions, root-confined artifacts and the real SDK binder without opening a connection.
    /// </summary>
    internal sealed class SchemaTestContext : IAsyncDisposable
    {
        /// <summary>
        /// Creates a finite, injectable schema host beneath the test workspace.
        /// </summary>
        internal SchemaTestContext(McpSchemaOptions? options = null)
        {
            Root = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"schema-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Shared = new OpcUaMcpOptions { TransferRoot = Root, MaxTransferBytes = 16 * 1024 * 1024 };
            Options = options ?? new McpSchemaOptions();
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore(Shared).AddOpcUaMcpSchema(Options);
            services.AddSingleton<IServiceMessageContext>(provider =>
                ServiceMessageContext.Create(provider.GetRequiredService<OpcUaSessionManager>().Telemetry));
            services.AddMcpServer().WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaSchemaTools(McpToolProfile.Schemas);
            Services = services.BuildServiceProvider();
            Context = Services.GetRequiredService<IServiceMessageContext>();
            TypeNamespace = Context.NamespaceUris.GetIndexOrAppend(TypeNamespaceUri);
            NameNamespace = Context.NamespaceUris.GetIndexOrAppend("urn:test:schema:names");
            Registry = Services.GetRequiredService<DataTypeDefinitionRegistry>();
            Tools = Services.GetRequiredService<SchemaTools>();
        }

        /// <summary>
        /// Gets the test-owned artifact root.
        /// </summary>
        internal string Root { get; }

        /// <summary>
        /// Gets the real dependency-injection provider.
        /// </summary>
        internal ServiceProvider Services { get; }

        /// <summary>
        /// Gets the shared transfer policy.
        /// </summary>
        internal OpcUaMcpOptions Shared { get; }

        /// <summary>
        /// Gets the host schema policy.
        /// </summary>
        internal McpSchemaOptions Options { get; }

        /// <summary>
        /// Gets the host's local namespace and generated-type context.
        /// </summary>
        internal IServiceMessageContext Context { get; }

        /// <summary>
        /// Gets the actual DataType identifier namespace.
        /// </summary>
        internal ushort TypeNamespace { get; }

        /// <summary>
        /// Gets a deliberately different BrowseName namespace.
        /// </summary>
        internal ushort NameNamespace { get; }

        /// <summary>
        /// Gets the registered definition seam.
        /// </summary>
        internal DataTypeDefinitionRegistry Registry { get; }

        /// <summary>
        /// Gets the real tool implementation.
        /// </summary>
        internal SchemaTools Tools { get; }

        /// <summary>
        /// Registers one explicit local definition without reflecting over CLR types.
        /// </summary>
        internal NodeId Register(string name, DataTypeDefinition definition, bool structureOptionSet = false)
        {
            NodeId id = Id(name);
            Registry.Add(new UaTypeDescription(id, new QualifiedName(name, NameNamespace), definition,
                TypeNamespaceUri, structureOptionSet));
            return id;
        }

        /// <summary>
        /// Creates a local identifier in the test model's DataType namespace.
        /// </summary>
        internal NodeId Id(string name)
        {
            return new NodeId(name, TypeNamespace);
        }

        /// <summary>
        /// Creates a scalar or array structure field with an explicit declared type.
        /// </summary>
        internal static StructureField Field(string name, NodeId id, int rank = ValueRanks.Scalar)
        {
            return new StructureField { Name = name, DataType = id, ValueRank = rank };
        }

        /// <summary>
        /// Creates a complete structure definition, including a non-null empty fields array.
        /// </summary>
        internal static StructureDefinition Structure(params StructureField[] fields)
        {
            return new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields = fields.ToArrayOf()
            };
        }

        /// <summary>
        /// Creates the finite preflight snapshot directly for exact boundary tests.
        /// </summary>
        internal SchemaDefinitionSnapshot Snapshot()
        {
            return new SchemaDefinitionSnapshot(Context, Options, Registry);
        }

        /// <summary>
        /// Reads matching structured and textual tool results with a meaningful error assertion.
        /// </summary>
        internal static JsonObject Payload(CallToolResult result, bool error = false)
        {
            Assert.That(result.IsError, Is.EqualTo(error), result.StructuredContent?.GetRawText());
            Assert.That(result.StructuredContent.HasValue, Is.True);
            string structured = result.StructuredContent!.Value.GetRawText();
            Assert.That(result.Content.OfType<TextContentBlock>().Single().Text, Is.EqualTo(structured));
            return JsonNode.Parse(structured)!.AsObject();
        }

        /// <summary>
        /// Invokes the actual SDK-bound shallow input contract.
        /// </summary>
        internal async Task<CallToolResult> InvokeAsync(string name, JsonObject arguments)
        {
            McpServerTool tool = Services.GetServices<McpServerTool>()
                .Single(candidate => candidate.ProtocolTool.Name == name);
            using var document = JsonDocument.Parse(arguments.ToJsonString());
            var original = document.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.Clone());
            var parameters = new CallToolRequestParams { Name = name, Arguments = original };
            var request = new RequestContext<CallToolRequestParams>(
                Services.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/call", Id = new RequestId("schema-test") }, parameters)
            {
                MatchedPrimitive = tool
            };
            CallToolResult result = await tool.InvokeAsync(request).ConfigureAwait(false);
            Assert.That(parameters.Arguments, Is.SameAs(original),
                "The input adapter must restore the caller's request.");
            Assert.That(original.ContainsKey("input"), Is.False);
            return result;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync().ConfigureAwait(false);
            Directory.Delete(Root, recursive: true);
        }

        /// <summary>The identifier namespace, deliberately distinct from the BrowseName namespace.</summary>
        internal const string TypeNamespaceUri = "urn:test:schema:types";
    }
}
#endif
