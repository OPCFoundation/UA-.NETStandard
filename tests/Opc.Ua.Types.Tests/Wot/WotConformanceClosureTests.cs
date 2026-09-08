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

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// WoT Binding Section 11 makes a profile a name for a set of conformance
    /// units, and makes the profiles nest. A claim is therefore only as good as
    /// the closure it expands to, so these pin every closure exactly rather
    /// than spot-checking membership: a unit quietly added to a profile, or
    /// quietly dropped from one, changes what every document claiming it
    /// promises.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotConformanceClosureTests
    {
        [Test]
        public void TheReaderProfileExpandsToExactlyItsUnits()
        {
            Assert.That(
                Closure("WoT-Reader"),
                Is.EqualTo(s_readerClosure).AsCollection);
        }

        [Test]
        public void TheModellerProfileExpandsToExactlyItsUnits()
        {
            Assert.That(
                Closure("WoT-Modeller"),
                Is.EqualTo(s_modellerClosure).AsCollection);
        }

        [Test]
        public void TheConverterProfileExpandsToExactlyItsUnits()
        {
            Assert.That(
                Closure("WoT-Converter"),
                Is.EqualTo(s_converterClosure).AsCollection);
        }

        [Test]
        public void TheArchivalConverterProfileExpandsToExactlyItsUnits()
        {
            Assert.That(
                Closure("WoT-ArchivalConverter"),
                Is.EqualTo(s_archivalConverterClosure).AsCollection);
        }

        /// <summary>
        /// The four profiles nest, so each closure contains the one below it
        /// whole. Checking containment as well as the exact sets is what makes
        /// a reordering of the definitions fail rather than silently produce a
        /// different lattice.
        /// </summary>
        [Test]
        public void TheProfilesNest()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Closure("WoT-Modeller"), Is.SupersetOf(Closure("WoT-Reader")));
                Assert.That(Closure("WoT-Converter"), Is.SupersetOf(Closure("WoT-Modeller")));
                Assert.That(
                    Closure("WoT-ArchivalConverter"), Is.SupersetOf(Closure("WoT-Converter")));
            });
        }

        /// <summary>
        /// A conformance unit expands to itself and nothing else: only a
        /// profile names other units, and a unit that quietly pulled in another
        /// would let one claim satisfy a requirement nobody claimed.
        /// </summary>
        [Test]
        public void EveryUnitExpandsToItselfAlone()
        {
            Assert.Multiple(() =>
            {
                foreach (string unit in s_units)
                {
                    Assert.That(Closure(unit), Is.EqualTo(new[] { unit }).AsCollection, unit);
                }
            });
        }

        /// <summary>
        /// Every name Section 11 defines is recognized, and nothing else is.
        /// The list is exact so a unit added upstream arrives as a failure
        /// rather than as a claim silently reported unrecognized.
        /// </summary>
        [Test]
        public void ExactlyTheSection11NamesAreRecognized()
        {
            Assert.Multiple(() =>
            {
                foreach (string name in s_units.Concat(s_profiles))
                {
                    Assert.That(
                        WotBindingConformance.IsConformanceName(name), Is.True, name);
                }
                Assert.That(
                    WotBindingConformance.IsConformanceName("WoT-Wizard"), Is.False);
                Assert.That(
                    WotBindingConformance.IsConformanceName("Reader"), Is.False);
                Assert.That(WotBindingConformance.IsConformanceName(null), Is.False);
                Assert.That(
                    WotBindingConformance.IsConformanceName(string.Empty), Is.False);
            });
        }

        /// <summary>
        /// A claim satisfies exactly the units its closure contains. Testing
        /// both directions is the point: a claim that satisfied something
        /// outside its closure would let a document promise a unit it never
        /// named.
        /// </summary>
        [Test]
        public void AClaimSatisfiesExactlyItsClosure()
        {
            Assert.Multiple(() =>
            {
                foreach (string profile in s_profiles)
                {
                    List<string> closure = Closure(profile);
                    string[] claim = [profile];
                    foreach (string unit in s_units.Concat(s_profiles))
                    {
                        Assert.That(
                            WotBindingConformance.ClaimsSatisfy(claim, unit),
                            Is.EqualTo(closure.Contains(unit)),
                            $"{profile} against {unit}");
                    }
                }
            });
        }

        /// <summary>
        /// A name Section 11 does not define expands to nothing: it is not a
        /// unit, so it names none. Reporting it as a unit of itself would let
        /// a misspelled claim satisfy a requirement spelled the same way.
        /// </summary>
        [Test]
        public void ExpandingAnUnknownOrMissingNameYieldsNothing()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WotBindingConformance.Expand("WoT-Wizard").Count, Is.Zero);
                Assert.That(WotBindingConformance.Expand(null).Count, Is.Zero);
                Assert.That(WotBindingConformance.Expand(string.Empty).Count, Is.Zero);
            });
        }

        private static List<string> Closure(string name)
        {
            var expanded = new List<string>();
            foreach (string unit in WotBindingConformance.Expand(name))
            {
                expanded.Add(unit);
            }
            expanded.Sort(StringComparer.Ordinal);
            return expanded;
        }

        /// <summary>
        /// The conformance units of WoT Binding Section 11, in ascending order.
        /// </summary>
        private static readonly string[] s_units =
        [
            "WoT-DataTypeDefinition",
            "WoT-EventMapping",
            "WoT-ExactRoundtrip",
            "WoT-JsonResidue",
            "WoT-ModelVocabulary",
            "WoT-NativeMapping",
            "WoT-NodeSetPreservation",
            "WoT-Projection",
            "WoT-ProtocolBinding",
            "WoT-StructuredFallback"
        ];

        /// <summary>
        /// The recommended profiles of WoT Binding Section 11, in ascending
        /// order.
        /// </summary>
        private static readonly string[] s_profiles =
        [
            "WoT-ArchivalConverter",
            "WoT-Converter",
            "WoT-Modeller",
            "WoT-Reader"
        ];

        /// <summary>
        /// The exact closure of the <c>WoT-Reader</c> profile, in ascending
        /// order.
        /// </summary>
        private static readonly string[] s_readerClosure =
        [
            "WoT-NativeMapping",
            "WoT-ProtocolBinding",
            "WoT-Reader"
        ];

        /// <summary>
        /// The exact closure of the <c>WoT-Modeller</c> profile, in ascending
        /// order.
        /// </summary>
        private static readonly string[] s_modellerClosure =
        [
            "WoT-DataTypeDefinition",
            "WoT-EventMapping",
            "WoT-ModelVocabulary",
            "WoT-Modeller",
            "WoT-NativeMapping",
            "WoT-Projection",
            "WoT-ProtocolBinding",
            "WoT-Reader"
        ];

        /// <summary>
        /// The exact closure of the <c>WoT-Converter</c> profile, in ascending
        /// order.
        /// </summary>
        private static readonly string[] s_converterClosure =
        [
            "WoT-Converter",
            "WoT-DataTypeDefinition",
            "WoT-EventMapping",
            "WoT-ExactRoundtrip",
            "WoT-JsonResidue",
            "WoT-ModelVocabulary",
            "WoT-Modeller",
            "WoT-NativeMapping",
            "WoT-Projection",
            "WoT-ProtocolBinding",
            "WoT-Reader",
            "WoT-StructuredFallback"
        ];

        /// <summary>
        /// The exact closure of the <c>WoT-ArchivalConverter</c> profile, in
        /// ascending order.
        /// </summary>
        private static readonly string[] s_archivalConverterClosure =
        [
            "WoT-ArchivalConverter",
            "WoT-Converter",
            "WoT-DataTypeDefinition",
            "WoT-EventMapping",
            "WoT-ExactRoundtrip",
            "WoT-JsonResidue",
            "WoT-ModelVocabulary",
            "WoT-Modeller",
            "WoT-NativeMapping",
            "WoT-NodeSetPreservation",
            "WoT-Projection",
            "WoT-ProtocolBinding",
            "WoT-Reader",
            "WoT-StructuredFallback"
        ];
    }
}
