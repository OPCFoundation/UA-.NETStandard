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
            TextWriter originalOutput = Console.Out;
            try
            {
                Console.SetOut(output);

                await OpenUsdConnectorRunner.PrintPickedPrimAsync(
                    "/World/RobotTargets/TargetA", CancellationToken.None);
            }
            finally
            {
                Console.SetOut(originalOutput);
            }

            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();

            Assert.That(output.ToString(), Is.EqualTo("Picked prim: /World/RobotTargets/TargetA" + Environment.NewLine));
            Assert.Throws<OperationCanceledException>(
                () => OpenUsdConnectorRunner.PrintPickedPrimAsync("/World/RobotTargets/TargetB", canceled.Token));
        }

        [Test]
        public void WriteStageUsdaComposesLiveLayerBeforeFetchedRootLayer()
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
                    }
                };

                OpenUsdConnectorRunner.WriteStageUsda(cacheDir, fetched);

                string stage = File.ReadAllText(Path.Combine(cacheDir, "stage.usda"));
                int liveIndex = stage.IndexOf("@./live.usda@", StringComparison.Ordinal);
                int rootIndex = stage.IndexOf("@./robot.usda@", StringComparison.Ordinal);

                Assert.That(liveIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(rootIndex, Is.GreaterThan(liveIndex));
                Assert.That(File.Exists(Path.Combine(cacheDir, "live.usda")), Is.True);
            }
            finally
            {
                Directory.Delete(cacheDir, recursive: true);
            }
        }

        [Test]
        public void GetPrivateStateRootCreatesPerUserConnectorDirectory()
        {
            string root = OpenUsdConnectorRunner.GetPrivateStateRoot();
            string fallbackRoot = OpenUsdConnectorRunner.GetPrivateStateRoot(string.Empty);

            Assert.That(root, Does.EndWith("Opc.Ua.OpenUsd.Connector"));
            Assert.That(Directory.Exists(root), Is.True);
            Assert.That(root, Does.Not.StartWith(Path.GetTempPath()).IgnoreCase);
            Assert.That(fallbackRoot, Does.StartWith(AppContext.BaseDirectory));
            Assert.That(Directory.Exists(fallbackRoot), Is.True);
            Directory.Delete(fallbackRoot, recursive: true);
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

        [Test]
        public void ProgramHelperCoversEntrypointFileWithoutRunningNetworkConnector()
        {
            Assert.That(OpenUsdConnectorProgram.HasArguments(["--view"]), Is.True);
            Assert.That(OpenUsdConnectorProgram.HasArguments([]), Is.False);
            Assert.That(OpenUsdConnectorProgram.NormalizeExitCode(-1), Is.EqualTo(1));
            Assert.That(OpenUsdConnectorProgram.NormalizeExitCode(5), Is.EqualTo(5));
        }
    }
}
