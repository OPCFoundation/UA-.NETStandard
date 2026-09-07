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
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Vision.OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Pins the guarantee that the OpenUsd provider will never surface a
    /// blank frame as a successful image. The guard exists because the
    /// underlying Silk backend has been observed to return blank frames
    /// after session reuse, and vision consumers must not treat that as
    /// a valid capture. A refactor that changes the definition of
    /// "blank" in any way must update these tests deliberately.
    /// </summary>
    [TestFixture]
    public sealed class BlankFrameGuardTests
    {
        [Test]
        public void DrawCountZeroIsBlankAndReasonExplainsWhy()
        {
            SilkFrameCaptureResult capture = MakeCapture(
                width: 4, height: 4,
                rgba: MakeGradient(4, 4),
                drawCount: 0,
                meshCount: 5);

            BlankFrameCheck check = BlankFrameGuard.Check(capture);

            Assert.Multiple(() =>
            {
                Assert.That(check.IsBlank, Is.True);
                Assert.That(check.DrawCount, Is.EqualTo(0));
                Assert.That(check.MeshCount, Is.EqualTo(5));
                Assert.That(check.IsUniform, Is.False,
                    "IsUniform must remain false because the guard short-circuits before scanning pixels.");
                Assert.That(check.Reason, Is.Not.Null);
                Assert.That(check.Reason, Does.Contain("drawCount=0"));
            });
        }

        [Test]
        public void MeshCountZeroIsBlankEvenWhenDrawCountIsPositive()
        {
            SilkFrameCaptureResult capture = MakeCapture(
                width: 4, height: 4,
                rgba: MakeGradient(4, 4),
                drawCount: 3,
                meshCount: 0);

            BlankFrameCheck check = BlankFrameGuard.Check(capture);

            Assert.Multiple(() =>
            {
                Assert.That(check.IsBlank, Is.True);
                Assert.That(check.DrawCount, Is.EqualTo(3));
                Assert.That(check.MeshCount, Is.EqualTo(0));
                Assert.That(check.Reason, Does.Contain("meshCount=0"));
            });
        }

        [Test]
        public void UniformRgbaBufferIsBlankAndFlaggedAsUniform()
        {
            byte[] rgba = new byte[4 * 4 * 4];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                rgba[i] = 0x20;
                rgba[i + 1] = 0x30;
                rgba[i + 2] = 0x40;
                rgba[i + 3] = 0xFF;
            }
            SilkFrameCaptureResult capture = MakeCapture(4, 4, rgba, drawCount: 1, meshCount: 1);

            BlankFrameCheck check = BlankFrameGuard.Check(capture);

            Assert.Multiple(() =>
            {
                Assert.That(check.IsBlank, Is.True);
                Assert.That(check.IsUniform, Is.True);
                Assert.That(check.DrawCount, Is.EqualTo(1));
                Assert.That(check.MeshCount, Is.EqualTo(1));
                Assert.That(check.Reason, Is.Not.Null);
                Assert.That(check.Reason, Does.Contain("same value"));
            });
        }

        [Test]
        public void NonUniformBufferWithPositiveDrawAndMeshCountsIsNotBlank()
        {
            byte[] rgba = MakeGradient(4, 4);
            SilkFrameCaptureResult capture = MakeCapture(4, 4, rgba, drawCount: 12, meshCount: 3);

            BlankFrameCheck check = BlankFrameGuard.Check(capture);

            Assert.Multiple(() =>
            {
                Assert.That(check.IsBlank, Is.False);
                Assert.That(check.IsUniform, Is.False);
                Assert.That(check.DrawCount, Is.EqualTo(12));
                Assert.That(check.MeshCount, Is.EqualTo(3));
                Assert.That(check.Reason, Is.Null,
                    "A non-blank frame must not surface a reason - callers switch on Reason != null.");
            });
        }

        [Test]
        public void CheckThrowsArgumentNullExceptionForNullResult()
        {
            Assert.That(
                () => BlankFrameGuard.Check(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void SingletonNonBlackPixelIsEnoughToBeConsideredNonBlank()
        {
            byte[] rgba = new byte[4 * 4 * 4];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                rgba[i + 3] = 0xFF;
            }
            rgba[0] = 0x01;
            SilkFrameCaptureResult capture = MakeCapture(4, 4, rgba, drawCount: 1, meshCount: 1);

            BlankFrameCheck check = BlankFrameGuard.Check(capture);

            Assert.Multiple(() =>
            {
                Assert.That(check.IsBlank, Is.False,
                    "A single differing pixel must be enough to disqualify the frame as uniform.");
                Assert.That(check.IsUniform, Is.False);
            });
        }

        [Test]
        public void EmptyRgbaBufferIsTreatedAsUniformAndBlank()
        {
            byte[] rgba = new byte[3];
            SilkFrameCaptureResult capture = MakeCapture(0, 0, rgba, drawCount: 1, meshCount: 1);

            BlankFrameCheck check = BlankFrameGuard.Check(capture);

            Assert.Multiple(() =>
            {
                Assert.That(check.IsBlank, Is.True,
                    "A buffer smaller than a single RGBA8 pixel has nothing to render, so " +
                    "the guard must classify it as blank rather than pretend it is a good frame.");
                Assert.That(check.IsUniform, Is.True,
                    "The empty-buffer branch of IsUniformRgba8 returns true; the guard " +
                    "must propagate that as IsUniform=true so callers can distinguish it " +
                    "from a no-draws blank.");
            });
        }

        private static byte[] MakeGradient(int width, int height)
        {
            byte[] rgba = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = ((y * width) + x) * 4;
                    rgba[i] = (byte)(x * 8);
                    rgba[i + 1] = (byte)(y * 8);
                    rgba[i + 2] = (byte)((x + y) * 4);
                    rgba[i + 3] = 0xFF;
                }
            }
            return rgba;
        }

        private static SilkFrameCaptureResult MakeCapture(
            int width, int height, byte[] rgba, int drawCount, int meshCount)
        {
            var stats = new SilkSceneGpuStatistics(meshCount, 0UL, 0UL, 0UL, 0UL, 0UL, 0UL, 0UL);
            var render = new SilkMeshRenderResult(drawCount, 0, stats);
            ConstructorInfo? ctor = typeof(SilkFrameCaptureResult).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[]
                {
                    typeof(int),
                    typeof(int),
                    typeof(byte[]),
                    typeof(SilkMeshRenderResult),
                    typeof(ulong),
                    typeof(uint)
                },
                modifiers: null);
            Assert.That(ctor, Is.Not.Null,
                "SilkFrameCaptureResult's internal (int,int,byte[],SilkMeshRenderResult,ulong,uint) constructor must exist for BlankFrameGuard tests to construct fixtures.");
            return (SilkFrameCaptureResult)ctor!.Invoke(new object[] { width, height, rgba, render, 0UL, 0U });
        }
    }
}
