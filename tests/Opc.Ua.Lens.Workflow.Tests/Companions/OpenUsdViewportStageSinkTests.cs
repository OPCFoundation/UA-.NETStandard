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
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;
using Opc.Ua.OpenUsd.Connector.Viewer;
using OpenUsd;
using OpenUsd.Geom;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    [Platform("Win,Linux")]
    [Category("LensOpenUsdNative")]
    [NonParallelizable]
    public sealed class OpenUsdViewportStageSinkTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task CanonicalVectorTransformChangesTheActualRetainedStageAsync(bool resetStack)
        {
            string path = await WriteStageAsync(resetStack).ConfigureAwait(false);
            try
            {
                var failures = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
                UsdViewportRuntime.Initialize();
                var scheduler = UsdStageScheduler.Open(path, 8, 8);
                await using (scheduler.ConfigureAwait(false))
                {
                    await scheduler.EditAsync(static stage => stage.SetEditTargetToSessionLayer(),
                        UsdStageInvalidationKind.Full, default).ConfigureAwait(false);
                    var sink = new UsdStageSink(scheduler, error => failures.TrySetResult(error), true);
                    await using (sink.ConfigureAwait(false))
                    {
                        sink.SetAttribute("/World/Matrix", "xformOp:translate",
                            Variant.From((ArrayOf<double>)[1, 2, 3]));
                        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        double x = 0;
                        while (x != 1)
                        {
                            await Task.Delay(20, deadline.Token).ConfigureAwait(false);
                            x = await scheduler.InvokeAsync(stage =>
                                UsdGeomXformable.Wrap(stage.GetPrim("/World/Matrix"))
                                    .GetLocalTransform().ExtractTranslation().X, deadline.Token).ConfigureAwait(false);
                        }
                        Assert.That(failures.Task.IsCompleted, Is.False);
                        await scheduler.InvokeAsync(stage =>
                        {
                            var transform = UsdGeomXformable.Wrap(stage.GetPrim("/World/Matrix"));
                            UsdVec3d translation = transform.GetLocalTransform().ExtractTranslation();
                            Assert.That(translation.X, Is.EqualTo(1));
                            Assert.That(translation.Y, Is.EqualTo(2));
                            Assert.That(translation.Z, Is.EqualTo(3));
                            Assert.That(transform.GetResetXformStack(), Is.EqualTo(resetStack));
                        }, deadline.Token).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                DeleteStage(path);
            }
        }

        [TestCase("/World/Translate", "xformOp:translate", true, typeof(NotSupportedException))]
        [TestCase("/World/Translate", "xformOp:rotateZ", false, typeof(NotSupportedException))]
        [TestCase("/World/Missing", "temperature", false, typeof(InvalidOperationException))]
        public async Task MissingAndInactiveTargetsFailExplicitlyInsteadOfIgnoringLiveWritesAsync(
            string prim, string property, bool vector, Type errorType)
        {
            string path = await WriteStageAsync(false).ConfigureAwait(false);
            try
            {
                var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
                UsdViewportRuntime.Initialize();
                var scheduler = UsdStageScheduler.Open(path, 8, 8);
                await using (scheduler.ConfigureAwait(false))
                {
                    var sink = new UsdStageSink(scheduler, error => failure.TrySetResult(error), true);
                    await using (sink.ConfigureAwait(false))
                    {
                        sink.SetAttribute(prim, property, vector
                            ? Variant.From((ArrayOf<double>)[1, 2, 3]) : Variant.From(15d));
                        Exception error = await failure.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        Assert.That(error, Is.TypeOf(errorType));
                    }
                }
            }
            finally
            {
                DeleteStage(path);
            }
        }

        /// <summary>
        /// A drained batch authors each native attribute with its declared type and time code.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task TypedBatchDrainsIntoTheRetainedStageWithoutChangingItsSourceAsync(bool timeSamples)
        {
            string path = await WriteStageAsync(false).ConfigureAwait(false);
            try
            {
                string original = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                var failures = new ConcurrentQueue<Exception>();
                UsdViewportRuntime.Initialize();
                var scheduler = UsdStageScheduler.Open(path, 8, 8);
                await using (scheduler.ConfigureAwait(false))
                {
                    await scheduler.EditAsync(static stage => stage.SetEditTargetToSessionLayer(),
                        UsdStageInvalidationKind.Full, default).ConfigureAwait(false);
                    var sink = new UsdStageSink(scheduler, failures.Enqueue, true);
                    await using (sink.ConfigureAwait(false))
                    using (sink.BeginBatch())
                    {
                        Write("reading", Variant.From(41.5));
                        Write("enabled", Variant.From(true));
                        Write("state", Variant.From("running"));
                        var vector = Variant.From([0.25f, 0.5f, 0.75f]);
                        Write("axis", vector);
                        Write("inputs:diffuseColor", vector);
                        Write("primvars:displayColor", vector);
                        sink.SetAttribute("/World/Values", "visibility", Variant.From("invisible"));
                    }
                    Assert.That(failures, Is.Empty);
                    await scheduler.InvokeAsync(stage =>
                    {
                        UsdPrim prim = stage.GetPrim("/World/Values");
                        Assert.Multiple(() =>
                        {
                            Assert.That(timeSamples ? prim.GetDouble("reading", 42.5) : prim.GetDouble("reading"),
                                Is.EqualTo(41.5));
                            Assert.That(timeSamples ? prim.GetBool("enabled", 42.5) : prim.GetBool("enabled"), Is.True);
                            Assert.That(timeSamples ? prim.GetToken("state", 42.5) : prim.GetToken("state"),
                                Is.EqualTo("running"));
                            Assert.That(prim.GetVisibility(), Is.EqualTo("invisible"));
                            AssertVector(timeSamples ? prim.GetVec3f("axis", 42.5) : prim.GetVec3f("axis"));
                            AssertVector(timeSamples
                                ? prim.GetColor3f("inputs:diffuseColor", 42.5)
                                : prim.GetColor3f("inputs:diffuseColor"));
                            UsdVec3f[] colors = timeSamples
                                ? prim.GetColor3fArray("primvars:displayColor", 42.5)
                                : prim.GetColor3fArray("primvars:displayColor");
                            Assert.That(colors, Has.Length.EqualTo(1));
                            if (colors.Length == 1)
                            {
                                AssertVector(colors[0]);
                            }
                            if (timeSamples)
                            {
                                Assert.That(prim.GetDouble("reading"), Is.EqualTo(-1));
                                Assert.That(prim.GetBool("enabled"), Is.False);
                                Assert.That(prim.GetToken("state"), Is.EqualTo("initial"));
                            }
                        });
                    }, default).ConfigureAwait(false);

                    void Write(string property, Variant value)
                    {
                        if (timeSamples)
                        {
                            sink.SetTimeSample(
                                "/World/Values", property, DateTime.UnixEpoch.AddSeconds(42.5), value);
                        }
                        else
                        {
                            sink.SetAttribute("/World/Values", property, value);
                        }
                    }
                }
                Assert.That(await File.ReadAllTextAsync(path).ConfigureAwait(false), Is.EqualTo(original));
            }
            finally
            {
                DeleteStage(path);
            }
        }

        /// <summary>
        /// Reference literals preserve their target, arc kind and instanceability in the native stage.
        /// </summary>
        [TestCase(OpenUsdCompositionArc.Reference, false)]
        [TestCase(OpenUsdCompositionArc.Reference, true)]
        [TestCase(OpenUsdCompositionArc.Instance, false)]
        [TestCase(OpenUsdCompositionArc.Instance, true)]
        [TestCase(OpenUsdCompositionArc.Payload, false)]
        [TestCase(OpenUsdCompositionArc.Payload, true)]
        public async Task CompositionBatchResolvesDefaultAndExplicitTargetsAsync(
            OpenUsdCompositionArc arc, bool explicitTarget)
        {
            string path = await WriteStageAsync(false).ConfigureAwait(false);
            string source = Path.Combine(Path.GetDirectoryName(path)!, "source.usda");
            try
            {
                string original = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                await File.WriteAllTextAsync(source, original).ConfigureAwait(false);
                var failures = new ConcurrentQueue<Exception>();
                UsdViewportRuntime.Initialize();
                var scheduler = UsdStageScheduler.Open(path, 8, 8);
                await using (scheduler.ConfigureAwait(false))
                {
                    await scheduler.EditAsync(static stage => stage.SetEditTargetToSessionLayer(),
                        UsdStageInvalidationKind.Full, default).ConfigureAwait(false);
                    var sink = new UsdStageSink(scheduler, failures.Enqueue, true);
                    await using (sink.ConfigureAwait(false))
                    {
                        string target = explicitTarget ? "</World/Values>" : string.Empty;
                        sink.ComposePrim("/World/Composed", arc, $"@{source}@{target}", true);
                    }
                    Assert.That(failures, Is.Empty);
                    await scheduler.InvokeAsync(stage =>
                    {
                        UsdPrim composed = stage.GetPrim("/World/Composed");
                        UsdPrim values = explicitTarget ? composed : stage.GetPrim("/World/Composed/Values");
                        Assert.Multiple(() =>
                        {
                            Assert.That(composed.Exists(), Is.True);
                            Assert.That(composed.IsActive(), Is.True);
                            Assert.That(composed.IsInstanceable(), Is.EqualTo(arc == OpenUsdCompositionArc.Instance));
                            Assert.That(composed.GetPayloadArcs(), Has.Count.EqualTo(
                                arc == OpenUsdCompositionArc.Payload ? 1 : 0));
                            Assert.That(values.GetDouble("reading"), Is.EqualTo(-1),
                                "The referenced source attribute must be visible, not just an empty composed prim.");
                        });
                    }, default).ConfigureAwait(false);
                }
            }
            finally
            {
                File.Delete(source);
                DeleteStage(path);
            }
        }

        /// <summary>
        /// Structured float and double bindings preserve unbound translation and the authored reset marker.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task RotationAndScaleBatchPreservesTranslationAndResetStackAsync(
            bool singlePrecision, bool resetStack)
        {
            string path = await WriteStageAsync(resetStack).ConfigureAwait(false);
            try
            {
                var failures = new ConcurrentQueue<Exception>();
                UsdViewportRuntime.Initialize();
                var scheduler = UsdStageScheduler.Open(path, 8, 8);
                await using (scheduler.ConfigureAwait(false))
                {
                    await scheduler.EditAsync(static stage =>
                    {
                        stage.SetEditTargetToSessionLayer();
                        stage.GetPrim("/World/Matrix").SetMatrix4d("xformOp:transform", new UsdMatrix4d(
                            1, 0, 0, 0,
                            0, 1, 0, 0,
                            0, 0, 1, 0,
                            4, 5, 6, 1));
                    }, UsdStageInvalidationKind.Full, default).ConfigureAwait(false);
                    var sink = new UsdStageSink(scheduler, failures.Enqueue, true);
                    await using (sink.ConfigureAwait(false))
                    using (sink.BeginBatch())
                    {
                        ArrayOf<float> floatRotation = [0, 0, 90];
                        ArrayOf<double> doubleRotation = [0, 0, 90];
                        ArrayOf<float> floatScale = [2, 3, 4];
                        ArrayOf<double> doubleScale = [2, 3, 4];
                        sink.SetAttribute("/World/Matrix", "xformOp:rotateXYZ", singlePrecision
                            ? Variant.From(floatRotation) : Variant.From(doubleRotation));
                        sink.SetAttribute("/World/Matrix", "xformOp:scale", singlePrecision
                            ? Variant.From(floatScale) : Variant.From(doubleScale));
                    }
                    Assert.That(failures, Is.Empty);
                    await scheduler.InvokeAsync(stage =>
                    {
                        var transform = UsdGeomXformable.Wrap(stage.GetPrim("/World/Matrix"));
                        UsdMatrix4d matrix = transform.GetLocalTransform();
                        Assert.Multiple(() =>
                        {
                            Assert.That(transform.GetResetXformStack(), Is.EqualTo(resetStack));
                            AssertPoint(matrix.TransformPoint(new UsdVec3d(0, 0, 0)), 4, 5, 6);
                            AssertPoint(matrix.TransformPoint(new UsdVec3d(1, 0, 0)), 4, 7, 6);
                            AssertPoint(matrix.TransformPoint(new UsdVec3d(0, 1, 0)), 1, 5, 6);
                            AssertPoint(matrix.TransformPoint(new UsdVec3d(0, 0, 1)), 4, 5, 10);
                        });
                    }, default).ConfigureAwait(false);
                }
            }
            finally
            {
                DeleteStage(path);
            }
        }

        /// <summary>
        /// Shutdown commits the last received values rather than an intermediate interpolation sample.
        /// </summary>
        [Test]
        public async Task RetargetedScalarAndVectorValuesSettleToTheirLatestTargetsOnDrainAsync()
        {
            string path = await WriteStageAsync(false).ConfigureAwait(false);
            try
            {
                var failures = new ConcurrentQueue<Exception>();
                UsdViewportRuntime.Initialize();
                var scheduler = UsdStageScheduler.Open(path, 8, 8);
                await using (scheduler.ConfigureAwait(false))
                {
                    await scheduler.EditAsync(static stage => stage.SetEditTargetToSessionLayer(),
                        UsdStageInvalidationKind.Full, default).ConfigureAwait(false);
                    var sink = new UsdStageSink(scheduler, failures.Enqueue, true);
                    await using (sink.ConfigureAwait(false))
                    using (sink.BeginBatch())
                    {
                        sink.SetAttribute("/World/Values", "reading", Variant.From(10d));
                        sink.SetAttribute("/World/Values", "reading", Variant.From(20d));
                        ArrayOf<double> initial = [1, 2, 3];
                        ArrayOf<double> latest = [4, 5, 6];
                        sink.SetAttribute("/World/Matrix", "xformOp:translate", Variant.From(initial));
                        sink.SetAttribute("/World/Matrix", "xformOp:translate", Variant.From(latest));
                    }
                    Assert.That(failures, Is.Empty);
                    await scheduler.InvokeAsync(stage =>
                    {
                        Assert.That(stage.GetPrim("/World/Values").GetDouble("reading"), Is.EqualTo(20d));
                        var transform = UsdGeomXformable.Wrap(stage.GetPrim("/World/Matrix"));
                        AssertPoint(transform.GetLocalTransform().ExtractTranslation(), 4, 5, 6);
                    }, default).ConfigureAwait(false);
                }
            }
            finally
            {
                DeleteStage(path);
            }
        }

        private static void AssertPoint(UsdVec3d point, double x, double y, double z)
        {
            Assert.That(point.X, Is.EqualTo(x).Within(1e-12));
            Assert.That(point.Y, Is.EqualTo(y).Within(1e-12));
            Assert.That(point.Z, Is.EqualTo(z).Within(1e-12));
        }

        private static void AssertVector(UsdVec3f vector)
        {
            Assert.That(vector.X, Is.EqualTo(0.25f));
            Assert.That(vector.Y, Is.EqualTo(0.5f));
            Assert.That(vector.Z, Is.EqualTo(0.75f));
        }

        private static async Task<string> WriteStageAsync(bool resetStack)
        {
            string root = Path.GetFullPath(Path.Combine("TestResults", "viewport-sink", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "stage.usda");
            string reset = resetStack ? "\"!resetXformStack!\", " : string.Empty;
            string stage = """
                #usda 1.0
                (
                    defaultPrim = "World"
                )
                def Xform "World"
                {
                    def Xform "Values"
                    {
                        custom double reading = -1
                        custom bool enabled = false
                        custom token state = "initial"
                        custom float3 axis = (0, 0, 0)
                        custom color3f inputs:diffuseColor = (0, 0, 0)
                        color3f[] primvars:displayColor = [(0, 0, 0)]
                        token visibility = "inherited"
                    }
                    def Xform "Translate"
                    {
                        double3 xformOp:translate = (0, 0, 0)
                        uniform token[] xformOpOrder = ["xformOp:translate"]
                    }
                    def Xform "Matrix"
                    {
                        matrix4d xformOp:transform = ((1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1))
                        uniform token[] xformOpOrder = [
                """ + reset + "\"xformOp:transform\"]\n    }\n}\n";
            await File.WriteAllTextAsync(path, stage).ConfigureAwait(false);
            return path;
        }

        private static void DeleteStage(string path)
        {
            File.Delete(path);
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: false);
        }
    }
}
