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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using AggregationClient;
using AggregationServer;
using FlatTagServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Samples;
using Opc.Ua.Server.Hosting;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server;

namespace Opc.Ua.WotCon.Samples.Tests
{
    /// <summary>
    /// Checks aggregation sample startup, secure host defaults, management access, and required runtime registrations.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Category("Samples")]
    public sealed class AggregationStartupTests
    {
        /// <summary>
        /// Verifies that explicit security flags override host settings and named ports override other configuration
        /// sources.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, true, true)]
        public async Task ExplicitSecurityFlagsOverrideHostConfigurationAsync(
            bool autoAccept, bool securityNone, bool anonymousManagement)
        {
            bool invoked = false;
            int exitCode = await WotSampleCommandLine.InvokeAsync(
                [
                    $"--auto-accept={autoAccept}", $"--security-none={securityNone}",
                    $"--allow-anonymous-management={anonymousManagement}",
                    "AutoAcceptUntrustedCertificates=true", "IncludeUnsecurePolicyNone=true",
                    "AllowAnonymousManagement=true", "port=62501", "--port", "62502",
                    "--", "--port", "62503"
                ],
                "test",
                ["port"],
                management: true,
                (builder, accept, none, anonymous, _) =>
                {
                    invoked = true;
                    Assert.That(accept, Is.EqualTo(autoAccept));
                    Assert.That(none, Is.EqualTo(securityNone));
                    Assert.That(anonymous, Is.EqualTo(anonymousManagement));
                    Assert.That(builder.Configuration["port"], Is.EqualTo("62502"));
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            Assert.That(exitCode, Is.Zero);
            Assert.That(invoked, Is.True);
        }

        /// <summary>
        /// Verifies that invalid security consent or out-of-range host options prevent the sample action from running.
        /// </summary>
        [TestCase("--security-none=invalid")]
        [TestCase("--allow-anonymous-management=invalid")]
        [TestCase("--port=65536")]
        public async Task InvalidSecurityAndHostOptionsNeverInvokeActionAsync(string argument)
        {
            bool invoked = false;
            int exitCode = await WotSampleCommandLine.InvokeAsync(
                [argument], "test", ["port"], management: true,
                (_, _, _, _, _) =>
                {
                    invoked = true;
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(invoked, Is.False);
        }

        /// <summary>
        /// Verifies that sample help and parse errors return their expected exit codes without creating PKI stores.
        /// </summary>
        [TestCase("FlatTagServer", "--help", 0)]
        [TestCase("AggregationServer", "--help", 0)]
        [TestCase("AggregationClient", "--help", 0)]
        [TestCase("FlatTagServer", "--unknown-option", 1)]
        [TestCase("AggregationServer", "--unknown-option", 1)]
        [TestCase("AggregationClient", "--unknown-option", 1)]
        [TestCase("FlatTagServer", "--auto-accept=invalid", 1)]
        public async Task CommandLineHelpAndErrorsDoNotInitializePkiAsync(
            string application,
            string argument,
            int expectedExitCode)
        {
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            string assembly = Path.Combine(AppContext.BaseDirectory, application + ".dll");
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("exec");
            startInfo.ArgumentList.Add("--depsfile");
            startInfo.ArgumentList.Add(Path.ChangeExtension(
                typeof(AggregationStartupTests).Assembly.Location, ".deps.json"));
            startInfo.ArgumentList.Add("--runtimeconfig");
            startInfo.ArgumentList.Add(Path.ChangeExtension(
                typeof(AggregationStartupTests).Assembly.Location, ".runtimeconfig.json"));
            startInfo.ArgumentList.Add(assembly);
            startInfo.ArgumentList.Add(argument);
            startInfo.ArgumentList.Add($"pkiRoot={root}");
            using Process process = Process.Start(startInfo)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                string diagnostic = await output.ConfigureAwait(false) + await error.ConfigureAwait(false);
                Assert.That(process.ExitCode, Is.EqualTo(expectedExitCode), diagnostic);
                Assert.That(diagnostic, Does.Contain("--help").Or.Contain("Usage"));
                Assert.That(Directory.Exists(root), Is.False);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies that registry management defaults to an authenticated SecurityAdmin on a SignAndEncrypt channel.
        /// </summary>
        [Test]
        public void RegistryManagementRequiresAuthenticatedSecurityAdminOnEncryptedChannel()
        {
            using IHost host = AggregationServerHost.Build(new AggregationServerOptions());
            WotManagementAccessPolicy policy = host.Services
                .GetRequiredService<WotRegistryServerOptions>().ManagementAccess;
            Assert.That(policy.AllowAnonymous, Is.False);
            Assert.That(policy.MinimumSecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(policy.RequiredRoleId, Is.EqualTo(Ua.ObjectIds.WellKnownRole_SecurityAdmin));
        }

        /// <summary>
        /// Verifies independent projection of certificate-trust and None-policy options into source, aggregate, and
        /// client hosts.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void TrustAndUnsecuredPoliciesAreIndependent(bool autoAccept, bool securityNone)
        {
            using IHost source = FlatTagServerHost.Build(new FlatTagServerOptions
            {
                AutoAcceptUntrustedCertificates = autoAccept,
                IncludeUnsecurePolicyNone = securityNone
            });
            using IHost aggregate = AggregationServerHost.Build(new AggregationServerOptions
            {
                AutoAcceptUntrustedCertificates = autoAccept,
                IncludeUnsecurePolicyNone = securityNone
            });
            using IHost client = AggregationClientRunner.BuildHost(new AggregationClientOptions
            {
                AutoAcceptUntrustedCertificates = autoAccept,
                UseSecurityPolicyNone = securityNone
            });
            Assert.That(source.Services.GetRequiredService<IOptions<OpcUaServerOptions>>()
                .Value.AutoAcceptUntrustedCertificates, Is.EqualTo(autoAccept));
            Assert.That(source.Services.GetRequiredService<IOptions<OpcUaServerOptions>>()
                .Value.IncludeUnsecurePolicyNone, Is.EqualTo(securityNone));
            Assert.That(aggregate.Services.GetRequiredService<IOptions<OpcUaServerOptions>>()
                .Value.AutoAcceptUntrustedCertificates, Is.EqualTo(autoAccept));
            Assert.That(aggregate.Services.GetRequiredService<IOptions<OpcUaServerOptions>>()
                .Value.IncludeUnsecurePolicyNone, Is.EqualTo(securityNone));
            Assert.That(aggregate.Services.GetRequiredService<IOptions<OpcUaClientOptions>>()
                .Value.AutoAcceptUntrustedCertificates, Is.EqualTo(autoAccept));
            Assert.That(client.Services.GetRequiredService<IOptions<OpcUaClientOptions>>()
                .Value.AutoAcceptUntrustedCertificates, Is.EqualTo(autoAccept));
            DiscoveryConnectOptions discovery = client.Services.GetRequiredService<DiscoveryConnectOptions>();
            Assert.That(discovery.SecurityMode, Is.EqualTo(
                securityNone ? MessageSecurityMode.None : MessageSecurityMode.SignAndEncrypt));
            Assert.That(discovery.SecurityPolicyUri, Is.EqualTo(
                securityNone ? SecurityPolicies.None : SecurityPolicies.Basic256Sha256));
        }

        /// <summary>
        /// Verifies that directly constructed sample hosts disable untrusted-certificate acceptance and None endpoints.
        /// </summary>
        [Test]
        public void DirectHostsRequireTrustedCertificatesAndSecureEndpoints()
        {
            using IHost source = FlatTagServerHost.Build(new FlatTagServerOptions());
            using IHost aggregate = AggregationServerHost.Build(new AggregationServerOptions());
            using IHost client = AggregationClientRunner.BuildHost(new AggregationClientOptions());

            Assert.That(source.Services.GetRequiredService<IOptions<OpcUaServerOptions>>()
                .Value.AutoAcceptUntrustedCertificates, Is.False);
            Assert.That(source.Services.GetRequiredService<IOptions<OpcUaServerOptions>>()
                .Value.IncludeUnsecurePolicyNone, Is.False);
            Assert.That(aggregate.Services.GetRequiredService<IOptions<OpcUaServerOptions>>()
                .Value.AutoAcceptUntrustedCertificates, Is.False);
            Assert.That(aggregate.Services.GetRequiredService<IOptions<OpcUaServerOptions>>()
                .Value.IncludeUnsecurePolicyNone, Is.False);
            Assert.That(aggregate.Services.GetRequiredService<IOptions<OpcUaClientOptions>>()
                .Value.AutoAcceptUntrustedCertificates, Is.False);
            Assert.That(client.Services.GetRequiredService<IOptions<OpcUaClientOptions>>()
                .Value.AutoAcceptUntrustedCertificates, Is.False);
        }

        /// <summary>
        /// Verifies that aggregation sample project settings select executable frameworks and retain legacy-TFM
        /// restrictions.
        /// </summary>
        [Test]
        public void SampleTargetsOnlyExecutableAggregationFrameworks()
        {
            XDocument server = LoadProject("AggregationServer");
            Assert.That(
                ReadProperty(server, "TargetFrameworks", "'$(CustomTestTarget)' == ''"),
                Is.EqualTo("net8.0;net9.0;net10.0"));
            Assert.That(
                ReadProperty(server, "RestrictForLegacyTfm", null),
                Is.EqualTo("true"));

            Assert.That(
                ReadProperty(LoadProject("AggregationClient"), "TargetFrameworks", null),
                Is.EqualTo("$(AppTargetFrameworks)"));
            Assert.That(
                ReadProperty(LoadProject("FlatTagServer"), "TargetFrameworks", null),
                Is.EqualTo("$(AppTargetFrameworks)"));
        }

        /// <summary>
        /// Verifies that the aggregation host registers the OPC UA binding executor required by its documented
        /// mappings.
        /// </summary>
        [Test]
        public void AggregationHostRegistersOpcUaExecutor()
        {
            using IHost host = AggregationServerHost.Build(
                new AggregationServerOptions
                {
                    EndpointUrl = "opc.tcp://127.0.0.1:62550/AggregationServerStartupTest",
                    ApplicationName = "AggregationServerStartupTest"
                });
            var executorIds = new List<string>();
            foreach (IWotBindingExecutor executor
                in host.Services.GetServices<IWotBindingExecutor>())
            {
                executorIds.Add(executor.Identity.Id);
            }

            Assert.That(
                executorIds,
                Does.Contain("opc.opcua"),
                "The documented OPC UA mappings must have a runtime executor.");
        }

        private static XDocument LoadProject(string projectName)
        {
            return XDocument.Load(
                Path.Combine(
                    FindRepositoryRoot(),
                    "samples",
                    "WotCon",
                    projectName,
                    projectName + ".csproj"));
        }

        private static string ReadProperty(
            XDocument project,
            string propertyName,
            string? condition)
        {
            foreach (XElement property in project.Descendants(propertyName))
            {
                string? actualCondition = property.Attribute("Condition")?.Value;
                if (string.Equals(actualCondition, condition, StringComparison.Ordinal))
                {
                    return property.Value;
                }
            }
            throw new AssertionException(
                $"Property '{propertyName}' with condition '{condition}' was not found.");
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("The repository root was not found.");
        }
    }
}
