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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.XRegistry.Tests
{
    [TestFixture]
    [Category("XRegistry")]
    public sealed class RegistryModelCompatibilityTests
    {
        [Test]
        public void EveryPreexistingNodeRetainsItsDefinitionArgumentsAndRelationships()
        {
            string assets = Path.Combine(TestContext.CurrentContext.TestDirectory, "Assets");
            XElement baseline = XElement.Load(Path.Combine(assets, "XRegistry-0.6.0.NodeSet2.xml"));
            XElement current = XElement.Load(Path.Combine(assets, "XRegistry-current.NodeSet2.xml"));
            XNamespace ns = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
            Dictionary<string, XElement> previous = Nodes(baseline);
            Dictionary<string, XElement> actual = Nodes(current);
            Assert.That(previous, Has.Count.EqualTo(117));
            Assert.That(actual, Has.Count.EqualTo(320));
            foreach (KeyValuePair<string, XElement> node in previous)
            {
                Assert.That(actual.TryGetValue(node.Key, out XElement? newer), Is.True, node.Key);
                XElement oldContract = Normalize(node.Value, Aliases(baseline), previous, actual, ns);
                XElement newContract = Normalize(newer!, Aliases(current), previous, actual, ns);
                Assert.That(XNode.DeepEquals(oldContract, newContract), Is.True,
                    $"The pre-existing contract changed: {node.Key}\n{oldContract}\n{newContract}");
            }
        }

        private static Dictionary<string, XElement> Nodes(XElement model)
        {
            return model.Elements().Where(element => element.Attribute("NodeId") is not null)
                .ToDictionary(element => element.Attribute("NodeId")!.Value, StringComparer.Ordinal);
        }

        private static Dictionary<string, string> Aliases(XElement model)
        {
            return model.Descendants().Where(element => element.Name.LocalName == "Alias")
                .ToDictionary(element => element.Attribute("Alias")!.Value, element => element.Value,
                    StringComparer.Ordinal);
        }

        private static XElement Normalize(
            XElement node,
            Dictionary<string, string> aliases,
            Dictionary<string, XElement> previous,
            Dictionary<string, XElement> actual,
            XNamespace ns)
        {
            var result = new XElement(node);
            // This stack baseline predates the specification's 0.7/0.8 editorial clarifications.
            // Wire and structural metadata are compared here; the spec repository separately freezes
            // whole-node text against its later reviewed 10a56cb baseline.
            result.Descendants(ns + "Description").Remove();
            foreach (XElement element in result.DescendantsAndSelf())
            {
                element.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Remove();
                foreach (string attributeName in new[] { "ReferenceType", "DataType" })
                {
                    XAttribute? attribute = element.Attribute(attributeName);
                    if (attribute is not null && aliases.TryGetValue(attribute.Value, out string? expanded))
                    {
                        attribute.Value = expanded;
                    }
                }
                if (element.Name == ns + "Reference" && aliases.TryGetValue(element.Value, out string? target))
                {
                    element.Value = target;
                }
            }
            foreach (XElement reference in result.Element(ns + "References")!.Elements().ToArray())
            {
                if (reference.Attribute("IsForward")?.Value != "false" &&
                    reference.Value.StartsWith("ns=1;", StringComparison.Ordinal) &&
                    !previous.ContainsKey(reference.Value) &&
                    reference.Attribute("ReferenceType")?.Value is "i=35" or "i=46" or "i=47")
                {
                    Assert.That(actual.TryGetValue(reference.Value, out XElement? added), Is.True);
                    XElement? rule = added!.Element(ns + "References")?.Elements()
                        .SingleOrDefault(candidate =>
                            candidate.Attribute("ReferenceType")?.Value is "HasModellingRule" or "i=37");
                    Assert.That(rule?.Value, Is.AnyOf("i=80", "i=11508"),
                        "Only optional members/placeholders may be added to an existing type.");
                    reference.Remove();
                }
            }
            if (result.Attribute("BrowseName")?.Value is "1:NamespaceVersion" or "1:NamespacePublicationDate")
            {
                result.Element(ns + "Value")?.Remove();
            }
            return result;
        }
    }
}
