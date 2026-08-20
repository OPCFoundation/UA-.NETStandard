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

using Opc.Ua.Aas.V3;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.Aas.Tests.Dpp
{
    /// <summary>
    /// Tests the embedded DPP SSSOM mapping set.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasDppMappingSetTests
    {
        [Test]
        public void EmbeddedResourceLoadsAndParsesRows()
        {
            List<AasDppMappingRow> rows = AasDppMappingSet.ReadEmbedded().ToList();

            Assert.Multiple(() =>
            {
                Assert.That(rows, Has.Count.EqualTo(AasDppMappingSet.PinnedRowCount));
                Assert.That(rows, Has.Count.EqualTo(185));
                Assert.That(rows[0].SubjectId, Is.EqualTo("0112/2///61360_7#AAS002#001"));
            });
        }

        [Test]
        public void ColumnOrderMatchesAnnexA()
        {
            ArrayOf<string> columns = AasDppMappingSet.Columns;

            Assert.Multiple(() =>
            {
                Assert.That(columns.Count, Is.EqualTo(11));
                Assert.That(columns[0], Is.EqualTo("subject_id"));
                Assert.That(columns[1], Is.EqualTo("subject_label"));
                Assert.That(columns[2], Is.EqualTo("predicate_id"));
                Assert.That(columns[3], Is.EqualTo("object_id"));
                Assert.That(columns[4], Is.EqualTo("mapping_justification"));
                Assert.That(columns[5], Is.EqualTo("subject_source"));
                Assert.That(columns[6], Is.EqualTo("subject_source_version"));
                Assert.That(columns[7], Is.EqualTo("object_source"));
                Assert.That(columns[8], Is.EqualTo("confidence"));
                Assert.That(columns[9], Is.EqualTo("subject_type"));
                Assert.That(columns[10], Is.EqualTo("comment"));
            });
        }

        [Test]
        public void PredicateSelectionDistinguishesGlobalReferenceOnlyFromMixedIdentifier()
        {
            bool foundGlobalReferenceOnly = AasDppMappingSet.TryFindEmbedded(
                "0112/2///61360_7#AAS002#001",
                out AasDppMappingRow? globalReferenceOnly);
            bool foundMixed = AasDppMappingSet.TryFindEmbedded(
                "0173-1#01-AHF578#003",
                out AasDppMappingRow? mixed);

            Assert.Multiple(() =>
            {
                Assert.That(foundGlobalReferenceOnly, Is.True);
                Assert.That(globalReferenceOnly, Is.Not.Null);
                Assert.That(globalReferenceOnly!.SubjectType, Is.EqualTo("GlobalReference"));
                Assert.That(globalReferenceOnly.PredicateId, Is.EqualTo("skos:exactMatch"));
                Assert.That(foundMixed, Is.True);
                Assert.That(mixed, Is.Not.Null);
                Assert.That(mixed!.SubjectType, Is.EqualTo("Submodel"));
                Assert.That(mixed.PredicateId, Is.EqualTo("skos:closeMatch"));
            });
        }

        [Test]
        public void ConfidenceFollowsIdentifierConstructionRule()
        {
            bool foundHash = AasDppMappingSet.TryFindEmbedded(
                "0112/2///61360_7#AAS002#001",
                out AasDppMappingRow? hash);
            bool foundEclass = AasDppMappingSet.TryFindEmbedded(
                "0173-1#01-AHX837#002",
                out AasDppMappingRow? eclass);

            Assert.Multiple(() =>
            {
                Assert.That(foundHash, Is.True);
                Assert.That(hash, Is.Not.Null);
                Assert.That(hash!.MappingJustification, Is.EqualTo("aas-dpp-hash"));
                Assert.That(hash.Confidence, Is.EqualTo(0.5d));
                Assert.That(foundEclass, Is.True);
                Assert.That(eclass, Is.Not.Null);
                Assert.That(eclass!.MappingJustification, Is.EqualTo("eclass-rdf-part1"));
                Assert.That(eclass.Confidence, Is.EqualTo(1.0d));
            });
        }

        [Test]
        public void TsvRoundTripPreservesRows()
        {
            List<AasDppMappingRow> rows = AasDppMappingSet.ReadEmbedded().Take(3).ToList();
            var writer = new StringWriter();

            AasDppMappingSet.WriteTsv(writer, rows);
            List<AasDppMappingRow> roundTrip = AasDppMappingSet.ReadTsv(new StringReader(writer.ToString())).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(roundTrip, Has.Count.EqualTo(rows.Count));
                Assert.That(roundTrip[0], Is.EqualTo(rows[0]));
                Assert.That(writer.ToString().Split('\n')[0].TrimEnd('\r'), Is.EqualTo(AnnexAHeader));
            });
        }

        [Test]
        public void LookupMissMeansPinnedTemplateIdentifierUsesRuleOne()
        {
            const string identifier = "urn:samm:io.admin-shell.idta.batterypass:1.0.0#Battery";

            bool found = AasDppMappingSet.TryFindEmbedded(identifier, out AasDppMappingRow? row);
            AasDppIdentifierResult result = AasDppIdentifier.Construct(identifier);

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.False);
                Assert.That(row, Is.Null);
                Assert.That(result.Rule, Is.EqualTo(AasDppIdentifierRule.AlreadyIri));
                Assert.That(result.Iri, Is.EqualTo(identifier));
            });
        }

        private const string AnnexAHeader =
            "subject_id\tsubject_label\tpredicate_id\tobject_id\tmapping_justification\tsubject_source\t" +
            "subject_source_version\tobject_source\tconfidence\tsubject_type\tcomment";
    }
}
