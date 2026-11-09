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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// Covers how dependency injection applies the acknowledgement options of a managed session.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class AcknowledgementOptionsTests
    {
        /// <summary>
        /// A provider set explicitly next to confirmation mode is rejected, as when the same
        /// options are passed to <see cref="ManagedSession.CreateAsync"/>, instead of being dropped.
        /// </summary>
        [Test]
        public async Task ExplicitProviderWithConfirmationModeIsRejectedAsync()
        {
            await using ServiceProvider provider = CreateServices(new ManagedSessionOptions
            {
                Endpoint = CreateEndpoint(),
                AcknowledgementMode = AcknowledgementMode.AfterConfirmation,
                AcknowledgementProvider = new SendAllProvider()
            });
            ManagedSessionOptions options = provider.GetRequiredService<IOptions<OpcUaClientOptions>>().Value.Session;

            await Assert.ThatAsync(
                () => provider.ConnectManagedSessionAsync(options, _ => { }, CancellationToken.None),
                Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("provider"))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// A registered provider does not apply in confirmation mode; outside of it, the options take
        /// precedence over the registered provider.
        /// </summary>
        [TestCase(AcknowledgementMode.AfterConfirmation, false, false)]
        [TestCase(AcknowledgementMode.Immediate, false, true)]
        [TestCase(AcknowledgementMode.Immediate, true, true)]
        public async Task RegisteredProviderAppliesOnlyWithoutConfirmationAsync(
            AcknowledgementMode mode,
            bool explicitProvider,
            bool expectProvider)
        {
            var registered = new SendAllProvider();
            var configured = new SendAllProvider();
            await using ServiceProvider provider = CreateServices(
                new ManagedSessionOptions
                {
                    Endpoint = CreateEndpoint(),
                    AcknowledgementMode = mode,
                    AcknowledgementProvider = explicitProvider ? configured : null
                },
                services => services.AddSingleton<ISubscriptionAcknowledgementProvider>(registered));
            ManagedSessionOptions options = provider.GetRequiredService<IOptions<OpcUaClientOptions>>().Value.Session;
            ManagedSessionOptions? built = null;

            await Assert.ThatAsync(
                () => provider.ConnectManagedSessionAsync(options, builder =>
                {
                    built = builder.Build();
                    throw new InvalidOperationException("Stop before connecting.");
                }, CancellationToken.None),
                Throws.InvalidOperationException).ConfigureAwait(false);

            Assert.That(built, Is.Not.Null);
            Assert.That(built!.AcknowledgementMode, Is.EqualTo(mode));
            if (!expectProvider)
            {
                Assert.That(built.AcknowledgementProvider, Is.Null);
            }
            else
            {
                Assert.That(built.AcknowledgementProvider, Is.SameAs(explicitProvider ? configured : registered));
            }
        }

        private static ServiceProvider CreateServices(
            ManagedSessionOptions session,
            Action<IServiceCollection>? configure = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(client =>
            {
                client.Configuration = new ApplicationConfiguration(telemetry)
                {
                    ApplicationUri = "urn:test:acknowledgement-options",
                    ApplicationName = "acknowledgement-options",
                    ClientConfiguration = new ClientConfiguration()
                };
                client.Session = session;
            });
            configure?.Invoke(services);
            return services.BuildServiceProvider();
        }

        private static ConfiguredEndpoint CreateEndpoint()
        {
            return new ConfiguredEndpoint(null!, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            });
        }

        private sealed class SendAllProvider : ISubscriptionAcknowledgementProvider
        {
            public ValueTask<AcknowledgementDecision> DecideAsync(
                ArrayOf<AcknowledgementHandle> acknowledgements,
                CancellationToken ct = default)
            {
                return new ValueTask<AcknowledgementDecision>(AcknowledgementDecision.SendAll);
            }
        }
    }
}
