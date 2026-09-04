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
 *
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
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// What the delete path does with the arguments and states it is not
    /// written for: a walk asked to run over nothing, a link that names no
    /// stored document, a policy the Server does not know, a request that
    /// carries no id, and a coordinator that has already been disposed.
    /// </summary>
    /// <remarks>
    /// A delete rewrites stored state, so each of these has to be refused or
    /// answered explicitly. The results a delete reports are read by a Client
    /// through Method output arguments, which is why the collections in them
    /// are normalized: an uninitialized <see cref="ImmutableArray{T}"/> throws
    /// on enumeration, and a Client cannot tell that apart from a Server that
    /// answered nothing.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    public sealed class WotDeleteBoundaryTests
    {
        private WotRegistryService m_registry = null!;
        private FakeWotProjectionHost m_host = null!;
        private FakeWotDocumentConverter m_converter = null!;
        private WotMaterializationCoordinator m_coordinator = null!;

        [SetUp]
        public void SetUp()
        {
            m_registry = new WotRegistryService();
            m_host = new FakeWotProjectionHost();
            m_converter = new FakeWotDocumentConverter();
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, m_host, documentConverter: m_converter);
        }

        [TearDown]
        public void TearDown()
        {
            m_coordinator.Dispose();
            m_registry.Dispose();
        }

        /// <summary>
        /// The walk reads every stored document, so it needs a registry to read
        /// from, a target to walk towards and a reader to read with. Each
        /// missing one is named rather than surfacing later as a null
        /// dereference in the middle of a delete.
        /// </summary>
        [TestCase("snapshot")]
        [TestCase("target")]
        [TestCase("readContent")]
        public async Task TheWalkNamesTheArgumentItWasNotGivenAsync(string missing)
        {
            await RegisterAsync(
                WotRegistryGroups.ThingModels,
                "tm-a",
                WoTDocumentKindEnum.ThingModel,
                TestMaterialization.Tm("urn:tm-a")).ConfigureAwait(false);
            WotRegistrySnapshot snapshot = m_registry.Current;
            WotResource target = snapshot.FindResource(
                WotRegistryGroups.ThingModels, "tm-a")!;

            Assert.That(
                async () => await WotDependencyGraph.FindDependentsWithFaultsAsync(
                    missing == "snapshot" ? null! : snapshot,
                    missing == "target" ? null! : target,
                    64,
                    missing == "readContent"
                        ? null!
                        : (_, _) => new ValueTask<ByteString>(ByteString.Empty),
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo(missing));
        }

        /// <summary>
        /// A <c>tm:extends</c> href that names no stored document is an edge
        /// that resolves to nothing. It is still an edge - the document does
        /// reference something - but deleting anything else cannot break it,
        /// so it never makes its document a dependent.
        /// </summary>
        [Test]
        public async Task AnHrefNoStoredDocumentAnswersToIsNoDependencyAsync()
        {
            await RegisterAsync(
                WotRegistryGroups.ThingModels,
                "tm-a",
                WoTDocumentKindEnum.ThingModel,
                TestMaterialization.Tm("urn:tm-a")).ConfigureAwait(false);
            await RegisterAsync(
                WotRegistryGroups.ThingDescriptions,
                "td-dangling",
                WoTDocumentKindEnum.ThingDescription,
                TestMaterialization.Td("urn:td-dangling", extendsHrefs: "urn:nowhere"))
                .ConfigureAwait(false);

            ImmutableArray<WotDependent> dependents =
                await WotDependencyGraph.FindDependentsAsync(
                    m_registry.Current,
                    m_registry.Current.FindResource(WotRegistryGroups.ThingModels, "tm-a")!,
                    64,
                    m_registry.ReadContentAsync,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                dependents,
                Is.Empty,
                "The dangling document references something, but not the target.");
        }

        /// <summary>
        /// The four policies are the ones the Binding states. A value outside
        /// them is not quietly read as the most destructive one: the target is
        /// removed, and nothing else is unloaded or marked failed, because no
        /// rule said to.
        /// </summary>
        [Test]
        public async Task APolicyTheServerDoesNotKnowTouchesNoDependentAsync()
        {
            await RegisterAsync(
                WotRegistryGroups.ThingModels,
                "tm-a",
                WoTDocumentKindEnum.ThingModel,
                TestMaterialization.Tm("urn:tm-a")).ConfigureAwait(false);
            await RegisterAsync(
                WotRegistryGroups.ThingDescriptions,
                "td-a",
                WoTDocumentKindEnum.ThingDescription,
                TestMaterialization.Td("urn:td-a", extendsHrefs: "urn:tm-a"))
                .ConfigureAwait(false);

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels,
                "tm-a",
                (WoTDeletePolicyEnum)0x7F).ConfigureAwait(false);

            WotResource? dependent = m_registry.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "td-a");

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(result.Deleted, Is.True);
                Assert.That(result.Dependents, Has.Length.EqualTo(1));
                Assert.That(result.Unloaded, Is.Empty);
                Assert.That(result.Failed, Is.Empty);
                Assert.That(dependent, Is.Not.Null);
                Assert.That(dependent!.Enabled, Is.True);
            });
        }

        /// <summary>
        /// A request that carries no id is answered with the empty id rather
        /// than with a null one: the id is echoed into a summary and an event a
        /// Client reads, and a null there is a value no Client asked for.
        /// </summary>
        [Test]
        public async Task ADeleteWithNoRequestIdEchoesTheEmptyIdAsync()
        {
            var events = new System.Collections.Generic.List<WotMaterializationEventArgs>();
            m_coordinator.Event += (_, e) => events.Add(e);
            await RegisterAsync(
                WotRegistryGroups.ThingModels,
                "tm-a",
                WoTDocumentKindEnum.ThingModel,
                TestMaterialization.Tm("urn:tm-a")).ConfigureAwait(false);
            await RegisterAsync(
                WotRegistryGroups.ThingDescriptions,
                "td-a",
                WoTDocumentKindEnum.ThingDescription,
                TestMaterialization.Td("urn:td-a", extendsHrefs: "urn:tm-a"))
                .ConfigureAwait(false);

            WotDeleteOutcome refused = await m_coordinator.DeleteAsync(new WotDeleteRequest
            {
                GroupId = WotRegistryGroups.ThingModels,
                ResourceId = "tm-a",
                Policy = WoTDeletePolicyEnum.Reject
            }).ConfigureAwait(false);
            WotDeleteOutcome accepted = await m_coordinator.DeleteAsync(new WotDeleteRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "td-a",
                Policy = WoTDeletePolicyEnum.Cascade
            }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(refused.Delete.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(refused.Summary.RequestId, Is.Empty);
                Assert.That(refused.Results, Is.Empty);
                Assert.That(accepted.Delete.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(accepted.Summary.RequestId, Is.Empty);
                Assert.That(
                    events.Any(e =>
                        e.Kind == WotMaterializationEventKind.RefreshCompleted &&
                        e.RequestId.Length == 0),
                    Is.True);
            });
        }

        /// <summary>
        /// A disposed coordinator owns nothing to delete from, so a delete that
        /// arrives after disposal is refused by name rather than running
        /// against half-released state.
        /// </summary>
        [Test]
        public void ADeleteAfterDisposalIsRefused()
        {
            m_coordinator.Dispose();

            Assert.That(
                async () => await m_coordinator.DeleteAsync(new WotDeleteRequest
                {
                    GroupId = WotRegistryGroups.ThingModels,
                    ResourceId = "tm-a",
                    Policy = WoTDeletePolicyEnum.Reject
                }).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        /// <summary>
        /// A delete request with no request object at all is refused before
        /// anything is read.
        /// </summary>
        [Test]
        public void ADeleteWithNoRequestIsRefused()
        {
            Assert.That(
                async () => await m_coordinator.DeleteAsync(null!).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }

        /// <summary>
        /// The collections a delete reports are what a Client enumerates out of
        /// the Method's output arguments. An uninitialized
        /// <see cref="ImmutableArray{T}"/> throws when enumerated, so a result
        /// built without one reports the empty set instead.
        /// </summary>
        [Test]
        public void ADeleteResultNeverReportsAnUninitializedCollection()
        {
            var result = new WotDeleteResult(
                WoTOutcomeEnum.Success,
                WoTDeletePolicyEnum.Force,
                generation: 7,
                deleted: true,
                retired: true,
                dependents: default,
                unloaded: default,
                failed: default,
                unreadable: [],
                message: "done");

            Assert.Multiple(() =>
            {
                Assert.That(result.Dependents, Is.Empty);
                Assert.That(result.Unloaded, Is.Empty);
                Assert.That(result.Failed, Is.Empty);
                Assert.That(result.Dependents.IsDefault, Is.False);
                Assert.That(result.Unloaded.IsDefault, Is.False);
                Assert.That(result.Failed.IsDefault, Is.False);
            });
        }

        /// <summary>
        /// The same holds for the per-resource results of the reconciliation a
        /// delete triggers.
        /// </summary>
        [Test]
        public void ADeleteOutcomeNeverReportsAnUninitializedResultSet()
        {
            var outcome = new WotDeleteOutcome(
                new WotDeleteResult(
                    WoTOutcomeEnum.Success,
                    WoTDeletePolicyEnum.Force,
                    generation: 7,
                    deleted: true,
                    retired: true,
                    dependents: [],
                    unloaded: [],
                    failed: [],
                    unreadable: [],
                    message: "done"),
                new WoTRefreshSummaryDataType(),
                results: default,
                generation: 7);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Results, Is.Empty);
                Assert.That(outcome.Results.IsDefault, Is.False);
                Assert.That(outcome.Generation, Is.EqualTo(7u));
            });
        }

        private async Task RegisterAsync(
            string groupId, string resourceId, WoTDocumentKindEnum kind, byte[] content)
        {
            await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = groupId,
                ResourceId = resourceId,
                Kind = kind,
                Content = ByteString.From(content)
            }).ConfigureAwait(false);
        }
    }
}
