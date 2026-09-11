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
using System.Linq;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Bindings;
using UaLens.Connection;

namespace UaLens.Tests.Connection;

[TestFixture]
public sealed class ConnectionTransportConfigurationTests
{
    [TestCase("opc.tcp", true, true, false)]
    [TestCase("https", true, false, false)]
    [TestCase("opc.https", true, false, false)]
    [TestCase("wss", false, false, true)]
    [TestCase("opc.wss", false, false, true)]
    [TestCase("opc.wss+json", false, false, false)]
    [TestCase("opc.quic", false, false, false)]
    public void DefaultCapabilitiesDistinguishRegistrationDirectionAndListenerTls(
        string scheme,
        bool forward,
        bool reverse,
        bool tls)
    {
        var catalog = new ConnectionTransportCatalog();

        ConnectionTransportCapability capability = catalog.GetCapability(scheme);

        Assert.That(capability.Scheme, Is.EqualTo(scheme));
        Assert.That(capability.ForwardRegistered, Is.EqualTo(forward));
        Assert.That(capability.ReverseRegistered, Is.EqualTo(reverse));
        Assert.That(capability.RequiresListenerTls, Is.EqualTo(tls));
        Assert.That(capability.Prerequisites, Is.Not.Empty);
        if (forward)
        {
            Assert.That(() => catalog.RequireForward($"{scheme}://server.example.test:4840/Factory"), Throws.Nothing);
        }
    }

    [TestCase("wss", true, true, true)]
    [TestCase("wss", true, false, false)]
    [TestCase("wss", false, true, false)]
    [TestCase("wss", false, false, false)]
    [TestCase("opc.wss", true, true, true)]
    [TestCase("https", true, true, false)]
    [TestCase("opc.custom", true, true, false)]
    public void ConfiguredReverseCapabilityRequiresBothFactoriesAndASupportedDirection(
        string scheme,
        bool channel,
        bool listener,
        bool reverse)
    {
        var registry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);
        registry.Setup(value => value.HasChannelFactory(scheme)).Returns(channel);
        registry.Setup(value => value.HasListenerFactory(scheme)).Returns(listener);
        var catalog = new ConnectionTransportCatalog(registry.Object);

        ConnectionTransportCapability capability = catalog.GetCapability(scheme);

        Assert.That(capability.ForwardRegistered, Is.EqualTo(channel));
        Assert.That(capability.ReverseRegistered, Is.EqualTo(reverse));
        registry.Verify(value => value.CreateChannel(It.IsAny<string>(), It.IsAny<ITelemetryContext>()), Times.Never);
        registry.Verify(value => value.CreateListener(It.IsAny<string>(), It.IsAny<ITelemetryContext>()), Times.Never);
    }

    [Test]
    public void SelectedCustomSchemeUsesTheLiveHostRegistrationWithoutAnAdditionalSchemeHint()
    {
        var registry = new DefaultTransportBindingRegistry();
        var factory = new Mock<ITransportChannelFactory>(MockBehavior.Strict);
        factory.SetupGet(value => value.UriScheme).Returns("opc.custom");
        var catalog = new ConnectionTransportCatalog(registry);
        Assert.That(catalog.GetCapability("opc.custom").ForwardRegistered, Is.False);

        registry.RegisterChannelFactory(factory.Object);

        Assert.That(catalog.GetCapability("OPC.CUSTOM").ForwardRegistered, Is.True);
        Assert.That(() => catalog.RequireForward("opc.custom://server.example.test:4840/Factory"), Throws.Nothing);
        Assert.That(registry.RemoveChannelFactory("opc.custom"), Is.True);
        Assert.That(() => catalog.RequireForward("opc.custom://server.example.test:4840/Factory"),
            Throws.TypeOf<NotSupportedException>());
        factory.Verify(value => value.Create(It.IsAny<ITelemetryContext>()), Times.Never);
    }

    [Test]
    public void AdditionalSchemesAreNormalizedDeduplicatedAndSorted()
    {
        var catalog = new ConnectionTransportCatalog(
            new DefaultTransportBindingRegistry(), ["OPC.TCP", "opc.custom", "OPC.CUSTOM"]);

        string[] schemes = catalog.Capabilities.ToList().Select(value => value.Scheme).ToArray();

        Assert.That(schemes.Count(value => value == "opc.tcp"), Is.EqualTo(1));
        Assert.That(schemes.Count(value => value == "opc.custom"), Is.EqualTo(1));
        Assert.That(schemes, Is.Ordered.Using<string>(StringComparer.Ordinal));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("1tcp")]
    [TestCase("opc.tcp://server")]
    [TestCase("opc\\tcp")]
    public void InvalidSchemeNamesAreRejectedBeforeRegistrationLookup(string? scheme)
    {
        var registry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);

        Assert.That(() => new ConnectionTransportCatalog(registry.Object, [scheme!]), Throws.ArgumentException);
        Assert.That(() => new ConnectionTransportCatalog(registry.Object).GetCapability(scheme!),
            Throws.ArgumentException);
        registry.VerifyNoOtherCalls();
    }

    [TestCase(128, true)]
    [TestCase(129, false)]
    public void AdditionalSchemeNameLengthHasAnExactBound(int length, bool valid)
    {
        string scheme = new('x', length);
        if (valid)
        {
            var catalog = new ConnectionTransportCatalog(new DefaultTransportBindingRegistry(), [scheme]);
            Assert.That(catalog.Capabilities.ToList().Select(value => value.Scheme), Does.Contain(scheme));
        }
        else
        {
            Assert.That(() => new ConnectionTransportCatalog(new DefaultTransportBindingRegistry(), [scheme]),
                Throws.ArgumentException);
        }
    }

    [TestCase("")]
    [TestCase("relative/Factory")]
    [TestCase("urn:server")]
    [TestCase("opc.tcp://operator@server.example.test:4840/Factory")]
    [TestCase("opc.tcp://server.example.test:4840/Factory?extra=value")]
    [TestCase("opc.tcp://server.example.test:4840/Factory#fragment")]
    [TestCase("opc.tcp://server.example.test:0/Factory")]
    [TestCase("opc.tcp://server.example.test:4840/Fac tory")]
    [TestCase("opc.tcp://server.example.test:4840/Factory\n")]
    public void InvalidForwardEndpointNeverReachesAFactory(string url)
    {
        var registry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);
        var catalog = new ConnectionTransportCatalog(registry.Object);

        Assert.That(() => catalog.RequireForward(url), Throws.ArgumentException);

        registry.VerifyNoOtherCalls();
    }

    [Test]
    public void UnknownForwardSchemeReportsUnregisteredInsteadOfSelectingTcp()
    {
        var catalog = new ConnectionTransportCatalog();

        Assert.That(() => catalog.RequireForward("opc.unknown://server.example.test:4840/Factory"),
            Throws.TypeOf<NotSupportedException>());
        Assert.That(catalog.GetCapability("opc.unknown").ForwardRegistered, Is.False);
        Assert.That(catalog.GetCapability("opc.unknown").ReverseRegistered, Is.False);
    }

    [TestCase(2048, true)]
    [TestCase(2049, false)]
    public void EndpointUrlLengthHasAnExactBound(int length, bool valid)
    {
        const string prefix = "opc.tcp://server.example.test:4840/";
        string url = prefix + new string('a', length - prefix.Length);
        var catalog = new ConnectionTransportCatalog();

        if (valid)
        {
            Assert.That(() => catalog.RequireForward(url), Throws.Nothing);
        }
        else
        {
            Assert.That(() => catalog.RequireForward(url), Throws.ArgumentException);
        }
    }

    [TestCase(1, 1)]
    [TestCase(300, 60)]
    public void ReverseConfigurationPreservesExactListenerAndTimingBounds(int wait, int hold)
    {
        ReverseConnectionProfile profile = Profile() with { WaitTimeoutSeconds = wait, HoldTimeSeconds = hold };

        ReverseConnectClientConfiguration configuration = profile.CreateConfiguration();

        Assert.That(configuration.ClientEndpoints, Has.Count.EqualTo(1));
        Assert.That(configuration.ClientEndpoints[0].EndpointUrl, Is.EqualTo(profile.ListenerUrl));
        Assert.That(configuration.WaitTimeout, Is.EqualTo(wait * 1000));
        Assert.That(configuration.HoldTime, Is.EqualTo(hold * 1000));
    }

    [TestCase(0, 15)]
    [TestCase(301, 15)]
    [TestCase(20, 0)]
    [TestCase(20, 61)]
    public void ReverseConfigurationRejectsImmediatelyOutsideTimingBounds(int wait, int hold)
    {
        ReverseConnectionProfile profile = Profile() with { WaitTimeoutSeconds = wait, HoldTimeSeconds = hold };

        Assert.That(() => profile.CreateConfiguration(), Throws.ArgumentException);
    }

    [TestCase("https://localhost:4841/client", "https://server.example.test:4840/Factory")]
    [TestCase("opc.tcp://localhost:4841/client", "wss://server.example.test:4840/Factory")]
    [TestCase("wss://localhost:4841/client", "opc.wss://server.example.test:4840/Factory")]
    [TestCase("opc.tcp://localhost/client", "opc.tcp://server.example.test:4840/Factory")]
    public void ReverseConfigurationRejectsUnsupportedDirectionSchemeAliasesAndMissingPorts(
        string listener,
        string endpoint)
    {
        ReverseConnectionProfile profile = Profile() with
        {
            ListenerUrl = listener, EndpointUrl = endpoint, TlsConfigurationId = "listener-tls"
        };

        Assert.That(() => profile.Validate(), Throws.ArgumentException);
    }

    private static ReverseConnectionProfile Profile()
    {
        return new ReverseConnectionProfile
        {
            ListenerUrl = "opc.tcp://localhost:4841/client",
            EndpointUrl = "opc.tcp://server.example.test:4840/Factory",
            ServerUri = "urn:expected-server"
        };
    }
}
