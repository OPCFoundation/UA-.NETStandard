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
using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class OpenUsdViewportScenePolicyTests
    {
        [Test]
        public void CapturedCompositionAcceptsLocalReferencesAndIgnoresDocumentationAndComments()
        {
            OpenUsdVerifiedAsset root = Layer("cell.usda", """"
                #usda 1.0
                (doc = """Do not fetch @https://outside.invalid/file@ or parse def Evil here.""")
                # @outside.usda@ is documentation, not an asset dependency.
                def Xform "World" { def Xform "Arm" (references = @./arm.usda@</Arm>) {} }
                """");
            Assert.That(() => OpenUsdViewportScenePolicy.Validate("cell.usda",
                [root, Layer("arm.usda", "#usda 1.0\ndef Cube \"Arm\" { double size = 1 }")]), Throws.Nothing);
        }

        [TestCase("@https://outside.invalid/scene.usda@", typeof(ServiceResultException))]
        [TestCase("@../other.usda@", typeof(ServiceResultException))]
        [TestCase("@C:\\other.usda@", typeof(ServiceResultException))]
        [TestCase("@missing.usda@", typeof(InvalidDataException))]
        [TestCase("</OtherPrim>", typeof(NotSupportedException))]
        public void UncapturedOrInternalCompositionIsRefusedBeforeNativeLoading(string reference, Type exceptionType)
        {
            OpenUsdVerifiedAsset root = Layer("cell.usda",
                $"#usda 1.0\ndef Xform \"World\" (references = {reference}) {{ }}");
            Assert.That(() => OpenUsdViewportScenePolicy.Validate("cell.usda", [root]),
                Throws.TypeOf(exceptionType));
        }

        [TestCase("PointInstancer")]
        [TestCase("Volume")]
        [TestCase("SkelRoot")]
        [TestCase("NurbsCurves")]
        public void ProceduralAndAmplifyingPrimTypesAreExplicitlyUnsupported(string type)
        {
            Assert.That(() => OpenUsdViewportScenePolicy.Validate("cell.usda",
                [Layer("cell.usda", $"#usda 1.0\ndef {type} \"World\" {{ }}")]),
                Throws.TypeOf<NotSupportedException>());
        }

        [TestCase("inherits = </Class>")]
        [TestCase("clips = { }")]
        [TestCase("variants = { }")]
        public void UnboundedCompositionFeaturesAreNotPassedToUsd(string feature)
        {
            Assert.That(() => OpenUsdViewportScenePolicy.Validate("cell.usda",
                [Layer("cell.usda", $"#usda 1.0\ndef Xform \"World\" ({feature}) {{ }}")]),
                Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void CyclicReferenceClosureIsRejected()
        {
            Assert.That(() => OpenUsdViewportScenePolicy.Validate("one.usda",
            [
                Layer("one.usda", "#usda 1.0\ndef Xform \"One\" (references = @two.usda@) {}"),
                Layer("two.usda", "#usda 1.0\ndef Xform \"Two\" (references = @one.usda@) {}")
            ]), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void AnInternalReferenceCannotHideBehindACapturedReferenceInAList()
        {
            Assert.That(() => OpenUsdViewportScenePolicy.Validate("cell.usda",
            [
                Layer("cell.usda", "#usda 1.0\ndef Xform \"World\" (references = [@arm.usda@</Arm>, </Other>]) {}"),
                Layer("arm.usda", "#usda 1.0\ndef Cube \"Arm\" {}")
            ]), Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void PrimLimitIsCheckedBeforeNativeStageAllocation()
        {
            string prims = string.Join("\n", Enumerable.Range(0, OpenUsdViewportScenePolicy.MaximumPrims)
                .Select(static index => $"def Cube \"P{index}\" {{ }}"));
            Assert.That(() => OpenUsdViewportScenePolicy.Validate(
                "cell.usda", [Layer("cell.usda", "#usda 1.0\n" + prims)]), Throws.Nothing);
            Assert.That(() => OpenUsdViewportScenePolicy.Validate(
                "cell.usda", [Layer("cell.usda", "#usda 1.0\n" + prims + "\ndef Cube \"Overflow\" {}")]),
                Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void ExpandedReferenceCostIsCheckedNotOnlyTheSourceFileSizes()
        {
            string references = string.Join("\n", Enumerable.Range(0, 64).Select(
                static index => $"def Xform \"I{index}\" (references = @arm.usda@) {{ }}"));
            string children = string.Join("\n", Enumerable.Range(0, 64).Select(
                static index => $"def Cube \"C{index}\" {{ }}"));
            Assert.That(() => OpenUsdViewportScenePolicy.Validate("cell.usda",
            [
                Layer("cell.usda", "#usda 1.0\n" + references),
                Layer("arm.usda", "#usda 1.0\n" + children)
            ]), Throws.TypeOf<InvalidDataException>());
        }

        [TestCase("-1")]
        [TestCase("2")]
        [TestCase("65")]
        [TestCase("2147483647")]
        [TestCase("3.5")]
        public void MeshFaceCountsCannotExpandBeyondTheAdmittedProfile(string count)
        {
            Assert.That(() => OpenUsdViewportScenePolicy.Validate("cell.usda",
                [Layer("cell.usda", $"#usda 1.0\ndef Mesh \"M\" {{ int[] faceVertexCounts = [{count}] }}")]),
                Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void CaseCollisionsAndBinaryAssetsCannotEnterThePrivateCache()
        {
            Assert.That(() => OpenUsdViewportScenePolicy.Validate("cell.usda",
                [Layer("cell.usda", "#usda 1.0"), Layer("Cell.usda", "#usda 1.0")]),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => OpenUsdViewportScenePolicy.Validate("cell.usdc",
                [Layer("cell.usdc", "PXR-USDC")]), Throws.TypeOf<NotSupportedException>());
        }

        internal static OpenUsdVerifiedAsset Layer(string identifier, string content)
        {
            return new OpenUsdVerifiedAsset(identifier, ByteString.From(Encoding.UTF8.GetBytes(content)));
        }
    }
}
