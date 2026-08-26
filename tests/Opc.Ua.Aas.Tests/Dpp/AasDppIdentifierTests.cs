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
    /// Tests the DPP semantic identifier construction rules.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasDppIdentifierTests
    {
        [Test]
        public void SammUrnIriIsUsedUnchanged()
        {
            const string identifier = "urn:samm:io.admin-shell.idta.batterypass:1.0.0#Battery";

            AasDppIdentifierResult result = AasDppIdentifier.Construct(identifier);

            Assert.Multiple(() =>
            {
                Assert.That(result.Iri, Is.EqualTo(identifier));
                Assert.That(result.Rule, Is.EqualTo(AasDppIdentifierRule.AlreadyIri));
                Assert.That(result.ExpectedToDereference, Is.False);
                Assert.That(result.OriginalIdentifier, Is.EqualTo(identifier));
            });
        }

        [Test]
        public void AdminShellIriIsUsedUnchangedAndDereferenceable()
        {
            const string identifier = "https://admin-shell.io/idta/CarbonFootprint/CarbonFootprint/0/9";

            AasDppIdentifierResult result = AasDppIdentifier.Construct(identifier);

            Assert.Multiple(() =>
            {
                Assert.That(result.Iri, Is.EqualTo(identifier));
                Assert.That(result.Rule, Is.EqualTo(AasDppIdentifierRule.AlreadyIri));
                Assert.That(result.ExpectedToDereference, Is.True);
            });
        }

        [Test]
        public void EclassIrdiUsesPublishedRdfResourceForm()
        {
            AasDppIdentifierResult result = AasDppIdentifier.Construct("0173-1#02-AAO677#002");

            Assert.Multiple(() =>
            {
                Assert.That(result.Iri, Is.EqualTo("https://rdf.eclass.eu/resource/0173-1_02-AAO677_002"));
                Assert.That(result.Rule, Is.EqualTo(AasDppIdentifierRule.EclassIrdi));
                Assert.That(result.ExpectedToDereference, Is.True);
            });
        }

        [Test]
        public void IecCddIrdiUsesHashIri()
        {
            AasDppIdentifierResult result = AasDppIdentifier.Construct("0112/2///61360_4#AAA123#001");

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Iri,
                    Is.EqualTo(
                        "https://w3id.org/aas-dpp/id/" +
                        "ff65a51290b8fa92acb3745ea7082f3586271499df18a833dfb7d8c0eff9d51d"));
                Assert.That(result.Rule, Is.EqualTo(AasDppIdentifierRule.Hash));
                Assert.That(result.ExpectedToDereference, Is.False);
            });
        }

        [TestCase(" 0173-1#05-AAA129#006", "0173-1#05-AAA129#006")]
        [TestCase("0173-1#07-ABZ789#003 ", "0173-1#07-ABZ789#003")]
        public void WhitespaceIsTrimmedAndReportedForAnnexCIdentifiers(
            string original,
            string trimmed)
        {
            AasDppIdentifierResult result = AasDppIdentifier.Construct(original);

            Assert.Multiple(() =>
            {
                Assert.That(result.OriginalIdentifier, Is.EqualTo(original));
                Assert.That(result.TrimmedIdentifier, Is.EqualTo(trimmed));
                Assert.That(result.WasTrimmed, Is.True);
                Assert.That(result.Rule, Is.EqualTo(AasDppIdentifierRule.EclassIrdi));
                Assert.That(result.Iri, Does.StartWith("https://rdf.eclass.eu/resource/0173-1_"));
            });
        }

        [Test]
        public void IriWithTwoHashCharactersFallsThroughToHashRule()
        {
            const string identifier = "https://example.test/dpp#one#two";

            AasDppIdentifierResult result = AasDppIdentifier.Construct(identifier);

            Assert.Multiple(() =>
            {
                Assert.That(result.Rule, Is.EqualTo(AasDppIdentifierRule.Hash));
                Assert.That(result.Iri, Does.StartWith("https://w3id.org/aas-dpp/id/"));
                Assert.That(result.OriginalIdentifier, Is.EqualTo(identifier));
            });
        }

        [Test]
        public void OriginalIdentifierIsRetainedVerbatimForHashRule()
        {
            const string identifier = "unrecognised identifier";

            AasDppIdentifierResult result = AasDppIdentifier.Construct(identifier);

            Assert.Multiple(() =>
            {
                Assert.That(result.OriginalIdentifier, Is.EqualTo(identifier));
                Assert.That(result.TrimmedIdentifier, Is.EqualTo(identifier));
                Assert.That(result.WasTrimmed, Is.False);
            });
        }
    }
}
