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
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Registry
{
    /// <summary>
    /// Exercises the durable file-backed registry store: bounded atomic replace,
    /// round-trip restore of resources / versions / bytes / state, and the
    /// persistence of invalid documents with their failure state.
    /// </summary>
    [TestFixture]
    public sealed class FileWotRegistryStoreTests
    {
        private string m_root = null!;

        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "wot-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_root);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(m_root))
                {
                    Directory.Delete(m_root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }

        [Test]
        public async Task Persist_And_Reload_RoundTripsResource()
        {
            var store = new FileWotRegistryStore(m_root);
            using (var service = new WotRegistryService(store))
            {
                await service.InitializeAsync();
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "a",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = TestMaterialization.Td("urn:a")
                });
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingModels,
                    ResourceId = "m",
                    Kind = WoTDocumentKindEnum.ThingModel,
                    Content = TestMaterialization.Tm("urn:m")
                });
            }

            var reloadStore = new FileWotRegistryStore(m_root);
            using var reloaded = new WotRegistryService(reloadStore);
            await reloaded.InitializeAsync();

            WotResource? td = reloaded.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "a");
            Assert.That(td, Is.Not.Null);
            Assert.That(td!.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingDescription));
            Assert.That(td.Versions.Length, Is.EqualTo(1));
            Assert.That(
                Encoding.UTF8.GetString(td.Versions[0].Content.ToArray()),
                Does.Contain("urn:a"));
            Assert.That(
                reloaded.Current.FindResource(WotRegistryGroups.ThingModels, "m"), Is.Not.Null);
        }

        [Test]
        public async Task InvalidDocument_SurvivesReload_WithFailureState()
        {
            var store = new FileWotRegistryStore(m_root);
            using (var service = new WotRegistryService(store))
            {
                await service.InitializeAsync();
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "bad",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = TestMaterialization.InvalidJson()
                });
            }

            var reloadStore = new FileWotRegistryStore(m_root);
            using var reloaded = new WotRegistryService(reloadStore);
            await reloaded.InitializeAsync();

            WotResource bad = reloaded.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "bad")!;
            Assert.That(bad.LoadState, Is.EqualTo(WoTLoadStateEnum.Failed));
            Assert.That(bad.Validation, Is.Not.Null);
            Assert.That(bad.Validation!.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Failed));
        }

        [Test]
        public async Task Upsert_OverwritesResourceAtomically()
        {
            var store = new FileWotRegistryStore(m_root);
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();
            await service.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "a",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = TestMaterialization.Td("urn:a", "v1")
            });
            await service.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "a",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = TestMaterialization.Td("urn:a", "v2")
            });

            var reloadStore = new FileWotRegistryStore(m_root);
            using var reloaded = new WotRegistryService(reloadStore);
            await reloaded.InitializeAsync();
            Assert.That(
                reloaded.Current.FindResource(WotRegistryGroups.ThingDescriptions, "a")!
                    .Versions.Length,
                Is.EqualTo(2));
        }
    }
}
