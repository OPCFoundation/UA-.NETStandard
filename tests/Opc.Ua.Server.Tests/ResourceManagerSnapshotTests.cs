/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests that the lock-free translation snapshot of <see cref="ResourceManager"/> is rebuilt
    /// after every change, so translations always reflect the tables as written.
    /// </summary>
    [TestFixture]
    [Category("ResourceManager")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ResourceManagerSnapshotTests
    {
        /// <summary>
        /// A translation read after an update, and after a new locale is added, sees the change.
        /// </summary>
        [Test]
        public void UpdatesAndNewLocalesAreVisibleAfterASnapshotWasBuilt()
        {
            using ResourceManager resourceManager = CreateResourceManager();
            resourceManager.Add("greeting", "de-DE", "Hallo");

            // builds the snapshot
            Assert.That(Translate(resourceManager, "de-DE"), Is.EqualTo("Hallo"));

            resourceManager.Add("greeting", "de-DE", "Servus");
            Assert.That(Translate(resourceManager, "de-DE"), Is.EqualTo("Servus"));

            resourceManager.Add("greeting", "fr-FR", "Bonjour");
            Assert.That(Translate(resourceManager, "fr-FR"), Is.EqualTo("Bonjour"));
            Assert.That(Translate(resourceManager, "de-DE"), Is.EqualTo("Servus"));
            Assert.That(resourceManager.GetAvailableLocales(), Does.Contain("fr-FR"));
        }

        /// <summary>
        /// Readers running while translations are added never fail, never see a partially
        /// written table, and see every translation once the writer is done.
        /// </summary>
        [Test]
        public async Task ConcurrentReadersSeeACompleteSnapshotOnceWritesCompleteAsync()
        {
            using ResourceManager resourceManager = CreateResourceManager();
            const int keyCount = 500;
            using var cts = new CancellationTokenSource();
            int wrongTexts = 0;

            Task[] readers = new Task[4];
            for (int r = 0; r < readers.Length; r++)
            {
                readers[r] = Task.Run(() =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        for (int ii = 0; ii < keyCount; ii += 37)
                        {
                            string key = "key" + ii.ToString(CultureInfo.InvariantCulture);
                            LocalizedText text = resourceManager.Translate(["de-DE"], key, "default");
                            // either not added yet (default) or the complete translation
                            if (text.Text != "default" && text.Text != "Wert" + ii.ToString(CultureInfo.InvariantCulture))
                            {
                                Interlocked.Increment(ref wrongTexts);
                            }
                        }
                    }
                });
            }

            for (int ii = 0; ii < keyCount; ii++)
            {
                resourceManager.Add(
                    "key" + ii.ToString(CultureInfo.InvariantCulture),
                    "de-DE",
                    "Wert" + ii.ToString(CultureInfo.InvariantCulture));
            }
            await cts.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            Assert.That(wrongTexts, Is.Zero);
            for (int ii = 0; ii < keyCount; ii++)
            {
                string key = "key" + ii.ToString(CultureInfo.InvariantCulture);
                Assert.That(
                    resourceManager.Translate(["de-DE"], key, "default").Text,
                    Is.EqualTo("Wert" + ii.ToString(CultureInfo.InvariantCulture)));
            }
        }

        private static ResourceManager CreateResourceManager()
        {
            return new ResourceManager(new ApplicationConfiguration(NUnitTelemetryContext.Create()));
        }

        private static string? Translate(ResourceManager resourceManager, string locale)
        {
            return resourceManager.Translate([locale], "greeting", "Hello").Text;
        }
    }
}
