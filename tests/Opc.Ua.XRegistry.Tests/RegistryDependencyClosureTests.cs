/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    [TestFixture]
    [Category("XRegistry")]
    public sealed class RegistryDependencyClosureTests
    {
        [Test]
        public async Task SharedDependenciesExpandAndOrderTheWholeClosureBeforeDependentsAsync()
        {
            var baseModel = new XRegistryRefreshMember("/models/base");
            var first = new XRegistryRefreshMember("/models/first");
            var second = new XRegistryRefreshMember("/models/second");
            var independent = new XRegistryRefreshMember("/models/separate");
            ArrayOf<XRegistryDependencyClosure> closures = await XRegistryDependencyGraph.BuildClosuresAsync(
                [first, second, independent, first],
                (member, _) => new ValueTask<ArrayOf<XRegistryDependencyReference>>(member.Xid == baseModel.Xid ||
                    member.Xid == independent.Xid ? [] : [new XRegistryDependencyReference("base", "extends")]),
                href => href == "base" ? baseModel : null).ConfigureAwait(false);

            Assert.That(closures.Count, Is.EqualTo(2));
            XRegistryDependencyClosure shared = closures.ToArray()!.First(closure => closure.Members.Count == 3);
            Assert.That(shared.IsProjectable, Is.True);
            Assert.That(shared.OrderedMembers[0].Xid, Is.EqualTo("/models/base"));
            Assert.That(shared.Members.ToArray()!.Select(member => member.Xid),
                Is.EquivalentTo(s_shared));
            Assert.That(shared.Dependencies.Count, Is.EqualTo(2));
            Assert.That(shared.Dependencies.ToArray()!.All(edge => edge.Resolved), Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task CyclesAndMissingDependenciesCannotProduceAnApparentlyProjectableClosureAsync(bool cycle)
        {
            var first = new XRegistryRefreshMember("/models/a");
            var second = new XRegistryRefreshMember("/models/b");
            ArrayOf<XRegistryDependencyClosure> closures = await XRegistryDependencyGraph.BuildClosuresAsync([first],
                (member, _) => new ValueTask<ArrayOf<XRegistryDependencyReference>>(
                    [new XRegistryDependencyReference(member.Xid == first.Xid ? "b" : "a", "extends")]),
                href => cycle ? href == "a" ? first : second : null).ConfigureAwait(false);

            Assert.That(closures.Count, Is.EqualTo(1));
            Assert.That(closures[0].IsProjectable, Is.False);
            Assert.That(closures[0].HasCycle, Is.EqualTo(cycle));
            Assert.That(closures[0].HasMissingDependency, Is.EqualTo(!cycle));
            Assert.That(closures[0].Diagnostics.Count, Is.GreaterThan(0));
            if (cycle)
            {
                Assert.That(closures[0].OrderedMembers.Count, Is.Zero);
                Assert.That(closures[0].Members.Count, Is.EqualTo(2));
            }
            else
            {
                Assert.That(closures[0].Dependencies[0].Resolved, Is.False);
                Assert.That(closures[0].Dependencies[0].TargetHref, Is.EqualTo("b"));
            }
        }

        private static readonly string[] s_shared = ["/models/base", "/models/first", "/models/second"];
    }
}
