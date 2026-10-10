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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Tests
{
    public sealed partial class WotLegacyNativeAdmissionLiveTests
    {
        [Test]
        public async Task MandatoryQualifiedNamesKeepDistinctLegacyIdentities()
        {
            const string OtherUri = "urn:test:r42-second";
            XDocument model = XDocument.Parse(s_model);
            model.Root!.Element(s_nodes + "NamespaceUris")!.Add(new XElement(s_nodes + "Uri", OtherUri));
            model.Root.Element(s_nodes + "Models")!.Add(new XElement(s_nodes + "Model",
                new XAttribute("ModelUri", OtherUri)));
            model.Root.Elements(s_nodes + "UAObjectType")
                .Single(node => (string?)node.Attribute("NodeId") == "ns=1;i=4001")
                .Element(s_nodes + "References")!.Add(Reference("i=46", "ns=2;i=4102"));
            XElement variable = new(model.Root.Element(s_nodes + "UAVariable")!);
            variable.SetAttributeValue("NodeId", "ns=2;i=4102");
            variable.SetAttributeValue("BrowseName", "2:Speed");
            model.Root.Add(variable);
            await using Fixture fixture = await Fixture.CreateAsync(
                modelXml: model.ToString(SaveOptions.DisableFormatting), modelNamespaces: [ModelUri, OtherUri]);
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ByteString content = Document(properties: "{}");
            await asset.UploadThingDescriptionAsync(content.Span.ToArray());
            ArrayOf<WotAssetVariableEntry> properties = await PropertiesAsync(asset);
            Assert.That(properties.Count, Is.EqualTo(2));
            Assert.That(properties.ToList().Select(property => property.NodeId), Is.Unique);
            var names = new List<QualifiedName>();
            for (int index = 0; index < properties.Count; index++)
            {
                ArrayOf<DataValue> values = await fixture.ReadAttributesAsync(
                    properties[index].NodeId, [Attributes.BrowseName, Attributes.Value]);
                Assert.That(values[0].WrappedValue.TryGetValue(out QualifiedName name), Is.True);
                Assert.That(values[1].WrappedValue.TryGetValue(out double value), Is.True);
                Assert.That(value, Is.Zero);
                names.Add(name);
            }
            Assert.That(names, Is.EquivalentTo(new[]
            {
                new QualifiedName("Speed", fixture.ModelNamespaceIndex),
                new QualifiedName("Speed", fixture.Session.NamespaceUris.GetIndexOrAppend(OtherUri))
            }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task QualifiedMandatoryIdentitiesPreserveUniqueNamesAcrossRestart(bool reverseOrder)
        {
            XDocument model = CreateQualifiedIdentityModel(reverseOrder);
            await using Fixture fixture = await Fixture.CreateAsync(
                modelXml: model.ToString(SaveOptions.DisableFormatting),
                modelNamespaces: [ModelUri, "urn:test:r42-second"]);
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ByteString content = Document(properties: "{}");
            await asset.UploadThingDescriptionAsync(content.Span.ToArray());
            ushort ns = asset.AssetId.NamespaceIndex;
            NodeId[] expected =
            [
                new NodeId("Assets/mapped/props/urn%3Atest%3Ar42-admission/Speed", ns),
                new NodeId("Assets/mapped/props/urn%3Atest%3Ar42-second/Speed", ns),
                new NodeId("Assets/mapped/props/Load", ns)
            ];
            Assert.That((await PropertiesAsync(asset)).ToList().Select(property => property.NodeId),
                Is.EquivalentTo(expected));
            await fixture.RestartManagerAsync();
            asset = await fixture.Client.OpenAssetAsync(asset.AssetId);
            Assert.That((await PropertiesAsync(asset)).ToList().Select(property => property.NodeId),
                Is.EquivalentTo(expected));
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));
        }

        [Test]
        public async Task AuthoredPropertyIdentityWinsOverQualifiedMandatorySibling()
        {
            XDocument model = CreateQualifiedIdentityModel(false);
            await using Fixture fixture = await Fixture.CreateAsync(
                modelXml: model.ToString(SaveOptions.DisableFormatting),
                modelNamespaces: [ModelUri, "urn:test:r42-second"]);
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ByteString content = Document(properties: """
                {"Speed":{"uav:browseName":"model:Speed","type":"number",
                    "forms":[{"href":"sim://opcua.test/wot/speed"}]}}
                """);
            await asset.UploadThingDescriptionAsync(content.Span.ToArray());
            ushort ns = asset.AssetId.NamespaceIndex;
            Assert.That((await PropertiesAsync(asset)).ToList().Select(property => property.NodeId),
                Is.EquivalentTo(new[]
                {
                    new NodeId("Assets/mapped/props/Speed", ns),
                    new NodeId("Assets/mapped/props/urn%3Atest%3Ar42-second/Speed", ns),
                    new NodeId("Assets/mapped/props/Load", ns)
                }));
        }

        private static XDocument CreateQualifiedIdentityModel(bool reverseOrder)
        {
            XDocument model = XDocument.Parse(s_model);
            model.Root!.Element(s_nodes + "NamespaceUris")!.Add(new XElement(s_nodes + "Uri", "urn:test:r42-second"));
            model.Root.Element(s_nodes + "Models")!.Add(new XElement(s_nodes + "Model",
                new XAttribute("ModelUri", "urn:test:r42-second")));
            XElement references = model.Root.Elements(s_nodes + "UAObjectType")
                .Single(node => (string?)node.Attribute("NodeId") == "ns=1;i=4001")
                .Element(s_nodes + "References")!;
            references.Add(Reference("i=46", "ns=2;i=4102"), Reference("i=46", "ns=1;i=4103"));
            XElement second = new(model.Root.Element(s_nodes + "UAVariable")!);
            second.SetAttributeValue("NodeId", "ns=2;i=4102");
            second.SetAttributeValue("BrowseName", "2:Speed");
            XElement unique = new(model.Root.Element(s_nodes + "UAVariable")!);
            unique.SetAttributeValue("NodeId", "ns=1;i=4103");
            unique.SetAttributeValue("BrowseName", "1:Load");
            unique.Element(s_nodes + "DisplayName")!.Value = "Load";
            model.Root.Add(second, unique);
            if (reverseOrder)
            {
                references.ReplaceNodes(references.Elements().Reverse().ToArray());
                XElement[] variables = model.Root.Elements(s_nodes + "UAVariable").Reverse().ToArray();
                foreach (XElement variable in variables)
                {
                    variable.Remove();
                }
                model.Root.Add(variables);
            }
            return model;
        }
    }
}
