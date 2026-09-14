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

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    public sealed partial class XRegistryNativeIntegrationTests
    {
        [Test]
        public async Task ExtendedModelSourceCapabilitiesAndDefaultSelectionKeepTheirDistinctRolesAsync()
        {
            XRegistryResponse replaced = await m_native.ExecuteAsync(Request(
                XRegistryAction.Replace, "/modelsource", /*lang=json,strict*/ """
                {"attributes":{"plant":{"type":"string"}},"groups":{"schemagroups":{
                  "singular":"schemagroup","resources":{"schemas":{"singular":"schema"}}}}}
                """)).ConfigureAwait(false);
            Assert.That(replaced.StatusCode, Is.EqualTo(200), replaced.Error?.Detail);
            XRegistryResponse source = await m_native.ExecuteAsync(Request(XRegistryAction.Read, "/modelsource"))
                .ConfigureAwait(false);
            XRegistryResponse effective = await m_native.ExecuteAsync(Request(XRegistryAction.Read, "/model"))
                .ConfigureAwait(false);
            XRegistryResponse offered = await m_native.ExecuteAsync(
                Request(XRegistryAction.Read, "/capabilitiesoffered")).ConfigureAwait(false);
            XRegistryResponse active = await m_native.ExecuteAsync(Request(XRegistryAction.Read, "/capabilities"))
                .ConfigureAwait(false);
            XRegistryResponse rejected = await m_native.ExecuteAsync(
                Request(XRegistryAction.Replace, "/capabilities", "{}")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(source.Metadata.GetProperty("attributes").GetProperty("plant").GetProperty("type")
                    .GetString(), Is.EqualTo("string"));
                Assert.That(source.Metadata.GetProperty("attributes").TryGetProperty("registryid", out _), Is.False);
                Assert.That(effective.Metadata.GetProperty("attributes").TryGetProperty("registryid", out _), Is.True);
                Assert.That(offered.StatusCode, Is.EqualTo(200));
                Assert.That(active.StatusCode, Is.EqualTo(200));
                Assert.That(offered.Metadata.GetProperty("flags").EnumerateArray().Select(value => value.GetString()),
                    Does.Contain("filter"));
                Assert.That(rejected.StatusCode, Is.EqualTo(405));
            });
            XRegistryResponse created = await m_native.ExecuteAsync(Request(XRegistryAction.Replace,
                "/schemagroups/g/schemas/r", /*lang=json,strict*/ """
                {"meta":{"defaultversionid":"v1","defaultversionsticky":true},
                 "versions":{"v1":{"schemabase64":"AQI="},"v2":{"ancestorid":"v1","schemabase64":"AwQ="}}}
                """)).ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            XRegistryResponse selected = await m_native.ExecuteAsync(Request(XRegistryAction.Merge,
                "/schemagroups/g/schemas/r/meta", /*lang=json,strict*/ """{"defaultversionid":"v2"}"""))
                .ConfigureAwait(false);
            Assert.That(selected.StatusCode, Is.EqualTo(200), selected.Error?.Detail);
            XRegistryResponse logical = await m_native.ExecuteAsync(Request(XRegistryAction.Read,
                "/schemagroups/g/schemas/r") with { View = XRegistryView.Default }).ConfigureAwait(false);
            XRegistryResponse exact = await m_native.ExecuteAsync(Request(XRegistryAction.Read,
                "/schemagroups/g/schemas/r/versions/v1") with { View = XRegistryView.Default }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(logical.Document, Is.EqualTo(ByteString.From(new byte[] { 3, 4 })));
                Assert.That(logical.Metadata.GetProperty("ancestorid").GetString(), Is.EqualTo("v1"));
                Assert.That(exact.Document, Is.EqualTo(ByteString.From(new byte[] { 1, 2 })));
                Assert.That(exact.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("v1"));
            });
        }
    }
}
