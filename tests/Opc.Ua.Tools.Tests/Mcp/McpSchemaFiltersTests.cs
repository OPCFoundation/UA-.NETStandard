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
