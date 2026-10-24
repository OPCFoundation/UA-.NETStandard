// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Core.DataChannels.Tests.ClientServer
{
    /// <summary>
    /// Verifies Part 31/32 endpoint selection independently of optional PubSub and direct capabilities.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class ReliableBaselineEndpointTests
    {
        /// <summary>
        /// Selects the protected Services baseline without manufacturing a Data Channel context.
        /// </summary>
        [TestCase(DataChannelCarrierKind.QuicUasc)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc)]
        public void ServicesBaselineDoesNotRequireAPubSubFactory(DataChannelCarrierKind kind)
        {
            EndpointDescription endpoint = CreateEndpoint(kind);
            Assert.That(UascDataChannelEndpoint.Validate(
                new Uri(endpoint.EndpointUrl!), endpoint, null, allowIntegrity: false), Is.Null);
        }

        /// <summary>
        /// Binds an explicitly configured query instead of discarding or prohibiting its resource selection.
        /// </summary>
        [TestCase(DataChannelCarrierKind.QuicUasc)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc)]
        public void ConfiguredBaselineQueryIsPartOfExactEndpointSelection(DataChannelCarrierKind kind)
        {
            EndpointDescription endpoint = CreateEndpoint(kind);
            endpoint.EndpointUrl += "?tenant=configured";
            Assert.That(UascDataChannelEndpoint.Validate(
                new Uri(endpoint.EndpointUrl), endpoint, null, allowIntegrity: false), Is.Null);
            var changed = new UriBuilder(endpoint.EndpointUrl) { Query = "tenant=foreign" };
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                UascDataChannelEndpoint.Validate(changed.Uri, endpoint, null, allowIntegrity: false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        /// <summary>
        /// Preserves native carrier identity when configured PubSub is explicitly selected on the baseline.
        /// </summary>
        [TestCase(DataChannelCarrierKind.QuicUasc)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc)]
        public void ConfiguredPubSubKeepsNativeBaselineIdentity(DataChannelCarrierKind kind)
        {
            EndpointDescription endpoint = CreateEndpoint(kind);
            var contexts = new Mock<IDataChannelContextFactory>(MockBehavior.Strict);
            Assert.That(UascDataChannelEndpoint.Validate(
                new Uri(endpoint.EndpointUrl!), endpoint, contexts.Object, allowIntegrity: false), Is.EqualTo(kind));
            contexts.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Refuses native baseline profile selection on a generic or differently selected transport factory.
        /// </summary>
        [TestCase(DataChannelCarrierKind.QuicUasc)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc)]
        public void NativeProfileCannotTurnAnUnnegotiatedStreamIntoANativeTransport(DataChannelCarrierKind kind)
        {
            EndpointDescription endpoint = CreateEndpoint(kind);
            var unselected = new Mock<IUaSCByteTransportFactory>(MockBehavior.Strict);
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                UascDataChannelEndpoint.ValidateTransportProfile(endpoint.TransportProfileUri, unselected.Object))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            unselected.VerifyNoOtherCalls();

            var mismatched = new Mock<IUaSCByteTransportFactory>(MockBehavior.Strict);
            mismatched.As<IUaSCTransportProfile>()
                .SetupGet(value => value.TransportProfileUri)
                .Returns(kind == DataChannelCarrierKind.QuicUasc
                    ? DataChannelProfiles.UascWebTransport : DataChannelProfiles.UascQuic);
            exception = Assert.Throws<ServiceResultException>(() =>
                UascDataChannelEndpoint.ValidateTransportProfile(endpoint.TransportProfileUri, mismatched.Object))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));

            var selected = new Mock<IUaSCByteTransportFactory>(MockBehavior.Strict);
            selected.As<IUaSCTransportProfile>()
                .SetupGet(value => value.TransportProfileUri).Returns(endpoint.TransportProfileUri!);
            Assert.DoesNotThrow(() =>
                UascDataChannelEndpoint.ValidateTransportProfile(endpoint.TransportProfileUri, selected.Object));
        }

        /// <summary>
        /// Refuses an unprotected or integrity-only baseline regardless of the legacy integrity opt-in.
        /// </summary>
        [TestCase(DataChannelCarrierKind.QuicUasc, MessageSecurityMode.None)]
        [TestCase(DataChannelCarrierKind.QuicUasc, MessageSecurityMode.Sign)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc, MessageSecurityMode.None)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc, MessageSecurityMode.Sign)]
        public void BaselineAlwaysRequiresSignAndEncrypt(
            DataChannelCarrierKind kind, MessageSecurityMode mode)
        {
            EndpointDescription endpoint = CreateEndpoint(kind);
            endpoint.SecurityMode = mode;
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                UascDataChannelEndpoint.Validate(
                    new Uri(endpoint.EndpointUrl!), endpoint, null, allowIntegrity: true))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeRejected));
        }

        /// <summary>
        /// Rejects policy None even when the advertised mode claims confidential protection.
        /// </summary>
        [TestCase(DataChannelCarrierKind.QuicUasc)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc)]
        public void BaselineRequiresAnAuthenticatedUascPolicy(DataChannelCarrierKind kind)
        {
            EndpointDescription endpoint = CreateEndpoint(kind);
            endpoint.SecurityPolicyUri = SecurityPolicies.None;
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                UascDataChannelEndpoint.Validate(
                    new Uri(endpoint.EndpointUrl!), endpoint, null, allowIntegrity: false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
        }

        /// <summary>
        /// Refuses every endpoint substitution before opening a stream or accepting UASC authentication.
        /// </summary>
        [Test]
        [Combinatorial]
        public void BaselineEndpointRequiresExactConfiguredAuthorityAndResource(
            [Values(DataChannelCarrierKind.QuicUasc, DataChannelCarrierKind.WebTransportUasc)]
            DataChannelCarrierKind kind,
            [Values("authority", "port", "resource", "scheme", "credentials", "query", "fragment")] string changed)
        {
            EndpointDescription endpoint = CreateEndpoint(kind);
            var url = new UriBuilder(endpoint.EndpointUrl!);
            switch (changed)
            {
                case "authority":
                    url.Host = "foreign.example";
                    break;
                case "port":
                    url.Port++;
                    break;
                case "resource":
                    url.Path += "/other";
                    break;
                case "scheme":
                    url.Scheme = "opc.tcp";
                    break;
                case "credentials":
                    url.UserName = "foreign";
                    break;
                case "query":
                    url.Query = "different=true";
                    break;
                case "fragment":
                    url.Fragment = "different";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(changed));
            }
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                UascDataChannelEndpoint.Validate(url.Uri, endpoint, null, allowIntegrity: false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        /// <summary>
        /// Builds an explicitly selected endpoint without Session, model or direct-delivery configuration.
        /// </summary>
        private static EndpointDescription CreateEndpoint(DataChannelCarrierKind kind)
        {
            bool quic = kind == DataChannelCarrierKind.QuicUasc;
            return new EndpointDescription
            {
                EndpointUrl = $"{(quic ? "opc.quic" : "https")}://device.example:4840/uasc",
                TransportProfileUri = quic ? DataChannelProfiles.UascQuic : DataChannelProfiles.UascWebTransport,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
        }
    }
}
