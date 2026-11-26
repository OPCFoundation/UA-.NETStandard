// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Connector;
using Opc.Ua.Security.Certificates;

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
        public void ParseSecurityOptions()
        {
            bool parsed = OpenUsdConnectorRunner.ConnectorRunOptions.TryParse(
                ["--trust-server", "--pki", @"D:\pki"],
                @"C:\connector",
                out OpenUsdConnectorRunner.ConnectorRunOptions options);

            Assert.That(parsed, Is.True);
            Assert.That(options.TrustServer, Is.True);
            Assert.That(options.Insecure, Is.False);
            Assert.That(options.PkiPath, Is.EqualTo(@"D:\pki"));
        }

        [Test]
        public void PkiRootIsStableAcrossRunsAndHonoursTheOption()
        {
            Assert.That(
                OpenUsdConnectorRunner.ResolvePkiRoot(null),
                Is.EqualTo(OpenUsdConnectorRunner.ResolvePkiRoot(null)));
            Assert.That(
                OpenUsdConnectorRunner.ResolvePkiRoot(null),
                Is.EqualTo(Path.Combine(OpenUsdConnectorRunner.GetPrivateStateRoot(), "pki")));
            Assert.That(
                OpenUsdConnectorRunner.ResolvePkiRoot("relative-pki"),
                Is.EqualTo(Path.GetFullPath("relative-pki")));
        }

        [Test]
        public void TrustOnFirstUseAcceptsOnlyTheFirstUntrustedCertificate()
        {
            using Certificate first = CertificateBuilder.Create("CN=First").SetRSAKeySize(2048).CreateForRSA();
            using Certificate second = CertificateBuilder.Create("CN=Second").SetRSAKeySize(2048).CreateForRSA();
            var trust = new OpenUsdConnectorRunner.TrustOnFirstUse();
            var untrusted = new ServiceResult(StatusCodes.BadCertificateUntrusted);

            Assert.That(trust.Accept(first, new ServiceResult(StatusCodes.BadCertificateHostNameInvalid)), Is.False);
            Assert.That(trust.Accept(first, new ServiceResult(StatusCodes.BadCertificateTimeInvalid)), Is.False);
            Assert.That(trust.AcceptedCertificate, Is.Null);
            Assert.That(trust.Accept(first, untrusted), Is.True);
            Assert.That(trust.Accept(first, untrusted), Is.True, "The same certificate is validated more than once.");
            Assert.That(trust.Accept(second, untrusted), Is.False);
            Assert.That(trust.AcceptedCertificate, Is.EqualTo(first.RawData));
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
                        Identifier = "parts/plant.usda",
                        Kind = OpenUsdAssetKind.RootLayer,
                        LocalPath = Path.Combine(cacheDir, "parts", "plant.usda")
                    }
                };
                File.WriteAllText(Path.Combine(cacheDir, "live.usda"), "#usda 1.0\n( subLayers = [@stale.usdc@] )\n");

                Assert.That(
                    OpenUsdConnectorRunner.TryWriteStageUsda(cacheDir, fetched, ClosureOf(fetched), out string? refusal),
                    Is.True,
                    refusal);

                string stage = File.ReadAllText(Path.Combine(cacheDir, "stage.usda"));
                int liveIndex = stage.IndexOf("@./live.usda@", StringComparison.Ordinal);
                int robotIndex = stage.IndexOf("@./robot.usda@", StringComparison.Ordinal);
                int plantIndex = stage.IndexOf("@./parts/plant.usda@", StringComparison.Ordinal);

                Assert.That(liveIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(robotIndex, Is.GreaterThan(liveIndex));
                Assert.That(plantIndex, Is.GreaterThan(robotIndex));
                Assert.That(
                    File.ReadAllText(Path.Combine(cacheDir, "live.usda")),
                    Does.Not.Contain("stale"),
                    "A live layer left by an earlier run is recreated, not composed.");
            }
            finally
            {
                Directory.Delete(cacheDir, recursive: true);
            }
        }

        [TestCase("a@, @uninspected.usdc@, @b.usda", OpenUsdAssetKind.RootLayer)]
        [TestCase("live.usda", OpenUsdAssetKind.SubLayer)]
        [TestCase("stage.usda", OpenUsdAssetKind.SubLayer)]
        [TestCase("cell.usdc", OpenUsdAssetKind.RootLayer)]
        [TestCase("parts/arm.usda", OpenUsdAssetKind.SubLayer)]
        public void WriteStageUsdaRefusesRootsItCannotAuthorSafely(string identifier, OpenUsdAssetKind kind)
        {
            string cacheDir = Path.Combine(
                Environment.CurrentDirectory,
                "TestResults",
                "OpenUsdConnectorRunnerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(cacheDir);
            try
            {
                // The last case has no root layer at all: there is no fallback to an
                // uninspected base.usda.
                var fetched = new List<OpenUsdConnector.FetchedAsset>
                {
                    new()
                    {
                        Identifier = identifier,
                        Kind = kind,
                        LocalPath = Path.Combine(cacheDir, identifier.Replace('/', Path.DirectorySeparatorChar))
                    }
                };

                Assert.That(
                    OpenUsdConnectorRunner.TryWriteStageUsda(cacheDir, fetched, ClosureOf(fetched), out string? refusal),
                    Is.False);
                Assert.That(refusal, Is.Not.Null);
                Assert.That(File.Exists(Path.Combine(cacheDir, "stage.usda")), Is.False);
            }
            finally
            {
                Directory.Delete(cacheDir, recursive: true);
            }
        }

        [Test]
        public void WriteStageUsdaRefusesARootOutsideTheInspectedClosure()
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
                        Identifier = "cell.usda",
                        Kind = OpenUsdAssetKind.RootLayer,
                        LocalPath = Path.Combine(cacheDir, "cell.usda")
                    }
                };
                File.WriteAllText(Path.Combine(cacheDir, "stage.usda"), "#usda 1.0\n");

                Assert.That(
                    OpenUsdConnectorRunner.TryWriteStageUsda(
                        cacheDir, fetched, new HashSet<string>(StringComparer.Ordinal), out _),
                    Is.False);
                OpenUsdConnectorRunner.DiscardGeneratedStage(cacheDir);
                Assert.That(File.Exists(Path.Combine(cacheDir, "stage.usda")), Is.False);
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

        [Test]
        public void RejectedCertificatesWrappedAsInvalidGetTrustGuidance()
        {
            Assert.That(OpenUsdConnectorRunner.IsCertificateTrustError(StatusCodes.BadCertificateInvalid), Is.True);
            Assert.That(OpenUsdConnectorRunner.IsCertificateTrustError(StatusCodes.BadCertificateUntrusted), Is.True);
            Assert.That(OpenUsdConnectorRunner.IsCertificateTrustError(StatusCodes.BadTimeout), Is.False);
        }

        private static HashSet<string> ClosureOf(List<OpenUsdConnector.FetchedAsset> fetched)
        {
            var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (OpenUsdConnector.FetchedAsset asset in fetched)
            {
                closure.Add(Path.GetFullPath(asset.LocalPath));
            }
            return closure;
        }

        /// <summary>
        /// Resolves the configured checkout before searching work-directory ancestors.
        /// </summary>
        /// <exception cref="DirectoryNotFoundException">The selected directory does not identify a checkout.</exception>
        private static string FindRepositoryRoot()
        {
            string? configured = Environment.GetEnvironmentVariable("OPCUA_TEST_REPOSITORY_ROOT");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                string root = Path.GetFullPath(configured);
                if (!File.Exists(Path.Combine(root, "UA.slnx")))
                {
                    throw new DirectoryNotFoundException("The configured repository root has no UA.slnx.");
                }
                return root;
            }
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
