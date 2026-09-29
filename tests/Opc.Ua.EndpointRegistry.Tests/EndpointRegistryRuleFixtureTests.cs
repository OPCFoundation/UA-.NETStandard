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
                Assert.That(header.GetProperty("format").GetString(), Is.EqualTo("EndpointRegistryRuleFixtures/1.0"));
                Assert.That(header.GetProperty("commit").GetString(), Has.Length.EqualTo(40));
                Assert.That(s_document.Value.RootElement.GetProperty("cases").GetArrayLength(), Is.EqualTo(65));
            });
        }

        [TestCaseSource(nameof(Cases))]
        public void CSharpRulesMatchPythonOutcome(string id, string rawCase)
        {
            using JsonDocument document = JsonDocument.Parse(rawCase);
            JsonElement fixture = document.RootElement;
            RegistryObjectValueDataType value = (RegistryObjectValueDataType)RegistryValues.Parse(
                Encoding.UTF8.GetBytes(fixture.GetProperty("document").GetRawText()));

            RegistryRuleException? error = null;
            try
            {
                Validate(fixture.GetProperty("function").GetString()!, value);
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
            var counts = s_document.Value.RootElement.GetProperty("cases").EnumerateArray()
                .GroupBy(item => item.GetProperty("function").GetString()!)
                .ToDictionary(group => group.Key, group => group.Count());

            Assert.Multiple(() =>
            {
                Assert.That(counts["validate_message"], Is.EqualTo(28));
                Assert.That(counts["validate_endpoint"], Is.EqualTo(15));
                Assert.That(counts["validate_media"], Is.EqualTo(14));
                Assert.That(counts["validate_registry"], Is.EqualTo(3));
                Assert.That(counts["validate_message_registry"], Is.EqualTo(3));
                Assert.That(counts["validate_group"], Is.EqualTo(2));
            });
        }

        private static void Validate(string function, RegistryObjectValueDataType value)
        {
            switch (function)
            {
                case "validate_message":
                    EndpointRegistryRules.ValidateMessage(value);
                    return;
                case "validate_group":
                    EndpointRegistryRules.ValidateMessageGroup(value);
                    return;
                case "validate_endpoint":
                    EndpointRegistryRules.ValidateEndpoint(value);
                    return;
                case "validate_media":
                    EndpointRegistryRules.ValidateEndpoint(value, true);
                    return;
                case "validate_registry":
                    EndpointRegistryRules.ValidateRegistry(value);
                    return;
                case "validate_message_registry":
                    EndpointRegistryRules.ValidateMessageRegistry(value);
                    return;
                default:
                    Assert.Fail("Unknown fixture function " + function);
                    return;
            }
        }

        private static readonly System.Lazy<JsonDocument> s_document = new(() => JsonDocument.Parse(
            File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Assets", "rule-fixtures.json"))));
    }
}
