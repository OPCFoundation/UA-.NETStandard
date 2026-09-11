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
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Configuration;

namespace Opc.Ua.Tools.Tests.Samples
{
    /// <summary>
    /// Checks reference-server consent parsing, warning-before-application behavior, and secure endpoint configuration.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class SampleReferenceServerHostPolicyTests
    {
        /// <summary>
        /// Verifies that help describes None, trust, and provisioning options without running the host action.
        /// </summary>
        [TestCase("--help")]
        [TestCase("-h")]
        [TestCase("-?")]
        public async Task HelpExplainsRelaxationsWithoutExecutingHostAsync(string option)
        {
            RootCommand command = CreateCommand();
            bool started = false;
            command.SetAction(_ => started = true);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await command.Parse(["--allow-none", "--provision", option])
                .InvokeAsync(new InvocationConfiguration { Output = output, Error = error })
                .ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero);
            Assert.That(started, Is.False);
            Assert.That(error.ToString(), Is.Empty);
            Assert.That(output.ToString(), Does.Contain("--allow-none").And.Contain("--autoaccept")
                .And.Contain("--provision").And.Contain("limited namespace").And.Contain("untrusted application"));
            Assert.That(output.ToString(), Does.Not.Contain("WARNING:"));
        }

        /// <summary>
        /// Verifies that unknown switches and malformed consent values fail before host execution or warnings.
        /// </summary>
        [TestCase("--unknown")]
        [TestCase("--allow-none=perhaps")]
        [TestCase("--autoaccept=perhaps")]
        [TestCase("--provision=perhaps")]
        public async Task InvalidCommandLineDoesNotExecuteHostAsync(string option)
        {
            RootCommand command = CreateCommand();
            bool started = false;
            command.SetAction(_ => started = true);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await command.Parse([option])
                .InvokeAsync(new InvocationConfiguration { Output = output, Error = error })
                .ConfigureAwait(false);

            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(started, Is.False);
            Assert.That(error.ToString(), Is.Not.Empty);
            Assert.That(output.ToString(), Does.Not.Contain("WARNING:"));
        }

        /// <summary>
        /// Verifies that a failed warning write prevents None policy from being added to server configuration.
        /// </summary>
        [Test]
        public async Task NoneIsNotEnabledIfWarningCannotBeWrittenAsync()
        {
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration()
            };
            await using var application = new ApplicationInstance(configuration, null);
            using var output = new UnavailableWarningWriter();
            Func<ParseResult, ApplicationConfigurationBuilder, TextWriter, bool> configure =
                LoadPublicMethod<Func<ParseResult, ApplicationConfigurationBuilder, TextWriter, bool>>("ConfigureHost");

            Assert.Throws<IOException>(() => configure(
                CreateCommand().Parse(["--allow-none"]), new ApplicationConfigurationBuilder(application), output));

            Assert.That(configuration.ServerConfiguration.SecurityPolicies, Is.Empty);
        }

        /// <summary>
        /// Verifies that explicit false CLI consent overrides enabled environment defaults for every supported alias.
        /// </summary>
        [TestCase("--allow-none", "REFSERVER_ALLOW-NONE", "--allow-none")]
        [TestCase("--autoaccept", "REFSERVER_AUTOACCEPT", "--autoaccept")]
        [TestCase("-a", "REFSERVER_AUTOACCEPT", "--autoaccept")]
        [TestCase("--provision", "REFSERVER_PROVISION", "--provision")]
        public void ExplicitCommandLineFalseWinsOverEnvironmentTrue(
            string option,
            string environmentKey,
            string canonicalOption)
        {
            string? previous = Environment.GetEnvironmentVariable(environmentKey);
            try
            {
                Environment.SetEnvironmentVariable(environmentKey, "true");
                Func<ArrayOf<string>, ParseResult> parse =
                    LoadPublicMethod<Func<ArrayOf<string>, ParseResult>>("ParseArguments");

                ParseResult inherited = parse([]);
                ParseResult disabled = parse([option + "=false"]);

                Assert.That(inherited.Errors, Is.Empty);
                Assert.That(inherited.GetValue<bool>(canonicalOption), Is.True);
                Assert.That(disabled.Errors, Is.Empty);
                Assert.That(disabled.GetValue<bool>(canonicalOption), Is.False);
            }
            finally
            {
                Environment.SetEnvironmentVariable(environmentKey, previous);
            }
        }

        /// <summary>
        /// Verifies that None-policy consent defaults to false and respects explicit enable and disable forms.
        /// </summary>
        [Test]
        public void NoneEndpointOptionIsExplicitAndDefaultFalse()
        {
            RootCommand command = CreateCommand();
            ParseResult defaults = command.Parse([]);
            ParseResult enabled = command.Parse(["--allow-none"]);
            ParseResult disabled = command.Parse(["--allow-none=false"]);

            Assert.That(defaults.Errors, Is.Empty);
            Assert.That(defaults.GetValue<bool>("--allow-none"), Is.False);
            Assert.That(enabled.Errors, Is.Empty);
            Assert.That(enabled.GetValue<bool>("--allow-none"), Is.True);
            Assert.That(disabled.Errors, Is.Empty);
            Assert.That(disabled.GetValue<bool>("--allow-none"), Is.False);
        }

        /// <summary>
        /// Verifies that None opt-in warns and preserves secure policies without enabling automatic certificate
        /// acceptance.
        /// </summary>
        [Test]
        public async Task NoneOptInAddsOnlyNoneAndWarnsAsync()
        {
            RootCommand command = CreateCommand();
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    SecurityPolicies =
                    [
                        new ServerSecurityPolicy
                        {
                            SecurityMode = MessageSecurityMode.SignAndEncrypt,
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                        }
                    ]
                }
            };
            await using var application = new ApplicationInstance(configuration, null);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            Func<ParseResult, ApplicationConfigurationBuilder, TextWriter, bool> configure =
                LoadPublicMethod<Func<ParseResult, ApplicationConfigurationBuilder, TextWriter, bool>>("ConfigureHost");

            bool autoAccept = configure(
                command.Parse(["--allow-none"]), new ApplicationConfigurationBuilder(application), output);

            Assert.That(autoAccept, Is.False);
            Assert.That(configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates, Is.False);
            Assert.That(configuration.ServerConfiguration.SecurityPolicies.ToList()
                .Select(policy => policy.SecurityPolicyUri),
                Is.EquivalentTo([SecurityPolicies.Basic256Sha256, SecurityPolicies.None]));
            Assert.That(
                output.ToString(),
                Does.Contain("WARNING").And.Contain("None").And.Contain("no message security"));
        }

        /// <summary>
        /// Verifies that trust and provisioning consent produce their own warnings without adding None endpoints.
        /// </summary>
        [TestCase(new string[] { }, false, "")]
        [TestCase(new[] { "--autoaccept" }, true, "untrusted application certificates")]
        [TestCase(new[] { "-a" }, true, "untrusted application certificates")]
        [TestCase(new[] { "--autoaccept=false" }, false, "")]
        [TestCase(new[] { "--autoaccept", "false" }, false, "")]
        [TestCase(new[] { "-a:false" }, false, "")]
        [TestCase(new[] { "--provision" }, true, "limited namespace")]
        [TestCase(new[] { "--provision=false" }, false, "")]
        [TestCase(new[] { "--provision", "--autoaccept=false" }, true, "limited namespace")]
        public async Task TrustAndProvisioningConsentNeverEnableNoneAsync(
            string[] arguments,
            bool expectedAutoAccept,
            string expectedWarning)
        {
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration()
            };
            await using var application = new ApplicationInstance(configuration, null);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            Func<ParseResult, ApplicationConfigurationBuilder, TextWriter, bool> configure =
                LoadPublicMethod<Func<ParseResult, ApplicationConfigurationBuilder, TextWriter, bool>>("ConfigureHost");

            bool autoAccept = configure(
                CreateCommand().Parse(arguments), new ApplicationConfigurationBuilder(application), output);

            Assert.That(autoAccept, Is.EqualTo(expectedAutoAccept));
            Assert.That(configuration.ServerConfiguration.SecurityPolicies, Is.Empty);
            Assert.That(configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates, Is.False);
            if (expectedAutoAccept)
            {
                Assert.That(output.ToString(), Does.Contain("WARNING").And.Contain(expectedWarning));
            }
            else
            {
                Assert.That(output.ToString(), Is.Empty);
            }
        }

        /// <summary>
        /// Verifies that explicit false removes loaded None policy while omission and unrelated server settings are
        /// preserved.
        /// </summary>
        [TestCase(new string[] { }, true)]
        [TestCase(new[] { "--allow-none=false" }, false)]
        [TestCase(new[] { "--allow-none", "false" }, false)]
        public async Task ExplicitFalseOverridesLoadedNoneButOmissionPreservesItAsync(
            string[] arguments,
            bool expectedNone)
        {
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxSessionCount = 37,
                    SecurityPolicies =
                    [
                        new ServerSecurityPolicy
                        {
                            SecurityMode = MessageSecurityMode.SignAndEncrypt,
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                        },
                        new ServerSecurityPolicy
                        {
                            SecurityMode = MessageSecurityMode.None,
                            SecurityPolicyUri = SecurityPolicies.None
                        }
                    ]
                }
            };
            await using var application = new ApplicationInstance(configuration, null);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            Func<ParseResult, ApplicationConfigurationBuilder, TextWriter, bool> configure =
                LoadPublicMethod<Func<ParseResult, ApplicationConfigurationBuilder, TextWriter, bool>>("ConfigureHost");

            bool autoAccept = configure(
                CreateCommand().Parse(arguments), new ApplicationConfigurationBuilder(application), output);

            Assert.That(autoAccept, Is.False);
            Assert.That(configuration.ServerConfiguration.SecurityPolicies.ToList()
                .Exists(policy => policy.SecurityMode == MessageSecurityMode.None), Is.EqualTo(expectedNone));
            Assert.That(configuration.ServerConfiguration.SecurityPolicies.ToList()
                .Exists(policy => policy.SecurityPolicyUri == SecurityPolicies.Basic256Sha256), Is.True);
            Assert.That(configuration.ServerConfiguration.MaxSessionCount, Is.EqualTo(37));
            if (!expectedNone)
            {
                Assert.That(output.ToString(), Is.Empty);
            }
        }

        /// <summary>
        /// Verifies that the ordinary reference-server XML configuration advertises no None security modes or policies.
        /// </summary>
        [Test]
        public void OrdinaryConfigurationAdvertisesOnlySecuredEndpoints()
        {
            var configuration = XDocument.Load(Path.Combine(
                FindRepository(), "samples", "Reference", "ConsoleReferenceServer",
                "Quickstarts.ReferenceServer.Config.xml"));
            XNamespace ns = "http://opcfoundation.org/UA/SDK/Configuration.xsd";
            XElement[] policies = [.. configuration.Root!
                .Element(ns + "ServerConfiguration")!
                .Element(ns + "SecurityPolicies")!
                .Elements(ns + "ServerSecurityPolicy")];

            Assert.That(policies, Is.Not.Empty);
            Assert.That(policies.Select(policy => (string?)policy.Element(ns + "SecurityMode")),
                Does.Not.Contain("None_1"));
            Assert.That(policies.Select(policy => (string?)policy.Element(ns + "SecurityPolicyUri")),
                Does.Not.Contain(SecurityPolicies.None));
        }

        private static RootCommand CreateCommand()
        {
            return LoadPublicMethod<Func<RootCommand>>("CreateCommand")();
        }

        private static T LoadPublicMethod<T>(string name) where T : Delegate
        {
            string configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
            string assemblyPath = Path.Combine(
                FindRepository(), "samples", "Reference", "ConsoleReferenceServer",
                "bin", configuration, "net10.0", "ConsoleReferenceServer.dll");
            Assert.That(File.Exists(assemblyPath), Is.True,
                "Build ConsoleReferenceServer for net10.0 before running the host policy tests.");
            Type program = Assembly.LoadFrom(assemblyPath).GetType("Quickstarts.ReferenceServer.Program", true)!;
            MethodInfo? method = program.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, $"The public ReferenceServer host seam {name} must be available.");
            return method!.CreateDelegate<T>();
        }

        private static string FindRepository()
        {
            DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new DirectoryNotFoundException("Run these tests from the repository build output.");
        }

        private sealed class UnavailableWarningWriter : StringWriter
        {
            /// <summary>
            /// Simulates unavailable warning output by throwing before any text can be written.
            /// </summary>
            public override void WriteLine(string? value)
            {
                throw new IOException("Warning output is unavailable.");
            }
        }
    }
}
#endif
