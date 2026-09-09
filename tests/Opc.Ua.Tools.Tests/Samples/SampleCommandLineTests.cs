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
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Samples;

namespace Opc.Ua.Tools.Tests.Samples
{
    /// <summary>
    /// Checks sample option parsing, explicit security consent, and separation from forwarded host configuration.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class SampleCommandLineTests
    {
        /// <summary>
        /// Verifies that omitting the auto-accept option leaves untrusted-certificate acceptance disabled.
        /// </summary>
        [Test]
        public void DefaultRequiresTrustedCertificates()
        {
            Option<bool> autoAccept = SampleCommandLine.CreateAutoAcceptOption();
            var command = new RootCommand("Sample") { autoAccept };

            ParseResult result = command.Parse([]);

            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.GetValue(autoAccept), Is.False);
        }

        /// <summary>
        /// Verifies that malformed Boolean consent prevents startup and reports the invalid value with help guidance.
        /// </summary>
        [Test]
        public async Task MalformedBooleanDoesNotStartHostAndSuggestsHelpAsync()
        {
            Option<bool> autoAccept = SampleCommandLine.CreateAutoAcceptOption();
            var command = new RootCommand("Sample") { autoAccept };
            bool started = false;
            command.SetAction(_ => started = true);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await SampleCommandLine.InvokeAsync(
                command, ["--auto-accept=perhaps"], output, error).ConfigureAwait(false);

            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(started, Is.False);
            Assert.That(error.ToString(), Does.Contain("perhaps").And.Contain("--help"));
            Assert.That(output.ToString(), Is.Empty);
        }

        /// <summary>
        /// Verifies that an endpoint supplied as a Boolean option value remains an error rather than a positional
        /// argument.
        /// </summary>
        [Test]
        public async Task MalformedBooleanCannotBecomePositionalEndpointAsync()
        {
            Option<bool> autoAccept = SampleCommandLine.CreateAutoAcceptOption();
            var endpoint = new Argument<string>("endpoint")
            {
                Arity = ArgumentArity.ZeroOrOne
            };
            var command = new RootCommand("Sample") { autoAccept, endpoint };
            bool started = false;
            command.SetAction(_ => started = true);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await SampleCommandLine.InvokeAsync(
                command, ["--auto-accept=opc.tcp://localhost:4840"], output, error).ConfigureAwait(false);

            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(started, Is.False);
            Assert.That(error.ToString(), Does.Contain("--auto-accept"));
        }

        /// <summary>
        /// Verifies that each warning describes only the enabled certificate-trust or None-policy relaxation.
        /// </summary>
        [TestCase(true, false, "--auto-accept", "SecurityPolicy None")]
        [TestCase(false, true, "--insecure", "untrusted server certificates")]
        public void WarningsDescribeOnlyEnabledRelaxation(
            bool autoAccept,
            bool securityNone,
            string expected,
            string absent)
        {
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            SampleCommandLine.WriteSecurityWarnings(
                error, autoAccept, securityNone, "server", "--insecure");

            Assert.That(error.ToString(), Does.Contain("WARNING").And.Contain(expected));
            Assert.That(error.ToString(), Does.Not.Contain(absent));
        }

        /// <summary>
        /// Verifies that aliases and explicit Boolean values keep certificate acceptance independent of None-policy
        /// consent.
        /// </summary>
        [TestCase(new string[] { }, false, false)]
        [TestCase(new[] { "--auto-accept" }, true, false)]
        [TestCase(new[] { "--auto-accept", "false" }, false, false)]
        [TestCase(new[] { "--auto-accept=false" }, false, false)]
        [TestCase(new[] { "--autoaccept", "true" }, true, false)]
        [TestCase(new[] { "-a" }, true, false)]
        [TestCase(new[] { "-a:false" }, false, false)]
        [TestCase(new[] { "--insecure" }, false, true)]
        [TestCase(new[] { "--insecure=false" }, false, false)]
        [TestCase(new[] { "--insecure", "false" }, false, false)]
        [TestCase(new[] { "--auto-accept", "--insecure" }, true, true)]
        [TestCase(new[] { "--auto-accept=false", "--insecure" }, false, true)]
        public async Task TrustAndNoneOptionsRemainIndependentAsync(
            string[] arguments,
            bool expectedAutoAccept,
            bool expectedNone)
        {
            Option<bool> autoAccept = SampleCommandLine.CreateAutoAcceptOption("--autoaccept", "-a");
            var insecure = new Option<bool>("--insecure");
            var command = new RootCommand("Sample") { autoAccept, insecure };
            (bool AutoAccept, bool None)? observed = null;
            command.SetAction(result => observed = (result.GetValue(autoAccept), result.GetValue(insecure)));
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await SampleCommandLine.InvokeAsync(
                command, arguments, output, error).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero);
            Assert.That(observed, Is.EqualTo((expectedAutoAccept, expectedNone)));
            Assert.That(error.ToString(), Is.Empty);
        }

        /// <summary>
        /// Verifies that every help alias prints usage without starting the host or warning about unapplied
        /// relaxations.
        /// </summary>
        [TestCase("--help")]
        [TestCase("-h")]
        [TestCase("-?")]
        public async Task HelpDoesNotCreateHostOrEmitRelaxationWarningsAsync(string help)
        {
            Option<bool> autoAccept = SampleCommandLine.CreateAutoAcceptOption();
            var insecure = new Option<bool>("--insecure");
            var command = new RootCommand("Sample description") { autoAccept, insecure };
            bool started = false;
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            command.SetAction(result =>
            {
                SampleCommandLine.WriteSecurityWarnings(
                    error, result.GetValue(autoAccept), result.GetValue(insecure), "server", "--insecure");
                started = true;
            });

            int exitCode = await SampleCommandLine.InvokeAsync(
                command, ["--auto-accept", "--insecure", help], output, error).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero);
            Assert.That(started, Is.False);
            Assert.That(output.ToString(), Does.Contain("Sample description").And.Contain("--auto-accept"));
            Assert.That(error.ToString(), Is.Empty);
        }

        /// <summary>
        /// Verifies that unknown options and invalid Boolean aliases report help guidance without invoking the action.
        /// </summary>
        [TestCase("--unknown")]
        [TestCase("-z")]
        [TestCase("--insecure=perhaps")]
        [TestCase("--autoaccept=perhaps")]
        [TestCase("-a:perhaps")]
        [TestCase("--auto-accept=")]
        public async Task InvalidOptionsDoNotInvokeActionAsync(string argument)
        {
            Option<bool> autoAccept = SampleCommandLine.CreateAutoAcceptOption("--autoaccept", "-a");
            var insecure = new Option<bool>("--insecure");
            var command = new RootCommand("Sample") { autoAccept, insecure };
            bool started = false;
            command.SetAction(_ => started = true);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await SampleCommandLine.InvokeAsync(
                command, [argument], output, error).ConfigureAwait(false);

            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(started, Is.False);
            Assert.That(error.ToString(), Does.Contain("--help").And.Not.Contain("WARNING"));
        }

        /// <summary>
        /// Verifies that flag-like text inside another option's value is preserved without enabling auto-accept.
        /// </summary>
        [Test]
        public async Task FlagTextInOptionValueDoesNotEnableTrustAsync()
        {
            Option<bool> autoAccept = SampleCommandLine.CreateAutoAcceptOption();
            var config = new Option<string>("--config");
            var command = new RootCommand("Sample") { autoAccept, config };
            string? observed = null;
            bool? observedAutoAccept = null;
            command.SetAction(result =>
            {
                observed = result.GetValue(config);
                observedAutoAccept = result.GetValue(autoAccept);
            });
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await SampleCommandLine.InvokeAsync(
                command, ["--config=--auto-accept=notboolean"], output, error).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero);
            Assert.That(observed, Is.EqualTo("--auto-accept=notboolean"));
            Assert.That(observedAutoAccept, Is.False);
            Assert.That(error.ToString(), Is.Empty);
        }

        /// <summary>
        /// Verifies that positional endpoint URLs survive Boolean parsing even when the URL contains flag-like text.
        /// </summary>
        [TestCase("opc.tcp://localhost:62542/MinimalCalcServer")]
        [TestCase("opc.tcp://localhost:4840/--auto-accept")]
        public async Task PositionalEndpointIsPreservedAsync(string discoveryUrl)
        {
            Option<bool> autoAccept = SampleCommandLine.CreateAutoAcceptOption();
            var endpoint = new Argument<string>("endpoint") { Arity = ArgumentArity.ZeroOrOne };
            var command = new RootCommand("Sample") { autoAccept, endpoint };
            string? observed = null;
            command.SetAction(result => observed = result.GetValue(endpoint));
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await SampleCommandLine.InvokeAsync(
                command, ["--auto-accept", "false", discoveryUrl], output, error).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero);
            Assert.That(observed, Is.EqualTo(discoveryUrl));
            Assert.That(error.ToString(), Is.Empty);
        }

        /// <summary>
        /// Verifies that disabling both security relaxations leaves the warning stream empty.
        /// </summary>
        [Test]
        public void DisabledRelaxationsProduceNoWarnings()
        {
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            SampleCommandLine.WriteSecurityWarnings(error, false, false, "client", "--nosecurity");

            Assert.That(error.ToString(), Is.Empty);
        }

        /// <summary>
        /// Verifies that command invocation returns the asynchronous action's exit status unchanged.
        /// </summary>
        [Test]
        public async Task ActionExitStatusIsPreservedAsync()
        {
            var command = new RootCommand("Sample");
            command.SetAction((_, _) => Task.FromResult(7));
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await SampleCommandLine.InvokeAsync(
                command, [], output, error).ConfigureAwait(false);

            Assert.That(exitCode, Is.EqualTo(7));
        }

        /// <summary>
        /// Verifies that arguments after the host separator bind configuration without becoming sample security flags.
        /// </summary>
        [Test]
        public void HostArgumentsRetainConfigurationValuesWithoutEnablingSampleFlags()
        {
            (string[] sample, string[] host) = SampleCommandLine.SplitHostArguments(
                ["--auto-accept=false", "--", "--port", "62543", "--Example:Label", "--auto-accept"]);
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = host,
                DisableDefaults = true
            });
            using ConfigurationManager configuration = builder.Configuration;

            Assert.That(sample, Has.Length.EqualTo(1));
            Assert.That(sample[0], Is.EqualTo("--auto-accept=false"));
            Assert.That(configuration["port"], Is.EqualTo("62543"));
            Assert.That(configuration["Example:Label"], Is.EqualTo("--auto-accept"));
        }

        /// <summary>
        /// Verifies that host cross-validation cannot accept unknown sample switches or non-assignment arguments.
        /// </summary>
        [TestCase("--unknown")]
        [TestCase("--unknown=value")]
        [TestCase("not-an-assignment")]
        public async Task HostConfigurationRejectsUnknownSampleOptionsEvenDuringCrossValidationAsync(string argument)
        {
            Argument<string[]> configuration = SampleCommandLine.CreateConfigurationArgument();
            var command = new RootCommand("Sample") { configuration };
            // Host cross-validation reads settings before argument validators run.
            command.Validators.Add(result => _ = result.GetValue(configuration));
            bool started = false;
            command.SetAction(_ => started = true);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await SampleCommandLine.InvokeAsync(
                command, [argument], output, error).ConfigureAwait(false);

            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(started, Is.False);
            Assert.That(error.ToString(), Does.Contain("key=value").And.Contain("--help"));
        }

        /// <summary>
        /// Verifies that a forwarded host switch without its value produces an actionable error.
        /// </summary>
        [TestCase("--port")]
        [TestCase("--environment", "Development", "--port")]
        [TestCase("environment=Development", "--port")]
        public void ForwardedHostSwitchRequiresAValue(params string[] arguments)
        {
            string? error = SampleCommandLine.GetHostArgumentError(arguments);

            Assert.That(error, Does.Contain("--port").And.Contain("value"));
        }

        /// <summary>
        /// Verifies that valid host assignments and flag-like configuration values pass host-argument validation.
        /// </summary>
        [TestCase]
        [TestCase("--port", "62543")]
        [TestCase("port=62543")]
        [TestCase("--Example:Label", "--auto-accept")]
        [TestCase("--Example:Label=--auto-accept")]
        public void ValidHostConfigurationValuesAreNotSampleFlags(params string[] arguments)
        {
            Assert.That(SampleCommandLine.GetHostArgumentError(arguments), Is.Null);
        }

        /// <summary>
        /// Verifies that malformed forwarded configuration is reported as an invalid host-configuration error.
        /// </summary>
        [TestCase("-p=62542")]
        public void MalformedForwardedHostConfigurationHasActionableError(params string[] arguments)
        {
            string? error = SampleCommandLine.GetHostArgumentError(arguments);

            Assert.That(error, Does.Contain("Invalid host configuration"));
        }

        /// <summary>
        /// Verifies that missing, nonnumeric, and out-of-range ports prevent invocation and explain the permitted
        /// values.
        /// </summary>
        [TestCase("65536", "65535")]
        [TestCase("0", "65535")]
        [TestCase("-1", "65535")]
        [TestCase("not-a-port", "65535")]
        [TestCase("", "Required argument missing")]
        public async Task InvalidNumericHostOptionDoesNotInvokeActionAsync(string value, string expectedError)
        {
            Option<string> port = SampleCommandLine.CreateInt32ConfigurationOption(
                "--port", "Endpoint port.", 1, 65535);
            var command = new RootCommand("Server") { port };
            bool started = false;
            command.SetAction(_ => started = true);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await SampleCommandLine.InvokeAsync(
                command, [$"--port={value}"], output, error).ConfigureAwait(false);

            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(started, Is.False);
            Assert.That(error.ToString(), Does.Contain("--port").And.Contain(expectedError).And.Contain("--help"));
        }

        /// <summary>
        /// Verifies that explicitly disabling each trust alias preserves unrelated JSON settings and keeps consent off.
        /// </summary>
        [TestCase("--auto-accept")]
        [TestCase("--autoaccept")]
        [TestCase("-a")]
        [TestCase("--insecure")]
        public async Task ServerTrustAliasesAcceptExplicitFalseWithoutOverridingJsonSettingsAsync(string alias)
        {
            Option<bool> autoAccept = SampleCommandLine.CreateAutoAcceptOption("--autoaccept", "-a", "--insecure");
            Option<string> port = SampleCommandLine.CreateInt32ConfigurationOption(
                "--port", "Endpoint port.", 1, 65535);
            Argument<string[]> configuration = SampleCommandLine.CreateConfigurationArgument();
            var command = new RootCommand("Server") { autoAccept, port, configuration };
            string? observedPort = null;
            string? observedLabel = null;
            bool? observedTrust = null;
            command.SetAction(result =>
            {
                using var json = new MemoryStream(Encoding.UTF8.GetBytes(
                    // lang=json
                    """{"port":62545,"AutoAcceptUntrustedCertificates":true,"Example":{"Label":"from-json"}}"""));
                using var settings = new ConfigurationManager();
                settings.AddJsonStream(json);
                settings.AddCommandLine(SampleCommandLine.GetHostArguments(result, [], configuration, port));
                observedPort = settings["port"];
                observedLabel = settings["Example:Label"];
                observedTrust = result.GetValue(autoAccept);
            });
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await SampleCommandLine.InvokeAsync(
                command, [alias, "false"], output, error).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero);
            Assert.That(observedPort, Is.EqualTo("62545"));
            Assert.That(observedLabel, Is.EqualTo("from-json"));
            Assert.That(observedTrust, Is.False);
            Assert.That(error.ToString(), Is.Empty);
        }

        /// <summary>
        /// Verifies that enabling a server trust alias warns about client certificates without advertising None policy.
        /// </summary>
        [TestCase("--auto-accept")]
        [TestCase("--autoaccept")]
        [TestCase("-a")]
        [TestCase("--insecure")]
        public async Task ServerTrustAliasesWarnWithoutEnablingNoneAsync(string alias)
        {
            Option<bool> autoAccept = SampleCommandLine.CreateAutoAcceptOption("--autoaccept", "-a", "--insecure");
            var command = new RootCommand("Server") { autoAccept };
            bool? observedTrust = null;
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            command.SetAction(result =>
            {
                observedTrust = result.GetValue(autoAccept);
                SampleCommandLine.WriteSecurityWarnings(error, observedTrust.Value, false, "client", string.Empty);
            });

            int exitCode = await SampleCommandLine.InvokeAsync(
                command, [alias], output, error).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero);
            Assert.That(observedTrust, Is.True);
            Assert.That(error.ToString(), Does.Contain("untrusted client certificates"));
            Assert.That(error.ToString(), Does.Not.Contain("SecurityPolicy None"));
        }

        /// <summary>
        /// Verifies that the minimum and maximum valid port values reach the command action unchanged.
        /// </summary>
        [TestCase("1")]
        [TestCase("65535")]
        public async Task NumericHostOptionPreservesBoundaryValuesAsync(string value)
        {
            Option<string> port = SampleCommandLine.CreateInt32ConfigurationOption(
                "--port", "Endpoint port.", 1, 65535);
            var command = new RootCommand("Server") { port };
            string? observed = null;
            command.SetAction(result => observed = result.GetValue(port));
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await SampleCommandLine.InvokeAsync(
                command, ["--port", value], output, error).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero);
            Assert.That(observed, Is.EqualTo(value));
            Assert.That(error.ToString(), Is.Empty);
        }

        /// <summary>
        /// Verifies named-option precedence over forwarded and positional configuration without changing explicit
        /// trust.
        /// </summary>
        [Test]
        public async Task NamedHostOptionsOverrideConfigurationWithoutChangingTrustAsync()
        {
            Option<bool> autoAccept = SampleCommandLine.CreateAutoAcceptOption("--autoaccept", "-a", "--insecure");
            var port = new Option<string>("--port");
            Argument<string[]> configuration = SampleCommandLine.CreateConfigurationArgument();
            var command = new RootCommand("Server") { autoAccept, port, configuration };
            string? observedPort = null;
            string? observedLabel = null;
            bool? observedTrust = null;
            command.SetAction(result =>
            {
                using var settings = new ConfigurationManager();
                settings.AddInMemoryCollection(
                    [new("port", "62541"), new("AutoAcceptUntrustedCertificates", "true")]);
                settings.AddCommandLine(SampleCommandLine.GetHostArguments(
                    result, ["--port", "62542", "--Example:Label", "--auto-accept"],
                    configuration, port));
                observedPort = settings["port"];
                observedLabel = settings["Example:Label"];
                observedTrust = result.GetValue(autoAccept);
            });
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await SampleCommandLine.InvokeAsync(
                command, ["--insecure=false", "port=62543", "--port", "62544"], output, error).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero);
            Assert.That(observedPort, Is.EqualTo("62544"));
            Assert.That(observedLabel, Is.EqualTo("--auto-accept"));
            Assert.That(observedTrust, Is.False);
            Assert.That(error.ToString(), Is.Empty);
        }

        /// <summary>
        /// Verifies that positional host assignments override forwarded values while flag-like labels remain inert.
        /// </summary>
        [Test]
        public async Task PositionalHostSettingsOverrideForwardedValuesWithoutChangingTrustAsync()
        {
            Option<bool> autoAccept = SampleCommandLine.CreateAutoAcceptOption();
            Argument<string[]> configuration = SampleCommandLine.CreateConfigurationArgument();
            var command = new RootCommand("Sample") { autoAccept, configuration };
            (string[] sample, string[] host) = SampleCommandLine.SplitHostArguments(
                ["--auto-accept=false", "port=62544", "Example:Label=--auto-accept", "--", "--port", "62543"]);
            string? port = null;
            string? label = null;
            bool? trust = null;
            command.SetAction(result =>
            {
                HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                {
                    Args = host,
                    DisableDefaults = true
                });
                using ConfigurationManager settings = builder.Configuration;
                settings.AddCommandLine(result.GetValue(configuration) ?? []);
                port = settings["port"];
                label = settings["Example:Label"];
                trust = result.GetValue(autoAccept);
            });
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await SampleCommandLine.InvokeAsync(
                command, sample, output, error).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero);
            Assert.That(port, Is.EqualTo("62544"));
            Assert.That(label, Is.EqualTo("--auto-accept"));
            Assert.That(trust, Is.False);
            Assert.That(error.ToString(), Is.Empty);
        }
    }
}
#endif
