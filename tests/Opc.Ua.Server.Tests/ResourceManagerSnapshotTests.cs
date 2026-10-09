/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
        public void ConcurrentReadersSeeACompleteSnapshotOnceWritesComplete()
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
            cts.Cancel();
            Assert.That(Task.WaitAll(readers, 30000), Is.True);

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
