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

namespace Opc.Ua.Tools.Tests.McpCompanion.Workflows
{
    /// <summary>
    /// Tests the consolidated package's real registration, published schemas and invocation binding.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class WorkflowBindingTests
    {
        /// <summary>
        /// Keeps every profile bounded and existing enum ordinals stable.
        /// </summary>
        [TestCase(McpToolProfile.Roles, 22, "roles_", 13)]
        [TestCase(McpToolProfile.Users, 23, "users_", 6)]
        [TestCase(McpToolProfile.Files, 24, "files_", 13)]
        [TestCase(McpToolProfile.Alarms, 25, "alarms_", 19)]
        [TestCase(McpToolProfile.History, 26, "history_", 23)]
        public async Task SingleProfilesExposeExactBoundedCatalogueAsync(
            McpToolProfile profile, int ordinal, string prefix, int count)
        {
            await using WorkflowTestContext fixture = await WorkflowTestContext.CreateAsync(
                profiles: new McpToolProfileSet(profile)).ConfigureAwait(false);
            string[] names = [.. fixture.Services.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];
            Assert.That((int)profile, Is.EqualTo(ordinal));
            Assert.That(names.Count(name => name.StartsWith(prefix, StringComparison.Ordinal)), Is.EqualTo(count));
            Assert.That(names, Has.Length.EqualTo(count + 4));
            Assert.That(names.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(names.Length));
        }

        /// <summary>
        /// Mixed single/composed registration does not duplicate tools or connection helpers.
        /// </summary>
        [Test]
        public async Task ComposedRegistrationIsIdempotentAndPreservesHostOptionsAsync()
        {
            var services = new ServiceCollection();
            var options = new WorkflowOptions { MaxItems = 17 };
            services.AddOpcUaMcpCore().AddOpcUaMcpWorkflows(options).AddOpcUaMcpWorkflows();
            services.AddMcpServer().WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaWorkflowTools(McpToolProfile.Roles)
                .WithOpcUaWorkflowTools(new McpToolProfileSet([McpToolProfile.Users, McpToolProfile.Roles]))
                .WithOpcUaWorkflowTools(McpToolProfile.Full);
            await using ServiceProvider provider = services.BuildServiceProvider();
            string[] names = [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];
            Assert.That(names, Has.Length.EqualTo(78));
            Assert.That(names.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(78));
            Assert.That(provider.GetRequiredService<WorkflowOptions>(), Is.SameAs(options));
        }

        /// <summary>
        /// A full profile is not a grant; role inspection remains visible but all user administration is gated.
        /// </summary>
        [Test]
        public async Task DisabledHostHidesIdentityAdministrationButRetainsRoleReadToolsAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync(enabled: false).ConfigureAwait(false);
            ListToolsResult list = await fixture.ListAsync().ConfigureAwait(false);
            Assert.That(list.Tools.Select(tool => tool.Name), Does.Contain("roles_list"));
            Assert.That(list.Tools.Select(tool => tool.Name), Does.Not.Contain("users_policy"));
            Assert.That(list.Tools.Select(tool => tool.Name), Does.Contain("files_upload"));
            Assert.That(list.Tools.Any(tool => WorkflowFilters.RequiresIdentityAdministration(tool.Name)),
                Is.False);
            Assert.That(list.Tools, Has.Count.EqualTo(62));
        }

        /// <summary>
        /// Typed arrays and scalar constraints are advertised concretely, not as JSON-in-string placeholders.
        /// </summary>
        [Test]
        public async Task PublishedInputSchemasContainConcreteArraysAndHostIndependentHardCeilingsAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            McpServerTool values = fixture.Services.GetServices<McpServerTool>()
                .Single(tool => tool.ProtocolTool.Name == "history_insert_values");
            JsonElement schema = values.ProtocolTool.InputSchema;
            Assert.That(schema.GetProperty("additionalProperties").GetBoolean(), Is.False);
            JsonElement parameters = schema.GetProperty("properties");
            Assert.That(parameters.TryGetProperty("input", out _), Is.False);
            JsonElement array = parameters.GetProperty("values");
            Assert.That(array.GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(array.GetProperty("maxItems").GetInt32(), Is.EqualTo(500));
            Assert.That(array.GetProperty("items").GetProperty("properties").TryGetProperty(
                "sourceTimestamp", out _), Is.True);
            Assert.That(array.GetProperty("items").GetProperty("properties").TryGetProperty("type", out _), Is.True);
            Assert.That(array.GetProperty("items").GetProperty("properties").GetProperty("value")
                .TryGetProperty("properties", out _), Is.False);
            Assert.That(SchemaDepth(schema), Is.EqualTo(3));
            McpServerTool observe = fixture.Services.GetServices<McpServerTool>()
                .Single(tool => tool.ProtocolTool.Name == "alarms_observe");
            JsonElement limits = observe.ProtocolTool.InputSchema.GetProperty("properties");
            Assert.That(limits.GetProperty("maxResults").GetProperty("maximum").GetInt32(), Is.EqualTo(500));
            Assert.That(limits.GetProperty("durationMilliseconds").GetProperty("maximum").GetInt32(),
                Is.EqualTo(30000));
            Assert.That(limits.TryGetProperty("caller", out _), Is.False);
            Assert.That(limits.TryGetProperty("owner", out _), Is.False);
        }

        /// <summary>
        /// The real SDK binder accepts the published nested typed-array shape.
        /// </summary>
        [Test]
        public async Task SdkBinderPreservesTypedDataValueQualityAndExactUtcOffsetAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            CallToolResult result = await fixture.InvokeAsync("history_insert_values", new JsonObject
            {
                ["nodeId"] = fixture.Remote.Id("historian").ToString(),
                ["values"] = JsonNode.Parse("""
                    [{"type":"Int32","value":0,
                      "sourceTimestamp":"2026-10-12T14:30:00+02:00","statusCode":1073741824}]
                    """)
            }).ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.HistoryUpdates, Has.Count.EqualTo(1));
            Assert.That(fixture.HistoryUpdates[0].TryGetValue(out UpdateDataDetails? details), Is.True);
            Assert.That(details!.PerformInsertReplace, Is.EqualTo(PerformUpdateType.Insert));
            Assert.That(details.UpdateValues[0].WrappedValue, Is.EqualTo(Variant.From(0)));
            Assert.That(details.UpdateValues[0].StatusCode, Is.EqualTo(new StatusCode(1073741824)));
            Assert.That(details.UpdateValues[0].SourceTimestamp,
                Is.EqualTo((DateTimeUtc)new DateTime(2026, 10, 12, 12, 30, 0, DateTimeKind.Utc)));
        }

        /// <summary>
        /// Tool registration needs both its own DTO contracts and SDK scalar/return metadata.
        /// </summary>
        [TestCase(typeof(int))]
        [TestCase(typeof(int?))]
        [TestCase(typeof(uint))]
        [TestCase(typeof(uint?))]
        [TestCase(typeof(double))]
        [TestCase(typeof(bool))]
        [TestCase(typeof(bool?))]
        [TestCase(typeof(string))]
        [TestCase(typeof(JsonElement?))]
        [TestCase(typeof(CallToolResult))]
        [TestCase(typeof(ArrayOf<WorkflowDataValueInput>))]
        [TestCase(typeof(ArrayOf<WorkflowHistoryEventInput>))]
        [TestCase(typeof(ArrayOf<WorkflowRolePermissionInput>))]
        [TestCase(typeof(ArrayOf<WorkflowArrayElementInput>))]
        [TestCase(typeof(ArrayOf<WorkflowEventFieldInput>))]
        [TestCase(typeof(ArrayOf<WorkflowEventPathInput>))]
        [TestCase(typeof(ArrayOf<WorkflowAnnotationInput>))]
        [TestCase(typeof(ArrayOf<string>))]
        [TestCase(typeof(IdentityCriteriaType))]
        [TestCase(typeof(MessageSecurityMode))]
        public void ToolOptionsResolveWorkflowAndSdkContractsWithoutReplacingEitherContext(Type type)
        {
            Assert.That(WorkflowToolJson.Options.GetTypeInfo(type).Type, Is.EqualTo(type));
            Assert.That(WorkflowToolJson.Options.IsReadOnly, Is.True);
            Assert.That(WorkflowToolJson.Options, Is.Not.SameAs(WorkflowInputJsonContext.Default.Options));
        }

        /// <summary>
        /// Measures all advertised branches using the pinned grader's actual traversal, including union branches.
        /// </summary>
        [Test]
        public async Task AdvertisedWorkflowParametersMatchPinnedGraderDepthAndDescriptionRulesAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            McpServerTool[] tools = [.. fixture.Services.GetServices<McpServerTool>()
                .Where(tool => tool.ProtocolTool.Name.StartsWith("roles_", StringComparison.Ordinal) ||
                    tool.ProtocolTool.Name.StartsWith("users_", StringComparison.Ordinal) ||
                    tool.ProtocolTool.Name.StartsWith("files_", StringComparison.Ordinal) ||
                    tool.ProtocolTool.Name.StartsWith("alarms_", StringComparison.Ordinal) ||
                    tool.ProtocolTool.Name.StartsWith("history_", StringComparison.Ordinal))];
            Assert.That(tools, Has.Length.EqualTo(74));
            foreach (McpServerTool tool in tools)
            {
                JsonElement schema = tool.ProtocolTool.InputSchema;
                Assert.That(SchemaDepth(schema), Is.LessThanOrEqualTo(3), tool.ProtocolTool.Name);
                JsonElement properties = schema.GetProperty("properties");
                Assert.That(properties.EnumerateObject().Count(), Is.LessThanOrEqualTo(8), tool.ProtocolTool.Name);
                Assert.That(schema.GetProperty("additionalProperties").GetBoolean(), Is.False);
                Assert.That(ContainsReference(schema), Is.False,
                    "Depth must not be hidden behind unresolved references.");
                foreach (JsonProperty parameter in properties.EnumerateObject())
                {
                    Assert.That(parameter.Value.TryGetProperty("description", out JsonElement description), Is.True,
                        $"{tool.ProtocolTool.Name}.{parameter.Name}");
                    Assert.That(description.GetString(), Is.Not.Null.And.Not.Empty);
                }
            }
        }

        /// <summary>
        /// Safety-sounding unknown arguments must not be silently ignored by the SDK binder.
        /// </summary>
        [Test]
        public async Task UnknownRootArgumentIsRefusedBeforeAnyRemoteWorkAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            CallToolResult result = await fixture.InvokeAsync("roles_create", new JsonObject
            {
                ["roleName"] = "Maintenance",
                ["namespaceUri"] = "urn:roles",
                ["expectedGeneration"] = 4
            }).ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadInvalidArgument);
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        /// <summary>
        /// The exact maximum and immediate neighbors are validated by host configuration.
        /// </summary>
        [TestCase(1, true)]
        [TestCase(500, true)]
        [TestCase(0, false)]
        [TestCase(501, false)]
        public void PageCeilingConfigurationHasExactBoundaries(int limit, bool accepted)
        {
            var options = new WorkflowOptions { MaxItems = limit };
            if (accepted)
            {
                Assert.That(options.Validate, Throws.Nothing);
            }
            else
            {
                Assert.That(options.Validate, Throws.TypeOf<ArgumentOutOfRangeException>());
            }
        }

        /// <summary>
        /// A caller cannot increase a reduced host ceiling even within the catalogue's absolute maximum.
        /// </summary>
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, true)]
        public async Task CallerCannotRaiseHostPageLimitAsync(int requested, bool refused)
        {
            await using WorkflowTestContext fixture = await WorkflowTestContext.CreateAsync(
                options: new WorkflowOptions { MaxItems = 3 }).ConfigureAwait(false);
            CallToolResult result = await fixture.Roles.ListAsync(requested, sessionName: "selected")
                .ConfigureAwait(false);
            Assert.That(result.IsError, Is.EqualTo(refused));
            if (refused)
            {
                Assert.That(fixture.Remote.Browses, Is.Empty);
            }
        }

        /// <summary>
        /// The production scope factory refuses arbitrary sessions instead of treating a reference as generation-safe.
        /// </summary>
        [Test]
        public async Task DefaultScopeFactoryRefusesUnmanagedSessionWithoutUnsafeFallbackAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            var factory = new WorkflowSessionFactory();
            await Assert.ThatAsync(async () =>
            {
                IWorkflowSessionScope scope = await factory.CaptureAsync(
                    fixture.Manager.GetSessionInfoOrThrow("selected")).ConfigureAwait(false);
                await scope.DisposeAsync().ConfigureAwait(false);
            }, Throws.InstanceOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadNotSupported)).ConfigureAwait(false);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(fixture.Remote.Reads, Is.Empty);
        }

        /// <summary>
        /// Follows mcpgrade feff7162903ab9fbe92f182be9a082fcb24c9bbc rules/util.ts:
        /// every properties/items/anyOf/oneOf/allOf edge adds one, starting at zero.
        /// </summary>
        private static int SchemaDepth(JsonElement schema, int depth = 0)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return depth;
            }
            int maximum = depth;
            if (schema.TryGetProperty("properties", out JsonElement properties))
            {
                foreach (JsonProperty child in properties.EnumerateObject())
                {
                    maximum = Math.Max(maximum, SchemaDepth(child.Value, depth + 1));
                }
            }
            if (schema.TryGetProperty("items", out JsonElement items))
            {
                maximum = Math.Max(maximum, items.ValueKind == JsonValueKind.Array
                    ? items.EnumerateArray().Select(child => SchemaDepth(child, depth + 1)).DefaultIfEmpty(depth).Max()
                    : SchemaDepth(items, depth + 1));
            }
            foreach (string name in new[] { "anyOf", "oneOf", "allOf" })
            {
                if (schema.TryGetProperty(name, out JsonElement alternatives))
                {
                    foreach (JsonElement child in alternatives.EnumerateArray())
                    {
                        maximum = Math.Max(maximum, SchemaDepth(child, depth + 1));
                    }
                }
            }
            return maximum;
        }

        /// <summary>
        /// Rejects references that could make an incomplete shape look artificially shallow.
        /// </summary>
        private static bool ContainsReference(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.Object => value.EnumerateObject()
                    .Any(property => property.Name == "$ref" || ContainsReference(property.Value)),
                JsonValueKind.Array => value.EnumerateArray().Any(ContainsReference),
                _ => false
            };
        }
    }
}
#endif
