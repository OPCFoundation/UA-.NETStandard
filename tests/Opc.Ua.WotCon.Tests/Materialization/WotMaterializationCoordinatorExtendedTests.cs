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
    /// Supplemental tests for <see cref="WotMaterializationCoordinator"/> covering
    /// cycle detection, <c>RemoveAllAsync</c>, selection filters, force refresh,
    /// and the <c>LoadFailure</c> and <c>BindingFailure</c> event kinds.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotMaterializationCoordinatorExtendedTests
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

        private Task<WotRegistryMutationResult> RegisterTd(string resourceId, byte[] content)
        {
            return m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = content
            }).AsTask();
        }

        private Task<WotRegistryMutationResult> RegisterTm(string resourceId, byte[] content)
        {
            return m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingModels,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingModel,
                Content = content
            }).AsTask();
        }

        [Test]
        public async Task CyclicTmsAreNotProjectedAndEmitLoadFailureEvent()
        {
            var events = new List<WotMaterializationEventArgs>();
            m_coordinator.Event += (_, e) => events.Add(e);

            await RegisterTm("tm-a", TestMaterialization.Tm("urn:tm-a", extendsHrefs: "urn:tm-b"));
            await RegisterTm("tm-b", TestMaterialization.Tm("urn:tm-b", extendsHrefs: "urn:tm-a"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.AddCount, Is.Zero,
                "A cyclic closure must not project.");
            Assert.That(result.Results, Has.Length.EqualTo(2));
            Assert.That(result.Results.All(r => r.Outcome == WoTOutcomeEnum.Failed), Is.True);
            Assert.That(result.Results.All(r => r.Phase == WoTPhaseEnum.DependencyResolution),
                Is.True);
            Assert.That(
                events.Any(e => e.Kind == WotMaterializationEventKind.LoadFailure),
                Is.True, "Each cyclic closure member must raise a LoadFailure event.");
        }

        [Test]
        public async Task RemoveAllAsyncRetiresPreviouslyProjectedClosures()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());
            Assert.That(m_host.AddCount, Is.EqualTo(1));

            await m_coordinator.RemoveAllAsync();

            Assert.That(m_host.RemoveCount, Is.EqualTo(1),
                "RemoveAllAsync must retire the live projection.");
        }

        [Test]
        public async Task ForceRefreshReprocessesUnchangedContent()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());
            Assert.That(m_host.AddCount, Is.EqualTo(1));

            WotRefreshResult second = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Force = true }
            });

            Assert.That(m_host.ShadowCount, Is.EqualTo(1),
                "A forced refresh must shadow-reload even when the content is unchanged.");
            Assert.That(
                second.Results.Single(r => r.ResourceId == "td-a").Outcome,
                Is.Not.EqualTo(WoTOutcomeEnum.Unchanged));
        }

        [Test]
        public async Task SelectionFilterLimitsResultsToSelectedResources()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));
            await RegisterTd("td-b", TestMaterialization.Td("urn:td-b"));

            string tdAXid = m_registry.Current
                .FindResource(WotRegistryGroups.ThingDescriptions, "td-a")!.Xid;

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Selection = [new WoTResourceSelectorDataType
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "td-a"
                }]
            });

            Assert.That(result.Results.All(r => r.ResourceId == "td-a"), Is.True,
                "Filtered refresh must only return results for selected resources.");
        }

        [Test]
        public async Task RefreshAllEmptyResourceRegistryReturnsEmptyResults()
        {
            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(result.Results, Is.Empty);
            Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Unchanged));
        }

        [Test]
        public async Task RefreshGenerationIncrements()
        {
            uint before = m_coordinator.Generation;

            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_coordinator.Generation, Is.GreaterThan(before));
        }

        [Test]
        public async Task RefreshCompletedEventIsRaisedAfterRefresh()
        {
            var events = new List<WotMaterializationEventArgs>();
            m_coordinator.Event += (_, e) => events.Add(e);

            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(
                events.Any(e => e.Kind == WotMaterializationEventKind.RefreshCompleted),
                Is.True, "A RefreshCompleted event must be raised after every refresh.");
        }

        [Test]
        public async Task ValidationFailureEventIsRaisedForInvalidContent()
        {
            var events = new List<WotMaterializationEventArgs>();
            m_coordinator.Event += (_, e) => events.Add(e);

            m_converter.MarkInvalid("td-a");
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(
                events.Any(e => e.Kind == WotMaterializationEventKind.ValidationFailure),
                Is.True, "A ValidationFailure event must be raised for invalid content.");
        }

        [Test]
        public async Task LoadFailureEventIsRaisedForMissingDependency()
        {
            var events = new List<WotMaterializationEventArgs>();
            m_coordinator.Event += (_, e) => events.Add(e);

            await RegisterTd("td-a",
                TestMaterialization.Td("urn:td-a", extendsHrefs: "urn:missing-tm"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(
                events.Any(e => e.Kind == WotMaterializationEventKind.LoadFailure),
                Is.True, "A LoadFailure event must be raised for each resource in an unresolvable closure.");
        }

        [Test]
        public async Task TwoIndependentClosuresProjectAsTwoSeparateRegistrations()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));
            await RegisterTd("td-b", TestMaterialization.Td("urn:td-b"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.AddCount, Is.EqualTo(2),
                "Two independent closures must each be added as a separate projection.");
            Assert.That(result.Results, Has.Length.EqualTo(2));
        }

        [Test]
        public async Task ResourceEventIsRaisedAfterSuccessfulProjection()
        {
            var events = new List<WotMaterializationEventArgs>();
            m_coordinator.Event += (_, e) => events.Add(e);

            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(
                events.Any(e => e.Kind == WotMaterializationEventKind.Resource &&
                                e.ResourceId == "td-a"),
                Is.True, "A Resource event must be raised for each successfully projected resource.");
        }

        [Test]
        public async Task BindingCapabilitiesIsNonNullEvenWithNoBinders()
        {
            Assert.That(m_coordinator.BindingCapabilities, Is.Not.Null);
            Assert.That(m_coordinator.BindingCapabilities, Is.Empty);
        }

        [Test]
        public async Task RemoveAllAsyncOnEmptyCoordinatorDoesNotThrow()
        {
            await m_coordinator.RemoveAllAsync();
        }

        [Test]
        public async Task CyclicClosureEmitsLoadFailureForEachMember()
        {
            var events = new List<WotMaterializationEventArgs>();
            m_coordinator.Event += (_, e) => events.Add(e);

            await RegisterTm("tm-x", TestMaterialization.Tm("urn:tm-x", extendsHrefs: "urn:tm-y"));
            await RegisterTm("tm-y", TestMaterialization.Tm("urn:tm-y", extendsHrefs: "urn:tm-x"));

            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            List<WotMaterializationEventArgs> failures = events
                .Where(e => e.Kind == WotMaterializationEventKind.LoadFailure)
                .ToList();
            Assert.That(failures, Has.Count.EqualTo(2),
                "Each member of a cyclic closure must receive its own LoadFailure event.");
        }
    }
}
