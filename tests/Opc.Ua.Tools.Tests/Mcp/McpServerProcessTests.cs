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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    [NonParallelizable]
    public sealed class McpServerProcessTests
    {
        [Test]
        public async Task HelpCommandStartsAndExitsSuccessfullyAsync()
        {
            using Process process = StartMcpProcess("--help");
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            string output = await standardOutput.ConfigureAwait(false);
            string error = await standardError.ConfigureAwait(false);

            Assert.That(process.ExitCode, Is.Zero);
            Assert.That(error, Does.Contain("OPC UA MCP Server"));
            Assert.That(output + error, Does.Contain("--transport"));
            Assert.That(output + error, Does.Contain("--profile"));
        }

        [TestCase("http")]
        [TestCase("sse")]
        public async Task HttpTransportAliasesExposeOnlyMcpRouteAsync(string transport)
        {
            int port = GetUnusedPort();
            using Process process = StartMcpProcess(
                "--transport",
                transport,
                "--port",
                port.ToString(CultureInfo.InvariantCulture),
                "--profile",
                "core");
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var client = new HttpClient();
            HttpResponseMessage? mcpResponse = null;

            try
            {
                while (!process.HasExited && !timeout.IsCancellationRequested)
                {
                    try
                    {
                        using HttpRequestMessage request = CreateInitializeRequest(
                            $"http://127.0.0.1:{port}/mcp");
                        mcpResponse = await client.SendAsync(
                            request,
                            timeout.Token).ConfigureAwait(false);
                        break;
                    }
                    catch (HttpRequestException)
                    {
                        await Task.Delay(100, timeout.Token).ConfigureAwait(false);
                    }
                }

                Assert.That(
                    mcpResponse,
                    Is.Not.Null,
                    "Streamable HTTP server did not become ready.");
                using HttpResponseMessage initializedResponse = mcpResponse!;
                Assert.That(initializedResponse.IsSuccessStatusCode, Is.True);

                using HttpRequestMessage rootRequest = CreateInitializeRequest(
                    $"http://127.0.0.1:{port}/");
                using HttpResponseMessage rootResponse = await client.SendAsync(
                    rootRequest,
                    timeout.Token).ConfigureAwait(false);
                Assert.That(rootResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            string error = await standardError.ConfigureAwait(false);
            Assert.That(
                error,
                Does.Contain("Starting MCP server with Streamable HTTP transport"));
        }

        [Test]
        public async Task StdioCommandStartsWithDiagnosticsEnabledAsync()
        {
            using Process process = StartMcpProcess(
                "--transport",
                "stdio",
                enableDiagnostics: true);
            Task<string> standardError = process.StandardError.ReadToEndAsync();

            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            bool running = !process.HasExited;
            if (running)
            {
                process.Kill(true);
            }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

            string error = await standardError.ConfigureAwait(false);
            Assert.That(
                running,
                Is.True,
                $"MCP server exited with code {process.ExitCode}:{Environment.NewLine}{error}");
            Assert.That(error, Does.Contain("Starting MCP server with stdio transport"));
        }

        [Test]
        public async Task StdioProtocolProvidesActionableErrorsAndExplicitSchemasAsync()
        {
            using Process process = StartMcpProcess("--profile", "full");
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                using JsonDocument initialize = await SendRequestAsync(
                    process,
                    """
                    {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"Opc.Ua.Tools.Tests","version":"1.0"}}}
                    """,
                    1,
                    timeout.Token).ConfigureAwait(false);
                Assert.That(initialize.RootElement.TryGetProperty("result", out _), Is.True);

                await process.StandardInput.WriteLineAsync(
                    """
                    {"jsonrpc":"2.0","method":"notifications/initialized"}
                    """).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);

                using JsonDocument listTools = await SendRequestAsync(
                    process,
                    """
                    {"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
                    """,
                    2,
                    timeout.Token).ConfigureAwait(false);
                JsonElement tools = listTools.RootElement
                    .GetProperty("result")
                    .GetProperty("tools");
                Assert.That(tools.GetArrayLength(), Is.GreaterThan(25));
                foreach (JsonElement tool in tools.EnumerateArray())
                {
                    JsonElement schema = tool.GetProperty("inputSchema");
                    Assert.That(
                        schema.TryGetProperty("required", out _),
                        Is.True,
                        $"Tool {tool.GetProperty("name").GetString()} omits required.");
                }

                string[] toolNames = ["Connect", "ModifySubscription", "HistoryUpdate", "ExportNodeSet"];
                string[] requiredArguments = ["endpointUrl", "subscriptionId", "nodeId", "filePath"];
                for (int index = 0; index < toolNames.Length; index++)
                {
                    int requestId = index + 3;
                    string request = string.Format(
                        CultureInfo.InvariantCulture,
                        "{{\"jsonrpc\":\"2.0\",\"id\":{0},\"method\":\"tools/call\"," +
                        "\"params\":{{\"name\":\"{1}\",\"arguments\":{{}}}}}}",
                        requestId,
                        toolNames[index]);
                    using JsonDocument response = await SendRequestAsync(
                        process,
                        request,
                        requestId,
                        timeout.Token).ConfigureAwait(false);
                    JsonElement result = response.RootElement.GetProperty("result");
                    string errorText = result
                        .GetProperty("content")[0]
                        .GetProperty("text")
                        .GetString()!;

                    Assert.That(result.GetProperty("isError").GetBoolean(), Is.True);
                    Assert.That(errorText, Does.Contain(toolNames[index]));
                    Assert.That(errorText, Does.Contain(requiredArguments[index]));
                }
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await standardError.ConfigureAwait(false);
            }
        }

        [TestCase("--transport", "unknown")]
        [TestCase("--profile", "unknown")]
        public async Task InvalidOptionValueExitsWithFailureAsync(string option, string value)
        {
            using Process process = StartMcpProcess(option, value);
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            string error = await standardError.ConfigureAwait(false);

            Assert.That(process.ExitCode, Is.Not.Zero);
            Assert.That(error, Does.Contain("unknown").IgnoreCase);
        }

        private static Process StartMcpProcess(params string[] arguments)
        {
            return StartMcpProcess(arguments, false);
        }

        private static Process StartMcpProcess(
            string firstArgument,
            string secondArgument,
            bool enableDiagnostics)
        {
            return StartMcpProcess(
                [firstArgument, secondArgument],
                enableDiagnostics);
        }

        private static Process StartMcpProcess(
            string[] arguments,
            bool enableDiagnostics)
        {
            string assemblyPath = Path.Combine(
                AppContext.BaseDirectory,
                "Opc.Ua.Mcp.dll");
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                    ?? "dotnet",
                WorkingDirectory = AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false
            };
            if (enableDiagnostics)
            {
                startInfo.Environment["OPCUA_PCAP_ENABLE_DIAGNOSTICS"] = "true";
            }
            startInfo.ArgumentList.Add(assemblyPath);
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start the MCP server process.");
        }

        private static HttpRequestMessage CreateInitializeRequest(string requestUri)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(
                    """
                    {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"Opc.Ua.Tools.Tests","version":"1.0"}}}
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            return request;
        }

        private static async Task<JsonDocument> SendRequestAsync(
            Process process,
            string request,
            int requestId,
            CancellationToken ct)
        {
            await process.StandardInput.WriteLineAsync(request).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(ct).ConfigureAwait(false);

            while (true)
            {
                string? response = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                if (response == null)
                {
                    throw new InvalidOperationException(
                        $"MCP server exited before responding to request {requestId}.");
                }

                JsonDocument document = JsonDocument.Parse(response);
                if (document.RootElement.TryGetProperty("id", out JsonElement id) &&
                    id.TryGetInt32(out int responseId) &&
                    responseId == requestId)
                {
                    return document;
                }

                document.Dispose();
            }
        }

        private static int GetUnusedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
#endif
