/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Schema;

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
        public XmlSchemaValidator(IDictionary<string, string> fileTable) : base(fileTable)
        {
            SetResourcePaths(WellKnownDictionaries);
        }

        /// <summary>
        /// Intializes the object with a import table.
        /// </summary>
        public XmlSchemaValidator(IDictionary<string, byte[]> importTable) : base(importTable)
        {
            SetResourcePaths(WellKnownDictionaries);
        }

        /// <summary>
        /// The schema set that was validated.
        /// </summary>
        public XmlSchemaSet SchemaSet => m_schemaSet;
        /// <summary>
        /// The schema that was validated.
        /// </summary>
        public XmlSchema TargetSchema => m_schema;

        /// <summary>
        /// Generates the code from the contents of the address space.
        /// </summary>
        public void Validate(string inputPath)
        {
            using Stream istrm = File.OpenRead(inputPath);
            Validate(istrm);
        }

        /// <summary>
        /// Generates the code from the contents of the address space.
        /// </summary>
        public void Validate(Stream stream)
        {
            using var xmlReader = XmlReader.Create(stream, Utils.DefaultXmlReaderSettings());
            m_schema = XmlSchema.Read(xmlReader, new ValidationEventHandler(OnValidate));

            Assembly assembly = typeof(XmlSchemaValidator).GetTypeInfo().Assembly;
            foreach (XmlSchemaImport import in m_schema.Includes.OfType<XmlSchemaImport>())
            {
                string location = null;

                if (!KnownFiles.TryGetValue(import.Namespace, out location))
                {
                    location = import.SchemaLocation;
                }

                var fileInfo = new FileInfo(location);
                XmlReaderSettings settings = Utils.DefaultXmlReaderSettings();
                if (!fileInfo.Exists)
                {
                    using var strm = new StreamReader(assembly.GetManifestResourceStream(location));
                    using var schemaReader = XmlReader.Create(strm, settings);
                    import.Schema = XmlSchema.Read(schemaReader, new ValidationEventHandler(OnValidate));
                }
                else
                {
                    using Stream strm = File.OpenRead(location);
                    using var schemaReader = XmlReader.Create(strm, settings);
                    import.Schema = XmlSchema.Read(schemaReader, new ValidationEventHandler(OnValidate));
                }
            }

            m_schemaSet = new XmlSchemaSet();
            m_schemaSet.Add(m_schema);
            m_schemaSet.Compile();
        }

        /// <summary>
        /// Returns the schema for the specified type (returns the entire schema if null).
        /// </summary>
        public override string GetSchema(string typeName)
        {
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();

            var ostrm = new MemoryStream();
            var writer = XmlWriter.Create(ostrm, settings);

            try
            {
                if (typeName == null || m_schema.Elements.Values.Count == 0)
                {
                    m_schema.Write(writer);
                }
                else
                {
                    foreach (XmlSchemaObject current in m_schema.Elements.Values)
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
        private static void OnValidate(object sender, ValidationEventArgs args)
        {
            Utils.LogError("Error in XML schema validation: {0}", args.Message);
            throw new InvalidOperationException(args.Message, args.Exception);
        }

        /// <summary>
        /// The well known schemas embedded in the assembly.
        /// </summary>
        protected static readonly string[][] WellKnownDictionaries =
        [
            [Namespaces.OpcUaXsd, "Opc.Ua.Schema.Opc.Ua.Types.xsd"]
        ];
        private XmlSchema m_schema;
        private XmlSchemaSet m_schemaSet;
    }
}
