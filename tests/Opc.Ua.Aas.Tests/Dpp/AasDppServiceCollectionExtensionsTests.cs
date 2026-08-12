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

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Aas.V3;

namespace Opc.Ua.Aas.Tests.Dpp
{
    /// <summary>
    /// Tests the DPP dependency injection registration surface.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasDppServiceCollectionExtensionsTests
    {
        [Test]
        public void PolicyResolvedFromDependencyInjectionIsDppPolicy()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddAasDpp()
                .BuildServiceProvider();

            IAasDisclosurePolicy policy = provider.GetRequiredService<IAasDisclosurePolicy>();

            Assert.That(policy, Is.TypeOf<AasDppDisclosurePolicy>());
        }

        [Test]
        public void ConstructedRuleTwoAndThreeIdentifiersAreFindableButRuleOneIsAbsent()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddAasDpp()
                .BuildServiceProvider();
            IAasDppIdentifierFactory identifierFactory = provider.GetRequiredService<IAasDppIdentifierFactory>();
            IAasDppMappingSet mappingSet = provider.GetRequiredService<IAasDppMappingSet>();

            const string eclassIdentifier = "0173-1#01-AHX837#002";
            const string hashIdentifier = "0112/2///61360_7#AAS002#001";
            const string iriIdentifier = "urn:samm:io.admin-shell.idta.batterypass:1.0.0#Battery";
            AasDppIdentifierResult eclass = identifierFactory.Construct(eclassIdentifier);
            AasDppIdentifierResult hash = identifierFactory.Construct(hashIdentifier);
            AasDppIdentifierResult iri = identifierFactory.Construct(iriIdentifier);
            bool foundEclass = mappingSet.TryFind(eclass.TrimmedIdentifier, out AasDppMappingRow? eclassRow);
            bool foundHash = mappingSet.TryFind(hash.TrimmedIdentifier, out AasDppMappingRow? hashRow);
            bool foundIri = mappingSet.TryFind(iri.TrimmedIdentifier, out AasDppMappingRow? iriRow);

            Assert.Multiple(() =>
            {
                Assert.That(eclass.Rule, Is.EqualTo(AasDppIdentifierRule.EclassIrdi));
                Assert.That(foundEclass, Is.True);
                Assert.That(eclassRow!.ObjectId, Is.EqualTo(eclass.Iri));
                Assert.That(hash.Rule, Is.EqualTo(AasDppIdentifierRule.Hash));
                Assert.That(foundHash, Is.True);
                Assert.That(hashRow!.ObjectId, Is.EqualTo(hash.Iri));
                Assert.That(iri.Rule, Is.EqualTo(AasDppIdentifierRule.AlreadyIri));
                Assert.That(foundIri, Is.False);
                Assert.That(iriRow, Is.Null);
            });
        }

        [Test]
        public void RegulatoryClassSurvivesThroughDependencyInjectionDisclosureDecision()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddAasDpp(options =>
                {
                    options.DisclosureRules = new ArrayOf<AasDppDisclosureRule>(new[]
                    {
                        new AasDppDisclosureRule(
                            "Property",
                            "LegitimateInterest",
                            AasDppRegulatoryClass.LegitimateInterestAndCommission),
                        new AasDppDisclosureRule(
                            "Property",
                            "AuthorityOnly",
                            AasDppRegulatoryClass.NotifiedBodiesAndMarketSurveillanceAuthorities)
                    });
                })
                .BuildServiceProvider();
            IAasDisclosurePolicy policy = provider.GetRequiredService<IAasDisclosurePolicy>();

            AasDisclosureDecision legitimateInterest = policy.GetDisclosure(new AasProperty
            {
                IdShort = AasOptional<string>.Present("LegitimateInterest"),
                ValueType = AASDataTypeDefXsdDataType.String
            });
            AasDisclosureDecision authorityOnly = policy.GetDisclosure(new AasProperty
            {
                IdShort = AasOptional<string>.Present("AuthorityOnly"),
                ValueType = AASDataTypeDefXsdDataType.String
            });

            Assert.Multiple(() =>
            {
                Assert.That(legitimateInterest.Tier, Is.EqualTo(AASDisclosureTierDataType.Controlled));
                Assert.That(authorityOnly.Tier, Is.EqualTo(AASDisclosureTierDataType.Controlled));
                Assert.That(legitimateInterest.Authorization, Does.Contain("legitimate interest"));
                Assert.That(authorityOnly.Authorization, Does.Contain("notified bodies"));
                Assert.That(legitimateInterest.Authorization, Is.Not.EqualTo(authorityOnly.Authorization));
            });
        }
    }
}
