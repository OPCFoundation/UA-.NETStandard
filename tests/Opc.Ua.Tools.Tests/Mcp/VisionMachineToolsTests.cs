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
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Pins the released-model tool catalog and its bounded input contracts.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class VisionMachineToolsTests
    {
        [TestCase(McpToolProfile.Vision)]
        [TestCase(McpToolProfile.Full)]
        public void ReleasedToolsAreRegisteredWithoutReplacingDraftTools(McpToolProfile profile)
        {
            using ServiceProvider provider = Services(profile).BuildServiceProvider();
            string[] names = provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name).ToArray();
            Assert.That(names.Where(name => name.StartsWith("vision_machine_", StringComparison.Ordinal)),
                Is.EquivalentTo(s_names));
            Assert.That(names, Does.Contain("vision_run_inference"));
            Assert.That(names, Is.Unique);
        }

        [Test]
        public void CoreProfileDoesNotGainVisionCommands()
        {
            using ServiceProvider provider = Services(McpToolProfile.Core).BuildServiceProvider();
            Assert.That(provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name),
                Has.None.StartsWith("vision_machine_"));
        }

        [Test]
        public void FileSchemaCarriesChunkLimitsAndRequiredSelectors()
        {
            using ServiceProvider provider = Services(McpToolProfile.Vision).BuildServiceProvider();
            McpServerTool tool = provider.GetServices<McpServerTool>()
                .Single(candidate => candidate.ProtocolTool.Name == "vision_machine_file");
            JsonElement schema = tool.ProtocolTool.InputSchema;
            string?[] required = schema.GetProperty("required").EnumerateArray()
                .Select(field => field.GetString()).ToArray();
            Assert.That(required, Does.Contain("nodeId"));
            Assert.That(required, Does.Contain("operation"));
            JsonElement length = schema.GetProperty("properties").GetProperty("length");
            Assert.That(length.GetProperty("minimum").GetInt32(), Is.EqualTo(1));
            Assert.That(length.GetProperty("maximum").GetInt32(), Is.EqualTo(1048576));
        }

        [TestCase(0u)]
        [TestCase(101u)]
        public void OversizedMetadataPagesFailBeforeSessionAccess(uint maximum)
        {
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await VisionMachineTools.ResultsAsync(null!, "i=1", "List", maximumResults: maximum)
                    .ConfigureAwait(false));
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1048577)]
        public void InvalidFileChunkLengthsFailBeforeSessionAccess(int length)
        {
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await VisionMachineTools.FileAsync(null!, "i=1", "Read", length: length).ConfigureAwait(false));
        }

        [TestCase("[]")]
        [TestCase("{}")]
        [TestCase("{\"Parameters\":42}")]
        public void MalformedStartParametersFailBeforeSessionAccess(string json)
        {
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await VisionMachineTools.CommandAsync(null!, "i=1", "StartSingleJob", parametersJson: json)
                    .ConfigureAwait(false));
        }

        [Test]
        public void OversizedStructuredFiltersFailBeforeSessionAccess()
        {
            string json = "{\"ignored\":\"" + new string('x', 1024 * 1024) + "\"}";
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await VisionMachineTools.ResultsAsync(null!, "i=1", "List", filtersJson: json).ConfigureAwait(false));
        }

        private static ServiceCollection Services(McpToolProfile profile)
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddOpcUaMcpVision();
            services.AddMcpServer().WithOpcUaVisionTools(profile);
            return services;
        }

        private static readonly string[] s_names =
        [
            "vision_machine_list",
            "vision_machine_read_state",
            "vision_machine_command",
            "vision_machine_read_asset",
            "vision_machine_recipes",
            "vision_machine_configurations",
            "vision_machine_results",
            "vision_machine_report_safety",
            "vision_machine_file",
            "vision_machine_recipe_object",
            "vision_machine_condition"
        ];
    }
}
#endif
