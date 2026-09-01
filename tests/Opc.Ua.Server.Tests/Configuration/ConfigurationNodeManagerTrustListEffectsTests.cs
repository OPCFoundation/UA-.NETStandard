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

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Integration tests for the OPC 10000-12 §7.10.9 wiring in
    /// <see cref="ConfigurationNodeManager"/>: mapping the TrustLists
    /// committed by a transaction to the correct post-<c>ApplyChanges</c>
    /// effect and dispatching those effects, after the grace boundary,
    /// through the injected
    /// <see cref="IPushConfigurationTrustListEffectHandler"/>.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [NonParallelizable]
    public class ConfigurationNodeManagerTrustListEffectsTests
    {
        private ServerFixture<StandardServer> m_fixture = null!;
        private StandardServer m_server = null!;
        private ConfigurationNodeManager m_configManager = null!;
        private ServerConfigurationState m_configNode = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);

            IServerInternal serverInternal = m_server.CurrentInstance;
            m_configManager = (serverInternal.ConfigurationNodeManager as ConfigurationNodeManager)!;
            Assert.That(m_configManager, Is.Not.Null);

            NodeState? node = await serverInternal.NodeManager
                .FindNodeInAddressSpaceAsync(ObjectIds.ServerConfiguration)
                .ConfigureAwait(false);
            m_configNode = (node as ServerConfigurationState)!;
            Assert.That(m_configNode, Is.Not.Null);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public void BuildTrustListEffectsMapsApplicationGroupToSecureChannelTrust()
        {
            List<TrustListChangeEffect> effects = m_configManager.BuildTrustListEffects(
                ArrayOf.Wrapped(
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList));

            Assert.That(effects, Has.Count.EqualTo(1));
            Assert.That(effects[0].Kind, Is.EqualTo(TrustListEffectKind.SecureChannelTrust));
            Assert.That(effects[0].ValidationScope, Is.EqualTo(TrustListIdentifier.Peers));
            Assert.That(
                effects[0].TrustListId,
                Is.EqualTo(ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList));
        }

        [Test]
        public void BuildTrustListEffectsMapsUserTokenGroupToUserIdentityTrust()
        {
            List<TrustListChangeEffect> effects = m_configManager.BuildTrustListEffects(
                ArrayOf.Wrapped(
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultUserTokenGroup_TrustList));

            // The reference server configures the user-token certificate group.
            Assert.That(effects, Has.Count.EqualTo(1));
            Assert.That(effects[0].Kind, Is.EqualTo(TrustListEffectKind.UserIdentityTrust));
            Assert.That(effects[0].ValidationScope, Is.EqualTo(TrustListIdentifier.Users));
        }

        [Test]
        public void BuildTrustListEffectsIgnoresUnknownTrustList()
        {
            List<TrustListChangeEffect> effects = m_configManager.BuildTrustListEffects(
                ArrayOf.Wrapped(new NodeId(Guid.NewGuid(), 1)));

            Assert.That(effects, Is.Empty);
        }

        [Test]
        public void BuildTrustListEffectsIgnoresNullTrustList()
        {
            List<TrustListChangeEffect> effects = m_configManager.BuildTrustListEffects(
                ArrayOf.Wrapped(NodeId.Null));

            Assert.That(effects, Is.Empty);
        }

        [Test]
        public async Task ApplyChangesDispatchesSecureChannelEffectForApplicationGroupTrustListAsync()
        {
            IReadOnlyList<TrustListChangeEffect> captured = await DriveApplyChangesForTrustListAsync(
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList)
                .ConfigureAwait(false);

            Assert.That(captured, Has.Count.EqualTo(1));
            Assert.That(captured[0].Kind, Is.EqualTo(TrustListEffectKind.SecureChannelTrust));
            Assert.That(captured[0].ValidationScope, Is.EqualTo(TrustListIdentifier.Peers));
        }

        [Test]
        public async Task ApplyChangesDispatchesUserIdentityEffectForUserTokenGroupTrustListAsync()
        {
            IReadOnlyList<TrustListChangeEffect> captured = await DriveApplyChangesForTrustListAsync(
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultUserTokenGroup_TrustList)
                .ConfigureAwait(false);

            Assert.That(captured, Has.Count.EqualTo(1));
            Assert.That(captured[0].Kind, Is.EqualTo(TrustListEffectKind.UserIdentityTrust));
            Assert.That(captured[0].ValidationScope, Is.EqualTo(TrustListIdentifier.Users));
        }

        [Test]
        public async Task ApplyChangesWithoutTrustListChangesDoesNotInvokeEffectHandlerAsync()
        {
            var handler = new CapturingEffectHandler();
            IPushConfigurationTrustListEffectHandler original = ReplaceEffectHandler(m_configManager, handler);
            TimeSpan originalGrace = m_configManager.ApplyChangesGracePeriod;

            try
            {
                m_configManager.ApplyChangesGracePeriod = TimeSpan.Zero;

                var sessionId = new NodeId(Guid.NewGuid(), 1);
                IPushConfigurationTransactionCoordinator coordinator = GetCoordinator(m_configManager);

                // A certificate-group-only staged operation (no AffectedTrustList)
                // must not produce any §7.10.9 TrustList effect.
                coordinator.Stage(sessionId, new PushConfigurationOperation
                {
                    AffectedCertificateGroup =
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    CommitAsync = _ => Task.CompletedTask
                });

                await InvokeApplyChangesAsync(sessionId).ConfigureAwait(false);
                await m_configManager.DrainPendingApplyChangesAsync().ConfigureAwait(false);

                Assert.That(handler.InvocationCount, Is.Zero);
            }
            finally
            {
                m_configManager.ApplyChangesGracePeriod = originalGrace;
                ReplaceEffectHandler(m_configManager, original);
            }
        }

        [Test]
        public async Task ApplyChangesUsesCommittedTargetsNotAConcurrentSessionsStagedTargetsAsync()
        {
            // Regression for the transaction-lifecycle race: after Session A's
            // ApplyChanges commit releases coordinator ownership, Session B may
            // immediately stage a new transaction in the window before A
            // computes/schedules its §7.10.9 effects. A must dispatch the
            // effects for the TrustLists IT committed - never B's uncommitted
            // staged targets, which a fresh coordinator snapshot would report.
            var handler = new CapturingEffectHandler();
            IPushConfigurationTrustListEffectHandler originalHandler = ReplaceEffectHandler(m_configManager, handler);
            TimeSpan originalGrace = m_configManager.ApplyChangesGracePeriod;
            IPushConfigurationTransactionCoordinator originalCoordinator = GetCoordinator(m_configManager);

            var sessionA = new NodeId(Guid.NewGuid(), 1);
            var sessionB = new NodeId(Guid.NewGuid(), 1);

            // A commits the application-group TrustList (SecureChannel effect).
            NodeId trustListA =
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList;
            // B stages the user-token-group TrustList (a distinct, user-identity
            // effect) that must never be observed by A's apply.
            NodeId trustListB =
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultUserTokenGroup_TrustList;

            // The decorator stages B on the real coordinator at the exact
            // moment A's commit has released ownership but A has not yet built
            // its effects - deterministically reproducing the race window.
            var decorator = new StageBAfterApplyCoordinator(
                originalCoordinator,
                () => originalCoordinator.Stage(sessionB, new PushConfigurationOperation
                {
                    AffectedTrustList = trustListB,
                    CommitAsync = _ => Task.CompletedTask
                }));

            try
            {
                m_configManager.ApplyChangesGracePeriod = TimeSpan.Zero;
                SwapCoordinator(m_configManager, decorator);

                decorator.Stage(sessionA, new PushConfigurationOperation
                {
                    AffectedTrustList = trustListA,
                    CommitAsync = _ => Task.CompletedTask
                });

                ServiceResult result = await InvokeApplyChangesAsync(sessionA).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(result), Is.True);

                await m_configManager.DrainPendingApplyChangesAsync().ConfigureAwait(false);

                // B did stage into the real coordinator during the race window.
                Assert.That(originalCoordinator.IsTransactionActive, Is.True);
                Assert.That(originalCoordinator.OwnerSessionId, Is.EqualTo(sessionB));

                // A's effect fan-out must reflect only A's committed TrustList.
                Assert.That(handler.InvocationCount, Is.EqualTo(1));
                Assert.That(handler.LastContext, Is.Not.Null);
                IReadOnlyList<TrustListChangeEffect> effects = handler.LastContext!.Effects;
                Assert.That(effects, Has.Count.EqualTo(1));
                Assert.That(effects[0].TrustListId, Is.EqualTo(trustListA));
                Assert.That(effects[0].Kind, Is.EqualTo(TrustListEffectKind.SecureChannelTrust));

                // B's uncommitted target must not have leaked into A's effects.
                Assert.That(
                    effects,
                    Has.None.Matches<TrustListChangeEffect>(e => e.TrustListId == trustListB));
                Assert.That(
                    effects,
                    Has.None.Matches<TrustListChangeEffect>(e => e.Kind == TrustListEffectKind.UserIdentityTrust));
            }
            finally
            {
                // Discard B's still-open transaction from the shared coordinator.
                originalCoordinator.CancelForSessionClose(sessionB);
                SwapCoordinator(m_configManager, originalCoordinator);
                m_configManager.ApplyChangesGracePeriod = originalGrace;
                ReplaceEffectHandler(m_configManager, originalHandler);
            }
        }

        [Test]
        public void BuildTrustListEffectsForScopesMapsPeersToSecureChannelTrust()
        {
            List<TrustListChangeEffect> effects = m_configManager.BuildTrustListEffectsForScopes(
                [TrustListIdentifier.Peers]);

            Assert.That(effects, Has.Count.EqualTo(1));
            Assert.That(effects[0].Kind, Is.EqualTo(TrustListEffectKind.SecureChannelTrust));
            Assert.That(effects[0].ValidationScope, Is.EqualTo(TrustListIdentifier.Peers));
            Assert.That(
                effects[0].TrustListId,
                Is.EqualTo(ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList));
        }

        [Test]
        public void BuildTrustListEffectsForScopesMapsUsersToUserIdentityTrust()
        {
            List<TrustListChangeEffect> effects = m_configManager.BuildTrustListEffectsForScopes(
                [TrustListIdentifier.Users]);

            Assert.That(effects, Has.Count.EqualTo(1));
            Assert.That(effects[0].Kind, Is.EqualTo(TrustListEffectKind.UserIdentityTrust));
            Assert.That(effects[0].ValidationScope, Is.EqualTo(TrustListIdentifier.Users));
        }

        [Test]
        public void BuildTrustListEffectsForScopesIgnoresUnmappedScopes()
        {
            List<TrustListChangeEffect> effects = m_configManager.BuildTrustListEffectsForScopes(
                [TrustListIdentifier.Rejected, new TrustListIdentifier("CustomScope"), null!]);

            Assert.That(effects, Is.Empty);
        }

        [Test]
        public async Task OutOfBandCrlUpdateDispatchesSecureChannelEffectAsync()
        {
            // An out-of-band CRL change (no push transaction, no ApplyChanges)
            // must still trigger the §7.10.9 effect fan-out via the
            // trust-material enforcement pump.
            var handler = new CapturingEffectHandler();
            IPushConfigurationTrustListEffectHandler original = ReplaceEffectHandler(m_configManager, handler);

            try
            {
                ICertificateManager certificateManager = m_fixture.Config.CertificateManager;
                Assert.That(certificateManager, Is.Not.Null);

                certificateManager.NotifyTrustListChanged(
                    TrustListIdentifier.Peers,
                    trustChanged: false,
                    crlChanged: true);

                await WaitUntilAsync(() => handler.InvocationCount >= 1).ConfigureAwait(false);

                PushConfigurationTrustListEffectContext context = handler.Contexts[0];
                Assert.That(context.Effects, Has.Count.EqualTo(1));
                Assert.That(context.Effects[0].Kind, Is.EqualTo(TrustListEffectKind.SecureChannelTrust));
                Assert.That(context.Effects[0].ValidationScope, Is.EqualTo(TrustListIdentifier.Peers));
                Assert.That(context.CertificateValidator, Is.Not.Null);
            }
            finally
            {
                ReplaceEffectHandler(m_configManager, original);
            }
        }

        [Test]
        public async Task OutOfBandUserTrustChangeDispatchesUserIdentityEffectAsync()
        {
            var handler = new CapturingEffectHandler();
            IPushConfigurationTrustListEffectHandler original = ReplaceEffectHandler(m_configManager, handler);

            try
            {
                ICertificateManager certificateManager = m_fixture.Config.CertificateManager;

                certificateManager.NotifyTrustListChanged(
                    TrustListIdentifier.Users,
                    trustChanged: true,
                    crlChanged: true);

                await WaitUntilAsync(() => handler.InvocationCount >= 1).ConfigureAwait(false);

                PushConfigurationTrustListEffectContext context = handler.Contexts[0];
                Assert.That(context.Effects, Has.Count.EqualTo(1));
                Assert.That(context.Effects[0].Kind, Is.EqualTo(TrustListEffectKind.UserIdentityTrust));
                Assert.That(context.Effects[0].ValidationScope, Is.EqualTo(TrustListIdentifier.Users));
            }
            finally
            {
                ReplaceEffectHandler(m_configManager, original);
            }
        }

        [Test]
        public async Task OutOfBandRejectedScopeChangeDispatchesNoEffectAsync()
        {
            var handler = new CapturingEffectHandler();
            IPushConfigurationTrustListEffectHandler original = ReplaceEffectHandler(m_configManager, handler);

            try
            {
                ICertificateManager certificateManager = m_fixture.Config.CertificateManager;

                // The Rejected scope maps to no server certificate group, so
                // the pump pass must produce no effect at all.
                certificateManager.NotifyTrustListChanged(
                    TrustListIdentifier.Rejected,
                    trustChanged: true,
                    crlChanged: true);

                await Task.Delay(500).ConfigureAwait(false);
                Assert.That(handler.InvocationCount, Is.Zero);
            }
            finally
            {
                ReplaceEffectHandler(m_configManager, original);
            }
        }

        [Test]
        public async Task PushedCrlIsEnforcedByTheLiveValidatorAfterApplyChangesAsync()
        {
            // End-to-end regression for the CRL-push enforcement pipeline: a
            // CRL committed through the ApplyChanges deferred apply must be
            // observed by the server's LIVE certificate validator immediately
            // afterwards — no token renewal, no store-cache staleness window.
            ICertificateManager certificateManager = m_fixture.Config.CertificateManager;
            SecurityConfiguration securityConfiguration = m_fixture.Config.SecurityConfiguration;
            var trustedStoreId = new CertificateStoreIdentifier(
                securityConfiguration.TrustedPeerCertificates!.StorePath!);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            using Certificate caCert = CertificateBuilder
                .Create("CN=CRL Push Test CA, O=OPC Foundation")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=CRL Push Test Client")
                .SetIssuer(caCert)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // An empty CRL so the CA has a known (non-revoked) revocation
            // status before the push.
            X509CRL emptyCrl = DefaultCertificateIssuer.Instance.RevokeCertificates(
                caCert, null!, null!);
            X509CRL revokingCrl = null!;

            TimeSpan originalGrace = m_configManager.ApplyChangesGracePeriod;
            try
            {
                using (ICertificateStore store = trustedStoreId.OpenStore(telemetry))
                {
                    await store.AddAsync(caCert).ConfigureAwait(false);
                    await store.AddCRLAsync(emptyCrl).ConfigureAwait(false);
                }

                // Make the live validator observe the seeded CA immediately.
                certificateManager.NotifyTrustListChanged(
                    TrustListIdentifier.Peers, trustChanged: true, crlChanged: true);

                CertificateValidationResult before = await certificateManager
                    .ValidateAsync(leaf, TrustListIdentifier.Peers)
                    .ConfigureAwait(false);
                Assert.That(before.IsValid, Is.True,
                    $"leaf must validate before revocation but was {before.StatusCode}");

                // Push the revocation through the real ApplyChanges pipeline:
                // the staged commit writes the CRL exactly like the TrustList
                // CloseAndUpdate commit does, and the deferred apply must
                // invalidate the validator's cached state afterwards.
                using (var revoked = new CertificateCollection { leaf })
                {
                    revokingCrl = DefaultCertificateIssuer.Instance.RevokeCertificates(
                        caCert,
                        new X509CRLCollection { emptyCrl },
                        revoked);
                }

                m_configManager.ApplyChangesGracePeriod = TimeSpan.Zero;
                var sessionId = new NodeId(Guid.NewGuid(), 1);
                IPushConfigurationTransactionCoordinator coordinator = GetCoordinator(m_configManager);
                coordinator.Stage(sessionId, new PushConfigurationOperation
                {
                    AffectedTrustList =
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                    CommitAsync = async ct =>
                    {
                        using ICertificateStore store = trustedStoreId.OpenStore(telemetry);
                        await store.AddCRLAsync(revokingCrl, ct).ConfigureAwait(false);
                    }
                });

                ServiceResult result = await InvokeApplyChangesAsync(sessionId).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(result), Is.True);
                await m_configManager.DrainPendingApplyChangesAsync().ConfigureAwait(false);

                CertificateValidationResult after = await certificateManager
                    .ValidateAsync(leaf, TrustListIdentifier.Peers)
                    .ConfigureAwait(false);
                Assert.That(after.IsValid, Is.False,
                    "revoked leaf must no longer validate after the CRL push");
                Assert.That(after.StatusCode, Is.EqualTo(StatusCodes.BadCertificateRevoked));
            }
            finally
            {
                m_configManager.ApplyChangesGracePeriod = originalGrace;

                // Remove the test CA and its CRLs so other tests see the
                // original trust material, and re-sync the validator.
                try
                {
                    using (ICertificateStore store = trustedStoreId.OpenStore(telemetry))
                    {
                        await store.DeleteAsync(caCert.Thumbprint).ConfigureAwait(false);
                        await store.DeleteCRLAsync(emptyCrl).ConfigureAwait(false);
                        if (revokingCrl != null)
                        {
                            await store.DeleteCRLAsync(revokingCrl).ConfigureAwait(false);
                        }
                    }
                }
                catch
                {
                    // Best-effort cleanup.
                }
                certificateManager.NotifyTrustListChanged(
                    TrustListIdentifier.Peers, trustChanged: true, crlChanged: true);
            }
        }

        [Test]
        public async Task CreateGroupStoreIdentifierOpensTheConfiguredCustomStoreTypeAsync()
        {
            MethodInfo method = typeof(ConfigurationNodeManager).GetMethod(
                "CreateGroupStoreIdentifier", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("CreateGroupStoreIdentifier not found.");

            // A custom store type is not inferable from the path; the group
            // identifier must carry the configured type so the push path
            // actually opens (and writes through) the registered custom
            // store implementation instead of silently downgrading to a
            // directory store. The registration below uses the legacy static
            // registry - the resolution path identifier-based store access
            // supports; there is no unregister API, so the name is unique
            // and the registration is process-lifetime.
            const string storeTypeName = "TrustListEffectsTestStore";
            if (CertificateStoreType.GetCertificateStoreTypeByName(storeTypeName) == null)
            {
#pragma warning disable CS0618 // The static registry is the identifier-resolvable extension point.
                CertificateStoreType.RegisterCertificateStoreType(
                    storeTypeName,
                    new InMemoryCertificateStoreType());
#pragma warning restore CS0618
            }

            var source = new CertificateTrustList
            {
                StoreType = storeTypeName,
                StorePath = "custom://trust/peers"
            };

            var identifier = (CertificateStoreIdentifier)method.Invoke(null, [source])!;
            Assert.That(identifier.StoreType, Is.EqualTo(storeTypeName));
            Assert.That(identifier.StorePath, Is.EqualTo("custom://trust/peers"));

            // The identifier must resolve to the registered implementation
            // and be usable for a real write/read round trip.
            try
            {
                using Certificate certificate = CertificateBuilder
                    .Create("CN=Custom Store Type Cert")
                    .SetRSAKeySize(2048)
                    .CreateForRSA();
                using (ICertificateStore store = identifier.OpenStore(NUnitTelemetryContext.Create()))
                {
                    Assert.That(store, Is.InstanceOf<CertificateIdentifierCollectionStore>());
                    await store.AddAsync(certificate).ConfigureAwait(false);
                    using CertificateCollection found = await store
                        .FindByThumbprintAsync(certificate.Thumbprint)
                        .ConfigureAwait(false);
                    Assert.That(found, Has.Count.EqualTo(1));
                }
            }
            finally
            {
                identifier.DisposeCachedStore();
            }
        }

        private sealed class InMemoryCertificateStoreType : ICertificateStoreType
        {
            public bool SupportsStorePath(string storePath)
            {
                return storePath != null &&
                    storePath.StartsWith("custom://", StringComparison.OrdinalIgnoreCase);
            }

            public ICertificateStore CreateStore(ITelemetryContext telemetry)
            {
                return new CertificateIdentifierCollectionStore(telemetry);
            }
        }

        private static Task WaitUntilAsync(Func<bool> condition)
        {
            return TestPolling.WaitUntilAsync(
                condition,
                timeoutMessage: "Timed out waiting for the enforcement pump.");
        }

        private async Task<IReadOnlyList<TrustListChangeEffect>> DriveApplyChangesForTrustListAsync(
            NodeId trustListId)
        {
            var handler = new CapturingEffectHandler();
            IPushConfigurationTrustListEffectHandler original = ReplaceEffectHandler(m_configManager, handler);
            TimeSpan originalGrace = m_configManager.ApplyChangesGracePeriod;

            try
            {
                m_configManager.ApplyChangesGracePeriod = TimeSpan.Zero;

                var sessionId = new NodeId(Guid.NewGuid(), 1);
                IPushConfigurationTransactionCoordinator coordinator = GetCoordinator(m_configManager);
                coordinator.Stage(sessionId, new PushConfigurationOperation
                {
                    AffectedTrustList = trustListId,
                    CommitAsync = _ => Task.CompletedTask
                });

                ServiceResult result = await InvokeApplyChangesAsync(sessionId).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(result), Is.True);

                await m_configManager.DrainPendingApplyChangesAsync().ConfigureAwait(false);

                Assert.That(handler.InvocationCount, Is.EqualTo(1));
                Assert.That(handler.LastContext, Is.Not.Null);
                PushConfigurationTrustListEffectContext context = handler.LastContext!;
                Assert.That(context.CertificateValidator, Is.Not.Null);
                Assert.That(context.SessionManager, Is.Not.Null);
                return context.Effects;
            }
            finally
            {
                m_configManager.ApplyChangesGracePeriod = originalGrace;
                ReplaceEffectHandler(m_configManager, original);
            }
        }

        private async Task<ServiceResult> InvokeApplyChangesAsync(NodeId sessionId)
        {
            var outputArguments = new List<Variant>();
            MethodState applyChanges = m_configNode.ApplyChanges!;
            return await applyChanges.OnCallMethod2Async!(
                CreateAdminContextForSession(sessionId),
                applyChanges,
                m_configNode.NodeId,
                ArrayOf<Variant>.Empty,
                outputArguments,
                CancellationToken.None).ConfigureAwait(false);
        }

        private static IPushConfigurationTransactionCoordinator GetCoordinator(ConfigurationNodeManager manager)
        {
            FieldInfo field = typeof(ConfigurationNodeManager).GetField(
                "m_coordinator", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_coordinator not found.");
            return (IPushConfigurationTransactionCoordinator)field.GetValue(manager)!;
        }

        private static void SwapCoordinator(
            ConfigurationNodeManager manager,
            IPushConfigurationTransactionCoordinator coordinator)
        {
            FieldInfo field = typeof(ConfigurationNodeManager).GetField(
                "m_coordinator", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_coordinator not found.");
            field.SetValue(manager, coordinator);
        }

        private static IPushConfigurationTrustListEffectHandler ReplaceEffectHandler(
            ConfigurationNodeManager manager,
            IPushConfigurationTrustListEffectHandler handler)
        {
            FieldInfo field = typeof(ConfigurationNodeManager).GetField(
                "m_trustListEffectHandler", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_trustListEffectHandler not found.");
            var previous = (IPushConfigurationTrustListEffectHandler)field.GetValue(manager)!;
            field.SetValue(manager, handler);
            return previous;
        }

        private static SessionSystemContext CreateAdminContextForSession(NodeId sessionId)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns(nameof(UserTokenType.UserName));
            identity.Setup(i => i.GrantedRoleIds)
                .Returns(ArrayOf.Wrapped(ObjectIds.WellKnownRole_SecurityAdmin));

            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(sessionId);
            session.Setup(s => s.EffectiveIdentity).Returns(identity.Object);
            session.Setup(s => s.PreferredLocales).Returns([]);

            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
            var channelContext = new SecureChannelContext("test", endpoint, RequestEncoding.Binary);
            var operationContext = new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.Call,
                RequestLifetime.None,
                session.Object);
            return new SessionSystemContext(operationContext, NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
        }

        /// <summary>
        /// Thread-safe capturing fake: invocations may arrive from the
        /// enforcement pump's background drain task while the test thread
        /// polls, so every access synchronizes on one lock.
        /// </summary>
        private sealed class CapturingEffectHandler : IPushConfigurationTrustListEffectHandler
        {
            public int InvocationCount
            {
                get
                {
                    lock (m_sync)
                    {
                        return m_contexts.Count;
                    }
                }
            }

            public PushConfigurationTrustListEffectContext? LastContext
            {
                get
                {
                    lock (m_sync)
                    {
                        return m_contexts.Count > 0 ? m_contexts[^1] : null;
                    }
                }
            }

            public IReadOnlyList<PushConfigurationTrustListEffectContext> Contexts
            {
                get
                {
                    lock (m_sync)
                    {
                        return [.. m_contexts];
                    }
                }
            }

            public ValueTask ApplyAsync(
                PushConfigurationTrustListEffectContext context,
                CancellationToken cancellationToken = default)
            {
                lock (m_sync)
                {
                    m_contexts.Add(context);
                }
                return default;
            }

            private readonly List<PushConfigurationTrustListEffectContext> m_contexts = [];
            private readonly Lock m_sync = new();
        }

        /// <summary>
        /// Coordinator decorator that runs <paramref name="afterApply"/>
        /// immediately after the inner coordinator's
        /// <c>ApplyChangesAsync</c> returns - i.e. after commit ownership has
        /// been released but before the caller computes its effects - to
        /// deterministically reproduce a second Session staging a new
        /// transaction in that race window. All other members delegate to the
        /// inner coordinator unchanged.
        /// </summary>
        private sealed class StageBAfterApplyCoordinator : IPushConfigurationTransactionCoordinator
        {
            public StageBAfterApplyCoordinator(
                IPushConfigurationTransactionCoordinator inner,
                Action afterApply)
            {
                m_inner = inner;
                m_afterApply = afterApply;
            }

            public NodeId OwnerSessionId => m_inner.OwnerSessionId;

            public bool IsTransactionActive => m_inner.IsTransactionActive;

            public bool HasOpenTrustListWriter => m_inner.HasOpenTrustListWriter;

            public void ValidateSessionCanParticipate(NodeId sessionId)
                => m_inner.ValidateSessionCanParticipate(sessionId);

            public void Stage(NodeId sessionId, PushConfigurationOperation operation)
                => m_inner.Stage(sessionId, operation);

            public void SetTrustListWriteOpen(NodeId trustListId, bool isOpen)
                => m_inner.SetTrustListWriteOpen(trustListId, isOpen);

            public ArrayOf<PushConfigurationOperation> GetStagedOperations()
                => m_inner.GetStagedOperations();

            public ValueTask<ServiceResult> ApplyChangesAsync(
                NodeId sessionId,
                CancellationToken cancellationToken = default)
                => ApplyChangesAsync(sessionId, new PushConfigurationApplyEffects(), cancellationToken);

            public async ValueTask<ServiceResult> ApplyChangesAsync(
                NodeId sessionId,
                PushConfigurationApplyEffects committedEffects,
                CancellationToken cancellationToken = default)
            {
                ServiceResult result = await m_inner
                    .ApplyChangesAsync(sessionId, committedEffects, cancellationToken)
                    .ConfigureAwait(false);
                m_afterApply();
                return result;
            }

            public ServiceResult CancelChanges(NodeId sessionId)
                => m_inner.CancelChanges(sessionId);

            public void CancelForSessionClose(NodeId sessionId)
                => m_inner.CancelForSessionClose(sessionId);

            public void Reset() => m_inner.Reset();

            public PushConfigurationTransactionSnapshot GetSnapshot() => m_inner.GetSnapshot();

            private readonly IPushConfigurationTransactionCoordinator m_inner;
            private readonly Action m_afterApply;
        }
    }
}
