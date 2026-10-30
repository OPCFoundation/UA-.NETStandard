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
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.AsyncServices
{
    /// <summary>
    /// Tests stable-owner admission in existing pools, not certificate authentication or a new RSE quota.
    /// </summary>
    [TestFixture]
    [Category("AsyncServices")]
    [Parallelizable]
    public sealed class RecoverableOwnerResourceTests
    {
        [OneTimeSetUp]
        public void CreateSyntheticCertificates()
        {
            m_original = CertificateBuilder.Create("CN=OwnerResourceSynthetic")
                .SetRSAKeySize(CertificateFactory.DefaultKeySize).CreateForRSA();
            m_alias = CertificateBuilder.Create("CN=OwnerResourceSyntheticAlias")
                .SetRSAKeySize(CertificateFactory.DefaultKeySize).CreateForRSA();
        }

        [OneTimeTearDown]
        public void DisposeSyntheticCertificates()
        {
            m_alias.Dispose();
            m_original.Dispose();
        }

        [TestCase(ResourceIsolationStage.RequestExecution)]
        [TestCase(ResourceIsolationStage.ParkedRequest)]
        [TestCase(ResourceIsolationStage.RequestQueueBytes)]
        public async Task ReplacementSessionsAndCertificateAliasesShareOneHardCeiling(ResourceIsolationStage stage)
        {
            var mapping = new ConfiguredRecoverableExecutionOwnerProvider(
                s_authority,
                [
                    new RecoverableExecutionApplicationMapping(s_application, Digest(m_original)),
                    new RecoverableExecutionApplicationMapping(s_application, Digest(m_alias))
                ],
                [new RecoverableExecutionUserMapping(s_user, "urn:synthetic:issuer", "operator")]);
            using OperationContext first = ValidatedContext(m_original, "session-1");
            using OperationContext replacement = ValidatedContext(m_original, "session-2");
            using OperationContext alias = ValidatedContext(m_alias, "session-3");
            RecoverableExecutionOwner originalOwner = await mapping.ResolveAsync(first).ConfigureAwait(false);
            RecoverableExecutionOwner replacementOwner = await mapping.ResolveAsync(replacement).ConfigureAwait(false);
            RecoverableExecutionOwner aliasOwner = await mapping.ResolveAsync(alias).ConfigureAwait(false);
            Assert.That(originalOwner.Id,
                Is.EqualTo(new RecoverableExecutionOwnerId(s_authority, s_application, s_user)));
            Assert.That(replacementOwner.Id, Is.EqualTo(originalOwner.Id));
            Assert.That(aliasOwner.Id, Is.EqualTo(originalOwner.Id));
            long unit = stage == ResourceIsolationStage.RequestQueueBytes ? 10 : 1;
            ServerResourceIsolationOptions options = Options();
            options.Stages[(int)stage].OwnerHardLimit = 2 * unit;
            using DefaultServerResourceIsolationProvider provider = Provider(options);
            ResourceIsolationOwner original = provider.ClassifyRecoverableOwner(originalOwner.Id);
            ResourceIsolationOwner renewed = provider.ClassifyRecoverableOwner(replacementOwner.Id);
            ResourceIsolationOwner aliased = provider.ClassifyRecoverableOwner(aliasOwner.Id);

            Assert.That(original.Key, Is.EqualTo(renewed.Key).And.EqualTo(aliased.Key));
            Assert.That(original.Key, Does.StartWith("rse:").And.Not.Contain(s_authority.ToString())
                .And.Not.Contain(s_application.ToString()).And.Not.Contain(s_user.ToString()));
            Assert.That(original.Class, Is.EqualTo(ResourceIsolationClass.Established));
            Assert.That(aliasOwner.ContinuityContext, Is.Not.EqualTo(originalOwner.ContinuityContext),
                "Different certificate evidence must still share the accounting key.");
            using IDisposable firstLease = Acquire(provider, stage, original, unit);
            using IDisposable secondLease = Acquire(provider, stage, renewed, unit);
            AssertRejected(provider, stage, aliased, unit, ResourceIsolationFailureReason.OwnerLimit);
            Assert.That(provider.GetUsage(stage), Is.EqualTo(2 * unit));
            Assert.That(provider.TrackedOwnerCount, Is.EqualTo(1));

            firstLease.Dispose();
            using IDisposable aliasLease = Acquire(provider, stage, aliased, unit);
            Assert.That(provider.GetUsage(stage), Is.EqualTo(2 * unit));
            Assert.That(provider.TrackedOwnerCount, Is.EqualTo(1));
            secondLease.Dispose();
            aliasLease.Dispose();
            Assert.That(provider.GetUsage(stage), Is.Zero);
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
        }

        [TestCase("authority")]
        [TestCase("application")]
        [TestCase("user")]
        public void DifferentStableOwnersHaveIndependentCeilings(string changedPart)
        {
            ServerResourceIsolationOptions options = Options();
            options.Stages[(int)ResourceIsolationStage.RequestExecution].OwnerHardLimit = 2;
            using DefaultServerResourceIsolationProvider provider = Provider(options);
            var firstId = new RecoverableExecutionOwnerId(s_authority, s_application, s_user);
            var secondId = new RecoverableExecutionOwnerId(
                changedPart == "authority" ? s_other : s_authority,
                changedPart == "application" ? s_other : s_application,
                changedPart == "user" ? s_other : s_user);
            ResourceIsolationOwner first = provider.ClassifyRecoverableOwner(firstId);
            ResourceIsolationOwner second = provider.ClassifyRecoverableOwner(secondId);
            using IDisposable fullFirst = Acquire(provider, ResourceIsolationStage.RequestExecution, first, 2);
            using IDisposable fullSecond = Acquire(provider, ResourceIsolationStage.RequestExecution, second, 2);

            Assert.That(first.Key, Is.Not.EqualTo(second.Key));
            AssertRejected(provider, ResourceIsolationStage.RequestExecution,
                provider.ClassifyRecoverableOwner(firstId), 1, ResourceIsolationFailureReason.OwnerLimit);
            Assert.That(provider.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(4));
            Assert.That(provider.TrackedOwnerCount, Is.EqualTo(2));
            fullFirst.Dispose();
            Assert.That(provider.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(2));
            Assert.That(provider.TrackedOwnerCount, Is.EqualTo(1));
            AssertRejected(provider, ResourceIsolationStage.RequestExecution, second, 1,
                ResourceIsolationFailureReason.OwnerLimit);
            fullSecond.Dispose();
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
        }

        [TestCase(ResourceIsolationClass.Established)]
        [TestCase(ResourceIsolationClass.Control)]
        [TestCase(ResourceIsolationClass.Trusted)]
        public void StableOwnerClassificationIsProviderIssuedAndCannotBeForged(ResourceIsolationClass claimedClass)
        {
            using DefaultServerResourceIsolationProvider provider = Provider(Options());
            using DefaultServerResourceIsolationProvider other = Provider(Options());
            var id = new RecoverableExecutionOwnerId(s_authority, s_application, s_user);
            ResourceIsolationOwner issued = provider.ClassifyRecoverableOwner(id);
            ResourceIsolationOwner foreign = other.ClassifyRecoverableOwner(id);
            long[] limits = new long[(int)ResourceIsolationStage.ParkedRequest + 1];
            for (int i = 0; i < limits.Length; i++)
            {
                limits[i] = issued.GetHardLimit((ResourceIsolationStage)i);
            }
            var forged = new ResourceIsolationOwner(issued.Key, claimedClass, issued.Weight, limits);

            Assert.That(foreign.Key, Is.EqualTo(issued.Key));
            AssertRejected(provider, ResourceIsolationStage.RequestExecution, forged, 1,
                ResourceIsolationFailureReason.InvalidOwner);
            AssertRejected(provider, ResourceIsolationStage.RequestExecution, foreign, 1,
                ResourceIsolationFailureReason.InvalidOwner);
            Assert.That(provider.GetUsage(ResourceIsolationStage.RequestExecution), Is.Zero);
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
            using IDisposable lease = Acquire(provider, ResourceIsolationStage.RequestExecution, issued, 1);
            Assert.That(provider.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
            Assert.That(other.GetUsage(ResourceIsolationStage.RequestExecution), Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StableOwnerRemainsEstablishedAndCannotSpendProtectedCapacity(bool provisionTrusted)
        {
            ServerResourceIsolationOptions options = Options();
            options.Stages[(int)ResourceIsolationStage.RequestExecution].ControlReserved = 1;
            if (provisionTrusted)
            {
                options.Mode = ServerResourceIsolationMode.TrustedReservations;
                options.TrustedOwners =
                [
                    new TrustedResourceOwnerOptions { Key = "configured-tenant" }
                ];
            }
            SessionBindingContext? binding = new(
                new NodeId(10), "control", 1, UserTokenType.UserName, "synthetic-user",
                SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt);
            var bindings = new Mock<IServerSessionBindingProvider>();
            bindings.Setup(value => value.TryGetSessionContext(
                It.IsAny<NodeId>(), It.IsAny<SecureChannelContext>(), out binding)).Returns(true);
            using DefaultServerResourceIsolationProvider provider = Provider(options, bindings.Object,
                provisionTrusted ? new TrustedClassifier() : null);
            var id = new RecoverableExecutionOwnerId(s_authority, s_application, s_user);
            ResourceIsolationOwner owner = provider.ClassifyRecoverableOwner(id);
            ResourceIsolationOwner normal = provider.Classify(Channel("normal"));
            ResourceIsolationOwner control =
                provider.Classify(Channel("control"), new NodeId(10), controlRequest: true);
            ResourceIsolationStagePlan stage = provider.Plan.GetStage(ResourceIsolationStage.RequestExecution);
            using IDisposable shared = Acquire(provider, stage.Stage, owner, stage.SharedCapacity);

            Assert.That(owner.Class, Is.EqualTo(ResourceIsolationClass.Established));
            Assert.That(normal.Class, Is.EqualTo(ResourceIsolationClass.Established));
            Assert.That(control.Class, Is.EqualTo(ResourceIsolationClass.Control));
            AssertRejected(provider, stage.Stage, provider.ClassifyRecoverableOwner(id), 1,
                ResourceIsolationFailureReason.Capacity);
            AssertRejected(provider, stage.Stage, normal, 1, ResourceIsolationFailureReason.Capacity);
            using IDisposable protectedControl = Acquire(provider, stage.Stage, control, 1);
            Assert.That(provider.GetUsage(stage.Stage), Is.EqualTo(stage.SharedCapacity + 1));
            if (provisionTrusted)
            {
                ResourceIsolationOwner trusted = provider.Classify(Channel("trusted"));
                Assert.That(trusted.Class, Is.EqualTo(ResourceIsolationClass.Trusted));
                using IDisposable trustedLease = Acquire(provider, stage.Stage, trusted, stage.TrustedReserved);
                Assert.That(provider.GetUsage(stage.Stage), Is.EqualTo(stage.Capacity));
                AssertRejected(provider, stage.Stage, owner, 1, ResourceIsolationFailureReason.Capacity);
            }
            protectedControl.Dispose();
            shared.Dispose();
            Assert.That(provider.GetUsage(stage.Stage), Is.Zero);
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
        }

        [Test]
        public void InvalidStableOwnerCannotCreateAnAccountingBucket()
        {
            using DefaultServerResourceIsolationProvider provider = Provider(Options());
            Assert.That(() => provider.ClassifyRecoverableOwner(default),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("owner"));
            Assert.That(provider.TrackedOwnerCount, Is.Zero);
            Assert.That(provider.GetUsage(ResourceIsolationStage.RequestExecution), Is.Zero);
            provider.Dispose();
            Assert.That(() => provider.ClassifyRecoverableOwner(
                new RecoverableExecutionOwnerId(s_authority, s_application, s_user)),
                Throws.TypeOf<ObjectDisposedException>());
        }

        private static OperationContext ValidatedContext(Certificate certificate, string id)
        {
            // Represents already-validated Core state, exactly as in the mapping fixture.
            // This does not claim that a mock performs certificate or password authentication.
            var identity = new SyntheticOwnerIdentity
            {
                Issuer = "urn:synthetic:issuer",
                Subject = "operator",
                TokenHandler = new UserNameIdentityTokenHandler("operator", [1])
            };
            var session = new Mock<ISession>();
            SecureChannelContext channel = Channel(id);
            session.SetupGet(value => value.Id).Returns(new NodeId(id, 2));
            session.SetupGet(value => value.Activated).Returns(true);
            session.SetupGet(value => value.Identity).Returns(identity);
            session.SetupGet(value => value.EffectiveIdentity).Returns(identity);
            session.SetupGet(value => value.IdentityToken).Returns(identity.TokenHandler);
            session.SetupGet(value => value.ClientCertificate).Returns(certificate);
            session.SetupGet(value => value.EndpointDescription).Returns(channel.EndpointDescription!);
            session.SetupGet(value => value.SecureChannelId).Returns(id);
            session.Setup(value => value.IsSecureChannelValid(id)).Returns(true);
            return new OperationContext(new RequestHeader(), channel, RequestType.Call, RequestLifetime.None,
                session.Object);
        }

        private static SecureChannelContext Channel(string id)
        {
            return new SecureChannelContext(id, new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            }, RequestEncoding.Binary);
        }

        private static ByteString Digest(Certificate certificate)
        {
#if NET8_0_OR_GREATER
            return new ByteString(SHA256.HashData(certificate.RawData));
#else
            using var sha = SHA256.Create();
            return new ByteString(sha.ComputeHash(certificate.RawData));
#endif
        }

        private static ServerResourceIsolationOptions Options()
        {
            var stages = new ResourceIsolationStageOptions[(int)ResourceIsolationStage.ParkedRequest + 1];
            for (int i = 0; i < stages.Length; i++)
            {
                long capacity = i is (int)ResourceIsolationStage.RequestQueueBytes or
                    (int)ResourceIsolationStage.ReassemblyBytes ? 100 : 10;
                stages[i] = new ResourceIsolationStageOptions
                {
                    Stage = (ResourceIsolationStage)i,
                    Capacity = capacity,
                    OwnerHardLimit = capacity,
                    BootstrapReserved = 0,
                    ReconnectReserved = 0,
                    ControlReserved = 0
                };
            }
            return new ServerResourceIsolationOptions { MaxRetainedMessageBytes = 10, Stages = stages };
        }

        private static DefaultServerResourceIsolationProvider Provider(
            ServerResourceIsolationOptions options, IServerSessionBindingProvider? bindings = null,
            IResourceIsolationClassifier? classifier = null)
        {
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration { MaxSessionCount = 2, MaxChannelCount = 10 },
                TransportQuotas = new TransportQuotas { MaxMessageSize = 10, MaxBufferSize = 8192 }
            };
            return new DefaultServerResourceIsolationProvider(
                options.CreateRuntimePlan(
                    configuration, new ServerRateLimitOptions(), new ChunkReassemblyBudget(100, 50)),
                NUnitTelemetryContext.Create(), bindings, classifier);
        }

        private static IDisposable Acquire(
            DefaultServerResourceIsolationProvider provider, ResourceIsolationStage stage,
            ResourceIsolationOwner owner, long amount)
        {
            Assert.That(provider.TryAcquire(
                stage, owner, amount, out IDisposable? lease, out ResourceIsolationFailure failure),
                Is.True, failure.ToString());
            Assert.That(failure.Reason, Is.EqualTo(ResourceIsolationFailureReason.None));
            return lease!;
        }

        private static void AssertRejected(
            DefaultServerResourceIsolationProvider provider, ResourceIsolationStage stage,
            ResourceIsolationOwner owner, long amount, ResourceIsolationFailureReason expected)
        {
            Assert.That(provider.TryAcquire(
                stage, owner, amount, out IDisposable? lease, out ResourceIsolationFailure failure), Is.False);
            Assert.That(lease, Is.Null);
            Assert.That(failure.Reason, Is.EqualTo(expected));
            Assert.That(failure.RetryAfter, Is.GreaterThan(TimeSpan.Zero));
        }

        private sealed class TrustedClassifier : IResourceIsolationClassifier
        {
            public bool TryClassifyIngress(IPEndPoint? remoteEndpoint, out ResourceIsolationIdentity identity)
            {
                identity = default;
                return false;
            }

            public bool TryClassify(
                SecureChannelContext channelContext, SessionBindingContext? sessionBinding,
                out ResourceIsolationIdentity identity)
            {
                identity = channelContext.SecureChannelId == "trusted"
                    ? new ResourceIsolationIdentity("configured-tenant", ResourceIsolationClass.Trusted) : default;
                return identity.Key != null;
            }
        }

        private static readonly Uuid s_authority = new("41000000-0000-0000-0000-000000000001");
        private static readonly Uuid s_application = new("42000000-0000-0000-0000-000000000001");
        private static readonly Uuid s_user = new("43000000-0000-0000-0000-000000000001");
        private static readonly Uuid s_other = new("44000000-0000-0000-0000-000000000001");
        private Certificate m_original = null!;
        private Certificate m_alias = null!;
    }
}
