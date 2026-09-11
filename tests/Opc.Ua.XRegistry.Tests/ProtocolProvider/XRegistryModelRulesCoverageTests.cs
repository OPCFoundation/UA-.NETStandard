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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryModelRulesCoverageTests
    {
        [TestCase("""{"type":"boolean"}""", "true")]
        [TestCase("""{"type":"integer"}""", "-9007199254740993")]
        [TestCase("""{"type":"uinteger"}""", "184467440737095516160")]
        [TestCase("""{"type":"decimal"}""", "-1.25")]
        [TestCase("""{"type":"string","enum":["alpha","beta"]}""", "\"alpha\"")]
        [TestCase("""{"type":"uriabsolute"}""", "\"https://example.test/value\"")]
        [TestCase("""{"type":"urlrelative"}""", "\"child/value\"")]
        [TestCase("""{"type":"uri"}""", "\"urn:example:value\"")]
        [TestCase("""{"type":"url"}""", "\"relative\"")]
        [TestCase("""{"type":"uritemplate"}""", "\"https://example.test/{id}\"")]
        [TestCase("""{"type":"xid"}""", "\"/groups/g\"")]
        [TestCase("""{"type":"xidtype"}""", "\"/groups\"")]
        [TestCase("""{"type":"array","item":{"type":"integer"}}""", "[1,-2,3]")]
        [TestCase("""{"type":"array","item":{"type":"integer"}}""", "[]")]
        [TestCase("""{"type":"map","item":{"type":"integer"}}""", """{"a":1,"z-1":2}""")]
        [TestCase("""{"type":"map","item":{"type":"integer"}}""", "{}")]
        [TestCase("""{"type":"any"}""", """{"unrestricted":[null,true,3]}""")]
        public async Task TypedAttributesRetainIndependentLiteralValuesAsync(string definition, string value)
        {
            using var endpoint = CreateWithAttributes("""{"value":""" + definition + "}");
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g", """{"value":""" + value + "}")).ConfigureAwait(false);
            XRegistryResponse read = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/groups/g")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(read.Metadata.GetProperty("value").GetRawText(), Is.EqualTo(value));
                Assert.That(read.Metadata.GetProperty("groupid").GetString(), Is.EqualTo("g"));
                Assert.That(read.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            });
        }

        [TestCase("""{"type":"boolean"}""", "\"true\"")]
        [TestCase("""{"type":"integer"}""", "1.5")]
        [TestCase("""{"type":"uinteger"}""", "-1")]
        [TestCase("""{"type":"uinteger"}""", "1.5")]
        [TestCase("""{"type":"decimal"}""", "\"1.5\"")]
        [TestCase("""{"type":"string"}""", "true")]
        [TestCase("""{"type":"array","item":{"type":"integer"}}""", "{}")]
        [TestCase("""{"type":"array","item":{"type":"integer"}}""", "[null]")]
        [TestCase("""{"type":"array","item":{"type":"integer"}}""", "[\"wrong\"]")]
        [TestCase("""{"type":"map","item":{"type":"integer"}}""", """{"a":null}""")]
        [TestCase("""{"type":"map","item":{"type":"integer"}}""", """{"a":"wrong"}""")]
        [TestCase("""{"type":"map","item":{"type":"integer"}}""", """{"":1}""")]
        [TestCase("""{"type":"map","item":{"type":"integer"}}""", """{"Upper":1}""")]
        [TestCase("""{"type":"map","item":{"type":"integer"}}""", """{"-leading":1}""")]
        [TestCase("""{"type":"uriabsolute"}""", "\"relative/path\"")]
        [TestCase("""{"type":"urirelative"}""", "\"https://example.test/absolute\"")]
        [TestCase("""{"type":"timestamp"}""", "\"2026-01-02T03:04:05.Z\"")]
        [TestCase("""{"type":"timestamp"}""", "\"2026-01-02T03:04:05.aZ\"")]
        [TestCase("""{"type":"timestamp"}""", "\"2026-01-02T03:04:05+0x:00\"")]
        [TestCase("""{"type":"timestamp"}""", "\"2026-01-02T03:04:05+00-00\"")]
        [TestCase("""{"type":"timestamp"}""", "\"2026-01-02T03:04:0xZ\"")]
        [TestCase("""{"type":"timestamp"}""", "\"2026-02-30T03:04:05Z\"")]
        public async Task InvalidTypedValuesCannotCreateAnEntityOrTouchItsParentAsync(string definition, string value)
        {
            using var endpoint = CreateWithAttributes("""{"value":""" + definition + "}");
            XRegistryResponse rejected = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g", """{"value":""" + value + "}")).ConfigureAwait(false);
            await XRegistryProviderCoverage.AssertPristineAsync(endpoint, rejected, "invalid_attribute")
                .ConfigureAwait(false);
        }

        [Test]
        public async Task NestedPatchRemovesNullsKeepsSiblingsAndReappliesScalarDefaultsAsync()
        {
            using var endpoint = CreateWithAttributes("""
                {"settings":{"type":"object","required":true,"attributes":{
                "enabled":{"type":"boolean","required":true},
                "retries":{"type":"integer","required":true,"default":3},
                "obsolete":{"type":"string"},
                "child":{"type":"object","attributes":{
                "mode":{"type":"string","required":true,"default":"closed"},"note":{"type":"string"}}}}}}
                """);
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g",
                """
                {"settings":{"enabled":true,"retries":9,"obsolete":"remove",
                "child":{"mode":"open","note":"keep"}}}
                """)).ConfigureAwait(false);
            XRegistryResponse patched = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, "/groups/g",
                """{"settings":{"retries":null,"obsolete":null,"child":{"mode":null}}}""")).ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, "/groups/g", """{"settings":{"enabled":null}}""")).ConfigureAwait(false);
            XRegistryResponse retained = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/groups/g")).ConfigureAwait(false);
            XRegistryResponse replaced = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g", """{"settings":{"enabled":false,"child":{}}}"""))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(patched.Metadata.GetProperty("settings").GetProperty("enabled").GetBoolean(), Is.True);
                Assert.That(patched.Metadata.GetProperty("settings").GetProperty("retries").GetInt32(), Is.EqualTo(3));
                Assert.That(patched.Metadata.GetProperty("settings").TryGetProperty("obsolete", out _), Is.False);
                Assert.That(patched.Metadata.GetProperty("settings").GetProperty("child").GetProperty("mode")
                    .GetString(), Is.EqualTo("closed"));
                Assert.That(patched.Metadata.GetProperty("settings").GetProperty("child").GetProperty("note")
                    .GetString(), Is.EqualTo("keep"));
                Assert.That(rejected.Error?.Code, Is.EqualTo("invalid_attribute"));
                Assert.That(retained.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                Assert.That(retained.Metadata.GetProperty("settings").GetProperty("enabled").GetBoolean(), Is.True);
                Assert.That(replaced.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(2));
                Assert.That(replaced.Metadata.GetProperty("settings").GetProperty("enabled").GetBoolean(), Is.False);
                Assert.That(replaced.Metadata.GetProperty("settings").GetProperty("child")
                    .TryGetProperty("note", out _),
                    Is.False);
            });
        }

        [Test]
        public async Task ReadonlyAndImmutableDefaultsSurviveReplacementAndIgnoreCallerValuesAsync()
        {
            using var endpoint = CreateWithAttributes("""
                {"server":{"type":"string","required":true,"readonly":true,"default":"owned"},
                "fixed":{"type":"integer","required":true,"immutable":true,"default":7},
                "settings":{"type":"object","attributes":{
                "server":{"type":"string","required":true,"readonly":true,"default":"nested"},
                "fixed":{"type":"integer","required":true,"immutable":true,"default":11},
                "editable":{"type":"string"}}}}
                """);
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g",
                """
                {"server":false,"fixed":"ignored","settings":{
                "server":0,"fixed":false,"editable":"remove on replace"}}
                """)).ConfigureAwait(false);
            XRegistryResponse replaced = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g",
                """{"server":null,"fixed":null,"settings":{"server":null,"fixed":null}}""")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(created.Metadata.GetProperty("server").GetString(), Is.EqualTo("owned"));
                Assert.That(created.Metadata.GetProperty("fixed").GetInt32(), Is.EqualTo(7));
                Assert.That(replaced.StatusCode, Is.EqualTo(200));
                Assert.That(replaced.Metadata.GetProperty("server").GetString(), Is.EqualTo("owned"));
                Assert.That(replaced.Metadata.GetProperty("fixed").GetInt32(), Is.EqualTo(7));
                Assert.That(replaced.Metadata.GetProperty("settings").GetProperty("server").GetString(),
                    Is.EqualTo("nested"));
                Assert.That(replaced.Metadata.GetProperty("settings").GetProperty("fixed").GetInt32(), Is.EqualTo(11));
                Assert.That(replaced.Metadata.GetProperty("settings").TryGetProperty("editable", out _), Is.False);
                Assert.That(replaced.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task MapPatchMergesNestedEntriesAndDeletesNullMembersAsync()
        {
            using var endpoint = CreateWithAttributes("""
                {"mapping":{"type":"map","item":{"type":"object","attributes":{
                "left":{"type":"string"},"right":{"type":"string"}}}}}
                """);
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g",
                """{"mapping":{"a":{"left":"old","right":"keep"},"b":{"left":"remove"}}}""")).ConfigureAwait(false);
            XRegistryResponse patched = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, "/groups/g",
                """{"mapping":{"a":{"left":"new"},"b":null,"c":{"right":"inserted"}}}""")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(patched.StatusCode, Is.EqualTo(200));
                Assert.That(patched.Metadata.GetProperty("mapping").GetRawText(),
                    Is.EqualTo("""{"a":{"left":"new","right":"keep"},"c":{"right":"inserted"}}"""));
                Assert.That(patched.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
            });
        }

        [TestCase(true, 400)]
        [TestCase(false, 201)]
        public async Task EnumStrictnessControlsUnlistedValuesAsync(bool strict, int expectedStatus)
        {
            using var endpoint = CreateWithAttributes(
                """{"value":{"type":"string","enum":["listed"],"strict":""" + (strict ? "true" : "false") + "}}");
            XRegistryResponse response = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g", """{"value":"unlisted"}""")).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
            if (strict)
            {
                await XRegistryProviderCoverage.AssertPristineAsync(endpoint, response, "invalid_attribute")
                    .ConfigureAwait(false);
            }
            else
            {
                Assert.That(response.Metadata.GetProperty("value").GetString(), Is.EqualTo("unlisted"));
            }
        }

        [Test]
        public async Task OmittedStrictFlagRejectsAnUnlistedEnumValueAsync()
        {
            using var endpoint = CreateWithAttributes("""{"value":{"type":"string","enum":["listed"]}}""");
            XRegistryResponse response = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g", """{"value":"unlisted"}""")).ConfigureAwait(false);
            await XRegistryProviderCoverage.AssertPristineAsync(endpoint, response, "invalid_attribute")
                .ConfigureAwait(false);
        }

        [TestCase("{}")]
        [TestCase("""{"groups":{"groups":{"singular":"group","plural":"different"}}}""")]
        [TestCase("""{"groups":{"Upper":{"singular":"group"}}}""")]
        [TestCase("""{"groups":{},"xinclude":[]}""")]
        [TestCase("""{"groups":{},"typemap":{}}""")]
        [TestCase("""{"groups":{"groups":{"singular":"group","ximportresources":[]}}}""")]
        public void UnsupportedModelStructureIsRejectedBeforeOpeningStorage(string model)
        {
            ArgumentException? error = Assert.Throws<ArgumentException>(() =>
            {
                using var endpoint = XRegistryProviderCoverage.Create(model);
            });
            Assert.Multiple(() =>
            {
                Assert.That(error?.ParamName, Is.EqualTo("options"));
                Assert.That(error?.InnerException, Is.InstanceOf<XRegistryRejectionException>());
            });
        }

        [TestCase("""{"type":"string","ifvalues":{}}""", "invalid_model")]
        [TestCase("""{"type":"xid","target":"/groups"}""", "invalid_model")]
        [TestCase("""{"type":"array"}""", "invalid_model")]
        [TestCase("""{"type":"map","item":null}""", "invalid_model")]
        [TestCase("""{"type":"string","default":"missing-required"}""", "model_required_true")]
        [TestCase("""{"type":"integer","required":true,"default":"wrong"}""", "invalid_attribute")]
        [TestCase("""{"type":"array","required":true,"default":[]}""", "model_scalar_default")]
        [TestCase("""{"type":"map","required":true,"default":{}}""", "model_scalar_default")]
        [TestCase("""{"type":"object","required":true,"default":{}}""", "model_scalar_default")]
        [TestCase("""{"type":"any","required":true,"default":true}""", "model_scalar_default")]
        [TestCase("""{"type":"unknown","required":true,"default":1}""", "invalid_model")]
        public void UnsupportedAttributeDefinitionsProduceQualifiedModelRejections(string definition, string code)
        {
            ArgumentException? error = Assert.Throws<ArgumentException>(() =>
            {
                using var endpoint = CreateWithAttributes("""{"value":""" + definition + "}");
            });
            if (error?.InnerException is not XRegistryRejectionException rejection)
            {
                throw new AssertionException("Expected a qualified model rejection as the constructor's cause.");
            }
            Assert.That(rejection.Code, Is.EqualTo(code));
        }

        [TestCase("versionmode", "\"unknown\"")]
        [TestCase("hasdocument", "\"true\"")]
        [TestCase("maxversions", "-1")]
        [TestCase("validateformat", "true")]
        [TestCase("validatecompatibility", "true")]
        [TestCase("strictvalidation", "true")]
        [TestCase("ximport", "[]")]
        public void UnsupportedResourceFeaturesAreRejectedAtConstruction(string name, string value)
        {
            string model = """
                {"groups":{"groups":{"singular":"group","resources":{"schemas":{"singular":"schema",
                """ + "\"" + name + "\":" + value + "}}}}}";
            ArgumentException? error = Assert.Throws<ArgumentException>(() =>
            {
                using var endpoint = XRegistryProviderCoverage.Create(model);
            });
            Assert.Multiple(() =>
            {
                Assert.That(error?.ParamName, Is.EqualTo("options"));
                Assert.That(error?.InnerException, Is.InstanceOf<XRegistryRejectionException>());
            });
        }

        private static XRegistryTransactionalEndpoint CreateWithAttributes(string attributes)
        {
            return XRegistryProviderCoverage.Create(
                """{"groups":{"groups":{"singular":"group","resources":{},"attributes":""" + attributes + "}}}");
        }
    }
}
