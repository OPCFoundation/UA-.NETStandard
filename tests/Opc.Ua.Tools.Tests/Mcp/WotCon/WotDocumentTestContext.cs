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
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Owns deterministic file and fake-HTTP resources without a UA session or public-network access.
    /// </summary>
    internal sealed class WotDocumentTestContext : IDisposable
    {
        /// <summary>
        /// Creates a workspace-confined root and optional fake transport.
        /// </summary>
        internal WotDocumentTestContext(
            WotMcpOptions? options = null,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respond = null)
        {
            Root = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"wot-documents-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Shared = new OpcUaMcpOptions { TransferRoot = Root, MaxTransferBytes = 64 * 1024 * 1024 };
            Options = options ?? new WotMcpOptions();
            Clients = new Mock<IHttpClientFactory>(MockBehavior.Strict);
            if (respond is not null)
            {
                m_handler = new WotDocumentHttpHandler(respond);
                Clients.Setup(factory => factory.CreateClient(WotDependencyHttp.ClientName))
                    .Returns(() => new HttpClient(m_handler, disposeHandler: false)
                    {
                        Timeout = Timeout.InfiniteTimeSpan
                    });
            }
            Files = new McpFileTransfers(Shared);
            Tools = new WotDocumentTools(Files, Shared, Options, Clients.Object);
        }

        /// <summary>
        /// Gets the test-owned transfer root.
        /// </summary>
        internal string Root { get; }

        /// <summary>
        /// Gets the explicit shared transfer policy.
        /// </summary>
        internal OpcUaMcpOptions Shared { get; }

        /// <summary>
        /// Gets the host document policy.
        /// </summary>
        internal WotMcpOptions Options { get; }

        /// <summary>
        /// Gets the strictly verified named-client factory.
        /// </summary>
        internal Mock<IHttpClientFactory> Clients { get; }

        /// <summary>
        /// Gets the real root-confined transfer helper.
        /// </summary>
        internal McpFileTransfers Files { get; }

        /// <summary>
        /// Gets the complete tool surface.
        /// </summary>
        internal WotDocumentTools Tools { get; }

        /// <summary>
        /// Creates a scope whose lifetime is owned by the caller.
        /// </summary>
        internal WotDocumentScope Scope(WotDocumentInput input, CancellationToken ct = default)
        {
            return new WotDocumentScope(input, Options, Shared, Files, Clients.Object, ct);
        }

        /// <summary>
        /// Creates explicit inline input without remote opt-in.
        /// </summary>
        internal static WotDocumentInput Input(string text = Model)
        {
            return new WotDocumentInput { Source = new WotDocumentSource { Text = text } };
        }

        /// <summary>
        /// Reads structured output while asserting matching MCP error state.
        /// </summary>
        internal static JsonObject Payload(CallToolResult result, bool error = false)
        {
            Assert.That(result.IsError, Is.EqualTo(error), result.StructuredContent?.GetRawText());
            Assert.That(result.StructuredContent, Is.Not.Null);
            return JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!.AsObject();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Tools.Dispose();
            m_handler?.Dispose();
            Directory.Delete(Root, recursive: true);
        }

        /// <summary>A locally projectable Thing Model with an explicit namespace-qualified identity.</summary>
        internal const string Model = /*lang=json,strict*/ """
            {
              "@context":[
                "https://www.w3.org/2022/wot/td/v1.1",
                {"uav":"http://opcfoundation.org/UA/WoT-Binding/","sample":"urn:test:wot:mcp"}
              ],
              "@type":["tm:ThingModel","uav:objectType"],
              "title":"Pump",
              "uav:browseName":"sample:PumpType",
              "uav:id":"nsu=urn:test:wot:mcp;i=1001",
              "properties":{"speed":{"type":"number","uav:browseName":"sample:Speed"}}
            }
            """;

        /// <summary>A minimal linked NodeSet with independent parent/child document identities.</summary>
        internal const string LinkedNodeSet = """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>urn:test:wot:mcp</Uri></NamespaceUris>
              <Models><Model ModelUri="urn:test:wot:mcp" /></Models>
              <UAObject NodeId="ns=1;s=Pump" BrowseName="1:Pump">
                <DisplayName>Pump</DisplayName>
                <References>
                  <Reference ReferenceType="i=40">i=58</Reference>
                  <Reference ReferenceType="i=47">ns=1;s=Child</Reference>
                </References>
              </UAObject>
              <UAObject NodeId="ns=1;s=Child" BrowseName="1:Child" ParentNodeId="ns=1;s=Pump">
                <DisplayName>Child</DisplayName>
                <References>
                  <Reference ReferenceType="i=40">i=58</Reference>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;s=Pump</Reference>
                  <Reference ReferenceType="i=47">ns=1;s=Speed</Reference>
                </References>
              </UAObject>
              <UAVariable NodeId="ns=1;s=Speed" BrowseName="1:Speed" ParentNodeId="ns=1;s=Child" DataType="i=11">
                <DisplayName>Speed</DisplayName>
                <References>
                  <Reference ReferenceType="i=40">i=68</Reference>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;s=Child</Reference>
                </References>
              </UAVariable>
            </UANodeSet>
            """;

        private readonly WotDocumentHttpHandler? m_handler;
    }

    /// <summary>
    /// Executes only explicitly supplied HTTP responses; it never opens a network connection.
    /// </summary>
    internal sealed class WotDocumentHttpHandler : HttpMessageHandler
    {
        /// <summary>
        /// Captures the deterministic per-request response.
        /// </summary>
        internal WotDocumentHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            m_respond = respond;
        }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return m_respond(request, cancellationToken);
        }

        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> m_respond;
    }

    /// <summary>
    /// Presents a non-seekable HTTP body and records bytes actually read at the budget boundary.
    /// </summary>
    internal sealed class WotChunkedReadStream : MemoryStream
    {
        /// <summary>
        /// Wraps the explicit test payload without reporting its length to HttpContent.
        /// </summary>
        internal WotChunkedReadStream(byte[] content)
            : base(content, writable: false)
        {
        }

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <summary>
        /// Gets the number of bytes consumed by the bounded reader.
        /// </summary>
        internal int BytesRead { get; private set; }

        /// <inheritdoc/>
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            BytesRead += read;
            return read;
        }
    }
}
#endif
