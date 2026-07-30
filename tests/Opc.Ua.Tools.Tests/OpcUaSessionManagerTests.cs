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
 *
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Tools.Tests
{
    [TestFixture]
    public class OpcUaSessionManagerTests
    {
        [Test]
        public async Task ConcurrentAutoAcceptContextsUseIndependentCertificateManagersAsync()
        {
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(static _ => { });
            ApplicationConfiguration configuration = CreateConfiguration(
                telemetry,
                out CertificateManager sharedCertificateManager);
            using (sharedCertificateManager)
            {
                Func<Certificate, ServiceResult, bool> sharedCallback = static (_, _) => false;
                sharedCertificateManager.AcceptError = sharedCallback;

                Task<OpcUaSessionManager.ConnectionValidationContext>[] tasks = Enumerable
                    .Range(0, 4)
                    .Select(_ => OpcUaSessionManager.CreateConnectionValidationContextAsync(
                        configuration,
                        telemetry,
                        true,
                        CancellationToken.None))
                    .ToArray();
                OpcUaSessionManager.ConnectionValidationContext[] contexts =
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                try
                {
                    Assert.That(configuration.CertificateManager, Is.SameAs(sharedCertificateManager));
                    Assert.That(sharedCertificateManager.AcceptError, Is.SameAs(sharedCallback));
                    Assert.That(
                        contexts.Select(context => context.Configuration.CertificateManager)
                            .Distinct()
                            .Count(),
                        Is.EqualTo(contexts.Length));

                    foreach (OpcUaSessionManager.ConnectionValidationContext context in contexts)
                    {
                        Assert.That(context.IsIsolated, Is.True);
                        Assert.That(context.UseSharedChannelManager, Is.False);
                        Assert.That(context.Configuration, Is.Not.SameAs(configuration));
                        Assert.That(
                            context.Configuration.CertificateManager,
                            Is.Not.SameAs(sharedCertificateManager));
                    }

                    contexts[0].Dispose();
                    Assert.That(
                        contexts[1].Configuration.CertificateManager.AcceptError,
                        Is.Not.Null);
                    Assert.That(sharedCertificateManager.AcceptError, Is.SameAs(sharedCallback));
                }
                finally
                {
                    foreach (OpcUaSessionManager.ConnectionValidationContext context in contexts)
                    {
                        context.Dispose();
                    }
                }
            }
        }

        [Test]
        public async Task AutoAcceptContextRetainsValidationPolicyForReconnectAsync()
        {
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(static _ => { });
            ApplicationConfiguration configuration = CreateConfiguration(
                telemetry,
                out CertificateManager sharedCertificateManager);
            using (sharedCertificateManager)
            using (OpcUaSessionManager.ConnectionValidationContext context =
                await OpcUaSessionManager.CreateConnectionValidationContextAsync(
                    configuration,
                    telemetry,
                    true,
                    CancellationToken.None).ConfigureAwait(false))
            {
                ApplicationConfiguration initialConfiguration = context.Configuration;
                ICertificateManager initialManager = initialConfiguration.CertificateManager;
                Func<Certificate, ServiceResult, bool>? acceptError = initialManager.AcceptError;

                Assert.That(acceptError, Is.Not.Null);
                Assert.That(
                    acceptError!(null!, new ServiceResult(StatusCodes.BadCertificateUntrusted)),
                    Is.True);
                Assert.That(
                    acceptError(null!, new ServiceResult(StatusCodes.BadCertificateTimeInvalid)),
                    Is.False);

                await Task.Yield();

                Assert.That(context.Configuration, Is.SameAs(initialConfiguration));
                Assert.That(context.Configuration.CertificateManager, Is.SameAs(initialManager));
                Assert.That(context.Configuration.CertificateManager.AcceptError, Is.SameAs(acceptError));
                Assert.That(configuration.CertificateManager, Is.SameAs(sharedCertificateManager));
                Assert.That(sharedCertificateManager.AcceptError, Is.Null);
            }
        }

        [Test]
        public async Task StrictContextUsesSharedValidationAndChannelManagerAsync()
        {
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(static _ => { });
            ApplicationConfiguration configuration = CreateConfiguration(
                telemetry,
                out CertificateManager sharedCertificateManager);
            using (sharedCertificateManager)
            using (OpcUaSessionManager.ConnectionValidationContext context =
                await OpcUaSessionManager.CreateConnectionValidationContextAsync(
                    configuration,
                    telemetry,
                    false,
                    CancellationToken.None).ConfigureAwait(false))
            {
                Assert.That(context.IsIsolated, Is.False);
                Assert.That(context.UseSharedChannelManager, Is.True);
                Assert.That(context.Configuration, Is.SameAs(configuration));
                Assert.That(
                    context.Configuration.CertificateManager,
                    Is.SameAs(sharedCertificateManager));
            }
        }

        private static ApplicationConfiguration CreateConfiguration(
            ITelemetryContext telemetry,
            out CertificateManager certificateManager)
        {
            var securityConfiguration = new SecurityConfiguration();
            certificateManager = CertificateManagerFactory.Create(
                securityConfiguration,
                telemetry);
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "MCP Session Manager Tests",
                ApplicationUri = "urn:localhost:opcua:mcp-tests",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = securityConfiguration,
                ClientConfiguration = new ClientConfiguration(),
                CertificateManager = certificateManager
            };
        }
    }
}
#endif
