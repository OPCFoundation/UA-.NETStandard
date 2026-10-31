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
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Tests.Conformance
{
    [TestFixture]
    public sealed class XRegistryCoreOracleTests
    {
        [Test]
        public async Task ZeroEpochDiffersFromAbsentAndNullEpochAsync()
        {
            await RunOracleAsync("epoch-presence").ConfigureAwait(false);
        }

        [Test]
        public async Task CreateIgnoresASuppliedEpochAsync()
        {
            await RunOracleAsync("create-epoch").ConfigureAwait(false);
        }

        [Test]
        public async Task IdenticalPutAndEmptyPatchAdvanceEpochAndModifiedAtAsync()
        {
            await RunOracleAsync("identical-touch").ConfigureAwait(false);
        }

        [Test]
        public async Task ParentEpochsTrackMembershipNotDescendantUpdatesAsync()
        {
            await RunOracleAsync("parent-membership").ConfigureAwait(false);
        }

        [Test]
        public async Task FailedCompoundWritePreservesMetadataDocumentsAndMembershipAsync()
        {
            await RunOracleAsync("compound-rollback").ConfigureAwait(false);
        }

        [Test]
        public async Task OmittedDocumentPreservesBytesButExplicitEmptyReplacesThemAsync()
        {
            await RunOracleAsync("omitted-empty-document").ConfigureAwait(false);
        }

        [Test]
        public async Task DocumentlessAndEmptyDocumentResourcesHaveDifferentBodiesAsync()
        {
            await RunOracleAsync("documentless-empty").ConfigureAwait(false);
        }

        [Test]
        public async Task MissingResourceIsNotASuccessfulEmptyDocumentAsync()
        {
            await RunOracleAsync("missing-not-empty").ConfigureAwait(false);
        }

        private static async Task RunOracleAsync(string id)
        {
            using JsonDocument fixture = XRegistryConformanceAssets.Read("core-provider-oracles.json");
            JsonElement root = fixture.RootElement;
            JsonElement scenario = root.GetProperty("cases").EnumerateArray()
                .Single(value => value.GetProperty("id").GetString() == id);
            var clock = new OracleTimeProvider(root.GetProperty("initialTime").GetDateTimeOffset());
            using var endpoint = new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                RegistryId = XRegistryConformanceAssets.Text(root, "registryId"),
                PublicRoot = new Uri(XRegistryConformanceAssets.Text(root, "publicRoot")),
                Model = root.GetProperty("model")
            }, new InMemoryXRegistryTransactionStore(), clock);

            int index = 0;
            foreach (JsonElement step in scenario.GetProperty("steps").EnumerateArray())
            {
                if (step.TryGetProperty("at", out JsonElement timestamp))
                {
                    clock.UtcNow = timestamp.GetDateTimeOffset();
                }
                XRegistryAction action = XRegistryConformanceAssets.Text(step, "action") switch
                {
                    "GET" => XRegistryAction.Read,
                    "PUT" => XRegistryAction.Replace,
                    "PATCH" => XRegistryAction.Merge,
                    "POST" => XRegistryAction.Create,
                    "DELETE" => XRegistryAction.Delete,
                    _ => throw new InvalidOperationException("An oracle contains an unknown action.")
                };
                XRegistryRequest request = Request(step, action);
                XRegistryResponse response = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
                string context = $"{id}, step {++index}, {request.Action} {request.Path}";
                AssertResponse(response, step.GetProperty("expect"), context);

                if (step.TryGetProperty("probes", out JsonElement probes))
                {
                    foreach (JsonElement probe in probes.EnumerateArray())
                    {
                        XRegistryRequest read = Request(probe, XRegistryAction.Read);
                        XRegistryResponse observed = await endpoint.ExecuteAsync(read).ConfigureAwait(false);
                        AssertResponse(observed, probe.GetProperty("expect"), $"{context}, probe {read.Path}");
                    }
                }
            }
        }

        private static XRegistryRequest Request(JsonElement input, XRegistryAction action)
        {
            return new XRegistryRequest(action, XRegistryConformanceAssets.Text(input, "path"))
            {
                View = input.TryGetProperty("view", out JsonElement view) ? view.GetString() switch
                {
                    "document" => XRegistryView.Default,
                    "metadata" => XRegistryView.Metadata,
                    _ => throw new InvalidOperationException("An oracle contains an unknown view.")
                } : XRegistryView.Metadata,
                Metadata = input.TryGetProperty("metadata", out JsonElement metadata) ? metadata : default,
                Document = input.TryGetProperty("documentBase64", out _)
                    ? ByteString.From(Convert.FromBase64String(
                        XRegistryConformanceAssets.Text(input, "documentBase64")))
                    : default,
                ContentType = input.TryGetProperty("contentType", out JsonElement type) ? type.GetString() : null,
                Context = new XRegistryCallContext("conformance-writer")
                {
                    IsAuthenticated = true,
                    Roles = ["xregistry.write"]
                }
            };
        }

        private static void AssertResponse(XRegistryResponse actual, JsonElement expected, string context)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.StatusCode, Is.EqualTo(expected.GetProperty("status").GetInt32()), context);
                Assert.That(actual.Error?.Code, Is.EqualTo(expected.GetProperty("error").GetString()), context);
                if (expected.TryGetProperty("metadata", out JsonElement metadata))
                {
                    AssertMetadata(actual.Metadata, metadata, context);
                }
                if (expected.TryGetProperty("absentMetadata", out JsonElement absent))
                {
                    foreach (JsonElement name in absent.EnumerateArray())
                    {
                        Assert.That(actual.Metadata.TryGetProperty(name.GetString()!, out _), Is.False,
                            $"{context}, unexpected metadata {name}");
                    }
                }
                if (expected.TryGetProperty("documentBase64", out JsonElement bytes))
                {
                    Assert.That(actual.Document.IsNull, Is.EqualTo(bytes.ValueKind == JsonValueKind.Null), context);
                    if (bytes.ValueKind != JsonValueKind.Null)
                    {
                        Assert.That(actual.Document.ToArray(),
                            Is.EqualTo(Convert.FromBase64String(bytes.GetString()!)), context);
                    }
                }
                if (expected.TryGetProperty("contentType", out JsonElement contentType))
                {
                    Assert.That(actual.ContentType, Is.EqualTo(contentType.GetString()), context);
                }
                if (expected.TryGetProperty("location", out JsonElement location))
                {
                    Assert.That(actual.Location, Is.EqualTo(location.GetString()), context);
                }
                if (expected.TryGetProperty("contentLocation", out JsonElement contentLocation))
                {
                    Assert.That(actual.ContentLocation, Is.EqualTo(contentLocation.GetString()), context);
                }
            });
        }

        private static void AssertMetadata(JsonElement actual, JsonElement expected, string context)
        {
            Assert.That(actual.ValueKind, Is.EqualTo(expected.ValueKind), context);
            if (expected.ValueKind == JsonValueKind.Object)
            {
                if (!expected.EnumerateObject().Any())
                {
                    Assert.That(actual.EnumerateObject(), Is.Empty, context);
                }
                foreach (JsonProperty property in expected.EnumerateObject())
                {
                    AssertMetadata(actual.GetProperty(property.Name), property.Value, $"{context}.{property.Name}");
                }
            }
            else
            {
                Assert.That(actual.GetRawText(), Is.EqualTo(expected.GetRawText()), context);
            }
        }

        private sealed class OracleTimeProvider(DateTimeOffset initialTime) : TimeProvider
        {
            public DateTimeOffset UtcNow { get; set; } = initialTime;

            public override DateTimeOffset GetUtcNow()
            {
                return UtcNow;
            }
        }
    }
}
