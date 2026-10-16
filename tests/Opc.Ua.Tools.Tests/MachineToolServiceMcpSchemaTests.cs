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
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;
using static Opc.Ua.Tools.Tests.MachineToolServiceMcpHarness;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Pins actual MCP wire descriptions, native unions, enumerated operations, consent and response bounds.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class MachineToolServiceMcpSchemaTests
    {
        /// <summary>
        /// The actual server lists five practical tools with different read and destructive metadata.
        /// </summary>
        [Test]
        public async Task ActualSchemasDescribeEveryParameterAndExactServiceBounds()
        {
            await using MachineToolServiceMcpHarness harness = await MachineToolServiceMcpHarness.CreateAsync()
                .ConfigureAwait(false);
            ListToolsResult listed = await harness.Client.ListToolsAsync(new ListToolsRequestParams())
                .ConfigureAwait(false);
            Assert.That(listed.Tools.Select(tool => tool.Name), Is.EquivalentTo(s_names));
            foreach (Tool tool in listed.Tools)
            {
                JsonElement properties = tool.InputSchema.GetProperty("properties");
                Assert.That(properties.TryGetProperty("accessor", out _), Is.False);
                Assert.That(properties.TryGetProperty("ct", out _), Is.False);
                foreach (JsonProperty property in properties.EnumerateObject())
                {
                    Assert.That(property.Value.GetProperty("description").GetString(), Has.Length.GreaterThan(20),
                        $"{tool.Name}.{property.Name}");
                }
                JsonElement node = properties.GetProperty("machineNodeId");
                Assert.That(node.GetProperty("minLength").GetInt32(), Is.EqualTo(1));
                Assert.That(node.GetProperty("maxLength").GetInt32(), Is.EqualTo(1024));
                JsonElement response = properties.GetProperty("maximumResponseBytes");
                Assert.That(response.GetProperty("minimum").GetInt32(), Is.EqualTo(1024));
                Assert.That(response.GetProperty("maximum").GetInt32(), Is.EqualTo(1048576));
                bool destructive = tool.Name is "machinetool_control_job" or "machinetool_change_file";
                Assert.That(tool.Annotations!.ReadOnlyHint, Is.EqualTo(!destructive));
                Assert.That(tool.Annotations.DestructiveHint, Is.EqualTo(destructive));
                Assert.That(tool.Annotations.IdempotentHint, Is.EqualTo(!destructive));
                if (destructive)
                {
                    Assert.That(properties.GetProperty("confirm").GetProperty("default").GetBoolean(), Is.False);
                }
                if (properties.TryGetProperty("root", out JsonElement root))
                {
                    Assert.That(root.GetProperty("enum").EnumerateArray().Select(value => value.GetString()),
                        Is.EqualTo(s_roots));
                }
                if (properties.TryGetProperty("maximumBytes", out JsonElement bytes))
                {
                    Assert.That(bytes.GetProperty("minimum").GetInt32(), Is.EqualTo(1));
                    Assert.That(bytes.GetProperty("maximum").GetInt32(), Is.EqualTo(524288));
                }
            }
            Tool control = listed.Tools.Single(tool => tool.Name == "machinetool_control_job");
            JsonElement inputs = control.InputSchema.GetProperty("properties");
            Assert.That(inputs.GetProperty("operation").GetProperty("enum").EnumerateArray()
                .Select(value => value.GetString()), Is.EquivalentTo(s_verbs));
            JsonElement nativeOrder = inputs.GetProperty("jobOrder");
            Assert.That(HasType(nativeOrder, "object"), Is.True);
            Assert.That(HasType(nativeOrder, "string"), Is.False);
            Assert.That(nativeOrder.GetProperty("properties").GetProperty("EncodingMask")
                .GetProperty("maximum").GetInt32(), Is.EqualTo(1023));
            Assert.That(inputs.GetProperty("comments").GetProperty("maxItems").GetInt32(),
                Is.EqualTo(8));
            Tool query = listed.Tools.Single(tool => tool.Name == "machinetool_query_jobs");
            JsonElement queryInputs = query.InputSchema.GetProperty("properties");
            Assert.That(queryInputs.GetProperty("query").GetProperty("enum").EnumerateArray()
                .Select(value => value.GetString()),
                Is.EqualTo(s_queries));
            Assert.That(queryInputs.GetProperty("states").GetProperty("maxItems").GetInt32(),
                Is.EqualTo(16));
            Tool file = listed.Tools.Single(tool => tool.Name == "machinetool_change_file");
            Assert.That(file.InputSchema.GetProperty("properties").GetProperty("operation").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo(s_fileActions));
            JsonElement options = file.InputSchema.GetProperty("properties").GetProperty("options");
            Assert.That(HasType(options, "object"), Is.True);
            Assert.That(options.GetProperty("additionalProperties").GetBoolean(), Is.False);
            JsonElement optionFields = options.GetProperty("properties");
            Assert.That(optionFields.GetProperty("contentBase64")
                .GetProperty("maxLength").GetInt32(), Is.EqualTo(699052));
            Assert.That(optionFields.GetProperty("destination").GetProperty("maxLength").GetInt32(), Is.EqualTo(1024));
            Assert.That(optionFields.GetProperty("maximumBytes").GetProperty("minimum").GetInt32(), Is.EqualTo(1));
            Assert.That(optionFields.GetProperty("maximumBytes").GetProperty("maximum").GetInt32(), Is.EqualTo(524288));
            Assert.That(optionFields.GetProperty("maximumBytes").GetProperty("default").GetInt32(), Is.EqualTo(65536));
            Assert.That(optionFields.GetProperty("recursive").GetProperty("default").GetBoolean(), Is.False);
            foreach (JsonProperty option in optionFields.EnumerateObject())
            {
                Assert.That(option.Value.GetProperty("description").GetString(), Has.Length.GreaterThan(20));
                Assert.That(file.InputSchema.GetProperty("properties").TryGetProperty(option.Name, out _), Is.False);
            }
            Assert.That(file.InputSchema.GetRawText(), Does.Not.Contain("ReadOnlyMemory"));
        }

        /// <summary>
        /// The real catalog stays within shallow native-schema and top-level argument budgets, with return contracts.
        /// </summary>
        [Test]
        public async Task ServiceCatalogIsShallowConciseAndDescribesReturns()
        {
            await using MachineToolServiceMcpHarness harness = await MachineToolServiceMcpHarness.CreateAsync()
                .ConfigureAwait(false);
            ListToolsResult listed = await harness.Client.ListToolsAsync(new ListToolsRequestParams())
                .ConfigureAwait(false);
            foreach (Tool tool in listed.Tools)
            {
                Assert.That(tool.InputSchema.GetProperty("properties").EnumerateObject().Count(),
                    Is.LessThanOrEqualTo(8), tool.Name);
                Assert.That(Depth(tool.InputSchema), Is.LessThanOrEqualTo(3), tool.Name);
                Assert.That(tool.Description, Does.Contain("Returns"), tool.Name);
                Assert.That(tool.InputSchema.TryGetProperty("$defs", out _), Is.False);
            }
            Assert.That(listed.Tools.Sum(tool => tool.Description!.Length), Is.LessThanOrEqualTo(1800));
        }

        /// <summary>
        /// Filters do not modify unrelated catalogs or malformed schemas and require a real continuation.
        /// </summary>
        [Test]
        public void FilterLeavesOtherCatalogsUnchanged()
        {
            var unrelated = new Tool
            {
                Name = "other",
                InputSchema = Parse("{\"type\":\"object\",\"properties\":{\"root\":{\"type\":\"string\"}}}")
            };
            var absent = new Tool { Name = "machinetool_query_jobs", InputSchema = Parse("{\"type\":\"object\"}") };
            string original = unrelated.InputSchema.GetRawText();
            MachineToolServiceFilters.Apply([unrelated, absent]);
            Assert.That(unrelated.InputSchema.GetRawText(), Is.EqualTo(original));
            Assert.That(absent.InputSchema.GetRawText(), Is.EqualTo("{\"type\":\"object\"}"));
            Assert.Throws<ArgumentException>(() => new Tool { Name = "invalid", InputSchema = Parse("true") });
            Assert.Throws<ArgumentNullException>(() => MachineToolServiceFilters.AddSchemas(null!));
        }

        /// <summary>
        /// Recognizes native nullable type unions without treating an object as JSON serialized inside a string.
        /// </summary>
        private static bool HasType(JsonElement schema, string expected)
        {
            if (!schema.TryGetProperty("type", out JsonElement type))
            {
                return false;
            }
            return type.ValueKind == JsonValueKind.String ? type.GetString() == expected :
                type.EnumerateArray().Any(value => value.GetString() == expected);
        }

        /// <summary>
        /// Counts argument container nesting, not documentation/examples or scalar schema constraints.
        /// </summary>
        private static int Depth(JsonElement schema)
        {
            int childDepth = 0;
            if (schema.TryGetProperty("properties", out JsonElement properties))
            {
                foreach (JsonProperty property in properties.EnumerateObject())
                {
                    childDepth = Math.Max(childDepth, Depth(property.Value));
                }
            }
            if (schema.TryGetProperty("items", out JsonElement items))
            {
                childDepth = Math.Max(childDepth, Depth(items));
            }
            return childDepth + (HasType(schema, "object") || HasType(schema, "array") ? 1 : 0);
        }

        /// <summary>The bounded service catalog's exact tool identities.</summary>
        private static readonly string[] s_names =
        [
            "machinetool_query_jobs", "machinetool_control_job", "machinetool_list_files",
            "machinetool_read_file", "machinetool_change_file"
        ];

        /// <summary>The only supported remote provider roots.</summary>
        private static readonly string[] s_roots = ["FileSystem", "WorkMasters"];

        /// <summary>The eleven standard ISA-95 V2 verbs.</summary>
        private static readonly string[] s_verbs =
        [
            "Store", "StoreAndStart", "Start", "Stop", "Abort", "Pause", "Resume", "Clear", "Cancel",
            "Update", "RevokeStart"
        ];

        /// <summary>The independent read-only job query selectors.</summary>
        private static readonly string[] s_queries = ["Orders", "ResponseById", "ResponsesByState"];

        /// <summary>The grouped explicit file operations.</summary>
        private static readonly string[] s_fileActions =
        [
            "Write", "CreateFile", "CreateDirectory", "Delete", "Move", "Copy"
        ];
    }
}
#endif
