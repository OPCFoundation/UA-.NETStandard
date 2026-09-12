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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Samples;

namespace UaLens.Tests.Samples
{
    [TestFixture]
    public sealed class RepositorySampleSetupTests
    {
        [TestCase(16, true)]
        [TestCase(17, false)]
        public async Task CleanupDepthBoundIsCheckedBeforeDeletingFiles(int depth, bool allowed)
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                RepositorySampleLaunch launch = await context.PrepareAsync(
                    RepositorySampleId.ConsoleReferenceServer).ConfigureAwait(false);
                string deepest = launch.Files.Root;
                for (int index = 0; index < depth; index++)
                {
                    deepest = Path.Combine(deepest, "d");
                }
                Directory.CreateDirectory(deepest);
                string sentinel = Path.Combine(launch.Files.Root, "preserve-until-checked.txt");
                await File.WriteAllTextAsync(sentinel, "owned sentinel").ConfigureAwait(false);
                if (!allowed)
                {
                    await Assert.ThatAsync(
                        () => launch.Files.CleanupAsync(CancellationToken.None),
                        Throws.TypeOf<IOException>()).ConfigureAwait(false);
                    Assert.That(await File.ReadAllTextAsync(sentinel).ConfigureAwait(false),
                        Is.EqualTo("owned sentinel"));
                    Assert.That(File.Exists(Path.Combine(launch.Files.Root, RepositorySampleRunFiles.MarkerName)),
                        Is.True);
                    Directory.Delete(deepest, recursive: false);
                }
                await launch.Files.CleanupAsync(CancellationToken.None).ConfigureAwait(false);
                Assert.That(Directory.Exists(launch.Files.Root), Is.False);
            }
        }

        [Test]
        public async Task DirectConstructionAndRestoreHaveNoTelemetryOrLaunchSideEffects()
        {
            var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            var service = new RepositorySampleService(telemetry.Object);
            await using (service.ConfigureAwait(false))
            {
                await service.RestoreSelectionAsync(RepositorySampleId.PumpSoftwareUpdateSimulator)
                    .ConfigureAwait(false);

                Assert.That(service.Snapshot.Phase, Is.EqualTo(RepositorySamplePhase.RequiresConfiguration));
                Assert.That(service.Snapshot.ProcessId, Is.Null);
                Assert.That(service.Snapshot.PrivatePkiRoot, Is.Null);
                Assert.That(service.Snapshot.OwnsResources, Is.False);
                Assert.That(service.Completion.IsCompletedSuccessfully, Is.True);
                telemetry.VerifyNoOtherCalls();
            }
        }

        [TestCase(true, 8, false)]
        [TestCase(false, 8, true)]
        [TestCase(true, 9, true)]
        [TestCase(false, 9, false)]
        [TestCase(true, 10, false)]
        [TestCase(false, 10, true)]
        public async Task ManagedBuildSelectionResolvesOnlyFixedLayouts(bool debug, int major, bool runtime)
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                RepositorySampleFramework framework = major switch
                {
                    8 => RepositorySampleFramework.Net8,
                    9 => RepositorySampleFramework.Net9,
                    10 => RepositorySampleFramework.Net10,
                    _ => throw new ArgumentOutOfRangeException(nameof(major))
                };
                RepositorySampleSource source = context.Source with
                {
                    Configuration = debug
                        ? RepositorySampleBuildConfiguration.Debug
                        : RepositorySampleBuildConfiguration.Release,
                    Framework = framework,
                    Layout = runtime
                        ? RepositorySampleBuildLayout.CurrentRuntimeDirectory
                        : RepositorySampleBuildLayout.FrameworkDirectory
                };
                string expected = Path.Combine(
                    context.Source.Root, "samples", "Reference", "ConsoleReferenceServer", "bin",
                    debug ? "Debug" : "Release", major == 8 ? "net8.0" : major == 9 ? "net9.0" : "net10.0");
                if (runtime)
                {
                    expected = Path.Combine(expected, RuntimeInformation.RuntimeIdentifier);
                }

                Assert.That(RepositorySampleCatalog.GetBuildDirectory(
                    source, RepositorySampleId.ConsoleReferenceServer), Is.EqualTo(expected));
                Assert.That(Directory.Exists(context.RunParent), Is.False);
            }
        }

        [Test]
        public async Task UninitializedAndCleanedRunDirectoriesCannotBeLaunched()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                RepositorySampleRunFiles uninitialized = context.Files.AllocateRun();
                await Assert.ThatAsync(
                    () => context.Files.PrepareAsync(
                        context.Source, RepositorySampleId.ConsoleReferenceServer, 58123,
                        RepositorySampleTestContext.Options, uninitialized, CancellationToken.None),
                    Throws.InvalidOperationException).ConfigureAwait(false);
                Assert.That(Directory.Exists(context.RunParent), Is.False);

                RepositorySampleLaunch launch = await context.PrepareAsync(
                    RepositorySampleId.ConsoleReferenceServer).ConfigureAwait(false);
                await launch.Files.CleanupAsync(CancellationToken.None).ConfigureAwait(false);
                Assert.That(() => RepositorySampleRuntime.CreateStartInfo(launch), Throws.InvalidOperationException);
                Assert.That(Directory.Exists(launch.Files.Root), Is.False);
                context.Runtime.VerifyNoOtherCalls();
            }
        }

        [Test]
        public async Task MissingSourceRequiresConfiguration()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                Assert.That(context.Service.Snapshot.Phase, Is.EqualTo(RepositorySamplePhase.RequiresConfiguration));
                RepositorySampleSnapshot result = await context.Service.ConfigureAsync(
                    RepositorySampleId.ConsoleReferenceServer,
                    context.Source with { Root = Path.Combine(context.Root, "absent") }).ConfigureAwait(false);

                Assert.That(result.Phase, Is.EqualTo(RepositorySamplePhase.RequiresConfiguration));
                Assert.That(result.Message, Does.StartWith("Requires configuration"));
                Assert.That(Directory.Exists(context.RunParent), Is.False);
                await Assert.ThatAsync(
                    () => context.Service.StartAsync(RepositorySampleTestContext.Options),
                    Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);
                context.Runtime.VerifyNoOtherCalls();
                context.Ports.VerifyNoOtherCalls();
                context.Probe.VerifyNoOtherCalls();
            }
        }

        [TestCase("apphost")]
        [TestCase("dll")]
        [TestCase("deps.json")]
        [TestCase("runtimeconfig.json")]
        public async Task MissingBuiltArtifactsPreventsStart(string artifact)
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                string suffix = artifact == "apphost"
                    ? OperatingSystem.IsWindows() ? ".exe" : string.Empty
                    : "." + artifact;
                string path = Path.Combine(
                    RepositorySampleCatalog.GetBuildDirectory(
                        context.Source, RepositorySampleId.ConsoleReferenceServer),
                    "ConsoleReferenceServer" + suffix);
                File.Delete(path);
                await context.ConfigureAsync().ConfigureAwait(false);

                Assert.That(context.Service.Snapshot.Phase, Is.EqualTo(RepositorySamplePhase.RequiresConfiguration));
                Assert.That(context.Service.Snapshot.Message, Does.Contain(Path.GetFileName(path)));
                await Assert.ThatAsync(
                    () => context.Service.StartAsync(RepositorySampleTestContext.Options),
                    Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);
                Assert.That(Directory.Exists(context.RunParent), Is.False);
                context.Runtime.VerifyNoOtherCalls();
                context.Ports.VerifyNoOtherCalls();
            }
        }

        [Test]
        public async Task BuildRemovedAfterConfigurationIsCheckedAgainBeforeReservation()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                await context.ConfigureAsync().ConfigureAwait(false);
                File.Delete(Path.Combine(
                    RepositorySampleCatalog.GetBuildDirectory(
                        context.Source, RepositorySampleId.ConsoleReferenceServer),
                    "ConsoleReferenceServer.dll"));

                await Assert.ThatAsync(
                    () => context.Service.StartAsync(RepositorySampleTestContext.Options),
                    Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);
                Assert.That(context.Service.Snapshot.Phase, Is.EqualTo(RepositorySamplePhase.RequiresConfiguration));
                context.Runtime.VerifyNoOtherCalls();
                context.Ports.VerifyNoOtherCalls();
            }
        }

        [Test]
        public async Task CatalogRejectsUnknownSamplesAndBuilds()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                Assert.That(RepositorySampleCatalog.Entries.Count, Is.EqualTo(2));
                Assert.That(() => RepositorySampleCatalog.Get((RepositorySampleId)99),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => RepositorySampleCatalog.GetBuildDirectory(
                    context.Source with { Configuration = (RepositorySampleBuildConfiguration)99 },
                    RepositorySampleId.ConsoleReferenceServer), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => RepositorySampleCatalog.GetBuildDirectory(
                    context.Source with { Framework = (RepositorySampleFramework)99 },
                    RepositorySampleId.ConsoleReferenceServer), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => RepositorySampleCatalog.GetBuildDirectory(
                    context.Source with { Layout = (RepositorySampleBuildLayout)99 },
                    RepositorySampleId.ConsoleReferenceServer), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(Directory.Exists(context.RunParent), Is.False);
            }
        }

        [Test]
        public async Task PathsRejectEscapesAndReparsePoints()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                Assert.That(() => RepositorySamplePaths.ValidateRoot("relative"), Throws.ArgumentException);
                Assert.That(() => RepositorySamplePaths.ValidateRoot(Path.GetPathRoot(context.Root)!),
                    Throws.ArgumentException);
                Assert.That(() => RepositorySamplePaths.ResolveChild(context.Source.Root, context.Root),
                    Throws.ArgumentException);
                Assert.That(() => RepositorySamplePaths.ResolveChild(
                    context.Source.Root, Path.Combine("..", "source-other", "sample.exe")), Throws.ArgumentException);
                Assert.That(() => RepositorySamplePaths.ResolveChild(context.Source.Root, "."),
                    Throws.ArgumentException);
                Assert.That(() => RepositorySamplePaths.RejectReparsePoint(
                    FileAttributes.Directory | FileAttributes.ReparsePoint), Throws.TypeOf<IOException>());
                Assert.That(() => RepositorySamplePaths.RejectReparsePoint(FileAttributes.Directory), Throws.Nothing);
                Assert.That(RepositorySamplePaths.ResolveChild(context.Source.Root, Path.Combine("a", "b")),
                    Is.EqualTo(Path.Combine(context.Source.Root, "a", "b")));
            }
        }

        [Test]
        public async Task ReferenceLaunchUsesWorkingDirectoryConfiguration()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                string before = await File.ReadAllTextAsync(context.ReferenceTemplatePath()).ConfigureAwait(false);
                RepositorySampleLaunch launch = await context.PrepareAsync(
                    RepositorySampleId.ConsoleReferenceServer).ConfigureAwait(false);
                ProcessStartInfo start = RepositorySampleRuntime.CreateStartInfo(launch);
                string text = await File.ReadAllTextAsync(
                    Path.Combine(launch.Files.Root, "Quickstarts.ReferenceServer.Config.xml")).ConfigureAwait(false);
                var document = XDocument.Parse(text);
                XNamespace ns = "http://opcfoundation.org/UA/SDK/Configuration.xsd";
                XNamespace ua = "http://opcfoundation.org/UA/2008/02/Types.xsd";
                XElement server = document.Root!.Element(ns + "ServerConfiguration")!;
                ArrayOf<string> expectedArguments = ["--timeout", "10", "--console", "--log"];

                Assert.That(start.WorkingDirectory, Is.EqualTo(launch.Files.Root));
                Assert.That(start.ArgumentList, Is.EqualTo(expectedArguments.ToArray()));
                Assert.That(document.Root.Element(ns + "ApplicationName")!.Value,
                    Is.EqualTo("ConsoleReferenceServer"));
                Assert.That(document.Root.Element(ns + "ApplicationUri")!.Value,
                    Is.EqualTo("urn:ualens-repository-sample:" + launch.Files.RunId.ToString("N")));
                Assert.That(server.Element(ns + "BaseAddresses")!.Elements().Single().Value,
                    Is.EqualTo("opc.tcp://127.0.0.1:58123/Quickstarts/ReferenceServer"));
                Assert.That(server.Descendants(ns + "SecurityMode").Single().Value, Is.EqualTo("SignAndEncrypt_3"));
                Assert.That(server.Descendants(ns + "SecurityPolicyUri").Single().Value,
                    Is.EqualTo(SecurityPolicies.Basic256Sha256));
                Assert.That(server.Descendants(ua + "TokenType").Single().Value, Is.EqualTo("Anonymous_0"));
                Assert.That(document.Descendants(ns + "AutoAcceptUntrustedCertificates").Single().Value,
                    Is.EqualTo("false"));
                Assert.That(document.Descendants(ns + "StorePath").Count(), Is.EqualTo(6));
                string pkiPrefix = launch.Files.PkiRoot + Path.DirectorySeparatorChar;
                Assert.That(document.Descendants(ns + "StorePath").All(value =>
                    value.Value.StartsWith(pkiPrefix, StringComparison.Ordinal)),
                    Is.True);
                Assert.That(server.Element(ns + "AlternateBaseAddresses"), Is.Null);
                Assert.That(server.Element(ns + "ReverseConnect"), Is.Null);
                Assert.That(server.Element(ns + "RegistrationEndpoint"), Is.Null);
                Assert.That(document.Root.Element(ns + "Extensions"), Is.Null);
                Assert.That(server.Element(ns + "DurableSubscriptionsEnabled")!.Value, Is.EqualTo("false"));
                Assert.That(server.Element(ns + "MultiCastDnsEnabled")!.Value, Is.EqualTo("false"));
                Assert.That(server.Element(ns + "NodeManagerSaveFile")!.Value,
                    Is.EqualTo(Path.Combine(launch.Files.Root, "server.nodes.xml")));
                Assert.That(text, Does.Not.Contain("HOST-"));
                Assert.That(await File.ReadAllTextAsync(context.ReferenceTemplatePath()).ConfigureAwait(false),
                    Is.EqualTo(before));
                context.Runtime.VerifyNoOtherCalls();
            }
        }

        [Test]
        public async Task PumpLaunchOptsIntoSimulatorWithoutAutoTrust()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                RepositorySampleLaunch launch = await context.PrepareAsync(
                    RepositorySampleId.PumpSoftwareUpdateSimulator).ConfigureAwait(false);
                ArrayOf<string> expected =
                [
                    "--host", "127.0.0.1", "--port", "58123", "--pumps", "2",
                    "--pki-root", Path.Combine(launch.Files.Root, "pki"),
                    "--autoaccept", "false", "--software-update-demo", "true", "--run-seconds", "10"
                ];

                Assert.That(launch.Arguments.ToArray(), Is.EqualTo(expected.ToArray()));
                Assert.That(launch.Endpoint.AbsoluteUri,
                    Is.EqualTo("opc.tcp://127.0.0.1:58123/PumpDeviceIntegrationServer"));
                Assert.That(launch.Descriptor.ProductUri,
                    Is.EqualTo("uri:opcfoundation.org:PumpDeviceIntegrationServer"));
                Assert.That(File.Exists(Path.Combine(launch.Files.Root, "Quickstarts.ReferenceServer.Config.xml")),
                    Is.False);
                Assert.That(launch.Executable, Does.Not.Contain(Path.DirectorySeparatorChar + "publish"));
                context.Runtime.VerifyNoOtherCalls();
            }
        }

        [Test]
        public async Task LaunchEnvironmentDoesNotInheritConfigurationOrSecrets()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                RepositorySampleLaunch launch = await context.PrepareAsync(
                    RepositorySampleId.ConsoleReferenceServer).ConfigureAwait(false);
                ProcessStartInfo start = RepositorySampleRuntime.CreateStartInfo(launch);
                ArrayOf<string> allowed =
                [
                    "SystemRoot", "WINDIR", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64",
                    "HOME", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "XDG_CONFIG_HOME", "XDG_DATA_HOME",
                    "TEMP", "TMP", "TMPDIR", "DOTNET_ENVIRONMENT", "DOTNET_CONTENTROOT",
                    "DOTNET_EnableDiagnostics", "DOTNET_DbgEnableMiniDump", "COMPlus_DbgEnableMiniDump"
                ];

                Assert.That(start.Environment.Keys.Except(allowed.ToArray()!), Is.Empty);
                Assert.That(start.Environment["TEMP"], Is.EqualTo(Path.Combine(launch.Files.Root, "temp")));
                Assert.That(start.Environment["TMPDIR"], Is.EqualTo(Path.Combine(launch.Files.Root, "temp")));
                Assert.That(start.Environment["HOME"], Is.EqualTo(Path.Combine(launch.Files.Root, "profile")));
                Assert.That(start.Environment["DOTNET_ENVIRONMENT"], Is.EqualTo("Production"));
                Assert.That(start.Environment["DOTNET_CONTENTROOT"], Is.EqualTo(launch.Files.Root));
                Assert.That(start.Environment["DOTNET_EnableDiagnostics"], Is.EqualTo("0"));
                Assert.That(start.UseShellExecute, Is.False);
                Assert.That(start.RedirectStandardOutput, Is.True);
                Assert.That(start.RedirectStandardError, Is.True);
                Assert.That(start.RedirectStandardInput, Is.True);
                Assert.That(start.FileName, Is.EqualTo(launch.Executable));
            }
        }

        [Test]
        public async Task ForgedLaunchCannotEscapeTheAllowlistedManagedBuild()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                RepositorySampleLaunch valid = await context.PrepareAsync(
                    RepositorySampleId.ConsoleReferenceServer).ConfigureAwait(false);
                var forged = new RepositorySampleLaunch(
                    valid.Descriptor, valid.Source, Path.Combine(context.Root, "other.exe"),
                    valid.Endpoint, valid.ApplicationUris, valid.Arguments, valid.Files);

                Assert.That(() => RepositorySampleRuntime.CreateStartInfo(forged), Throws.ArgumentException);
                context.Runtime.VerifyNoOtherCalls();
            }
        }

        [Test]
        public async Task RunAllocationDoesNotCreateDirectoriesAndOccupiedRootsRemainUntouched()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                RepositorySampleRunFiles files = context.Files.AllocateRun();
                Assert.That(Directory.Exists(context.RunParent), Is.False);
                Directory.CreateDirectory(files.Root);
                string sentinel = Path.Combine(files.Root, "unowned.txt");
                await File.WriteAllTextAsync(sentinel, "preserve").ConfigureAwait(false);

                await Assert.ThatAsync(
                    () => files.InitializeAsync(CancellationToken.None), Throws.TypeOf<IOException>())
                    .ConfigureAwait(false);
                await files.CleanupAsync(CancellationToken.None).ConfigureAwait(false);
                Assert.That(await File.ReadAllTextAsync(sentinel).ConfigureAwait(false), Is.EqualTo("preserve"));
                Assert.That(File.Exists(Path.Combine(files.Root, RepositorySampleRunFiles.MarkerName)), Is.False);
            }
        }

        [Test]
        public async Task MalformedConfigurationCleansTrackedPreparationWithoutStarting()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                await File.WriteAllTextAsync(context.ReferenceTemplatePath(), "<wrong />").ConfigureAwait(false);
                await context.ConfigureAsync().ConfigureAwait(false);

                await Assert.ThatAsync(
                    () => context.Service.StartAsync(RepositorySampleTestContext.Options)
                        .WaitAsync(RepositorySampleTestContext.Bound),
                    Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);
                Assert.That(context.Service.Snapshot.Phase, Is.EqualTo(RepositorySamplePhase.Failed));
                Assert.That(context.Service.Snapshot.OwnsResources, Is.False);
                Assert.That(Directory.EnumerateFileSystemEntries(context.RunParent), Is.Empty);
                Assert.That(context.Port.DisposeCount, Is.EqualTo(1));
                context.Runtime.VerifyNoOtherCalls();
            }
        }

        [Test]
        public async Task RestoreClearsSourceTrustWithoutLaunching()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                await context.ConfigureAsync().ConfigureAwait(false);
                await context.Service.RestoreSelectionAsync(RepositorySampleId.PumpSoftwareUpdateSimulator)
                    .ConfigureAwait(false);

                Assert.That(context.Service.Snapshot.Selection,
                    Is.EqualTo(RepositorySampleId.PumpSoftwareUpdateSimulator));
                Assert.That(context.Service.Snapshot.Phase, Is.EqualTo(RepositorySamplePhase.RequiresConfiguration));
                await Assert.ThatAsync(
                    () => context.Service.StartAsync(RepositorySampleTestContext.Options),
                    Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);
                Assert.That(Directory.Exists(context.RunParent), Is.False);
                context.Runtime.VerifyNoOtherCalls();
                context.Ports.VerifyNoOtherCalls();
                context.Probe.VerifyNoOtherCalls();
            }
        }

        [Test]
        public async Task RegistrationIsInjectableIdempotentAndDoesNotLaunch()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                var services = new ServiceCollection();
                services.AddSingleton(new Mock<ITelemetryContext>(MockBehavior.Strict).Object);
                services.AddSingleton(context.Files);
                services.AddSingleton(context.Clock.Provider);
                services.AddSingleton(context.Ports.Object);
                services.AddSingleton(context.Runtime.Object);
                services.AddSingleton(context.Probe.Object);
                services.AddUaLensRepositorySamples();
                services.AddUaLensRepositorySamples();
                ServiceProvider provider = services.BuildServiceProvider();
                await using (provider.ConfigureAwait(false))
                {
                    IRepositorySampleService owner = provider.GetRequiredService<IRepositorySampleService>();
                    Assert.That(provider.GetRequiredService<IRepositorySampleService>(), Is.SameAs(owner));
                    Assert.That(provider.GetRequiredService<IRepositorySampleRuntime>(),
                        Is.SameAs(context.Runtime.Object));
                    Assert.That(owner.Snapshot.Phase, Is.EqualTo(RepositorySamplePhase.RequiresConfiguration));
                    Assert.That(Directory.Exists(context.RunParent), Is.False);
                    context.Runtime.VerifyNoOtherCalls();
                    context.Ports.VerifyNoOtherCalls();
                    context.Probe.VerifyNoOtherCalls();
                }
            }
        }

        [TestCase(0)]
        [TestCase(301)]
        public void RunDurationOutsideTheModuleBoundIsRejected(int seconds)
        {
            Assert.That(() => (RepositorySampleTestContext.Options with { RunSeconds = seconds }).Validate(),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(1)]
        [TestCase(300)]
        public void RunDurationAtBothBoundsIsAccepted(int seconds)
        {
            Assert.That(() => (RepositorySampleTestContext.Options with { RunSeconds = seconds }).Validate(),
                Throws.Nothing);
        }

        [TestCase("startup", 0, false)]
        [TestCase("startup", 60000, true)]
        [TestCase("startup", 60001, false)]
        [TestCase("shutdown", 0, false)]
        [TestCase("shutdown", 30000, true)]
        [TestCase("shutdown", 30001, false)]
        [TestCase("termination", 0, false)]
        [TestCase("termination", 30000, true)]
        [TestCase("termination", 30001, false)]
        [TestCase("probe", -1, false)]
        [TestCase("probe", 0, false)]
        [TestCase("probe", 1, true)]
        [TestCase("probe", 5000, true)]
        [TestCase("probe", 5001, false)]
        public void TimeBudgetBoundsAreEnforced(string budget, int milliseconds, bool accepted)
        {
            var value = TimeSpan.FromMilliseconds(milliseconds);
            RepositorySampleRunOptions options = budget switch
            {
                "startup" => RepositorySampleTestContext.Options with { StartupTimeout = value },
                "shutdown" => RepositorySampleTestContext.Options with { ShutdownAllowance = value },
                "termination" => RepositorySampleTestContext.Options with { TerminationTimeout = value },
                "probe" => RepositorySampleTestContext.Options with { ProbeInterval = value },
                _ => throw new ArgumentOutOfRangeException(nameof(budget))
            };
            if (accepted)
            {
                Assert.That(options.Validate, Throws.Nothing);
            }
            else
            {
                Assert.That(options.Validate, Throws.TypeOf<ArgumentOutOfRangeException>());
            }
        }
    }
}
