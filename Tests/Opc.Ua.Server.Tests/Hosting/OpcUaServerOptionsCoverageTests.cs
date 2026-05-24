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

#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// Coverage tests for the new first-class option properties exposed
    /// on <see cref="OpcUaServerOptions"/> (reverse connect, user-token
    /// policies, extended transport quotas, ECC policies, operation
    /// limits, registration endpoint, security hardening).
    /// </summary>
    /// <remarks>
    /// These tests inspect the bound <see cref="IOptions{TOptions}"/>
    /// snapshot after registration — they do not start the hosted
    /// service. The hosted service's projection of each option into the
    /// underlying <see cref="IApplicationConfigurationBuilder"/> chain
    /// is exercised by the existing AOT and combined-host integration
    /// tests.
    /// </remarks>
    [TestFixture]
    [Category("Server")]
    [Category("Hosting")]
    [Parallelizable]
    public sealed class OpcUaServerOptionsCoverageTests
    {
        [Test]
        public void DefaultsForSecurityHardeningArePresent()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(_ => { });
            using ServiceProvider sp = services.BuildServiceProvider();

            OpcUaServerOptions opts = sp.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
            Assert.That(opts.RejectSHA1Certificates, Is.True);
            Assert.That(opts.MinCertificateKeySize, Is.EqualTo(2048));
            Assert.That(opts.IncludeEccPolicies, Is.False);
            Assert.That(opts.UserTokenPolicies, Is.Empty);
            Assert.That(opts.ReverseConnect, Is.Null);
            Assert.That(opts.OperationLimits, Is.Null);
            Assert.That(opts.RegistrationEndpointUrl, Is.Null);
            Assert.That(opts.MaxMessageSize, Is.Null);
            Assert.That(opts.OperationTimeoutMs, Is.Null);
        }

        [Test]
        public void ReverseConnectOptionsRoundTrip()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(o =>
            {
                o.ReverseConnect = new ServerReverseConnectOptions
                {
                    ConnectIntervalMs = 5000,
                    ConnectTimeoutMs = 10000,
                    RejectTimeoutMs = 20000
                };
                o.ReverseConnect.Clients.Add(new ServerReverseConnectClientOptions
                {
                    EndpointUrl = "opc.tcp://client.local:4841",
                    Timeout = 7000,
                    MaxSessionCount = 2,
                    Enabled = true
                });
            });
            using ServiceProvider sp = services.BuildServiceProvider();

            OpcUaServerOptions opts = sp.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
            Assert.That(opts.ReverseConnect, Is.Not.Null);
            Assert.That(opts.ReverseConnect!.ConnectIntervalMs, Is.EqualTo(5000));
            Assert.That(opts.ReverseConnect.ConnectTimeoutMs, Is.EqualTo(10000));
            Assert.That(opts.ReverseConnect.RejectTimeoutMs, Is.EqualTo(20000));
            Assert.That(opts.ReverseConnect.Clients, Has.Count.EqualTo(1));
            ServerReverseConnectClientOptions client = opts.ReverseConnect.Clients[0];
            Assert.That(client.EndpointUrl, Is.EqualTo("opc.tcp://client.local:4841"));
            Assert.That(client.Timeout, Is.EqualTo(7000));
            Assert.That(client.MaxSessionCount, Is.EqualTo(2));
            Assert.That(client.Enabled, Is.True);
        }

        [Test]
        public void UserTokenPoliciesRoundTrip()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(o =>
            {
                o.UserTokenPolicies.Add(new OpcUaUserTokenPolicy
                {
                    TokenType = UserTokenType.UserName
                });
                o.UserTokenPolicies.Add(new OpcUaUserTokenPolicy
                {
                    TokenType = UserTokenType.Certificate
                });
            });
            using ServiceProvider sp = services.BuildServiceProvider();

            OpcUaServerOptions opts = sp.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
            Assert.That(opts.UserTokenPolicies, Has.Count.EqualTo(2));
            Assert.That(opts.UserTokenPolicies[0].TokenType, Is.EqualTo(UserTokenType.UserName));
            Assert.That(opts.UserTokenPolicies[1].TokenType, Is.EqualTo(UserTokenType.Certificate));
        }

        [Test]
        public void ExtendedTransportQuotasRoundTrip()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(o =>
            {
                o.MaxMessageSize = 8 * 1024 * 1024;
                o.OperationTimeoutMs = 120_000;
            });
            using ServiceProvider sp = services.BuildServiceProvider();

            OpcUaServerOptions opts = sp.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
            Assert.That(opts.MaxMessageSize, Is.EqualTo(8 * 1024 * 1024));
            Assert.That(opts.OperationTimeoutMs, Is.EqualTo(120_000));
        }

        [Test]
        public void EccPoliciesToggleRoundTrip()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(o => o.IncludeEccPolicies = true);
            using ServiceProvider sp = services.BuildServiceProvider();

            OpcUaServerOptions opts = sp.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
            Assert.That(opts.IncludeEccPolicies, Is.True);
        }

        [Test]
        public void OperationLimitsProjectsToOperationLimits()
        {
            var options = new OperationLimitsOptions
            {
                MaxNodesPerRead = 100,
                MaxNodesPerWrite = 200,
                MaxNodesPerBrowse = 300,
                MaxNodesPerMethodCall = 50,
                MaxMonitoredItemsPerCall = 1000
            };
            OperationLimits projected = ProjectionAccessor.Project(options);

            Assert.That(projected.MaxNodesPerRead, Is.EqualTo(100u));
            Assert.That(projected.MaxNodesPerWrite, Is.EqualTo(200u));
            Assert.That(projected.MaxNodesPerBrowse, Is.EqualTo(300u));
            Assert.That(projected.MaxNodesPerMethodCall, Is.EqualTo(50u));
            Assert.That(projected.MaxMonitoredItemsPerCall, Is.EqualTo(1000u));
        }

        [Test]
        public void RegistrationEndpointUrlRoundTrip()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(o =>
                o.RegistrationEndpointUrl = "opc.tcp://lds.local:4840");
            using ServiceProvider sp = services.BuildServiceProvider();

            OpcUaServerOptions opts = sp.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
            Assert.That(opts.RegistrationEndpointUrl, Is.EqualTo("opc.tcp://lds.local:4840"));
        }

        [Test]
        public void SecurityHardeningOverridesPersistThroughBinding()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(o =>
            {
                o.RejectSHA1Certificates = false;
                o.MinCertificateKeySize = 4096;
            });
            using ServiceProvider sp = services.BuildServiceProvider();

            OpcUaServerOptions opts = sp.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
            Assert.That(opts.RejectSHA1Certificates, Is.False);
            Assert.That(opts.MinCertificateKeySize, Is.EqualTo((ushort)4096));
        }

        /// <summary>
        /// Bridge to the internal <c>OperationLimitsOptions.ToOperationLimits</c>
        /// projection method (declared internal so it doesn't pollute the
        /// public API).
        /// </summary>
        private static class ProjectionAccessor
        {
            public static OperationLimits Project(OperationLimitsOptions options)
            {
                System.Reflection.MethodInfo method = typeof(OperationLimitsOptions)
                    .GetMethod(
                        "ToOperationLimits",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                    !;
                return (OperationLimits)method.Invoke(options, null)!;
            }
        }
    }
}
