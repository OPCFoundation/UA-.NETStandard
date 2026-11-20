/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// A materialized attribute is remotely writable and the exporter reads it
    /// back verbatim, so a value must not be able to terminate the literal it is
    /// authored into and author its own layer syntax.
    /// </summary>
    /// <remarks>
    /// The property asserted is that no payload ever becomes a line of its own
    /// in the emitted layer: a standards-compliant USD consumer resolves
    /// composition arcs and prim declarations from layer syntax, so a value that
    /// stays inside its literal cannot introduce either. The payload text itself
    /// may still appear, inertly, within the literal.
    /// </remarks>
    [TestFixture]
    public class UsdaWriterInjectionTests
    {
        /// <summary>
        /// Closes the quote, then tries to author a reference arc a renderer
        /// opening the exported file would resolve.
        /// </summary>
        private const string ArcPayload =
            "x\" )\n    prepend references = @/etc/passwd@\n)\n{\n    #";

        [Test]
        public void AStringValueCannotAuthorAnArcLine()
        {
            UsdStage stage = CreateStage("string", ArcPayload);

            string written = UsdaWriter.Write(stage);

            AssertNoInjectedLine(written);
            AssertNoInjection(UsdaReader.Parse(written, stage.StageName));
        }

        [Test]
        public void AnAssetValueCannotEscapeItsDelimiters()
        {
            UsdStage stage = CreateStage("asset", "a@<b>c\nd");

            string written = UsdaWriter.Write(stage);

            // '@', '<' and '>' would close the asset reference and a newline
            // would end the line, so none may survive into the literal.
            string assetLine = written
                .Split('\n')
                .Single(line => line.Contains("asset label", StringComparison.Ordinal));
            Assert.That(assetLine, Does.Contain("@abcd@"));
            AssertNoInjectedLine(written);
        }

        [Test]
        public void DocumentationCannotAuthorAPrimLine()
        {
            UsdStage stage = CreateStage("string", "safe");
            stage.RootPrims[0].Documentation =
                "d\"\"\"\n)\nover \"Injected\"\n{\n    #";

            string written = UsdaWriter.Write(stage);

            AssertNoInjectedLine(written);
            Assert.That(
                UsdaReader.Parse(written, stage.StageName)
                    .AllPrims().Any(p => p.Name == "Injected"),
                Is.False,
                "documentation must not be able to author a sibling prim");
        }

        [Test]
        public void AVariantSelectionCannotAuthorAnArcLine()
        {
            UsdStage stage = CreateStage("string", "safe");
            stage.RootPrims[0].VariantSets.Add(
                new UsdVariantSet("look")
                {
                    Selection = "a\" }\n    prepend references = @/etc/passwd@\n" +
                        "    variants = { string x = \""
                });

            string written = UsdaWriter.Write(stage);

            AssertNoInjectedLine(written);
        }

        /// <summary>
        /// Asserts no emitted line is itself a composition arc or a prim
        /// declaration contributed by a payload. The single prim the test
        /// authors, <c>Target</c>, is the only legitimate declaration.
        /// </summary>
        private static void AssertNoInjectedLine(string written)
        {
            string[] offenders = written
                .Split('\n')
                .Select(line => line.Trim())
                .Where(IsInjectedLine)
                .ToArray();

            Assert.That(
                offenders,
                Is.Empty,
                "a payload escaped its literal and became layer syntax");
        }

        private static bool IsInjectedLine(string line)
        {
            if (line.StartsWith("prepend references", StringComparison.Ordinal) ||
                line.StartsWith("references =", StringComparison.Ordinal) ||
                line.StartsWith("payload =", StringComparison.Ordinal))
            {
                return true;
            }

            bool declaresPrim =
                line.StartsWith("over ", StringComparison.Ordinal) ||
                line.StartsWith("def ", StringComparison.Ordinal) ||
                line.StartsWith("class ", StringComparison.Ordinal);
            return declaresPrim && !line.Contains("\"Target\"", StringComparison.Ordinal);
        }

        private static void AssertNoInjection(UsdStage reparsed)
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    reparsed.RootPrims,
                    Has.Count.EqualTo(1),
                    "no extra prim may be authored");
                Assert.That(
                    reparsed.AllPrims().SelectMany(p => p.Composition),
                    Is.Empty,
                    "no composition arc may be authored");
            });
        }

        private static UsdStage CreateStage(string typeName, string value)
        {
            var stage = new UsdStage("Injection");
            var prim = new UsdPrim("Target");
            prim.Attributes.Add(new UsdAttribute("label", typeName) { Value = UsdValue.FromString(value) });
            stage.AddRootPrim(prim);
            return stage;
        }
    }
}
