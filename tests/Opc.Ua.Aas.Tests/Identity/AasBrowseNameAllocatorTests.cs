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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Aas.Tests.Identity
{
    /// <summary>
    /// Tests the clause 6.1.3 BrowseName rules: the derived name of an
    /// Identifiable without a short name, and the ordering that keeps
    /// allocation independent of the source document's array order.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasBrowseNameAllocatorTests
    {
        [TestCase(AasNodeKind.Shell, "AssetAdministrationShell")]
        [TestCase(AasNodeKind.Submodel, "Submodel")]
        [TestCase(AasNodeKind.ConceptDescription, "ConceptDescription")]
        public void KindNameOfMatchesTheMetamodelClassName(AasNodeKind kind, string expected)
        {
            Assert.That(AasBrowseNameAllocator.KindNameOf(kind), Is.EqualTo(expected));
        }

        [Test]
        public void DerivedBaseNameIsTheKindAndTheLowercaseSha256OfTheIdentifier()
        {
            const string id = "https://fabrikam.com/ids/sm/ordering";

            var expectedDigest = new StringBuilder();
            byte[] idBytes = Encoding.UTF8.GetBytes(id);
#if NET5_0_OR_GREATER
            byte[] hash = SHA256.HashData(idBytes);
#else
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(idBytes);
#endif
            foreach (byte octet in hash)
            {
                expectedDigest.Append(octet.ToString("x2", CultureInfo.InvariantCulture));
            }

            Assert.That(
                AasBrowseNameAllocator.DeriveBaseName(AasNodeKind.Submodel, id),
                Is.EqualTo("Submodel_" + expectedDigest));
        }

        [Test]
        public void DerivedBaseNameNeverContainsTheRawIdentifier()
        {
            const string id = "urn:x:secret-identifier";

            Assert.That(
                AasBrowseNameAllocator.DeriveBaseName(AasNodeKind.Shell, id),
                Does.Not.Contain("secret-identifier"));
        }

        [Test]
        public void DerivedBaseNameDoesNotNormalizeTheIdentifier()
        {
            // Two identifiers differing only by normalization form are
            // different identifiers, so they must not share a BrowseName.
            Assert.That(
                AasBrowseNameAllocator.DeriveBaseName(AasNodeKind.Submodel, "\u00E9"),
                Is.Not.EqualTo(
                    AasBrowseNameAllocator.DeriveBaseName(AasNodeKind.Submodel, "e\u0301")));
        }

        [Test]
        public void AnUncontestedDerivedNameIsUnsuffixed()
        {
            var allocator = new AasBrowseNameAllocator();
            allocator.RegisterDerived(AasNodeKind.Submodel, "urn:x:1");

            IReadOnlyDictionary<string, string> allocated = allocator.Allocate();

            Assert.That(
                allocated["urn:x:1"],
                Is.EqualTo(AasBrowseNameAllocator.DeriveBaseName(AasNodeKind.Submodel, "urn:x:1")));
        }

        [Test]
        public void AnAuthoredShortNameWinsOverACollidingDerivedName()
        {
            const string id = "urn:x:1";
            string derived = AasBrowseNameAllocator.DeriveBaseName(AasNodeKind.Submodel, id);

            var allocator = new AasBrowseNameAllocator();

            // The author happens to have used the derived base name as an
            // idShort. Reservation runs first, so the derived name yields.
            allocator.Reserve(derived);
            allocator.RegisterDerived(AasNodeKind.Submodel, id);

            Assert.That(allocator.Allocate()[id], Is.EqualTo(derived + "_0"));
        }

        [Test]
        public void AllocationIsIndependentOfRegistrationOrder()
        {
            // Force a shared base name so the disambiguation path runs, and
            // assert the two orders agree.
            string[] ids = ["urn:x:b", "urn:x:a", "urn:x:c"];

            IReadOnlyDictionary<string, string> forward = AllocateAll(ids);
            IReadOnlyDictionary<string, string> reversed = AllocateAll(Enumerable.Reverse(ids));

            Assert.That(forward, Is.EquivalentTo(reversed));
        }

        [Test]
        public void CollidingBaseNamesTakeTheSmallestFreeSuffixInUtf8Order()
        {
            const string first = "urn:x:a";
            const string second = "urn:x:b";
            string shared = AasBrowseNameAllocator.DeriveBaseName(AasNodeKind.Submodel, first);

            // Simulate a digest collision by reserving the base name of the
            // first identifier and registering both against it.
            var allocator = new AasBrowseNameAllocator();
            allocator.Reserve(shared);
            allocator.RegisterDerived(AasNodeKind.Submodel, first);
            allocator.RegisterDerived(AasNodeKind.Submodel, second);

            IReadOnlyDictionary<string, string> allocated = allocator.Allocate();

            Assert.Multiple(() =>
            {
                Assert.That(allocated[first], Is.EqualTo(shared + "_0"));
                Assert.That(
                    allocated[second],
                    Is.EqualTo(AasBrowseNameAllocator.DeriveBaseName(AasNodeKind.Submodel, second)));
            });
        }

        [Test]
        public void EveryAllocatedNameIsDistinct()
        {
            string[] ids = ["urn:x:a", "urn:x:b", "urn:x:c", "urn:x:d"];

            var allocator = new AasBrowseNameAllocator();
            foreach (string id in ids)
            {
                allocator.Reserve(AasBrowseNameAllocator.DeriveBaseName(AasNodeKind.Shell, id));
                allocator.RegisterDerived(AasNodeKind.Shell, id);
            }

            IEnumerable<string> names = allocator.Allocate().Values;

            Assert.That(names.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(ids.Length));
        }

        [TestCase(0, "0")]
        [TestCase(1, "1")]
        [TestCase(42, "42")]
        public void AListMemberIsNamedByItsIndex(int index, string expected)
        {
            Assert.That(AasBrowseNameAllocator.ForListMember(index), Is.EqualTo(expected));
        }

        [Test]
        public void ForListMemberRejectsANegativeIndex()
        {
            Assert.That(
                () => AasBrowseNameAllocator.ForListMember(-1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void DisplayNameIsTheShortNameWhereOneExists()
        {
            Assert.That(
                AasBrowseNameAllocator.DisplayNameFor("Ordering", "Ordering"),
                Is.EqualTo("Ordering"));
        }

        [TestCase(null)]
        [TestCase("")]
        public void DisplayNameFallsBackToTheBrowseName(string? idShort)
        {
            Assert.That(
                AasBrowseNameAllocator.DisplayNameFor(idShort, "Submodel_abc"),
                Is.EqualTo("Submodel_abc"));
        }

        [TestCase(null)]
        [TestCase("")]
        public void ReserveRejectsAnAbsentShortName(string? idShort)
        {
            var allocator = new AasBrowseNameAllocator();

            Assert.That(
                () => allocator.Reserve(idShort!),
                Throws.ArgumentException);
        }

        [Test]
        public void RegisterDerivedRejectsTheElementKind()
        {
            var allocator = new AasBrowseNameAllocator();

            Assert.That(
                () => allocator.RegisterDerived(AasNodeKind.SubmodelElement, "urn:x:1"),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void RegisterDerivedRejectsANullIdentifier()
        {
            var allocator = new AasBrowseNameAllocator();

            Assert.That(
                () => allocator.RegisterDerived(AasNodeKind.Shell, null!),
                Throws.ArgumentNullException);
        }

        private static IReadOnlyDictionary<string, string> AllocateAll(IEnumerable<string> ids)
        {
            var allocator = new AasBrowseNameAllocator();
            foreach (string id in ids)
            {
                allocator.RegisterDerived(AasNodeKind.Submodel, id);
            }

            return allocator.Allocate();
        }
    }
}
