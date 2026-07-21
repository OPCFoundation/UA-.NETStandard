/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using NUnit.Framework;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Unit tests for <see cref="UsdFileSink"/>: USD text authoring, identifier
    /// validation, value formatting, composition metadata and batching.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class UsdFileSinkTests
    {
        private string m_path = string.Empty;

        [SetUp]
        public void SetUp()
        {
            m_path = Path.Combine(Path.GetTempPath(), "usdfilesink-" + Guid.NewGuid().ToString("N") + ".usda");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(m_path))
            {
                File.Delete(m_path);
            }
        }

        private string ReadLayer() => File.ReadAllText(m_path);

        [Test]
        public void SetAttributeAuthorsTypedScalarProperty()
        {
            var sink = new UsdFileSink(m_path);
            sink.SetAttribute("/Pump", "radius", new Variant(2.5));

            string layer = ReadLayer();
            Assert.That(layer, Does.Contain("over \"Pump\""));
            Assert.That(layer, Does.Contain("double radius = 2.5000"));
        }

        [Test]
        public void SetAttributeDisplayColorAuthorsColor3fArray()
        {
            var sink = new UsdFileSink(m_path);
            sink.SetAttribute("/Pump", "primvars:displayColor",
                new Variant((ArrayOf<float>)new[] { 1f, 0f, 0f }));

            string layer = ReadLayer();
            Assert.That(layer, Does.Contain("color3f[] primvars:displayColor = [(1.0000, 0.0000, 0.0000)]"));
        }

        [Test]
        public void SetAttributeNonDisplayColorAuthorsColor3fScalar()
        {
            var sink = new UsdFileSink(m_path);
            sink.SetAttribute("/Pump", "inputs:emissiveColor",
                new Variant((ArrayOf<float>)new[] { 0.1f, 1f, 0.2f }));

            string layer = ReadLayer();
            Assert.That(layer, Does.Contain("color3f inputs:emissiveColor = (0.1000, 1.0000, 0.2000)"));
        }

        [Test]
        public void SetAttributeTokenEscapesControlCharacters()
        {
            var sink = new UsdFileSink(m_path);
            sink.SetAttribute("/Pump", "visibility", new Variant("line1\nline2"));

            string layer = ReadLayer();
            // The newline is escaped so the token value never breaks out of its line.
            Assert.That(layer, Does.Contain("token visibility = \"line1\\nline2\""));
            Assert.That(layer, Does.Not.Contain("line1\nline2"));
        }

        [Test]
        public void NestedPrimPathBuildsHierarchy()
        {
            var sink = new UsdFileSink(m_path);
            sink.SetAttribute("/Plant/Line1/Pump", "radius", new Variant(1.0));

            string layer = ReadLayer();
            Assert.That(layer, Does.Contain("over \"Plant\""));
            Assert.That(layer, Does.Contain("over \"Line1\""));
            Assert.That(layer, Does.Contain("over \"Pump\""));
        }

        [Test]
        public void InvalidPrimPathIsRejectedAndNothingIsWritten()
        {
            var sink = new UsdFileSink(m_path);
            sink.SetAttribute("1bad/Pump", "radius", new Variant(1.0));

            Assert.That(File.Exists(m_path), Is.False, "An invalid prim path must not author a layer.");
        }

        [Test]
        public void PrimPathTraversalSegmentIsRejected()
        {
            var sink = new UsdFileSink(m_path);
            sink.SetAttribute("/Pump/../Root", "radius", new Variant(1.0));

            Assert.That(File.Exists(m_path), Is.False, "A '..' path segment must be rejected.");
        }

        [Test]
        public void InvalidPropertyNameIsRejected()
        {
            var sink = new UsdFileSink(m_path);
            sink.SetAttribute("/Pump", "bad prop", new Variant(1.0));

            Assert.That(File.Exists(m_path), Is.False, "An invalid property name must be rejected.");
        }

        [Test]
        public void ComposePrimReferenceAuthorsPrependReferences()
        {
            var sink = new UsdFileSink(m_path);
            sink.ComposePrim("/Pump", OpenUsdCompositionArc.Reference, "@pump.usda@", active: true);

            string layer = ReadLayer();
            Assert.That(layer, Does.Contain("prepend references = @pump.usda@"));
            Assert.That(layer, Does.Contain("active = true"));
        }

        [Test]
        public void ComposePrimPayloadAuthorsPrependPayload()
        {
            var sink = new UsdFileSink(m_path);
            sink.ComposePrim("/Pump", OpenUsdCompositionArc.Payload, "@pump.usda@", active: true);

            Assert.That(ReadLayer(), Does.Contain("prepend payload = @pump.usda@"));
        }

        [Test]
        public void ComposePrimInstanceAuthorsInstanceable()
        {
            var sink = new UsdFileSink(m_path);
            sink.ComposePrim("/Pump", OpenUsdCompositionArc.Instance, "@pump.usda@", active: true);

            string layer = ReadLayer();
            Assert.That(layer, Does.Contain("instanceable = true"));
            Assert.That(layer, Does.Contain("prepend references = @pump.usda@"));
        }

        [Test]
        public void ComposePrimInactiveAuthorsActiveFalse()
        {
            var sink = new UsdFileSink(m_path);
            sink.ComposePrim("/Pump", OpenUsdCompositionArc.Child, assetReference: null, active: false);

            Assert.That(ReadLayer(), Does.Contain("active = false"));
        }

        [Test]
        public void UnsafeAssetReferenceIsNotAuthored()
        {
            var sink = new UsdFileSink(m_path);
            sink.ComposePrim("/Pump", OpenUsdCompositionArc.Reference, "bad\"ref", active: true);

            string layer = ReadLayer();
            Assert.That(layer, Does.Not.Contain("references"));
            // The prim itself is still authored (active state), only the unsafe ref is dropped.
            Assert.That(layer, Does.Contain("over \"Pump\""));
        }

        [Test]
        public void TimeSamplesAuthorFrameBlock()
        {
            var sink = new UsdFileSink(m_path);
            var t0 = new DateTime(1970, 1, 1, 0, 0, 1, DateTimeKind.Utc);
            var t1 = new DateTime(1970, 1, 1, 0, 0, 2, DateTimeKind.Utc);
            using (sink.BeginBatch())
            {
                sink.SetTimeSample("/Pump", "radius", t0, new Variant(1.0));
                sink.SetTimeSample("/Pump", "radius", t1, new Variant(2.0));
            }

            string layer = ReadLayer();
            Assert.That(layer, Does.Contain("double radius.timeSamples = {"));
            Assert.That(layer, Does.Contain("1.000: 1.0000"));
            Assert.That(layer, Does.Contain("2.000: 2.0000"));
        }

        [Test]
        public void BatchDefersWriteUntilScopeDisposed()
        {
            var sink = new UsdFileSink(m_path);
            IDisposable batch = sink.BeginBatch();
            sink.SetAttribute("/Pump", "radius", new Variant(1.0));

            Assert.That(File.Exists(m_path), Is.False, "Writes must be deferred while a batch is open.");
            batch.Dispose();
            Assert.That(File.Exists(m_path), Is.True, "Closing the batch must flush the layer once.");
            Assert.That(ReadLayer(), Does.Contain("double radius = 1.0000"));
        }
    }
}
