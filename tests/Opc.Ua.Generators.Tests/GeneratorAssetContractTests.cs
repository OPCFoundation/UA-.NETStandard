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

namespace Opc.Ua.Generators.Tests
{
    /// <summary>
    /// Pins the authoring contract the OpenUSD connector depends on.
    /// </summary>
    /// <remarks>
    /// These assertions are about the generated <c>generator.usda</c> rather than
    /// the server, so they run without a session and stay fast. They exist because
    /// every violation here fails <em>silently</em> at runtime - the geometry simply
    /// does not move, with no error anywhere - which is the hardest kind of defect
    /// to notice in a 3D scene.
    /// </remarks>
    [TestFixture]
    [Category("Generators")]
    public sealed class GeneratorAssetContractTests
    {
        private string m_asset = string.Empty;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "generator.usda");
            Assert.That(File.Exists(path), Is.True, $"The generated asset is missing: {path}");
            m_asset = File.ReadAllText(path);
        }

        /// <summary>
        /// A prim a connector positions must declare the matrix op, not the
        /// translate op.
        /// </summary>
        /// <remarks>
        /// A connector folds Translation, Rotation and Scale render targets into a
        /// single <c>xformOp:transform</c>, and <c>xformOpOrder</c> is
        /// <c>uniform</c>, so it cannot add itself to the list from the stronger
        /// layer it edits. Naming <c>xformOp:translate</c> instead makes USD discard
        /// every value written, and every configured machine renders on the origin.
        /// </remarks>
        [Test]
        public void PositionedPrimsDeclareTheMatrixTransformOp()
        {
            foreach (string prim in new[] { "Generator", "Surface" })
            {
                string ops = XformOpOrderOf(prim);
                Assert.That(
                    ops,
                    Does.Contain("xformOp:transform"),
                    $"'{prim}' is driven by a Translation target, so it must declare " +
                    "xformOp:transform or USD discards every value written to it.");
            }
        }

        /// <summary>
        /// Indicator geometry starts hidden.
        /// </summary>
        /// <remarks>
        /// An indicator that defaults to visible puts a permanent alarm halo on
        /// every machine no binding happens to drive.
        /// </remarks>
        [Test]
        public void IndicatorsDefaultToInvisible()
        {
            foreach (string indicator in new[] { "AlarmRing", "OverheatHalo", "OilHalo", "RunLamp" })
            {
                Assert.That(
                    BlockOf(indicator),
                    Does.Contain("visibility = \"invisible\""),
                    $"'{indicator}' must start hidden.");
            }
        }

        /// <summary>
        /// Every prim a live binding targets exists in the asset.
        /// </summary>
        /// <remarks>
        /// A binding whose prim path does not resolve is accepted and then does
        /// nothing, so a typo costs a silently dead indicator.
        /// </remarks>
        [Test]
        public void EveryBoundPrimExists()
        {
            foreach (string prim in new[]
            {
                "Radiator", "Fan", "Core", "LoadGauge", "TempGauge", "Needle",
                "Exhaust", "Stack", "FuelTank", "Surface", "AlarmRing",
                "OverheatHalo", "OilHalo", "RunLamp", "ControlPanel", "Engine",
                "Alternator", "Skid",
            })
            {
                Assert.That(
                    m_asset,
                    Does.Contain($"\"{prim}\""),
                    $"The asset has no '{prim}' prim, so its binding would drive nothing.");
            }
        }

        /// <summary>
        /// The component asset never references the stage that composes it.
        /// </summary>
        [Test]
        public void ComponentAssetIsSelfContained()
        {
            Assert.That(
                m_asset,
                Does.Not.Contain("/Powerhouse/"),
                "The component asset must not reference the stage that composes it.");
        }

        /// <summary>
        /// Returns the <c>xformOpOrder</c> declared on the named prim.
        /// </summary>
        /// <param name="prim">Prim name to look for.</param>
        /// <returns>The declared op order, or an empty string.</returns>
        private string XformOpOrderOf(string prim)
        {
            string block = BlockOf(prim);
            int at = block.IndexOf("xformOpOrder", StringComparison.Ordinal);
            if (at < 0)
            {
                return string.Empty;
            }
            int end = block.IndexOf(']', at);
            return end < 0 ? block[at..] : block[at..end];
        }

        /// <summary>
        /// Returns the text following a prim declaration, up to the next one.
        /// </summary>
        /// <param name="prim">Prim name to look for.</param>
        /// <returns>The prim's declaration text.</returns>
        private string BlockOf(string prim)
        {
            int at = m_asset.IndexOf($"\"{prim}\"", StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThanOrEqualTo(0), $"No '{prim}' prim in the asset.");
            int next = m_asset.IndexOf("\n    def ", at + 1, StringComparison.Ordinal);
            return next < 0 ? m_asset[at..] : m_asset[at..next];
        }
    }
}
