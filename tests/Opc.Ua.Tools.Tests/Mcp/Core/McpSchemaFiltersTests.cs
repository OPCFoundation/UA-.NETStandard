/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

#if NET10_0_OR_GREATER
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    public sealed class McpSchemaFiltersTests
    {
        [Test]
        public void AddExplicitRequiredArraysWithNullNextThrows()
        {
            Assert.That(
                () => McpSchemaFilters.AddExplicitRequiredArrays(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task AddExplicitRequiredArraysWithOptionalAndRequiredToolsNormalizesSchemasAsync()
        {
            Tool optionalTool = CreateTool(
                "optional",
                """
                {
                  "type": "object",
                  "properties": {
                    "value": { "type": "string" }
                  }
                }
                """);
            Tool requiredTool = CreateTool(
                "required",
                """
                {
                  "type": "object",
                  "required": ["value"]
                }
                """);
            var expected = new ListToolsResult
            {
                Tools = [optionalTool, requiredTool]
            };
            bool nextCalled = false;
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> filter =
                McpSchemaFilters.AddExplicitRequiredArrays((_, cancellationToken) =>
                {
                    nextCalled = true;
                    Assert.That(cancellationToken.CanBeCanceled, Is.True);
                    return ValueTask.FromResult(expected);
                });
            using var cancellation = new CancellationTokenSource();

            ListToolsResult result = await filter(null!, cancellation.Token).ConfigureAwait(false);

            Assert.That(nextCalled, Is.True);
            Assert.That(result, Is.SameAs(expected));
            JsonElement optionalRequired = optionalTool.InputSchema.GetProperty("required");
            Assert.That(optionalRequired.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(optionalRequired.GetArrayLength(), Is.Zero);
            JsonElement existingRequired = requiredTool.InputSchema.GetProperty("required");
            Assert.That(existingRequired.GetArrayLength(), Is.EqualTo(1));
            Assert.That(existingRequired[0].GetString(), Is.EqualTo("value"));
        }

        private static Tool CreateTool(string name, string inputSchema)
        {
            return new Tool
            {
                Name = name,
                InputSchema = JsonSerializer.Deserialize<JsonElement>(inputSchema)
            };
        }
    }
}
#endif
