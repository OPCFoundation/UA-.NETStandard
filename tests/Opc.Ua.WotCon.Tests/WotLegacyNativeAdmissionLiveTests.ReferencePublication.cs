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
 *
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
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Tests
{
    public sealed partial class WotLegacyNativeAdmissionLiveTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExistingOwnerReferencesAreIdempotentDuringNativeReplacement(bool wotComponent)
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            await asset.UploadThingDescriptionAsync(Document().Span.ToArray());
            ByteString content = NativeDocument(WotNodeSetPreservationMode.Always, "mapped", modify: model =>
            {
                model.Root!.Element(s_nodes + "NamespaceUris")!.Add(new XElement(s_nodes + "Uri", Namespaces.WotCon));
                XElement root = model.Root.Elements(s_nodes + "UAObject").Single();
                root.Element(s_nodes + "References")!.Add(Reference(
                    wotComponent ? "ns=3;i=" + ReferenceTypes.HasWoTComponent : Ua.ReferenceTypeIds.HasInterface.ToString(),
                    wotComponent ? "ns=1;s=Assets/mapped/props/Speed" : "ns=3;i=" + ObjectTypes.IWoTAssetType));
            });
            await asset.UploadThingDescriptionAsync(content.Span.ToArray());
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));
            Assert.That((await PropertiesAsync(asset)).Count, Is.EqualTo(1));
            await asset.UploadThingDescriptionAsync(Document().Span.ToArray());
            ArrayOf<ReferenceDescription> interfaces = await ReferencesAsync(
                fixture, asset.AssetId, Ua.ReferenceTypeIds.HasInterface, BrowseDirection.Forward);
            Assert.That(interfaces.Count, Is.EqualTo(1), "Retirement must not remove the fixed owner interface.");
        }

        private static XElement Reference(string type, string target, bool inverse = false)
        {
            return new XElement(s_nodes + "Reference", new XAttribute("ReferenceType", type),
                inverse ? new XAttribute("IsForward", false) : null, target);
        }

        private static async Task<ArrayOf<ReferenceDescription>> ReferencesAsync(
            Fixture fixture, NodeId node, NodeId type, BrowseDirection direction)
        {
            BrowseResponse result = await fixture.Session.BrowseAsync(null, new ViewDescription(), 0,
                [new BrowseDescription
                {
                    NodeId = node, ReferenceTypeId = type, BrowseDirection = direction,
                    IncludeSubtypes = false, ResultMask = (uint)BrowseResultMask.All
                }], default);
            Assert.That(result.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            return result.Results[0].References;
        }

        private static readonly XNamespace s_nodes = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
    }
}
