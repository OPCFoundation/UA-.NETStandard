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

#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Where Annex G.4's opaque members are found, and where they are not.
    /// </summary>
    /// <remarks>
    /// The scan walks the whole document, so it meets every JSON shape a
    /// document can hold: objects that carry an opaque member, arrays whose
    /// items carry one, and the scalars that carry nothing. An opaque member's
    /// own contents stay opaque - the scan stops there rather than reporting
    /// nested members of a value the Binding has deliberately not read.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotOpaqueMemberLocationTests
    {
        private static readonly string[] s_expectedPointers =
        [
            "/uav:metadata",
            "/properties/Speed/uav:propertyConfiguration",
            "/forms/1/uav:actionConfiguration",
            "/forms/2/0/uav:eventConfiguration"
        ];

        private static readonly string[] s_escapedPointer = ["/a~1b/c~0d/uav:metadata"];

        /// <summary>
        /// An opaque member is located wherever it occurs: at the root, inside
        /// a nested object, and inside an array item, each by the RFC 6901
        /// pointer that addresses exactly that occurrence.
        /// </summary>
        [Test]
        public void EveryOpaqueMemberIsLocatedByItsOwnPointer()
        {
            using JsonDocument parsed = JsonDocument.Parse(
                "{\"title\":\"Tank\"," +
                "\"uav:metadata\":{\"a\":1}," +
                "\"count\":3,\"enabled\":true,\"absent\":null," +
                "\"properties\":{\"Speed\":{\"uav:propertyConfiguration\":{\"b\":2}}}," +
                "\"forms\":[{\"href\":\"x\"}," +
                "{\"uav:actionConfiguration\":{\"c\":3}}," +
                "[{\"uav:eventConfiguration\":{\"d\":4}}]]}");

            List<WotOpaqueMember> found =
                WotBindingConformance.FindOpaqueMembers(parsed.RootElement).ToList();

            Assert.That(
                found.Select(m => m.Pointer),
                Is.EqualTo(s_expectedPointers).AsCollection);
        }

        /// <summary>
        /// A pointer token is escaped, so a member name that itself contains
        /// <c>/</c> or <c>~</c> addresses one member rather than a path through
        /// several.
        /// </summary>
        [Test]
        public void APointerTokenIsEscaped()
        {
            using JsonDocument parsed = JsonDocument.Parse(
                "{\"a/b\":{\"c~d\":{\"uav:metadata\":{}}}}");

            List<WotOpaqueMember> found =
                WotBindingConformance.FindOpaqueMembers(parsed.RootElement).ToList();

            Assert.That(
                found.Select(m => m.Pointer),
                Is.EqualTo(s_escapedPointer).AsCollection);
        }

        /// <summary>
        /// The contents of an opaque member are opaque, so a member of an
        /// opaque object that is itself named like an opaque member is part of
        /// the value rather than a second occurrence: reporting it would bound
        /// the same bytes twice.
        /// </summary>
        [Test]
        public void AnOpaqueValueIsNotWalkedInto()
        {
            using JsonDocument parsed = JsonDocument.Parse(
                "{\"uav:metadata\":{\"uav:eventConfiguration\":{\"nested\":true}}}");

            List<WotOpaqueMember> found =
                WotBindingConformance.FindOpaqueMembers(parsed.RootElement).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(found, Has.Count.EqualTo(1));
                Assert.That(found[0].Member, Is.EqualTo("uav:metadata"));
                Assert.That(found[0].CompactUtf8Length, Is.GreaterThan(0));
            });
        }

        /// <summary>
        /// A document carrying nothing opaque reports nothing, whatever shape
        /// it has. A scan that reported a member of a scalar or of an array
        /// would be measuring something Annex G.4 never bounds.
        /// </summary>
        [TestCase("{\"title\":\"Tank\"}")]
        [TestCase("[1,2,3]")]
        [TestCase("\"Tank\"")]
        [TestCase("42")]
        [TestCase("null")]
        public void ADocumentWithNothingOpaqueReportsNothing(string json)
        {
            using JsonDocument parsed = JsonDocument.Parse(json);

            Assert.That(
                WotBindingConformance.FindOpaqueMembers(parsed.RootElement), Is.Empty);
        }
    }
}
