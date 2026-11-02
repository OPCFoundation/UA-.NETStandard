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
