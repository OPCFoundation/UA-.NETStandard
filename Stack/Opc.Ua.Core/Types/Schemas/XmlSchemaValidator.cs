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
        #region Constructors
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
        #endregion

        #region Public Members
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
            using (Stream istrm = File.OpenRead(inputPath))
            {
                Validate(istrm);
            }
        }

        /// <summary>
        /// Generates the code from the contents of the address space.
        /// </summary>
        public void Validate(Stream stream)
        {
            using (var xmlReader = XmlReader.Create(stream, Utils.DefaultXmlReaderSettings()))
            {
                m_schema = XmlSchema.Read(xmlReader, new ValidationEventHandler(OnValidate));

                var assembly = typeof(XmlSchemaValidator).GetTypeInfo().Assembly;
                foreach (XmlSchemaImport import in m_schema.Includes)
                {
                    string location = null;

                    if (!KnownFiles.TryGetValue(import.Namespace, out location))
                    {
                        location = import.SchemaLocation;
                    }

                    FileInfo fileInfo = new FileInfo(location);
                    var settings = Utils.DefaultXmlReaderSettings();
                    if (!fileInfo.Exists)
                    {
                        using (StreamReader strm = new StreamReader(assembly.GetManifestResourceStream(location)))
                        using (var schemaReader = XmlReader.Create(strm, settings))
                        {
                            import.Schema = XmlSchema.Read(schemaReader, new ValidationEventHandler(OnValidate));
                        }
                    }
                    else
                    {
                        using (Stream strm = File.OpenRead(location))
                        using (var schemaReader = XmlReader.Create(strm, settings))
                        {
                            import.Schema = XmlSchema.Read(schemaReader, new ValidationEventHandler(OnValidate));
                        }
                    }
                }

                m_schemaSet = new XmlSchemaSet();
                m_schemaSet.Add(m_schema);
                m_schemaSet.Compile();
            }
        }

        /// <summary>
        /// Returns the schema for the specified type (returns the entire schema if null).
        /// </summary>
        public override string GetSchema(string typeName)
        {
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();

            MemoryStream ostrm = new MemoryStream();
            XmlWriter writer = XmlWriter.Create(ostrm, settings);

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
                        XmlSchemaElement element = current as XmlSchemaElement;

                        if (element != null)
                        {
                            if (element.Name == typeName)
                            {
                                XmlSchema schema = new XmlSchema();
                                schema.Items.Add(element.ElementSchemaType);
                                schema.Items.Add(element);
                                schema.Write(writer);
                                break;
                            }
                        }
                    }
                }
            }
            finally
            {
                writer.Flush();
                writer.Dispose();
            }

            return new UTF8Encoding().GetString(ostrm.ToArray());
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Handles a validation error.
        /// </summary>
        static void OnValidate(object sender, ValidationEventArgs args)
        {
            Utils.LogError("Error in XML schema validation: {0}", args.Message);
            throw new InvalidOperationException(args.Message, args.Exception);
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// The well known schemas embedded in the assembly.
        /// </summary>
        protected static readonly string[][] WellKnownDictionaries = new string[][]
        {
            new string[] { Namespaces.OpcUaXsd, "Opc.Ua.Schema.Opc.Ua.Types.xsd" }
        };
        private XmlSchema m_schema;
        private XmlSchemaSet m_schemaSet;
        #endregion
    }
}
