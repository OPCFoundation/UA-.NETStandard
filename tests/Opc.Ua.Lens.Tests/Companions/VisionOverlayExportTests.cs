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
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Vision.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using static UaLens.Tests.Companions.VisionWorkflowTestSupport;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class VisionOverlayExportTests
    {
        [Test]
        [SetCulture("de-DE")]
        public void SvgPreservesPixelGeometryAndInvariantNumbersWithoutActiveContent()
        {
            VisionDetectionResultSnapshot snapshot = Snapshot();
            snapshot.Detections[0].ClassLabel = "<script>alert('label')</script> & evidence";
            snapshot.Detections[0].BoundingBox2D.Rotation = 450;
            snapshot.Frame!.Uri = "https://media.invalid/frame?secret=never-export";
            string svg = Encoding.UTF8.GetString(VisionOverlayExport.Render(snapshot).Span);
            XElement root = XDocument.Parse(svg).Root!;
            XNamespace ns = "http://www.w3.org/2000/svg";

            Assert.That(root.Name, Is.EqualTo(ns + "svg"));
            Assert.That(root.Attribute("width")?.Value, Is.EqualTo("640"));
            Assert.That(root.Attribute("height")?.Value, Is.EqualTo("480"));
            Assert.That(root.Attribute("viewBox")?.Value, Is.EqualTo("0 0 640 480"));
            XElement rectangle = root.Descendants(ns + "rect").Single();
            Assert.That(rectangle.Attribute("x")?.Value, Is.EqualTo("25.5"));
            Assert.That(rectangle.Attribute("y")?.Value, Is.EqualTo("30.25"));
            Assert.That(rectangle.Attribute("width")?.Value, Is.EqualTo("10"));
            Assert.That(rectangle.Attribute("height")?.Value, Is.EqualTo("20"));
            Assert.That(root.Element(ns + "g")?.Attribute("transform")?.Value, Is.EqualTo("rotate(90 30.5 40.25)"));
            Assert.That(root.Descendants(ns + "text").Single().Value,
                Does.StartWith("<script>alert('label')</script> & evidence ").And.Contain("87.5"));
            Assert.That(root.Descendants().Any(element => element.Name.LocalName is "script" or "image"), Is.False);
            Assert.That(root.Descendants().Attributes().Any(attribute => attribute.Name.LocalName == "href"), Is.False);
            Assert.That(svg, Does.Not.Contain("never-export").And.Not.Contain("https://media.invalid"));
            Assert.That(root.Element(ns + "desc")!.Value,
                Does.Contain(snapshot.SensorId.ToString()).And.Contain(snapshot.PipelineId.ToString())
                    .And.Contain(Convert.ToHexString(snapshot.Frame.Digest.Span)));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(32)]
        [TestCase(33)]
        public void OverlayHonorsDetectionCapacityIncludingTheExplicitEmptyScene(int count)
        {
            VisionDetectionResultSnapshot snapshot = Snapshot() with
            {
                Detections = [.. Enumerable.Range(0, count).Select(index => Detection("detection-" + index))]
            };
            if (count <= 32)
            {
                string svg = Encoding.UTF8.GetString(VisionOverlayExport.Render(snapshot).Span);
                Assert.That(XDocument.Parse(svg).Descendants().Count(element => element.Name.LocalName == "rect"),
                    Is.EqualTo(count));
            }
            else
            {
                Assert.That(() => VisionOverlayExport.Render(snapshot),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
        }

        [TestCase("missing-frame")]
        [TestCase("missing-id")]
        [TestCase("dimensions")]
        [TestCase("extent")]
        [TestCase("nonfinite")]
        [TestCase("digest")]
        public void IncompleteOrUnsafeOverlayGeometryDoesNotProduceAFile(string fault)
        {
            VisionDetectionResultSnapshot snapshot = Snapshot();
            switch (fault)
            {
                case "missing-frame":
                    snapshot = snapshot with { Frame = null };
                    break;
                case "missing-id":
                    snapshot = snapshot with { ResultId = null };
                    break;
                case "dimensions":
                    snapshot.Frame!.Width = 0;
                    break;
                case "extent":
                    snapshot.Detections[0].BoundingBox2D.Width = 1280.01;
                    break;
                case "nonfinite":
                    snapshot.Detections[0].BoundingBox2D.CenterX = double.NaN;
                    break;
                case "digest":
                    snapshot.Frame!.Digest = ByteString.Empty;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
            Assert.That(() => VisionOverlayExport.Render(snapshot),
                Throws.TypeOf<ServiceResultException>().Or.TypeOf<ArgumentException>());
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(1_048_576)]
        [TestCase(1_048_577)]
        public async Task ExportSizeBoundIsAppliedBeforeCreatingAnyFile(int length)
        {
            string root = CreateRoot();
            try
            {
                string destination = Path.Combine(root, "overlay.svg");
                var payload = new ByteString(new byte[length]);
                if (length is > 0 and <= 1_048_576)
                {
                    await VisionOverlayExport.WriteAsync(destination, payload, static () => { }, CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(new FileInfo(destination).Length, Is.EqualTo(length));
                    Assert.That(Directory.GetFiles(root), Has.Length.EqualTo(1));
                }
                else
                {
                    await Assert.ThatAsync(() => VisionOverlayExport.WriteAsync(
                        destination, payload, static () => { }, CancellationToken.None), Throws.ArgumentException)
                        .ConfigureAwait(false);
                    Assert.That(Directory.GetFiles(root), Is.Empty);
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [TestCase("early-cancel")]
        [TestCase("late-cancel")]
        [TestCase("identity")]
        [TestCase("destination-race")]
        public async Task FailedPublicationRemovesOnlyTheOwnedStagingFile(string fault)
        {
            string root = CreateRoot();
            try
            {
                string destination = Path.Combine(root, "overlay.svg");
                string sentinel = Path.Combine(root, "preserve.txt");
                await File.WriteAllTextAsync(sentinel, "preserve").ConfigureAwait(false);
                using var cancellation = new CancellationTokenSource();
                if (fault == "early-cancel")
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                }
                int checks = 0;
                void RequireCurrent()
                {
                    if (++checks != 2)
                    {
                        return;
                    }
                    if (fault == "late-cancel")
                    {
                        cancellation.Cancel();
                    }
                    else if (fault == "identity")
                    {
                        throw new InvalidOperationException("source identity changed");
                    }
                    else if (fault == "destination-race")
                    {
                        File.WriteAllText(destination, "another writer owns this");
                    }
                }
                Task operation = VisionOverlayExport.WriteAsync(
                    destination, VisionOverlayExport.Render(Snapshot()), RequireCurrent, cancellation.Token);
                if (fault is "early-cancel" or "late-cancel")
                {
                    await Assert.ThatAsync(() => operation, Throws.InstanceOf<OperationCanceledException>())
                        .ConfigureAwait(false);
                }
                else if (fault == "identity")
                {
                    await Assert.ThatAsync(() => operation,
                        Throws.InvalidOperationException.With.Message.Contains("source identity changed"))
                        .ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(() => operation, Throws.TypeOf<IOException>()).ConfigureAwait(false);
                    Assert.That(await File.ReadAllTextAsync(destination).ConfigureAwait(false),
                        Is.EqualTo("another writer owns this"));
                }
                Assert.That(await File.ReadAllTextAsync(sentinel).ConfigureAwait(false), Is.EqualTo("preserve"));
                Assert.That(Directory.GetFiles(root, "*.pending"), Is.Empty);
                Assert.That(File.Exists(destination), Is.EqualTo(fault == "destination-race"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public async Task ExportRequiresANewLocalSvgWithoutOverwritingExistingContent()
        {
            string root = CreateRoot();
            try
            {
                string existing = Path.Combine(root, "existing.svg");
                await File.WriteAllTextAsync(existing, "keep").ConfigureAwait(false);
                Assert.That(() => VisionOverlayExport.ValidateDestination(existing), Throws.TypeOf<IOException>());
                Assert.That(() => VisionOverlayExport.ValidateDestination(Path.Combine(root, "wrong.txt")),
                    Throws.ArgumentException);
                Assert.That(() => VisionOverlayExport.ValidateDestination("relative.svg"), Throws.ArgumentException);
                Assert.That(() => VisionOverlayExport.ValidateDestination(@"\\server\share\overlay.svg"),
                    Throws.ArgumentException);
                Assert.That(() => VisionOverlayExport.ValidateDestination(@"\\?\C:\overlay.svg"),
                    Throws.ArgumentException);
                Assert.That(await File.ReadAllTextAsync(existing).ConfigureAwait(false), Is.EqualTo("keep"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public async Task ProviderExportsVerifiedTypedResultWithoutAcquiringMediaOrInference()
        {
            string root = CreateRoot();
            try
            {
                var fixture = new VisionWorkflowTestSupport();
                NodeId result = fixture.AddResult();
                var target = new CompanionTarget("vision", result, "Published result", "VisionResult");
                string destination = Path.Combine(root, "result.svg");
                var provider = new VisionCompanionProvider();
                CompanionOperationResult output = await provider.ExecuteAsync(
                    fixture.Context, target, "export-overlay", destination, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(output.Summary, Does.Contain("managed SVG"));
                string svg = await File.ReadAllTextAsync(destination).ConfigureAwait(false);
                Assert.That(svg, Does.Contain("result-7"));
                Assert.That(XDocument.Parse(svg).Descendants().Count(element => element.Name.LocalName == "rect"),
                    Is.EqualTo(1));
                fixture.Fixture.VerifyNoMutationOrSessionOwnership();
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static VisionDetectionResultSnapshot Snapshot()
        {
            return new()
            {
                NodeId = new NodeId("result-node", 1),
                ResultId = "result-7",
                SensorId = new NodeId("sensor-node", 1),
                PipelineId = new NodeId("pipeline-node", 1),
                CreationTime = new DateTimeUtc(2026, 9, 15, 6),
                Frame = Image(),
                Detections = [Detection()]
            };
        }

        private static string CreateRoot()
        {
            string root = OpenUsdTestPaths.NewDestination();
            Directory.CreateDirectory(root);
            return root;
        }
    }
}
