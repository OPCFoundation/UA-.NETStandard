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
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WoT")]
    public sealed class WotDocumentKindContractTests
    {
        [TestCase(-1, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(int.MaxValue, false)]
        public void DocumentKindIsDistinctFromTheSelectorDomain(int value, bool document)
        {
            Assert.That(WotDocumentKinds.IsDocument((WoTDocumentKindEnum)value), Is.EqualTo(document));
        }

        [Test]
        public void NonDocumentKindsCannotConstructStoredOrExecutableDocuments(
            [Values(-1, 2, 3)] int value,
            [Values("group", "resource", "upload", "plan")] string surface)
        {
            var kind = (WoTDocumentKindEnum)value;

            Assert.That(() => Construct(surface, kind), Throws.TypeOf<ArgumentOutOfRangeException>()
                .With.Property("ParamName").EqualTo("kind"));
        }

        [TestCase(WoTDocumentKindEnum.ThingDescription)]
        [TestCase(WoTDocumentKindEnum.ThingModel)]
        public void BothDocumentKindsRemainValidAcrossDocumentBoundaries(WoTDocumentKindEnum kind)
        {
            var group = new WotResourceGroup("documents", kind);
            var resource = new WotResource("documents", "document", kind, []);
            var upload = new WotRegistryDocument(kind, "documents", "document", ByteString.From("{}"u8));
            var plan = new WotBindingPlanRequest("document", kind, []);

            Assert.That(group.Kind, Is.EqualTo(kind));
            Assert.That(resource.Kind, Is.EqualTo(kind));
            Assert.That(upload.Kind, Is.EqualTo(kind));
            Assert.That(plan.Kind, Is.EqualTo(kind));
            Assert.That(plan.IsDeclarationContext, Is.EqualTo(kind == WoTDocumentKindEnum.ThingModel));
        }

        [Test]
        public async Task NonDocumentCreationKindsFailBeforeAnySnapshotMutation(
            [Values("createGroup", "getGroup", "createResource", "getResource", "createVersion", "getVersion")]
            string operation,
            [Values] bool exists)
        {
            using var service = new WotRegistryService();
            if (exists)
            {
                await service.GetOrCreateVersionAsync(
                    "documents", "document", "version", WoTDocumentKindEnum.ThingDescription).ConfigureAwait(false);
            }
            WotRegistrySnapshot before = service.Current;

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await CreateAsync(service, operation, WoTDocumentKindEnum.All).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(service.Current, Is.SameAs(before));
        }

        [Test]
        public async Task NonDocumentUpsertKindsAreRejectedWithoutPublishing(
            [Values(-1, 2, 3)] int value, [Values] bool exists)
        {
            using var service = new WotRegistryService();
            if (exists)
            {
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = "documents",
                    ResourceId = "document",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = ByteString.From(TestMaterialization.Td("urn:test:original"))
                }).ConfigureAwait(false);
            }
            WotRegistrySnapshot before = service.Current;

            WotRegistryMutationResult result = await service.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "documents",
                ResourceId = "document",
                Kind = (WoTDocumentKindEnum)value,
                Content = ByteString.From(TestMaterialization.Td("urn:test:replacement"))
            }).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(result.Diagnostics, Is.Not.Empty);
            Assert.That(service.Current, Is.SameAs(before));
        }

        [Test]
        public void NonDocumentPlanKindFailsBeforeResolvingAnEventDefinition()
        {
            var resolver = new Mock<IWotThingResolver>(MockBehavior.Strict);
            byte[] document = Encoding.UTF8.GetBytes(
                /*lang=json,strict*/
                                     """
                {
                  "events":{"alarm":{"tm:ref":"urn:source:event",
                    "forms":[{"href":"https://source.test/events","op":"subscribeevent"}]}}
                }
                """);

            Assert.That(() => WotBindingPlanRequest.FromDocumentAsync(
                "document", WoTDocumentKindEnum.All, document, resolver.Object).AsTask(),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("kind"));
            resolver.VerifyNoOtherCalls();
        }

        private static void Construct(string surface, WoTDocumentKindEnum kind)
        {
            switch (surface)
            {
                case "group":
                    _ = new WotResourceGroup("documents", kind);
                    break;
                case "resource":
                    _ = new WotResource("documents", "document", kind, []);
                    break;
                case "upload":
                    _ = new WotRegistryDocument(kind, "documents", "document", ByteString.From("{}"u8));
                    break;
                case "plan":
                    _ = new WotBindingPlanRequest("document", kind, []);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(surface));
            }
        }

        private static async Task CreateAsync(
            WotRegistryService service, string operation, WoTDocumentKindEnum kind)
        {
            switch (operation)
            {
                case "createGroup":
                    await service.TryCreateGroupAsync("documents", kind).ConfigureAwait(false);
                    break;
                case "getGroup":
                    await service.GetOrCreateGroupAsync("documents", kind).ConfigureAwait(false);
                    break;
                case "createResource":
                    await service.TryCreateResourceAsync("documents", "document", kind).ConfigureAwait(false);
                    break;
                case "getResource":
                    await service.GetOrCreateResourceAsync("documents", "document", kind).ConfigureAwait(false);
                    break;
                case "createVersion":
                    await service.TryCreateVersionAsync("documents", "document", "version", kind).ConfigureAwait(false);
                    break;
                case "getVersion":
                    await service.GetOrCreateVersionAsync("documents", "document", "version", kind)
                        .ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
    }
}
