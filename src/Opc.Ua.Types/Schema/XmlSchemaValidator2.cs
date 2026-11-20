// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Schema;

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
                [Ua.Types.Namespaces.OpcUaBuiltInTypes] = "BuiltInTypes.xsd",
                [Ua.Types.Namespaces.OpcUaXsd] = "Opc.Ua.Types.xsd",
                [Ua.Types.Namespaces.OpcUa] = "Opc.Ua.Types.xsd"
            };

        /// <summary>
        /// Intializes the object with a file table.
        /// </summary>
        public XmlSchemaValidator2(
            IFileSystem fileSystem,
            Dictionary<string, string>? fileTable = null)
            : base(fileSystem, fileTable, null)
        {
            AddWellKnownFiles(WellKnown);
        }

        /// <summary>
        /// The schema set that was validated.
        /// </summary>
        public XmlSchemaSet? SchemaSet { get; private set; }

        /// <summary>
        /// The schema that was validated.
        /// </summary>
        public XmlSchema? TargetSchema { get; private set; }

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

            // Includes carries xs:include and xs:redefine as well, which are not
            // XmlSchemaImport - casting every entry threw InvalidCastException.
            foreach (XmlSchemaImport import in TargetSchema.Includes.OfType<XmlSchemaImport>())
            {
                import.Schema = Load(import.SchemaLocation, import.Namespace, handler);
            }

            // As in XmlSchemaValidator: includes and redefines are not resolved,
            // on every target framework.
            SchemaSet = new XmlSchemaSet { XmlResolver = null };
            SchemaSet.Add(TargetSchema);
            SchemaSet.Compile();
        }

        /// <summary>
        /// Returns the schema for the specified type (returns the entire schema if null).
        /// </summary>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification = "XmlSchema.Write is used with schema objects constructed at design time.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification =
                "XmlSchema.Write is used with schema objects constructed at design time.")]
        public override string GetSchema(string? typeName)
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
                if (typeName == null || TargetSchema!.Elements.Values.Count == 0)
                {
                    TargetSchema!.Write(writer);
                }
                else
                {
                    foreach (XmlSchemaObject current in TargetSchema.Elements.Values)
                    {
                        if (current is XmlSchemaElement element && element.Name == typeName)
                        {
                            var schema = new XmlSchema();
                            schema.Items.Add(element.ElementSchemaType!);
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
        private static void OnValidate(object? sender, ValidationEventArgs args)
        {
            throw new InvalidOperationException(args.Message, args.Exception);
        }
    }
}
