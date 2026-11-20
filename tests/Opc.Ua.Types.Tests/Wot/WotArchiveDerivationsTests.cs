/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Verifies the invocation-scoped reuse rules of the merged NodeSet derivations.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotArchiveDerivationsTests
    {
        [Test]
        public void EqualAliasContentInDistinctArraysAndResolversReusesTheSameDerivations()
        {
            UANodeSet context = CreateContext();
            var derivations = new WotNodeSetConverter.ArchiveDerivations(context);
            NodeIdAlias[] first = [Alias("X", "i=45")];
            NodeIdAlias[] equal = [Alias("X", "i=45")];

            Dictionary<string, UANode?> firstIndex = derivations.GetFactIndex(context, first, Resolver(first));
            Dictionary<string, UANode?> equalIndex = derivations.GetFactIndex(context, equal, Resolver(equal));

            Assert.That(equalIndex, Is.SameAs(firstIndex));
            WotReferenceTypeNames firstReferenceTypes = derivations.GetReferenceTypes(context);
            WotReferenceTypeNames secondReferenceTypes = derivations.GetReferenceTypes(context);
            Assert.That(secondReferenceTypes, Is.SameAs(firstReferenceTypes));
            (HashSet<string> NodeIds, HashSet<string> BrowseNames) a = derivations.GetIdentities(context);
            (HashSet<string> NodeIds, HashSet<string> BrowseNames) b = derivations.GetIdentities(context);
            Assert.That(b.NodeIds, Is.SameAs(a.NodeIds));
            Assert.That(b.BrowseNames, Is.SameAs(a.BrowseNames));
        }

        [Test]
        public void DifferentAliasMapsOfTheSameContextMatchTheUncachedOracle()
        {
            UANodeSet context = CreateContext();
            UANodeSet oracle = CreateContext();
            var derivations = new WotNodeSetConverter.ArchiveDerivations(context);
            NodeIdAlias[] first = [Alias("X", "i=45")];
            NodeIdAlias[] second = [Alias("X", "i=47")];
            NodeIdAlias[] reordered = [Alias("Y", "i=1"), Alias("X", "i=45")];
            NodeIdAlias[] split = [Alias("XY", "i=45")];
            NodeIdAlias[] duplicate = [Alias("X", "i=47"), Alias("X", "i=45")];
            NodeIdAlias[]?[] sequences = [first, second, reordered, split, null, [], duplicate];

            var indexes = new List<Dictionary<string, UANode?>>();
            foreach (NodeIdAlias[]? sequence in sequences)
            {
                Dictionary<string, UANode?> cached = derivations.GetFactIndex(context, sequence, Resolver(sequence));
                Dictionary<string, UANode?> uncached = derivations.GetFactIndex(oracle, sequence, Resolver(sequence));
                Assert.That(cached.Keys, Is.EquivalentTo(uncached.Keys));
                foreach (string key in uncached.Keys)
                {
                    Assert.That(cached[key]?.NodeId, Is.EqualTo(uncached[key]?.NodeId), key);
                }
                indexes.Add(cached);
            }

            Assert.That(indexes[0].ContainsKey("id:i=45"), Is.True);
            Assert.That(indexes[1].ContainsKey("id:i=47"), Is.True);
            Assert.That(indexes[1].ContainsKey("id:i=45"), Is.False);
            Assert.That(indexes[1], Is.Not.SameAs(indexes[0]));
            Assert.That(indexes[2].ContainsKey("id:i=45"), Is.True);
            Assert.That(indexes[3].ContainsKey("id:X"), Is.True);
            Assert.That(indexes[3].ContainsKey("id:i=45"), Is.False);
            Assert.That(indexes[3], Is.Not.SameAs(indexes[0]));
            Assert.That(indexes[6].ContainsKey("id:i=47"), Is.True);
            Assert.That(indexes[6].ContainsKey("id:i=45"), Is.False);
            Assert.That(indexes[6], Is.Not.SameAs(indexes[1]));
        }

        [Test]
        public void ADifferentNodeSetInstanceIsNeverCached()
        {
            UANodeSet context = CreateContext();
            UANodeSet other = CreateContext();
            var derivations = new WotNodeSetConverter.ArchiveDerivations(context);
            NodeIdAlias[] aliases = [Alias("X", "i=45")];

            Dictionary<string, UANode?> cached = derivations.GetFactIndex(context, aliases, Resolver(aliases));
            Dictionary<string, UANode?> otherFirst = derivations.GetFactIndex(other, aliases, Resolver(aliases));
            Dictionary<string, UANode?> otherSecond = derivations.GetFactIndex(other, aliases, Resolver(aliases));

            Assert.That(otherFirst, Is.Not.SameAs(cached));
            Assert.That(otherSecond, Is.Not.SameAs(otherFirst));
            Assert.That(
                derivations.GetIdentities(other).NodeIds,
                Is.Not.SameAs(derivations.GetIdentities(other).NodeIds));
        }

        [Test]
        public void ANewScopeAfterMutationSeesFreshFactsAndDoesNotReuseTheOldIndex()
        {
            UANodeSet context = CreateContext();
            NodeIdAlias[] aliases = [Alias("X", "i=45")];
            var before = new WotNodeSetConverter.ArchiveDerivations(context);
            Dictionary<string, UANode?> old = before.GetFactIndex(context, aliases, Resolver(aliases));

            context.Items = [.. context.Items!, new UAObjectType { NodeId = "ns=1;s=Added", BrowseName = "1:Added" }];
            var after = new WotNodeSetConverter.ArchiveDerivations(context);
            Dictionary<string, UANode?> fresh = after.GetFactIndex(context, aliases, Resolver(aliases));

            Assert.That(fresh, Is.Not.SameAs(old));
            Assert.That(fresh.ContainsKey("id:ns=1;s=Added"), Is.True);
            Assert.That(after.GetIdentities(context).NodeIds, Does.Contain("ns=1;s=Added"));
        }

        private static NodeSetDeclaredAliases Resolver(NodeIdAlias[]? aliases)
        {
            return NodeSetDeclaredAliases.FromNodeSet(new UANodeSet { Aliases = aliases }, WotNodeSetAliases.Instance);
        }

        private static NodeIdAlias Alias(string alias, string value)
        {
            return new NodeIdAlias { Alias = alias, Value = value };
        }

        private static UANodeSet CreateContext()
        {
            return new UANodeSet
            {
                Items =
                [
                    new UAObjectType { NodeId = "X", BrowseName = "1:Aliased" },
                    new UAObjectType { NodeId = "ns=1;s=Plain", BrowseName = "1:Plain" }
                ]
            };
        }
    }
}
