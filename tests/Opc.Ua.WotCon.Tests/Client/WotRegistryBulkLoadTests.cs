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

using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Tests.Client
{
    /// <summary>
    /// Session-mock unit tests for
    /// <see cref="WotRegistryClient.LoadDocumentsAsync"/>: dependency
    /// ordering (Thing Models before Thing Descriptions) and mutation /
    /// refresh failure propagation.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class WotRegistryBulkLoadTests
    {
        private static ITelemetryContext CreateTelemetry()
        {
            return Mock.Of<ITelemetryContext>();
        }

        private static ByteString Content(string text)
        {
            return ByteString.From(Encoding.UTF8.GetBytes(text));
        }

        [Test]
        public async Task LoadDocumentsOrdersThingModelsBeforeThingDescriptionsAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);

            ArrayOf<WotRegistryDocument> documents = new WotRegistryDocument[]
            {
                new(WoTDocumentKindEnum.ThingDescription, "thingdescriptions", "td1", Content("td1")),
                new(WoTDocumentKindEnum.ThingModel, "thingmodels", "tm1", Content("tm1")),
                new(WoTDocumentKindEnum.ThingDescription, "thingdescriptions", "td2", Content("td2")),
                new(WoTDocumentKindEnum.ThingModel, "thingmodels", "tm2", Content("tm2"))
            }.ToArrayOf();

            WotRegistryBulkLoadResult result = await client
                .LoadDocumentsAsync(documents, refresh: false)
                .ConfigureAwait(false);

            var order = new List<string>();
            foreach (WotRegistryDocumentLoadOutcome outcome in result.Uploaded)
            {
                order.Add(outcome.Document.ResourceId);
            }

            Assert.That(order, Is.EqualTo(s_expectedOrder));
            Assert.That(result.Refresh, Is.Null);
        }

        [Test]
        public async Task LoadDocumentsThrowsWhenDocumentKindDisagreesWithGroupKindAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);

            // "thingmodels" is the reserved Thing Model group; declaring a
            // Thing Description document against it must fail fast.
            ArrayOf<WotRegistryDocument> documents = new WotRegistryDocument[]
            {
                new(WoTDocumentKindEnum.ThingDescription, "thingmodels", "oops", Content("oops"))
            }.ToArrayOf();

            Assert.That(
                () => client.LoadDocumentsAsync(documents, refresh: false).AsTask(),
                Throws.InstanceOf<ServiceResultException>());
        }

        [Test]
        public async Task LoadDocumentsThrowsWhenCachedGroupKindDisagreesAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);

            ArrayOf<WotRegistryDocument> documents = new WotRegistryDocument[]
            {
                new(WoTDocumentKindEnum.ThingDescription, "mixed", "td", Content("td")),
                new(WoTDocumentKindEnum.ThingModel, "mixed", "tm", Content("tm"))
            }.ToArrayOf();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.LoadDocumentsAsync(documents, refresh: false)
                    .ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task LoadDocumentsPropagatesMutationFailuresImmediatelyAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);

            ArrayOf<WotRegistryDocument> documents = new WotRegistryDocument[]
            {
                new(WoTDocumentKindEnum.ThingDescription, "thingdescriptions", "boom", Content("boom")),
                new(WoTDocumentKindEnum.ThingDescription, "thingdescriptions", "never-reached", Content("x"))
            }.ToArrayOf();

            // Fail the next GetOrCreateResource call so the first
            // document's mutation fails.
            NodeId resourceMethodId = FindGetOrCreateResourceMethodId(mock);
            mock.FailNextCallOn[resourceMethodId] = StatusCodes.BadResourceUnavailable;

            Assert.That(
                () => client.LoadDocumentsAsync(documents, refresh: false).AsTask(),
                Throws.InstanceOf<ServiceResultException>());

            // The second document's resource must never have been created.
            WotRegistryGroupClient group = await client
                .OpenGroupAsync("thingdescriptions").ConfigureAwait(false);
            Assert.That(
                () => group.OpenResourceAsync("never-reached").AsTask(),
                Throws.InstanceOf<ServiceResultException>());
        }

        [Test]
        public async Task LoadDocumentsSurfacesRefreshFailuresWithoutThrowingAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            mock.InvalidResourceIds.Add("bad");

            ArrayOf<WotRegistryDocument> documents = new WotRegistryDocument[]
            {
                new(WoTDocumentKindEnum.ThingDescription, "thingdescriptions", "bad", Content("bad"))
            }.ToArrayOf();

            WotRegistryBulkLoadResult result = await client
                .LoadDocumentsAsync(documents, refresh: true)
                .ConfigureAwait(false);

            Assert.That(result.Uploaded, Has.Count.EqualTo(1));
            Assert.That(result.Refresh, Is.Not.Null);
            Assert.That(result.Refresh!.HasFailures, Is.True);
        }

        [Test]
        public async Task LoadDocumentsUsesSingleGetOrCreateResourceCallPerDocumentAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            ArrayOf<WotRegistryDocument> documents = new WotRegistryDocument[]
            {
                new(WoTDocumentKindEnum.ThingDescription, "thingdescriptions", "td1", Content("td1")),
                new(WoTDocumentKindEnum.ThingDescription, "thingdescriptions", "td2", Content("td2"))
            }.ToArrayOf();
            NodeId getOrCreateResourceMethodId = FindGetOrCreateResourceMethodId(mock);

            WotRegistryBulkLoadResult result = await client
                .LoadDocumentsAsync(documents, refresh: false)
                .ConfigureAwait(false);

            int calls = 0;
            foreach (CallMethodRequest request in mock.Capture)
            {
                if (request.MethodId == getOrCreateResourceMethodId)
                {
                    calls++;
                }
            }
            Assert.That(calls, Is.EqualTo(documents.Count));
            Assert.That(result.Uploaded[0].VersionId, Is.EqualTo("1"));
            Assert.That(result.Uploaded[1].VersionId, Is.EqualTo("1"));
        }

        private static NodeId FindGetOrCreateResourceMethodId(WotRegistrySessionMock mock)
        {
            return ExpandedNodeId.ToNodeId(
                XRegistry.MethodIds.GroupType_GetOrCreateResource, mock.Session.NamespaceUris);
        }

        [Test]
        public async Task LoadDocumentsRejectsANullDocumentArrayAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);

            Assert.That(
                () => client.LoadDocumentsAsync(default, refresh: false).AsTask(),
                Throws.ArgumentNullException
                    .With.Property("ParamName").EqualTo("documents"));
        }

        [Test]
        public void DocumentRejectsAMissingGroupId()
        {
            Assert.That(
                () => new WotRegistryDocument(
                    WoTDocumentKindEnum.ThingDescription, "  ", "td", Content("td")),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("groupId"));
        }

        [Test]
        public void DocumentRejectsAMissingResourceId()
        {
            Assert.That(
                () => new WotRegistryDocument(
                    WoTDocumentKindEnum.ThingDescription, "thingdescriptions", string.Empty, Content("td")),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("resourceId"));
        }

        [Test]
        public void DocumentRejectsMissingContent()
        {
            Assert.That(
                () => new WotRegistryDocument(
                    WoTDocumentKindEnum.ThingDescription, "thingdescriptions", "td", default),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("content"));
        }

        [Test]
        public void DocumentDefaultsTheVersionIdToEmpty()
        {
            var document = new WotRegistryDocument(
                WoTDocumentKindEnum.ThingModel, "thingmodels", "tm", Content("tm"), null!);

            Assert.That(document.VersionId, Is.Empty);
            Assert.That(document.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingModel));
            Assert.That(document.GroupId, Is.EqualTo("thingmodels"));
            Assert.That(document.ResourceId, Is.EqualTo("tm"));
            Assert.That(document.Content, Is.EqualTo(Content("tm")));
        }

        [Test]
        public async Task LoadDocumentsReportsResourceNodeIdsAndCreationFlagsAsync()
        {
            var mock = new WotRegistrySessionMock();
            WotRegistryClient client = await WotRegistryClient
                .ForServerAsync(mock.Session, CreateTelemetry())
                .ConfigureAwait(false);
            ArrayOf<WotRegistryDocument> documents = new WotRegistryDocument[]
            {
                new(WoTDocumentKindEnum.ThingDescription, "thingdescriptions", "td1", Content("td1"))
            }.ToArrayOf();

            WotRegistryBulkLoadResult first = await client
                .LoadDocumentsAsync(documents, refresh: false).ConfigureAwait(false);
            WotRegistryBulkLoadResult second = await client
                .LoadDocumentsAsync(documents, refresh: false).ConfigureAwait(false);

            Assert.That(first.Uploaded[0].Created, Is.True);
            Assert.That(second.Uploaded[0].Created, Is.False);
            Assert.That(first.Uploaded[0].ResourceNodeId.IsNull, Is.False);
            Assert.That(first.Uploaded[0].VersionId, Is.EqualTo("1"));
            Assert.That(second.Uploaded[0].VersionId, Is.EqualTo("2"));
            Assert.That(
                second.Uploaded[0].ResourceNodeId,
                Is.Not.EqualTo(first.Uploaded[0].ResourceNodeId));
            Assert.That(
                second.Uploaded[0].ResourceNodeId,
                Is.EqualTo(new NodeId(
                    "WoTRegistry/groups/thingdescriptions/resources/td1/versions/" +
                    second.Uploaded[0].VersionId,
                    mock.WotConNs)));
        }

        private static readonly string[] s_expectedOrder = ["tm1", "tm2", "td1", "td2"];
    }
}
