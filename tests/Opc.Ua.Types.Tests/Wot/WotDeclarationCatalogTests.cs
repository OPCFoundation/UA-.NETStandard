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

using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// What a <see cref="WotDeclarationCatalog"/> says about the type a
    /// document bound to, which is the only thing WoT Binding Section 5.2.1's
    /// merge and Section 6.8's closed-content rule are decided from.
    /// </summary>
    /// <remarks>
    /// The three states the catalog distinguishes are not interchangeable: a
    /// type that declares nothing, a local context that holds the type but
    /// reports no declarations for it, and a local context that offers no
    /// declaration capability at all each make a different rule decidable or
    /// undecidable, and each has to name its own reason.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotDeclarationCatalogTests
    {
        private const string TankTypeId = "nsu=urn:test:catalog;i=1042";
        private const string ModelNamespace = "urn:test:catalog";

        /// <summary>
        /// A document that binds to no type is not a document whose
        /// declarations could not be read: nothing was asked, so the catalog
        /// names no type, matches nothing and offers no reason.
        /// </summary>
        [Test]
        public void ANotBoundCatalogAsksNothingAndMatchesNothing()
        {
            WotDeclarationCatalog catalog = WotDeclarationCatalog.NotBound;

            Assert.Multiple(() =>
            {
                Assert.That(catalog.TypeNodeId, Is.Null);
                Assert.That(catalog.HasDeclarations, Is.False);
                Assert.That(catalog.CapabilityOffered, Is.False);
                Assert.That(catalog.IsComplete, Is.False);
                Assert.That(catalog.Detail, Is.Null);
                Assert.That(catalog.Scope, Is.EqualTo(WotDeclarationScope.Effective));
                Assert.That(catalog.Match(ModelNamespace, "Speed"), Is.Empty);
            });
        }

        /// <summary>
        /// A local context that holds the type but reports no declarations for
        /// it is a different answer from one that offers no capability: the
        /// question was asked and answered, so the reason names the type.
        /// </summary>
        [Test]
        public void ATypeTheContextReportsNothingForNamesTheType()
        {
            WotDeclarationCatalog catalog = WotDeclarationCatalog.Create(
                TankTypeId, WotDeclarationScope.Direct, set: null, capabilityOffered: true);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.TypeNodeId, Is.EqualTo(TankTypeId));
                Assert.That(catalog.CapabilityOffered, Is.True);
                Assert.That(catalog.Scope, Is.EqualTo(WotDeclarationScope.Direct));
                Assert.That(catalog.HasDeclarations, Is.False);
                Assert.That(catalog.Detail, Does.Contain(TankTypeId));
                Assert.That(catalog.Detail, Does.Contain("reports no"));
            });
        }

        /// <summary>
        /// A context that offers no capability cannot be answering about this
        /// type in particular, so the reason is the missing capability and not
        /// whatever a set handed in alongside it happens to say. The capability
        /// is what Section 6.8's rule depends on, so the more fundamental
        /// reason is the one reported.
        /// </summary>
        [Test]
        public void TheMissingCapabilityOutranksASetsOwnReason()
        {
            var set = new WotTypeDeclarationSet
            {
                TypeNodeId = TankTypeId,
                Declarations = new ArrayOf<WotTypeDeclaration>(
                    new[] { Declaration("Speed", WotDeclarationKind.Variable) }),
                IsComplete = false,
                Detail = "the supertype chain was cut short"
            };

            WotDeclarationCatalog catalog = WotDeclarationCatalog.Create(
                TankTypeId, WotDeclarationScope.Effective, set, capabilityOffered: false);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.CapabilityOffered, Is.False);
                Assert.That(catalog.HasDeclarations, Is.True);
                Assert.That(catalog.IsComplete, Is.False);
                Assert.That(catalog.Detail, Does.Contain("No part of the local context"));
                Assert.That(
                    catalog.Detail,
                    Does.Not.Contain("supertype chain"),
                    "The set's own reason never replaces the capability's.");
            });
        }

        /// <summary>
        /// A set that names its own reason keeps it when the capability is
        /// offered, because then the set's reason is the only one there is.
        /// </summary>
        [Test]
        public void AnIncompleteSetKeepsItsOwnReason()
        {
            var set = new WotTypeDeclarationSet
            {
                TypeNodeId = TankTypeId,
                Declarations = new ArrayOf<WotTypeDeclaration>(
                    new[] { Declaration("Speed", WotDeclarationKind.Variable) }),
                IsComplete = false,
                Detail = "the supertype chain was cut short"
            };

            WotDeclarationCatalog catalog = WotDeclarationCatalog.Create(
                TankTypeId, WotDeclarationScope.Effective, set, capabilityOffered: true);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Detail, Is.EqualTo("the supertype chain was cut short"));
                Assert.That(catalog.IsComplete, Is.False);
            });
        }

        /// <summary>
        /// A match is by the exact qualified BrowseName, so a name of the right
        /// spelling in another namespace is a different member. Two
        /// declarations of one qualified name are both reported, which is what
        /// lets the merge call the document ambiguous rather than pick one.
        /// </summary>
        [Test]
        public void MatchingIsByTheWholeQualifiedName()
        {
            var set = new WotTypeDeclarationSet
            {
                TypeNodeId = TankTypeId,
                Declarations = new ArrayOf<WotTypeDeclaration>(
                    new[]
                    {
                        Declaration("Speed", WotDeclarationKind.Variable),
                        Declaration("Speed", WotDeclarationKind.Method),
                        Declaration("Serial", WotDeclarationKind.Variable)
                    })
            };

            WotDeclarationCatalog catalog = WotDeclarationCatalog.Create(
                TankTypeId, WotDeclarationScope.Effective, set, capabilityOffered: true);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.IsComplete, Is.True);
                Assert.That(catalog.Detail, Is.Null);
                Assert.That(catalog.Match(ModelNamespace, "Speed"), Has.Count.EqualTo(2));
                Assert.That(catalog.Match(ModelNamespace, "Serial"), Has.Count.EqualTo(1));
                Assert.That(catalog.Match(ModelNamespace, "Absent"), Is.Empty);
                Assert.That(
                    catalog.Match("urn:test:other", "Speed"),
                    Is.Empty,
                    "The same spelling in another namespace is another member.");
            });
        }

        /// <summary>
        /// Only the two ModellingRules that oblige an instance to carry the
        /// declaration make a same-named member populate it; the rest leave the
        /// member free to stand beside it.
        /// </summary>
        [TestCase(WotModellingRule.None, false)]
        [TestCase(WotModellingRule.Mandatory, true)]
        [TestCase(WotModellingRule.Optional, false)]
        [TestCase(WotModellingRule.MandatoryPlaceholder, true)]
        [TestCase(WotModellingRule.OptionalPlaceholder, false)]
        [TestCase(WotModellingRule.ExposesItsArray, false)]
        public void OnlyTheObligingModellingRulesAreMandatory(
            WotModellingRule rule, bool mandatory)
        {
            var declaration = new WotTypeDeclaration
            {
                NamespaceUri = ModelNamespace,
                BrowseName = "Speed",
                Kind = WotDeclarationKind.Variable,
                DeclaringTypeNodeId = TankTypeId,
                ModellingRule = rule
            };

            Assert.That(declaration.IsMandatory, Is.EqualTo(mandatory));
        }

        private static WotTypeDeclaration Declaration(string browseName, WotDeclarationKind kind)
        {
            return new WotTypeDeclaration
            {
                NamespaceUri = ModelNamespace,
                BrowseName = browseName,
                Kind = kind,
                DeclaringTypeNodeId = TankTypeId
            };
        }
    }
}
