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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// WoT Connectivity's four delete policies answer one question - what
    /// happens to the documents that were resolving through the one being
    /// deleted - and they have to answer it differently. These pin each answer
    /// in the registry and, through the coordinator, in the AddressSpace.
    /// </summary>
    [TestFixture]
    public sealed class WotDeletePolicyTests
    {
        private const string ModelXid = "/groups/thingmodels/resources/tm-a";
        private const string InstanceXid = "/groups/thingdescriptions/resources/td-a";
        private const string SecondInstanceXid = "/groups/thingdescriptions/resources/td-b";

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
        /// Reject leaves everything as it was. That is the whole point of it:
        /// if a rejected delete moved any state, the difference between Reject
        /// and Force would only be a message.
        /// </summary>
        [Test]
        public async Task RejectRefusesWhileADependentExistsAndChangesNothing()
        {
            await RegisterModelAndInstanceAsync();
            long generation = m_registry.Current.Generation;
            WotResource before = ModelResource()!;

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "tm-a", WoTDeletePolicyEnum.Reject);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(result.Policy, Is.EqualTo(WoTDeletePolicyEnum.Reject));
                Assert.That(result.Deleted, Is.False);
                Assert.That(result.Retired, Is.False);
                Assert.That(result.Dependents, Is.EqualTo(new[] { InstanceXid }).AsCollection);
                Assert.That(result.Unloaded, Is.Empty);
                Assert.That(result.Failed, Is.Empty);
                Assert.That(result.Generation, Is.EqualTo(generation));
                Assert.That(m_registry.Current.Generation, Is.EqualTo(generation));
                Assert.That(ModelResource(), Is.Not.Null);
                Assert.That(ModelResource()!.Epoch, Is.EqualTo(before.Epoch));
                Assert.That(ModelResource()!.Enabled, Is.True);
                Assert.That(ModelResource()!.LoadState, Is.EqualTo(before.LoadState));
                Assert.That(InstanceResource()!.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            });
        }

        /// <summary>
        /// Reject with nothing depending on the document is an ordinary delete:
        /// the policy is about dependents, not about deleting.
        /// </summary>
        [Test]
        public async Task RejectDeletesWhenNothingDependsOnIt()
        {
            await RegisterModelAsync();
            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "tm-a", WoTDeletePolicyEnum.Reject);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(result.Deleted, Is.True);
                Assert.That(result.Dependents, Is.Empty);
                Assert.That(ModelResource(), Is.Null);
            });
        }

        /// <summary>
        /// Retire takes the projection down and keeps the document, so its
        /// dependents keep resolving through it.
        /// </summary>
        [Test]
        public async Task RetireRemovesTheProjectionAndKeepsTheDocumentResolvable()
        {
            await RegisterModelAndInstanceAsync();

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "tm-a", WoTDeletePolicyEnum.Retire);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(result.Deleted, Is.False);
                Assert.That(result.Retired, Is.True);
                Assert.That(result.Dependents, Is.EqualTo(new[] { InstanceXid }).AsCollection);
                Assert.That(result.Unloaded, Is.Empty);
                Assert.That(result.Failed, Is.Empty);
                Assert.That(result.Message, Does.Contain("remains stored and resolvable"));
                Assert.That(ModelResource(), Is.Not.Null);
                Assert.That(ModelResource()!.Enabled, Is.False);
                Assert.That(ModelResource()!.LoadState, Is.EqualTo(WoTLoadStateEnum.Retired));
                Assert.That(
                    WotDependencyGraph.Resolve(m_registry.Current, "urn:tm-a"),
                    Is.Not.Null,
                    "A retired document is still there for its dependents to resolve.");
            });
        }

        /// <summary>
        /// Cascade unloads a dependent that has no other way to resolve what it
        /// took from the deleted document.
        /// </summary>
        [Test]
        public async Task CascadeUnloadsADependentThatResolvesOnlyThroughIt()
        {
            await RegisterModelAndInstanceAsync();

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "tm-a", WoTDeletePolicyEnum.Cascade);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(result.Deleted, Is.True);
                Assert.That(result.Unloaded, Is.EqualTo(new[] { InstanceXid }).AsCollection);
                Assert.That(result.Failed, Is.Empty);
                Assert.That(ModelResource(), Is.Null);
                Assert.That(InstanceResource(), Is.Not.Null);
                Assert.That(InstanceResource()!.Enabled, Is.False);
                Assert.That(
                    InstanceResource()!.LoadState, Is.EqualTo(WoTLoadStateEnum.Unloaded));
            });
        }

        /// <summary>
        /// A dependent whose reference is still answered by another stored
        /// document was never in danger, so Cascade leaves its projection
        /// alone. Unloading it would remove something the delete did not break.
        /// </summary>
        [Test]
        public async Task CascadeLeavesADependentThatStillResolvesElsewhere()
        {
            await RegisterAlternativeResolutionAsync();

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "tm-a", WoTDeletePolicyEnum.Cascade);

            Assert.Multiple(() =>
            {
                Assert.That(result.Dependents, Is.EqualTo(new[] { InstanceXid }).AsCollection);
                Assert.That(result.Unloaded, Is.Empty);
                Assert.That(InstanceResource()!.Enabled, Is.True);
                Assert.That(
                    InstanceResource()!.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            });
        }

        /// <summary>
        /// Cascade is transitive: a document that resolved only through a
        /// document that resolved only through the target is gone as well.
        /// </summary>
        [Test]
        public async Task CascadeFollowsTheDependencyChain()
        {
            await RegisterModelAsync();
            await RegisterAsync(
                WotRegistryGroups.ThingModels,
                "tm-b",
                WoTDocumentKindEnum.ThingModel,
                TestMaterialization.Tm("urn:tm-b", extendsHrefs: "urn:tm-a"));
            await RegisterAsync(
                WotRegistryGroups.ThingDescriptions,
                "td-b",
                WoTDocumentKindEnum.ThingDescription,
                TestMaterialization.Td("urn:td-b", extendsHrefs: "urn:tm-b"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "tm-a", WoTDeletePolicyEnum.Cascade);

            Assert.That(
                result.Unloaded,
                Is.EqualTo(new[] { SecondInstanceXid, "/groups/thingmodels/resources/tm-b" })
                    .AsCollection);
        }

        /// <summary>
        /// Force deletes regardless and says what it broke: the dependents are
        /// marked Failed because they are now projecting against something that
        /// is gone.
        /// </summary>
        [Test]
        public async Task ForceDeletesAndMarksRemainingDependentsFailed()
        {
            await RegisterModelAndInstanceAsync();

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "tm-a", WoTDeletePolicyEnum.Force);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(result.Deleted, Is.True);
                Assert.That(result.Failed, Is.EqualTo(new[] { InstanceXid }).AsCollection);
                Assert.That(result.Unloaded, Is.Empty);
                Assert.That(result.Message, Does.Contain("force-deleted"));
                Assert.That(ModelResource(), Is.Null);
                Assert.That(InstanceResource()!.LoadState, Is.EqualTo(WoTLoadStateEnum.Failed));
                Assert.That(InstanceResource()!.Enabled, Is.False);
                Assert.That(
                    InstanceResource()!.Diagnostics.Single(),
                    Does.Contain("force-deleted"));
            });
        }

        /// <summary>
        /// Force marks even a dependent that could still resolve elsewhere:
        /// unlike Cascade, it is not asking whether it broke anything, it is
        /// reporting that it deleted while dependents existed.
        /// </summary>
        [Test]
        public async Task ForceMarksEveryDependentIncludingOnesThatStillResolve()
        {
            await RegisterAlternativeResolutionAsync();

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "tm-a", WoTDeletePolicyEnum.Force);

            Assert.Multiple(() =>
            {
                Assert.That(result.Failed, Is.EqualTo(new[] { InstanceXid }).AsCollection);
                Assert.That(
                    InstanceResource()!.LoadState, Is.EqualTo(WoTLoadStateEnum.Failed));
            });
        }

        /// <summary>
        /// Registers a model, a Thing Description that extends it by its exact
        /// xid, and a second document the same reference also answers to - so
        /// deleting the model leaves the dependent with a way to resolve.
        /// </summary>
        private async Task RegisterAlternativeResolutionAsync()
        {
            await RegisterModelAsync();
            await RegisterAsync(
                WotRegistryGroups.ThingDescriptions,
                "tm-a",
                WoTDocumentKindEnum.ThingDescription,
                TestMaterialization.Td("urn:tm-a-mirror"));
            await RegisterAsync(
                WotRegistryGroups.ThingDescriptions,
                "td-a",
                WoTDocumentKindEnum.ThingDescription,
                TestMaterialization.Td("urn:td-a", extendsHrefs: ModelXid));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());
        }

        [Test]
        public async Task AnUnknownResourceFailsWithoutTouchingTheRegistry()
        {
            await RegisterModelAsync();
            long generation = m_registry.Current.Generation;

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "missing", WoTDeletePolicyEnum.Force);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
                Assert.That(result.Message, Is.EqualTo("Resource not found."));
                Assert.That(m_registry.Current.Generation, Is.EqualTo(generation));
            });
        }

        [Test]
        public async Task AnEpochMismatchIsRejectedWithoutTouchingTheRegistry()
        {
            await RegisterModelAsync();
            long generation = m_registry.Current.Generation;

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels,
                "tm-a",
                WoTDeletePolicyEnum.Force,
                expectedEpoch: ModelResource()!.Epoch + 99);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(result.Message, Is.EqualTo("Epoch mismatch."));
                Assert.That(m_registry.Current.Generation, Is.EqualTo(generation));
                Assert.That(ModelResource(), Is.Not.Null);
            });
        }

        /// <summary>
        /// Through the coordinator, a rejected delete reconciles nothing: no
        /// projection is taken down, and the summary says so.
        /// </summary>
        [Test]
        public async Task TheCoordinatorReconcilesNothingForARejectedDelete()
        {
            await RegisterModelAndInstanceAsync();
            int removedBefore = m_host.RemoveCount;
            uint generation = m_coordinator.Generation;
            var events = new List<WotMaterializationEventArgs>();
            m_coordinator.Event += (_, e) => events.Add(e);

            WotDeleteOutcome outcome = await m_coordinator.DeleteAsync(new WotDeleteRequest
            {
                GroupId = WotRegistryGroups.ThingModels,
                ResourceId = "tm-a",
                Policy = WoTDeletePolicyEnum.Reject,
                RequestId = "r-1"
            });

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Delete.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(outcome.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(outcome.Summary.RequestId, Is.EqualTo("r-1"));
                Assert.That(outcome.Summary.Total, Is.Zero);
                Assert.That(outcome.Summary.Retired, Is.Zero);
                Assert.That(outcome.Summary.Failed, Is.Zero);
                Assert.That(outcome.Results, Is.Empty);
                Assert.That(outcome.Generation, Is.EqualTo(generation));
                Assert.That(m_host.RemoveCount, Is.EqualTo(removedBefore));
                Assert.That(
                    events.Single(e => e.Kind == WotMaterializationEventKind.RefreshCompleted)
                        .Reason,
                    Does.Contain("delete policy is Reject"));
            });
        }

        /// <summary>
        /// A cascaded delete is reconciled: the closure that held both
        /// documents is taken out of the AddressSpace, and the summary counts
        /// it as retired.
        /// </summary>
        [Test]
        public async Task TheCoordinatorRetiresTheProjectionOfACascadedDelete()
        {
            await RegisterModelAndInstanceAsync();
            int removedBefore = m_host.RemoveCount;
            var events = new List<WotMaterializationEventArgs>();
            m_coordinator.Event += (_, e) => events.Add(e);

            WotDeleteOutcome outcome = await m_coordinator.DeleteAsync(new WotDeleteRequest
            {
                GroupId = WotRegistryGroups.ThingModels,
                ResourceId = "tm-a",
                Policy = WoTDeletePolicyEnum.Cascade,
                RequestId = "r-2"
            });

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Delete.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(
                    outcome.Delete.Unloaded, Is.EqualTo(new[] { InstanceXid }).AsCollection);
                Assert.That(outcome.Summary.RequestId, Is.EqualTo("r-2"));
                Assert.That(outcome.Summary.Retired, Is.EqualTo(1));
                Assert.That(m_host.RemoveCount, Is.EqualTo(removedBefore + 1));
                Assert.That(
                    events.Any(e => e.Kind == WotMaterializationEventKind.RefreshCompleted),
                    Is.True);
            });
        }

        /// <summary>
        /// A retired document keeps resolving, so a dependent that is still
        /// enabled keeps projecting - which is exactly what separates Retire
        /// from Cascade.
        /// </summary>
        [Test]
        public async Task TheCoordinatorKeepsADependentProjectedAfterRetire()
        {
            await RegisterModelAndInstanceAsync();

            WotDeleteOutcome outcome = await m_coordinator.DeleteAsync(new WotDeleteRequest
            {
                GroupId = WotRegistryGroups.ThingModels,
                ResourceId = "tm-a",
                Policy = WoTDeletePolicyEnum.Retire,
                RequestId = "r-3"
            });

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Delete.Retired, Is.True);
                Assert.That(outcome.Delete.Deleted, Is.False);
                Assert.That(InstanceResource()!.Enabled, Is.True);
                Assert.That(
                    InstanceResource()!.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            });
        }

        [Test]
        public void TheCoordinatorRejectsANullRequest()
        {
            Assert.That(
                async () => await m_coordinator.DeleteAsync(null!),
                Throws.ArgumentNullException);
        }

        private WotResource? ModelResource()
        {
            return m_registry.Current.FindResource(WotRegistryGroups.ThingModels, "tm-a");
        }

        private WotResource? InstanceResource()
        {
            return m_registry.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "td-a");
        }

        private Task RegisterModelAsync()
        {
            return RegisterAsync(
                WotRegistryGroups.ThingModels,
                "tm-a",
                WoTDocumentKindEnum.ThingModel,
                TestMaterialization.Tm("urn:tm-a"));
        }

        private Task RegisterInstanceAsync()
        {
            return RegisterAsync(
                WotRegistryGroups.ThingDescriptions,
                "td-a",
                WoTDocumentKindEnum.ThingDescription,
                TestMaterialization.Td("urn:td-a", extendsHrefs: "urn:tm-a"));
        }

        private async Task RegisterModelAndInstanceAsync()
        {
            await RegisterModelAsync();
            await RegisterInstanceAsync();
            await m_coordinator.RefreshAsync(new WotRefreshRequest());
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
            });
        }
    }
}
