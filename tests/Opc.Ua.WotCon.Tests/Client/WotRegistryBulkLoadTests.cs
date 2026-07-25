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

        private static NodeId FindGetOrCreateResourceMethodId(WotRegistrySessionMock mock)
        {
            return ExpandedNodeId.ToNodeId(
                XRegistry.MethodIds.GroupType_GetOrCreateResource, mock.Session.NamespaceUris);
        }

        private static readonly string[] s_expectedOrder = ["tm1", "tm2", "td1", "td2"];
    }
}
