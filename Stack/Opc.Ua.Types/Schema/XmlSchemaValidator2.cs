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

using System.Text;
using System.Xml;
using System.Reflection;
using System.Xml.Schema;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;

namespace Opc.Ua.Schema.Xml
{
    /// <summary>
    /// Generates files used to describe data types.
    /// </summary>
    public class XmlSchemaValidator2 : SchemaValidator
    {
        /// <summary>
        /// Intializes the object with default values.
        /// </summary>
        public XmlSchemaValidator2(IFileSystem fileSystem)
        {
            m_fileSystem = fileSystem ?? throw new ArgumentException(null, nameof(fileSystem));
            SetResourcePaths(s_wellKnownDictionaries);
        }

        /// <summary>
        /// Intializes the object with a file table.
        /// </summary>
        public XmlSchemaValidator2(IFileSystem fileSystem, Dictionary<string, string> fileTable)
            : base(fileTable)
        {
            m_fileSystem = fileSystem ?? throw new ArgumentException(null, nameof(fileSystem));
            SetResourcePaths(s_wellKnownDictionaries);
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
            using Stream istrm = m_fileSystem.OpenRead(inputPath);
            Validate(istrm);
        }

        /// <summary>
        /// Generates the code from the contents of the address space.
        /// </summary>
        public void Validate(Stream stream)
        {
            using var xmlReader = XmlReader.Create(stream, CoreUtils.DefaultXmlReaderSettings());
            TargetSchema = XmlSchema.Read(xmlReader, new ValidationEventHandler(OnValidate));

            foreach (XmlSchemaImport import in TargetSchema.Includes.Cast<XmlSchemaImport>())
            {
                if (import.Namespace == Namespaces.OpcUa)
                {
                    var strm = new StreamReader(Assembly
                        .GetExecutingAssembly()
                        .GetManifestResourceStream("Opc.Ua.Schema.Opc.Ua.Types.xsd"));
                    using var xmlReader2 = XmlReader.Create(strm, CoreUtils.DefaultXmlReaderSettings());
                    import.Schema = XmlSchema.Read(xmlReader2, new ValidationEventHandler(OnValidate));
                    continue;
                }

                if (!KnownFiles.TryGetValue(import.Namespace, out string location))
                {
                    location = import.SchemaLocation;
                }

                if (!m_fileSystem.Exists(location))
                {
                    var strm = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(location));
                    using var xmlReader2 = XmlReader.Create(strm, CoreUtils.DefaultXmlReaderSettings());
                    import.Schema = XmlSchema.Read(xmlReader2, new ValidationEventHandler(OnValidate));
                }
                else
                {
                    Stream strm = m_fileSystem.OpenRead(location);
                    using var xmlReader2 = XmlReader.Create(strm, CoreUtils.DefaultXmlReaderSettings());
                    import.Schema = XmlSchema.Read(xmlReader2, new ValidationEventHandler(OnValidate));
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

            return new UTF8Encoding().GetString(ostrm.ToArray());
        }

        /// <summary>
        /// Handles a valdiation error.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static void OnValidate(object sender, ValidationEventArgs args)
        {
            throw new InvalidOperationException(args.Message, args.Exception);
        }

        private static readonly string[][] s_wellKnownDictionaries =
        [
            [Namespaces.OpcUaBuiltInTypes, "Opc.Ua.Schema.BuiltInTypes.xsd"]
        ];

        private readonly IFileSystem m_fileSystem;
    }
}
