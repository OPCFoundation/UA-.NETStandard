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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    /// <summary>
    /// Round-trip coverage for
    /// <see cref="XmlPubSubConfigurationStore"/>: loading the legacy
    /// publisher and subscriber XML fixtures, save / load preservation,
    /// <see cref="IPubSubConfigurationStore.Changed"/> event semantics,
    /// and missing-file behaviour.
    /// </summary>
    [TestFixture]
    [TestSpec("9.1.6", Summary = "PubSub configuration object model — XML persistence")]
    public class XmlPubSubConfigurationStoreTests
    {
        private string m_baseDir = null!;
        private string m_workDir = null!;
        private ITelemetryContext m_telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            m_baseDir = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "Configuration");
            m_workDir = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "Phase4Xml",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_workDir);
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(m_workDir))
                {
                    Directory.Delete(m_workDir, recursive: true);
                }
            }
            catch
            {
            }
        }

        [Test]
        public void Constructor_NullFilePath_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new XmlPubSubConfigurationStore(null!, m_telemetry));
        }

        [Test]
        public void Constructor_EmptyFilePath_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => new XmlPubSubConfigurationStore(string.Empty, m_telemetry));
        }

        [Test]
        public void Constructor_NullTelemetry_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new XmlPubSubConfigurationStore("x.xml", null!));
        }

        [Test]
        public void LoadAsync_MissingFile_ThrowsFileNotFound()
        {
            string path = Path.Combine(m_workDir, "missing.xml");
            var store = new XmlPubSubConfigurationStore(path, m_telemetry);
            Assert.ThrowsAsync<FileNotFoundException>(
                async () => await store.LoadAsync().ConfigureAwait(false));
        }

        [Test]
        [TestSpec("9.1.6", Summary = "Legacy publisher XML round-trip preserves structure")]
        public async Task LoadAsync_PublisherFixture_ReturnsConfiguration()
        {
            string path = Path.Combine(m_baseDir, "PublisherConfiguration.xml");
            Assume.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
            var store = new XmlPubSubConfigurationStore(path, m_telemetry);
            PubSubConfigurationDataType config = await store.LoadAsync()
                .ConfigureAwait(false);
            Assert.That(config, Is.Not.Null);
            Assert.That(config.Connections.IsNull, Is.False);
            Assert.That(config.Connections.Count, Is.GreaterThan(0));
        }

        [Test]
        [TestSpec("9.1.6", Summary = "Legacy subscriber XML round-trip preserves structure")]
        public async Task LoadAsync_SubscriberFixture_ReturnsConfiguration()
        {
            string path = Path.Combine(m_baseDir, "SubscriberConfiguration.xml");
            Assume.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
            var store = new XmlPubSubConfigurationStore(path, m_telemetry);
            PubSubConfigurationDataType config = await store.LoadAsync()
                .ConfigureAwait(false);
            Assert.That(config, Is.Not.Null);
            Assert.That(config.Connections.IsNull, Is.False);
            Assert.That(config.Connections.Count, Is.GreaterThan(0));
        }

        [Test]
        [TestSpec("9.1.6", Summary = "Save → Reload preserves configuration structure")]
        public async Task SaveAsync_ThenLoadAsync_PreservesStructure()
        {
            string source = Path.Combine(m_baseDir, "PublisherConfiguration.xml");
            Assume.That(File.Exists(source), Is.True, $"Test fixture missing: {source}");
            var sourceStore = new XmlPubSubConfigurationStore(source, m_telemetry);
            PubSubConfigurationDataType loaded = await sourceStore.LoadAsync()
                .ConfigureAwait(false);

            string outPath = Path.Combine(m_workDir, "rt.xml");
            var outStore = new XmlPubSubConfigurationStore(outPath, m_telemetry);
            await outStore.SaveAsync(loaded).ConfigureAwait(false);
            PubSubConfigurationDataType reloaded = await outStore.LoadAsync()
                .ConfigureAwait(false);
            AssertStructurallyEquivalent(loaded, reloaded);
        }

        [Test]
        public async Task SaveAsync_NewFile_ChangedEventWithNullPrevious()
        {
            string outPath = Path.Combine(m_workDir, "new.xml");
            var store = new XmlPubSubConfigurationStore(outPath, m_telemetry);

            PubSubConfigurationChangedEventArgs? observed = null;
            store.Changed += (_, args) => observed = args;

            var config = NewMinimalConfig();
            await store.SaveAsync(config).ConfigureAwait(false);

            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.Previous, Is.Null);
            Assert.That(observed.Current, Is.SameAs(config));
            Assert.That(File.Exists(outPath), Is.True);
        }

        [Test]
        public async Task SaveAsync_ExistingFile_ChangedEventWithPreviousLoaded()
        {
            string outPath = Path.Combine(m_workDir, "existing.xml");
            var store = new XmlPubSubConfigurationStore(outPath, m_telemetry);

            await store.SaveAsync(NewMinimalConfig("First")).ConfigureAwait(false);

            PubSubConfigurationChangedEventArgs? observed = null;
            store.Changed += (_, args) => observed = args;

            await store.SaveAsync(NewMinimalConfig("Second")).ConfigureAwait(false);

            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.Previous, Is.Not.Null);
            Assert.That(observed.Previous!.Connections.Count, Is.EqualTo(1));
            Assert.That(observed.Previous.Connections[0].Name, Is.EqualTo("First"));
            Assert.That(observed.Current.Connections[0].Name, Is.EqualTo("Second"));
        }

        [Test]
        public void SaveAsync_NullConfiguration_Throws()
        {
            var store = new XmlPubSubConfigurationStore(
                Path.Combine(m_workDir, "ignored.xml"),
                m_telemetry);
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await store.SaveAsync(null!).ConfigureAwait(false));
        }

        [Test]
        public async Task LoadAsync_RespectsCancellation()
        {
            string outPath = Path.Combine(m_workDir, "cancel.xml");
            var store = new XmlPubSubConfigurationStore(outPath, m_telemetry);
            await store.SaveAsync(NewMinimalConfig()).ConfigureAwait(false);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.That(
                async () => await store.LoadAsync(cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void FilePath_ExposedThroughProperty()
        {
            string outPath = Path.Combine(m_workDir, "expose.xml");
            var store = new XmlPubSubConfigurationStore(outPath, m_telemetry);
            Assert.That(store.FilePath, Is.EqualTo(outPath));
            Assert.That(store.TimeProvider, Is.SameAs(TimeProvider.System));
        }

        [Test]
        public async Task WatchForChanges_ExternalEdit_RaisesChanged()
        {
            string path = Path.Combine(m_workDir, "watch.xml");
            using var watching = new XmlPubSubConfigurationStore(
                path, m_telemetry, watchForChanges: true);
            await watching.SaveAsync(NewMinimalConfig("Initial")).ConfigureAwait(false);

            var changed = new TaskCompletionSource<PubSubConfigurationChangedEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            watching.Changed += (_, args) => changed.TrySetResult(args);

            // Simulate an external process rewriting the configuration file.
            using var external = new XmlPubSubConfigurationStore(path, m_telemetry);
            await external.SaveAsync(NewMinimalConfig("ExternallyEdited")).ConfigureAwait(false);

            PubSubConfigurationChangedEventArgs observed =
                await changed.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(observed.Current, Is.Not.Null);
            Assert.That(observed.Current!.Connections[0].Name, Is.EqualTo("ExternallyEdited"));
        }

        [Test]
        public async Task WatchForChanges_Disabled_DoesNotRaiseOnExternalEdit()
        {
            string path = Path.Combine(m_workDir, "nowatch.xml");
            using var store = new XmlPubSubConfigurationStore(path, m_telemetry);
            await store.SaveAsync(NewMinimalConfig("Initial")).ConfigureAwait(false);

            int changedCount = 0;
            store.Changed += (_, _) => Interlocked.Increment(ref changedCount);

            using var external = new XmlPubSubConfigurationStore(path, m_telemetry);
            await external.SaveAsync(NewMinimalConfig("ExternallyEdited")).ConfigureAwait(false);

            await Task.Delay(1500).ConfigureAwait(false);

            Assert.That(Volatile.Read(ref changedCount), Is.Zero);
        }

        [Test]
        public async Task WatchForChanges_SelfSave_DoesNotDoubleFire()
        {
            string path = Path.Combine(m_workDir, "selfsave.xml");
            using var watching = new XmlPubSubConfigurationStore(
                path, m_telemetry, watchForChanges: true);
            await watching.SaveAsync(NewMinimalConfig("Initial")).ConfigureAwait(false);

            int changedCount = 0;
            watching.Changed += (_, _) => Interlocked.Increment(ref changedCount);

            await watching.SaveAsync(NewMinimalConfig("Second")).ConfigureAwait(false);

            // Allow the file-watch debounce window to elapse; the self-write must
            // not produce a second Changed beyond the one SaveAsync already raised.
            await Task.Delay(1500).ConfigureAwait(false);

            Assert.That(Volatile.Read(ref changedCount), Is.EqualTo(1));
        }

        private static PubSubConfigurationDataType NewMinimalConfig(string connectionName = "Conn1")
        {
            return new PubSubConfigurationDataType
            {
                Enabled = true,
                PublishedDataSets = new ArrayOf<PublishedDataSetDataType>(
                    new[] { new PublishedDataSetDataType { Name = "DS1" } }),
                Connections = new ArrayOf<PubSubConnectionDataType>(
                    new[]
                    {
                        new PubSubConnectionDataType
                        {
                            Name = connectionName,
                            Enabled = true,
                            PublisherId = new Variant((ushort)42),
                            TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                            Address = new ExtensionObject(
                                new NetworkAddressUrlDataType
                                {
                                    Url = "opc.udp://224.0.0.22:4840",
                                    NetworkInterface = string.Empty
                                })
                        }
                    })
            };
        }

        private static void AssertStructurallyEquivalent(
            PubSubConfigurationDataType expected,
            PubSubConfigurationDataType actual)
        {
            Assert.That(actual.Enabled, Is.EqualTo(expected.Enabled));
            int expectedConnections = expected.Connections.IsNull ? 0 : expected.Connections.Count;
            int actualConnections = actual.Connections.IsNull ? 0 : actual.Connections.Count;
            Assert.That(actualConnections, Is.EqualTo(expectedConnections));
            int expectedPds = expected.PublishedDataSets.IsNull
                ? 0
                : expected.PublishedDataSets.Count;
            int actualPds = actual.PublishedDataSets.IsNull
                ? 0
                : actual.PublishedDataSets.Count;
            Assert.That(actualPds, Is.EqualTo(expectedPds));
            if (expectedConnections == 0)
            {
                return;
            }
            for (int i = 0; i < expectedConnections; i++)
            {
                PubSubConnectionDataType e = expected.Connections[i];
                PubSubConnectionDataType a = actual.Connections[i];
                Assert.That(a.Name, Is.EqualTo(e.Name));
                Assert.That(a.TransportProfileUri, Is.EqualTo(e.TransportProfileUri));
                int eWgCount = e.WriterGroups.IsNull ? 0 : e.WriterGroups.Count;
                int aWgCount = a.WriterGroups.IsNull ? 0 : a.WriterGroups.Count;
                Assert.That(aWgCount, Is.EqualTo(eWgCount));
                int eRgCount = e.ReaderGroups.IsNull ? 0 : e.ReaderGroups.Count;
                int aRgCount = a.ReaderGroups.IsNull ? 0 : a.ReaderGroups.Count;
                Assert.That(aRgCount, Is.EqualTo(eRgCount));
            }
        }
    }
}
