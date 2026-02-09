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
        public XmlSchemaValidator(
            IFileSystem fileSystem = null,
            IDictionary<string, string> knownFiles = null)
            : base(fileSystem, knownFiles, null)
        {
        }

        /// <summary>
        /// Intializes the object with a import table.
        /// </summary>
        public XmlSchemaValidator(IReadOnlyDictionary<string, byte[]> importTable)
            : base(null, null, importTable)
        {
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
            using Stream istrm = FileSystem.OpenRead(inputPath);
            Validate(istrm, logger);
        }

        /// <summary>
        /// Generates the code from the contents of the address space.
        /// </summary>
        public void Validate(Stream stream, ILogger logger)
        {
            var handler = new ValidationEventHandler((_, e) => OnValidate(e, logger));
            TargetSchema = Load(stream, handler);

            foreach (XmlSchemaImport import in TargetSchema.Includes.OfType<XmlSchemaImport>())
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
        private static void OnValidate(ValidationEventArgs args, ILogger logger)
        {
            logger.LogError("Error in XML schema validation: {Message}", args.Message);
            throw new InvalidOperationException(args.Message, args.Exception);
        }
    }
}
