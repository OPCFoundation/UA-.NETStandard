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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests.Samples
{
    [TestFixture]
    public sealed class SamplePubSubHostPolicyTests
    {
        [Test]
        public async Task UnknownOptionFailsWithoutStartingHostAsync()
        {
            (int exitCode, string output, string error) = await RunAsync(
                ["external", "--unknown"]).ConfigureAwait(false);

            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(error, Does.Contain("--unknown"));
            Assert.That(output, Does.Not.Contain("Bridge started"));
        }

        [Test]
        public async Task OmittedSecurityConsentKeepsEveryBridgeDirectionSecureAsync()
        {
            (int exitCode, string output, string error) = await RunAsync(
                ["external", "--mode", "publisher,subscriber,responder", "--validate-configuration"])
                .ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, error);
            Assert.That(output, Does.Contain("Publisher: SecurityMode=SignAndEncrypt"));
            Assert.That(output, Does.Contain("Subscriber: SecurityMode=SignAndEncrypt"));
            Assert.That(output, Does.Contain("Responder: SecurityMode=SignAndEncrypt"));
            Assert.That(output, Does.Contain("AllowUnsecuredActions=False"));
            Assert.That(output, Does.Not.Contain("warn:"));
        }

        [Test]
        public async Task ExplicitNoneWarnsBeforeApplyingAndDoesNotPermitUnsecuredActionsAsync()
        {
            (int exitCode, string output, string error) = await RunAsync(
                ["external", "--mode", "publisher,subscriber,responder", "--validate-configuration", "--security-none"])
                .ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, error);
            Assert.That(output, Does.Contain("Publisher: SecurityMode=None"));
            Assert.That(output, Does.Contain("Subscriber: SecurityMode=None"));
            Assert.That(output, Does.Contain("Responder: SecurityMode=None"));
            Assert.That(output, Does.Contain("AllowUnsecuredActions=False"));
            Assert.That(output, Does.Contain("not signed or encrypted"));
            Assert.That(
                output.IndexOf("not signed or encrypted", StringComparison.Ordinal),
                Is.LessThan(output.IndexOf("Publisher: SecurityMode=None", StringComparison.Ordinal)));
        }

        [Test]
        public async Task UnsecuredActionsRequireIndependentConsentAndWarnAsync()
        {
            (int exitCode, string output, string error) = await RunAsync(
                ["external", "--mode", "responder", "--validate-configuration", "--allow-unsecured-actions"])
                .ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, error);
            Assert.That(output, Does.Contain("Responder: SecurityMode=SignAndEncrypt"));
            Assert.That(output, Does.Contain("AllowUnsecuredActions=True"));
            Assert.That(output, Does.Contain("unauthenticated PubSub actions"));
            Assert.That(output, Does.Not.Contain("not signed or encrypted"));
        }

        [Test]
        public async Task HostSuppliesTrustedCertificateConfigurationInsteadOfAdapterFallbackAsync()
        {
            (int exitCode, string output, string error) = await RunAsync(
                ["external", "--mode", "publisher,subscriber,responder", "--validate-configuration"])
                .ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, error);
            Assert.That(output, Does.Contain("AutoAcceptUntrustedCertificates=False"));
            Assert.That(output, Does.Not.Contain("AutoAcceptUntrustedCertificates=True"));
        }

        [Test]
        public async Task HotReloadConfigurationCannotImplicitlyEnableLegacyInsecureOptionsAsync()
        {
            (int exitCode, string output, string error) = await RunAsync(
                ["external", "--mode", "publisher,subscriber,responder", "--validate-configuration", "--hot-reload"],
                                     /*lang=json,strict*/
                                     """
                {
                  "ExternalPublisher": { "Connection": { "SecurityMode": "None" } },
                  "ExternalSubscriber": { "Connection": { "SecurityMode": "None" } },
                  "ExternalResponder": { "Connection": { "SecurityMode": "None" }, "AllowUnsecured": true }
                }
                """).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, error);
            Assert.That(output, Does.Contain("Publisher: SecurityMode=SignAndEncrypt"));
            Assert.That(output, Does.Contain("Subscriber: SecurityMode=SignAndEncrypt"));
            Assert.That(output, Does.Contain("Responder: SecurityMode=SignAndEncrypt; AllowUnsecuredActions=False"));
            Assert.That(output, Does.Not.Contain("warn:"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConfigurationReloadAppliesAndRevokesBothExplicitConsentsAsync(bool initiallyUnsecured)
        {
            const string enabled =
                                     /*lang=json,strict*/
                                     """{ "ExternalBridge": { "UseSecurityNone": true, "AllowUnsecuredActions": true } }""";
            const string disabled =
                                     /*lang=json,strict*/
                                     """{ "ExternalBridge": { "UseSecurityNone": false, "AllowUnsecuredActions": false } }""";
            (_, string output, string error) = await RunAsync(
                [
                    "external", "--mode", "publisher,subscriber,responder", "--validate-configuration",
                    "--hot-reload", "--watch-configuration"
                ],
                initiallyUnsecured ? enabled : disabled,
                reloadSettings: initiallyUnsecured ? disabled : enabled)
                .ConfigureAwait(false);

            Assert.That(error, Is.Empty);
            Assert.That(output, Does.Contain("Responder: SecurityMode=None; AllowUnsecuredActions=True"));
            Assert.That(output, Does.Contain("Publisher: SecurityMode=SignAndEncrypt"));
            Assert.That(output, Does.Contain("Subscriber: SecurityMode=SignAndEncrypt"));
            Assert.That(output, Does.Contain("Responder: SecurityMode=SignAndEncrypt; AllowUnsecuredActions=False"));
            Assert.That(
                output.IndexOf("not signed or encrypted", StringComparison.Ordinal),
                Is.LessThan(output.IndexOf("Publisher: SecurityMode=None", StringComparison.Ordinal)));
            Assert.That(
                output.IndexOf("unauthenticated PubSub actions", StringComparison.Ordinal),
                Is.LessThan(output.IndexOf("Responder: SecurityMode=None; AllowUnsecuredActions=True",
                    StringComparison.Ordinal)));
        }

        [TestCase("--security-none=perhaps")]
        [TestCase("--allow-unsecured-actions=perhaps")]
        [TestCase("--unknown")]
        public async Task HelpDoesNotHideInvalidOptionsAsync(string argument)
        {
            (int exitCode, string output, string error) = await RunAsync(
                ["external", argument, "--help"]).ConfigureAwait(false);

            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(error, Is.Not.Empty);
            Assert.That(output, Does.Not.Contain("warn:").And.Not.Contain("Bridge started"));
        }

        [TestCase("--security-none=perhaps")]
        [TestCase("--security-none=")]
        [TestCase("--security-none=1")]
        [TestCase("--allow-unsecured-actions=perhaps")]
        [TestCase("--allow-unsecured-actions=")]
        [TestCase("--hot-reload=perhaps")]
        public async Task MalformedBooleanFailsWithoutConfigurationSideEffectsAsync(string argument)
        {
            (int exitCode, string output, string error) = await RunAsync(
                ["external", "--validate-configuration", argument]).ConfigureAwait(false);

            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(error, Is.Not.Empty);
            Assert.That(output, Does.Not.Contain("warn:").And.Not.Contain("SecurityMode="));
        }

        [TestCase("--help")]
        [TestCase("-h")]
        [TestCase("-?")]
        public async Task HelpWithRelaxationFlagsHasNoHostSideEffectsAsync(string help)
        {
            (int exitCode, string output, string error) = await RunAsync(
                ["external", "--security-none", "--allow-unsecured-actions", help]).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, error);
            Assert.That(output, Does.Contain("--security-none").And.Contain("--allow-unsecured-actions"));
            Assert.That(output, Does.Not.Contain("warn:").And.Not.Contain("Bridge started"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitFalseOverridesJsonAndEnvironmentAsync(bool hotReload)
        {
            string[] arguments = hotReload
                ? [
                    "external", "--mode", "publisher,subscriber,responder", "--validate-configuration",
                    "--hot-reload", "--security-none=false", "--allow-unsecured-actions", "false"
                ]
                : [
                    "external", "--mode", "publisher,subscriber,responder", "--validate-configuration",
                    "--security-none", "false", "--allow-unsecured-actions=false"
                ];
            (int exitCode, string output, string error) = await RunAsync(
                arguments,
                                     /*lang=json,strict*/
                                     """{ "ExternalBridge": { "UseSecurityNone": true, "AllowUnsecuredActions": true } }""",
                environment: new Dictionary<string, string?>
                {
                    ["ExternalBridge__UseSecurityNone"] = "true",
                    ["ExternalBridge__AllowUnsecuredActions"] = "true"
                }).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, error);
            Assert.That(output, Does.Contain("Publisher: SecurityMode=SignAndEncrypt"));
            Assert.That(output, Does.Contain("Subscriber: SecurityMode=SignAndEncrypt"));
            Assert.That(output, Does.Contain("Responder: SecurityMode=SignAndEncrypt; AllowUnsecuredActions=False"));
            Assert.That(output, Does.Not.Contain("warn:"));
        }

        [Test]
        public async Task ExplicitFalseRemainsAuthoritativeAfterJsonReloadAsync()
        {
            (_, string output, string error) = await RunAsync(
                [
                    "external", "--mode", "publisher,subscriber,responder", "--validate-configuration",
                    "--hot-reload", "--watch-configuration", "--security-none=false", "--allow-unsecured-actions=false"
                ],
                                     /*lang=json,strict*/
                                     """{ "ExternalBridge": { "UseSecurityNone": false, "AllowUnsecuredActions": false } }""",
                reloadSettings: /*lang=json,strict*/ """{ "ExternalBridge": { "UseSecurityNone": true, "AllowUnsecuredActions": true } }""",
                environment: new Dictionary<string, string?>
                {
                    ["ExternalBridge__UseSecurityNone"] = "true",
                    ["ExternalBridge__AllowUnsecuredActions"] = "true"
                }).ConfigureAwait(false);

            Assert.That(error, Is.Empty);
            Assert.That(output, Does.Contain("Publisher: SecurityMode=SignAndEncrypt"));
            Assert.That(output, Does.Contain("Subscriber: SecurityMode=SignAndEncrypt"));
            Assert.That(output, Does.Contain("Responder: SecurityMode=SignAndEncrypt; AllowUnsecuredActions=False"));
            Assert.That(output, Does.Not.Contain("SecurityMode=None").And.Not.Contain("AllowUnsecuredActions=True"));
            Assert.That(output, Does.Not.Contain("warn:"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EnvironmentConsentOverridesJsonWhenCliIsOmittedAsync(bool hotReload)
        {
            string[] arguments = hotReload
                ? ["external", "--mode", "responder", "--validate-configuration", "--hot-reload"]
                : ["external", "--mode", "responder", "--validate-configuration"];
            (int exitCode, string output, string error) = await RunAsync(
                arguments,
                                     /*lang=json,strict*/
                                     """{ "ExternalBridge": { "UseSecurityNone": false, "AllowUnsecuredActions": false } }""",
                environment: new Dictionary<string, string?>
                {
                    ["ExternalBridge__UseSecurityNone"] = "true",
                    ["ExternalBridge__AllowUnsecuredActions"] = "true"
                }).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, error);
            Assert.That(output, Does.Contain("Responder: SecurityMode=None; AllowUnsecuredActions=True"));
            Assert.That(output, Does.Contain("not signed or encrypted").And.Contain("unauthenticated PubSub actions"));
        }

        [TestCase("UseSecurityNone")]
        [TestCase("AllowUnsecuredActions")]
        public async Task InvalidHostConsentFailsClosedAsync(string key)
        {
            (int exitCode, string output, string error) = await RunAsync(
                ["external", "--mode", "responder", "--validate-configuration"],
                $$"""{ "ExternalBridge": { "{{key}}": "perhaps" } }""").ConfigureAwait(false);

            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(error, Does.Contain(key));
            Assert.That(output, Does.Not.Contain("Responder:"));
        }

        [TestCase("publisher", "--profile", "invalid")]
        [TestCase("subscriber", "--profile", "invalid")]
        [TestCase("external", "--mode", "invalid")]
        [TestCase("external", "--read-mode", "invalid")]
        [TestCase("external", "--affinity", "invalid")]
        public async Task ExistingValidationExitCodesRemainNonzeroAsync(string mode, string option, string value)
        {
            (int exitCode, string output, string error) = await RunAsync([mode, option, value])
                .ConfigureAwait(false);

            Assert.That(exitCode, Is.EqualTo(2), error);
            Assert.That(error, Does.Contain(value));
            Assert.That(output, Does.Not.Contain("started"));
        }

        [Test]
        public async Task WatchRequiresConfigurationOnlyHotReloadAsync()
        {
            (int exitCode, _, string error) = await RunAsync(["external", "--watch-configuration"])
                .ConfigureAwait(false);

            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(error, Does.Contain("--hot-reload").And.Contain("--validate-configuration"));
        }

        [Test]
        public async Task SuppliedHotReloadTemplateSelectsSecureHostOptionsAsync()
        {
            (int exitCode, string output, string error) = await RunAsync(
                ["external", "--mode", "publisher,subscriber,responder", "--hot-reload", "--validate-configuration"])
                .ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, error);
            Assert.That(output, Does.Contain("Publisher: SecurityMode=SignAndEncrypt"));
            Assert.That(output, Does.Contain("Subscriber: SecurityMode=SignAndEncrypt"));
            Assert.That(output, Does.Contain("Responder: SecurityMode=SignAndEncrypt; AllowUnsecuredActions=False"));
            Assert.That(output, Does.Not.Contain("warn:"));
        }

        private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
            string[] arguments,
            string? settings = null,
            string? reloadSettings = null,
            Dictionary<string, string?>? environment = null)
        {
            DirectoryInfo? root = new(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "UA.slnx")))
            {
                root = root.Parent;
            }
            Assert.That(root, Is.Not.Null, "Run these tests from a repository build.");
            string sampleDirectory = Path.Combine(
                root!.FullName, "samples", "PubSub", "ConsoleReferencePubSubClient", "bin",
#if DEBUG
                "Debug",
#else
                "Release",
#endif
                "net10.0");
            string assembly = Path.Combine(sampleDirectory, "ConsoleReferencePubSubClient.dll");
            Assert.That(File.Exists(assembly), Is.True,
                "The referenced PubSub sample must be built before its host policy tests.");
            string workingDirectory = Directory.CreateTempSubdirectory("opcua-pubsub-host-").FullName;
            try
            {
                if (Array.IndexOf(arguments, "--hot-reload") >= 0)
                {
                    foreach (string file in Directory.EnumerateFiles(sampleDirectory))
                    {
                        if (file.EndsWith(".dll", StringComparison.Ordinal) ||
                            file.EndsWith(".deps.json", StringComparison.Ordinal) ||
                            file.EndsWith(".runtimeconfig.json", StringComparison.Ordinal))
                        {
                            File.Copy(file, Path.Combine(workingDirectory, Path.GetFileName(file)));
                        }
                    }
                    assembly = Path.Combine(workingDirectory, "ConsoleReferencePubSubClient.dll");
                    if (settings is null)
                    {
                        File.Copy(
                            Path.Combine(sampleDirectory, "appsettings.json"),
                            Path.Combine(workingDirectory, "appsettings.json"));
                    }
                }
                if (settings is not null)
                {
                    await File.WriteAllTextAsync(
                        Path.Combine(workingDirectory, "appsettings.json"), settings).ConfigureAwait(false);
                }
                string[] expectedFiles = Directory.GetFileSystemEntries(workingDirectory);
                var startInfo = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add(assembly);
                foreach (string argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }
                startInfo.Environment.Remove("ExternalBridge__UseSecurityNone");
                startInfo.Environment.Remove("ExternalBridge__AllowUnsecuredActions");
                startInfo.Environment["DOTNET_PROCESSOR_COUNT"] = "2";
                if (environment is not null)
                {
                    foreach ((string key, string? value) in environment)
                    {
                        startInfo.Environment[key] = value;
                    }
                }
                using var process = new Process { StartInfo = startInfo };
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                Assert.That(process.Start(), Is.True);
                Task<string> output = ReadOutputAsync(
                    process.StandardOutput, workingDirectory, reloadSettings, timeout.Token);
                Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
                try
                {
                    string capturedOutput = await output.ConfigureAwait(false);
                    if (reloadSettings is not null && !process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(
                        Directory.GetFileSystemEntries(workingDirectory),
                        Has.Length.EqualTo(expectedFiles.Length),
                        "Configuration validation must not create XML configuration or PKI files.");
                    return (
                        process.ExitCode,
                        capturedOutput,
                        await error.ConfigureAwait(false));
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    await Task.WhenAll(output, error).ConfigureAwait(false);
                }
            }
            finally
            {
                await DeleteWorkingDirectoryAsync(workingDirectory).ConfigureAwait(false);
            }
        }

        private static async Task DeleteWorkingDirectoryAsync(string path)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(path, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 9)
                {
                    TestContext.Out.WriteLine("Retrying cleanup of the exited sample's workspace.");
                }
                catch (UnauthorizedAccessException) when (attempt < 9)
                {
                    TestContext.Out.WriteLine("Retrying cleanup of the exited sample's workspace.");
                }
                // Only cleanup is retried, after process disposal; assertions and execution are not retried.
                await Task.Delay(250).ConfigureAwait(false);
            }
        }

        private static async Task<string> ReadOutputAsync(
            StreamReader reader,
            string workingDirectory,
            string? settings,
            CancellationToken cancellationToken)
        {
            if (settings is null)
            {
                return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            var output = new StringBuilder();
            bool reloading = false;
            var directions = new HashSet<string>(StringComparer.Ordinal);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                is string line)
            {
                output.AppendLine(line);
                if (!reloading && line.Contains("Watching adapter configuration", StringComparison.Ordinal))
                {
                    reloading = true;
                    await File.WriteAllTextAsync(
                        Path.Combine(workingDirectory, "appsettings.json"), settings, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (reloading)
                {
                    foreach (string direction in new[] { "Publisher:", "Subscriber:", "Responder:" })
                    {
                        if (line.Contains(direction, StringComparison.Ordinal))
                        {
                            directions.Add(direction);
                        }
                    }
                    if (directions.Count == 3)
                    {
                        break;
                    }
                }
            }
            Assert.That(reloading, Is.True, output.ToString());
            Assert.That(directions, Has.Count.EqualTo(3), output.ToString());
            return output.ToString();
        }
    }
}
#endif
