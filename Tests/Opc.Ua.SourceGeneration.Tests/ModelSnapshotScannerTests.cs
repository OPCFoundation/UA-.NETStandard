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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Opc.Ua.SourceGeneration.Snapshot;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Tests for <see cref="ReferencedModelSnapshotScanner"/>.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ModelSnapshotScannerTests
    {
        [Test]
        public void ScanReturnsEmptyWhenAttributeTypeNotFound()
        {
            var compilation = CSharpCompilation.Create("Empty",
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            ImmutableArray<ReferencedModelSnapshot> result =
                ReferencedModelSnapshotScanner.Scan(compilation);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ScanReturnsEmptyWhenNullCompilation()
        {
            ImmutableArray<ReferencedModelSnapshot> result =
                ReferencedModelSnapshotScanner.Scan(null);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ScanReadsSnapshotAttributeFromReferencedCompilation()
        {
            var snapshot = new ModelSnapshotV1 { ModelUri = "urn:test:Producer" };
            snapshot.Nodes.Add(new SnapshotNode
            {
                SymbolicName = "WidgetType",
                SymbolicNamespace = "urn:test:Producer",
                ClassName = "Widget",
                Kind = SnapshotNodeKind.ObjectType,
                NumericId = 1001
            });
            string payload = snapshot.ToBase64Payload();

            CSharpCompilation producer = OptimizationLevel.Release.CreateCompilation("Producer")
                .AddCode(new Dictionary<string, string>
                {
                    ["AssemblyAttributes.cs"] =
                        "[assembly: global::Opc.Ua.ModelSnapshotAttribute(" +
                        "\"urn:test:Producer\", \"" + payload + "\")]"
                }, LanguageVersion.CSharp11);

            ImmutableArray<Diagnostic> diags = producer.GetDiagnostics();
            Assert.That(diags.Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty, "Producer compilation must compile");

            CSharpCompilation consumer = OptimizationLevel.Release.CreateCompilation("Consumer")
                .AddReferences(producer.ToMetadataReference());

            ImmutableArray<ReferencedModelSnapshot> result =
                ReferencedModelSnapshotScanner.Scan(consumer);

            Assert.That(result, Has.Length.EqualTo(1));
            ReferencedModelSnapshot entry = result[0];
            Assert.That(entry.ModelUri, Is.EqualTo("urn:test:Producer"));
            Assert.That(entry.AssemblyName, Is.EqualTo("Producer"));

            ModelSnapshotV1 decoded = entry.GetSnapshot();
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.ModelUri, Is.EqualTo("urn:test:Producer"));
            Assert.That(decoded.Nodes, Has.Count.EqualTo(1));
            Assert.That(decoded.Nodes[0].SymbolicName, Is.EqualTo("WidgetType"));
            Assert.That(decoded.Nodes[0].NumericId, Is.EqualTo(1001u));
        }

        [Test]
        public void ScanIgnoresAttributesWithEmptyUriOrPayload()
        {
            CSharpCompilation producer = OptimizationLevel.Release.CreateCompilation("Producer")
                .AddCode(new Dictionary<string, string>
                {
                    ["AssemblyAttributes.cs"] =
                        "[assembly: global::Opc.Ua.ModelSnapshotAttribute(\"\", \"abc\")]" +
                        System.Environment.NewLine +
                        "[assembly: global::Opc.Ua.ModelSnapshotAttribute(\"urn:x\", \"\")]"
                }, LanguageVersion.CSharp11);

            CSharpCompilation consumer = OptimizationLevel.Release.CreateCompilation("Consumer")
                .AddReferences(producer.ToMetadataReference());

            ImmutableArray<ReferencedModelSnapshot> result =
                ReferencedModelSnapshotScanner.Scan(consumer);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void GetSnapshotReturnsNullForMalformedPayload()
        {
            CSharpCompilation producer = OptimizationLevel.Release.CreateCompilation("Producer")
                .AddCode(new Dictionary<string, string>
                {
                    ["AssemblyAttributes.cs"] =
                        "[assembly: global::Opc.Ua.ModelSnapshotAttribute(" +
                        "\"urn:test:Bad\", \"not-base64@@@\")]"
                }, LanguageVersion.CSharp11);

            CSharpCompilation consumer = OptimizationLevel.Release.CreateCompilation("Consumer")
                .AddReferences(producer.ToMetadataReference());

            ImmutableArray<ReferencedModelSnapshot> result =
                ReferencedModelSnapshotScanner.Scan(consumer);

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].GetSnapshot(), Is.Null,
                "Malformed payload must return null instead of throwing.");
        }
    }
}
