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
        public void SameContextAndAliasSequenceReusesTheDerivations()
        {
            UANodeSet context = CreateContext();
            var derivations = new WotNodeSetConverter.ArchiveDerivations(context);
            NodeIdAlias[] aliases = [Alias("X", "i=45")];

            Dictionary<string, UANode?> first = derivations.GetFactIndex(context, aliases, Resolver(aliases));
            Dictionary<string, UANode?> second = derivations.GetFactIndex(context, aliases, Resolver(aliases));

            Assert.That(second, Is.SameAs(first));
            Assert.That(derivations.GetReferenceTypes(context), Is.SameAs(derivations.GetReferenceTypes(context)));
            Assert.That(
                derivations.GetIdentities(context).NodeIds,
                Is.SameAs(derivations.GetIdentities(context).NodeIds));
        }

        [Test]
        public void DifferentAliasSequencesOfTheSameContextNeverShareAnIndex()
        {
            UANodeSet context = CreateContext();
            UANodeSet oracle = CreateContext();
            var derivations = new WotNodeSetConverter.ArchiveDerivations(context);
            NodeIdAlias[] first = [Alias("X", "i=45")];
            NodeIdAlias[] second = [Alias("X", "i=47")];
            NodeIdAlias[] reordered = [Alias("Y", "i=1"), Alias("X", "i=45")];
            NodeIdAlias[] split = [Alias("XY", "i=45")];
            NodeIdAlias[][] sequences = [first, second, reordered, split, null!, []];

            var indexes = new List<Dictionary<string, UANode?>>();
            foreach (NodeIdAlias[] sequence in sequences)
            {
                Dictionary<string, UANode?> cached = derivations.GetFactIndex(context, sequence, Resolver(sequence));
                Dictionary<string, UANode?> uncached = derivations.GetFactIndex(oracle, sequence, Resolver(sequence));
                Assert.That(cached.Keys, Is.EquivalentTo(uncached.Keys));
                indexes.Add(cached);
            }

            Assert.That(indexes[0].ContainsKey("id:i=45"), Is.True);
            Assert.That(indexes[1].ContainsKey("id:i=47"), Is.True);
            Assert.That(indexes[1].ContainsKey("id:i=45"), Is.False);
            Assert.That(indexes[0], Is.Not.SameAs(indexes[1]));
            Assert.That(indexes[2], Is.SameAs(derivations.GetFactIndex(context, reordered, Resolver(reordered))));
            Assert.That(indexes[2], Is.Not.SameAs(indexes[0]));
            Assert.That(indexes[3], Is.Not.SameAs(indexes[0]));
            Assert.That(indexes[3].ContainsKey("id:X"), Is.True);
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
        public void SeparateInvocationsObserveChangesMadeBetweenThem()
        {
            UANodeSet context = CreateContext();
            NodeIdAlias[] aliases = [Alias("X", "i=45")];
            var before = new WotNodeSetConverter.ArchiveDerivations(context);
            Dictionary<string, UANode?> stale = before.GetFactIndex(context, aliases, Resolver(aliases));

            context.Items = [.. context.Items!, new UAObjectType { NodeId = "ns=1;s=Added", BrowseName = "1:Added" }];
            var after = new WotNodeSetConverter.ArchiveDerivations(context);
            Dictionary<string, UANode?> fresh = after.GetFactIndex(context, aliases, Resolver(aliases));

            Assert.That(fresh, Is.Not.SameAs(stale));
            Assert.That(fresh.ContainsKey("id:ns=1;s=Added"), Is.True);
            Assert.That(stale.ContainsKey("id:ns=1;s=Added"), Is.False);
            Assert.That(after.GetIdentities(context).NodeIds, Does.Contain("ns=1;s=Added"));
        }

        private static INodeSetAliasResolver Resolver(NodeIdAlias[] aliases)
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
