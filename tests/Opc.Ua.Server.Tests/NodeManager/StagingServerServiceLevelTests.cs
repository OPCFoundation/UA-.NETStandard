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
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies that candidate facades cannot claim or publish service level outside their published lifetime.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    public sealed class StagingServerServiceLevelTests
    {
        /// <summary>
        /// Verifies that private and aborted candidates never acquire the serving server's lifetime claim.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UnpublishedCandidateCannotClaimServiceLevel(bool detached)
        {
            Mock<IServerInternal> server = CreateServer();
            Mock<IServerServiceLevelControl> control = server.As<IServerServiceLevelControl>();
            using ChangePreparer.StagingServer facade = new ChangePreparer(server.Object).CreateServer();
            if (detached)
            {
                facade.Detach();
            }
            IServerInternal supplied = facade;

            Assert.That(supplied, Is.InstanceOf<IServerServiceLevelControl>());
            Assert.That(
                () => ((IServerServiceLevelControl)supplied).ClaimServiceLevelControl(),
                Throws.TypeOf<InvalidOperationException>());
            control.Verify(value => value.ClaimServiceLevelControl(), Times.Never);
        }

        /// <summary>
        /// Verifies that publication forwards one claim and detachment revokes the facade's returned writer.
        /// </summary>
        [Test]
        public void PublishedCandidateWriterRejectsPublicationAfterDetachment()
        {
            Mock<IServerInternal> server = CreateServer();
            var publications = new System.Collections.Generic.List<byte>();
            Mock<IServerServiceLevelControl> control = server.As<IServerServiceLevelControl>();
            control.Setup(value => value.ClaimServiceLevelControl())
                .Returns((Action<byte>)publications.Add);
            using ChangePreparer.StagingServer facade = new ChangePreparer(server.Object).CreateServer();
            facade.Bind();
            IServerInternal supplied = facade;

            Assert.That(supplied, Is.InstanceOf<IServerServiceLevelControl>());
            Action<byte> publish = ((IServerServiceLevelControl)supplied).ClaimServiceLevelControl();
            publish(123);
            Assert.That(publications, Is.EqualTo(new byte[] { 123 }));
            control.Verify(value => value.ClaimServiceLevelControl(), Times.Once);

            facade.Detach();

            Assert.That(() => publish(234), Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => ((IServerServiceLevelControl)supplied).ClaimServiceLevelControl(),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(publications, Is.EqualTo(new byte[] { 123 }));
            control.Verify(value => value.ClaimServiceLevelControl(), Times.Once);
        }

        /// <summary>
        /// A replacement generation can claim service-level publication after the retired owner is revoked.
        /// </summary>
        [Test]
        public void RetiredCandidateReleasesServiceLevelClaimForReplacement()
        {
            Mock<IServerInternal> server = CreateServer();
            Mock<IServerServiceLevelControl> control = server.As<IServerServiceLevelControl>();
            Mock<IServerServiceLevelControlRevocation> revocation =
                server.As<IServerServiceLevelControlRevocation>();
            var oldPublications = new List<byte>();
            var newPublications = new List<byte>();
            Action<byte> oldWriter = oldPublications.Add;
            Action<byte> newWriter = newPublications.Add;
            control.SetupSequence(value => value.ClaimServiceLevelControl())
                .Returns(oldWriter)
                .Returns(newWriter);
            using ChangePreparer.StagingServer oldFacade = new ChangePreparer(server.Object).CreateServer();
            oldFacade.Bind();
            Action<byte> oldFacadeWriter =
                ((IServerServiceLevelControl)(IServerInternal)oldFacade).ClaimServiceLevelControl();
            oldFacadeWriter(11);

            oldFacade.RevokeServiceLevelControl();

            Assert.That(() => oldFacadeWriter(12), Throws.TypeOf<InvalidOperationException>());
            using ChangePreparer.StagingServer newFacade = new ChangePreparer(server.Object).CreateServer();
            newFacade.Bind();
            Action<byte> replacementWriter =
                ((IServerServiceLevelControl)(IServerInternal)newFacade).ClaimServiceLevelControl();
            replacementWriter(21);

            Assert.That(oldPublications, Is.EqualTo(new byte[] { 11 }));
            Assert.That(newPublications, Is.EqualTo(new byte[] { 21 }));
            revocation.Verify(value => value.ReleaseServiceLevelControl(oldWriter), Times.Once);
            control.Verify(value => value.ClaimServiceLevelControl(), Times.Exactly(2));
        }

        /// <summary>
        /// Verifies that an absent serving capability fails explicitly after publication.
        /// </summary>
        [Test]
        public void PublishedCandidateReportsUnsupportedServiceLevelControl()
        {
            Mock<IServerInternal> server = CreateServer();
            using ChangePreparer.StagingServer facade = new ChangePreparer(server.Object).CreateServer();
            facade.Bind();
            IServerInternal supplied = facade;

            Assert.That(supplied, Is.InstanceOf<IServerServiceLevelControl>());
            Assert.That(
                () => ((IServerServiceLevelControl)supplied).ClaimServiceLevelControl(),
                Throws.TypeOf<NotSupportedException>());
        }

        /// <summary>
        /// Verifies that the facade preserves the serving server's exclusive-ownership exception.
        /// </summary>
        [Test]
        public void PublishedCandidatePreservesServiceLevelClaimFailure()
        {
            Mock<IServerInternal> server = CreateServer();
            var expected = new InvalidOperationException("Service level already has an owner.");
            Mock<IServerServiceLevelControl> control = server.As<IServerServiceLevelControl>();
            control.Setup(value => value.ClaimServiceLevelControl()).Throws(expected);
            using ChangePreparer.StagingServer facade = new ChangePreparer(server.Object).CreateServer();
            facade.Bind();
            IServerInternal supplied = facade;

            Assert.That(supplied, Is.InstanceOf<IServerServiceLevelControl>());
            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                () => ((IServerServiceLevelControl)supplied).ClaimServiceLevelControl());
            Assert.That(actual, Is.SameAs(expected));
            control.Verify(value => value.ClaimServiceLevelControl(), Times.Once);
        }

        /// <summary>
        /// Verifies that non-metadata providers preserve serving identities and observe later configuration.
        /// </summary>
        [TestCase("private")]
        [TestCase("published")]
        [TestCase("detached")]
        public void CandidateForwardsCurrentNonMetadataProviders(string state)
        {
            Mock<IServerInternal> server = CreateServer();
            ArrayOf<EndpointDescription> endpoints = [new EndpointDescription { EndpointUrl = "opc.tcp://first" }];
            IReadOnlyList<ITransportListener> listeners = [new Mock<ITransportListener>().Object];
            ISecurityPolicyRegistry policies = new Mock<ISecurityPolicyRegistry>().Object;
            ICertificateValidatorEx validator = new Mock<ICertificateValidatorEx>().Object;
            IHistoryContinuationPointStore store = new Mock<IHistoryContinuationPointStore>().Object;
            server.As<IServerEndpointRegistryProvider>().SetupGet(value => value.ServerEndpoints)
                .Returns(() => endpoints);
            server.As<ITransportListenerRegistryProvider>().SetupGet(value => value.TransportListeners)
                .Returns(() => listeners);
            server.As<ISecurityPolicyRegistryProvider>().SetupGet(value => value.SecurityPolicyRegistry)
                .Returns(() => policies);
            server.As<ICertificateValidatorProvider>().SetupGet(value => value.CertificateValidator)
                .Returns(() => validator);
            server.As<IHistoryContinuationPointStoreProvider>().SetupGet(value => value.HistoryContinuationPointStore)
                .Returns(() => store);
            using ChangePreparer.StagingServer facade = new ChangePreparer(server.Object).CreateServer();
            if (state != "private")
            {
                facade.Bind();
            }
            if (state == "detached")
            {
                facade.Detach();
            }
            IServerInternal supplied = facade;

            Assert.That(supplied, Is.InstanceOf<IServerEndpointRegistryProvider>());
            Assert.That(supplied, Is.InstanceOf<ITransportListenerRegistryProvider>());
            Assert.That(supplied, Is.InstanceOf<ISecurityPolicyRegistryProvider>());
            Assert.That(supplied, Is.InstanceOf<ICertificateValidatorProvider>());
            Assert.That(supplied, Is.InstanceOf<IHistoryContinuationPointStoreProvider>());
            Assert.That(((IServerEndpointRegistryProvider)supplied).ServerEndpoints[0], Is.SameAs(endpoints[0]));
            Assert.That(((ITransportListenerRegistryProvider)supplied).TransportListeners, Is.SameAs(listeners));
            Assert.That(((ISecurityPolicyRegistryProvider)supplied).SecurityPolicyRegistry, Is.SameAs(policies));
            Assert.That(((ICertificateValidatorProvider)supplied).CertificateValidator, Is.SameAs(validator));
            Assert.That(((IHistoryContinuationPointStoreProvider)supplied).HistoryContinuationPointStore,
                Is.SameAs(store));

            endpoints = [new EndpointDescription { EndpointUrl = "opc.tcp://second" }];
            listeners = [new Mock<ITransportListener>().Object];
            policies = new Mock<ISecurityPolicyRegistry>().Object;
            validator = new Mock<ICertificateValidatorEx>().Object;
            store = new Mock<IHistoryContinuationPointStore>().Object;

            Assert.That(((IServerEndpointRegistryProvider)supplied).ServerEndpoints[0], Is.SameAs(endpoints[0]));
            Assert.That(((ITransportListenerRegistryProvider)supplied).TransportListeners, Is.SameAs(listeners));
            Assert.That(((ISecurityPolicyRegistryProvider)supplied).SecurityPolicyRegistry, Is.SameAs(policies));
            Assert.That(((ICertificateValidatorProvider)supplied).CertificateValidator, Is.SameAs(validator));
            Assert.That(((IHistoryContinuationPointStoreProvider)supplied).HistoryContinuationPointStore,
                Is.SameAs(store));
        }

        /// <summary>
        /// Verifies that absent serving providers retain the optional interfaces' empty or default results.
        /// </summary>
        [Test]
        public void CandidatePreservesAbsentNonMetadataProviderResults()
        {
            Mock<IServerInternal> server = CreateServer();
            using ChangePreparer.StagingServer facade = new ChangePreparer(server.Object).CreateServer();
            IServerInternal supplied = facade;

            Assert.That(supplied, Is.InstanceOf<IServerEndpointRegistryProvider>());
            Assert.That(supplied, Is.InstanceOf<ITransportListenerRegistryProvider>());
            Assert.That(supplied, Is.InstanceOf<ISecurityPolicyRegistryProvider>());
            Assert.That(supplied, Is.InstanceOf<ICertificateValidatorProvider>());
            Assert.That(supplied, Is.InstanceOf<IHistoryContinuationPointStoreProvider>());
            Assert.That(((IServerEndpointRegistryProvider)supplied).ServerEndpoints.Count, Is.Zero);
            Assert.That(((ITransportListenerRegistryProvider)supplied).TransportListeners, Is.Empty);
            Assert.That(((ISecurityPolicyRegistryProvider)supplied).SecurityPolicyRegistry,
                Is.SameAs(SecurityPolicies.Default));
            Assert.That(((ICertificateValidatorProvider)supplied).CertificateValidator, Is.Null);
            Assert.That(((IHistoryContinuationPointStoreProvider)supplied).HistoryContinuationPointStore, Is.Null);
        }

        /// <summary>
        /// Creates metadata sufficient for a facade without creating the mock proxy before optional interfaces.
        /// </summary>
        private static Mock<IServerInternal> CreateServer()
        {
            var server = new Mock<IServerInternal>();
            var namespaces = new NamespaceTable();
            namespaces.Append(DeterministicServerMock.TestNamespaceUri);
            var serverUris = new StringTable();
            IEncodeableFactory factory = EncodeableFactory.Create();
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(serverUris);
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
            server.SetupGet(value => value.Factory).Returns(factory);
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.SetupGet(value => value.MessageContext).Returns(new ServiceMessageContext(telemetry, factory)
            {
                NamespaceUris = namespaces,
                ServerUris = serverUris
            });
            server.SetupGet(value => value.DefaultSystemContext)
                .Returns(() => new ServerSystemContext(server.Object));
            return server;
        }
    }
}
