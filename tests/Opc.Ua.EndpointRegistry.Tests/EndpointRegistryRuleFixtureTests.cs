/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.Tests
{
    /// <summary>
    /// Replays Endpoint Registry rule outcomes recorded from the normative Python validators.
    /// </summary>
    [TestFixture]
    [Category("EndpointRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class EndpointRegistryRuleFixtureTests
    {
        public static IEnumerable<TestCaseData> Cases()
        {
            foreach (JsonElement item in s_document.Value.RootElement.GetProperty("cases").EnumerateArray())
            {
                if (item.TryGetProperty("skipReplay", out JsonElement skip) && skip.GetBoolean())
                {
                    continue;
                }
                yield return new TestCaseData(item.GetProperty("id").GetString()!, item.GetRawText())
                    .SetArgDisplayNames(item.GetProperty("id").GetString()!);
            }
        }

        [Test]
        public void FixturesCarryTheirProvenance()
        {
            JsonElement header = s_document.Value.RootElement.GetProperty("header");

            Assert.Multiple(() =>
            {
                Assert.That(header.GetProperty("format").GetString(), Is.EqualTo("EndpointRegistryRuleFixtures/2.0"));
                Assert.That(header.GetProperty("commit").GetString(), Has.Length.EqualTo(40));
                Assert.That(s_document.Value.RootElement.GetProperty("cases").GetArrayLength(), Is.EqualTo(5440));
                Assert.That(Cases().Count(), Is.EqualTo(5272));
            });
        }

        [TestCaseSource(nameof(Cases))]
        public void CSharpRulesMatchPythonOutcome(string id, string rawCase)
        {
            using JsonDocument document = JsonDocument.Parse(rawCase);
            JsonElement fixture = document.RootElement;
            RegistryObjectValueDataType value = (RegistryObjectValueDataType)RegistryValues.Parse(
                Encoding.UTF8.GetBytes(fixture.GetProperty("args")[0].GetRawText()));

            RegistryRuleException? error = null;
            try
            {
                Validate(fixture.GetProperty("function").GetString()!, value, fixture.GetProperty("kwargs"));
            }
            catch (RegistryRuleException caught)
            {
                error = caught;
            }

            if (fixture.GetProperty("valid").GetBoolean())
            {
                Assert.That(error, Is.Null, id);
                return;
            }
            Assert.That(error, Is.Not.Null, id);
            Assert.Multiple(() =>
            {
                Assert.That(error!.Code, Is.EqualTo(fixture.GetProperty("code").GetString()), id);
                if (fixture.TryGetProperty("path", out JsonElement path) && path.GetString() is { } expected)
                {
                    Assert.That(error.PathText, Is.EqualTo(expected), id);
                }
            });
        }

        [Test]
        public void FixtureCountsByFunctionAreStable()
        {
            JsonElement counts = s_document.Value.RootElement.GetProperty("header").GetProperty("counts");

            Assert.Multiple(() =>
            {
                Assert.That(counts.GetProperty("vector").GetProperty("message_rules.validate_message").GetInt32(), Is.EqualTo(27));
                Assert.That(counts.GetProperty("harvest").GetProperty("message_rules.validate_message").GetInt32(), Is.EqualTo(167));
                Assert.That(counts.GetProperty("mutation").GetProperty("message_rules.validate_message").GetInt32(), Is.EqualTo(2472));
                Assert.That(counts.GetProperty("harvest").GetProperty("endpoint_rules.validate_endpoint").GetInt32(), Is.EqualTo(100));
                Assert.That(counts.GetProperty("mutation").GetProperty("media_rules.validate_media").GetInt32(), Is.EqualTo(205));
                Assert.That(s_document.Value.RootElement.GetProperty("header").GetProperty("skippedInputs")
                    .GetProperty("missing test module: test_extension_contract").GetInt32(), Is.EqualTo(1));
            });
        }

        private static void Validate(string function, RegistryObjectValueDataType value, JsonElement kwargs)
        {
            switch (function)
            {
                case "message_rules.validate_message":
                    EndpointRegistryRules.ValidateMessage(value, JsonObjectArgument(kwargs, "group"));
                    return;
                case "message_rules.validate_group":
                    EndpointRegistryRules.ValidateMessageGroup(value, JsonStringArgument(kwargs, "group_id"));
                    return;
                case "message_rules.validate_container":
                    EndpointRegistryRules.ValidateContainer(value, JsonStringArgument(kwargs, "group_xid"));
                    return;
                case "message_rules.validate_registry":
                    EndpointRegistryRules.ValidateMessageRegistry(value);
                    return;
                case "endpoint_rules.validate_endpoint":
                    EndpointRegistryRules.ValidateEndpoint(value, JsonBooleanArgument(kwargs, "media"));
                    return;
                case "endpoint_rules.validate_registry":
                    EndpointRegistryRules.ValidateRegistry(value, JsonBooleanArgument(kwargs, "media"));
                    return;
                case "media_rules.validate_media":
                    EndpointRegistryRules.ValidateEndpoint(value, true);
                    return;
                case "extension_contract.validate_definition":
                    EndpointRegistryRules.ValidateExtensionDefinition(value);
                    return;
                default:
                    Assert.Fail("Unknown fixture function " + function);
                    return;
            }
        }

        private static string? JsonStringArgument(JsonElement kwargs, string name)
        {
            return kwargs.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static bool JsonBooleanArgument(JsonElement kwargs, string name)
        {
            return kwargs.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
        }

        private static string? JsonObjectArgument(JsonElement kwargs, string name)
        {
            return kwargs.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object
                ? value.GetRawText()
                : null;
        }

        private static readonly System.Lazy<JsonDocument> s_document = new(() => JsonDocument.Parse(
            File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Assets", "rule-fixtures.json"))));
    }
}
