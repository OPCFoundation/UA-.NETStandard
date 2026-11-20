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
            IFileSystem? fileSystem = null,
            IDictionary<string, string>? knownFiles = null)
            : base(fileSystem, knownFiles, null)
        {
        }

        /// <summary>
        /// Intializes the object with a import table.
        /// </summary>
        public XmlSchemaValidator(IReadOnlyDictionary<string, byte[]> importTable)
            : base(NullFileSystem.Instance, null, importTable)
        {
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

            // Imports are resolved above from the import table only. Includes and
            // redefines are not resolved: on .NET Framework the default resolver
            // of a schema set would open any location the schema names.
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
            XmlWriterSettings settings = CoreUtils.DefaultXmlWriterSettings();

            var ostrm = new MemoryStream();
            var writer = XmlWriter.Create(ostrm, settings);

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
            logger.SchemaValidationError(args.Message);
            throw new InvalidOperationException(args.Message, args.Exception);
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="XmlSchemaValidator"/>.
    /// </summary>
    internal static partial class XmlSchemaValidatorLog
    {
        [LoggerMessage(EventId = TypesEventIds.XmlSchemaValidator + 0, Level = LogLevel.Error,
            Message = "Error in XML schema validation: {Message}")]
        public static partial void SchemaValidationError(this ILogger logger, string message);
    }
}
