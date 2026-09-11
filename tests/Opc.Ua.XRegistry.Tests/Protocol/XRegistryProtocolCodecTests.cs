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
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Tests.Protocol
{
    [TestFixture]
    [Category("XRegistryProtocol")]
    public sealed class XRegistryProtocolCodecTests
    {
        [Test]
        public void EncodeRequestMatchesIndependentEnvelopeAndOmitsCallerContext()
        {
            using JsonDocument metadata = JsonDocument.Parse(k_metadata);
            var request = new XRegistryRequest(XRegistryAction.Merge, "/groups/g")
            {
                View = XRegistryView.Metadata,
                Metadata = metadata.RootElement,
                Document = ByteString.From([0, 1, 255, 128]),
                ContentType = "application/octet-stream",
                OperationId = "op-7",
                Parameters = [new("filter", "a=1"), new("filter", null), new("sort", string.Empty)],
                Context = new XRegistryCallContext("host-only")
                {
                    Authority = "trusted-issuer",
                    IsAuthenticated = true,
                    Roles = ["writer"],
                    SessionId = "session-9"
                }
            };

            ByteString encoded = new XRegistryProtocolCodec().EncodeRequest(request);

            Assert.That(Encoding.UTF8.GetString(encoded.Span.ToArray()), Is.EqualTo(k_request));
            Assert.That(encoded.Length, Is.EqualTo(337));
        }

        [Test]
        public void DecodeRequestRetainsLiteralFieldsAndRepeatedParameterOrderAfterDocumentDisposal()
        {
            var context = new XRegistryCallContext("verified-reader");

            XRegistryRequest request = new XRegistryProtocolCodec().DecodeRequest(Utf8(k_request), context);

            Assert.That(request.Action, Is.EqualTo(XRegistryAction.Merge));
            Assert.That(request.Path, Is.EqualTo("/groups/g"));
            Assert.That(request.View, Is.EqualTo(XRegistryView.Metadata));
            Assert.That(request.IsMutation, Is.True);
            Assert.That(request.Metadata.GetProperty("epoch").GetRawText(), Is.EqualTo("4294967296"));
            Assert.That(request.Metadata.GetProperty("meta").GetProperty("epoch").GetInt32(), Is.Zero);
            Assert.That(request.Metadata.GetProperty("enabled").GetBoolean(), Is.True);
            Assert.That(request.Metadata.GetProperty("tags")[0].GetInt32(), Is.EqualTo(2));
            Assert.That(request.Metadata.GetProperty("tags")[1].GetString(), Is.EqualTo("x"));
            Assert.That(request.Metadata.GetProperty("nullable").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(request.Document, Is.EqualTo(ByteString.From([0, 1, 255, 128])));
            Assert.That(request.ContentType, Is.EqualTo("application/octet-stream"));
            Assert.That(request.OperationId, Is.EqualTo("op-7"));
            Assert.That(request.Context, Is.SameAs(context));
            Assert.That(request.Parameters.Count, Is.EqualTo(3));
            Assert.That(request.Parameters[0].Name, Is.EqualTo("filter"));
            Assert.That(request.Parameters[0].Value, Is.EqualTo("a=1"));
            Assert.That(request.Parameters[1].Name, Is.EqualTo("filter"));
            Assert.That(request.Parameters[1].Value, Is.Null);
            Assert.That(request.Parameters[2].Name, Is.EqualTo("sort"));
            Assert.That(request.Parameters[2].Value, Is.Empty);
        }

        [Test]
        public void EncodeResponseMatchesIndependentEnvelopeIncludingErrorAndLinks()
        {
            using JsonDocument metadata = JsonDocument.Parse("""{"epoch":0,"resourceid":"r"}""");
            var response = new XRegistryResponse(400)
            {
                Metadata = metadata.RootElement,
                Document = ByteString.From([0, 255]),
                ContentType = "application/json",
                Location = "/groups/g/resources/r",
                ContentLocation = "/groups/g/resources/r/versions/v1",
                CorrelationId = "event-4",
                Error = new XRegistryError("mismatched_epoch", "Expected epoch 0.") { Subject = "/groups/g" },
                Links = [new("self", "/groups/g"), new("alternate", "/groups/g/resources/r")],
                AllowedActions = [XRegistryAction.Read, XRegistryAction.Describe]
            };

            ByteString encoded = new XRegistryProtocolCodec().EncodeResponse(response);

            Assert.That(Encoding.UTF8.GetString(encoded.Span.ToArray()), Is.EqualTo(k_response));
        }

        [Test]
        public void DecodeResponseRetainsLiteralErrorLinksAndBodyAfterDocumentDisposal()
        {
            XRegistryResponse response = new XRegistryProtocolCodec().DecodeResponse(Utf8(k_response));

            Assert.That(response.StatusCode, Is.EqualTo(400));
            Assert.That(response.IsSuccess, Is.False);
            Assert.That(response.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            Assert.That(response.Metadata.GetProperty("resourceid").GetString(), Is.EqualTo("r"));
            Assert.That(response.Document, Is.EqualTo(ByteString.From([0, 255])));
            Assert.That(response.ContentType, Is.EqualTo("application/json"));
            Assert.That(response.Location, Is.EqualTo("/groups/g/resources/r"));
            Assert.That(response.ContentLocation, Is.EqualTo("/groups/g/resources/r/versions/v1"));
            Assert.That(response.CorrelationId, Is.EqualTo("event-4"));
            Assert.That(response.Error, Is.Not.Null);
            Assert.That(response.Error!.Code, Is.EqualTo("mismatched_epoch"));
            Assert.That(response.Error.Detail, Is.EqualTo("Expected epoch 0."));
            Assert.That(response.Error.Subject, Is.EqualTo("/groups/g"));
            Assert.That(response.Links.Count, Is.EqualTo(2));
            Assert.That(response.Links[0].Relation, Is.EqualTo("self"));
            Assert.That(response.Links[0].Target, Is.EqualTo("/groups/g"));
            Assert.That(response.Links[1].Relation, Is.EqualTo("alternate"));
            Assert.That(response.Links[1].Target, Is.EqualTo("/groups/g/resources/r"));
            Assert.That(response.AllowedActions.Count, Is.EqualTo(2));
            Assert.That(response.AllowedActions[0], Is.EqualTo(XRegistryAction.Read));
            Assert.That(response.AllowedActions[1], Is.EqualTo(XRegistryAction.Describe));
        }

        [Test]
        public void EncodeDescriptionMatchesIndependentEnvelope()
        {
            using JsonDocument model = JsonDocument.Parse("""{"groups":{"devices":{}}}""");
            using JsonDocument capabilities = JsonDocument.Parse("""{"flags":["filter"]}""");
            var description = new XRegistryEndpointDescription("registry-a")
            {
                Profile = "transactional-v1",
                Model = model.RootElement,
                Capabilities = capabilities.RootElement,
                SupportsAtomicMutations = true,
                SupportsConditionalMutations = false,
                SupportsWriteTouch = true,
                SupportsOperationReplay = false
            };

            ByteString encoded = new XRegistryProtocolCodec().EncodeDescription(description);

            Assert.That(Encoding.UTF8.GetString(encoded.Span.ToArray()), Is.EqualTo(k_description));
        }

        [Test]
        public void DecodeDescriptionOwnsLiteralModelAndCapabilitiesAfterDocumentDisposal()
        {
            XRegistryEndpointDescription description = new XRegistryProtocolCodec()
                .DecodeDescription(Utf8(k_description));

            Assert.That(description.RegistryId, Is.EqualTo("registry-a"));
            Assert.That(description.Profile, Is.EqualTo("transactional-v1"));
            Assert.That(description.Model.GetProperty("groups").GetProperty("devices").ValueKind,
                Is.EqualTo(JsonValueKind.Object));
            Assert.That(description.Capabilities.GetProperty("flags").GetArrayLength(), Is.EqualTo(1));
            Assert.That(description.Capabilities.GetProperty("flags")[0].GetString(), Is.EqualTo("filter"));
            Assert.That(description.SupportsAtomicMutations, Is.True);
            Assert.That(description.SupportsConditionalMutations, Is.False);
            Assert.That(description.SupportsWriteTouch, Is.True);
            Assert.That(description.SupportsOperationReplay, Is.False);
        }

        [TestCase(true, false, false, false)]
        [TestCase(false, true, false, false)]
        [TestCase(false, false, true, false)]
        [TestCase(false, false, false, true)]
        public void DescriptionGuaranteesAreEncodedAndDecodedIndependently(
            bool atomic,
            bool conditional,
            bool touch,
            bool replay)
        {
            var codec = new XRegistryProtocolCodec();
            var description = new XRegistryEndpointDescription("r")
            {
                SupportsAtomicMutations = atomic,
                SupportsConditionalMutations = conditional,
                SupportsWriteTouch = touch,
                SupportsOperationReplay = replay
            };
            using JsonDocument encoded = JsonDocument.Parse(codec.EncodeDescription(description).Memory);
            Assert.That(encoded.RootElement.GetProperty("atomicMutations").GetBoolean(), Is.EqualTo(atomic));
            Assert.That(encoded.RootElement.GetProperty("conditionalMutations").GetBoolean(), Is.EqualTo(conditional));
            Assert.That(encoded.RootElement.GetProperty("writeTouch").GetBoolean(), Is.EqualTo(touch));
            Assert.That(encoded.RootElement.GetProperty("operationReplay").GetBoolean(), Is.EqualTo(replay));

            string literal = """{"format":1,"registryId":"r","profile":"unqualified","atomicMutations":""" +
                (atomic ? "true" : "false") + ""","conditionalMutations":""" + (conditional ? "true" : "false") +
                ""","writeTouch":""" + (touch ? "true" : "false") +
                ""","operationReplay":""" + (replay ? "true" : "false") + "}";
            XRegistryEndpointDescription decoded = codec.DecodeDescription(Utf8(literal));
            Assert.That(decoded.SupportsAtomicMutations, Is.EqualTo(atomic));
            Assert.That(decoded.SupportsConditionalMutations, Is.EqualTo(conditional));
            Assert.That(decoded.SupportsWriteTouch, Is.EqualTo(touch));
            Assert.That(decoded.SupportsOperationReplay, Is.EqualTo(replay));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void DescriptionDistinguishesAbsentAndNullModelAndCapabilities(bool nullModel, bool nullCapabilities)
        {
            using JsonDocument document = JsonDocument.Parse("null");
            var description = new XRegistryEndpointDescription("r")
            {
                Model = nullModel ? document.RootElement : default,
                Capabilities = nullCapabilities ? document.RootElement : default
            };
            var codec = new XRegistryProtocolCodec();
            using JsonDocument encoded = JsonDocument.Parse(codec.EncodeDescription(description).Memory);
            Assert.That(encoded.RootElement.TryGetProperty("model", out JsonElement model), Is.EqualTo(nullModel));
            Assert.That(model.ValueKind, Is.EqualTo(nullModel ? JsonValueKind.Null : JsonValueKind.Undefined));
            Assert.That(encoded.RootElement.TryGetProperty("capabilities", out JsonElement capabilities),
                Is.EqualTo(nullCapabilities));
            Assert.That(capabilities.ValueKind,
                Is.EqualTo(nullCapabilities ? JsonValueKind.Null : JsonValueKind.Undefined));

            string literal = "{\"format\":1,\"registryId\":\"r\",\"profile\":\"unqualified\"," +
                "\"atomicMutations\":false,\"conditionalMutations\":false," +
                "\"writeTouch\":false,\"operationReplay\":false" +
                (nullModel ? ",\"model\":null" : string.Empty) +
                (nullCapabilities ? ",\"capabilities\":null" : string.Empty) + "}";
            XRegistryEndpointDescription decoded = codec.DecodeDescription(Utf8(literal));
            Assert.That(decoded.Model.ValueKind, Is.EqualTo(nullModel ? JsonValueKind.Null : JsonValueKind.Undefined));
            Assert.That(decoded.Capabilities.ValueKind,
                Is.EqualTo(nullCapabilities ? JsonValueKind.Null : JsonValueKind.Undefined));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void RequestMetadataAndDocumentPresenceRemainIndependent(bool nullMetadata, bool emptyDocument)
        {
            using JsonDocument metadata = JsonDocument.Parse("null");
            var codec = new XRegistryProtocolCodec();
            var request = new XRegistryRequest(XRegistryAction.Replace, "/")
            {
                Metadata = nullMetadata ? metadata.RootElement : default,
                Document = emptyDocument ? ByteString.Empty : default
            };
            using JsonDocument encoded = JsonDocument.Parse(codec.EncodeRequest(request).Memory);
            Assert.That(encoded.RootElement.TryGetProperty("metadata", out JsonElement encodedMetadata),
                Is.EqualTo(nullMetadata));
            Assert.That(encodedMetadata.ValueKind,
                Is.EqualTo(nullMetadata ? JsonValueKind.Null : JsonValueKind.Undefined));
            Assert.That(encoded.RootElement.TryGetProperty("document", out JsonElement encodedDocument),
                Is.EqualTo(emptyDocument));
            if (emptyDocument)
            {
                Assert.That(encodedDocument.GetString(), Is.Empty);
            }

            string literal = """{"format":1,"action":1,"path":"/","view":0""" +
                (nullMetadata ? ""","metadata":null""" : string.Empty) +
                (emptyDocument ? ",\"document\":\"\"" : string.Empty) + "}";
            XRegistryRequest decoded = codec.DecodeRequest(Utf8(literal), XRegistryCallContext.Anonymous);
            Assert.That(decoded.Metadata.ValueKind,
                Is.EqualTo(nullMetadata ? JsonValueKind.Null : JsonValueKind.Undefined));
            Assert.That(decoded.Document.IsNull, Is.EqualTo(!emptyDocument));
            Assert.That(decoded.Document.Length, Is.Zero);
            Assert.That(decoded.Parameters.Count, Is.Zero);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ResponseMetadataAndDocumentPresenceRemainIndependent(bool nullMetadata, bool emptyDocument)
        {
            using JsonDocument metadata = JsonDocument.Parse("null");
            var codec = new XRegistryProtocolCodec();
            var response = new XRegistryResponse(200)
            {
                Metadata = nullMetadata ? metadata.RootElement : default,
                Document = emptyDocument ? ByteString.Empty : default
            };
            using JsonDocument encoded = JsonDocument.Parse(codec.EncodeResponse(response).Memory);
            Assert.That(encoded.RootElement.TryGetProperty("metadata", out JsonElement encodedMetadata),
                Is.EqualTo(nullMetadata));
            Assert.That(encodedMetadata.ValueKind,
                Is.EqualTo(nullMetadata ? JsonValueKind.Null : JsonValueKind.Undefined));
            Assert.That(encoded.RootElement.TryGetProperty("document", out JsonElement encodedDocument),
                Is.EqualTo(emptyDocument));
            if (emptyDocument)
            {
                Assert.That(encodedDocument.GetString(), Is.Empty);
            }

            string literal = """{"format":1,"status":200""" +
                (nullMetadata ? ""","metadata":null""" : string.Empty) +
                (emptyDocument ? ",\"document\":\"\"" : string.Empty) + "}";
            XRegistryResponse decoded = codec.DecodeResponse(Utf8(literal));
            Assert.That(decoded.Metadata.ValueKind,
                Is.EqualTo(nullMetadata ? JsonValueKind.Null : JsonValueKind.Undefined));
            Assert.That(decoded.Document.IsNull, Is.EqualTo(!emptyDocument));
            Assert.That(decoded.Document.Length, Is.Zero);
            Assert.That(decoded.Error, Is.Null);
            Assert.That(decoded.Links.Count, Is.Zero);
            Assert.That(decoded.AllowedActions.Count, Is.Zero);
        }

        [TestCase("0", "4294967296")]
        [TestCase("4294967296", "0")]
        [TestCase("18446744073709551615", "18446744073709551616")]
        public void EpochNumbersRetainUnsignedWidthAndSeparateResourceMetaCounter(
            string versionEpoch,
            string resourceEpoch)
        {
            string metadataJson = "{\"epoch\":" + versionEpoch + ",\"meta\":{\"epoch\":" + resourceEpoch + "}}";
            using JsonDocument metadata = JsonDocument.Parse(metadataJson);
            var codec = new XRegistryProtocolCodec();
            var request = new XRegistryRequest(XRegistryAction.Merge, "/groups/g")
            {
                Metadata = metadata.RootElement
            };
            using JsonDocument encoded = JsonDocument.Parse(codec.EncodeRequest(request).Memory);
            JsonElement encodedMetadata = encoded.RootElement.GetProperty("metadata");
            Assert.That(encodedMetadata.GetProperty("epoch").ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(encodedMetadata.GetProperty("epoch").GetRawText(), Is.EqualTo(versionEpoch));
            Assert.That(encodedMetadata.GetProperty("meta").GetProperty("epoch").GetRawText(),
                Is.EqualTo(resourceEpoch));

            string literal = """{"format":1,"action":2,"path":"/groups/g","view":0,"metadata":""" + metadataJson + "}";
            XRegistryRequest decoded = codec.DecodeRequest(Utf8(literal), XRegistryCallContext.Anonymous);
            Assert.That(decoded.Metadata.GetProperty("epoch").ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(decoded.Metadata.GetProperty("epoch").GetRawText(), Is.EqualTo(versionEpoch));
            Assert.That(decoded.Metadata.GetProperty("meta").GetProperty("epoch").GetRawText(),
                Is.EqualTo(resourceEpoch));
        }

        [Test]
        public void DecodeRequestUsesOnlySuppliedHostAuthenticationDespiteSpoofedEnvelope()
        {
            const string literal = """
                {"format":1,"action":0,"path":"/","view":0,
                 "context":{"subject":"attacker","authority":"untrusted","isAuthenticated":true,"roles":["admin"]},
                 "subject":"attacker","authority":"untrusted","roles":["admin"],"sessionId":"spoofed"}
                """;
            var context = new XRegistryCallContext("verified-user")
            {
                Authority = "host-issuer",
                IsAuthenticated = true,
                Roles = ["reader"],
                SessionId = "host-session"
            };

            XRegistryRequest request = new XRegistryProtocolCodec().DecodeRequest(Utf8(literal), context);

            Assert.That(request.Context, Is.SameAs(context));
            Assert.That(request.Context.Subject, Is.EqualTo("verified-user"));
            Assert.That(request.Context.Authority, Is.EqualTo("host-issuer"));
            Assert.That(request.Context.IsAuthenticated, Is.True);
            Assert.That(request.Context.Roles.Count, Is.EqualTo(1));
            Assert.That(request.Context.Roles[0], Is.EqualTo("reader"));
            Assert.That(request.Context.SessionId, Is.EqualTo("host-session"));
        }

        [Test]
        public void DecodeRequestRequiresHostContextEvenWhenEnvelopeClaimsAuthentication()
        {
            const string literal = """
                {"format":1,"action":0,"path":"/","view":0,"context":{"subject":"admin","isAuthenticated":true}}
                """;

            Assert.That(() => new XRegistryProtocolCodec().DecodeRequest(Utf8(literal), null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("context"));
        }

        [Test]
        public void RequestDigestMatchesIndependentSha256AndExcludesHostContext()
        {
            using JsonDocument metadata = JsonDocument.Parse(k_metadata);
            var request = new XRegistryRequest(XRegistryAction.Merge, "/groups/g")
            {
                View = XRegistryView.Metadata,
                Metadata = metadata.RootElement,
                Document = ByteString.From([0, 1, 255, 128]),
                ContentType = "application/octet-stream",
                OperationId = "op-7",
                Parameters = [new("filter", "a=1"), new("filter", null), new("sort", string.Empty)]
            };
            var codec = new XRegistryProtocolCodec();

            Assert.That(codec.ComputeRequestDigest(request), Is.EqualTo(k_digest));
            Assert.That(codec.ComputeRequestDigest(request), Is.EqualTo(k_digest));
            Assert.That(codec.ComputeRequestDigest(request with
            {
                Context = new XRegistryCallContext("other-caller")
                {
                    Authority = "other-authority",
                    IsAuthenticated = true,
                    Roles = ["administrator"],
                    SessionId = "other-session"
                }
            }), Is.EqualTo(k_digest));
        }

        [TestCase("action")]
        [TestCase("path")]
        [TestCase("view")]
        [TestCase("metadata-epoch")]
        [TestCase("metadata-meta-epoch")]
        [TestCase("metadata-type")]
        [TestCase("metadata-null")]
        [TestCase("metadata-absent")]
        [TestCase("document-byte")]
        [TestCase("document-empty")]
        [TestCase("document-absent")]
        [TestCase("content-type")]
        [TestCase("content-type-empty")]
        [TestCase("content-type-absent")]
        [TestCase("operation-id")]
        [TestCase("operation-id-empty")]
        [TestCase("operation-id-absent")]
        [TestCase("parameter-name")]
        [TestCase("parameter-value")]
        [TestCase("parameter-null-to-empty")]
        [TestCase("parameter-order")]
        [TestCase("parameter-count")]
        public void RequestDigestChangesForEveryMutationRelevantField(string mutation)
        {
            string metadataJson = mutation switch
            {
                "metadata-epoch" => k_metadata.Replace("4294967296", "4294967297", StringComparison.Ordinal),
                "metadata-meta-epoch" => k_metadata.Replace("\"epoch\":0", "\"epoch\":1", StringComparison.Ordinal),
                "metadata-type" => k_metadata.Replace("true", "\"true\"", StringComparison.Ordinal),
                "metadata-null" => "null",
                _ => k_metadata
            };
            using JsonDocument metadata = JsonDocument.Parse(metadataJson);
            var request = new XRegistryRequest(
                mutation == "action" ? XRegistryAction.Replace : XRegistryAction.Merge,
                mutation == "path" ? "/groups/other" : "/groups/g")
            {
                View = XRegistryView.Metadata,
                Metadata = metadata.RootElement,
                Document = ByteString.From([0, 1, 255, 128]),
                ContentType = "application/octet-stream",
                OperationId = "op-7",
                Parameters = [new("filter", "a=1"), new("filter", null), new("sort", string.Empty)]
            };
            request = mutation switch
            {
                "view" => request with { View = XRegistryView.Default },
                "metadata-absent" => request with { Metadata = default },
                "document-byte" => request with { Document = ByteString.From([0, 1, 255, 129]) },
                "document-empty" => request with { Document = ByteString.Empty },
                "document-absent" => request with { Document = default },
                "content-type" => request with { ContentType = "application/json" },
                "content-type-empty" => request with { ContentType = string.Empty },
                "content-type-absent" => request with { ContentType = null },
                "operation-id" => request with { OperationId = "op-8" },
                "operation-id-empty" => request with { OperationId = string.Empty },
                "operation-id-absent" => request with { OperationId = null },
                "parameter-name" => request with
                {
                    Parameters = [new("inline", "a=1"), new("filter", null), new("sort", string.Empty)]
                },
                "parameter-value" => request with
                {
                    Parameters = [new("filter", "a=2"), new("filter", null), new("sort", string.Empty)]
                },
                "parameter-null-to-empty" => request with
                {
                    Parameters = [new("filter", "a=1"), new("filter", string.Empty), new("sort", string.Empty)]
                },
                "parameter-order" => request with
                {
                    Parameters = [new("filter", null), new("filter", "a=1"), new("sort", string.Empty)]
                },
                "parameter-count" => request with
                {
                    Parameters = [new("filter", "a=1"), new("filter", null)]
                },
                _ => request
            };

            Assert.That(new XRegistryProtocolCodec().ComputeRequestDigest(request), Is.Not.EqualTo(k_digest), mutation);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void ConstructorRejectsNonpositiveByteLimits(int maximumBytes)
        {
            Assert.That(() => new XRegistryProtocolCodec(maximumBytes),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("maximumBytes"));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(1025)]
        public void ConstructorRejectsDepthOutsideInclusiveRange(int maximumDepth)
        {
            Assert.That(() => new XRegistryProtocolCodec(maximumDepth: maximumDepth),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("maximumDepth"));
        }

        [Test]
        public void ConstructorAcceptsInclusiveLimitsAndDefaults()
        {
            var defaults = new XRegistryProtocolCodec();
            Assert.That(defaults.MaximumBytes, Is.EqualTo(33_554_432));
            Assert.That(defaults.MaximumDepth, Is.EqualTo(64));
            var smallest = new XRegistryProtocolCodec(1, 1);
            Assert.That(smallest.EncodeJson(JsonValue.Create(0)!), Is.EqualTo(Utf8("0")));
            var largest = new XRegistryProtocolCodec(int.MaxValue, 1024);
            Assert.That(largest.MaximumBytes, Is.EqualTo(int.MaxValue));
            Assert.That(largest.MaximumDepth, Is.EqualTo(1024));
        }

        [Test]
        public void PublicEncodersRejectNullInputs()
        {
            var codec = new XRegistryProtocolCodec();

            Assert.That(() => codec.EncodeJson(null!), Throws.ArgumentNullException);
            Assert.That(() => codec.EncodeRequest(null!), Throws.ArgumentNullException);
            Assert.That(() => codec.EncodeResponse(null!), Throws.ArgumentNullException);
            Assert.That(() => codec.EncodeDescription(null!), Throws.ArgumentNullException);
            Assert.That(() => codec.ComputeRequestDigest(null!), Throws.ArgumentNullException);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DecodersRejectAbsentAndEmptyMessages(bool empty)
        {
            ByteString data = empty ? ByteString.Empty : default;
            var codec = new XRegistryProtocolCodec();

            Assert.That(() => codec.DecodeRequest(data, XRegistryCallContext.Anonymous),
                Throws.InstanceOf<JsonException>());
            Assert.That(() => codec.DecodeResponse(data), Throws.InstanceOf<JsonException>());
            Assert.That(() => codec.DecodeDescription(data), Throws.InstanceOf<JsonException>());
        }

        [TestCase("null")]
        [TestCase("[]")]
        [TestCase("42")]
        [TestCase("{")]
        [TestCase("{}")]
        [TestCase("""{"format":0}""")]
        [TestCase("""{"format":2}""")]
        [TestCase("""{"format":"1"}""")]
        [TestCase("""{"format":1.5}""")]
        [TestCase("""{"format":4294967296}""")]
        [TestCase("""{"format":1,"format":1}""")]
        [TestCase("""{"format":1,"f\u006frmat":1}""")]
        [TestCase("""{"format":1,"extra":0,"extra":1}""")]
        public void AllDecodersRejectHostileRootEnvelopes(string literal)
        {
            ByteString data = Utf8(literal);
            var codec = new XRegistryProtocolCodec();

            Assert.That(() => codec.DecodeRequest(data, XRegistryCallContext.Anonymous),
                Throws.InstanceOf<JsonException>());
            Assert.That(() => codec.DecodeResponse(data), Throws.InstanceOf<JsonException>());
            Assert.That(() => codec.DecodeDescription(data), Throws.InstanceOf<JsonException>());
        }

        [TestCase("""{"format":1,"path":"/","view":0}""")]
        [TestCase("""{"format":1,"action":null,"path":"/","view":0}""")]
        [TestCase("""{"format":1,"action":"0","path":"/","view":0}""")]
        [TestCase("""{"format":1,"action":0.5,"path":"/","view":0}""")]
        [TestCase("""{"format":1,"action":-1,"path":"/","view":0}""")]
        [TestCase("""{"format":1,"action":6,"path":"/","view":0}""")]
        [TestCase("""{"format":1,"action":4294967296,"path":"/","view":0}""")]
        [TestCase("""{"format":1,"action":0,"path":"/"}""")]
        [TestCase("""{"format":1,"action":0,"path":"/","view":-1}""")]
        [TestCase("""{"format":1,"action":0,"path":"/","view":2}""")]
        [TestCase("""{"format":1,"action":0,"path":"/","view":"0"}""")]
        [TestCase("""{"format":1,"action":0,"path":null,"view":0}""")]
        [TestCase("""{"format":1,"action":0,"view":0}""")]
        [TestCase("""{"format":1,"action":0,"path":"/","view":0,"document":null}""")]
        [TestCase("""{"format":1,"action":0,"path":"/","view":0,"document":false}""")]
        [TestCase("""{"format":1,"action":0,"path":"/","view":0,"document":"invalid!"}""")]
        [TestCase("""{"format":1,"action":0,"path":"/","view":0,"contentType":3}""")]
        [TestCase("""{"format":1,"action":0,"path":"/","view":0,"operationId":{}}""")]
        [TestCase("""{"format":1,"action":0,"path":"/","view":0,"parameters":{}}""")]
        [TestCase("""{"format":1,"action":0,"path":"/","view":0,"parameters":[null]}""")]
        [TestCase("""{"format":1,"action":0,"path":"/","view":0,"parameters":[{}]}""")]
        [TestCase("""{"format":1,"action":0,"path":"/","view":0,"parameters":[{"name":""}]}""")]
        [TestCase("""{"format":1,"action":0,"path":"/","view":0,"parameters":[{"name":"a","value":0}]}""")]
        [TestCase("""{"format":1,"action":0,"path":"/","view":0,"parameters":[{"name":"a","name":"b"}]}""")]
        public void DecodeRequestRejectsMalformedFieldsAndNestedEnvelopeMembers(string literal)
        {
            Assert.That(() => new XRegistryProtocolCodec().DecodeRequest(Utf8(literal), XRegistryCallContext.Anonymous),
                Throws.InstanceOf<JsonException>());
        }

        [TestCase(0, XRegistryAction.Read)]
        [TestCase(1, XRegistryAction.Replace)]
        [TestCase(2, XRegistryAction.Merge)]
        [TestCase(3, XRegistryAction.Create)]
        [TestCase(4, XRegistryAction.Delete)]
        [TestCase(5, XRegistryAction.Describe)]
        public void DecodeRequestAcceptsEveryDefinedActionIncludingBothRangeEndpoints(
            int wireAction,
            XRegistryAction action)
        {
            var envelope = new JsonObject
            {
                ["format"] = 1,
                ["action"] = wireAction,
                ["path"] = "/",
                ["view"] = 0
            };

            XRegistryRequest request = new XRegistryProtocolCodec()
                .DecodeRequest(Utf8(envelope.ToJsonString()), XRegistryCallContext.Anonymous);

            Assert.That(request.Action, Is.EqualTo(action));
            Assert.That(request.View, Is.EqualTo(XRegistryView.Default));
            Assert.That(request.Path, Is.EqualTo("/"));
        }

        [Test]
        public void DecodeParametersPreservesUnknownNamesOmittedNullAndEmptyValuesInOrder()
        {
            const string literal = """
                {"format":1,"action":0,"path":"/","view":0,"parameters":[
                    {"name":"future-flag"},{"name":"future-flag","value":null},{"name":"future-flag","value":""}]}
                """;

            XRegistryRequest request = new XRegistryProtocolCodec()
                .DecodeRequest(Utf8(literal), XRegistryCallContext.Anonymous);

            Assert.That(request.Parameters.Count, Is.EqualTo(3));
            Assert.That(request.Parameters[0].Name, Is.EqualTo("future-flag"));
            Assert.That(request.Parameters[0].Value, Is.Null);
            Assert.That(request.Parameters[1].Name, Is.EqualTo("future-flag"));
            Assert.That(request.Parameters[1].Value, Is.Null);
            Assert.That(request.Parameters[2].Name, Is.EqualTo("future-flag"));
            Assert.That(request.Parameters[2].Value, Is.Empty);
        }

        [TestCase("")]
        [TestCase("/groups/%2f")]
        [TestCase("/groups/../g")]
        [TestCase("/groups/g?epoch=0")]
        public void DecodeRequestCannotBypassPathConstructorValidation(string path)
        {
            string literal = "{\"format\":1,\"action\":0,\"path\":\"" + path + "\",\"view\":0}";

            Assert.That(() => new XRegistryProtocolCodec().DecodeRequest(Utf8(literal), XRegistryCallContext.Anonymous),
                Throws.ArgumentException);
        }

        [TestCase(null)]
        [TestCase("")]
        public void EncodeRequestRejectsMissingParameterNames(string? name)
        {
            var request = new XRegistryRequest(XRegistryAction.Read, "/")
            {
                Parameters = [new(name!, "value")]
            };

            Assert.That(() => new XRegistryProtocolCodec().EncodeRequest(request),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("request"));
        }

        [Test]
        public void EncodeRequestRejectsNullParameterEntries()
        {
            var request = new XRegistryRequest(XRegistryAction.Read, "/") { Parameters = [null!] };

            Assert.That(() => new XRegistryProtocolCodec().EncodeRequest(request), Throws.ArgumentException);
        }

        [TestCase("""{"format":1}""")]
        [TestCase("""{"format":1,"status":"200"}""")]
        [TestCase("""{"format":1,"status":200.5}""")]
        [TestCase("""{"format":1,"status":4294967296}""")]
        [TestCase("""{"format":1,"status":200,"status":201}""")]
        [TestCase("""{"format":1,"status":200,"document":null}""")]
        [TestCase("""{"format":1,"status":200,"document":"?"}""")]
        [TestCase("""{"format":1,"status":200,"location":false}""")]
        [TestCase("""{"format":1,"status":200,"contentLocation":0}""")]
        [TestCase("""{"format":1,"status":200,"correlationId":[]}""")]
        [TestCase("""{"format":1,"status":400,"error":null}""")]
        [TestCase("""{"format":1,"status":400,"error":{"code":"x"}}""")]
        [TestCase("""{"format":1,"status":400,"error":{"code":"x","code":"y","detail":"d"}}""")]
        [TestCase("""{"format":1,"status":400,"error":{"code":"x","detail":"d","subject":true}}""")]
        [TestCase("""{"format":1,"status":200,"links":{}}""")]
        [TestCase("""{"format":1,"status":200,"links":[null]}""")]
        [TestCase("""{"format":1,"status":200,"links":[{"relation":"self"}]}""")]
        [TestCase("""{"format":1,"status":200,"links":[{"relation":"a","relation":"b","target":"/"}]}""")]
        [TestCase("""{"format":1,"status":200,"allowedActions":null}""")]
        [TestCase("""{"format":1,"status":200,"allowedActions":[-1]}""")]
        [TestCase("""{"format":1,"status":200,"allowedActions":[6]}""")]
        [TestCase("""{"format":1,"status":200,"allowedActions":["0"]}""")]
        [TestCase("""{"format":1,"status":200,"allowedActions":[0.5]}""")]
        public void DecodeResponseRejectsMalformedStatusErrorLinksAndActions(string literal)
        {
            Assert.That(() => new XRegistryProtocolCodec().DecodeResponse(Utf8(literal)),
                Throws.InstanceOf<JsonException>());
        }

        [TestCase(99)]
        [TestCase(600)]
        public void DecodeResponseRejectsStatusImmediatelyOutsideValidRange(int status)
        {
            var envelope = new JsonObject { ["format"] = 1, ["status"] = status };

            Assert.That(() => new XRegistryProtocolCodec().DecodeResponse(Utf8(envelope.ToJsonString())),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("statusCode"));
        }

        [TestCase(100, false)]
        [TestCase(199, false)]
        [TestCase(200, true)]
        [TestCase(299, true)]
        [TestCase(300, false)]
        [TestCase(599, false)]
        public void DecodeResponseAcceptsInclusiveStatusBoundsAndSuccessPartition(int status, bool success)
        {
            var envelope = new JsonObject { ["format"] = 1, ["status"] = status };

            XRegistryResponse response = new XRegistryProtocolCodec().DecodeResponse(Utf8(envelope.ToJsonString()));

            Assert.That(response.StatusCode, Is.EqualTo(status));
            Assert.That(response.IsSuccess, Is.EqualTo(success));
        }

        [Test]
        public void DecodeDescriptionRequiresEachGuaranteeToBeAnExplicitBoolean(
            [Values("atomicMutations", "conditionalMutations", "writeTouch", "operationReplay")] string field,
            [Values("missing", "null", "0", "\"true\"")] string invalidValue)
        {
            JsonObject envelope = JsonNode.Parse(k_description)!.AsObject();
            if (invalidValue == "missing")
            {
                _ = envelope.Remove(field);
            }
            else
            {
                envelope[field] = JsonNode.Parse(invalidValue);
            }

            Assert.That(() => new XRegistryProtocolCodec().DecodeDescription(Utf8(envelope.ToJsonString())),
                Throws.InstanceOf<JsonException>());
        }

        [Test]
        public void DecodeDescriptionRequiresIdentityAndProfileStrings(
            [Values("registryId", "profile")] string field,
            [Values("missing", "null", "1")] string invalidValue)
        {
            JsonObject envelope = JsonNode.Parse(k_description)!.AsObject();
            if (invalidValue == "missing")
            {
                _ = envelope.Remove(field);
            }
            else
            {
                envelope[field] = JsonNode.Parse(invalidValue);
            }

            Assert.That(() => new XRegistryProtocolCodec().DecodeDescription(Utf8(envelope.ToJsonString())),
                Throws.InstanceOf<JsonException>());
        }

        [TestCase(1)]
        [TestCase(4096)]
        public void EncodeJsonAcceptsExactActualByteLimitAndRejectsOneByteLess(int textLength)
        {
            string text = new('a', textLength);
            string literal = "{\"value\":\"" + text + "\"}";
            var value = new JsonObject { ["value"] = text };
            int length = Encoding.UTF8.GetByteCount(literal);

            Assert.That(new XRegistryProtocolCodec(length).EncodeJson(value), Is.EqualTo(Utf8(literal)));
            Assert.That(new XRegistryProtocolCodec(length + 1).EncodeJson(value), Is.EqualTo(Utf8(literal)));
            Assert.That(() => new XRegistryProtocolCodec(length - 1).EncodeJson(value), Throws.ArgumentException);
        }

        [TestCase(1)]
        [TestCase(4096)]
        public void EncodeRequestAcceptsExactActualByteLimitAndRejectsOneByteLess(int textLength)
        {
            string contentType = new('a', textLength);
            var request = new XRegistryRequest(XRegistryAction.Read, "/") { ContentType = contentType };
            string literal = "{\"format\":1,\"action\":0,\"path\":\"/\",\"view\":0,\"contentType\":\"" +
                contentType + "\",\"parameters\":[]}";
            int length = Encoding.UTF8.GetByteCount(literal);

            Assert.That(new XRegistryProtocolCodec(length).EncodeRequest(request), Is.EqualTo(Utf8(literal)));
            Assert.That(new XRegistryProtocolCodec(length + 1).EncodeRequest(request), Is.EqualTo(Utf8(literal)));
            Assert.That(() => new XRegistryProtocolCodec(length - 1).EncodeRequest(request), Throws.ArgumentException);
        }

        [Test]
        public void ResponseAndDescriptionEncodingUseExactEnvelopeByteLimits()
        {
            var response = new XRegistryResponse(204);
            const string responseJson = """{"format":1,"status":204,"links":[],"allowedActions":[]}""";
            int responseLength = Encoding.UTF8.GetByteCount(responseJson);
            Assert.That(new XRegistryProtocolCodec(responseLength).EncodeResponse(response),
                Is.EqualTo(Utf8(responseJson)));
            Assert.That(() => new XRegistryProtocolCodec(responseLength - 1).EncodeResponse(response),
                Throws.ArgumentException);

            var description = new XRegistryEndpointDescription("r");
            const string descriptionJson = "{\"format\":1,\"registryId\":\"r\",\"profile\":\"unqualified\"," +
                "\"atomicMutations\":false,\"conditionalMutations\":false," +
                "\"writeTouch\":false,\"operationReplay\":false}";
            int descriptionLength = Encoding.UTF8.GetByteCount(descriptionJson);
            Assert.That(new XRegistryProtocolCodec(descriptionLength).EncodeDescription(description),
                Is.EqualTo(Utf8(descriptionJson)));
            Assert.That(() => new XRegistryProtocolCodec(descriptionLength - 1).EncodeDescription(description),
                Throws.ArgumentException);
        }

        [Test]
        public void DecodeByteLimitCountsUtf8BytesAndIncludesExactBoundary()
        {
            ByteString data = Utf8("{\"format\":1,\"action\":0,\"path\":\"/groups/\u6c34\",\"view\":0}");
            var context = new XRegistryCallContext("reader");

            XRegistryRequest request = new XRegistryProtocolCodec(data.Length).DecodeRequest(data, context);

            Assert.That(request.Path, Is.EqualTo("/groups/%E6%B0%B4"));
            Assert.That(new XRegistryProtocolCodec(data.Length + 1).DecodeRequest(data, context).Path,
                Is.EqualTo("/groups/%E6%B0%B4"));
            Assert.That(() => new XRegistryProtocolCodec(data.Length - 1).DecodeRequest(data, context),
                Throws.InstanceOf<JsonException>());
        }

        [Test]
        public void ResponseAndDescriptionDecodingUseExactEnvelopeByteLimits()
        {
            ByteString response = Utf8(k_response);
            Assert.That(new XRegistryProtocolCodec(response.Length).DecodeResponse(response).StatusCode,
                Is.EqualTo(400));
            Assert.That(() => new XRegistryProtocolCodec(response.Length - 1).DecodeResponse(response),
                Throws.InstanceOf<JsonException>());
            ByteString description = Utf8(k_description);
            Assert.That(new XRegistryProtocolCodec(description.Length).DecodeDescription(description).Profile,
                Is.EqualTo("transactional-v1"));
            Assert.That(() => new XRegistryProtocolCodec(description.Length - 1).DecodeDescription(description),
                Throws.InstanceOf<JsonException>());
        }

        [Test]
        public void RequestEncodingAndDecodingIncludeExactDepthAndRejectAdjacentDepth()
        {
            using JsonDocument metadata = JsonDocument.Parse("""{"items":[]}""");
            var request = new XRegistryRequest(XRegistryAction.Merge, "/") { Metadata = metadata.RootElement };
            const string literal = "{\"format\":1,\"action\":2,\"path\":\"/\",\"view\":0," +
                "\"metadata\":{\"items\":[]},\"parameters\":[]}";
            var exact = new XRegistryProtocolCodec(maximumDepth: 3);

            Assert.That(exact.EncodeRequest(request), Is.EqualTo(Utf8(literal)));
            XRegistryRequest decoded = exact.DecodeRequest(Utf8(literal), XRegistryCallContext.Anonymous);
            Assert.That(decoded.Metadata.GetProperty("items").GetArrayLength(), Is.Zero);
            var below = new XRegistryProtocolCodec(maximumDepth: 2);
            Assert.That(() => below.EncodeRequest(request), Throws.InvalidOperationException);
            Assert.That(() => below.DecodeRequest(Utf8(literal), XRegistryCallContext.Anonymous),
                Throws.InstanceOf<JsonException>());
        }

        [Test]
        public void EncodeJsonIncludesExactDepthAndRejectsAdjacentDepth()
        {
            JsonNode value = JsonNode.Parse("""{"items":[[1]]}""")!;
            const string literal = """{"items":[[1]]}""";

            Assert.That(new XRegistryProtocolCodec(maximumDepth: 3).EncodeJson(value), Is.EqualTo(Utf8(literal)));
            Assert.That(() => new XRegistryProtocolCodec(maximumDepth: 2).EncodeJson(value),
                Throws.InvalidOperationException);
        }

        [Test]
        public void ResponseAndDescriptionEnforceDepthInsideMetadataDocuments()
        {
            using JsonDocument metadata = JsonDocument.Parse("""{"items":[1]}""");
            var response = new XRegistryResponse(200) { Metadata = metadata.RootElement };
            const string responseJson = "{\"format\":1,\"status\":200,\"metadata\":{\"items\":[1]}," +
                "\"links\":[],\"allowedActions\":[]}";
            var description = new XRegistryEndpointDescription("r") { Model = metadata.RootElement };
            const string descriptionJson = "{\"format\":1,\"registryId\":\"r\",\"profile\":\"unqualified\"," +
                "\"model\":{\"items\":[1]},\"atomicMutations\":false,\"conditionalMutations\":false," +
                "\"writeTouch\":false,\"operationReplay\":false}";
            var exact = new XRegistryProtocolCodec(maximumDepth: 3);
            var below = new XRegistryProtocolCodec(maximumDepth: 2);

            Assert.That(exact.EncodeResponse(response), Is.EqualTo(Utf8(responseJson)));
            Assert.That(exact.DecodeResponse(Utf8(responseJson)).Metadata.GetProperty("items")[0].GetInt32(),
                Is.EqualTo(1));
            Assert.That(() => below.EncodeResponse(response), Throws.InvalidOperationException);
            Assert.That(() => below.DecodeResponse(Utf8(responseJson)), Throws.InstanceOf<JsonException>());
            Assert.That(exact.EncodeDescription(description), Is.EqualTo(Utf8(descriptionJson)));
            Assert.That(exact.DecodeDescription(Utf8(descriptionJson)).Model.GetProperty("items")[0].GetInt32(),
                Is.EqualTo(1));
            Assert.That(() => below.EncodeDescription(description), Throws.InvalidOperationException);
            Assert.That(() => below.DecodeDescription(Utf8(descriptionJson)), Throws.InstanceOf<JsonException>());
        }

        private static ByteString Utf8(string value)
        {
            return ByteString.From(Encoding.UTF8.GetBytes(value));
        }

        private const string k_metadata =
            """{"epoch":4294967296,"meta":{"epoch":0},"enabled":true,"tags":[2,"x"],"nullable":null}""";
        private const string k_request = "{\"format\":1,\"action\":2,\"path\":\"/groups/g\",\"view\":1,\"metadata\":" +
            k_metadata + ",\"document\":\"AAH/gA==\",\"contentType\":\"application/octet-stream\"," +
            "\"operationId\":\"op-7\"," +
            "\"parameters\":[{\"name\":\"filter\",\"value\":\"a=1\"},{\"name\":\"filter\",\"value\":null}," +
            "{\"name\":\"sort\",\"value\":\"\"}]}";
        private const string k_response = "{\"format\":1,\"status\":400," +
            "\"metadata\":{\"epoch\":0,\"resourceid\":\"r\"}," +
            "\"document\":\"AP8=\",\"contentType\":\"application/json\",\"location\":\"/groups/g/resources/r\"," +
            "\"contentLocation\":\"/groups/g/resources/r/versions/v1\",\"correlationId\":\"event-4\"," +
            "\"error\":{\"code\":\"mismatched_epoch\",\"detail\":\"Expected epoch 0.\",\"subject\":\"/groups/g\"}," +
            "\"links\":[{\"relation\":\"self\",\"target\":\"/groups/g\"}," +
            "{\"relation\":\"alternate\",\"target\":\"/groups/g/resources/r\"}],\"allowedActions\":[0,5]}";
        private const string k_description = "{\"format\":1,\"registryId\":\"registry-a\"," +
            "\"profile\":\"transactional-v1\",\"model\":{\"groups\":{\"devices\":{}}}," +
            "\"capabilities\":{\"flags\":[\"filter\"]},\"atomicMutations\":true," +
            "\"conditionalMutations\":false,\"writeTouch\":true,\"operationReplay\":false}";
        private const string k_digest = "ecb2e55b848896fd36cf278d46addbb1063b09c70326e756362541e469fcd07e";
    }
}
