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

using NUnit.Framework;
using Opc.Ua.Aas.V3;

namespace Opc.Ua.Aas.Tests.Dpp
{
    /// <summary>
    /// Tests the DPP disclosure tier mapping.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasDppDisclosurePolicyTests
    {
        [Test]
        public void RegulatoryClassesMapToDisclosureTiers()
        {
            AasDisclosureDecision publicDecision = AasDppDisclosurePolicy.Map(
                AasDppRegulatoryClass.AvailableToPublic);
            AasDisclosureDecision legitimateInterestDecision = AasDppDisclosurePolicy.Map(
                AasDppRegulatoryClass.LegitimateInterestAndCommission);
            AasDisclosureDecision authorityDecision = AasDppDisclosurePolicy.Map(
                AasDppRegulatoryClass.NotifiedBodiesAndMarketSurveillanceAuthorities);

            Assert.Multiple(() =>
            {
                Assert.That(publicDecision.Tier, Is.EqualTo(AASDisclosureTierDataType.Public));
                Assert.That(legitimateInterestDecision.Tier, Is.EqualTo(AASDisclosureTierDataType.Controlled));
                Assert.That(authorityDecision.Tier, Is.EqualTo(AASDisclosureTierDataType.Controlled));
            });
        }

        [Test]
        public void ControlledRegulatoryClassesRemainDistinguishable()
        {
            AasDisclosureDecision legitimateInterestDecision = AasDppDisclosurePolicy.Map(
                AasDppRegulatoryClass.LegitimateInterestAndCommission);
            AasDisclosureDecision authorityDecision = AasDppDisclosurePolicy.Map(
                AasDppRegulatoryClass.NotifiedBodiesAndMarketSurveillanceAuthorities);

            Assert.Multiple(() =>
            {
                Assert.That(legitimateInterestDecision.Tier, Is.EqualTo(authorityDecision.Tier));
                Assert.That(legitimateInterestDecision.DisclosureClass, Is.Not.EqualTo(authorityDecision.DisclosureClass));
                Assert.That(legitimateInterestDecision.Authorization, Does.Contain("legitimate interest"));
                Assert.That(authorityDecision.Authorization, Does.Contain("notified bodies"));
            });
        }

        [Test]
        public void PolicyAppliesMatchingRuleBeforeDefaultClass()
        {
            var element = new AasProperty
            {
                IdShort = AasOptional<string>.Present("RestrictedElement"),
                ValueType = AASDataTypeDefXsdDataType.String
            };
            var policy = new AasDppDisclosurePolicy(
                new ArrayOf<AasDppDisclosureRule>(
                    new[]
                    {
                        new AasDppDisclosureRule(
                            "Property",
                            "RestrictedElement",
                            AasDppRegulatoryClass.NotifiedBodiesAndMarketSurveillanceAuthorities)
                    }),
                AasDppRegulatoryClass.AvailableToPublic);

            AasDisclosureDecision decision = policy.GetDisclosure(element);

            Assert.Multiple(() =>
            {
                Assert.That(decision.Tier, Is.EqualTo(AASDisclosureTierDataType.Controlled));
                Assert.That(
                    decision.DisclosureClass,
                    Is.EqualTo("available to notified bodies and market surveillance authorities"));
            });
        }
    }
}
