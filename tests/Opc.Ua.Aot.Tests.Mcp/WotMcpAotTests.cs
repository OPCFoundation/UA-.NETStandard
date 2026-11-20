// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.WotCon;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises the new request and result contracts without enabling JSON reflection.
    /// </summary>
    public sealed class WotMcpAotTests
    {
        /// <summary>
        /// Bounded ArrayOf request converters retain typed selectors and explicit refresh scope.
        /// </summary>
        [Test]
        public async Task RefreshInputsUseSourceGeneratedArrayMetadataAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            WotRefreshInput input = JsonSerializer.Deserialize(
                """
                {"selection":[{"kind":"ThingModel","groupId":"models","resourceId":"motor","versionId":"v1"}],
                 "allResources":false,"dryRun":true,"requestId":"req-1"}
                """, WotAotJsonContext.Default.WotRefreshInput)!;

            await Assert.That(input.Selection.Count).IsEqualTo(1);
            await Assert.That(input.Selection[0].Kind).IsEqualTo(WoTDocumentKindEnum.ThingModel);
            await Assert.That(input.Selection[0].GroupId).IsEqualTo("models");
            await Assert.That(input.Selection[0].ResourceId).IsEqualTo("motor");
            await Assert.That(input.Selection[0].VersionId).IsEqualTo("v1");
            await Assert.That(input.DryRun).IsTrue();
            await Assert.That(input.AllResources).IsFalse();
            string serialized = JsonSerializer.Serialize(input, WotAotJsonContext.Default.WotRefreshInput);
            using var json = JsonDocument.Parse(serialized);
            await Assert.That(json.RootElement.GetProperty("selection").ValueKind).IsEqualTo(JsonValueKind.Array);
        }

        /// <summary>
        /// Document dependencies and conformance claims retain their concrete array shapes.
        /// </summary>
        [Test]
        public async Task DocumentInputsUseSourceGeneratedDependencyMetadataAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            WotDocumentInput input = JsonSerializer.Deserialize(
                """
                {"source":{"text":"{}"},"dependencies":[{"reference":"model.json","text":"{}"}],
                 "requiredConformance":["test-profile"],"resolveRemoteDependencies":false}
                """, WotAotJsonContext.Default.WotDocumentInput)!;

            await Assert.That(input.Dependencies.Count).IsEqualTo(1);
            await Assert.That(input.Dependencies[0].Reference).IsEqualTo("model.json");
            await Assert.That(input.RequiredConformance[0]).IsEqualTo("test-profile");
            await Assert.That(input.ResolveRemoteDependencies).IsFalse();
            string serialized = JsonSerializer.Serialize(input, WotAotJsonContext.Default.WotDocumentInput);
            using var json = JsonDocument.Parse(serialized);
            await Assert.That(json.RootElement.GetProperty("dependencies").GetArrayLength()).IsEqualTo(1);
            await Assert.That(json.RootElement.GetProperty("requiredConformance").GetArrayLength()).IsEqualTo(1);
        }

        /// <summary>
        /// Non-default method status, raw argument statuses and typed values remain reflection-free.
        /// </summary>
        [Test]
        public async Task RegistryMethodResultsPreserveRawStatusWithoutReflectionAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            IServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            var method = new CallMethodResult
            {
                StatusCode = StatusCodes.GoodResultsMayBeIncomplete,
                InputArgumentResults = [StatusCodes.Good],
                OutputArguments = [Variant.From(false), Variant.From(0u)]
            };
            var result = McpModelAccess.MethodResult(method, context);

            await Assert.That(result["error"]!.GetValue<bool>()).IsFalse();
            await Assert.That(result["statusCodeValue"]!.GetValue<uint>())
                .IsEqualTo(StatusCodes.GoodResultsMayBeIncomplete.Code);
            await Assert.That(result["outputArguments"]![0]!["Value"]!.GetValue<bool>()).IsFalse();
            await Assert.That(result["outputArguments"]![1]!["Value"]!.GetValue<uint>()).IsEqualTo(0u);
            var validation = new WoTValidationOutcomeDataType
            {
                FormatValidated = true,
                FormatOutcome = WoTOutcomeEnum.Warning,
                FormatReason = "supported with caveats"
            };
            var encoded = McpCompanionJson.Encode(validation, context)!;
            await Assert.That(encoded["FormatReason"]!.GetValue<string>()).IsEqualTo("supported with caveats");
        }
    }

    /// <summary>
    /// The embedding caller supplies closed metadata instead of enabling JSON reflection.
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        UseStringEnumConverter = true)]
    [JsonSerializable(typeof(WotRefreshInput))]
    [JsonSerializable(typeof(WotDocumentInput))]
    internal sealed partial class WotAotJsonContext : JsonSerializerContext
    {
    }
}
