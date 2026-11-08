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

using System;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;
using static Opc.Ua.XRegistry.Tests.ProtocolProvider.XRegistryProviderCoverage;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryDocumentValidationTests
    {
        [TestCase("JSON/1.0", "{\"valid\":true}", 201)]
        [TestCase("json/1.0", "[1,2,3]", 201)]
        [TestCase("JSON/1.0", "{broken", 400)]
        [TestCase("JSON/1.0", "", 400)]
        [TestCase("XML/1.0", "<root/>", 201)]
        [TestCase("XML/1.0", "<broken>", 400)]
        [TestCase("XML/1.0", "<!DOCTYPE root SYSTEM 'https://not-contacted.example/entity'><root/>", 400)]
        public async Task SyntaxValidationAcceptsOnlyWellFormedDocumentsAsync(string format, string bytes, int status)
        {
            using XRegistryTransactionalEndpoint endpoint = Create(k_model);
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, k_version,
                new JsonObject { ["format"] = format }.ToJsonString()) with
            { Document = ByteString.From(Encoding.UTF8.GetBytes(bytes)) }).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(status), response.Error?.Detail);
            if (status == 201)
            {
                Assert.That(response.Metadata.GetProperty("formatvalidated").GetBoolean(), Is.True);
                Assert.That(response.Metadata.TryGetProperty("formatvalidatedreason", out _), Is.False);
            }
            else
            {
                Assert.That(response.Error?.Code, Is.EqualTo("format_violation"));
                Assert.That(
                    (await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups")).ConfigureAwait(false))
                    .Metadata.GetRawText(), Is.EqualTo("{}"));
            }
        }

        [TestCase(false, false, 201, null)]
        [TestCase(true, false, 400, "format_unknown")]
        [TestCase(false, true, 201, null)]
        [TestCase(true, true, 400, "format_external")]
        public async Task UnsupportedOrExternalFormatsAreNeverReportedAsValidatedAsync(
            bool strict, bool external, int status, string? code)
        {
            using XRegistryTransactionalEndpoint endpoint = Create(k_model.Replace(
                "\"strictvalidation\":false", "\"strictvalidation\":" + (strict ? "true" : "false"),
                StringComparison.Ordinal));
            var metadata = new JsonObject { ["format"] = external ? "JSON/1.0" : "JsonSchema/draft-07" };
            if (external)
            {
                metadata["schemaurl"] = "https://not-contacted.example/document";
            }
            XRegistryResponse response = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, k_version, metadata.ToJsonString())).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(status), response.Error?.Detail);
                Assert.That(response.Error?.Code, Is.EqualTo(code));
                if (status == 201)
                {
                    Assert.That(response.Metadata.GetProperty("formatvalidated").GetBoolean(), Is.False);
                    Assert.That(response.Metadata.GetProperty("formatvalidatedreason").GetString(), Is.Not.Empty);
                }
            });
        }

        [Test]
        public async Task CompatibilityViolationCannotChangeRetainedVersionsOrTheirMetaAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(k_model);
            XRegistryResponse created =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g/schemas/r",
                /*lang=json,strict*/ """
                {"versions":{"v1":{"format":"JSON/1.0","schema":{"value":1}},
                             "v2":{"format":"JSON/1.0","schema":{"value":1}}},
                 "meta":{"compatibility":"identical"}}
                """)).ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, k_version,
                /*lang=json,strict*/ """{"format":"JSON/1.0","schema":{"value":2}}""")).ConfigureAwait(false);
            XRegistryResponse retained = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, k_version))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.Error?.Code, Is.EqualTo("compatibility_violation"), rejected.Error?.Detail);
                Assert.That(retained.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(retained.Metadata.GetProperty("compatibilityvalidated").GetBoolean(), Is.True);
            });
        }

        [Test]
        public async Task MissingFormatDisablesChecksEvenWithStrictValidationAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(k_model.Replace(
                "\"strictvalidation\":false", "\"strictvalidation\":true", StringComparison.Ordinal));
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, k_version, "{}"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(201), response.Error?.Detail);
                Assert.That(response.Metadata.TryGetProperty("formatvalidated", out _), Is.False);
                Assert.That(response.Metadata.TryGetProperty("compatibilityvalidated", out _), Is.False);
            });
        }

        private const string k_version = "/groups/g/schemas/r/versions/v1";

        private const string k_model = /*lang=json,strict*/ """
            {"groups":{"groups":{"singular":"group","resources":{"schemas":{
              "singular":"schema","validateformat":true,"validatecompatibility":true,"strictvalidation":false
            }}}}}
            """;
    }
}
