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
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Pcap.Capture;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    public sealed class PcapMcpFiltersTests
    {
        [Test]
        public void SurfaceDiagnosticsErrorsWithNullNextThrows()
        {
            Assert.That(
                () => PcapMcpFilters.SurfaceDiagnosticsErrors(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task SurfaceDiagnosticsErrorsWithSuccessfulNextReturnsItsResultAsync()
        {
            var expected = new CallToolResult { IsError = false };
            int callCount = 0;
            using var cancellation = new CancellationTokenSource();
            McpRequestHandler<CallToolRequestParams, CallToolResult> filter =
                PcapMcpFilters.SurfaceDiagnosticsErrors((request, cancellationToken) =>
                {
                    callCount++;
                    Assert.That(request, Is.Null);
                    Assert.That(cancellationToken, Is.EqualTo(cancellation.Token));
                    return ValueTask.FromResult(expected);
                });

            CallToolResult result = await filter(null!, cancellation.Token).ConfigureAwait(false);

            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(result, Is.SameAs(expected));
            Assert.That(result.IsError, Is.False);
        }

        [Test]
        public async Task SurfaceDiagnosticsErrorsWithDiagnosticsExceptionReturnsActionableErrorAsync()
        {
            const string expectedMessage = "Capture source 'missing' was not found.";
            int callCount = 0;
            McpRequestHandler<CallToolRequestParams, CallToolResult> filter =
                PcapMcpFilters.SurfaceDiagnosticsErrors((_, _) =>
                {
                    callCount++;
                    return ValueTask.FromException<CallToolResult>(
                        new PcapDiagnosticsException(expectedMessage));
                });

            CallToolResult result = await filter(null!, CancellationToken.None).ConfigureAwait(false);

            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content, Has.Count.EqualTo(1));
            Assert.That(result.Content[0], Is.TypeOf<TextContentBlock>());
            Assert.That(((TextContentBlock)result.Content[0]).Text, Is.EqualTo(expectedMessage));
        }

        [Test]
        public void AddCanonicalEnumSchemasWithNullNextThrows()
        {
            Assert.That(
                () => PcapMcpFilters.AddCanonicalEnumSchemas(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task AddCanonicalEnumSchemasWithKnownToolsAddsExactConstraintsAsync()
        {
            Tool startCapture = CreateTool(
                "start_capture",
                """
                {
                  "type": "object",
                  "properties": {
                    "request": {
                      "properties": {
                        "source": {
                          "description": "preserved",
                          "type": "integer",
                          "default": 17
                        }
                      }
                    }
                  }
                }
                """);
            Tool captureNow = CreateTool(
                "capture_now",
                """
                {
                  "type": "object",
                  "properties": {
                    "request": {
                      "properties": {
                        "start": {
                          "properties": {
                            "source": { "description": "preserved" }
                          }
                        },
                        "format": { "description": "preserved" }
                      }
                    }
                  }
                }
                """);
            Tool captureNowWithOnlySource = CreateTool(
                "capture_now",
                """
                {
                  "type": "object",
                  "properties": {
                    "request": {
                      "properties": {
                        "start": {
                          "properties": {
                            "source": { "description": "preserved" }
                          }
                        }
                      }
                    }
                  }
                }
                """);
            Tool captureNowWithOnlyFormat = CreateTool(
                "capture_now",
                """
                {
                  "type": "object",
                  "properties": {
                    "request": {
                      "properties": {
                        "format": { "description": "preserved" }
                      }
                    }
                  }
                }
                """);
            var expected = new ListToolsResult
            {
                Tools =
                [
                    startCapture,
                    captureNow,
                    captureNowWithOnlySource,
                    captureNowWithOnlyFormat
                ]
            };
            bool nextCalled = false;
            using var cancellation = new CancellationTokenSource();
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> filter =
                PcapMcpFilters.AddCanonicalEnumSchemas((request, cancellationToken) =>
                {
                    nextCalled = true;
                    Assert.That(request, Is.Null);
                    Assert.That(cancellationToken, Is.EqualTo(cancellation.Token));
                    return ValueTask.FromResult(expected);
                });

            ListToolsResult result = await filter(null!, cancellation.Token).ConfigureAwait(false);

            Assert.That(nextCalled, Is.True);
            Assert.That(result, Is.SameAs(expected));
            AssertStringEnum(
                GetProperty(startCapture.InputSchema, "properties", "request", "properties", "source"),
                ["nic", "inproc-client", "inproc-server", "replay"],
                "inproc-client");
            AssertStringEnum(
                GetProperty(
                    captureNow.InputSchema,
                    "properties",
                    "request",
                    "properties",
                    "start",
                    "properties",
                    "source"),
                ["nic", "inproc-client", "inproc-server", "replay"],
                "inproc-client");
            AssertStringEnum(
                GetProperty(captureNow.InputSchema, "properties", "request", "properties", "format"),
                ["pcap", "pcapng", "json", "csv", "text", "service-timeline"],
                "service-timeline");
            AssertStringEnum(
                GetProperty(
                    captureNowWithOnlySource.InputSchema,
                    "properties",
                    "request",
                    "properties",
                    "start",
                    "properties",
                    "source"),
                ["nic", "inproc-client", "inproc-server", "replay"],
                "inproc-client");
            AssertStringEnum(
                GetProperty(
                    captureNowWithOnlyFormat.InputSchema,
                    "properties",
                    "request",
                    "properties",
                    "format"),
                ["pcap", "pcapng", "json", "csv", "text", "service-timeline"],
                "service-timeline");
        }

        [Test]
        public async Task AddCanonicalEnumSchemasWithUnsupportedSchemasLeavesThemUnchangedAsync()
        {
            Tool unknownTool = CreateTool(
                "other_tool",
                """{"type":"object","marker":"unknown"}""");
            Tool missingPath = CreateTool(
                "start_capture",
                """{"type":"object","properties":{}}""");
            Tool scalarProperty = CreateTool(
                "start_capture",
                """
                {
                  "type": "object",
                  "properties": {
                    "request": {
                      "properties": {
                        "source": false
                      }
                    }
                  }
                }
                """);
            Tool captureNowWithoutEnums = CreateTool(
                "capture_now",
                """
                {
                  "type": "object",
                  "properties": {
                    "request": {
                      "properties": {}
                    }
                  }
                }
                """);
            Tool[] tools = [unknownTool, missingPath, scalarProperty, captureNowWithoutEnums];
            string[] originalSchemas = tools.Select(tool => tool.InputSchema.GetRawText()).ToArray();
            var expected = new ListToolsResult { Tools = tools };
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> filter =
                PcapMcpFilters.AddCanonicalEnumSchemas((_, _) => ValueTask.FromResult(expected));

            ListToolsResult result = await filter(null!, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.SameAs(expected));
            Assert.That(
                tools.Select(tool => tool.InputSchema.GetRawText()),
                Is.EqualTo(originalSchemas));
        }

        private static Tool CreateTool(string name, string inputSchema)
        {
            return new Tool
            {
                Name = name,
                InputSchema = JsonSerializer.Deserialize<JsonElement>(inputSchema)
            };
        }

        private static JsonElement GetProperty(JsonElement schema, params string[] path)
        {
            foreach (string segment in path)
            {
                schema = schema.GetProperty(segment);
            }

            return schema;
        }

        private static void AssertStringEnum(
            JsonElement property,
            IReadOnlyList<string> expectedValues,
            string expectedDefault)
        {
            Assert.That(property.GetProperty("type").GetString(), Is.EqualTo("string"));
            Assert.That(
                property.GetProperty("enum").EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo(expectedValues));
            Assert.That(property.GetProperty("default").GetString(), Is.EqualTo(expectedDefault));
            Assert.That(property.GetProperty("description").GetString(), Is.EqualTo("preserved"));
        }
    }

    [TestFixture]
    public sealed class PubSubPcapMcpFiltersTests
    {
        [Test]
        public void SurfaceDiagnosticsErrorsWithNullNextThrows()
        {
            Assert.That(
                () => PubSubPcapMcpFilters.SurfaceDiagnosticsErrors(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task SurfaceDiagnosticsErrorsWithSuccessfulNextReturnsItsResultAsync()
        {
            var expected = new CallToolResult { IsError = false };
            int callCount = 0;
            using var cancellation = new CancellationTokenSource();
            McpRequestHandler<CallToolRequestParams, CallToolResult> filter =
                PubSubPcapMcpFilters.SurfaceDiagnosticsErrors((request, cancellationToken) =>
                {
                    callCount++;
                    Assert.That(request, Is.Null);
                    Assert.That(cancellationToken, Is.EqualTo(cancellation.Token));
                    return ValueTask.FromResult(expected);
                });

            CallToolResult result = await filter(null!, cancellation.Token).ConfigureAwait(false);

            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(result, Is.SameAs(expected));
            Assert.That(result.IsError, Is.False);
        }

        [Test]
        public async Task SurfaceDiagnosticsErrorsWithDiagnosticsExceptionReturnsActionableErrorAsync()
        {
            const string expectedMessage = "PubSub capture source 'missing' was not found.";
            int callCount = 0;
            McpRequestHandler<CallToolRequestParams, CallToolResult> filter =
                PubSubPcapMcpFilters.SurfaceDiagnosticsErrors((_, _) =>
                {
                    callCount++;
                    return ValueTask.FromException<CallToolResult>(
                        new PcapDiagnosticsException(expectedMessage));
                });

            CallToolResult result = await filter(null!, CancellationToken.None).ConfigureAwait(false);

            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content, Has.Count.EqualTo(1));
            Assert.That(result.Content[0], Is.TypeOf<TextContentBlock>());
            Assert.That(((TextContentBlock)result.Content[0]).Text, Is.EqualTo(expectedMessage));
        }
    }
}
#endif
