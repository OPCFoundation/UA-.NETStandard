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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Connector;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdConnectorRunnerTests
    {
        [Test]
        public void ParseDefaultsLeaveViewportAndPickingOptional()
        {
            bool parsed = OpenUsdConnectorRunner.ConnectorRunOptions.TryParse(
                [],
                @"C:\connector",
                out OpenUsdConnectorRunner.ConnectorRunOptions options);

            Assert.That(parsed, Is.True);
            Assert.That(options.Server, Is.EqualTo("opc.tcp://localhost:62542/PumpDeviceIntegrationServer"));
            Assert.That(options.OutPath, Is.EqualTo(Path.Combine(@"C:\connector", "live.usda")));
            Assert.That(options.Seconds, Is.Zero);
            Assert.That(options.View, Is.False);
            Assert.That(options.PrintPickCommands, Is.False);
            Assert.That(options.CommandPrimPath, Is.Null);
            Assert.That(options.PickMode, Is.EqualTo(UsdViewPickMode.Auto));
        }

        [Test]
        public void ParseExplicitPickCommandAndMode()
        {
            string[] args =
            [
                "--view",
                "--pick-command",
                "/World/IntentCommand",
                "--pick-mode",
                "command-prim",
                "--seconds",
                "3"
            ];

            bool parsed = OpenUsdConnectorRunner.ConnectorRunOptions.TryParse(
                args,
                @"C:\connector",
                out OpenUsdConnectorRunner.ConnectorRunOptions options);

            Assert.That(parsed, Is.True);
            Assert.That(options.View, Is.True);
            Assert.That(options.PrintPickCommands, Is.True);
            Assert.That(options.CommandPrimPath, Is.EqualTo("/World/IntentCommand"));
            Assert.That(options.PickMode, Is.EqualTo(UsdViewPickMode.CommandPrim));
            Assert.That(options.Seconds, Is.EqualTo(3));
        }

        [Test]
        public void ParseRejectsUnknownPickMode()
        {
            bool parsed = OpenUsdConnectorRunner.ConnectorRunOptions.TryParse(
                ["--pick-mode", "anything"],
                @"C:\connector",
                out OpenUsdConnectorRunner.ConnectorRunOptions options);

            Assert.That(parsed, Is.False);
            Assert.That(options.PickMode, Is.EqualTo(UsdViewPickMode.Auto));
        }

        [Test]
        public void CreateViewOptionsWiresPickCallbackOnlyWhenPrintingIsRequested()
        {
            UsdViewOptions quiet = OpenUsdConnectorRunner.CreateViewOptions(
                @"C:\stage.usda",
                renderer: "Storm",
                pluginPath: @"C:\plugins",
                cameraPath: "/World/Camera",
                printPickCommands: false,
                commandPrimPath: "/World/Command",
                pickMode: UsdViewPickMode.Renderer);
            UsdViewOptions printing = OpenUsdConnectorRunner.CreateViewOptions(
                @"C:\stage.usda",
                renderer: null,
                pluginPath: null,
                cameraPath: null,
                printPickCommands: true,
                commandPrimPath: null,
                pickMode: UsdViewPickMode.Auto);

            Assert.That(quiet.StagePath, Is.EqualTo(@"C:\stage.usda"));
            Assert.That(quiet.Renderer, Is.EqualTo("Storm"));
            Assert.That(quiet.PluginPath, Is.EqualTo(@"C:\plugins"));
            Assert.That(quiet.CameraPath, Is.EqualTo("/World/Camera"));
            Assert.That(quiet.PrimPicked, Is.Null);
            Assert.That(quiet.CommandPrimPath, Is.EqualTo("/World/Command"));
            Assert.That(quiet.PickMode, Is.EqualTo(UsdViewPickMode.Renderer));
            Assert.That(printing.PrimPicked, Is.Not.Null);
            Assert.That(printing.CommandPrimPath, Is.EqualTo(UsdViewPickCommand.DefaultCommandPrimPath));
            Assert.That(printing.PickMode, Is.EqualTo(UsdViewPickMode.Auto));
        }

        [Test]
        public async Task PrintPickedPrimWritesFormattedLineAndHonorsCancellation()
        {
            using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            await OpenUsdConnectorRunner.PrintPickedPrimAsync(
                "/World/RobotTargets/TargetA", output, CancellationToken.None);

            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();

            Assert.That(output.ToString(), Is.EqualTo("Picked prim: /World/RobotTargets/TargetA" + Environment.NewLine));
            Assert.That(
                async () => await OpenUsdConnectorRunner.PrintPickedPrimAsync(
                    "/World/RobotTargets/TargetB", output, canceled.Token).ConfigureAwait(false),
                Throws.TypeOf<OperationCanceledException>());
        }

        [Test]
        public void WriteStageUsdaComposesLiveLayerBeforeEveryFetchedRootLayer()
        {
            string cacheDir = Path.Combine(
                Environment.CurrentDirectory,
                "TestResults",
                "OpenUsdConnectorRunnerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(cacheDir);
            try
            {
                var fetched = new List<OpenUsdConnector.FetchedAsset>
                {
                    new()
                    {
                        Identifier = "textures/material.usda",
                        Kind = OpenUsdAssetKind.Texture,
                        LocalPath = Path.Combine(cacheDir, "textures", "material.usda")
                    },
                    new()
                    {
                        Identifier = "robot.usda",
                        Kind = OpenUsdAssetKind.RootLayer,
                        LocalPath = Path.Combine(cacheDir, "robot.usda")
                    },
                    new()
                    {
                        Identifier = "plant.usda",
                        Kind = OpenUsdAssetKind.RootLayer,
                        LocalPath = Path.Combine(cacheDir, "plant.usda")
                    }
                };

                OpenUsdConnectorRunner.WriteStageUsda(cacheDir, fetched);

                string stage = File.ReadAllText(Path.Combine(cacheDir, "stage.usda"));
                int liveIndex = stage.IndexOf("@./live.usda@", StringComparison.Ordinal);
                int robotIndex = stage.IndexOf("@./robot.usda@", StringComparison.Ordinal);
                int plantIndex = stage.IndexOf("@./plant.usda@", StringComparison.Ordinal);

                Assert.That(liveIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(robotIndex, Is.GreaterThan(liveIndex));
                Assert.That(plantIndex, Is.GreaterThan(robotIndex));
                Assert.That(File.Exists(Path.Combine(cacheDir, "live.usda")), Is.True);
            }
            finally
            {
                Directory.Delete(cacheDir, recursive: true);
            }
        }

        [Test]
        public void ViewportValueSinkForwardsValuesButNotComposition()
        {
            var inner = new MockUsdSink();
            var sink = new UsdViewportValueSink(inner);

            sink.SetAttribute("/Plant/Pump", "speed", Variant.From(42.0));
            sink.SetTimeSample(
                "/Plant/Pump",
                "speed",
                DateTime.UtcNow,
                Variant.From(43.0));
            sink.ComposePrim(
                "/Plant/Pump",
                OpenUsdCompositionArc.Reference,
                "@pump.usda@</Pump>",
                active: true);

            Assert.That(inner.TotalWrites, Is.EqualTo(1));
            Assert.That(inner.TimeSampleWrites, Is.EqualTo(1));
            Assert.That(inner.ComposedPrimCount, Is.Zero);
        }

        [Test]
        public void ViewportValueSinkRejectsNullInnerSink()
        {
            Assert.That(
                () => new UsdViewportValueSink(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void SiteCameraUsesProvenThreeQuarterFraming()
        {
            string stagePath = Path.Combine(
                FindRepositoryRoot(),
                "samples",
                "OpenUsd",
                "SiteCompositionServer",
                "Assets",
                "Site.usda");
            string stage = File.ReadAllText(stagePath);

            Assert.That(
                stage,
                Does.Contain("double3 xformOp:translate = (-22, -24, 14)"));
            Assert.That(
                stage,
                Does.Contain("double3 xformOp:rotateXYZ = (71, 0, -35)"));
        }

        [Test]
        public void GetPrivateStateRootCreatesConnectorDirectoryUnderOwnedRoot()
        {
            string baseDirectory = Path.Combine(
                Environment.CurrentDirectory,
                "TestResults",
                "OpenUsdConnectorRunnerTests",
                Guid.NewGuid().ToString("N"));
            try
            {
                string root = OpenUsdConnectorRunner.GetPrivateStateRoot(baseDirectory);

                Assert.That(root, Is.EqualTo(Path.Combine(baseDirectory, "Opc.Ua.OpenUsd.Connector")));
                Assert.That(Directory.Exists(root), Is.True);
            }
            finally
            {
                if (Directory.Exists(baseDirectory))
                {
                    Directory.Delete(baseDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void TryParseCommandValueRequiresEnabledInvariantDouble()
        {
            bool disabled = OpenUsdConnectorRunner.TryParseCommandValue(
                enableCommands: false, commandValueOpt: "1.25", out double disabledValue);
            bool missing = OpenUsdConnectorRunner.TryParseCommandValue(
                enableCommands: true, commandValueOpt: null, out double missingValue);
            bool invalid = OpenUsdConnectorRunner.TryParseCommandValue(
                enableCommands: true, commandValueOpt: "not-a-number", out double invalidValue);
            bool parsed = OpenUsdConnectorRunner.TryParseCommandValue(
                enableCommands: true, commandValueOpt: "1.25", out double parsedValue);

            Assert.That(disabled, Is.False);
            Assert.That(disabledValue, Is.Zero);
            Assert.That(missing, Is.False);
            Assert.That(missingValue, Is.Zero);
            Assert.That(invalid, Is.False);
            Assert.That(invalidValue, Is.Zero);
            Assert.That(parsed, Is.True);
            Assert.That(parsedValue, Is.EqualTo(1.25));
        }

        [Test]
        public async Task WaitForShutdownCompletesWhenCancellationIsRequested()
        {
            using var canceled = new CancellationTokenSource();

            Task wait = OpenUsdConnectorRunner.WaitForShutdownAsync(canceled.Token);
            await canceled.CancelAsync();

            await wait;
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new(TestContext.CurrentContext.WorkDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate the repository root from the test directory.");
        }
    }
}
