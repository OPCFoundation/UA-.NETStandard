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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Schema.Xml
{
    /// <summary>
    /// Generates files used to describe data types.
    /// </summary>
    public class XmlSchemaValidator : SchemaValidator
    {
        /// <summary>
        /// Intializes the object with default values.
        /// </summary>
        public XmlSchemaValidator()
        {
            SetResourcePaths(WellKnownDictionaries);
        }

        /// <summary>
        /// Intializes the object with a file table.
        /// </summary>
        public XmlSchemaValidator(IDictionary<string, string> fileTable)
            : base(fileTable)
        {
            SetResourcePaths(WellKnownDictionaries);
        }

        /// <summary>
        /// Intializes the object with a import table.
        /// </summary>
        public XmlSchemaValidator(IDictionary<string, byte[]> importTable)
            : base(importTable)
        {
            SetResourcePaths(WellKnownDictionaries);
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
        public void Validate(string inputPath, ILogger logger)
        {
            using Stream istrm = File.OpenRead(inputPath);
            Validate(istrm, logger);
        }

        /// <summary>
        /// Generates the code from the contents of the address space.
        /// </summary>
        public void Validate(Stream stream, ILogger logger)
        {
            var handler = new ValidationEventHandler((sender, e) => OnValidate(sender, e, logger));
            using var xmlReader = XmlReader.Create(stream, CoreUtils.DefaultXmlReaderSettings());
            TargetSchema = XmlSchema.Read(xmlReader, handler);

            Assembly assembly = typeof(XmlSchemaValidator).GetTypeInfo().Assembly;
            foreach (XmlSchemaImport import in TargetSchema.Includes.OfType<XmlSchemaImport>())
            {
                if (!KnownFiles.TryGetValue(import.Namespace, out string location))
                {
                    location = import.SchemaLocation;
                }

                var fileInfo = new FileInfo(location);
                XmlReaderSettings settings = CoreUtils.DefaultXmlReaderSettings();
                if (!fileInfo.Exists)
                {
                    using var strm = new StreamReader(assembly.GetManifestResourceStream(location));
                    using var schemaReader = XmlReader.Create(strm, settings);
                    import.Schema = XmlSchema.Read(schemaReader, handler);
                }
                else
                {
                    using Stream strm = File.OpenRead(location);
                    using var schemaReader = XmlReader.Create(strm, settings);
                    import.Schema = XmlSchema.Read(schemaReader, handler);
                }
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
            XmlWriterSettings settings = CoreUtils.DefaultXmlWriterSettings();

            var ostrm = new MemoryStream();
            var writer = XmlWriter.Create(ostrm, settings);

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
                writer.Flush();
                writer.Dispose();
            }

            return Encoding.UTF8.GetString(ostrm.ToArray());
        }

        /// <summary>
        /// Handles a validation error.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static void OnValidate(object sender, ValidationEventArgs args, ILogger logger)
        {
            logger.LogError("Error in XML schema validation: {Message}", args.Message);
            throw new InvalidOperationException(args.Message, args.Exception);
        }

        /// <summary>
        /// The well known schemas embedded in the assembly.
        /// </summary>
        protected static readonly string[][] WellKnownDictionaries =
        [
            [Namespaces.OpcUaXsd, "Opc.Ua.Schema.Opc.Ua.Types.xsd"]
        ];
    }
}
