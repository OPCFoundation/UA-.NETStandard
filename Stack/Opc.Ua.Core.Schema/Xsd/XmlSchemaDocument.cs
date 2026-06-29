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

using System;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Schema;

namespace Opc.Ua.Schema.Xsd
{
    /// <summary>
    /// An XML Schema document generated for an OPC UA data type or namespace.
    /// </summary>
    public sealed class XmlSchemaDocument : IUaSchema
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="XmlSchemaDocument"/> class.
        /// </summary>
        /// <param name="targetNamespace">The target namespace of the schema.</param>
        /// <param name="schema">The XML Schema object model.</param>
        public XmlSchemaDocument(string targetNamespace, XmlSchema schema)
        {
            TargetNamespace = targetNamespace ?? throw new ArgumentNullException(nameof(targetNamespace));
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        }

        /// <inheritdoc/>
        public UaSchemaFormat Format => UaSchemaFormat.Xsd;

        /// <inheritdoc/>
        public string MediaType => "application/xml";

        /// <inheritdoc/>
        public string TargetNamespace { get; }

        /// <summary>
        /// The XML Schema object model.
        /// </summary>
        public XmlSchema Schema { get; }

        /// <inheritdoc/>
        public void WriteTo(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using var writer = XmlWriter.Create(stream, WriterSettings());
            WriteSchema(writer);
        }

        /// <inheritdoc/>
        public void WriteTo(TextWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            using var xmlWriter = XmlWriter.Create(writer, WriterSettings());
            WriteSchema(xmlWriter);
        }

        /// <inheritdoc/>
        public string ToSchemaString()
        {
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            WriteTo(writer);
            return writer.ToString();
        }

        private static XmlWriterSettings WriterSettings()
        {
            return new XmlWriterSettings
            {
                Indent = true
            };
        }

        private void WriteSchema(XmlWriter writer)
        {
            writer.WriteStartElement("xs", "schema", XmlSchema.Namespace);
            writer.WriteAttributeString("xmlns", "ua", null, UaTypesNamespace);
            writer.WriteAttributeString("xmlns", "tns", null, TargetNamespace);
            WriteImportedNamespaceDeclarations(writer);
            writer.WriteAttributeString("targetNamespace", TargetNamespace);
            writer.WriteAttributeString("elementFormDefault", "qualified");

            foreach (XmlSchemaObject include in Schema.Includes)
            {
                WriteSchemaObject(writer, include);
            }

            foreach (XmlSchemaObject item in Schema.Items)
            {
                WriteSchemaObject(writer, item);
            }

            writer.WriteEndElement();
        }

        private void WriteSchemaObject(XmlWriter writer, XmlSchemaObject item)
        {
            switch (item)
            {
                case XmlSchemaImport import:
                    writer.WriteStartElement("xs", "import", XmlSchema.Namespace);
                    writer.WriteAttributeString("namespace", import.Namespace);
                    writer.WriteEndElement();
                    break;
                case XmlSchemaComplexType complexType:
                    WriteComplexType(writer, complexType);
                    break;
                case XmlSchemaSimpleType simpleType:
                    WriteSimpleType(writer, simpleType);
                    break;
                case XmlSchemaElement element:
                    WriteElement(writer, element);
                    break;
                case XmlSchemaSequence sequence:
                    WriteParticle(writer, sequence);
                    break;
                case XmlSchemaChoice choice:
                    WriteParticle(writer, choice);
                    break;
            }
        }

        private void WriteComplexType(XmlWriter writer, XmlSchemaComplexType complexType)
        {
            writer.WriteStartElement("xs", "complexType", XmlSchema.Namespace);
            if (!string.IsNullOrEmpty(complexType.Name))
            {
                writer.WriteAttributeString("name", complexType.Name);
            }

            WriteParticle(writer, complexType.Particle);
            writer.WriteEndElement();
        }

        private void WriteSimpleType(XmlWriter writer, XmlSchemaSimpleType simpleType)
        {
            writer.WriteStartElement("xs", "simpleType", XmlSchema.Namespace);
            writer.WriteAttributeString("name", simpleType.Name);
            if (simpleType.Content is XmlSchemaSimpleTypeRestriction restriction)
            {
                writer.WriteStartElement("xs", "restriction", XmlSchema.Namespace);
                writer.WriteAttributeString("base", QualifiedName(restriction.BaseTypeName));
                foreach (XmlSchemaObject facet in restriction.Facets)
                {
                    if (facet is XmlSchemaEnumerationFacet enumeration)
                    {
                        writer.WriteStartElement("xs", "enumeration", XmlSchema.Namespace);
                        writer.WriteAttributeString("value", enumeration.Value);
                        writer.WriteEndElement();
                    }
                }
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        private void WriteParticle(XmlWriter writer, XmlSchemaParticle? particle)
        {
            switch (particle)
            {
                case XmlSchemaSequence sequence:
                    writer.WriteStartElement("xs", "sequence", XmlSchema.Namespace);
                    foreach (XmlSchemaObject item in sequence.Items)
                    {
                        WriteSchemaObject(writer, item);
                    }
                    writer.WriteEndElement();
                    break;
                case XmlSchemaChoice choice:
                    writer.WriteStartElement("xs", "choice", XmlSchema.Namespace);
                    foreach (XmlSchemaObject item in choice.Items)
                    {
                        WriteSchemaObject(writer, item);
                    }
                    writer.WriteEndElement();
                    break;
            }
        }

        private void WriteElement(XmlWriter writer, XmlSchemaElement element)
        {
            writer.WriteStartElement("xs", "element", XmlSchema.Namespace);
            writer.WriteAttributeString("name", element.Name);
            if (!element.SchemaTypeName.IsEmpty)
            {
                writer.WriteAttributeString("type", QualifiedName(element.SchemaTypeName));
            }
            if (element.MinOccurs != 1)
            {
                writer.WriteAttributeString("minOccurs", XmlConvert.ToString(element.MinOccurs));
            }
            if (!string.IsNullOrEmpty(element.MaxOccursString))
            {
                writer.WriteAttributeString("maxOccurs", element.MaxOccursString);
            }
            if (element.IsNillable)
            {
                writer.WriteAttributeString("nillable", "true");
            }
            if (element.SchemaType is XmlSchemaComplexType complexType)
            {
                WriteComplexType(writer, complexType);
            }
            writer.WriteEndElement();
        }

        private string QualifiedName(XmlQualifiedName name)
        {
            if (name.Namespace == XmlSchema.Namespace)
            {
                return "xs:" + name.Name;
            }
            if (name.Namespace == UaTypesNamespace)
            {
                return "ua:" + name.Name;
            }
            if (name.Namespace == TargetNamespace)
            {
                return "tns:" + name.Name;
            }

            string prefix = PrefixForNamespace(name.Namespace);
            if (!string.IsNullOrEmpty(prefix))
            {
                return prefix + ":" + name.Name;
            }

            return name.Name;
        }

        private void WriteImportedNamespaceDeclarations(XmlWriter writer)
        {
            XmlQualifiedName[] namespaces = Schema.Namespaces.ToArray();
            for (int i = 0; i < namespaces.Length; i++)
            {
                XmlQualifiedName namespaceDeclaration = namespaces[i];
                if (namespaceDeclaration.Name is "xs" or
                    "ua" or
                    "tns")
                {
                    continue;
                }

                writer.WriteAttributeString(
                    "xmlns",
                    namespaceDeclaration.Name,
                    null,
                    namespaceDeclaration.Namespace);
            }
        }

        private string PrefixForNamespace(string namespaceUri)
        {
            XmlQualifiedName[] namespaces = Schema.Namespaces.ToArray();
            for (int i = 0; i < namespaces.Length; i++)
            {
                XmlQualifiedName namespaceDeclaration = namespaces[i];
                if (namespaceDeclaration.Namespace == namespaceUri)
                {
                    return namespaceDeclaration.Name;
                }
            }

            return string.Empty;
        }

        private const string UaTypesNamespace = "http://opcfoundation.org/UA/2008/02/Types.xsd";
    }
}
