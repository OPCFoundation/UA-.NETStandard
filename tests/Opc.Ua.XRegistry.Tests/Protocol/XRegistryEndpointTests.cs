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
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Tests.Protocol
{
    [TestFixture]
    [Category("XRegistryProtocol")]
    public sealed class XRegistryEndpointTests
    {
        [Test]
        public void RequestOwnsTypedMetadataAfterSourceDocumentIsDisposed()
        {
            XRegistryRequest request;
            using (JsonDocument document = JsonDocument.Parse(
                """{"epoch":4294967296,"meta":{"epoch":0},"items":[true,null,2.5],"label":"original"}"""))
            {
                request = new XRegistryRequest(XRegistryAction.Replace, "/groups/g")
                {
                    Metadata = document.RootElement
                };
            }

            Assert.That(request.Metadata.GetProperty("epoch").GetRawText(), Is.EqualTo("4294967296"));
            Assert.That(request.Metadata.GetProperty("meta").GetProperty("epoch").GetInt32(), Is.Zero);
            JsonElement items = request.Metadata.GetProperty("items");
            Assert.That(items.GetArrayLength(), Is.EqualTo(3));
            Assert.That(items[0].GetBoolean(), Is.True);
            Assert.That(items[1].ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(items[2].GetDecimal(), Is.EqualTo(2.5m));
            Assert.That(request.Metadata.GetProperty("label").GetString(), Is.EqualTo("original"));
        }

        [Test]
        public void ResponseOwnsMetadataAfterSourceDocumentIsDisposed()
        {
            XRegistryResponse response;
            using (JsonDocument document = JsonDocument.Parse(
                """{"epoch":18446744073709551616,"labels":{"enabled":false},"resourceid":"r"}"""))
            {
                response = new XRegistryResponse(201) { Metadata = document.RootElement };
            }

            Assert.That(response.Metadata.GetProperty("epoch").GetRawText(), Is.EqualTo("18446744073709551616"));
            Assert.That(response.Metadata.GetProperty("labels").GetProperty("enabled").GetBoolean(), Is.False);
            Assert.That(response.Metadata.GetProperty("resourceid").GetString(), Is.EqualTo("r"));
        }

        [Test]
        public void DescriptionOwnsModelAndCapabilitiesAfterBothSourceDocumentsAreDisposed()
        {
            XRegistryEndpointDescription description;
            using (JsonDocument model = JsonDocument.Parse("""{"groups":{"devices":{"resources":{"schemas":{}}}}}"""))
            using (JsonDocument capabilities = JsonDocument.Parse("""{"flags":["filter","sort"],"mutable":true}"""))
            {
                description = new XRegistryEndpointDescription("registry-a")
                {
                    Model = model.RootElement,
                    Capabilities = capabilities.RootElement
                };
            }

            Assert.That(description.Model.GetProperty("groups").GetProperty("devices")
                .GetProperty("resources").GetProperty("schemas").ValueKind, Is.EqualTo(JsonValueKind.Object));
            JsonElement flags = description.Capabilities.GetProperty("flags");
            Assert.That(flags.GetArrayLength(), Is.EqualTo(2));
            Assert.That(flags[0].GetString(), Is.EqualTo("filter"));
            Assert.That(flags[1].GetString(), Is.EqualTo("sort"));
            Assert.That(description.Capabilities.GetProperty("mutable").GetBoolean(), Is.True);
        }

        [Test]
        public void RequestCopyOwnsReplacementMetadataWithoutChangingOriginalSnapshot()
        {
            XRegistryRequest original;
            XRegistryRequest changed;
            using (JsonDocument first = JsonDocument.Parse("""{"epoch":5,"value":"before"}"""))
            using (JsonDocument second = JsonDocument.Parse("""{"epoch":6,"value":null}"""))
            {
                original = new XRegistryRequest(XRegistryAction.Merge, "/groups/g") { Metadata = first.RootElement };
                changed = original with { Metadata = second.RootElement };
            }

            Assert.That(original.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(5));
            Assert.That(original.Metadata.GetProperty("value").GetString(), Is.EqualTo("before"));
            Assert.That(changed.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(6));
            Assert.That(changed.Metadata.GetProperty("value").ValueKind, Is.EqualTo(JsonValueKind.Null));
        }

        [TestCase(XRegistryAction.Read, false)]
        [TestCase(XRegistryAction.Replace, true)]
        [TestCase(XRegistryAction.Merge, true)]
        [TestCase(XRegistryAction.Create, true)]
        [TestCase(XRegistryAction.Delete, true)]
        [TestCase(XRegistryAction.Describe, false)]
        public void RequestClassifiesOnlyWriteActionsAsMutations(XRegistryAction action, bool mutation)
        {
            var request = new XRegistryRequest(action, "/");

            Assert.That(request.IsMutation, Is.EqualTo(mutation));
        }

        [TestCase(-1)]
        [TestCase(6)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void RequestConstructorRejectsUnknownActions(int action)
        {
            Assert.That(() => new XRegistryRequest((XRegistryAction)action, "/"),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("action"));
        }

        [TestCase("")]
        [TestCase("groups/g")]
        [TestCase("/groups/../g")]
        [TestCase("/groups/%2F")]
        [TestCase("/groups/g?epoch=0")]
        [TestCase("/groups/g#fragment")]
        public void RequestConstructorRejectsInvalidPaths(string path)
        {
            Assert.That(() => new XRegistryRequest(XRegistryAction.Read, path), Throws.ArgumentException);
        }

        [Test]
        public void RequestConstructorRejectsNullPath()
        {
            Assert.That(() => new XRegistryRequest(XRegistryAction.Read, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("path"));
        }

        [Test]
        public void RequestConstructorCanonicalizesEscapesWithoutNormalizingIdentity()
        {
            var request = new XRegistryRequest(XRegistryAction.Read, "/groups/cafe%cc%81/");

            Assert.That(request.Path, Is.EqualTo("/groups/cafe%CC%81"));
            Assert.That(request.Path, Is.Not.EqualTo("/groups/caf%C3%A9"));
            Assert.That(request.View, Is.EqualTo(XRegistryView.Default));
            Assert.That(request.Metadata.ValueKind, Is.EqualTo(JsonValueKind.Undefined));
            Assert.That(request.Document.IsNull, Is.True);
        }

        [TestCase(-1)]
        [TestCase(2)]
        [TestCase(int.MaxValue)]
        public void RequestRejectsUnknownViews(int view)
        {
            Assert.That(() => new XRegistryRequest(XRegistryAction.Read, "/") { View = (XRegistryView)view },
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(100, false)]
        [TestCase(199, false)]
        [TestCase(200, true)]
        [TestCase(299, true)]
        [TestCase(300, false)]
        [TestCase(599, false)]
        public void ResponseClassifiesOnlyTwoHundredStatusesAsSuccess(int status, bool success)
        {
            var response = new XRegistryResponse(status);

            Assert.That(response.IsSuccess, Is.EqualTo(success));
        }

        [TestCase(99)]
        [TestCase(600)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void ResponseConstructorRejectsOutOfRangeStatus(int status)
        {
            Assert.That(() => new XRegistryResponse(status),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("statusCode"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t")]
        public void CallContextRejectsMissingSubject(string? subject)
        {
            Assert.That(() => new XRegistryCallContext(subject!),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("subject"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t")]
        public void DescriptionRejectsMissingRegistryIdentity(string? registryId)
        {
            Assert.That(() => new XRegistryEndpointDescription(registryId!),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("registryId"));
        }

        [Test]
        public void DescriptionDoesNotClaimUnmeasuredGuaranteesOrInventDocuments()
        {
            var description = new XRegistryEndpointDescription("registry-a");

            Assert.That(description.Profile, Is.EqualTo("unqualified"));
            Assert.That(description.Model.ValueKind, Is.EqualTo(JsonValueKind.Undefined));
            Assert.That(description.Capabilities.ValueKind, Is.EqualTo(JsonValueKind.Undefined));
            Assert.That(description.SupportsAtomicMutations, Is.False);
            Assert.That(description.SupportsConditionalMutations, Is.False);
            Assert.That(description.SupportsWriteTouch, Is.False);
            Assert.That(description.SupportsOperationReplay, Is.False);
            Assert.That(description.SupportsPreparedMutations, Is.False);
            Assert.That(description.PublicRoot, Is.Null);
        }

        [Test]
        public void DescriptionEnvelopeRetainsOptionalSourceRootAndPreparedCapability()
        {
            var codec = new XRegistryProtocolCodec();
            var description = new XRegistryEndpointDescription("registry")
            {
                PublicRoot = new Uri("https://source.example/registry/"),
                SupportsPreparedMutations = true
            };
            XRegistryEndpointDescription decoded = codec.DecodeDescription(codec.EncodeDescription(description));
            Assert.Multiple(() =>
            {
                Assert.That(decoded.PublicRoot, Is.EqualTo(description.PublicRoot));
                Assert.That(decoded.SupportsPreparedMutations, Is.True);
                Assert.That(decoded.SupportsOperationReplay, Is.False);
            });
        }

        [Test]
        public void CallerOwnedBuffersCannotChangeAnImmutableRequestOrResponsePreview()
        {
            byte[] bytes = [1, 2, 3];
            string[] roles = ["reader"];
            XRegistryParameter[] parameters = [new("epoch", "0")];
            XRegistryLink[] links = [new("next", "/groups?page=2")];
            XRegistryAction[] actions = [XRegistryAction.Read];
            var context = new XRegistryCallContext("caller")
            {
                Roles = new ArrayOf<string>(roles.AsMemory())
            };
            var request = new XRegistryRequest(XRegistryAction.Replace, "/")
            {
                Document = new ByteString(bytes.AsMemory()),
                Parameters = new ArrayOf<XRegistryParameter>(parameters.AsMemory()),
                Context = context
            };
            var response = new XRegistryResponse(200)
            {
                Document = new ByteString(bytes.AsMemory()),
                Links = new ArrayOf<XRegistryLink>(links.AsMemory()),
                AllowedActions = new ArrayOf<XRegistryAction>(actions.AsMemory())
            };
            bytes[0] = 99;
            roles[0] = "writer";
            parameters[0] = new XRegistryParameter("epoch", "100");
            links[0] = new XRegistryLink("next", "/different");
            actions[0] = XRegistryAction.Delete;
            Assert.Multiple(() =>
            {
                Assert.That(request.Document[0], Is.EqualTo(1));
                Assert.That(request.Parameters[0].Value, Is.EqualTo("0"));
                Assert.That(request.Context.Roles[0], Is.EqualTo("reader"));
                Assert.That(response.Document[0], Is.EqualTo(1));
                Assert.That(response.Links[0].Target, Is.EqualTo("/groups?page=2"));
                Assert.That(response.AllowedActions[0], Is.EqualTo(XRegistryAction.Read));
            });
        }
    }
}
