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
using Opc.Ua.Schema.Binary;

namespace Opc.Ua.Schema.Bsd
{
    /// <summary>
    /// An OPC Binary schema document generated for an OPC UA data type or namespace.
    /// </summary>
    public sealed class BinarySchemaDocument : IUaSchema
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BinarySchemaDocument"/> class.
        /// </summary>
        /// <param name="targetNamespace">The target namespace of the dictionary.</param>
        /// <param name="dictionary">The OPC Binary type dictionary object model.</param>
        public BinarySchemaDocument(string targetNamespace, TypeDictionary dictionary)
        {
            TargetNamespace = targetNamespace ?? throw new ArgumentNullException(nameof(targetNamespace));
            Dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        }

        /// <inheritdoc/>
        public UaSchemaFormat Format => UaSchemaFormat.Bsd;

        /// <inheritdoc/>
        public string MediaType => "application/xml";

        /// <inheritdoc/>
        public string TargetNamespace { get; }

        /// <summary>
        /// The OPC Binary type dictionary object model.
        /// </summary>
        public TypeDictionary Dictionary { get; }

        /// <inheritdoc/>
        public void WriteTo(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using XmlWriter writer = XmlWriter.Create(stream, WriterSettings());
            WriteDictionary(writer);
        }

        /// <inheritdoc/>
        public void WriteTo(TextWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            using XmlWriter xmlWriter = XmlWriter.Create(writer, WriterSettings());
            WriteDictionary(xmlWriter);
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
            return new XmlWriterSettings { Indent = true };
        }

        private void WriteDictionary(XmlWriter writer)
        {
            writer.WriteStartElement("opc", "TypeDictionary", OpcBinaryNamespace);
            writer.WriteAttributeString("xmlns", "xsi", null, XmlSchemaInstanceNamespace);
            writer.WriteAttributeString("xmlns", "ua", null, UaTypesNamespace);
            writer.WriteAttributeString("xmlns", "tns", null, TargetNamespace);
            WriteImportedNamespaceDeclarations(writer);
            if (Dictionary.DefaultByteOrderSpecified)
            {
                writer.WriteAttributeString("DefaultByteOrder", Dictionary.DefaultByteOrder.ToString());
            }
            writer.WriteAttributeString("TargetNamespace", TargetNamespace);

            if (Dictionary.Import != null)
            {
                foreach (ImportDirective import in Dictionary.Import)
                {
                    WriteImport(writer, import);
                }
            }

            if (Dictionary.Items != null)
            {
                foreach (TypeDescription item in Dictionary.Items)
                {
                    WriteTypeDescription(writer, item);
                }
            }

            writer.WriteEndElement();
        }

        private static void WriteImport(XmlWriter writer, ImportDirective import)
        {
            writer.WriteStartElement("opc", "Import", OpcBinaryNamespace);
            if (!string.IsNullOrEmpty(import.Namespace))
            {
                writer.WriteAttributeString("Namespace", import.Namespace);
            }
            if (!string.IsNullOrEmpty(import.Location))
            {
                writer.WriteAttributeString("Location", import.Location);
            }
            writer.WriteEndElement();
        }

        private void WriteTypeDescription(XmlWriter writer, TypeDescription item)
        {
            switch (item)
            {
                case StructuredType structuredType:
                    WriteStructuredType(writer, structuredType);
                    break;
                case EnumeratedType enumeratedType:
                    WriteEnumeratedType(writer, enumeratedType);
                    break;
                case OpaqueType opaqueType:
                    WriteOpaqueType(writer, opaqueType);
                    break;
            }
        }

        private void WriteStructuredType(XmlWriter writer, StructuredType structuredType)
        {
            writer.WriteStartElement("opc", "StructuredType", OpcBinaryNamespace);
            writer.WriteAttributeString("Name", structuredType.Name);
            WriteDocumentation(writer, structuredType.Documentation);
            if (structuredType.Field != null)
            {
                foreach (FieldType field in structuredType.Field)
                {
                    WriteField(writer, field);
                }
            }
            writer.WriteEndElement();
        }

        private void WriteEnumeratedType(XmlWriter writer, EnumeratedType enumeratedType)
        {
            writer.WriteStartElement("opc", "EnumeratedType", OpcBinaryNamespace);
            writer.WriteAttributeString("Name", enumeratedType.Name);
            if (enumeratedType.LengthInBitsSpecified)
            {
                writer.WriteAttributeString(
                    "LengthInBits",
                    XmlConvert.ToString(enumeratedType.LengthInBits));
            }
            WriteDocumentation(writer, enumeratedType.Documentation);
            if (enumeratedType.EnumeratedValue != null)
            {
                foreach (EnumeratedValue value in enumeratedType.EnumeratedValue)
                {
                    WriteEnumeratedValue(writer, value);
                }
            }
            writer.WriteEndElement();
        }

        private void WriteOpaqueType(XmlWriter writer, OpaqueType opaqueType)
        {
            writer.WriteStartElement("opc", "OpaqueType", OpcBinaryNamespace);
            writer.WriteAttributeString("Name", opaqueType.Name);
            if (opaqueType.LengthInBitsSpecified)
            {
                writer.WriteAttributeString("LengthInBits", XmlConvert.ToString(opaqueType.LengthInBits));
            }
            WriteDocumentation(writer, opaqueType.Documentation);
            writer.WriteEndElement();
        }

        private void WriteField(XmlWriter writer, FieldType field)
        {
            writer.WriteStartElement("opc", "Field", OpcBinaryNamespace);
            writer.WriteAttributeString("Name", field.Name);
            if (field.TypeName != null)
            {
                writer.WriteAttributeString("TypeName", QualifiedName(field.TypeName));
            }
            if (field.LengthSpecified)
            {
                writer.WriteAttributeString("Length", XmlConvert.ToString(field.Length));
            }
            if (!string.IsNullOrEmpty(field.LengthField))
            {
                writer.WriteAttributeString("LengthField", field.LengthField);
            }
            if (field.IsLengthInBytes)
            {
                writer.WriteAttributeString("IsLengthInBytes", "true");
            }
            if (!string.IsNullOrEmpty(field.SwitchField))
            {
                writer.WriteAttributeString("SwitchField", field.SwitchField);
            }
            if (field.SwitchValueSpecified)
            {
                writer.WriteAttributeString("SwitchValue", XmlConvert.ToString(field.SwitchValue));
            }
            if (field.SwitchOperandSpecified)
            {
                writer.WriteAttributeString("SwitchOperand", field.SwitchOperand.ToString());
            }
            WriteDocumentation(writer, field.Documentation);
            writer.WriteEndElement();
        }

        private static void WriteEnumeratedValue(XmlWriter writer, EnumeratedValue value)
        {
            writer.WriteStartElement("opc", "EnumeratedValue", OpcBinaryNamespace);
            writer.WriteAttributeString("Name", value.Name);
            if (value.ValueSpecified)
            {
                writer.WriteAttributeString("Value", XmlConvert.ToString(value.Value));
            }
            WriteDocumentation(writer, value.Documentation);
            writer.WriteEndElement();
        }

        private static void WriteDocumentation(XmlWriter writer, Documentation? documentation)
        {
            if (documentation?.Text == null || documentation.Text.Length == 0)
            {
                return;
            }

            writer.WriteStartElement("opc", "Documentation", OpcBinaryNamespace);
            for (int i = 0; i < documentation.Text.Length; i++)
            {
                writer.WriteString(documentation.Text[i]);
            }
            writer.WriteEndElement();
        }

        private string QualifiedName(XmlQualifiedName name)
        {
            if (name.Namespace == OpcBinaryNamespace)
            {
                return "opc:" + name.Name;
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
            if (Dictionary.Import == null)
            {
                return;
            }

            int prefixIndex = 1;
            for (int i = 0; i < Dictionary.Import.Length; i++)
            {
                string? namespaceUri = Dictionary.Import[i].Namespace;
                if (string.IsNullOrEmpty(namespaceUri) ||
                    namespaceUri == UaTypesNamespace ||
                    namespaceUri == TargetNamespace)
                {
                    continue;
                }

                writer.WriteAttributeString("xmlns", "n" + prefixIndex, null, namespaceUri);
                prefixIndex++;
            }
        }

        private string PrefixForNamespace(string namespaceUri)
        {
            if (Dictionary.Import == null)
            {
                return string.Empty;
            }

            int prefixIndex = 1;
            for (int i = 0; i < Dictionary.Import.Length; i++)
            {
                string? importNamespace = Dictionary.Import[i].Namespace;
                if (string.IsNullOrEmpty(importNamespace) ||
                    importNamespace == UaTypesNamespace ||
                    importNamespace == TargetNamespace)
                {
                    continue;
                }

                if (importNamespace == namespaceUri)
                {
                    return "n" + prefixIndex;
                }

                prefixIndex++;
            }

            return string.Empty;
        }

        private const string OpcBinaryNamespace = "http://opcfoundation.org/BinarySchema/";
        private const string UaTypesNamespace = "http://opcfoundation.org/UA/";
        private const string XmlSchemaInstanceNamespace = "http://www.w3.org/2001/XMLSchema-instance";
    }
}
