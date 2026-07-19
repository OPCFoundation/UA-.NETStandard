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

#nullable enable

using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// Directly exercises the internal static
    /// <see cref="OpcUaServerApplicationConfigurationFeature.Configure"/>
    /// projection for the <see cref="OpcUaServerOptions"/> knobs that are
    /// not otherwise exercised by the hosted-service integration tests
    /// (transport quotas, ECC policies, operation limits, reverse connect,
    /// and the LDS/GDS registration endpoint).
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("Hosting")]
    [Parallelizable]
    public sealed class OpcUaServerApplicationConfigurationFeatureTests
    {
        [Test]
        public void ConfigureAppliesMaxMessageSizeAndOperationTimeoutWhenSet()
        {
            var options = new OpcUaServerOptions
            {
                MaxMessageSize = 123456,
                OperationTimeoutMs = 7890
            };
            options.EndpointUrls.Add("opc.tcp://localhost:0/MaxMessageSize");

            ApplicationConfiguration configuration = Configure(options);

            Assert.That(configuration.TransportQuotas!.MaxMessageSize, Is.EqualTo(123456));
            Assert.That(configuration.TransportQuotas!.OperationTimeout, Is.EqualTo(7890));
        }

        [Test]
        public void ConfigureLeavesTransportQuotaDefaultsWhenNotSet()
        {
            var options = new OpcUaServerOptions();
            options.EndpointUrls.Add("opc.tcp://localhost:0/DefaultQuotas");

            ApplicationConfiguration configuration = Configure(options);

            // The stack default (not the sentinel values used above) is kept.
            Assert.That(configuration.TransportQuotas!.MaxMessageSize, Is.Not.EqualTo(123456));
            Assert.That(configuration.TransportQuotas!.OperationTimeout, Is.Not.EqualTo(7890));
        }

        [Test]
        public void ConfigureAddsEccPoliciesWhenRequested()
        {
            var options = new OpcUaServerOptions { IncludeEccPolicies = true };
            options.EndpointUrls.Add("opc.tcp://localhost:0/Ecc");

            ApplicationConfiguration configuration = Configure(options);

            Assert.That(configuration.ServerConfiguration!.SecurityPolicies, Is.Not.Empty);
        }

        [Test]
        public void ConfigureAppliesOperationLimitsWhenSet()
        {
            var options = new OpcUaServerOptions
            {
                OperationLimits = new OperationLimitsOptions
                {
                    MaxNodesPerRead = 111,
                    MaxNodesPerBrowse = 222,
                    MaxMonitoredItemsPerCall = 333
                }
            };
            options.EndpointUrls.Add("opc.tcp://localhost:0/OperationLimits");

            ApplicationConfiguration configuration = Configure(options);

            Assert.That(configuration.ServerConfiguration!.OperationLimits, Is.Not.Null);
            Assert.That(
                configuration.ServerConfiguration!.OperationLimits.MaxNodesPerRead,
                Is.EqualTo(111u));
            Assert.That(
                configuration.ServerConfiguration!.OperationLimits.MaxNodesPerBrowse,
                Is.EqualTo(222u));
            Assert.That(
                configuration.ServerConfiguration!.OperationLimits.MaxMonitoredItemsPerCall,
                Is.EqualTo(333u));
        }

        [Test]
        public void ConfigureAppliesReverseConnectWhenSet()
        {
            var options = new OpcUaServerOptions
            {
                ReverseConnect = new ServerReverseConnectOptions
                {
                    ConnectIntervalMs = 1000,
                    ConnectTimeoutMs = 2000,
                    RejectTimeoutMs = 3000
                }
            };
            options.ReverseConnect.Clients.Add(new ServerReverseConnectClientOptions
            {
                EndpointUrl = "opc.tcp://reverse-client:4841",
                Timeout = 500,
                MaxSessionCount = 2,
                Enabled = true
            });
            options.EndpointUrls.Add("opc.tcp://localhost:0/ReverseConnect");

            ApplicationConfiguration configuration = Configure(options);

            ReverseConnectServerConfiguration? maybeReverseConnect =
                configuration.ServerConfiguration!.ReverseConnect;
            Assert.That(maybeReverseConnect, Is.Not.Null);
            ReverseConnectServerConfiguration reverseConnect = maybeReverseConnect!;
            Assert.That(reverseConnect.ConnectInterval, Is.EqualTo(1000));
            Assert.That(reverseConnect.ConnectTimeout, Is.EqualTo(2000));
            Assert.That(reverseConnect.RejectTimeout, Is.EqualTo(3000));
            Assert.That(reverseConnect.Clients.Count, Is.EqualTo(1));
            Assert.That(reverseConnect.Clients[0].EndpointUrl, Is.EqualTo("opc.tcp://reverse-client:4841"));
            Assert.That(reverseConnect.Clients[0].Timeout, Is.EqualTo(500));
            Assert.That(reverseConnect.Clients[0].MaxSessionCount, Is.EqualTo(2));
            Assert.That(reverseConnect.Clients[0].Enabled, Is.True);
        }

        [Test]
        public void ConfigureAppliesRegistrationEndpointUrlWhenSet()
        {
            var options = new OpcUaServerOptions
            {
                RegistrationEndpointUrl = "opc.tcp://lds.example.com:4840"
            };
            options.EndpointUrls.Add("opc.tcp://localhost:0/Registration");

            ApplicationConfiguration configuration = Configure(options);

            Assert.That(
                configuration.ServerConfiguration!.RegistrationEndpoint?.EndpointUrl,
                Is.EqualTo("opc.tcp://lds.example.com:4840"));
        }

        [Test]
        public void ConfigureLeavesRegistrationEndpointNullWhenNotSet()
        {
            var options = new OpcUaServerOptions();
            options.EndpointUrls.Add("opc.tcp://localhost:0/NoRegistration");

            ApplicationConfiguration configuration = Configure(options);

            Assert.That(configuration.ServerConfiguration!.RegistrationEndpoint, Is.Null);
        }

        private static ApplicationConfiguration Configure(OpcUaServerOptions options)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(isServer: true);
            var appInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = options.ApplicationName
            };
            IApplicationConfigurationBuilderTypes builder = appInstance.Build(
                "urn:localhost:OpcUaServerApplicationConfigurationFeatureTests",
                "uri:opcfoundation.org:OpcUaServerApplicationConfigurationFeatureTests");

            OpcUaServerApplicationConfigurationFeature.Configure(builder, options);

            return appInstance.ApplicationConfiguration!;
        }
    }
}
