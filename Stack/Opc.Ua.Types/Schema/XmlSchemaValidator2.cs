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

using System.Text;
using System.Xml;
using System.Xml.Schema;
using System.Collections.Generic;
using System;
using System.Linq;
using System.IO;

namespace Opc.Ua.Schema.Xml
{
    /// <summary>
    /// Generates files used to describe data types.
    /// </summary>
    public class XmlSchemaValidator2 : SchemaValidator
    {
        /// <summary>
        /// Well known xml schema files to namespace mappings.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> WellKnown =
            new Dictionary<string, string>
            {
                [Namespaces.OpcUaBuiltInTypes] = "BuiltInTypes.xsd",
                [Namespaces.OpcUaXsd] = "Opc.Ua.Types.xsd",
                [Namespaces.OpcUa] = "Opc.Ua.Types.xsd"
            };

        /// <summary>
        /// Intializes the object with a file table.
        /// </summary>
        public XmlSchemaValidator2(
            IFileSystem fileSystem,
            Dictionary<string, string> fileTable = null)
            : base(fileSystem, fileTable, null)
        {
            AddWellKnownFiles(WellKnown);
        }

        /// <summary>
        /// The schema set that was validated.
        /// </summary>
        public XmlSchemaSet SchemaSet { get; private set; }

        /// <summary>
        /// The schema that was validated.
        /// </summary>
        public XmlSchema TargetSchema { get; private set; }

        /// <summary>
        /// Generates the code from the contents of the address space.
        /// </summary>
        public void Validate(string inputPath)
        {
            using Stream istrm = FileSystem.OpenRead(inputPath);
            Validate(istrm);
        }

        /// <summary>
        /// Generates the code from the contents of the address space.
        /// </summary>
        public void Validate(Stream stream)
        {
            var handler = new ValidationEventHandler(OnValidate);
            TargetSchema = Load(stream, handler);

            foreach (XmlSchemaImport import in TargetSchema.Includes.Cast<XmlSchemaImport>())
            {
                import.Schema = Load(import.SchemaLocation, import.Namespace, handler);
            }

            SchemaSet = new XmlSchemaSet();
            SchemaSet.Add(TargetSchema);
            SchemaSet.Compile();
        }

        /// <summary>
        /// Returns the schema for the specified type (returns the entire schema if null).
        /// </summary>
        public override string GetSchema(string typeName)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                Indent = true,
                IndentChars = "    "
            };

            using var ostrm = new MemoryStream();
            using var writer = XmlWriter.Create(ostrm, settings);

            try
            {
                if (typeName == null || TargetSchema.Elements.Values.Count == 0)
                {
                    TargetSchema.Write(writer);
                }
                else
                {
                    foreach (XmlSchemaObject current in TargetSchema.Elements.Values)
                    {
                        if (current is XmlSchemaElement element && element.Name == typeName)
                        {
                            var schema = new XmlSchema();
                            schema.Items.Add(element.ElementSchemaType);
                            schema.Items.Add(element);
                            schema.Write(writer);
                            break;
                        }
                    }
                }
            }
            finally
            {
                writer.Close();
            }

            return Encoding.UTF8.GetString(ostrm.ToArray());
        }

        /// <summary>
        /// Handles a valdiation error.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static void OnValidate(object sender, ValidationEventArgs args)
        {
            throw new InvalidOperationException(args.Message, args.Exception);
        }
    }
}
