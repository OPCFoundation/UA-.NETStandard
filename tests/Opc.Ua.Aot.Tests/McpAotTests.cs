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

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises <c>Opc.Ua.Mcp.Core</c> in the environment Native AOT produces.
    /// </summary>
    /// <remarks>
    /// Publishing ahead of time turns JSON reflection off, so these tests run with
    /// <c>IsReflectionEnabledByDefault</c> disabled even before the binary is compiled
    /// natively. That is the condition that matters here: marking an assembly
    /// <c>IsAotCompatible</c> only proves its own IL is clean, and says nothing about
    /// whether the library still functions once reflection is unavailable. Tool
    /// discovery under normal (reflection-enabled) hosting is covered by the NUnit
    /// suite in <c>Opc.Ua.Tools.Tests</c>.
    /// </remarks>
    public class McpAotTests
    {
        /// <summary>
        /// Registering the OPC UA tools does not yet work without JSON reflection.
        /// </summary>
        /// <remarks>
        /// The MCP SDK builds every tool's input schema by asking
        /// <c>JsonSchemaExporter</c> for a <c>JsonTypeInfo</c> of each parameter type.
        /// Without a source-generated context covering the tool signatures - and with
        /// the injected <c>OpcUaSessionManager</c> being described rather than treated
        /// as a service - that throws. This test pins the limitation so it is stated
        /// rather than discovered in production, and turns red the moment it is lifted.
        /// </remarks>
        [Test]
        public async Task ToolRegistrationStillNeedsJsonReflectionAsync()
        {
            await Assert.That(JsonReflectionIsEnabled()).IsFalse();

            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddMcpServer()
                .WithOpcUaMcpFilters()
                .WithOpcUaCoreTools(McpToolProfile.Full);

            await Assert.That(() => ResolveTools(services)).Throws<NotSupportedException>();
        }

        /// <summary>
        /// Every tool renders its result through this helper, so it has to work without
        /// reflection. It writes JSON directly with a <c>Utf8JsonWriter</c> rather than
        /// through <c>JsonSerializer</c> for exactly that reason - the assertion below
        /// fails outright if that ever regresses.
        /// </summary>
        [Test]
        public async Task ToolResultsSerializeWithoutReflectionAsync()
        {
            await Assert.That(JsonReflectionIsEnabled()).IsFalse();

            string json = OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
            {
                ["error"] = true,
                ["statusCode"] = "BadNodeIdUnknown",
                ["message"] = null,
                ["values"] = new List<object?> { 1, 2.5, "three", null }
            });

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            await Assert.That(root.GetProperty("error").GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty("statusCode").GetString()).IsEqualTo("BadNodeIdUnknown");
            await Assert.That(root.GetProperty("message").ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(root.GetProperty("values").GetArrayLength()).IsEqualTo(4);
        }

        /// <summary>
        /// The diagnostics tools are deliberately absent from this binary. They sit on
        /// SharpPcap and on reflective service-call dissection, neither of which is trim-
        /// or AOT-safe, so a host publishing ahead of time takes the core tools alone.
        /// </summary>
        [Test]
        public async Task DiagnosticsToolsAreNotPartOfTheAotSurfaceAsync()
        {
            await Assert.That(
                AppDomain.CurrentDomain.GetAssemblies()
                    .Any(assembly => assembly.GetName().Name == "Opc.Ua.Mcp.Diagnostics"))
                .IsFalse();
        }

        private static bool JsonReflectionIsEnabled()
        {
            return AppContext.TryGetSwitch(
                "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault",
                out bool enabled) && enabled;
        }

        private static IReadOnlyList<McpServerTool> ResolveTools(IServiceCollection services)
        {
            using ServiceProvider provider = services.BuildServiceProvider();
            return [.. provider.GetServices<McpServerTool>()];
        }
    }
}
