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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    public sealed class WotPublicationPlannerTests
    {
        [Test]
        public async Task AcyclicActivationDependenciesKeepRequestedResourceBoundaries()
        {
            using var registry = new WotRegistryService();
            await AddAsync(registry, "a", "one", "b").ConfigureAwait(false);
            await AddAsync(registry, "b", "two").ConfigureAwait(false);

            WotPublicationPlan plan = await PlanAsync(registry, WoTAtomicityEnum.PerResource).ConfigureAwait(false);

            Assert.That(plan.AppliedAtomicity, Is.EqualTo(WoTAtomicityEnum.PerResource));
            Assert.That(plan.Units.Count, Is.EqualTo(2));
            Assert.That(ActivationIds(plan.Units[0]), Is.EqualTo(s_b));
            Assert.That(ActivationIds(plan.Units[1]), Is.EqualTo(s_a));
        }

        [Test]
        public async Task ReciprocalReferencesCoactivateWithoutOrderingCycle()
        {
            using var registry = new WotRegistryService();
            await AddAsync(registry, "a", "one", "b").ConfigureAwait(false);
            await AddAsync(registry, "b", "two", "a").ConfigureAwait(false);

            WotPublicationPlan plan = await PlanAsync(registry, WoTAtomicityEnum.PerResource).ConfigureAwait(false);

            Assert.That(plan.AppliedAtomicity, Is.EqualTo(WoTAtomicityEnum.PerClosure));
            Assert.That(plan.Units.Count, Is.EqualTo(1));
            Assert.That(ActivationIds(plan.Units[0]), Is.EquivalentTo(s_ab));
            Assert.That(plan.Units[0].ToList().All(closure => closure.IsProjectable && !closure.HasCycle), Is.True);
        }

        [Test]
        public async Task GroupCycleCoarsensInsteadOfPublishingUnsafeOrder()
        {
            using var registry = new WotRegistryService();
            await AddAsync(registry, "a", "one", "b").ConfigureAwait(false);
            await AddAsync(registry, "b", "two").ConfigureAwait(false);
            await AddAsync(registry, "c", "one").ConfigureAwait(false);
            await AddAsync(registry, "d", "two", "c").ConfigureAwait(false);

            WotPublicationPlan plan = await PlanAsync(registry, WoTAtomicityEnum.PerGroup).ConfigureAwait(false);

            Assert.That(plan.AppliedAtomicity, Is.EqualTo(WoTAtomicityEnum.PerRegistry));
            Assert.That(plan.Units.Count, Is.EqualTo(1));
            Assert.That(ActivationIds(plan.Units[0]), Is.EquivalentTo(s_abcd));
        }

        [Test]
        public async Task DisabledResolutionInputDoesNotExpandItsGroup()
        {
            using var registry = new WotRegistryService();
            await AddAsync(registry, "a", "one", "b").ConfigureAwait(false);
            WotResource disabled = await AddAsync(registry, "b", "two").ConfigureAwait(false);
            await AddAsync(registry, "c", "two").ConfigureAwait(false);
            await registry.SetEnabledAsync(disabled.GroupId, disabled.ResourceId, false).ConfigureAwait(false);

            WotPublicationPlan plan = await PlanAsync(registry, WoTAtomicityEnum.PerGroup).ConfigureAwait(false);

            Assert.That(plan.AppliedAtomicity, Is.EqualTo(WoTAtomicityEnum.PerGroup));
            Assert.That(plan.Units.Count, Is.EqualTo(2));
            Assert.That(plan.Units.ToList().SelectMany(ActivationIds), Is.EquivalentTo(s_ac));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeModelPrerequisitesAreSelectedPinnedAndOrdered(bool selectOnlyDependent)
        {
            var contents = new Dictionary<string, ByteString>();
            WotResource dependency = NativeModel("a-base", "urn:unit:base", null, contents);
            WotResource dependent = NativeModel("z-dependent", "urn:unit:dependent", "urn:unit:base", contents);
            WotResource unrelated = NativeModel("unrelated", "urn:unit:unrelated", null, contents);
            var group = new WotResourceGroup("models", WoTDocumentKindEnum.ThingModel,
                ImmutableDictionary<string, WotResource>.Empty
                    .Add(dependency.ResourceId, dependency).Add(dependent.ResourceId, dependent)
                    .Add(unrelated.ResourceId, unrelated));
            var snapshot = new WotRegistrySnapshot(1,
                ImmutableDictionary<string, WotResourceGroup>.Empty.Add(group.GroupId, group));
            var reads = new List<string>();
            ImmutableArray<WotDependencyClosure> closures = await WotDependencyGraph.BuildClosuresAsync(
                snapshot, selectOnlyDependent ? [dependent] : [dependent, dependency], 64,
                (version, _) =>
                {
                    reads.Add(version.DigestHex);
                    return new ValueTask<ByteString>(contents[version.DigestHex]);
                }, CancellationToken.None).ConfigureAwait(false);

            WotPublicationPlan plan = WotPublicationPlanner.Create(closures.ToArrayOf(), WoTAtomicityEnum.PerResource);

            Assert.That(reads, Has.Count.EqualTo(2));
            Assert.That(reads, Does.Not.Contain(unrelated.DefaultVersion!.DigestHex));
            Assert.That(plan.AppliedAtomicity, Is.EqualTo(WoTAtomicityEnum.PerResource));
            Assert.That(plan.Units.Count, Is.EqualTo(2));
            Assert.That(ActivationIds(plan.Units[0]).Single(), Is.EqualTo("a-base"));
            Assert.That(ActivationIds(plan.Units[1]).Single(), Is.EqualTo("z-dependent"));
            Assert.That(plan.Units[1][0].Members.Select(member => member.Xid),
                Is.EquivalentTo(new[] { dependency.Xid, dependent.Xid }));
            WotDependency edge = plan.Units[1][0].Dependencies.Single();
            Assert.That(edge.SourceXid, Is.EqualTo(dependent.Xid));
            Assert.That(edge.TargetXid, Is.EqualTo(dependency.Xid));
            Assert.That(edge.TargetHref, Is.EqualTo("urn:unit:base"));
            Assert.That(edge.Resolved, Is.True);
        }

        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(2147483648d)]
        public void CapturedBudgetRejectsInvalidValues(double timeout)
        {
            Assert.That(() => WotCapturedRefreshRequest.Capture(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Timeout = timeout }
            }), Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadInvalidArgument));
        }

        [TestCase(0d)]
        [TestCase(1d)]
        [TestCase(2147483647d)]
        public void CapturedBudgetRetainsAllowedBoundaryValues(double timeout)
        {
            var request = new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Timeout = timeout, MaxParallelism = 1 }
            };
            WotCapturedRefreshRequest captured = WotCapturedRefreshRequest.Capture(request);
            request.Options.Timeout = -1;
            request.Options.MaxParallelism = 2;

            Assert.That(captured.Timeout, Is.EqualTo(timeout));
            Assert.That(captured.MaxParallelism, Is.EqualTo(1u));
        }

        [Test]
        public void CapturedRequestRejectsUnknownAtomicity()
        {
            Assert.That(() => WotCapturedRefreshRequest.Capture(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = (WoTAtomicityEnum)42 }
            }), Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadInvalidArgument));
        }

        private static WotResource NativeModel(
            string id, string uri, string? required, Dictionary<string, ByteString> contents)
        {
            string requiredModels = required is null ? "[]" : $$"""[{"modelUri":"{{required}}"}]""";
            ByteString bytes = ByteString.From(Encoding.UTF8.GetBytes($$$"""
                {"id":"urn:document:{{{id}}}","uav:nodes":{"profileVersion":"1.0",
                  "models":[{"modelUri":"{{{uri}}}","requiredModels":{{{requiredModels}}}}],"nodes":[]}}
                """));
            var version = new WotResourceVersion("v1", WotContentDigest.Compute(bytes), bytes.Length,
                "application/tm+json", "WoT-TM/1.0", default, default)
            {
                Dependencies = WotDependencyGraph.ReadMetadata(bytes, 64)
            };
            contents.Add(version.DigestHex, bytes);
            return new WotResource("models", id, WoTDocumentKindEnum.ThingModel, [version], defaultVersionId: "v1");
        }

        private static string[] ActivationIds(ArrayOf<WotDependencyClosure> unit)
        {
            return [.. unit.ToList().SelectMany(closure => closure.ActivationMembers.ToList())
                .Select(resource => resource.ResourceId).Distinct()];
        }

        private static async Task<WotPublicationPlan> PlanAsync(
            WotRegistryService registry, WoTAtomicityEnum atomicity)
        {
            using WotMaterializationSnapshot input = await WotDependencyGraph.CaptureAsync(
                registry, [], false, 64, CancellationToken.None).ConfigureAwait(false);
            return WotPublicationPlanner.Create(input.Closures, atomicity);
        }

        private static async Task<WotResource> AddAsync(
            WotRegistryService registry, string id, string group, string? target = null)
        {
            string links = target is null ? "[]" : $$"""[{"rel":"ua:HasComponent","href":"urn:{{target}}"}]""";
            WotRegistryMutationResult result = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = group,
                ResourceId = id,
                VersionId = "v1",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(Encoding.UTF8.GetBytes($$"""
                    {"@context":"https://www.w3.org/2022/wot/td/v1.1","id":"urn:{{id}}","links":{{links}}}
                    """))
            }).ConfigureAwait(false);
            Assert.That(result.Changed, Is.True, result.Message);
            return result.Resource!;
        }

        private static readonly string[] s_a = ["a"];
        private static readonly string[] s_b = ["b"];
        private static readonly string[] s_ab = ["a", "b"];
        private static readonly string[] s_ac = ["a", "c"];
        private static readonly string[] s_abcd = ["a", "b", "c", "d"];
    }
}
