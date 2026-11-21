/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OnboardingDevice;
using OnboardingRegistrar;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies reset marker generations and compatibility with earlier persisted markers.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class DevicePullResetGenerationTests
    {
        /// <summary>
        /// Selects an isolated checkpoint directory for each test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_directory = Path.Combine(
                Path.GetTempPath(), nameof(DevicePullResetGenerationTests), Guid.NewGuid().ToString("N"));
        }

        /// <summary>
        /// Removes this test's checkpoint directory after its writer lease is released.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_directory))
            {
                Directory.Delete(m_directory, recursive: true);
            }
        }

        /// <summary>
        /// A marker captured before a later reset cannot publish after that reset completes.
        /// </summary>
        [Test]
        public async Task RepeatedResetRejectsAnEarlierMarkerAfterWriterRestart()
        {
            byte[]? first;
            using (var files = new PushRegistrarCheckpointFiles(m_directory))
            {
                var journal = CreateJournal(files);
                await journal.BeginResetAsync().ConfigureAwait(false);
                await journal.CompleteResetAsync().ConfigureAwait(false);
                first = await files.ReadAsync(kJournalKey, CancellationToken.None).ConfigureAwait(false);
            }
            Assert.That(first, Is.Not.Null);

            using var restarted = new PushRegistrarCheckpointFiles(m_directory);
            var resumed = CreateJournal(restarted);
            await resumed.BeginResetAsync().ConfigureAwait(false);
            await resumed.CompleteResetAsync().ConfigureAwait(false);
            byte[]? second = await restarted.ReadAsync(kJournalKey, CancellationToken.None).ConfigureAwait(false);

            Assert.That(second, Is.Not.Null);
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(
                await restarted.CompareExchangeAsync(kJournalKey, first, new byte[] { 1 }, CancellationToken.None)
                    .ConfigureAwait(false),
                Is.False);
            Assert.That(await restarted.ReadAsync(kJournalKey, CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(second));
            Assert.That(await resumed.ReadAsync().ConfigureAwait(false), Is.Null);
        }

        /// <summary>
        /// A legacy pending reset completes with a new marker and rejects its old encoded value.
        /// </summary>
        [Test]
        public async Task LegacyResetMarkerCompletesWithANewGeneration()
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            var telemetry = NUnitTelemetryContext.Create();
            using var buffer = new MemoryStream();
            using (var encoder = new BinaryEncoder(buffer, ServiceMessageContext.Create(telemetry), leaveOpen: true))
            {
                encoder.WriteUInt32(null, 2);
                encoder.WriteString(null, kApplicationUri);
                encoder.WriteInt32(null, (int)DevicePullJournalStage.Resetting);
                encoder.WriteStringArray(null, []);
            }
            byte[] legacy = buffer.ToArray();
            await files.WriteAsync(kJournalKey, legacy, CancellationToken.None).ConfigureAwait(false);
            var journal = new DevicePullJournal(files, kApplicationUri, null, telemetry);

            await journal.CompleteResetAsync().ConfigureAwait(false);

            byte[]? current = await files.ReadAsync(kJournalKey, CancellationToken.None).ConfigureAwait(false);
            Assert.That(current, Is.Not.Null);
            Assert.That(current, Is.Not.EqualTo(legacy));
            Assert.That(
                await files.CompareExchangeAsync(kJournalKey, legacy, new byte[] { 1 }, CancellationToken.None)
                    .ConfigureAwait(false),
                Is.False);
            Assert.That(await journal.ReadAsync().ConfigureAwait(false), Is.Null);
        }

        /// <summary>
        /// Creates a journal bound to the test's checkpoint writer.
        /// </summary>
        private static DevicePullJournal CreateJournal(PushRegistrarCheckpointFiles files)
        {
            return new DevicePullJournal(files, kApplicationUri, null, NUnitTelemetryContext.Create());
        }

        /// <summary>The test device identity.</summary>
        private const string kApplicationUri = "urn:opcfoundation:tests:pull-reset";

        /// <summary>The journal's complete-value persistence key.</summary>
        private const string kJournalKey = "DevicePullSigning-" + kApplicationUri;

        /// <summary>The current test's isolated checkpoint directory.</summary>
        private string m_directory = string.Empty;
    }
}
