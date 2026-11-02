/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AMB.Client;
using Opc.Ua.AMB.Server;
using Opc.Ua.Di;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;
using AmbClientOptions = AmbSample.Client.AmbClientOptions;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Verifies opt-in configuration and the typed health sample's original evidence.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    public sealed class HistoryOptionsTests
    {
        /// <summary>
        /// Gets the sample qualities and whether they expose a good typed health value.
        /// </summary>
        private static IEnumerable<TestCaseData> HealthQualities =>
        [
            new TestCaseData((uint)StatusCodes.Good, true),
            new TestCaseData((uint)StatusCodes.Uncertain, false),
            new TestCaseData((uint)StatusCodes.BadNoData, false)
        ];

        [Test]
        public void ScopedRegistryBindingPreservesFallbacksAndRejectsAnExistingExactOwner()
        {
            var namespaces = new NamespaceTable();
            ushort index = namespaces.GetIndexOrAppend("urn:amb:scoped-registry");
            var node = new NodeId("health", index);
            using var registry = new HistorianProviderRegistry(namespaces);
            using var fallback = new InMemoryHistorianProvider();
            using var namespaceProvider = new InMemoryHistorianProvider();
            using var capture = new InMemoryHistorianProvider();
            registry.RegisterDefault(fallback);
            registry.RegisterForNamespace("urn:amb:scoped-registry", namespaceProvider);
            Assert.That(registry.TryRegisterForNode(node, capture, out IDisposable? binding), Is.True);
            using (binding)
            {
                Assert.That(registry.Resolve(node), Is.SameAs(capture));
                Assert.That(registry.TryRegisterForNode(node, fallback, out IDisposable? rejected), Is.False);
                Assert.That(rejected, Is.Null);
                Assert.That(registry.Resolve(node), Is.SameAs(capture));
            }
            Assert.That(registry.Resolve(node), Is.SameAs(namespaceProvider));
            Assert.That(registry.UnregisterForNamespace("urn:amb:scoped-registry"), Is.True);
            Assert.That(registry.Resolve(node), Is.SameAs(fallback));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MetadataReleaseExcludesReplacementAndRetainsItsCompletedOutcomeAsync(bool failCleanup)
        {
            using var registry = new HistorianProviderRegistry(new NamespaceTable());
            using var provider = new InMemoryHistorianProvider();
            using var successor = new InMemoryHistorianProvider();
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("health", 1),
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead
            };
            Assert.That(registry.TryRegisterForNode(node, provider, out IDisposable? registration), Is.True);
            var binding = (IHistorianNodeBinding)registration!;
            node.Historizing = true;
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleanupFailure = new InvalidOperationException("Metadata cleanup failed.");
            int calls = 0;
            async ValueTask CleanupAsync()
            {
                calls++;
                started.SetResult(true);
                await finish.Task.ConfigureAwait(false);
                if (failCleanup)
                {
                    throw cleanupFailure;
                }
            }
            Task release = binding.ReleaseAsync(CleanupAsync).AsTask();
            await started.Task.ConfigureAwait(false);
            Assert.That(registry.Resolve(node.NodeId), Is.SameAs(provider));
            Assert.That(registry.TryRegisterForNode(node, successor, out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => registry.RegisterForNode(node.NodeId, successor));
            Assert.Throws<InvalidOperationException>(() =>
                ((IHistorianNodeReplacementRegistry)registry).BeginNodeReplacement(node, successor));
            Task repeated = binding.ReleaseAsync(() => throw new AssertionException("Cleanup repeated.")).AsTask();
            Assert.That(repeated, Is.SameAs(release));
            finish.SetResult(true);
            if (failCleanup)
            {
                Assert.That(await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await release.ConfigureAwait(false)), Is.SameAs(cleanupFailure));
                Assert.That(await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await repeated.ConfigureAwait(false)), Is.SameAs(cleanupFailure));
            }
            else
            {
                await release.ConfigureAwait(false);
                await repeated.ConfigureAwait(false);
            }
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(node.Historizing, Is.False);
            Assert.That(node.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(node.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(registry.Resolve(node.NodeId), Is.Null);
            registry.RegisterForNode(node.NodeId, successor);
            Assert.That(registry.Resolve(node.NodeId), Is.SameAs(successor));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task MetadataReleaseWaitsForReplacementWithoutTouchingASuccessorAsync(
            bool commit,
            bool shutdown)
        {
            using var registry = new HistorianProviderRegistry(new NamespaceTable());
            using var provider = new InMemoryHistorianProvider();
            using var successor = new InMemoryHistorianProvider();
            var node = new BaseDataVariableState(null) { NodeId = new NodeId("health", 1) };
            Assert.That(registry.TryRegisterForNode(node, provider, out IDisposable? registration), Is.True);
            node.Historizing = true;
            using IHistorianNodeReplacement replacement =
                ((IHistorianNodeReplacementRegistry)registry).BeginNodeReplacement(node, successor);
            int cleanupCalls = 0;
            Task release = ((IHistorianNodeBinding)registration!).ReleaseAsync(() =>
            {
                cleanupCalls++;
                return default;
            }).AsTask();
            Assert.That(release.IsCompleted, Is.False);
            if (shutdown)
            {
                registry.Dispose();
                Assert.That(release.IsCompleted, Is.False);
            }
            if (commit && shutdown)
            {
                Assert.Throws<ObjectDisposedException>(() => replacement.Commit());
            }
            else if (commit)
            {
                replacement.Commit();
            }
            else
            {
                replacement.Dispose();
            }
            await release.ConfigureAwait(false);
            bool replaced = commit && !shutdown;
            Assert.That(cleanupCalls, Is.EqualTo(replaced ? 0 : 1));
            Assert.That(registry.Resolve(node.NodeId), replaced ? Is.SameAs(successor) : Is.Null);
            Assert.That(node.Historizing, Is.EqualTo(replaced));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ScopedRegistryDisposalCannotRemoveALaterRegistration(bool sameProvider)
        {
            using var registry = new HistorianProviderRegistry(new NamespaceTable());
            using var first = new InMemoryHistorianProvider();
            using var other = new InMemoryHistorianProvider();
            IHistorianProvider replacement = sameProvider ? first : other;
            var node = new NodeId("health", 1);
            Assert.That(registry.TryRegisterForNode(node, first, out IDisposable? binding), Is.True);
            registry.RegisterForNode(node, replacement);
            binding!.Dispose();
            binding.Dispose();
            Assert.That(registry.Resolve(node), Is.SameAs(replacement));
            Assert.That(registry.Providers.Contains(replacement), Is.True);
        }

        [Test]
        public async Task ConcurrentScopedRegistryRegistrationHasOnlyOneOwnerAsync()
        {
            using var registry = new HistorianProviderRegistry(new NamespaceTable());
            using var provider = new InMemoryHistorianProvider();
            var node = new NodeId("health", 1);
            var bindings = new IDisposable?[16];
            var tasks = new Task[bindings.Length];
            int winners = 0;
            for (int i = 0; i < tasks.Length; i++)
            {
                int index = i;
                tasks[i] = Task.Run(() =>
                {
                    if (registry.TryRegisterForNode(node, provider, out bindings[index]))
                    {
                        Interlocked.Increment(ref winners);
                    }
                });
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
            try
            {
                Assert.That(winners, Is.EqualTo(1));
                Assert.That(registry.Resolve(node), Is.SameAs(provider));
            }
            finally
            {
                foreach (IDisposable? binding in bindings)
                {
                    binding?.Dispose();
                }
            }
            Assert.That(registry.Resolve(node), Is.Null);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ScopedAttributeRestorationRespectsReplacementOrdering(bool events, bool replaceBeforeDisposal)
        {
            using var registry = new HistorianProviderRegistry(new NamespaceTable());
            using var provider = new InMemoryHistorianProvider();
            NodeState node = events
                ? new BaseObjectState(null)
                {
                    NodeId = new NodeId("events", 1),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                }
                : new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("health", 1),
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead
                };
            Assert.That(registry.TryRegisterForNode(node, provider, out IDisposable? binding), Is.True);
            if (node is BaseVariableState captured)
            {
                captured.Historizing = true;
                captured.AccessLevel |= AccessLevels.HistoryRead;
                captured.UserAccessLevel |= AccessLevels.HistoryRead;
            }
            else
            {
                ((BaseObjectState)node).EventNotifier |= EventNotifiers.HistoryRead;
            }
            if (!replaceBeforeDisposal)
            {
                binding!.Dispose();
                Assert.That(registry.Resolve(node.NodeId), Is.Null);
                if (node is BaseVariableState restored)
                {
                    Assert.That(restored.Historizing, Is.False);
                    Assert.That(restored.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
                    Assert.That(restored.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
                }
                else
                {
                    Assert.That(((BaseObjectState)node).EventNotifier, Is.EqualTo(EventNotifiers.SubscribeToEvents));
                }
            }
            registry.RegisterForNode(node.NodeId, provider);
            if (node is BaseVariableState replacement)
            {
                replacement.Historizing = true;
                replacement.AccessLevel = AccessLevels.HistoryRead | AccessLevels.HistoryWrite;
                replacement.UserAccessLevel = AccessLevels.HistoryWrite;
            }
            else
            {
                ((BaseObjectState)node).EventNotifier = EventNotifiers.HistoryWrite;
            }
            binding!.Dispose();
            Assert.That(registry.Resolve(node.NodeId), Is.SameAs(provider));
            if (node is BaseVariableState retained)
            {
                Assert.That(retained.Historizing, Is.True);
                Assert.That(retained.AccessLevel, Is.EqualTo(AccessLevels.HistoryRead | AccessLevels.HistoryWrite));
                Assert.That(retained.UserAccessLevel, Is.EqualTo(AccessLevels.HistoryWrite));
            }
            else
            {
                Assert.That(((BaseObjectState)node).EventNotifier, Is.EqualTo(EventNotifiers.HistoryWrite));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ScopedAttributeRestorationCannotOverwriteAConcurrentReplacementAsync(bool events)
        {
            using var registry = new HistorianProviderRegistry(new NamespaceTable());
            using var first = new InMemoryHistorianProvider();
            using var replacement = new InMemoryHistorianProvider();
            for (int i = 0; i < 32; i++)
            {
                NodeState node = events
                    ? new BaseObjectState(null) { NodeId = new NodeId(Guid.NewGuid(), 1) }
                    : new BaseDataVariableState(null) { NodeId = new NodeId(Guid.NewGuid(), 1) };
                Assert.That(registry.TryRegisterForNode(node, first, out IDisposable? binding), Is.True);
                await Task.WhenAll(
                    Task.Run(() => binding!.Dispose()),
                    Task.Run(() =>
                    {
                        registry.RegisterForNode(node.NodeId, replacement);
                        if (node is BaseVariableState variable)
                        {
                            variable.Historizing = true;
                            variable.AccessLevel = AccessLevels.HistoryRead | AccessLevels.HistoryWrite;
                            variable.UserAccessLevel = AccessLevels.HistoryRead;
                        }
                        else
                        {
                            ((BaseObjectState)node).EventNotifier =
                                EventNotifiers.HistoryRead | EventNotifiers.HistoryWrite;
                        }
                    })).ConfigureAwait(false);
                Assert.That(registry.Resolve(node.NodeId), Is.SameAs(replacement));
                if (node is BaseVariableState variable)
                {
                    Assert.That(variable.Historizing, Is.True);
                    Assert.That(variable.AccessLevel,
                        Is.EqualTo(AccessLevels.HistoryRead | AccessLevels.HistoryWrite));
                    Assert.That(variable.UserAccessLevel, Is.EqualTo(AccessLevels.HistoryRead));
                }
                else
                {
                    Assert.That(((BaseObjectState)node).EventNotifier, Is.EqualTo(
                        EventNotifiers.HistoryRead | EventNotifiers.HistoryWrite));
                }
                Assert.That(registry.UnregisterForNode(node.NodeId), Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReplacementReservationsKeepUncommittedProvidersInvisibleAndRejectOverlapAsync(bool commit)
        {
            using var registry = new HistorianProviderRegistry(new NamespaceTable());
            using var original = new InMemoryHistorianProvider();
            using var attempted = new InMemoryHistorianProvider();
            using var conflicting = new InMemoryHistorianProvider();
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("pending", 1),
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead
            };
            Assert.That(registry.TryRegisterForNode(node, original, out IDisposable? owner), Is.True);
            node.Historizing = true;
            node.AccessLevel |= AccessLevels.HistoryRead;
            node.UserAccessLevel |= AccessLevels.HistoryRead;
            var replacements = (IHistorianNodeReplacementRegistry)registry;
            using (IHistorianNodeReplacement pending = replacements.BeginNodeReplacement(node, attempted))
            {
                node.AccessLevel |= AccessLevels.HistoryWrite;
                await Task.Run(() =>
                {
                    Assert.That(registry.Resolve(node.NodeId), Is.SameAs(original));
                    Assert.That(registry.Providers.Contains(attempted), Is.False);
                    Assert.Throws<InvalidOperationException>(() =>
                        replacements.BeginNodeReplacement(node, conflicting));
                    Assert.Throws<InvalidOperationException>(() => registry.RegisterForNode(node.NodeId, conflicting));
                    Assert.Throws<InvalidOperationException>(() => registry.UnregisterForNode(node.NodeId));
                    Assert.That(registry.TryRegisterForNode(node, conflicting, out IDisposable? rejected), Is.False);
                    Assert.That(rejected, Is.Null);
                }).ConfigureAwait(false);
                owner!.Dispose();
                Assert.That(node.Historizing, Is.True);
                Assert.That(registry.Resolve(node.NodeId), Is.SameAs(original));
                if (commit)
                {
                    pending.Commit();
                    Assert.That(registry.Resolve(node.NodeId), Is.SameAs(attempted));
                }
            }
            if (commit)
            {
                Assert.That(registry.Resolve(node.NodeId), Is.SameAs(attempted));
                Assert.That(node.Historizing, Is.True);
                Assert.That(node.AccessLevel, Is.EqualTo(
                    AccessLevels.CurrentRead | AccessLevels.HistoryRead | AccessLevels.HistoryWrite));
                Assert.That(node.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentRead | AccessLevels.HistoryRead));
            }
            else
            {
                Assert.That(registry.Resolve(node.NodeId), Is.Null);
                Assert.That(node.Historizing, Is.False);
                Assert.That(node.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
                Assert.That(node.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            }
            Assert.That(registry.Providers.Contains(conflicting), Is.False);
            Assert.That(registry.Providers.Contains(attempted), Is.EqualTo(commit));
        }

        [Test]
        public void FailedReservationDoesNotBecomeTheOwnerRestoredByASubsequentFailure()
        {
            using var registry = new HistorianProviderRegistry(new NamespaceTable());
            using var original = new InMemoryHistorianProvider();
            using var failed = new InMemoryHistorianProvider();
            using var later = new InMemoryHistorianProvider();
            var node = new BaseDataVariableState(null) { NodeId = new NodeId("overlap", 1) };
            registry.RegisterForNode(node.NodeId, original);
            var replacements = (IHistorianNodeReplacementRegistry)registry;
            using (IHistorianNodeReplacement first = replacements.BeginNodeReplacement(node, failed))
            {
                Assert.Throws<InvalidOperationException>(() => replacements.BeginNodeReplacement(node, later));
            }
            using (IHistorianNodeReplacement second = replacements.BeginNodeReplacement(node, later))
            {
                Assert.That(registry.Resolve(node.NodeId), Is.SameAs(original));
            }
            Assert.That(registry.Resolve(node.NodeId), Is.SameAs(original));
            Assert.That(registry.Providers.Contains(failed), Is.False);
            Assert.That(registry.Providers.Contains(later), Is.False);
        }

        [Test]
        public async Task MemoryProvisioningPreservesExplicitCapabilitiesAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var node = new NodeId("health", 1);
            HistorianNodeCapabilities capabilities = HistorianNodeCapabilities.DataReadWrite with
            {
                ReadRawData = false,
                Stepped = false
            };
            provider.SetCapabilities(node, capabilities);
            Assert.That(provider.TryRegister(node), Is.False);
            Assert.That(
                await provider.GetCapabilitiesAsync(node, CancellationToken.None).ConfigureAwait(false),
                Is.SameAs(capabilities));
            var fresh = new NodeId("new-health", 1);
            Assert.That(provider.TryRegister(fresh, capabilities), Is.True);
            Assert.That(provider.TryRegister(fresh, HistorianNodeCapabilities.DataReadWrite), Is.False);
            Assert.That(
                await provider.GetCapabilitiesAsync(fresh, CancellationToken.None).ConfigureAwait(false),
                Is.SameAs(capabilities));
        }

        [Test]
        public void SampleClientCertificateRootIsExplicitAndMustBeAbsolute()
        {
            Assert.That(AmbClientOptions.Parse([]).PkiRoot, Is.Null);
            Assert.That(
                AmbClientOptions.Parse(["--pki-root", TestContext.CurrentContext.WorkDirectory]).PkiRoot,
                Is.EqualTo(TestContext.CurrentContext.WorkDirectory));
            Assert.Throws<ArgumentException>(() => AmbClientOptions.Parse(["--pki-root", "relative-pki"]));
        }

        [Test]
        public void HistoryDefaultsToDisabledAndFluentConfigurationKeepsTheProvider()
        {
            using var provider = new InMemoryHistorianProvider();
            var options = new AmbServerOptions();
            Assert.That(options.History, Is.Null);
            Assert.That(options.UseHistory(provider, AssetHistoryKinds.DeviceHealth), Is.SameAs(options));
            Assert.That(options.History!.Provider, Is.SameAs(provider));
            Assert.That(options.History.Kinds, Is.EqualTo(AssetHistoryKinds.DeviceHealth));
            Assert.That(options.History.Capture, Is.Null);
        }

        [Test]
        public void HistoryOptionsRejectNullProvidersAndUnknownSelectionBits()
        {
            using var provider = new InMemoryHistorianProvider();
            Assert.Throws<ArgumentNullException>(() => new AssetHistoryOptions(null!));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new AssetHistoryOptions(provider, (AssetHistoryKinds)8));
            Assert.That(new AssetHistoryOptions(provider).Kinds, Is.EqualTo(AssetHistoryKinds.All));
            Assert.That(
                new AssetHistoryOptions(provider, AssetHistoryKinds.None).Kinds,
                Is.EqualTo(AssetHistoryKinds.None));
        }

        [TestCaseSource(nameof(HealthQualities))]
        public void HealthSamplesRetainQualityAndTimestamps(uint status, bool typed)
        {
            var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var value = new DataValue(Variant.From((int)DeviceHealthEnumeration.FAILURE), status, timestamp);
            var sample = new AssetHealthHistoryValue(new NodeId(42), value);
            Assert.That(sample.NodeId, Is.EqualTo(new NodeId(42)));
            Assert.That(sample.Value, Is.EqualTo(value));
            Assert.That(sample.Value.SourceTimestamp, Is.EqualTo((DateTimeUtc)timestamp));
            Assert.That(sample.Value.StatusCode, Is.EqualTo((StatusCode)status));
            Assert.That(sample.DeviceHealth.HasValue, Is.EqualTo(typed));
            if (typed)
            {
                Assert.That(sample.DeviceHealth, Is.EqualTo(DeviceHealthEnumeration.FAILURE));
            }
        }

        [Test]
        public void IncorrectlyTypedHealthDoesNotBecomeAValidEnumeration()
        {
            var sample = new AssetHealthHistoryValue(new NodeId(42), new DataValue(Variant.From("FAILURE")));
            Assert.That(sample.DeviceHealth, Is.Null);
            Assert.That(sample.Value.WrappedValue.TryGetValue(out string? original), Is.True);
            Assert.That(original, Is.EqualTo("FAILURE"));
        }
    }
}
