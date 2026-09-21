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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [TestCase(WoTAtomicityEnum.PerResource, false, 2)]
        [TestCase(WoTAtomicityEnum.PerResource, true, 2)]
        [TestCase(WoTAtomicityEnum.PerGroup, false, 1)]
        [TestCase(WoTAtomicityEnum.PerGroup, true, 2)]
        [TestCase(WoTAtomicityEnum.PerClosure, false, 2)]
        [TestCase(WoTAtomicityEnum.PerClosure, true, 2)]
        [TestCase(WoTAtomicityEnum.PerRegistry, false, 1)]
        [TestCase(WoTAtomicityEnum.PerRegistry, true, 1)]
        [Platform("Win")]
        public async Task AtomicityPublishesExactlyTheSelectedNativeUnits(
            WoTAtomicityEnum atomicity, bool differentGroups, int unitCount)
        {
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = differentGroups
                ? await AddUnitModelAsync("second").ConfigureAwait(false)
                : await AddAsync("second").ConfigureAwait(false);
            WotResource unrelated = await AddAsync("unrelated").ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            var images = new List<WotRegistrySnapshot>();
            EventHandler<WotRegistryChangedEventArgs> observe = (_, change) => images.Add(change.Current);
            m_registry.Changed += observe;
            WotRefreshResult result;
            try
            {
                result = await m_coordinator.RefreshAsync(new WotRefreshRequest
                {
                    RequestId = "actual-units",
                    Selection = [UnitSelector(first), UnitSelector(second)],
                    Options = new WoTRefreshOptionsDataType { Atomicity = atomicity, MaxParallelism = 1 }
                }).ConfigureAwait(false);
            }
            finally
            {
                m_registry.Changed -= observe;
            }

            Assert.That(result.Summary.Atomicity, Is.EqualTo(atomicity));
            Assert.That(result.NewGeneration, Is.EqualTo((uint)unitCount));
            Assert.That(result.Summary.Generation, Is.EqualTo((uint)unitCount));
            Assert.That(m_coordinator.Generation, Is.EqualTo((uint)unitCount));
            Assert.That(m_registry.Current.RefreshGeneration, Is.EqualTo((uint)unitCount));
            Assert.That(m_registry.Current.Generation, Is.EqualTo(before.Generation + unitCount));
            Assert.That(result.Summary.Total, Is.EqualTo(2u));
            Assert.That(result.Summary.Succeeded, Is.EqualTo(2u));
            Assert.That(result.Summary.Failed, Is.Zero);
            Assert.That(images, Has.Count.EqualTo(unitCount));
            for (int i = 0; i < images.Count; i++)
            {
                Assert.That(images[i].RefreshGeneration, Is.EqualTo((uint)i + 1));
                Assert.That(images[i].AllResources().Count(resource => resource.ActiveVersionId == "v1"),
                    Is.EqualTo(unitCount == 1 ? 2 : i + 1));
                Assert.That(images[i].FindResource(unrelated.GroupId, unrelated.ResourceId), Is.SameAs(unrelated));
            }
            Assert.That(m_coordinator.CommittedPublication.RegistrySnapshot, Is.SameAs(m_registry.Current));
            Assert.That(result.Results.OrderBy(row => row.Generation).Select(row => row.Generation).Distinct(),
                Is.EqualTo(unitCount == 1 ? new uint[] { 1 } : [1u, 2u]));
            ReadResponse read = await m_session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = Root(first), AttributeId = Attributes.NodeClass },
                    new ReadValueId { NodeId = Root(second), AttributeId = Attributes.NodeClass },
                    new ReadValueId { NodeId = Root(unrelated), AttributeId = Attributes.NodeClass }
                ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(read.Results.ToList().Select(value => value.StatusCode),
                Is.EqualTo(new StatusCode[] { StatusCodes.Good, StatusCodes.Good, StatusCodes.BadNodeIdUnknown }));
            using var observer = new FileWotRegistryStore(Path.Combine(m_root, "registry"));
            WotRegistrySnapshot durable = await observer.LoadAsync().ConfigureAwait(false);
            Assert.That(durable.RefreshGeneration, Is.EqualTo((uint)unitCount));
            Assert.That(durable.Generation, Is.EqualTo(before.Generation + unitCount));
            Assert.That(durable.AllResources().Count(resource => resource.ActiveVersionId == "v1"), Is.EqualTo(2));
        }

        [TestCase(WoTAtomicityEnum.PerResource, 2)]
        [TestCase(WoTAtomicityEnum.PerGroup, 2)]
        [TestCase(WoTAtomicityEnum.PerClosure, 1)]
        [TestCase(WoTAtomicityEnum.PerRegistry, 1)]
        [Platform("Win")]
        public async Task AcyclicDependencyPublishesItsExactPrerequisiteFirst(
            WoTAtomicityEnum atomicity, int unitCount)
        {
            WotResource dependency = await AddUnitModelAsync("dependency").ConfigureAwait(false);
            WotResource dependent = await AddAsync("dependent").ConfigureAwait(false);
            await SetUnitDependencyAsync(dependent, "dependency").ConfigureAwait(false);
            var images = new List<WotRegistrySnapshot>();
            EventHandler<WotRegistryChangedEventArgs> observe = (_, change) => images.Add(change.Current);
            m_registry.Changed += observe;
            WotRefreshResult result;
            try
            {
                result = await m_coordinator.RefreshAsync(new WotRefreshRequest
                {
                    Selection = [UnitSelector(dependent)],
                    Options = new WoTRefreshOptionsDataType { Atomicity = atomicity }
                }).ConfigureAwait(false);
            }
            finally
            {
                m_registry.Changed -= observe;
            }
            Assert.That(result.Summary.Atomicity, Is.EqualTo(atomicity));
            Assert.That(result.NewGeneration, Is.EqualTo((uint)unitCount));
            Assert.That(result.Summary.Succeeded, Is.EqualTo(2u));
            Assert.That(images, Has.Count.EqualTo(unitCount));
            Assert.That(images[0].FindResource(dependency.GroupId, dependency.ResourceId)!.ActiveVersionId,
                Is.EqualTo("v1"));
            Assert.That(images[0].FindResource(dependent.GroupId, dependent.ResourceId)!.ActiveVersionId,
                unitCount == 1 ? Is.EqualTo("v1") : Is.Null);
            Assert.That((await ReadNodeClassAsync(Root(dependency)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((await ReadNodeClassAsync(Root(dependent)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        [Platform("Win")]
        public async Task FailedPrerequisiteBlocksDependentButKeepsIndependentNativeSuccess()
        {
            WotResource dependency = await AddUnitModelAsync("dependency").ConfigureAwait(false);
            WotResource dependent = await AddAsync("dependent").ConfigureAwait(false);
            WotResource independent = await AddAsync("independent").ConfigureAwait(false);
            await SetUnitDependencyAsync(dependent, "dependency").ConfigureAwait(false);
            m_converter.MarkInvalid(dependency.ResourceId);

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource }
            }).ConfigureAwait(false);

            Assert.That(result.Summary.Total, Is.EqualTo(3u));
            Assert.That(result.Summary.Failed, Is.EqualTo(2u));
            Assert.That(result.Summary.Succeeded, Is.EqualTo(1u));
            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(result.Results.Single(row => row.ResourceId == dependent.ResourceId).Phase,
                Is.EqualTo(WoTPhaseEnum.DependencyResolution));
            Assert.That((await ReadNodeClassAsync(Root(dependency)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That((await ReadNodeClassAsync(Root(dependent)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That((await ReadNodeClassAsync(Root(independent)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        [Platform("Win")]
        public async Task DisabledResolutionInputNeverGetsANativeActivationOwner()
        {
            WotResource dependency = await AddUnitModelAsync("dependency").ConfigureAwait(false);
            WotResource dependent = await AddAsync("dependent").ConfigureAwait(false);
            await SetUnitDependencyAsync(dependent, "dependency").ConfigureAwait(false);
            await m_registry.SetEnabledAsync(dependency.GroupId, dependency.ResourceId, false).ConfigureAwait(false);

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Selection = [UnitSelector(dependent)],
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerGroup }
            }).ConfigureAwait(false);

            Assert.That(result.Summary.Atomicity, Is.EqualTo(WoTAtomicityEnum.PerGroup));
            Assert.That(result.Summary.Total, Is.EqualTo(1u));
            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That((await ReadNodeClassAsync(Root(dependent)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((await ReadNodeClassAsync(Root(dependency)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(m_registry.Current.FindResource(dependency.GroupId, dependency.ResourceId)!.ActiveVersionId,
                Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        [Platform("Win")]
        public async Task ReplacementMergeOrSplitKeepsTheCompleteOldNativeClosureUntilSuccess(bool split)
        {
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            WotResource unrelated = await AddAsync("unrelated").ConfigureAwait(false);
            if (split)
            {
                await SetUnitDependencyAsync(first, "second").ConfigureAwait(false);
            }
            WotRefreshResult initial = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Selection = [UnitSelector(first), UnitSelector(second)],
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerClosure }
            }).ConfigureAwait(false);
            Assert.That(initial.NewGeneration, Is.EqualTo(split ? 1u : 2u));
            await UpdateHandoffResourceAsync(first).ConfigureAwait(false);
            await UpdateHandoffResourceAsync(second).ConfigureAwait(false);
            if (!split)
            {
                WotRegistryMutationResult update = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = first.GroupId,
                    ResourceId = first.ResourceId,
                    VersionId = "v2",
                    Kind = first.Kind,
                    Content = ByteString.From(TestMaterialization.Td("urn:first", "2", extendsHrefs: "urn:second"))
                }).ConfigureAwait(false);
                Assert.That(update.Changed, Is.True, update.Message);
            }
            m_converter.MarkInvalid(second.ResourceId);
            var request = new WotRefreshRequest
            {
                Selection = [UnitSelector(first)],
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource }
            };

            WotRefreshResult rejected = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

            Assert.That(rejected.NewGeneration, Is.EqualTo(initial.NewGeneration));
            Assert.That(rejected.Summary.Atomicity, Is.EqualTo(WoTAtomicityEnum.PerClosure));
            Assert.That(rejected.Summary.Total, Is.EqualTo(2u));
            Assert.That(rejected.Summary.Failed, Is.EqualTo(2u));
            foreach (WotResource resource in new[] { first, second })
            {
                Assert.That(m_registry.Current.FindResource(resource.GroupId, resource.ResourceId)!.ActiveVersionId,
                    Is.EqualTo("v1"));
                Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                Assert.That((await ReadNodeClassAsync(NewHandoffNode(resource)).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
            m_converter.ClearInvalid(second.ResourceId);

            WotRefreshResult committed = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

            Assert.That(committed.NewGeneration, Is.EqualTo(initial.NewGeneration + 1));
            Assert.That(committed.Summary.Atomicity, Is.EqualTo(WoTAtomicityEnum.PerClosure));
            Assert.That(committed.Summary.Succeeded, Is.EqualTo(2u));
            foreach (WotResource resource in new[] { first, second })
            {
                Assert.That(m_registry.Current.FindResource(resource.GroupId, resource.ResourceId)!.ActiveVersionId,
                    Is.EqualTo("v2"));
                Assert.That((await ReadNodeClassAsync(NewHandoffNode(resource)).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
            }
            Assert.That(m_registry.Current.FindResource(unrelated.GroupId, unrelated.ResourceId), Is.SameAs(unrelated));
            Assert.That((await ReadNodeClassAsync(Root(unrelated)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [TestCase(WoTAtomicityEnum.PerResource, false, 1)]
        [TestCase(WoTAtomicityEnum.PerResource, true, 1)]
        [TestCase(WoTAtomicityEnum.PerGroup, false, 0)]
        [TestCase(WoTAtomicityEnum.PerGroup, true, 0)]
        [TestCase(WoTAtomicityEnum.PerClosure, false, 1)]
        [TestCase(WoTAtomicityEnum.PerClosure, true, 1)]
        [TestCase(WoTAtomicityEnum.PerRegistry, false, 0)]
        [TestCase(WoTAtomicityEnum.PerRegistry, true, 0)]
        [Platform("Win")]
        public async Task FailedPeerStaysInItsIntendedNativeUnit(
            WoTAtomicityEnum atomicity, bool invalidFirst, int committedUnits)
        {
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            WotResource bad = invalidFirst ? first : second;
            WotResource good = invalidFirst ? second : first;
            m_converter.MarkInvalid(bad.ResourceId);

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = atomicity }
            }).ConfigureAwait(false);

            Assert.That(result.Summary.Total, Is.EqualTo(2u));
            Assert.That(result.Summary.Succeeded, Is.EqualTo((uint)committedUnits));
            Assert.That(result.Summary.Failed, Is.EqualTo((uint)(2 - committedUnits)));
            Assert.That(result.NewGeneration, Is.EqualTo((uint)committedUnits));
            Assert.That(result.Summary.Generation, Is.EqualTo((uint)committedUnits));
            Assert.That(result.Results.Single(row => row.ResourceId == bad.ResourceId).Phase,
                Is.EqualTo(WoTPhaseEnum.FormatValidation));
            Assert.That((await ReadNodeClassAsync(Root(bad)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That((await ReadNodeClassAsync(Root(good)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(committedUnits == 0 ? StatusCodes.BadNodeIdUnknown : StatusCodes.Good));
            if (committedUnits == 0)
            {
                Assert.That(result.Results.Single(row => row.ResourceId == good.ResourceId).Phase,
                    Is.EqualTo(WoTPhaseEnum.Activation));
            }
            using var observer = new FileWotRegistryStore(Path.Combine(m_root, "registry"));
            WotRegistrySnapshot durable = await observer.LoadAsync().ConfigureAwait(false);
            Assert.That(durable.RefreshGeneration, Is.EqualTo((uint)committedUnits));
        }

        private async Task<WotResource> AddUnitModelAsync(string id)
        {
            WotRegistryMutationResult created = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingModels,
                ResourceId = id,
                VersionId = "v1",
                Kind = WoTDocumentKindEnum.ThingModel,
                Content = ByteString.From(TestMaterialization.Tm("urn:" + id))
            }).ConfigureAwait(false);
            Assert.That(created.Changed, Is.True, created.Message);
            WotResource resource = created.Resource ?? throw new InvalidOperationException("No test model.");
            m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(ModelUri(resource));
            m_converter.SetRootNodeId(resource.ResourceId, new ExpandedNodeId(5000u, ModelUri(resource)));
            return resource;
        }

        private async Task SetUnitDependencyAsync(WotResource resource, string target)
        {
            WotRegistryMutationResult updated = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = resource.GroupId,
                ResourceId = resource.ResourceId,
                VersionId = "v1",
                Kind = resource.Kind,
                Content = ByteString.From(TestMaterialization.Td("urn:" + resource.ResourceId,
                    extendsHrefs: "urn:" + target))
            }).ConfigureAwait(false);
            Assert.That(updated.Changed, Is.True, updated.Message);
        }

        private static WoTResourceSelectorDataType UnitSelector(WotResource resource)
        {
            return new WoTResourceSelectorDataType
            {
                Kind = resource.Kind,
                GroupId = resource.GroupId,
                ResourceId = resource.ResourceId
            };
        }
    }
}
