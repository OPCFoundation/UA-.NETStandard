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
using System.IO;
using NUnit.Framework;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Unit tests for the defensive edges of <see cref="UsdFileSink"/>: every write path
    /// silently drops a prim path or property name that is not a valid USD identifier chain,
    /// the token escaper neutralises every character that could break out of a quoted token,
    /// and a value of an unmapped kind degrades to a typed zero rather than producing invalid
    /// USD text.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class UsdFileSinkValidationTests
    {
        private string m_path = string.Empty;

        [SetUp]
        public void SetUp()
        {
            m_path = Path.Combine(
                Path.GetTempPath(), "usdfilesink-validation-" + Guid.NewGuid().ToString("N") + ".usda");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(m_path))
            {
                File.Delete(m_path);
            }
        }

        [TestCase("")]
        [TestCase("/")]
        [TestCase("///")]
        [TestCase("/9Pump")]
        [TestCase("/Pump/../Secret")]
        [TestCase("/Pump Body")]
        public void ComposePrimRejectsAnInvalidPrimPath(string primPath)
        {
            var sink = new UsdFileSink(m_path);

            sink.ComposePrim(primPath, OpenUsdCompositionArc.Reference, "@pump.usda@", true);

            Assert.That(File.Exists(m_path), Is.False);
        }

        [Test]
        public void ComposePrimAcceptsAValidPrimPath()
        {
            var sink = new UsdFileSink(m_path);

            sink.ComposePrim("/Pump", OpenUsdCompositionArc.Reference, "@pump.usda@", true);

            Assert.That(File.ReadAllText(m_path), Does.Contain("prepend references"));
        }

        [TestCase("")]
        [TestCase("/")]
        [TestCase("/Pump/../Secret")]
        public void SetTimeSampleRejectsAnInvalidPrimPath(string primPath)
        {
            var sink = new UsdFileSink(m_path);

            sink.SetTimeSample(primPath, "radius", DateTime.UtcNow, new Variant(1.0));

            Assert.That(File.Exists(m_path), Is.False);
        }

        [TestCase("")]
        [TestCase("xformOp::translate")]
        [TestCase("2fast")]
        [TestCase("has space")]
        public void SetTimeSampleRejectsAnInvalidPropertyName(string propertyName)
        {
            var sink = new UsdFileSink(m_path);

            sink.SetTimeSample("/Pump", propertyName, DateTime.UtcNow, new Variant(1.0));

            Assert.That(File.Exists(m_path), Is.False);
        }

        [Test]
        public void SetTimeSampleAcceptsANamespacedPropertyName()
        {
            var sink = new UsdFileSink(m_path);

            sink.SetTimeSample(
                "/Pump", "xformOp:translate", new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc),
                new Variant(4.0));

            Assert.That(File.ReadAllText(m_path), Does.Contain("xformOp:translate.timeSamples"));
        }

        [Test]
        public void SetAttributeRejectsAnEmptyPropertyName()
        {
            var sink = new UsdFileSink(m_path);

            sink.SetAttribute("/Pump", string.Empty, new Variant(1.0));

            Assert.That(File.Exists(m_path), Is.False);
        }

        [Test]
        public void SetAttributeEscapesEveryTokenBreakoutCharacter()
        {
            var sink = new UsdFileSink(m_path);

            sink.SetAttribute(
                "/Pump", "visibility", new Variant("a\\b\"c\rd\te"));

            string layer = File.ReadAllText(m_path);
            Assert.That(layer, Does.Contain("token visibility = \"a\\\\b\\\"c\\rd\\te\""));
            Assert.That(layer, Does.Not.Contain("\t"));
        }

        [Test]
        public void SetAttributeWritesATypedZeroForAnUnmappedValueKind()
        {
            var sink = new UsdFileSink(m_path);

            sink.SetAttribute("/Pump", "enabled", new Variant(true));

            Assert.That(File.ReadAllText(m_path), Does.Contain("double enabled = 0.0000"));
        }
    }
}
