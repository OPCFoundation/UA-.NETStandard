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
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Bridge.Tests.Sync;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests
{
    [TestFixture]
    public sealed class XRegistryGenerationReadScopeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativePagedInventoryKeepsTheGenerationAcrossContinuationLinksAsync(bool change)
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync("/schemagroups/g", "{}").ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/schemagroups/second", "{}").ConfigureAwait(false);
            int pages = 0;
            fixture.Native.TransformRequest = request => request.Path == "/schemagroups"
                ? request with { Parameters = [] } : request;
            fixture.Native.TransformResponse = (request, response) =>
            {
                if (request.Path != "/schemagroups" || !response.IsSuccess)
                {
                    return response;
                }
                pages++;
                bool last = request.Parameters.Count != 0;
                string id = last ? "second" : "g";
                var page = new JsonObject { [id] = JsonNode.Parse(response.Metadata.GetProperty(id).GetRawText()) };
                return response with
                {
                    Metadata = XRegistrySyncFixture.Json(page.ToJsonString()),
                    Links = last ? [] : [new XRegistryLink("next", "/schemagroups?cursor=second")]
                };
            };
            fixture.Native.AfterExecuteAsync = async (request, _, _) =>
            {
                if (change && request.Path == "/schemagroups" && request.Parameters.Count == 0)
                {
                    fixture.Native.AfterExecuteAsync = null;
                    await fixture.Native.ChangeAsync("/schemagroups/g",
                        /*lang=json,strict*/ """{"name":"page-race"}""").ConfigureAwait(false);
                }
            };
            var options = new XRegistryBridgeNativeOptions { ProjectionContext = XRegistrySyncFixture.Writer };
            if (change)
            {
                ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await XRegistryNativeSnapshot.LoadAsync(fixture.Native, options, CancellationToken.None)
                        .ConfigureAwait(false));
                Assert.Multiple(() =>
                {
                    Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                    Assert.That(pages, Is.EqualTo(1));
                });
            }
            else
            {
                XRegistryNativeSnapshot snapshot = await XRegistryNativeSnapshot.LoadAsync(
                    fixture.Native, options, CancellationToken.None).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(snapshot.NativeGroups.ToArray()!.Select(group => group.Id),
                        Is.EquivalentTo(s_groupIds));
                    Assert.That(pages, Is.EqualTo(2));
                });
            }
            Assert.That(fixture.Native.Mutations, Is.Empty);
        }

        [TestCase(null)]
        [TestCase("")]
        public void AdvertisedGenerationCannotBeAbsent(string? generation)
        {
            var endpoint = new Mock<IXRegistryEndpoint>(MockBehavior.Strict);
            Assert.That(() => new XRegistryGenerationReadScope(endpoint.Object,
                new XRegistryEndpointDescription("registry")
                {
                    SupportsGenerationGuards = true,
                    Generation = generation
                }),
                Throws.TypeOf<InvalidDataException>());
            endpoint.VerifyNoOtherCalls();
        }

        [Test]
        public void ConstructorRequiresEndpointAndDescription()
        {
            Assert.That(() => new XRegistryGenerationReadScope(null!, Description()), Throws.ArgumentNullException);
            Assert.That(() => new XRegistryGenerationReadScope(Mock.Of<IXRegistryEndpoint>(), null!),
                Throws.ArgumentNullException);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadScopePreservesContextAndAddsOnlyNegotiatedGuardsAsync(bool qualified)
        {
            var context = new XRegistryCallContext("reader") { IsAuthenticated = true, Authority = "test" };
            var response = new XRegistryResponse(200) { Generation = qualified ? "generation" : null };
            var endpoint = new Mock<IXRegistryEndpoint>(MockBehavior.Strict);
            using var cancellation = new CancellationTokenSource();
            endpoint.Setup(value => value.ExecuteAsync(
                It.Is<XRegistryRequest>(request =>
                    request.Context == context &&
                    request.ExpectedGeneration == (qualified ? "generation" : null) &&
                    request.Path == "/groups" &&
                    request.View == XRegistryView.Metadata),
                cancellation.Token)).ReturnsAsync(response);
            var scope = new XRegistryGenerationReadScope(endpoint.Object, Description() with
            {
                SupportsGenerationGuards = qualified
            });
            XRegistryResponse read = await scope.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/groups")
            {
                Context = context,
                View = XRegistryView.Metadata
            }, cancellation.Token).ConfigureAwait(false);
            Assert.That(read, Is.SameAs(response));
            endpoint.VerifyAll();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("other")]
        public void SuccessfulReadsCannotOmitOrChangeTheGeneration(string? generation)
        {
            var endpoint = new Mock<IXRegistryEndpoint>();
            endpoint.Setup(value => value.ExecuteAsync(It.IsAny<XRegistryRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new XRegistryResponse(200) { Generation = generation });
            var scope = new XRegistryGenerationReadScope(endpoint.Object, Description());
            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await scope.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/")).ConfigureAwait(false));
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [TestCase(false, "generation")]
        [TestCase(true, null)]
        [TestCase(true, "different")]
        public void FinalInspectionRejectsAChangedOrRevokedGeneration(bool supported, string? generation)
        {
            var endpoint = new Mock<IXRegistryEndpoint>();
            endpoint.Setup(value => value.InspectAsync(It.IsAny<XRegistryCallContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Description() with { SupportsGenerationGuards = supported, Generation = generation });
            var scope = new XRegistryGenerationReadScope(endpoint.Object, Description());
            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await scope.InspectAsync(XRegistryCallContext.Anonymous).ConfigureAwait(false));
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task RejectedReadsPreserveTheActualBackendErrorAsync()
        {
            var response = new XRegistryResponse(403) { Error = new XRegistryError("unauthorized", "revoked") };
            var endpoint = new Mock<IXRegistryEndpoint>();
            endpoint.Setup(value => value.ExecuteAsync(It.IsAny<XRegistryRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);
            var scope = new XRegistryGenerationReadScope(endpoint.Object, Description());
            XRegistryResponse actual = await scope.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.That(actual, Is.SameAs(response));
        }

        [TestCase(XRegistryAction.Replace, null)]
        [TestCase(XRegistryAction.Merge, null)]
        [TestCase(XRegistryAction.Create, null)]
        [TestCase(XRegistryAction.Delete, null)]
        [TestCase(XRegistryAction.Read, "other")]
        public void ScopeCannotMutateOrSilentlyReplaceAnotherGuard(XRegistryAction action, string? generation)
        {
            var endpoint = new Mock<IXRegistryEndpoint>(MockBehavior.Strict);
            var scope = new XRegistryGenerationReadScope(endpoint.Object, Description());
            Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.ExecuteAsync(
                new XRegistryRequest(action, "/") { ExpectedGeneration = generation }).ConfigureAwait(false));
            endpoint.VerifyNoOtherCalls();
        }

        private static XRegistryEndpointDescription Description()
        {
            return new XRegistryEndpointDescription("registry")
            {
                SupportsGenerationGuards = true,
                Generation = "generation"
            };
        }

        private static readonly string[] s_groupIds = ["g", "second"];
    }
}
