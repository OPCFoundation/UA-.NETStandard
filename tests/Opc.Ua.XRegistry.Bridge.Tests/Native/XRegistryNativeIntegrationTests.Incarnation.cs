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
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    public sealed partial class XRegistryNativeIntegrationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task GuardedNativeRequestsRejectUnadvertisedSupportBeforeDispatchAsync(bool prepare)
        {
            m_forwarder.AdvertiseIncarnations = false;
            XRegistryRequest request = Request(
                XRegistryAction.Merge, "/schemagroups/g/schemas/r/versions/v1", "{}") with
            {
                ExpectedVersionIncarnation = "required-instance"
            };
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
            XRegistryResponse root = await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(m_forwarder.Mutations, Is.Empty);
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task WritableFileOpenRequiresAdvertisedAndReturnedIncarnationAsync(bool advertised)
        {
            await SeedAsync("/schemagroups/g/schemas/r",
                /*lang=json,strict*/ """{"versionid":"v1","schema":"retained"}""")
                .ConfigureAwait(false);
            m_forwarder.AdvertiseIncarnations = advertised;
            m_forwarder.OmitIncarnations = advertised;
            NodeId group = await FindChildEntityAsync(m_manager.RegistryNodeId, "/schemagroups/g")
                .ConfigureAwait(false);
            NodeId logical = await FindChildEntityAsync(group, "/schemagroups/g/schemas/r").ConfigureAwait(false);
            ResourceTypeClient file = m_generic.GetResource(logical);
            ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await file.OpenAsync(6).ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode,
                    Is.EqualTo(advertised ? StatusCodes.BadNotSupported : StatusCodes.BadNotWritable));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
            uint handle = await file.OpenAsync(1).ConfigureAwait(false);
            ByteString retained = await file.ReadAsync(handle, 256).ConfigureAwait(false);
            await file.CloseAsync(handle).ConfigureAwait(false);
            Assert.That(Utf8(retained), Is.EqualTo("retained"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FileCloseRejectsRecreatedVersionWithIdenticalEpochAndTimestampsAsync(bool duringClose)
        {
            const string resourcePath = "/schemagroups/g/schemas/r";
            const string versionPath = resourcePath + "/versions/v1";
            await ResetEndpointAsync(timeProvider: m_clock).ConfigureAwait(false);
            await SeedAsync(resourcePath, /*lang=json,strict*/ """{"versionid":"v1","schema":"original"}""")
                .ConfigureAwait(false);
            XRegistryResponse original = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Read, versionPath)).ConfigureAwait(false);
            NodeId group = await FindChildEntityAsync(m_manager.RegistryNodeId, "/schemagroups/g")
                .ConfigureAwait(false);
            NodeId logical = await FindChildEntityAsync(group, resourcePath).ConfigureAwait(false);
            ResourceTypeClient file = m_generic.GetResource(logical);
            uint handle = await file.OpenAsync(6).ConfigureAwait(false);
            await file.WriteAsync(handle, ByteString.From(Encoding.UTF8.GetBytes("stale")))
                .ConfigureAwait(false);
            Task? closing = null;
            if (duringClose)
            {
                m_forwarder.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                m_forwarder.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
                closing = file.CloseAsync(handle).AsTask();
                await m_forwarder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            try
            {
                XRegistryResponse deleted = await m_forwarder.Inner.ExecuteAsync(
                    Request(XRegistryAction.Delete, resourcePath)).ConfigureAwait(false);
                XRegistryResponse recreated = await m_forwarder.Inner.ExecuteAsync(
                    Request(XRegistryAction.Replace, versionPath, "{}") with
                    {
                        Document = ByteString.From(Encoding.UTF8.GetBytes("replacement")),
                        ContentType = "text/plain"
                    }).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(deleted.StatusCode, Is.EqualTo(204));
                    Assert.That(recreated.StatusCode, Is.EqualTo(201));
                    Assert.That(recreated.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                    Assert.That(recreated.Metadata.GetProperty("createdat").GetString(),
                        Is.EqualTo(original.Metadata.GetProperty("createdat").GetString()));
                    Assert.That(recreated.Metadata.GetProperty("modifiedat").GetString(),
                        Is.EqualTo(original.Metadata.GetProperty("modifiedat").GetString()));
                });
            }
            finally
            {
                m_forwarder.Release?.TrySetResult(true);
            }
            closing ??= file.CloseAsync(handle).AsTask();
            ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await closing.ConfigureAwait(false));
            XRegistryResponse current = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Read, versionPath) with { View = XRegistryView.Default })
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(Utf8(current.Document), Is.EqualTo("replacement"));
                Assert.That(current.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(m_forwarder.Mutations, Has.Count.EqualTo(1), "A rejected close must not be retried.");
            });
        }
    }
}
