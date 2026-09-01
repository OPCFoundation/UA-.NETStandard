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

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Opc.Ua.Export;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// NodeSet2 fixtures for the analog Properties of OPC 10000-8 -
    /// <c>EngineeringUnits</c>, <c>EURange</c> and <c>InstrumentRange</c> - and
    /// for localized text, shared by the units, ranges, localization and
    /// value-rank tests.
    /// </summary>
    internal static class WotAnalogTestData
    {
        internal const string UaXmlNamespace = "http://opcfoundation.org/UA/2008/02/Types.xsd";
        internal const string UnitAuthority = "http://www.opcfoundation.org/UA/units/un/cefact";
        internal const string RootNodeId = "ns=1;i=1000";
        internal const string MeasurementNodeId = "ns=1;i=1001";
        internal const string EuRangeNodeId = "ns=1;i=1002";
        internal const string EngineeringUnitsNodeId = "ns=1;i=1003";
        internal const string InstrumentRangeNodeId = "ns=1;i=1004";

        /// <summary>
        /// An ObjectType whose analog Variable declares all three Properties
        /// OPC 10000-8 gives an <c>AnalogItemType</c>.
        /// </summary>
        internal static UANodeSet CreateAnalogNodeSet(
            bool withInstrumentRange = true,
            string? unitLocale = null,
            string? descriptionLocale = null)
        {
            var items = new List<UANode>
            {
                new UAObjectType
                {
                    NodeId = RootNodeId,
                    BrowseName = "1:AnalogDeviceType",
                    DisplayName = Text("AnalogDeviceType"),
                    References =
                    [
                        new Reference
                        {
                            ReferenceType = "HasSubtype", IsForward = false, Value = "i=58"
                        },
                        new Reference
                        {
                            ReferenceType = "HasComponent",
                            IsForward = true,
                            Value = MeasurementNodeId
                        }
                    ]
                },
                new UAVariable
                {
                    NodeId = MeasurementNodeId,
                    BrowseName = "1:Measurement",
                    DisplayName = Text("Measurement"),
                    ParentNodeId = RootNodeId,
                    DataType = "i=11",
                    AccessLevel = 1,
                    References =
                    [
                        new Reference
                        {
                            ReferenceType = "HasTypeDefinition",
                            IsForward = true,
                            Value = "i=2368"
                        },
                        new Reference
                        {
                            ReferenceType = "HasComponent",
                            IsForward = false,
                            Value = RootNodeId
                        },
                        new Reference
                        {
                            ReferenceType = "HasModellingRule",
                            IsForward = true,
                            Value = "i=78"
                        },
                        new Reference
                        {
                            ReferenceType = "HasProperty",
                            IsForward = true,
                            Value = EuRangeNodeId
                        },
                        new Reference
                        {
                            ReferenceType = "HasProperty",
                            IsForward = true,
                            Value = EngineeringUnitsNodeId
                        }
                    ]
                },
                CreateRangeProperty(
                    EuRangeNodeId, "EURange", MeasurementNodeId, -5, 95, "i=78"),
                CreateEngineeringUnitsProperty(
                    EngineeringUnitsNodeId,
                    MeasurementNodeId,
                    UnitAuthority,
                    4408652,
                    "°C",
                    unitLocale,
                    "degree Celsius",
                    descriptionLocale)
            };
            if (withInstrumentRange)
            {
                items[1].References =
                [
                    .. items[1].References!,
                    new Reference
                    {
                        ReferenceType = "HasProperty",
                        IsForward = true,
                        Value = InstrumentRangeNodeId
                    }
                ];
                items.Add(CreateRangeProperty(
                    InstrumentRangeNodeId,
                    "InstrumentRange",
                    MeasurementNodeId,
                    -50,
                    150,
                    "i=80"));
            }
            // A NodeSet2 document may only use a name where a NodeId is
            // expected if it declares that name, so the fixture declares what
            // it uses and is a document a Server could load.
            return NodeSetAliasCompleter.Complete(new UANodeSet
            {
                NamespaceUris = ["urn:test:analog"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:analog" }],
                Items = [.. items]
            })!;
        }

        internal static UAVariable CreateRangeProperty(
            string nodeId,
            string browseName,
            string parentNodeId,
            double low,
            double high,
            string modellingRule)
        {
            return new UAVariable
            {
                NodeId = nodeId,
                BrowseName = browseName,
                DisplayName = Text(browseName),
                ParentNodeId = parentNodeId,
                DataType = "i=884",
                AccessLevel = 1,
                Value = RangeValue(low, high),
                References =
                [
                    new Reference
                    {
                        ReferenceType = "HasTypeDefinition", IsForward = true, Value = "i=68"
                    },
                    new Reference
                    {
                        ReferenceType = "HasProperty",
                        IsForward = false,
                        Value = parentNodeId
                    },
                    new Reference
                    {
                        ReferenceType = "HasModellingRule",
                        IsForward = true,
                        Value = modellingRule
                    }
                ]
            };
        }

        internal static UAVariable CreateEngineeringUnitsProperty(
            string nodeId,
            string parentNodeId,
            string namespaceUri,
            int unitId,
            string displayName,
            string? displayLocale,
            string? description,
            string? descriptionLocale)
        {
            return new UAVariable
            {
                NodeId = nodeId,
                BrowseName = "EngineeringUnits",
                DisplayName = Text("EngineeringUnits"),
                ParentNodeId = parentNodeId,
                DataType = "i=887",
                AccessLevel = 1,
                Value = EngineeringUnitsValue(
                    namespaceUri, unitId, displayName, displayLocale,
                    description, descriptionLocale),
                References =
                [
                    new Reference
                    {
                        ReferenceType = "HasTypeDefinition", IsForward = true, Value = "i=68"
                    },
                    new Reference
                    {
                        ReferenceType = "HasProperty",
                        IsForward = false,
                        Value = parentNodeId
                    },
                    new Reference
                    {
                        ReferenceType = "HasModellingRule", IsForward = true, Value = "i=80"
                    }
                ]
            };
        }

        internal static System.Xml.XmlElement RangeValue(double low, double high)
        {
            var document = new System.Xml.XmlDocument { XmlResolver = null };
            System.Xml.XmlElement body = ExtensionObject(document, "i=885", "Range");
            Append(document, body, "Low", low.ToString("R", CultureInfo.InvariantCulture));
            Append(document, body, "High", high.ToString("R", CultureInfo.InvariantCulture));
            return (System.Xml.XmlElement)body.ParentNode!.ParentNode!;
        }

        internal static System.Xml.XmlElement EngineeringUnitsValue(
            string namespaceUri,
            int unitId,
            string displayName,
            string? displayLocale = null,
            string? description = null,
            string? descriptionLocale = null)
        {
            var document = new System.Xml.XmlDocument { XmlResolver = null };
            System.Xml.XmlElement body = ExtensionObject(document, "i=888", "EUInformation");
            Append(document, body, "NamespaceUri", namespaceUri);
            Append(
                document, body, "UnitId", unitId.ToString(CultureInfo.InvariantCulture));
            AppendText(document, body, "DisplayName", displayName, displayLocale);
            if (description is not null)
            {
                AppendText(document, body, "Description", description, descriptionLocale);
            }
            return (System.Xml.XmlElement)body.ParentNode!.ParentNode!;
        }

        internal static Export.LocalizedText[] Text(string value, string? locale = null)
        {
            return
            [
                new Export.LocalizedText
                {
                    Locale = locale ?? string.Empty,
                    Value = value
                }
            ];
        }

        internal static Export.LocalizedText[] Text(
            params (string Locale, string Value)[] entries)
        {
            var texts = new List<Export.LocalizedText>();
            foreach ((string locale, string value) in entries)
            {
                texts.Add(new Export.LocalizedText { Locale = locale, Value = value });
            }
            return [.. texts];
        }

        private static System.Xml.XmlElement ExtensionObject(
            System.Xml.XmlDocument document,
            string encodingId,
            string bodyName)
        {
            System.Xml.XmlElement extension = document.CreateElement(
                "uax", "ExtensionObject", UaXmlNamespace);
            System.Xml.XmlElement typeId = document.CreateElement("uax", "TypeId", UaXmlNamespace);
            Append(document, typeId, "Identifier", encodingId);
            extension.AppendChild(typeId);
            System.Xml.XmlElement wrapper = document.CreateElement("uax", "Body", UaXmlNamespace);
            System.Xml.XmlElement body = document.CreateElement("uax", bodyName, UaXmlNamespace);
            wrapper.AppendChild(body);
            extension.AppendChild(wrapper);
            return body;
        }

        private static void Append(
            System.Xml.XmlDocument document,
            System.Xml.XmlElement parent,
            string name,
            string value)
        {
            System.Xml.XmlElement element = document.CreateElement("uax", name, UaXmlNamespace);
            element.InnerText = value;
            parent.AppendChild(element);
        }

        private static void AppendText(
            System.Xml.XmlDocument document,
            System.Xml.XmlElement parent,
            string name,
            string value,
            string? locale)
        {
            System.Xml.XmlElement element = document.CreateElement("uax", name, UaXmlNamespace);
            if (!string.IsNullOrEmpty(locale))
            {
                Append(document, element, "Locale", locale!);
            }
            Append(document, element, "Text", value);
            parent.AppendChild(element);
        }

        internal static string Describe(IReadOnlyList<Ua.Wot.WotDiagnostic> diagnostics)
        {
            var builder = new System.Text.StringBuilder();
            foreach (Ua.Wot.WotDiagnostic diagnostic in diagnostics)
            {
                builder.Append(diagnostic).Append(Environment.NewLine);
            }
            return builder.ToString();
        }
    }
}
