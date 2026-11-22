// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2000: test code; disposables are ownership-transferred to test fixtures or are short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Validates the fluent historian extensions that bridge the
    /// <see cref="INodeManagerBuilder"/> surface to the Part 11 historian
    /// provider model. Confirms that <c>UseHistorian()</c> reuses a
    /// per-builder <see cref="HistorianBuilder"/>, that
    /// <c>Historize()</c> sets the historizing flags and registers the
    /// variable with the server-wide
    /// <see cref="IHistorianProviderRegistry"/>, that
    /// <c>WithHistorian()</c> binds a per-node provider that takes
    /// precedence over the default, and that the bare "just works"
    /// <c>Historize()</c> path lazily installs an in-memory engine.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.None)] // ConditionalWeakTable keyed by builder; per-test fixture
    public class HistorianFluentTests
    {
        private const ushort kNs = 2;

        /// <summary>
        /// Verifies that repeated UseHistorian calls return the same historian builder.
        /// </summary>
        [Test]
        public void UseHistorianReturnsSameBuilderAcrossCalls()
        {
            (NodeManagerBuilder b, _) = CreateBuilderWithVariable();

            HistorianBuilder first = b.UseHistorian();
            HistorianBuilder second = b.UseHistorian();

            Assert.That(first, Is.SameAs(second));
        }

        /// <summary>
        /// Verifies that Historize retains its original binary-compatible overload signatures.
        /// </summary>
        [Test]
        public void HistorizeRetainsOriginalBinarySignatures()
        {
            var methods = typeof(HistorianFluentExtensions)
                .GetMethods()
                .Where(value => value.Name == nameof(HistorianFluentExtensions.Historize))
                .ToArray();

            Assert.That(
                methods.Count(value =>
                    value.IsGenericMethodDefinition &&
                    value.GetParameters().Length == 6),
                Is.EqualTo(1));
            Assert.That(
                methods.Count(value =>
                    !value.IsGenericMethodDefinition &&
                    value.GetParameters().Length == 6),
                Is.EqualTo(1));

            var stateMethods = typeof(HistorianStateFluentExtensions)
                .GetMethods()
                .Where(value =>
                    value.Name == nameof(HistorianStateFluentExtensions.Historize))
                .ToArray();
            Assert.That(
                stateMethods.Count(value =>
                    value.IsGenericMethodDefinition &&
                    value.GetParameters().Length == 7),
                Is.EqualTo(1));
            Assert.That(
                stateMethods.Count(value =>
                    !value.IsGenericMethodDefinition &&
                    value.GetParameters().Length == 7),
                Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that Historize lazily creates an in-memory provider when UseHistorian was not called first.
        /// </summary>
        [Test]
        public void HistorizeWithoutPriorUseHistorianLazilyCreatesInMemoryProvider()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            IServerInternal server = ((ServerSystemContext)b.Context).Server;
            IHistorianProviderRegistry registry = ((IHistorianRegistryProvider)server).HistorianRegistry;

            // Sanity: nothing registered before.
            Assert.That(registry.Providers, Has.Count.EqualTo(0));

            IVariableBuilder<int> view = b.Variable<int>(v.NodeId).Historize();

            Assert.That(view, Is.Not.Null);
            Assert.That(registry.Providers, Has.Count.EqualTo(1));
            IHistorianProvider? resolved = registry.Resolve(v.NodeId);
            Assert.That(resolved, Is.InstanceOf<InMemoryHistorianProvider>(),
                "Bare Historize() should lazily install an in-memory provider as the default.");
        }

        /// <summary>
        /// Verifies that Historize enables historizing and the appropriate history access levels.
        /// </summary>
        [Test]
        public void HistorizeSetsHistorizingAndAccessLevels()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            v.AccessLevel = AccessLevels.CurrentRead;
            v.UserAccessLevel = AccessLevels.CurrentRead;

            b.Variable<int>(v.NodeId).Historize();

            Assert.That(v.Historizing, Is.True);
            Assert.That((byte)(v.AccessLevel & AccessLevels.HistoryRead),
                Is.EqualTo(AccessLevels.HistoryRead));
            Assert.That((byte)(v.UserAccessLevel & AccessLevels.HistoryRead),
                Is.EqualTo(AccessLevels.HistoryRead));
            Assert.That((byte)(v.AccessLevel & AccessLevels.HistoryWrite),
                Is.Zero,
                "The default historian must be read-only.");
            Assert.That((byte)(v.UserAccessLevel & AccessLevels.HistoryWrite),
                Is.Zero,
                "The default historian must not grant HistoryWrite to every user.");
        }

        /// <summary>
        /// Verifies that HistoryWrite is granted only on explicit opt-in.
        /// </summary>
        [Test]
        public void HistorizeGrantsHistoryWriteOnlyWhenRequested()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            v.AccessLevel = AccessLevels.CurrentRead;
            v.UserAccessLevel = AccessLevels.CurrentRead;

            b.Variable<int>(v.NodeId).Historize(
                historyAccessLevel: AccessLevels.HistoryRead | AccessLevels.HistoryWrite);

            Assert.That((byte)(v.AccessLevel & AccessLevels.HistoryWrite),
                Is.EqualTo(AccessLevels.HistoryWrite));
            Assert.That((byte)(v.UserAccessLevel & AccessLevels.HistoryWrite),
                Is.EqualTo(AccessLevels.HistoryWrite));
        }

        /// <summary>
        /// Verifies that a default literal selects the legacy typed Historize overload.
        /// </summary>
        [Test]
        public void HistorizeDefaultLiteralUsesLegacyTypedOverload()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderWithVariable();

            b.Variable<int>(v.NodeId).Historize(default);

            Assert.That(v.Historizing, Is.True);
        }

        /// <summary>
        /// Verifies that Historize can explicitly leave the Historizing attribute false.
        /// </summary>
        [Test]
        public void HistorizeCanExplicitlyLeaveHistorizingFalse()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            IServerInternal server = ((ServerSystemContext)b.Context).Server;
            IHistorianProviderRegistry registry =
                ((IHistorianRegistryProvider)server).HistorianRegistry;
            v.AccessLevel = AccessLevels.CurrentRead;
            v.UserAccessLevel = AccessLevels.CurrentRead;
            v.Historizing = true;

            b.Variable<int>(v.NodeId).Historize(
                autoCapture: false,
                historizing: false);

            Assert.That(v.Historizing, Is.False);
            Assert.That(
                (byte)(v.AccessLevel & AccessLevels.HistoryRead),
                Is.EqualTo(AccessLevels.HistoryRead));
            Assert.That(
                (byte)(v.UserAccessLevel & AccessLevels.HistoryRead),
                Is.EqualTo(AccessLevels.HistoryRead));
            Assert.That(registry.Resolve(v.NodeId), Is.Not.Null);
        }

        /// <summary>
        /// Verifies that Historize can preserve a provider-owned Historizing state.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void HistorizeCanPreserveProviderOwnedHistorizing(bool initialValue)
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            v.Historizing = initialValue;

            b.Variable<int>(v.NodeId).Historize(
                autoCapture: false,
                historizing: null);

            Assert.That(v.Historizing, Is.EqualTo(initialValue));
        }

        /// <summary>
        /// Verifies that Historize respects a custom access-level selection.
        /// </summary>
        [Test]
        public void HistorizeRespectsCustomAccessLevel()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            v.AccessLevel = AccessLevels.CurrentRead;
            v.UserAccessLevel = AccessLevels.CurrentRead;

            b.Variable<int>(v.NodeId).Historize(historyAccessLevel: AccessLevels.HistoryRead);

            Assert.That((byte)(v.AccessLevel & AccessLevels.HistoryRead),
                Is.EqualTo(AccessLevels.HistoryRead));
            Assert.That((byte)(v.AccessLevel & AccessLevels.HistoryWrite),
                Is.Zero,
                "HistoryWrite must not be set when only HistoryRead was requested.");
        }

        /// <summary>
        /// Verifies that fluent historian configuration registers an explicitly supplied provider.
        /// </summary>
        [Test]
        public void UseHistorianFluentChainRegistersExplicitProvider()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            IServerInternal server = ((ServerSystemContext)b.Context).Server;
            IHistorianProviderRegistry registry = ((IHistorianRegistryProvider)server).HistorianRegistry;

            using var customProvider = new InMemoryHistorianProvider();
            b.UseHistorian().UseProvider(customProvider).RegisterAsDefault();
            b.Variable<int>(v.NodeId).Historize();

            Assert.That(registry.Resolve(v.NodeId), Is.SameAs(customProvider));
        }

        /// <summary>
        /// Verifies that WithHistorian binds a per-node provider with precedence over broader registrations.
        /// </summary>
        [Test]
        public void WithHistorianBindsPerNodeAndTakesPrecedence()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            IServerInternal server = ((ServerSystemContext)b.Context).Server;
            IHistorianProviderRegistry registry = ((IHistorianRegistryProvider)server).HistorianRegistry;

            using var defaultProvider = new InMemoryHistorianProvider();
            using var specialProvider = new InMemoryHistorianProvider();

            b.UseHistorian().UseProvider(defaultProvider).RegisterAsDefault();
            b.Variable<int>(v.NodeId)
                .WithHistorian(specialProvider)
                .Historize();

            // Per-node binding wins over the default.
            Assert.That(registry.Resolve(v.NodeId), Is.SameAs(specialProvider));
        }

        /// <summary>
        /// Verifies that an explicit Historize provider argument creates a per-node binding.
        /// </summary>
        [Test]
        public void HistorizeWithExplicitProviderArgumentBindsPerNode()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            IServerInternal server = ((ServerSystemContext)b.Context).Server;
            IHistorianProviderRegistry registry = ((IHistorianRegistryProvider)server).HistorianRegistry;

            using var perCallProvider = new InMemoryHistorianProvider();
            b.Variable<int>(v.NodeId).Historize(provider: perCallProvider);

            Assert.That(registry.Resolve(v.NodeId), Is.SameAs(perCallProvider));
            Assert.That(v.Historizing, Is.True);
        }

        /// <summary>
        /// Exercises old-owner disposal after fluent attribute changes and before replacement setup completes.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, false, true)]
        [TestCase(true, true, true)]
        public void ExplicitProviderReplacementPreservesAttributesAndRollsBackFailure(
            bool failSetup,
            bool disposePreviousDuringSetup,
            bool customRegistry)
        {
            using var storageRegistry = new HistorianProviderRegistry(new NamespaceTable());
            IHistorianProviderRegistry registry = storageRegistry;
            if (customRegistry)
            {
                var injected = new Mock<IHistorianProviderRegistry>(MockBehavior.Strict);
                injected.Setup(value => value.Resolve(It.IsAny<NodeId>()))
                    .Returns((NodeId id) => storageRegistry.Resolve(id));
                injected.As<IHistorianNodeReplacementRegistry>()
                    .Setup(value => value.BeginNodeReplacement(
                        It.IsAny<BaseVariableState>(), It.IsAny<IHistorianProvider>()))
                    .Returns((BaseVariableState node, IHistorianProvider provider) =>
                        ((IHistorianNodeReplacementRegistry)storageRegistry).BeginNodeReplacement(node, provider));
                injected.As<IHistorianNodeBindingRegistry>();
                registry = injected.Object;
            }
            IServerInternal server = CreateServerWithRegistry(registryOverride: registry);
            var variable = new Mock<BaseDataVariableState>(MockBehavior.Default, (NodeState?)null)
            {
                CallBase = true
            };
            (NodeManagerBuilder builder, BaseDataVariableState node) =
                CreateBuilderForServer(server, variable.Object);
            node.AccessLevel = AccessLevels.CurrentRead;
            node.UserAccessLevel = AccessLevels.CurrentRead;
            using var previous = new InMemoryHistorianProvider();
            using var replacement = new InMemoryHistorianProvider();
            Assert.That(storageRegistry.TryRegisterForNode(node, previous, out IDisposable? oldBinding), Is.True);
            node.Historizing = true;
            node.AccessLevel |= AccessLevels.HistoryRead;
            node.UserAccessLevel |= AccessLevels.HistoryRead;
            var setupFailure = new InvalidOperationException("The annotation lookup failed.");
            variable.Setup(value => value.FindChild(
                    It.IsAny<ISystemContext>(), new QualifiedName(BrowseNames.Annotations)))
                .Returns(() =>
                {
                    Assert.That(storageRegistry.Resolve(node.NodeId), Is.SameAs(previous));
                    Assert.That(storageRegistry.Providers.Contains(replacement), Is.False);
                    if (disposePreviousDuringSetup)
                    {
                        oldBinding!.Dispose();
                    }
                    if (failSetup)
                    {
                        throw setupFailure;
                    }
                    return null;
                });

            if (failSetup)
            {
                Assert.That(Assert.Throws<InvalidOperationException>(() =>
                    builder.Variable<int>(node.NodeId).Historize(
                        provider: replacement, autoCapture: false)), Is.SameAs(setupFailure));
                Assert.That(registry.Resolve(node.NodeId),
                    disposePreviousDuringSetup ? Is.Null : Is.SameAs(previous));
                Assert.That(node.Historizing, Is.EqualTo(!disposePreviousDuringSetup));
                byte expectedAccess = disposePreviousDuringSetup
                    ? AccessLevels.CurrentRead
                    : (byte)(AccessLevels.CurrentRead | AccessLevels.HistoryRead);
                Assert.That(node.AccessLevel, Is.EqualTo(expectedAccess));
                Assert.That(node.UserAccessLevel, Is.EqualTo(expectedAccess));
                oldBinding!.Dispose();
                Assert.That(registry.Resolve(node.NodeId), Is.Null);
                Assert.That(node.Historizing, Is.False);
                Assert.That(node.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
                Assert.That(node.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            }
            else
            {
                builder.Variable<int>(node.NodeId).Historize(provider: replacement, autoCapture: false);
                oldBinding!.Dispose();
                Assert.That(registry.Resolve(node.NodeId), Is.SameAs(replacement));
                Assert.That(node.Historizing, Is.True);
                Assert.That(node.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead | AccessLevels.HistoryRead));
                Assert.That(node.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentRead | AccessLevels.HistoryRead));
            }
            variable.Verify(value => value.FindChild(
                It.IsAny<ISystemContext>(), new QualifiedName(BrowseNames.Annotations)), Times.Once);
        }

        /// <summary>
        /// A competing mutation is rejected before it can change an in-progress fluent replacement.
        /// </summary>
        [Test]
        public void ExplicitProviderReplacementRejectsConcurrentMutationBeforeChangingTheOwner()
        {
            IServerInternal server = CreateServerWithRegistry();
            var variable = new Mock<BaseDataVariableState>(MockBehavior.Default, (NodeState?)null)
            {
                CallBase = true
            };
            (NodeManagerBuilder builder, BaseDataVariableState node) =
                CreateBuilderForServer(server, variable.Object);
            var registry = (HistorianProviderRegistry)((IHistorianRegistryProvider)server).HistorianRegistry;
            using var previous = new InMemoryHistorianProvider();
            using var attempted = new InMemoryHistorianProvider();
            using var later = new InMemoryHistorianProvider();
            Assert.That(registry.TryRegisterForNode(node, previous, out IDisposable? oldBinding), Is.True);
            node.Historizing = true;
            node.AccessLevel = AccessLevels.HistoryRead;
            node.UserAccessLevel = AccessLevels.HistoryRead;
            variable.Setup(value => value.FindChild(
                    It.IsAny<ISystemContext>(), new QualifiedName(BrowseNames.Annotations)))
                .Returns(() =>
                {
                    Assert.That(registry.Resolve(node.NodeId), Is.SameAs(previous));
                    Assert.Throws<InvalidOperationException>(() => registry.RegisterForNode(node.NodeId, later));
                    Assert.That(registry.Resolve(node.NodeId), Is.SameAs(previous));
                    throw new InvalidOperationException("Replacement setup failed.");
                });

            Assert.Throws<InvalidOperationException>(() =>
                builder.Variable<int>(node.NodeId).Historize(provider: attempted, autoCapture: false));
            Assert.That(registry.Resolve(node.NodeId), Is.SameAs(previous));
            Assert.That(node.Historizing, Is.True);
            Assert.That(node.AccessLevel, Is.EqualTo(AccessLevels.HistoryRead));
            Assert.That(node.UserAccessLevel, Is.EqualTo(AccessLevels.HistoryRead));
            oldBinding!.Dispose();
            Assert.That(registry.Resolve(node.NodeId), Is.Null);
            Assert.That(node.Historizing, Is.False);
        }

        /// <summary>
        /// Registry shutdown during fluent setup cannot report a successful unpublished replacement.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ExplicitProviderReplacementReportsShutdownAndRestoresAttributesAsync(
            bool failSetup,
            bool autoCapture)
        {
            using var registry = new HistorianProviderRegistry(new NamespaceTable());
            var builders = new List<HistorianBuilder>();
            IServerInternal server = CreateServerWithRegistry(builders, registry);
            var variable = new Mock<BaseDataVariableState>(MockBehavior.Default, (NodeState?)null)
            {
                CallBase = true
            };
            (NodeManagerBuilder builder, BaseDataVariableState node) =
                CreateBuilderForServer(server, variable.Object);
            node.AccessLevel = AccessLevels.CurrentRead;
            node.UserAccessLevel = AccessLevels.CurrentRead;
            using var previous = new InMemoryHistorianProvider();
            using var attempted = new InMemoryHistorianProvider();
            registry.RegisterForNode(node.NodeId, previous);
            var setupFailure = new InvalidOperationException("Setup failed during shutdown.");
            variable.Setup(value => value.FindChild(
                    It.IsAny<ISystemContext>(), new QualifiedName(BrowseNames.Annotations)))
                .Returns(() =>
                {
                    registry.Dispose();
                    if (failSetup)
                    {
                        throw setupFailure;
                    }
                    return null;
                });

            if (failSetup)
            {
                Assert.That(Assert.Throws<InvalidOperationException>(() =>
                    builder.Variable<int>(node.NodeId).Historize(provider: attempted, autoCapture: autoCapture)),
                    Is.SameAs(setupFailure));
            }
            else
            {
                Assert.Throws<ObjectDisposedException>(() =>
                    builder.Variable<int>(node.NodeId).Historize(provider: attempted, autoCapture: autoCapture));
            }
            Assert.That(registry.Resolve(node.NodeId), Is.Null);
            Assert.That(registry.Providers.IsEmpty, Is.True);
            Assert.That(node.Historizing, Is.False);
            Assert.That(node.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(node.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            node.Value = 987;
            node.Timestamp = DateTime.UtcNow;
            node.ClearChangeMasks(builder.Context, false);
            foreach (HistorianBuilder captureBuilder in builders)
            {
                await captureBuilder.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(await CountHistoryAsync(attempted, builder.Context, node.NodeId).ConfigureAwait(false),
                Is.Zero);
        }

        /// <summary>
        /// A custom replacement's rollback error cannot hide the initiating setup or commit failure.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitProviderReplacementPreservesSetupAndRollbackFailuresAsync(bool failCommit)
        {
            var registry = new Mock<IHistorianProviderRegistry>(MockBehavior.Strict);
            var replacement = new Mock<IHistorianNodeReplacement>(MockBehavior.Strict);
            registry.As<IHistorianNodeReplacementRegistry>()
                .Setup(value => value.BeginNodeReplacement(
                    It.IsAny<BaseVariableState>(), It.IsAny<IHistorianProvider>()))
                .Returns(replacement.Object);
            var builders = new List<HistorianBuilder>();
            IServerInternal server = CreateServerWithRegistry(builders, registry.Object);
            var variable = new Mock<BaseDataVariableState>(MockBehavior.Default, (NodeState?)null)
            {
                CallBase = true
            };
            (NodeManagerBuilder builder, BaseDataVariableState node) =
                CreateBuilderForServer(server, variable.Object);
            using var provider = new InMemoryHistorianProvider();
            var setupFailure = new InvalidOperationException("The replacement setup failed.");
            var rollbackFailure = new InvalidOperationException("The replacement rollback failed.");
            variable.Setup(value => value.FindChild(
                    It.IsAny<ISystemContext>(), new QualifiedName(BrowseNames.Annotations)))
                .Returns(() => failCommit ? null : throw setupFailure);
            replacement.Setup(value => value.Commit()).Throws(setupFailure);
            replacement.Setup(value => value.Dispose()).Throws(rollbackFailure);

            AggregateException? failure = Assert.Throws<AggregateException>(() =>
                builder.Variable<int>(node.NodeId).Historize(provider: provider));

            Assert.That(failure!.Flatten().InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(failure.Flatten().InnerExceptions[0], Is.SameAs(setupFailure));
            Assert.That(failure.Flatten().InnerExceptions[1], Is.SameAs(rollbackFailure));
            replacement.Verify(value => value.Commit(), failCommit ? Times.Once() : Times.Never());
            replacement.Verify(value => value.Dispose(), Times.Once);
            registry.As<IHistorianNodeReplacementRegistry>()
                .Verify(value => value.BeginNodeReplacement(node, provider), Times.Once);
            replacement.VerifyNoOtherCalls();
            registry.VerifyNoOtherCalls();
            node.Value = 987;
            node.Timestamp = DateTime.UtcNow;
            node.ClearChangeMasks(builder.Context, false);
            foreach (HistorianBuilder captureBuilder in builders)
            {
                await captureBuilder.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(await CountHistoryAsync(provider, builder.Context, node.NodeId).ConfigureAwait(false),
                Is.Zero);
        }

        /// <summary>
        /// A custom scoped registry without replacement support rejects only an existing exact-node binding.
        /// </summary>
        [Test]
        public void ExplicitProviderRejectsUnsupportedScopedReplacementBeforeMutation()
        {
            var registry = new Mock<IHistorianProviderRegistry>(MockBehavior.Strict);
            registry.As<IHistorianNodeBindingRegistry>();
            var builders = new List<HistorianBuilder>();
            IServerInternal server = CreateServerWithRegistry(builders, registry.Object);
            (NodeManagerBuilder builder, BaseDataVariableState node) = CreateBuilderForServer(server);
            using var provider = new InMemoryHistorianProvider();
            IDisposable? registration = null;
            registry.As<IHistorianNodeBindingRegistry>()
                .Setup(value => value.TryRegisterForNode(node, provider, out registration))
                .Returns(false);
            node.AccessLevel = AccessLevels.CurrentRead;
            node.UserAccessLevel = AccessLevels.CurrentRead;

            Assert.Throws<NotSupportedException>(() =>
                builder.Variable<int>(node.NodeId).Historize(provider: provider, autoCapture: false));

            Assert.That(node.Historizing, Is.False);
            Assert.That(node.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(node.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(builders, Is.Empty);
            registry.As<IHistorianNodeBindingRegistry>()
                .Verify(value => value.TryRegisterForNode(node, provider, out registration), Times.Once);
            registry.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Custom scoped registries can add unrelated nodes without implementing replacement transactions.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ExplicitProviderAddsUnrelatedNodeOnCustomScopedRegistry(bool failSetup)
        {
            using var backing = new HistorianProviderRegistry(new NamespaceTable());
            using var unrelatedProvider = new InMemoryHistorianProvider();
            var unrelated = new NodeId("other", kNs);
            backing.RegisterForNode(unrelated, unrelatedProvider);
            var registry = new Mock<IHistorianProviderRegistry>(MockBehavior.Strict);
            registry.As<IHistorianNodeBindingRegistry>();
            IDisposable? acquired = null;
            var variable = new Mock<BaseDataVariableState>(MockBehavior.Default, (NodeState?)null)
            {
                CallBase = true
            };
            IServerInternal server = CreateServerWithRegistry(registryOverride: registry.Object);
            (NodeManagerBuilder builder, BaseDataVariableState node) =
                CreateBuilderForServer(server, variable.Object);
            using var provider = new InMemoryHistorianProvider();
            Assert.That(backing.TryRegisterForNode(node, provider, out acquired), Is.True);
            registry.As<IHistorianNodeBindingRegistry>()
                .Setup(value => value.TryRegisterForNode(node, provider, out acquired)).Returns(true);
            variable.Setup(value => value.FindChild(
                    It.IsAny<ISystemContext>(), new QualifiedName(BrowseNames.Annotations)))
                .Returns(() => failSetup ? throw new InvalidOperationException("Lookup failed.") : null);

            if (failSetup)
            {
                Assert.Throws<InvalidOperationException>(() =>
                    builder.Variable<int>(node.NodeId).Historize(provider: provider, autoCapture: false));
                Assert.That(backing.Resolve(node.NodeId), Is.Null);
                Assert.That(node.Historizing, Is.False);
                Assert.That(node.AccessLevel & AccessLevels.HistoryRead, Is.Zero);
                Assert.That(node.UserAccessLevel & AccessLevels.HistoryRead, Is.Zero);
            }
            else
            {
                builder.Variable<int>(node.NodeId).Historize(provider: provider, autoCapture: false);
                Assert.That(backing.Resolve(node.NodeId), Is.SameAs(provider));
                Assert.That(node.Historizing, Is.True);
                Assert.That(node.AccessLevel & AccessLevels.HistoryRead, Is.EqualTo(AccessLevels.HistoryRead));
                Assert.That(node.UserAccessLevel & AccessLevels.HistoryRead, Is.EqualTo(AccessLevels.HistoryRead));
            }
            Assert.That(backing.Resolve(unrelated), Is.SameAs(unrelatedProvider));
            registry.As<IHistorianNodeBindingRegistry>()
                .Verify(value => value.TryRegisterForNode(node, provider, out acquired), Times.Once);
            registry.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Registries without scoped ownership retain the existing explicit-provider fluent behavior.
        /// </summary>
        [Test]
        public void ExplicitProviderPreservesLegacyCustomRegistryBehavior()
        {
            var registry = new Mock<IHistorianProviderRegistry>(MockBehavior.Strict);
            IServerInternal server = CreateServerWithRegistry(registryOverride: registry.Object);
            (NodeManagerBuilder builder, BaseDataVariableState node) = CreateBuilderForServer(server);
            using var provider = new InMemoryHistorianProvider();
            registry.Setup(value => value.RegisterForNode(node.NodeId, provider))
                .Callback(() => Assert.That(node.Historizing, Is.True));

            builder.Variable<int>(node.NodeId).Historize(provider: provider, autoCapture: false);

            Assert.That(node.Historizing, Is.True);
            Assert.That(node.AccessLevel & AccessLevels.HistoryRead, Is.EqualTo(AccessLevels.HistoryRead));
            Assert.That(node.UserAccessLevel & AccessLevels.HistoryRead, Is.EqualTo(AccessLevels.HistoryRead));
            registry.Verify(value => value.RegisterForNode(node.NodeId, provider), Times.Once);
            registry.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Verifies that a builder disposed by its owner leaves the server's shutdown list.
        /// </summary>
        [Test]
        public async Task DisposedBuilderUnregistersFromServerAsync()
        {
            var builders = new List<HistorianBuilder>();
            IServerInternal server = CreateServerWithRegistry(builders);
            Mock<IHistorianBuilderRegistry> lifecycle = Mock.Get(server).As<IHistorianBuilderRegistry>();
            lifecycle
                .Setup(value => value.UnregisterHistorianBuilder(It.IsAny<HistorianBuilder>()))
                .Callback<HistorianBuilder>(builder => builders.Remove(builder));
            var builder = new HistorianBuilder(server);
            Assert.That(builders, Is.Empty);
            using var provider = new InMemoryHistorianProvider();
            builder.UseProvider(provider).RegisterAsDefault();
            Assert.That(builders, Has.Count.EqualTo(1));
            Assert.That(builders[0], Is.SameAs(builder));

            await builder.DisposeAsync().ConfigureAwait(false);

            Assert.That(builders, Is.Empty);
            lifecycle.Verify(value => value.RegisterHistorianBuilder(builder), Times.Once);
            lifecycle.Verify(value => value.UnregisterHistorianBuilder(builder), Times.Once);
        }

        /// <summary>
        /// Verifies that owner disposal and full server shutdown, started in either order, both wait
        /// for the single in-flight capture drain, and that the server disposes the historian
        /// registry and its providers only after that drain has finished.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task OwnerDisposalAndServerShutdownAwaitTheSameCaptureDrainAsync(bool ownerFirst)
        {
            var gate = new GatedCaptureProvider();
            ServerInternalData server = CreateServerInternalData(new FakeTimeProvider());
            try
            {
                HistorianBuilder builder = StartGatedCapture(server, gate);
                await gate.Entered.WaitAsync(s_raceTimeout).ConfigureAwait(false);

                (Task owner, Task shutdown) = StartRacingDisposals(server, builder, ownerFirst);

                Assert.That(owner.IsCompleted, Is.False);
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(gate.Events, Is.Empty);

                gate.Release();
                await owner.WaitAsync(s_raceTimeout).ConfigureAwait(false);
                await shutdown.WaitAsync(s_raceTimeout).ConfigureAwait(false);

                Assert.That(gate.Events, Is.EqualTo(s_drainThenDispose));
                Assert.That(builder.DisposeAsync().AsTask().IsCompleted, Is.True);
            }
            finally
            {
                gate.Release();
                await server.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that a capture drain failure surfaces to both the owner and the server shutdown,
        /// whichever of them started the drain.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task CaptureDrainFailureSurfacesToOwnerAndServerShutdownAsync(bool ownerFirst)
        {
            var gate = new GatedCaptureProvider();
            var timeProvider = new FakeTimeProvider();
            ServerInternalData server = CreateServerInternalData(timeProvider);
            try
            {
                HistorianBuilder builder = StartGatedCapture(server, gate);
                await gate.Entered.WaitAsync(s_raceTimeout).ConfigureAwait(false);

                (Task owner, Task shutdown) = StartRacingDisposals(server, builder, ownerFirst);
                Assert.That(owner.IsCompleted, Is.False);
                Assert.That(shutdown.IsCompleted, Is.False);

                timeProvider.Advance(TimeSpan.FromSeconds(5));

                AggregateException? ownerFailure = await Assert.ThrowsAsync<AggregateException>(
                    async () => await owner.WaitAsync(s_raceTimeout).ConfigureAwait(false));
                Assert.That(ownerFailure?.InnerExceptions, Has.Count.EqualTo(1));
                Assert.That(ownerFailure?.InnerExceptions[0], Is.TypeOf<TimeoutException>());
                AggregateException? shutdownFailure = await Assert.ThrowsAsync<AggregateException>(
                    async () => await shutdown.WaitAsync(s_raceTimeout).ConfigureAwait(false));
                Assert.That(shutdownFailure?.InnerExceptions, Has.Count.EqualTo(1));
                Assert.That(shutdownFailure?.InnerExceptions[0], Is.SameAs(ownerFailure));
            }
            finally
            {
                gate.Release();
                await server.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that an explicitly supplied provider receives automatically captured values.
        /// </summary>
        [Test]
        public async Task ExplicitProviderReceivesAutomaticCaptureAsync()
        {
            var builders = new List<HistorianBuilder>();
            IServerInternal server = CreateServerWithRegistry(builders);
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderForServer(server);
            using var defaultProvider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions
                {
                    RawDataRetentionPeriod = TimeSpan.Zero
                });
            using var perCallProvider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions
                {
                    RawDataRetentionPeriod = TimeSpan.Zero
                });
            b.UseHistorian()
                .UseProvider(defaultProvider)
                .RegisterAsDefault();
            b.Variable<int>(v.NodeId).Historize(
                provider: perCallProvider,
                captureOptions: new HistorianCaptureOptions
                {
                    BatchTarget = 1,
                    BatchWindow = TimeSpan.Zero
                });
            v.Value = 4387;
            v.Timestamp = DateTime.UtcNow;
            v.StatusCode = StatusCodes.Good;
            v.ClearChangeMasks(b.Context, includeChildren: false);
            for (int i = 0; i < builders.Count; i++)
            {
                await builders[i].DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(
                await CountHistoryAsync(
                    perCallProvider,
                    b.Context,
                    v.NodeId).ConfigureAwait(false),
                Is.EqualTo(1));
            Assert.That(
                await CountHistoryAsync(
                    defaultProvider,
                    b.Context,
                    v.NodeId).ConfigureAwait(false),
                Is.Zero);
        }

        /// <summary>
        /// Verifies that concurrent builder disposal waits for its capture owner and shares a timeout failure.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConcurrentBuilderDisposalJoinsCaptureDrainAsync(bool timeoutCapture)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var timeProvider = new FakeTimeProvider();
            var builders = new List<HistorianBuilder>();
            var unregistrationSignals =
                new ConcurrentDictionary<HistorianBuilder, TaskCompletionSource<bool>>();
            IServerInternal server = CreateServerWithRegistry(builders, timeProvider: timeProvider);
            Mock<IHistorianBuilderRegistry> lifecycle = Mock.Get(server).As<IHistorianBuilderRegistry>();
            lifecycle
                .Setup(value => value.UnregisterHistorianBuilder(It.IsAny<HistorianBuilder>()))
                .Callback<HistorianBuilder>(builder =>
                {
                    builders.Remove(builder);
                    if (unregistrationSignals.TryGetValue(builder, out TaskCompletionSource<bool>? signal))
                    {
                        signal.TrySetResult(true);
                    }
                });
            (NodeManagerBuilder nodeBuilder, BaseDataVariableState variable) = CreateBuilderForServer(server);
            using var inner = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                RawDataRetentionPeriod = TimeSpan.Zero
            });
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IHistorianProvider>();
            NodeId capturedNode = default;
            ArrayOf<DataValue> capturedValues = default;
            provider.As<IHistorianDataProvider>()
                .Setup(value => value.InsertAsync(
                    It.IsAny<HistorianOperationContext>(), It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<DataValue>>(), It.IsAny<CancellationToken>()))
                .Returns(async (HistorianOperationContext context, NodeId nodeId,
                    ArrayOf<DataValue> values, CancellationToken _) =>
                {
                    capturedNode = nodeId;
                    capturedValues = values;
                    entered.TrySetResult(true);
                    try
                    {
                        await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        return await inner.InsertAsync(context, nodeId, values, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        finished.TrySetResult(true);
                    }
                });
            HistorianBuilder historian = nodeBuilder.UseHistorian().UseProvider(provider.Object);
            var historianUnregistered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            unregistrationSignals[historian] = historianUnregistered;
            historian.Historize(variable, systemContext: nodeBuilder.Context,
                captureOptions: new HistorianCaptureOptions
                {
                    BatchTarget = 1,
                    BatchWindow = TimeSpan.Zero
                });
            variable.Value = 42;
            variable.ClearChangeMasks(nodeBuilder.Context, includeChildren: false);
            Task? disposal = null;
            AggregateException? terminalFailure = null;
            HistorianBuilder? successorHistorian = null;
            BaseDataVariableState? successorVariable = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Task disposing = historian.DisposeAsync().AsTask();
                disposal = disposing;
                Task joining = historian.DisposeAsync().AsTask();

                Assert.That(disposing.IsCompleted, Is.False);
                Assert.That(joining.IsCompleted, Is.False,
                    "A concurrent disposer must join the capture drain, not acknowledge transferred resources.");
                Assert.That(() => historian.Historize(variable, systemContext: nodeBuilder.Context),
                    Throws.TypeOf<ObjectDisposedException>());
                await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await historian.HistorizeEventsAsync(
                        new BaseObjectState(null) { NodeId = new NodeId("disposing-notifier", 1) },
                        nodeBuilder.Context,
                        installConfiguration: false).ConfigureAwait(false)).ConfigureAwait(false);
                variable.Value = 84;
                variable.ClearChangeMasks(nodeBuilder.Context, includeChildren: false);

                if (timeoutCapture)
                {
                    timeProvider.Advance(TimeSpan.FromSeconds(5));
                    AggregateException? failure = await Assert.CatchAsync<AggregateException>(
                        () => disposing).ConfigureAwait(false);
                    AggregateException? joinedFailure = await Assert.CatchAsync<AggregateException>(
                        () => joining).ConfigureAwait(false);
                    Assert.That(failure!.InnerExceptions, Has.Count.EqualTo(1));
                    Assert.That(failure.InnerExceptions[0], Is.TypeOf<TimeoutException>());
                    Assert.That(joinedFailure, Is.SameAs(failure));
                    terminalFailure = failure;
                    Assert.That(finished.Task.IsCompleted, Is.False,
                        "Timing out the drain must not claim that the provider has completed.");
                    Assert.That(builders, Does.Contain(historian),
                        "A builder with a live provider consumer must remain registered for server shutdown.");

                    (NodeManagerBuilder _, BaseDataVariableState pendingVariable) =
                        CreateBuilderForServer(server);
                    pendingVariable.NodeId = new NodeId("successor-var", kNs);
                    byte accessBefore = pendingVariable.AccessLevel;
                    byte userAccessBefore = pendingVariable.UserAccessLevel;
                    successorVariable = pendingVariable;
                    successorHistorian = new HistorianBuilder(server);
                    successorHistorian.UseProvider(provider.Object);
                    Assert.That(
                        () => successorHistorian.Historize(
                            pendingVariable,
                            systemContext: nodeBuilder.Context),
                        Throws.TypeOf<InvalidOperationException>());
                    Assert.That(pendingVariable.Historizing, Is.False);
                    Assert.That(pendingVariable.AccessLevel, Is.EqualTo(accessBefore));
                    Assert.That(pendingVariable.UserAccessLevel, Is.EqualTo(userAccessBefore));
                    Assert.That(
                        () => successorHistorian.CreateCaptureHandlerTargetReference(pendingVariable),
                        Throws.TypeOf<InvalidOperationException>());
                }
                else
                {
                    release.TrySetResult(true);
                    await Task.WhenAll(disposing, joining).WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                if (timeoutCapture)
                {
                    AggregateException? repeatedFailure = await Assert.CatchAsync<AggregateException>(
                        async () => await historian.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(repeatedFailure, Is.SameAs(terminalFailure));
                }
                else
                {
                    await historian.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                release.TrySetResult(true);
                await finished.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (disposal is { IsCompleted: false })
                {
                    await disposal.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                if (disposal is not null && timeoutCapture && disposal.IsFaulted)
                {
                    AggregateException? cleanupFailure = await Assert.CatchAsync<AggregateException>(
                        async () => await historian.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                    if (terminalFailure is not null)
                    {
                        Assert.That(cleanupFailure, Is.SameAs(terminalFailure));
                    }
                }
                else
                {
                    await historian.DisposeAsync().ConfigureAwait(false);
                }
            }

            if (successorHistorian is not null && successorVariable is not null)
            {
                await HistorianBuilder.WaitForProviderCaptureCleanupAsync(provider.Object)
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                await historianUnregistered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                successorHistorian.Historize(
                    successorVariable,
                    systemContext: nodeBuilder.Context,
                    captureOptions: new HistorianCaptureOptions
                    {
                        BatchTarget = 1,
                        BatchWindow = TimeSpan.Zero
                    });
                await successorHistorian.DisposeAsync().ConfigureAwait(false);
            }

            provider.As<IHistorianDataProvider>().Verify(value => value.InsertAsync(
                It.IsAny<HistorianOperationContext>(), variable.NodeId,
                It.IsAny<ArrayOf<DataValue>>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(capturedNode, Is.EqualTo(variable.NodeId));
            Assert.That(capturedValues.Count, Is.EqualTo(1));
            Assert.That(capturedValues[0].WrappedValue.TryGetValue(out int capturedValue), Is.True);
            Assert.That(capturedValue, Is.EqualTo(42));

            Assert.That(() => historian.Historize(variable, systemContext: nodeBuilder.Context),
                Throws.TypeOf<ObjectDisposedException>());
            variable.Value = 126;
            variable.ClearChangeMasks(nodeBuilder.Context, includeChildren: false);

            provider.As<IHistorianDataProvider>().Verify(value => value.InsertAsync(
                It.IsAny<HistorianOperationContext>(), variable.NodeId,
                It.IsAny<ArrayOf<DataValue>>(), It.IsAny<CancellationToken>()), Times.Once);

            if (timeoutCapture)
            {
                await VerifyEventCaptureTimeoutAsync().ConfigureAwait(false);
            }
            else
            {
                await historianUnregistered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(builders, Is.Empty);
            lifecycle.Verify(value => value.UnregisterHistorianBuilder(historian), Times.Once);

            async Task VerifyEventCaptureTimeoutAsync()
            {
                var eventEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var eventRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var eventFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var eventProvider = new Mock<IHistorianProvider>();
                var eventHistorianUnregistered = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                eventProvider.As<IHistorianEventProvider>()
                    .Setup(value => value.UpdateEventsAsync(
                        It.IsAny<HistorianOperationContext>(), It.IsAny<NodeId>(),
                        It.IsAny<ArrayOf<HistorianEventRecord>>(), It.IsAny<CancellationToken>()))
                    .Returns(async (HistorianOperationContext _, NodeId _,
                        ArrayOf<HistorianEventRecord> _, CancellationToken _) =>
                    {
                        eventEntered.TrySetResult(true);
                        try
                        {
                            await eventRelease.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                            return new HistorianUpdateOutcome<HistorianEventRecord>([StatusCodes.Good]);
                        }
                        finally
                        {
                            eventFinished.TrySetResult(true);
                        }
                    });
                var eventHistorian = new HistorianBuilder(server);
                unregistrationSignals[eventHistorian] = eventHistorianUnregistered;
                eventHistorian.UseProvider(eventProvider.Object);
                var notifier = new BaseObjectState(null)
                {
                    NodeId = new NodeId("event-drain", kNs),
                    BrowseName = new QualifiedName("event-drain", kNs)
                };
                int forwarded = 0;
                notifier.OnReportEventAsync = (_, _, _, _) =>
                {
                    forwarded++;
                    return default;
                };
                await eventHistorian.HistorizeEventsAsync(
                    notifier,
                    nodeBuilder.Context,
                    installConfiguration: false,
                    capabilities: HistorianNodeCapabilities.EventReadWrite with
                    {
                        EventTypes = [ObjectTypeIds.BaseEventType]
                    },
                    captureOptions: new HistorianCaptureOptions
                    {
                        BatchTarget = 1,
                        BatchWindow = TimeSpan.Zero
                    },
                    cancellationToken: timeout.Token).ConfigureAwait(false);
                var reportedEvent = new BaseEventState(null);
                reportedEvent.Initialize(
                    nodeBuilder.Context, notifier, EventSeverity.Medium, new LocalizedText("event drain"));
                await notifier.ReportEventAsync(nodeBuilder.Context, reportedEvent, timeout.Token).ConfigureAwait(false);

                Task? eventDisposal = null;
                AggregateException? terminalEventFailure = null;
                var successorEventHistorian = new HistorianBuilder(server);
                successorEventHistorian.UseProvider(eventProvider.Object);
                var successorNotifier = new BaseObjectState(null)
                {
                    NodeId = new NodeId("successor-event-drain", kNs),
                    BrowseName = new QualifiedName("successor-event-drain", kNs)
                };
                try
                {
                    await eventEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Task disposing = eventHistorian.DisposeAsync().AsTask();
                    eventDisposal = disposing;
                    Task joining = eventHistorian.DisposeAsync().AsTask();
                    Assert.That(disposing.IsCompleted, Is.False);
                    Assert.That(joining.IsCompleted, Is.False);
                    await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                        await eventHistorian.HistorizeEventsAsync(
                            notifier, nodeBuilder.Context, installConfiguration: false).ConfigureAwait(false))
                        .ConfigureAwait(false);

                    timeProvider.Advance(TimeSpan.FromSeconds(5));
                    AggregateException? failure = await Assert.CatchAsync<AggregateException>(
                        () => disposing).ConfigureAwait(false);
                    AggregateException? joinedFailure = await Assert.CatchAsync<AggregateException>(
                        () => joining).ConfigureAwait(false);
                    Assert.That(failure!.InnerExceptions, Has.Count.EqualTo(1));
                    Assert.That(failure.InnerExceptions[0], Is.TypeOf<TimeoutException>());
                    Assert.That(joinedFailure, Is.SameAs(failure));
                    terminalEventFailure = failure;
                    AggregateException? repeatedFailure = await Assert.CatchAsync<AggregateException>(
                        async () => await eventHistorian.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(repeatedFailure, Is.SameAs(failure));
                    Assert.That(eventFinished.Task.IsCompleted, Is.False);
                    Assert.That(builders, Does.Contain(eventHistorian),
                        "The timed-out event consumer must keep its builder registered.");

                    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        await successorEventHistorian.HistorizeEventsAsync(
                            successorNotifier,
                            nodeBuilder.Context,
                            installConfiguration: false,
                            capabilities: HistorianNodeCapabilities.EventReadWrite with
                            {
                                EventTypes = [ObjectTypeIds.BaseEventType]
                            }).ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(successorNotifier.EventNotifier, Is.Zero);

                    await notifier.ReportEventAsync(nodeBuilder.Context, reportedEvent, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(forwarded, Is.EqualTo(2));
                }
                finally
                {
                    eventRelease.TrySetResult(true);
                    if (eventEntered.Task.IsCompleted)
                    {
                        await eventFinished.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    if (eventDisposal is { IsFaulted: true })
                    {
                        AggregateException? cleanupFailure = await Assert.CatchAsync<AggregateException>(
                            async () => await eventHistorian.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                        if (terminalEventFailure is not null)
                        {
                            Assert.That(cleanupFailure, Is.SameAs(terminalEventFailure));
                        }
                    }
                    else
                    {
                        await eventHistorian.DisposeAsync().ConfigureAwait(false);
                    }
                }
                await HistorianBuilder.WaitForProviderCaptureCleanupAsync(eventProvider.Object)
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                await eventHistorianUnregistered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await successorEventHistorian.HistorizeEventsAsync(
                    successorNotifier,
                    nodeBuilder.Context,
                    installConfiguration: false,
                    capabilities: HistorianNodeCapabilities.EventReadWrite with
                    {
                        EventTypes = [ObjectTypeIds.BaseEventType]
                    }).ConfigureAwait(false);
                await successorEventHistorian.DisposeAsync().ConfigureAwait(false);
                Assert.That(builders, Does.Not.Contain(eventHistorian));
                lifecycle.Verify(value => value.UnregisterHistorianBuilder(eventHistorian), Times.Once);
                eventProvider.As<IHistorianEventProvider>().Verify(value => value.UpdateEventsAsync(
                    It.IsAny<HistorianOperationContext>(), notifier.NodeId,
                    It.IsAny<ArrayOf<HistorianEventRecord>>(), It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        /// <summary>
        /// Verifies that provider capture admission stays closed until every timed-out consumer completes.
        /// </summary>
        [Test]
        public async Task ProviderCaptureAdmissionWaitsForEveryTimedOutConsumerAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var timeProvider = new FakeTimeProvider();
            IServerInternal server = CreateServerWithRegistry(timeProvider: timeProvider);
            (NodeManagerBuilder firstNodeBuilder, BaseDataVariableState firstVariable) =
                CreateBuilderForServer(server);
            (NodeManagerBuilder secondNodeBuilder, BaseDataVariableState secondVariable) =
                CreateBuilderForServer(server);
            firstVariable.NodeId = new NodeId("first-pending-provider", kNs);
            secondVariable.NodeId = new NodeId("second-pending-provider", kNs);

            var entered = new TaskCompletionSource<bool>[]
            {
                new(TaskCreationOptions.RunContinuationsAsynchronously),
                new(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            var release = new TaskCompletionSource<bool>[]
            {
                new(TaskCreationOptions.RunContinuationsAsynchronously),
                new(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            var finished = new TaskCompletionSource<bool>[]
            {
                new(TaskCreationOptions.RunContinuationsAsynchronously),
                new(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            int calls = 0;
            var provider = new Mock<IHistorianProvider>();
            provider.As<IHistorianDataProvider>()
                .Setup(value => value.InsertAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<DataValue>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (
                    HistorianOperationContext _,
                    NodeId _,
                    ArrayOf<DataValue> _,
                    CancellationToken _) =>
                {
                    int slot = Interlocked.Increment(ref calls) - 1;
                    entered[slot].TrySetResult(true);
                    try
                    {
                        await release[slot].Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        return new HistorianUpdateOutcome<DataValue>([StatusCodes.Good]);
                    }
                    finally
                    {
                        finished[slot].TrySetResult(true);
                    }
                });

            HistorianBuilder firstHistorian = new HistorianBuilder(server).UseProvider(provider.Object);
            firstHistorian.Historize(
                firstVariable,
                systemContext: firstNodeBuilder.Context,
                captureOptions: new HistorianCaptureOptions
                {
                    BatchTarget = 1,
                    BatchWindow = TimeSpan.Zero
                });
            HistorianBuilder secondHistorian = new HistorianBuilder(server).UseProvider(provider.Object);
            secondHistorian.Historize(
                secondVariable,
                systemContext: secondNodeBuilder.Context,
                captureOptions: new HistorianCaptureOptions
                {
                    BatchTarget = 1,
                    BatchWindow = TimeSpan.Zero
                });

            firstVariable.Value = 1;
            firstVariable.ClearChangeMasks(firstNodeBuilder.Context, includeChildren: false);
            secondVariable.Value = 2;
            secondVariable.ClearChangeMasks(secondNodeBuilder.Context, includeChildren: false);

            Task? firstDisposal = null;
            Task? secondDisposal = null;
            AggregateException? firstTerminalFailure = null;
            AggregateException? secondTerminalFailure = null;
            try
            {
                await Task.WhenAll(entered[0].Task, entered[1].Task)
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                Task firstDispose = firstHistorian.DisposeAsync().AsTask();
                Task secondDispose = secondHistorian.DisposeAsync().AsTask();
                firstDisposal = firstDispose;
                secondDisposal = secondDispose;
                timeProvider.Advance(TimeSpan.FromSeconds(5));

                AggregateException? firstFailure = await Assert.CatchAsync<AggregateException>(
                    () => firstDispose).ConfigureAwait(false);
                AggregateException? secondFailure = await Assert.CatchAsync<AggregateException>(
                    () => secondDispose).ConfigureAwait(false);
                Assert.That(firstFailure!.InnerExceptions, Has.Some.TypeOf<TimeoutException>());
                Assert.That(secondFailure!.InnerExceptions, Has.Some.TypeOf<TimeoutException>());
                firstTerminalFailure = firstFailure;
                secondTerminalFailure = secondFailure;

                release[0].TrySetResult(true);
                await finished[0].Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                var blockedHistorian = new HistorianBuilder(server);
                blockedHistorian.UseProvider(provider.Object);
                var blockedVariable = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("still-pending-provider", kNs),
                    BrowseName = new QualifiedName("still-pending-provider", kNs)
                };
                Assert.That(
                    () => blockedHistorian.Historize(
                        blockedVariable,
                        systemContext: firstNodeBuilder.Context),
                    Throws.TypeOf<InvalidOperationException>());
                Assert.That(blockedVariable.Historizing, Is.False);
                await blockedHistorian.DisposeAsync().ConfigureAwait(false);

                release[1].TrySetResult(true);
                await HistorianBuilder.WaitForProviderCaptureCleanupAsync(provider.Object)
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                var successorHistorian = new HistorianBuilder(server);
                successorHistorian.UseProvider(provider.Object);
                var successorVariable = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("reopened-provider", kNs),
                    BrowseName = new QualifiedName("reopened-provider", kNs)
                };
                successorHistorian.Historize(
                    successorVariable,
                    systemContext: firstNodeBuilder.Context);
                await successorHistorian.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                release[0].TrySetResult(true);
                release[1].TrySetResult(true);
                if (entered[0].Task.IsCompleted)
                {
                    await finished[0].Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                if (entered[1].Task.IsCompleted)
                {
                    await finished[1].Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                foreach (Task? disposal in new[] { firstDisposal, secondDisposal })
                {
                    if (disposal is { IsCompleted: false })
                    {
                        await disposal.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                }
                await VerifyDisposalResultAsync(firstHistorian, firstDisposal, firstTerminalFailure)
                    .ConfigureAwait(false);
                await VerifyDisposalResultAsync(secondHistorian, secondDisposal, secondTerminalFailure)
                    .ConfigureAwait(false);
            }

            provider.As<IHistorianDataProvider>().Verify(value => value.InsertAsync(
                It.IsAny<HistorianOperationContext>(),
                It.IsAny<NodeId>(),
                It.IsAny<ArrayOf<DataValue>>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));

            async Task VerifyDisposalResultAsync(
                HistorianBuilder historian,
                Task? disposal,
                AggregateException? terminalFailure)
            {
                if (disposal is { IsFaulted: true })
                {
                    AggregateException? repeatedFailure = await Assert.CatchAsync<AggregateException>(
                        async () => await historian.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                    if (terminalFailure is not null)
                    {
                        Assert.That(repeatedFailure, Is.SameAs(terminalFailure));
                    }
                }
                else
                {
                    await historian.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Verifies that disposing an unused builder also prevents later capture registration.
        /// </summary>
        [Test]
        public async Task EmptyBuilderDisposalRejectsValueAndEventCaptureAsync()
        {
            IServerInternal server = CreateServerWithRegistry();
            (NodeManagerBuilder nodeBuilder, BaseDataVariableState variable) = CreateBuilderForServer(server);
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions());
            var historian = new HistorianBuilder(server);
            historian.UseProvider(provider);

            await historian.DisposeAsync().ConfigureAwait(false);
            await historian.DisposeAsync().ConfigureAwait(false);

            Assert.That(() => historian.Historize(variable, systemContext: nodeBuilder.Context),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => historian.UseProvider(provider), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => historian.RegisterAsDefault(), Throws.TypeOf<ObjectDisposedException>());
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await historian.HistorizeEventsAsync(
                    new BaseObjectState(null) { NodeId = new NodeId("disposed-notifier", 1) },
                    nodeBuilder.Context,
                    installConfiguration: false).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(variable.Historizing, Is.False);

            var registeredBuilders = new List<HistorianBuilder>();
            IServerInternal reentrantServer = CreateServerWithRegistry(registeredBuilders);
            (NodeManagerBuilder reentrantNodeBuilder, _) = CreateBuilderForServer(reentrantServer);
            Task? reentrantDisposal = null;
            Mock.Get(reentrantServer).As<IHistorianBuilderRegistry>()
                .Setup(value => value.RegisterHistorianBuilder(It.IsAny<HistorianBuilder>()))
                .Callback<HistorianBuilder>(builder => reentrantDisposal = builder.DisposeAsync().AsTask());
            Mock.Get(reentrantServer).SetupGet(value => value.Telemetry)
                .Throws(new InvalidOperationException("Event capture must not start after disposal."));
            var eventProvider = new Mock<IHistorianProvider>();
            eventProvider.As<IHistorianEventProvider>();
            var reentrantHistorian = new HistorianBuilder(reentrantServer);
            reentrantHistorian.UseProvider(eventProvider.Object);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await reentrantHistorian.HistorizeEventsAsync(
                    new BaseObjectState(null) { NodeId = new NodeId("reentrant-notifier", 1) },
                    reentrantNodeBuilder.Context,
                    installConfiguration: false,
                    capabilities: HistorianNodeCapabilities.EventReadWrite).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(reentrantDisposal, Is.Not.Null);
            await reentrantDisposal!.ConfigureAwait(false);

            await VerifyConstructionDisposalAsync(eventCapture: false, failConstruction: false).ConfigureAwait(false);
            await VerifyConstructionDisposalAsync(eventCapture: false, failConstruction: true).ConfigureAwait(false);
            await VerifyConstructionDisposalAsync(eventCapture: true, failConstruction: false).ConfigureAwait(false);
            await VerifyConstructionDisposalAsync(eventCapture: true, failConstruction: true).ConfigureAwait(false);
            await VerifyExistingCaptureDetachesDuringConstructionAsync().ConfigureAwait(false);
            await VerifyNestedConstructionDisposalAsync().ConfigureAwait(false);

            async Task VerifyConstructionDisposalAsync(bool eventCapture, bool failConstruction)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                IServerInternal constructionServer = CreateServerWithRegistry([]);
                (NodeManagerBuilder constructionNodeBuilder, BaseDataVariableState constructionVariable) =
                    CreateBuilderForServer(constructionServer);
                var constructionProvider = new Mock<IHistorianProvider>();
                constructionProvider.As<IHistorianDataProvider>();
                constructionProvider.As<IHistorianEventProvider>();
                var constructionHistorian = new HistorianBuilder(constructionServer);
                constructionHistorian.UseProvider(constructionProvider.Object);
                var failure = new InvalidOperationException("The capture time provider failed.");
                Task? constructionDisposal = null;
                bool disposalPendingInCallback = false;
                Mock.Get(constructionServer).As<ITimeProviderProvider>()
                    .SetupGet(value => value.TimeProvider)
                    .Returns(() =>
                    {
                        constructionDisposal = constructionHistorian.DisposeAsync().AsTask();
                        disposalPendingInCallback = !constructionDisposal.IsCompleted;
                        if (failConstruction)
                        {
                            throw failure;
                        }
                        return TimeProvider.System;
                    });

                if (eventCapture)
                {
                    var notifier = new BaseObjectState(null)
                    {
                        NodeId = new NodeId("construction-notifier", kNs),
                        EventNotifier = EventNotifiers.SubscribeToEvents
                    };
                    if (failConstruction)
                    {
                        await Assert.ThatAsync(async () =>
                            await constructionHistorian.HistorizeEventsAsync(
                                notifier,
                                constructionNodeBuilder.Context,
                                installConfiguration: false,
                                capabilities: HistorianNodeCapabilities.EventReadWrite).ConfigureAwait(false),
                            Throws.Exception.SameAs(failure)).ConfigureAwait(false);
                    }
                    else
                    {
                        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                            await constructionHistorian.HistorizeEventsAsync(
                                notifier,
                                constructionNodeBuilder.Context,
                                installConfiguration: false,
                                capabilities: HistorianNodeCapabilities.EventReadWrite).ConfigureAwait(false))
                            .ConfigureAwait(false);
                    }
                    Assert.That(notifier.EventNotifier, Is.EqualTo(EventNotifiers.SubscribeToEvents));
                }
                else if (failConstruction)
                {
                    Assert.That(
                        () => constructionHistorian.Historize(
                            constructionVariable, systemContext: constructionNodeBuilder.Context),
                        Throws.Exception.SameAs(failure));
                }
                else
                {
                    Assert.That(
                        () => constructionHistorian.Historize(
                            constructionVariable, systemContext: constructionNodeBuilder.Context),
                        Throws.TypeOf<ObjectDisposedException>());
                }

                Assert.That(constructionDisposal, Is.Not.Null);
                Assert.That(disposalPendingInCallback, Is.True);
                await constructionDisposal!.WaitAsync(timeout.Token).ConfigureAwait(false);
                await constructionHistorian.DisposeAsync().ConfigureAwait(false);
                Assert.That(() => constructionHistorian.UseProvider(constructionProvider.Object),
                    Throws.TypeOf<ObjectDisposedException>());
                if (!eventCapture)
                {
                    Assert.That(
                        () => constructionHistorian.CreateCaptureHandlerTargetReference(constructionVariable),
                        Throws.TypeOf<InvalidOperationException>());
                }
            }

            async Task VerifyExistingCaptureDetachesDuringConstructionAsync()
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                IServerInternal constructionServer = CreateServerWithRegistry([]);
                (NodeManagerBuilder constructionNodeBuilder, BaseDataVariableState constructionVariable) =
                    CreateBuilderForServer(constructionServer);
                var constructionProvider = new Mock<IHistorianProvider>();
                Mock<IHistorianDataProvider> dataProvider = constructionProvider.As<IHistorianDataProvider>();
                constructionProvider.As<IHistorianEventProvider>();
                dataProvider.Setup(value => value.InsertAsync(
                    It.IsAny<HistorianOperationContext>(), It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<DataValue>>(), It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<HistorianUpdateOutcome<DataValue>>(
                        new HistorianUpdateOutcome<DataValue>([StatusCodes.Good])));
                var constructionHistorian = new HistorianBuilder(constructionServer);
                constructionHistorian.UseProvider(constructionProvider.Object);
                constructionHistorian.Historize(
                    constructionVariable,
                    systemContext: constructionNodeBuilder.Context,
                    captureOptions: new HistorianCaptureOptions
                    {
                        BatchTarget = 1,
                        BatchWindow = TimeSpan.Zero
                    });
                Task? constructionDisposal = null;
                Mock.Get(constructionServer).As<ITimeProviderProvider>()
                    .SetupGet(value => value.TimeProvider)
                    .Returns(() =>
                    {
                        constructionDisposal = constructionHistorian.DisposeAsync().AsTask();
                        constructionVariable.Value = 42;
                        constructionVariable.ClearChangeMasks(constructionNodeBuilder.Context, includeChildren: false);
                        return TimeProvider.System;
                    });
                var notifier = new BaseObjectState(null)
                {
                    NodeId = new NodeId("construction-detach-notifier", kNs)
                };
                await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await constructionHistorian.HistorizeEventsAsync(
                        notifier,
                        constructionNodeBuilder.Context,
                        installConfiguration: false,
                        capabilities: HistorianNodeCapabilities.EventReadWrite).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(constructionDisposal, Is.Not.Null);
                await constructionDisposal!.WaitAsync(timeout.Token).ConfigureAwait(false);
                dataProvider.Verify(value => value.InsertAsync(
                    It.IsAny<HistorianOperationContext>(), It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<DataValue>>(), It.IsAny<CancellationToken>()), Times.Never);
            }

            async Task VerifyNestedConstructionDisposalAsync()
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                IServerInternal constructionServer = CreateServerWithRegistry([]);
                (NodeManagerBuilder constructionNodeBuilder, BaseDataVariableState outerVariable) =
                    CreateBuilderForServer(constructionServer);
                var innerVariable = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("nested-construction", kNs),
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar
                };
                var constructionProvider = new Mock<IHistorianProvider>();
                constructionProvider.As<IHistorianDataProvider>();
                var constructionHistorian = new HistorianBuilder(constructionServer);
                constructionHistorian.UseProvider(constructionProvider.Object);
                Task? constructionDisposal = null;
                int timeProviderReads = 0;
                Mock.Get(constructionServer).As<ITimeProviderProvider>()
                    .SetupGet(value => value.TimeProvider)
                    .Returns(() =>
                    {
                        if (++timeProviderReads == 1)
                        {
                            Assert.That(
                                () => constructionHistorian.Historize(
                                    innerVariable, systemContext: constructionNodeBuilder.Context),
                                Throws.TypeOf<ObjectDisposedException>());
                            Assert.That(constructionDisposal, Is.Not.Null);
                            Assert.That(constructionDisposal!.IsCompleted, Is.False);
                        }
                        else
                        {
                            constructionDisposal = constructionHistorian.DisposeAsync().AsTask();
                            Assert.That(constructionDisposal.IsCompleted, Is.False);
                        }
                        return TimeProvider.System;
                    });
                Assert.That(
                    () => constructionHistorian.Historize(
                        outerVariable, systemContext: constructionNodeBuilder.Context),
                    Throws.TypeOf<ObjectDisposedException>());
                Assert.That(timeProviderReads, Is.EqualTo(2));
                Assert.That(constructionDisposal, Is.Not.Null);
                await constructionDisposal!.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(
                    () => constructionHistorian.CreateCaptureHandlerTargetReference(innerVariable),
                    Throws.TypeOf<InvalidOperationException>());
                Assert.That(
                    () => constructionHistorian.CreateCaptureHandlerTargetReference(outerVariable),
                    Throws.TypeOf<InvalidOperationException>());
            }
        }

        /// <summary>
        /// Prevents an event capture started before disposal from attaching after its capability lookup completes.
        /// </summary>
        [Test]
        public async Task DisposalDuringEventCapabilityLookupPreventsCaptureAttachmentAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            IServerInternal server = CreateServerWithRegistry();
            (NodeManagerBuilder nodeBuilder, _) = CreateBuilderForServer(server);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var notifier = new BaseObjectState(null)
            {
                NodeId = new NodeId("pending-notifier", 1),
                EventNotifier = EventNotifiers.SubscribeToEvents
            };
            var provider = new Mock<IHistorianProvider>();
            provider.Setup(value => value.GetCapabilitiesAsync(
                notifier.NodeId, It.IsAny<CancellationToken>()))
                .Returns(async (NodeId _, CancellationToken cancellationToken) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return HistorianNodeCapabilities.EventReadWrite;
                });
            var historian = new HistorianBuilder(server);
            historian.UseProvider(provider.Object);
            Task capture = historian.HistorizeEventsAsync(
                notifier,
                nodeBuilder.Context,
                installConfiguration: false,
                cancellationToken: timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await historian.DisposeAsync().ConfigureAwait(false);
                Assert.That(capture.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    async () => await capture.WaitAsync(timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                await historian.DisposeAsync().ConfigureAwait(false);
            }
            provider.Verify(value => value.GetCapabilitiesAsync(
                notifier.NodeId, It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(notifier.EventNotifier, Is.EqualTo(EventNotifiers.SubscribeToEvents));
        }

        /// <summary>
        /// Verifies that a provider selected by WithHistorian receives automatically captured values.
        /// </summary>
        [Test]
        public async Task WithHistorianProviderReceivesAutomaticCaptureAsync()
        {
            var builders = new List<HistorianBuilder>();
            IServerInternal server = CreateServerWithRegistry(builders);
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderForServer(server);
            using var defaultProvider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions
                {
                    RawDataRetentionPeriod = TimeSpan.Zero
                });
            using var perNodeProvider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions
                {
                    RawDataRetentionPeriod = TimeSpan.Zero
                });
            b.UseHistorian()
                .UseProvider(defaultProvider)
                .RegisterAsDefault();
            b.Variable<int>(v.NodeId)
                .WithHistorian(perNodeProvider)
                .Historize(
                    captureOptions: new HistorianCaptureOptions
                    {
                        BatchTarget = 1,
                        BatchWindow = TimeSpan.Zero
                    });
            v.Value = 4388;
            v.Timestamp = DateTime.UtcNow;
            v.StatusCode = StatusCodes.Good;
            v.ClearChangeMasks(b.Context, includeChildren: false);
            for (int i = 0; i < builders.Count; i++)
            {
                await builders[i].DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(
                await CountHistoryAsync(
                    perNodeProvider,
                    b.Context,
                    v.NodeId).ConfigureAwait(false),
                Is.EqualTo(1));
            Assert.That(
                await CountHistoryAsync(
                    defaultProvider,
                    b.Context,
                    v.NodeId).ConfigureAwait(false),
                Is.Zero);
        }

        /// <summary>
        /// Verifies that Historize uses the capabilities advertised by the provider.
        /// </summary>
        [Test]
        public async Task HistorizeWithCapabilitiesAdvertisedByProvider()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            var custom = new HistorianNodeCapabilities
            {
                InsertData = true,
                ReplaceData = true,
                DeleteRaw = false,
                InsertAnnotation = true,
                ServerTimestampSupported = true
            };

            b.Variable<int>(v.NodeId).Historize(capabilities: custom);

            IServerInternal server = ((ServerSystemContext)b.Context).Server;
            IHistorianProvider? provider =
                ((IHistorianRegistryProvider)server).HistorianRegistry.Resolve(v.NodeId);
            Assert.That(provider, Is.Not.Null);
            HistorianNodeCapabilities advertised = await provider!
                .GetCapabilitiesAsync(v.NodeId, default).ConfigureAwait(false);
            Assert.That(advertised.InsertAnnotation, Is.True);
            Assert.That(advertised.DeleteRaw, Is.False,
                "Provider should advertise the capability set the user supplied verbatim.");
        }

        /// <summary>
        /// Verifies that Historize is available on an untyped node builder.
        /// </summary>
        [Test]
        public void HistorizeAlsoWorksFromUntypedNodeBuilder()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();

            INodeBuilder<BaseVariableState> view = b.Node<BaseVariableState>(v.NodeId);
            view.Historize();

            Assert.That(v.Historizing, Is.True);
        }

        /// <summary>
        /// Verifies that a default literal selects the legacy untyped Historize overload.
        /// </summary>
        [Test]
        public void HistorizeDefaultLiteralUsesLegacyUntypedOverload()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderWithVariable();
            INodeBuilder<BaseVariableState> view =
                b.Node<BaseVariableState>(v.NodeId);

            view.Historize(default);

            Assert.That(v.Historizing, Is.True);
        }

        /// <summary>
        /// Verifies that untyped Historize honors explicit control of the Historizing attribute.
        /// </summary>
        [Test]
        public void UntypedHistorizeHonorsHistorizingControl()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariable();
            INodeBuilder<BaseVariableState> view = b.Node<BaseVariableState>(v.NodeId);
            v.Historizing = true;

            view.Historize(
                autoCapture: false,
                historizing: false);

            Assert.That(v.Historizing, Is.False);

            view.Historize(
                autoCapture: false,
                historizing: null);

            Assert.That(v.Historizing, Is.False);
        }

        /// <summary>
        /// Verifies that Historize fails cleanly when the server does not expose a historian registry.
        /// </summary>
        [Test]
        public void HistorizeFailsCleanlyWhenServerLacksRegistry()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) = CreateBuilderWithVariableNoRegistry();

            Assert.That(
                () => b.Variable<int>(v.NodeId).Historize(),
                Throws.TypeOf<InvalidOperationException>());
        }

        private static (NodeManagerBuilder Builder, BaseDataVariableState Var) CreateBuilderWithVariable()
        {
            IServerInternal server = CreateServerWithRegistry();
            return CreateBuilderForServer(server);
        }

        private static (NodeManagerBuilder Builder, BaseDataVariableState Var) CreateBuilderWithVariableNoRegistry()
        {
            IServerInternal server = CreateServerWithoutRegistry();
            return CreateBuilderForServer(server);
        }

        private static (NodeManagerBuilder Builder, BaseDataVariableState Var) CreateBuilderForServer(
            IServerInternal server,
            BaseDataVariableState? variable = null)
        {
            var ctx = new ServerSystemContext(server);
            BaseDataVariableState var1 = variable ?? new BaseDataVariableState(parent: null);
            var1.NodeId = new NodeId("Var1", kNs);
            var1.BrowseName = new QualifiedName("Var1", kNs);
            var1.DisplayName = new LocalizedText("Var1");
            var1.DataType = DataTypeIds.Int32;
            var1.ValueRank = ValueRanks.Scalar;

            var roots = new Dictionary<QualifiedName, NodeState> { [var1.BrowseName] = var1 };
            var byId = new Dictionary<NodeId, NodeState> { [var1.NodeId] = var1 };

            var builder = new NodeManagerBuilder(
                ctx,
                nodeManager: Mock.Of<IAsyncNodeManager>(),
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState? n) ? n : null!,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState? n) ? n : null!,
                typeIdResolver: _ => []);
            return (builder, var1);
        }

        private static async ValueTask<int> CountHistoryAsync(
            InMemoryHistorianProvider provider,
            ISystemContext systemContext,
            NodeId nodeId)
        {
            using var requestContext = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.HistoryRead,
                RequestLifetime.None);
            var context = new HistorianOperationContext(
                (ServerSystemContext)systemContext,
                requestContext,
                null,
                HistoryUpdateType.Insert);
            HistorianPage<HistoricalDataValue> page =
                await provider.ReadRawAsync(
                    context,
                    new HistorianRawReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = DateTimeUtc.MinValue,
                        EndTime = DateTimeUtc.MaxValue,
                        IsForward = true
                    },
                    default,
                    CancellationToken.None).ConfigureAwait(false);
            return page.Values.Count;
        }

        private static IServerInternal CreateServerWithRegistry(
            List<HistorianBuilder>? builders = null,
            IHistorianProviderRegistry? registryOverride = null,
            TimeProvider? timeProvider = null)
        {
            var nsTable = new NamespaceTable();
            nsTable.Append("urn:test:fluent-historian");

            var mockTelemetry = new Mock<ITelemetryContext>();
            IHistorianProviderRegistry registry = registryOverride ?? new HistorianProviderRegistry(nsTable);

            var mockServer = new Mock<IServerInternal>();
            mockServer.Setup(s => s.NamespaceUris).Returns(nsTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(nsTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);
            mockServer.As<ITimeProviderProvider>()
                .Setup(value => value.TimeProvider).Returns(timeProvider ?? TimeProvider.System);
            mockServer.As<IHistorianRegistryProvider>()
                .Setup(p => p.HistorianRegistry).Returns(registry);
            if (builders != null)
            {
                mockServer.As<IHistorianBuilderRegistry>()
                    .Setup(value => value.RegisterHistorianBuilder(
                        It.IsAny<HistorianBuilder>()))
                    .Callback<HistorianBuilder>(builders.Add);
            }

            return mockServer.Object;
        }

        private static IServerInternal CreateServerWithoutRegistry()
        {
            var nsTable = new NamespaceTable();
            nsTable.Append("urn:test:fluent-noregistry");

            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockServer = new Mock<IServerInternal>();
            mockServer.Setup(s => s.NamespaceUris).Returns(nsTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(nsTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);
            return mockServer.Object;
        }

        private static ServerInternalData CreateServerInternalData(TimeProvider timeProvider)
        {
            var configuration = new ApplicationConfiguration
            {
                ApplicationUri = "urn:test:historian-shutdown",
                ApplicationName = "HistorianShutdownTest",
                ServerConfiguration = new ServerConfiguration
                {
                    BaseAddresses = ["opc.tcp://localhost:4840"]
                }
            };
            return new ServerInternalData(
                new ServerProperties
                {
                    ProductName = "HistorianShutdownTest",
                    ProductUri = "urn:test:product",
                    ManufacturerName = "TestManufacturer",
                    SoftwareVersion = "1.0.0",
                    BuildNumber = "1",
                    BuildDate = DateTime.UtcNow
                },
                configuration,
                ServiceMessageContext.Create(NUnitTelemetryContext.Create()),
                timeProvider);
        }

        /// <summary>
        /// Binds a gated provider to a variable of the real server, captures one value and leaves
        /// that value blocked inside the provider insert.
        /// </summary>
        private static HistorianBuilder StartGatedCapture(ServerInternalData server, GatedCaptureProvider gate)
        {
            var variable = new BaseDataVariableState(parent: null)
            {
                NodeId = new NodeId("GatedCapture", 0),
                BrowseName = new QualifiedName("GatedCapture", 0),
                DisplayName = new LocalizedText("GatedCapture"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };
            server.HistorianRegistry.RegisterForNode(variable.NodeId, gate.Provider);
            HistorianBuilder builder = new HistorianBuilder(server)
                .UseProvider(gate.Provider, provisionNodes: false)
                .Historize(
                    variable,
                    systemContext: server.DefaultSystemContext,
                    captureOptions: new HistorianCaptureOptions
                    {
                        BatchTarget = 1,
                        BatchWindow = TimeSpan.Zero
                    });
            variable.Value = 7;
            variable.Timestamp = DateTime.UtcNow;
            variable.StatusCode = StatusCodes.Good;
            variable.ClearChangeMasks(server.DefaultSystemContext, includeChildren: false);
            return builder;
        }

        /// <summary>
        /// Starts owner disposal and full server shutdown in the requested order.
        /// </summary>
        private static (Task Owner, Task Shutdown) StartRacingDisposals(
            ServerInternalData server,
            HistorianBuilder builder,
            bool ownerFirst)
        {
            if (ownerFirst)
            {
                Task owner = builder.DisposeAsync().AsTask();
                return (owner, server.DisposeAsync().AsTask());
            }
            Task shutdown = server.DisposeAsync().AsTask();
            return (builder.DisposeAsync().AsTask(), shutdown);
        }

        /// <summary>
        /// A per-node data provider whose insert blocks until released and which records the
        /// completed insert and its own disposal in order.
        /// </summary>
        private sealed class GatedCaptureProvider
        {
            /// <summary>
            /// Creates the gated provider.
            /// </summary>
            public GatedCaptureProvider()
            {
                var provider = new Mock<IHistorianProvider>();
                provider.As<IHistorianDataProvider>()
                    .Setup(value => value.InsertAsync(
                        It.IsAny<HistorianOperationContext>(),
                        It.IsAny<NodeId>(),
                        It.IsAny<ArrayOf<DataValue>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<HistorianOperationContext, NodeId, ArrayOf<DataValue>, CancellationToken>(
                        (_, _, values, _) => InsertAsync(values.Count));
                provider.As<IDisposable>()
                    .Setup(value => value.Dispose())
                    .Callback(() => m_events.Enqueue("disposed"));
                Provider = provider.Object;
            }

            /// <summary>
            /// The provider registered with the server and bound to the builder.
            /// </summary>
            public IHistorianProvider Provider { get; }

            /// <summary>
            /// Completes when the capture pipeline has entered the provider insert.
            /// </summary>
            public Task Entered => m_entered.Task;

            /// <summary>
            /// The completed insert and disposal, in the order they happened.
            /// </summary>
            public string[] Events => [.. m_events];

            /// <summary>
            /// Lets the blocked insert complete.
            /// </summary>
            public void Release()
            {
                m_release.TrySetResult(true);
            }

            private async ValueTask<HistorianUpdateOutcome<DataValue>> InsertAsync(int count)
            {
                m_entered.TrySetResult(true);
                await m_release.Task.ConfigureAwait(false);
                m_events.Enqueue("inserted");
                return new HistorianUpdateOutcome<DataValue>(new StatusCode[count]);
            }

            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly ConcurrentQueue<string> m_events = new();
        }

        private static readonly TimeSpan s_raceTimeout = TimeSpan.FromSeconds(30);
        private static readonly string[] s_drainThenDispose = ["inserted", "disposed"];
    }
}
