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

#if NET10_0_OR_GREATER
using System;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Connector;

namespace Opc.Ua.Tools.Tests.XRegistryConnector
{
    [TestFixture]
    [Category("XRegistryConnector")]
    public sealed class XRegistryConnectorCommandLineTests
    {
        [Test]
        public void CreateRejectsMissingExecutor()
        {
            Assert.That(() => XRegistryConnectorCommandLine.Create(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("execute"));
        }

        [Test]
        public async Task HttpGatewayDispatchesValidatedNativeEndpointListenerPublicRootAndProfileAsync()
        {
            InvocationResult result = await InvokeAsync(k_httpGateway + " --profile operator-a").ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(37));
            Assert.That(result.Executions, Is.EqualTo(1));
            Assert.That(result.Settings, Is.Not.Null);
            Assert.That(result.Settings!.Command, Is.EqualTo(XRegistryConnectorCommand.HttpGateway));
            Assert.That(result.Settings.OpcUaEndpoint!.AbsoluteUri, Is.EqualTo("opc.tcp://localhost:4840/"));
            Assert.That(result.Settings.RegistryNodeId, Is.EqualTo("ns=2;s=Registry"));
            Assert.That(result.Settings.ListenAddress!.AbsoluteUri, Is.EqualTo("https://localhost:8443/"));
            Assert.That(result.Settings.PublicHttpRoot!.AbsoluteUri, Is.EqualTo("https://bridge.example/xregistry/"));
            Assert.That(result.Settings.CredentialProfile, Is.EqualTo("operator-a"));
            Assert.That(result.Settings.HttpRoot, Is.Null);
            Assert.That(result.Settings.StateDirectory, Is.Null);
            Assert.That(result.ValidationError, Is.Empty);
            Assert.That(result.ParserError, Is.Empty);
        }

        [Test]
        public async Task OpcUaGatewayDispatchesHttpUpstreamAndNativeListenerAsync()
        {
            InvocationResult result = await InvokeAsync(k_opcUaGateway).ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(37));
            Assert.That(result.Executions, Is.EqualTo(1));
            Assert.That(result.Settings, Is.Not.Null);
            Assert.That(result.Settings!.Command, Is.EqualTo(XRegistryConnectorCommand.OpcUaGateway));
            Assert.That(result.Settings.HttpRoot!.AbsoluteUri, Is.EqualTo("https://registry.example/root/"));
            Assert.That(result.Settings.ListenAddress!.AbsoluteUri, Is.EqualTo("opc.tcp://localhost:4841/"));
            Assert.That(result.Settings.OpcUaEndpoint, Is.Null);
            Assert.That(result.Settings.PublicHttpRoot, Is.Null);
            Assert.That(result.Settings.CredentialProfile, Is.EqualTo("default"));
            Assert.That(result.ValidationError, Is.Empty);
            Assert.That(result.ParserError, Is.Empty);
        }

        [TestCase("", false, false)]
        [TestCase(" --dry-run", true, false)]
        [TestCase(" --once", false, true)]
        [TestCase(" --dry-run --once", true, true)]
        public async Task SyncFlagsRetainManualGuardedDefaultsAndBothEndpointSettingsAsync(
            string flags,
            bool dryRun,
            bool once)
        {
            InvocationResult result = await InvokeAsync(k_sync + flags).ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(37));
            Assert.That(result.Executions, Is.EqualTo(1));
            Assert.That(result.Settings, Is.Not.Null);
            Assert.That(result.Settings!.Command, Is.EqualTo(XRegistryConnectorCommand.Sync));
            Assert.That(result.Settings.OpcUaEndpoint!.AbsoluteUri, Is.EqualTo("opc.tcp://localhost:4840/"));
            Assert.That(result.Settings.RegistryNodeId, Is.EqualTo("ns=2;s=Registry"));
            Assert.That(result.Settings.HttpRoot!.AbsoluteUri, Is.EqualTo("https://registry.example/root/"));
            Assert.That(result.Settings.StateDirectory, Is.EqualTo("xregistry-unit-state"));
            Assert.That(result.Settings.JobId, Is.EqualTo("xregistry"));
            Assert.That(result.Settings.ConflictPolicy, Is.EqualTo("manual"));
            Assert.That(result.Settings.PropagateDeletes, Is.True);
            Assert.That(result.Settings.DryRun, Is.EqualTo(dryRun));
            Assert.That(result.Settings.Once, Is.EqualTo(once));
            Assert.That(result.Settings.PollInterval, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(result.Settings.AllowLoopbackHttp, Is.False);
            Assert.That(result.Settings.ListenAddress, Is.Null);
            Assert.That(result.ValidationError, Is.Empty);
            Assert.That(result.ParserError, Is.Empty);
        }

        [TestCase("manual", "on", true)]
        [TestCase("prefer-opcua", "off", false)]
        [TestCase("prefer-http", "on", true)]
        public async Task SyncDispatchesExplicitPolicyDeleteGuardProfileJobAndPollingIntervalAsync(
            string policy,
            string deletes,
            bool propagate)
        {
            InvocationResult result = await InvokeAsync(k_sync + " --conflict-policy " + policy +
                " --deletes " + deletes + " --profile operator-a --job job-a --poll-interval 00:00:17")
                .ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(37));
            Assert.That(result.Executions, Is.EqualTo(1));
            Assert.That(result.Settings, Is.Not.Null);
            Assert.That(result.Settings!.ConflictPolicy, Is.EqualTo(policy));
            Assert.That(result.Settings.PropagateDeletes, Is.EqualTo(propagate));
            Assert.That(result.Settings.CredentialProfile, Is.EqualTo("operator-a"));
            Assert.That(result.Settings.JobId, Is.EqualTo("job-a"));
            Assert.That(result.Settings.PollInterval, Is.EqualTo(TimeSpan.FromSeconds(17)));
            Assert.That(result.ValidationError, Is.Empty);
            Assert.That(result.ParserError, Is.Empty);
        }

        [TestCase("inspect --http-root https://registry.example/root/", false, true)]
        [TestCase("inspect --opcua opc.tcp://localhost:4840 --registry-node ns=2;s=Registry", true, false)]
        [TestCase("inspect --opcua opc.tcp://localhost:4840 --registry-node ns=2;s=Registry " +
            "--http-root https://registry.example/root/", true, true)]
        public async Task InspectAcceptsHttpNativeOrBothWithoutListenerOrStateAsync(
            string arguments,
            bool native,
            bool http)
        {
            InvocationResult result = await InvokeAsync(arguments).ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(37));
            Assert.That(result.Executions, Is.EqualTo(1));
            Assert.That(result.Settings, Is.Not.Null);
            Assert.That(result.Settings!.Command, Is.EqualTo(XRegistryConnectorCommand.Inspect));
            Assert.That(result.Settings.OpcUaEndpoint?.AbsoluteUri,
                Is.EqualTo(native ? "opc.tcp://localhost:4840/" : null));
            Assert.That(result.Settings.RegistryNodeId, Is.EqualTo(native ? "ns=2;s=Registry" : null));
            Assert.That(result.Settings.HttpRoot?.AbsoluteUri,
                Is.EqualTo(http ? "https://registry.example/root/" : null));
            Assert.That(result.Settings.ListenAddress, Is.Null);
            Assert.That(result.Settings.StateDirectory, Is.Null);
            Assert.That(result.ValidationError, Is.Empty);
            Assert.That(result.ParserError, Is.Empty);
        }

        [Test]
        public async Task ConflictsDispatchesOnlyPersistedStateAndJobWithoutEndpointsAsync()
        {
            InvocationResult result = await InvokeAsync("conflicts --state xregistry-unit-state --job job-a")
                .ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(37));
            Assert.That(result.Executions, Is.EqualTo(1));
            Assert.That(result.Settings, Is.Not.Null);
            Assert.That(result.Settings!.Command, Is.EqualTo(XRegistryConnectorCommand.Conflicts));
            Assert.That(result.Settings.StateDirectory, Is.EqualTo("xregistry-unit-state"));
            Assert.That(result.Settings.JobId, Is.EqualTo("job-a"));
            Assert.That(result.Settings.HttpRoot, Is.Null);
            Assert.That(result.Settings.OpcUaEndpoint, Is.Null);
            Assert.That(result.Settings.ListenAddress, Is.Null);
            Assert.That(result.Settings.ConflictId, Is.Null);
            Assert.That(result.Settings.Resolution, Is.Null);
            Assert.That(result.ValidationError, Is.Empty);
            Assert.That(result.ParserError, Is.Empty);
        }

        [TestCase("prefer-opcua")]
        [TestCase("prefer-http")]
        public async Task ResolveDispatchesConflictIdentityAndExplicitSidePreferenceAsync(string resolution)
        {
            InvocationResult result = await InvokeAsync(
                "resolve --state xregistry-unit-state --job job-a --conflict conflict-42 --resolution " + resolution)
                .ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(37));
            Assert.That(result.Executions, Is.EqualTo(1));
            Assert.That(result.Settings, Is.Not.Null);
            Assert.That(result.Settings!.Command, Is.EqualTo(XRegistryConnectorCommand.Resolve));
            Assert.That(result.Settings.StateDirectory, Is.EqualTo("xregistry-unit-state"));
            Assert.That(result.Settings.JobId, Is.EqualTo("job-a"));
            Assert.That(result.Settings.ConflictId, Is.EqualTo("conflict-42"));
            Assert.That(result.Settings.Resolution, Is.EqualTo(resolution));
            Assert.That(result.Settings.OpcUaEndpoint, Is.Null);
            Assert.That(result.Settings.HttpRoot, Is.Null);
            Assert.That(result.ValidationError, Is.Empty);
            Assert.That(result.ParserError, Is.Empty);
        }

        [TestCase("inspect --http-root http://localhost:8080/ --allow-loopback-http")]
        [TestCase("inspect --http-root http://127.0.0.1:8080/ --allow-loopback-http")]
        [TestCase("inspect --http-root http://[::1]:8080/ --allow-loopback-http")]
        public async Task LoopbackHttpOptInIsPassedOnlyForLocalDevelopmentAsync(string arguments)
        {
            InvocationResult result = await InvokeAsync(arguments).ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(37));
            Assert.That(result.Executions, Is.EqualTo(1));
            Assert.That(result.Settings, Is.Not.Null);
            Assert.That(result.Settings!.AllowLoopbackHttp, Is.True);
            Assert.That(result.Settings.HttpRoot!.Scheme, Is.EqualTo("http"));
            Assert.That(result.Settings.HttpRoot.IsLoopback, Is.True);
            Assert.That(result.ValidationError, Is.Empty);
            Assert.That(result.ParserError, Is.Empty);
        }

        [TestCase("replicate", "replicate")]
        [TestCase("http-gateway", "--listen")]
        [TestCase("http-gateway --listen https://localhost:8443/", "--public-root")]
        [TestCase("opcua-gateway --http-root https://registry.example/", "--listen")]
        [TestCase("sync --opcua opc.tcp://localhost:4840 --registry-node ns=2;s=Registry " +
            "--http-root https://registry.example/", "--state")]
        [TestCase("conflicts", "--state")]
        [TestCase("resolve --state state --resolution prefer-http", "--conflict")]
        [TestCase("resolve --state state --conflict conflict-42", "--resolution")]
        [TestCase(k_httpGateway + " --dry-run", "--dry-run")]
        [TestCase(k_opcUaGateway + " --once", "--once")]
        [TestCase("inspect --http-root https://registry.example/ --once", "--once")]
        [TestCase("conflicts --state state --http-root https://registry.example/", "--http-root")]
        [TestCase("resolve --state state --conflict c --resolution prefer-http --dry-run", "--dry-run")]
        public async Task ParseFailuresProduceDiagnosticsWithoutCallingExecutorAsync(
            string arguments,
            string diagnostic)
        {
            InvocationResult result = await InvokeAsync(arguments).ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.ParseErrors, Is.GreaterThan(0));
            Assert.That(result.Executions, Is.Zero);
            Assert.That(result.Settings, Is.Null);
            Assert.That(result.ParserError, Does.Contain(diagnostic));
            Assert.That(result.ValidationError, Is.Empty);
        }

        [TestCase("http-gateway --listen https://localhost:8443/ --public-root https://bridge.example/", "--opcua")]
        [TestCase("http-gateway --listen https://localhost:8443/ --public-root https://bridge.example/ " +
            "--opcua opc.tcp://localhost:4840", "--registry-node")]
        [TestCase("opcua-gateway --listen opc.tcp://localhost:4841", "--http-root")]
        [TestCase("sync --state state --http-root https://registry.example/", "--opcua")]
        [TestCase("sync --state state --opcua opc.tcp://localhost:4840 --registry-node ns=2;s=Registry", "--http-root")]
        [TestCase("inspect", "Inspect requires")]
        [TestCase("inspect --opcua opc.tcp://localhost:4840", "--registry-node")]
        [TestCase(k_sync + " --conflict-policy latest", "Conflict policy")]
        [TestCase(k_sync + " --deletes unguarded", "no unguarded mode")]
        [TestCase(k_sync + " --deletes false", "--deletes must be on or off")]
        [TestCase(k_sync + " --poll-interval tomorrow", "--poll-interval requires a TimeSpan")]
        [TestCase(k_sync + " --poll-interval 00:00:00", "polling interval")]
        [TestCase(k_sync + " --poll-interval 1.00:00:00.0000001", "polling interval")]
        [TestCase(k_sync + " --profile ../operator", "Profile and job names")]
        [TestCase("inspect --http-root http://registry.example/", "HTTPS")]
        [TestCase("inspect --http-root http://registry.example/ --allow-loopback-http", "HTTPS")]
        [TestCase("inspect --http-root http://localhost:8080/", "HTTPS")]
        [TestCase("inspect --http-root ftp://localhost/ --allow-loopback-http", "HTTPS")]
        [TestCase("inspect --opcua opc.tcp://localhost:4840 --registry-node \" \"", "--registry-node")]
        [TestCase("inspect --http-root https://operator@registry.example/", "no credentials")]
        [TestCase("inspect --http-root https://registry.example/?q=1", "no credentials, query or fragment")]
        [TestCase("inspect --http-root https://registry.example/#part", "no credentials, query or fragment")]
        [TestCase("resolve --state state --conflict conflict-42 --resolution manual", "Resolution must be")]
        public async Task ValidationFailuresUseInjectedErrorWriterAndNeverExecuteAsync(
            string arguments,
            string diagnostic)
        {
            InvocationResult result = await InvokeAsync(arguments).ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(result.ParseErrors, Is.Zero);
            Assert.That(result.Executions, Is.Zero);
            Assert.That(result.Settings, Is.Null);
            Assert.That(result.ValidationError, Does.Contain(diagnostic));
            Assert.That(result.ParserError, Is.Empty);
        }

        [TestCase("--password")]
        [TestCase("--token")]
        [TestCase("--access-token")]
        [TestCase("--username")]
        [TestCase("--unguarded-deletes")]
        [TestCase("--unsafe-deletes")]
        public async Task SecretAndUnguardedDeleteOptionsAreNotAvailableAsync(string option)
        {
            InvocationResult result = await InvokeAsync(k_sync + " " + option + " placeholder").ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.ParseErrors, Is.GreaterThan(0));
            Assert.That(result.Executions, Is.Zero);
            Assert.That(result.Settings, Is.Null);
            Assert.That(result.ParserError, Does.Contain(option));
            Assert.That(result.ValidationError, Is.Empty);
        }

        [TestCase("--config", "configuration file")]
        [TestCase("--model", "model file")]
        public async Task MissingReferencedFilesAreReportedBeforeExecutorIsCalledAsync(string option, string diagnostic)
        {
            string missing = Path.Combine(Path.GetTempPath(), "xregistry-cli-missing-" + Guid.NewGuid().ToString("N"));

            InvocationResult result = await InvokeAsync(k_httpGateway + " " + option + " \"" + missing + "\"")
                .ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(result.ParseErrors, Is.Zero);
            Assert.That(result.Executions, Is.Zero);
            Assert.That(result.ValidationError, Does.Contain(diagnostic));
            Assert.That(result.ParserError, Is.Empty);
        }

        [Test]
        public async Task ConfigurationAndModelPathsWithSpacesReachExecutorWithoutModifyingFilesAsync()
        {
            string directory = Path.Combine(Path.GetTempPath(), "xregistry-cli-" + Guid.NewGuid().ToString("N"));
            string configuration = Path.Combine(directory, "configuration file.json");
            string model = Path.Combine(directory, "model file.json");
            _ = Directory.CreateDirectory(directory);
            try
            {
                await File.WriteAllTextAsync(configuration, """{"profiles":{}}""").ConfigureAwait(false);
                await File.WriteAllTextAsync(model, """{"groups":{}}""").ConfigureAwait(false);

                InvocationResult result = await InvokeAsync(k_httpGateway +
                    " --config \"" + configuration + "\" --model \"" + model + "\"").ConfigureAwait(false);

                Assert.That(result.ExitCode, Is.EqualTo(37));
                Assert.That(result.Executions, Is.EqualTo(1));
                Assert.That(result.Settings, Is.Not.Null);
                Assert.That(result.Settings!.ConfigurationFile, Is.EqualTo(configuration));
                Assert.That(result.Settings.ModelFile, Is.EqualTo(model));
                Assert.That(await File.ReadAllTextAsync(configuration).ConfigureAwait(false),
                    Is.EqualTo("""{"profiles":{}}"""));
                Assert.That(await File.ReadAllTextAsync(model).ConfigureAwait(false), Is.EqualTo("""{"groups":{}}"""));
                Assert.That(result.ValidationError, Is.Empty);
                Assert.That(result.ParserError, Is.Empty);
            }
            finally
            {
                File.Delete(configuration);
                File.Delete(model);
                Directory.Delete(directory);
            }
        }

        [Test]
        public async Task ExecuteDelegateIsAwaitedAndReceivesCallerCancellationTokenAsync()
        {
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            using var cancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken observedToken = default;
            RootCommand command = XRegistryConnectorCommandLine.Create((settings, token) =>
            {
                observedToken = token;
                entered.SetResult(settings.Command == XRegistryConnectorCommand.Conflicts);
                return completion.Task;
            }, error);
            Task<int> invocation = command.Parse("conflicts --state state").InvokeAsync(new InvocationConfiguration
            {
                Output = TextWriter.Null,
                Error = error,
                EnableDefaultExceptionHandler = false
            }, cancellation.Token);
            try
            {
                Task first = await Task.WhenAny(entered.Task, invocation).ConfigureAwait(false);
                Assert.That(first, Is.SameAs(entered.Task));
                Assert.That(await entered.Task.ConfigureAwait(false), Is.True);
                Assert.That(invocation.IsCompleted, Is.False);
                Assert.That(observedToken.CanBeCanceled, Is.True);
                await cancellation.CancelAsync().ConfigureAwait(false);
                Assert.That(observedToken.IsCancellationRequested, Is.True);
                completion.SetResult(23);

                Assert.That(await invocation.ConfigureAwait(false), Is.EqualTo(23));
                Assert.That(error.ToString(), Is.Empty);
            }
            finally
            {
                _ = completion.TrySetResult(23);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ExecutorFailuresAreNotMisreportedAsConfigurationErrorsAsync(bool argumentFailure)
        {
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            Exception failure = argumentFailure
                ? new ArgumentException("Backend rejected the operation.")
                : new IOException("Backend transport failed.");
            int executions = 0;
            RootCommand command = XRegistryConnectorCommandLine.Create((_, _) =>
            {
                executions++;
                return Task.FromException<int>(failure);
            }, error);
            var invocation = new InvocationConfiguration
            {
                Output = TextWriter.Null,
                Error = error,
                EnableDefaultExceptionHandler = false
            };

            await Assert.ThatAsync(() => command.Parse("conflicts --state state").InvokeAsync(invocation),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            Assert.That(executions, Is.EqualTo(1));
            Assert.That(error.ToString(), Is.Empty);
        }

        [TestCase("--help", "http-gateway")]
        [TestCase("sync --help", "--deletes")]
        [TestCase("resolve --help", "--resolution")]
        public async Task HelpDescribesCommandsWithoutInvokingExecutorAsync(string arguments, string expectedHelp)
        {
            InvocationResult result = await InvokeAsync(arguments).ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.Executions, Is.Zero);
            Assert.That(result.Settings, Is.Null);
            Assert.That(result.Output, Does.Contain(expectedHelp));
            Assert.That(result.ValidationError, Is.Empty);
            Assert.That(result.ParserError, Is.Empty);
        }

        private static async Task<InvocationResult> InvokeAsync(string arguments)
        {
            using var validationError = new StringWriter(CultureInfo.InvariantCulture);
            using var parserError = new StringWriter(CultureInfo.InvariantCulture);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            int executions = 0;
            XRegistryConnectorSettings? captured = null;
            RootCommand command = XRegistryConnectorCommandLine.Create((settings, _) =>
            {
                executions++;
                captured = settings;
                return Task.FromResult(37);
            }, validationError);
            ParseResult parse = command.Parse(arguments);
            int exitCode = await parse.InvokeAsync(new InvocationConfiguration
            {
                Output = output,
                Error = parserError,
                EnableDefaultExceptionHandler = false
            }).ConfigureAwait(false);
            return new InvocationResult(exitCode, executions, captured, parse.Errors.Count,
                validationError.ToString(), parserError.ToString(), output.ToString());
        }

        private sealed record InvocationResult(
            int ExitCode,
            int Executions,
            XRegistryConnectorSettings? Settings,
            int ParseErrors,
            string ValidationError,
            string ParserError,
            string Output);

        private const string k_httpGateway =
            "http-gateway --opcua opc.tcp://localhost:4840 --registry-node ns=2;s=Registry " +
            "--listen https://localhost:8443/ --public-root https://bridge.example/xregistry/";
        private const string k_opcUaGateway =
            "opcua-gateway --http-root https://registry.example/root/ --listen opc.tcp://localhost:4841";
        private const string k_sync =
            "sync --opcua opc.tcp://localhost:4840 --registry-node ns=2;s=Registry " +
            "--http-root https://registry.example/root/ --state xregistry-unit-state";
    }
}
#endif
