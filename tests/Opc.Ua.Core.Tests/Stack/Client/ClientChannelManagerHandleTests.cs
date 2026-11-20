/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    /// <summary>
    /// Certificate handle ownership on the channel creation path.
    /// </summary>
    /// <remarks>
    /// The creation helpers take over the caller's client-certificate handles.
    /// A channel that never opens has to release them exactly once: not at all
    /// leaks a handle per failed connect attempt, and twice invalidates the
    /// handle its owner is still using. The certificate was also only released
    /// on failures raised after the channel object existed, so a failure before
    /// that - an unsupported transport profile, for instance - left the handles
    /// behind entirely.
    /// </remarks>
    [TestFixture]
    [Category("ClientChannelManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ClientChannelManagerHandleTests
    {
        /// <summary>
        /// The scheme has no transport binding, so the channel is never built
        /// and the failure is raised before anything downstream could take the
        /// handles over.
        /// </summary>
        [Test]
        public void CreateChannelReleasesTheCertificateWhenTheSchemeIsUnsupported()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            Certificate owner = CertificateBuilder
                .Create("CN=ChannelManagerHandle")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // what the caller hands over: a reference of its own, which the
            // creation path owns from here on.
            Certificate handedOver = owner.AddRef();

            var description = new EndpointDescription
            {
                EndpointUrl = "unsupported.scheme://localhost:4840",
                TransportProfileUri = "urn:unknown:transport",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };

            Assert.That(
                async () => await ClientChannelManager.CreateUaBinaryChannelAsync(
                    configuration: null!,
                    description,
                    EndpointConfiguration.Create(),
                    handedOver,
                    clientCertificateChain: null,
                    ServiceMessageContext.CreateEmpty(telemetry),
                    ct: CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.Exception);

            // Exactly one release. The caller's own reference still holds the
            // certificate open - two releases would already have torn it down -
            // and giving that one up is the last, so the handle is gone
            // afterwards rather than outliving everyone who knows about it.
            Assert.That(owner.RawData, Is.Not.Null);

            owner.Dispose();

            Assert.That(() => owner.AddRef(), Throws.TypeOf<ObjectDisposedException>());
        }
    }
}
