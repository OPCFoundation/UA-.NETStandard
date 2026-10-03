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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    public sealed partial class XRegistryNativeIntegrationTests
    {
        [Test]
        public async Task GeneratedNativeTransportPreservesAndEnforcesGenerationGuardsAsync()
        {
            XRegistryEndpointDescription description = await m_native.InspectAsync(s_writer).ConfigureAwait(false);
            XRegistryResponse read = await m_native.ExecuteAsync(Request(XRegistryAction.Read, "/") with
            {
                ExpectedGeneration = description.Generation
            }).ConfigureAwait(false);
            XRegistryResponse changed = await m_native.ExecuteAsync(Request(
                XRegistryAction.Merge, "/", /*lang=json,strict*/ """{"name":"guarded-native"}""") with
            {
                ExpectedGeneration = description.Generation
            }).ConfigureAwait(false);
            XRegistryResponse stale = await m_native.ExecuteAsync(Request(XRegistryAction.Read, "/") with
            {
                ExpectedGeneration = description.Generation
            }).ConfigureAwait(false);
            XRegistryResponse current = await m_native.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(description.SupportsGenerationGuards, Is.True);
                Assert.That(description.Generation, Is.Not.Null.And.Not.Empty);
                Assert.That(read.Generation, Is.EqualTo(description.Generation));
                Assert.That(changed.StatusCode, Is.EqualTo(200));
                Assert.That(stale.StatusCode, Is.EqualTo(409));
                Assert.That(stale.Error?.Code, Is.EqualTo("concurrent_change"));
                Assert.That(current.Generation, Is.Not.EqualTo(description.Generation));
                Assert.That(current.Metadata.GetProperty("name").GetString(), Is.EqualTo("guarded-native"));
                Assert.That(m_forwarder.Mutations, Has.Count.EqualTo(1));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeGenerationGuardsRequireAdvertisedSupportBeforeStaging(bool prepare)
        {
            m_forwarder.AdvertiseGenerations = false;
            XRegistryRequest request =
                Request(XRegistryAction.Merge, "/", "{}") with { ExpectedGeneration = "required" };
            ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (prepare)
                {
                    IXRegistryPreparedOperation operation = await m_native.PrepareAsync(request).ConfigureAwait(false);
                    await operation.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await m_native.ExecuteAsync(request).ConfigureAwait(false);
                }
            });
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }

        [TestCase("/")]
        [TestCase("/schemagroups")]
        [TestCase("/schemagroups/g/schemas")]
        [TestCase("/schemagroups/g/schemas/r/meta")]
        [TestCase("/schemagroups/g/schemas/r/versions")]
        public async Task ProjectionRejectsGenerationChangesAtEveryInventoryBoundaryAsync(string boundary)
        {
            const string version = "/schemagroups/g/schemas/r/versions/v1";
            await SeedAsync("/schemagroups/g/schemas/r",
                /*lang=json,strict*/ """{"versionid":"v1","name":"original","schema":"retained"}""")
                .ConfigureAwait(false);
            XRegistryResponse before = await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            bool changed = false;
            m_forwarder.AfterReadAsync = async (request, ct) =>
            {
                if (request.Path == boundary && !changed)
                {
                    changed = true;
                    XRegistryResponse edited = await m_forwarder.Inner.ExecuteAsync(
                        Request(XRegistryAction.Merge, version, /*lang=json,strict*/ """{"name":"concurrent"}"""), ct)
                        .ConfigureAwait(false);
                    Assert.That(edited.StatusCode, Is.EqualTo(200));
                }
            };
            ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_manager.RefreshAsync().ConfigureAwait(false));
            m_forwarder.AfterReadAsync = null;
            XRegistryResponse after = await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(changed, Is.True);
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(after.Metadata.GetProperty("epoch").GetInt32(),
                    Is.EqualTo(before.Metadata.GetProperty("epoch").GetInt32()),
                    "The root epoch is not a recursive registry-generation watermark.");
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
            await m_manager.RefreshAsync().ConfigureAwait(false);
        }
    }
}
