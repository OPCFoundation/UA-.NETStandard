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

using System.Linq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// The WoT Binding conversion writes the standard base-namespace names, so
    /// it is the WoT side that states what an undeclared one means. These
    /// tests state that the policy exists as one object, that it says what the
    /// base namespace says, and that it reaches the comparison and the alias
    /// completion pass without either of them knowing that WoT exists.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotNodeSetAliasesTests
    {
        /// <summary>
        /// The conversion options carry the policy to whatever compares two
        /// NodeSets on the conversion's behalf.
        /// </summary>
        [Test]
        public void TheConversionOptionsCarryTheWotPolicy()
        {
            NodeSetComparisonOptions options =
                new WotNodeSetConverterOptions().ToComparisonOptions();

            Assert.That(options.AliasResolver, Is.SameAs(WotNodeSetAliases.Instance));
        }

        /// <summary>
        /// The policy states no names of its own: it says the WoT Binding
        /// writes the ones NodeSet2 already knows, so a name and the
        /// identifier it stands for are stated once in the library.
        /// </summary>
        [Test]
        public void TheWotPolicyStatesTheStandardNames()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    WotNodeSetAliases.Instance.TryResolve("HasComponent", out string reference),
                    Is.True);
                Assert.That(reference, Is.EqualTo("i=47"));
                Assert.That(
                    WotNodeSetAliases.Instance.TryResolve("Double", out string dataType),
                    Is.True);
                Assert.That(dataType, Is.EqualTo("i=11"));
            });
        }

        /// <summary>
        /// A vendor name is the source document's own to declare, so the
        /// policy says nothing about it and the name stays as written.
        /// </summary>
        [Test]
        public void TheWotPolicyLeavesAVendorNameToTheDocument()
        {
            Assert.That(
                WotNodeSetAliases.Instance.TryResolve("VendorSpecificReference", out string nodeId),
                Is.False);
            Assert.That(nodeId, Is.Empty);
        }

        /// <summary>
        /// With the WoT policy injected, a document that writes a standard
        /// name it did not declare reads as the identifier the name stands
        /// for; without it, the same pair of documents is not equivalent.
        /// </summary>
        [Test]
        public void TheWotComparisonReadsAnUndeclaredStandardName()
        {
            UANodeSet undeclared = WotTestData.CreateReconstructableNodeSet();
            undeclared.Aliases = null;
            undeclared.Items!.OfType<UAVariable>().Single().DataType = "Double";
            UANodeSet expanded = WotTestData.CreateReconstructableNodeSet();
            expanded.Aliases = null;
            expanded.Items!.OfType<UAVariable>().Single().DataType = "i=11";

            Assert.That(
                NodeSetComparer.CompareEquivalent(undeclared, expanded).AreEquivalent,
                Is.False,
                "A comparison of two NodeSets states no profile's policy of its own.");
            Assert.That(
                NodeSetComparer.CompareEquivalent(
                        undeclared,
                        expanded,
                        new WotNodeSetConverterOptions().ToComparisonOptions())
                    .AreEquivalent,
                Is.True,
                "The WoT Binding writes the standard names, so it reads them too.");
        }

        /// <summary>
        /// The policy the conversion injects is also the one its restored
        /// documents are completed against, so a converted document declares
        /// what it writes and a round trip still reproduces the source.
        /// </summary>
        [Test]
        public void AConvertedNodeSetDeclaresWhatItWritesAndStillRoundTrips()
        {
            WotNodeSetRoundtripReport report = WotNodeSetRoundtrip.Run(
                WotTestData.CreateRichNodeSet());

            Assert.That(report.NativeProjectionPreserved, Is.True);
            Assert.That(report.Comparison.AreEquivalent, Is.True);

            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                WotTestData.CreateRichNodeSet());
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);

            Assert.That(
                restored.Aliases!.Where(
                    alias => WotNodeSetAliases.Instance.TryResolve(alias.Alias, out _)),
                Is.Not.Empty,
                "A restored document declares the standard names the policy knows.");
            foreach (NodeIdAlias alias in restored.Aliases!)
            {
                // A declaration the source document brought stays its own; one
                // the policy also knows has to say what the policy says, or the
                // completion pass and the comparison would read the document
                // differently.
                if (WotNodeSetAliases.Instance.TryResolve(alias.Alias, out string nodeId))
                {
                    Assert.That(alias.Value, Is.EqualTo(nodeId));
                }
            }
        }
    }
}
