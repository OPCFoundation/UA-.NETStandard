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

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Opc.Ua.Schema.Types;

namespace Opc.Ua.Schema.Binary
{
    /// <summary>
    /// Generates files used to describe data types.
    /// </summary>
    public class BinarySchemaValidator : SchemaValidator
    {
        /// <summary>
        /// Intializes the object with a file table.
        /// </summary>
        public BinarySchemaValidator(
            IFileSystem fileSystem = null,
            IDictionary<string, string> knownFiles = null)
            : base(fileSystem, knownFiles, StandardTypeImports)
        {
        }

        /// <summary>
        /// Intializes the object with a import table.
        /// </summary>
        public BinarySchemaValidator(IReadOnlyDictionary<string, byte[]> importTable)
            : base(null, null, AndStandardTypeImports(importTable))
        {
        }

        /// <summary>
        /// The dictionary that was validated.
        /// </summary>
        public TypeDictionary Dictionary { get; private set; }

        /// <summary>
        /// The types defined in the dictionary.
        /// </summary>
        public IList<TypeDescription> ValidatedDescriptions => m_validatedDescriptions;

        /// <summary>
        /// Any warnings during validation.
        /// </summary>
        public ICollection<string> Warnings => m_warnings;

        /// <summary>
        /// Generates the code from the contents of the address space.
        /// </summary>
        public void Validate(Stream stream)
        {
            // read and parse the file.
            Dictionary = LoadInput<TypeDictionary>(stream);
            Validate();
        }

        /// <summary>
        /// Generates the code from the contents of the address space.
        /// </summary>
        public void Validate(string inputPath)
        {
            // read and parse the file.
            Dictionary = LoadInput<TypeDictionary>(inputPath);
            Validate();
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
                if (typeName == null)
                {
                    var serializer = new XmlSerializer(typeof(TypeDictionary));
                    serializer.Serialize(writer, Dictionary);
                }
                else if (!m_descriptions.TryGetValue(
                        new XmlQualifiedName(typeName, Dictionary.TargetNamespace),
                        out TypeDescription description))
                {
                    var serializer = new XmlSerializer(typeof(TypeDictionary));
                    serializer.Serialize(writer, Dictionary);
                }
                else
                {
                    var serializer = new XmlSerializer(typeof(TypeDescription));
                    serializer.Serialize(writer, description);
                }
            }
            finally
            {
                writer.Flush();
                writer.Dispose();
            }

            return Encoding.UTF8.GetString(ostrm.ToArray(), 0, (int)ostrm.Length);
        }

        /// <summary>
        /// Generates the code from the contents of the address space.
        /// </summary>
        private void Validate()
        {
            m_descriptions = [];
            m_validatedDescriptions = [];
            m_warnings = [];

            // import types from referenced dictionaries.
            if (Dictionary.Import != null)
            {
                foreach (ImportDirective directive in Dictionary.Import)
                {
                    Import(directive.Location, directive.Namespace);
                }
            }
            else if (Dictionary.TargetNamespace != Namespaces.OpcUa)
            {
                // Import built-in types if no imports are specified and not built in.
                Import(null, Namespaces.OpcUa);
            }

            // import types from imported dictionaries.
            foreach (TypeDescription description in m_descriptions.Values)
            {
                ValidateDescription(description);
            }

            // import types from target dictionary.
            if (Dictionary.Items != null)
            {
                foreach (TypeDescription description in Dictionary.Items)
                {
                    ImportDescription(description, Dictionary.TargetNamespace);
                    m_validatedDescriptions.Add(description);
                }

                // validate types from target dictionary.
                foreach (TypeDescription description in m_validatedDescriptions)
                {
                    ValidateDescription(description);
                    m_warnings.Add(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} '{1}' validated.",
                            description.GetType().Name,
                            description.Name));
                }
            }
        }

        /// <summary>
        /// Imports a dictionary identified by an import directive.
        /// </summary>
        private void Import(string location, string namespaceUri)
        {
            // check if already loaded.
            if (LoadedFiles.ContainsKey(namespaceUri))
            {
                return;
            }

            TypeDictionary dictionary = Load<TypeDictionary>(location, namespaceUri);

            // verify namespace.
            if (!string.IsNullOrEmpty(dictionary.TargetNamespace) &&
                namespaceUri != dictionary.TargetNamespace)
            {
                throw Exception(
                    "Imported dictionary '{0}' does not match uri specified: '{1}'.",
                    dictionary.TargetNamespace,
                    namespaceUri);
            }

            // save file.
            LoadedFiles.Add(dictionary.TargetNamespace, dictionary);

            // import nested dictionaries.
            if (dictionary.Import != null)
            {
                for (int ii = 0; ii < dictionary.Import.Length; ii++)
                {
                    ImportDirective directive = dictionary.Import[ii];
                    Import(directive.Location, directive.Namespace);
                }
            }

            // import types.
            if (dictionary.Items != null)
            {
                foreach (TypeDescription description in dictionary.Items)
                {
                    ImportDescription(description, dictionary.TargetNamespace);
                }
            }
        }

        /// <summary>
        /// Returns true if the documentation element is empty.
        /// </summary>
        private static bool IsNull([NotNullWhen(false)] Documentation documentation)
        {
            if (documentation == null)
            {
                return true;
            }

            if (documentation.Text != null && documentation.Text.Length > 0)
            {
                for (int ii = 0; ii < documentation.Text.Length; ii++)
                {
                    if (!string.IsNullOrEmpty(documentation.Text[ii]))
                    {
                        return false;
                    }
                }
            }

            return documentation.Items == null || documentation.Items.Length == 0;
        }

        /// <summary>
        /// Returns true if field is an integer type.
        /// </summary>
        private bool IsIntegerType(FieldType field)
        {
            if (!m_descriptions.TryGetValue(field.TypeName, out TypeDescription description))
            {
                return false;
            }

            if (description is EnumeratedType)
            {
                return true;
            }

            if (description is OpaqueType opaqueType && opaqueType.LengthInBitsSpecified)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the length of field in bits (-1 if the length is not fixed).
        /// </summary>
        private int GetFieldLength(FieldType field)
        {
            if (!m_descriptions.TryGetValue(field.TypeName, out TypeDescription description))
            {
                return -1;
            }

            uint count = 1;

            if (field.LengthSpecified)
            {
                count = field.Length;

                if (field.IsLengthInBytes)
                {
                    count *= 8;
                }
            }

            if (description is EnumeratedType enumerated)
            {
                if (enumerated.LengthInBitsSpecified)
                {
                    return enumerated.LengthInBits * ((int)count);
                }
            }
            else if (description is OpaqueType opaque && opaque.LengthInBitsSpecified)
            {
                return opaque.LengthInBits * ((int)count);
            }

            return -1;
        }

        /// <summary>
        /// Checks if a string is a valid part of a qname.
        /// </summary>
        private static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            if (!char.IsLetter(name[0]) && name[0] != '_' && name[0] != '"')
            {
                return false;
            }

            bool insideParentheses = name[0] == '"';
            for (int ii = 1; ii < name.Length; ii++)
            {
                if (char.IsLetter(name[ii]) || char.IsDigit(name[ii]))
                {
                    continue;
                }

                if (name[ii] == '"')
                {
                    insideParentheses = !insideParentheses;
                    continue;
                }

                if (name[ii] is '.' or '-' or '_')
                {
                    continue;
                }

                if (name[ii] == ' ' && insideParentheses)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Imports a type description.
        /// </summary>
        private void ImportDescription(TypeDescription description, string targetNamespace)
        {
            if (description == null)
            {
                return;
            }

            if (!IsValidName(description.Name))
            {
                throw Exception("'{0}' is not a valid qualified name.", description.Name);
            }

            description.QName = new XmlQualifiedName(description.Name, targetNamespace);

            if (m_descriptions.ContainsKey(description.QName))
            {
                throw Exception(
                    "The description name '{0}' already used by another description.",
                    description.Name);
            }

            m_descriptions.Add(description.QName, description);
        }

        /// <summary>
        /// Validates a type description.
        /// </summary>
        private void ValidateDescription(TypeDescription description)
        {
            if (description is OpaqueType opaque)
            {
                if (!opaque.LengthInBitsSpecified)
                {
                    m_warnings.Add(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Warning: The opaque type '{0}' does not have a length specified.",
                            description.Name));
                }

                if (IsNull(opaque.Documentation))
                {
                    m_warnings.Add(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Warning: The opaque type '{0}' does not have any documentation.",
                            description.Name));
                }
            }

            if (description is EnumeratedType enumerated && !enumerated.LengthInBitsSpecified)
            {
                throw Exception(
                    "The enumerated type '{0}' does not have a length specified.",
                    description.Name);
            }

            if (description is StructuredType structure)
            {
                if (structure.Field == null || structure.Field.Length == 0)
                {
                    structure.Field = [];
                }

                int bitCount = 0;

                var fields = new Dictionary<string, FieldType>();

                for (int ii = 0; ii < structure.Field.Length; ii++)
                {
                    FieldType field = structure.Field[ii];

                    ValidateField(structure, fields, field);

                    int fieldLength = GetFieldLength(field);

                    if (fieldLength == -1)
                    {
                        if (bitCount % 8 != 0)
                        {
                            throw Exception(
                                "Field '{1}' in structured type '{0}' is not aligned on a byte boundary.",
                                description.Name,
                                field.Name);
                        }

                        bitCount = 0;
                    }
                    else
                    {
                        bitCount += fieldLength;
                    }

                    fields.Add(field.Name, field);
                }
            }
        }

        /// <summary>
        /// Validates a field in a structured type description.
        /// </summary>
        private void ValidateField(
            StructuredType description,
            Dictionary<string, FieldType> fields,
            FieldType field)
        {
            if (field == null || string.IsNullOrEmpty(field.Name))
            {
                throw Exception(
                    "The structured type '{0}' has an unnamed field.",
                    description.Name);
            }

            if (fields.ContainsKey(field.Name))
            {
                throw Exception(
                    "The structured type '{0}' has a duplicate field name '{1}'.",
                    description.Name,
                    field.Name);
            }

            if (IsNull(field.TypeName))
            {
                throw Exception(
                    "Field '{0}' in structured type '{1}' has no type specified.",
                    field.Name,
                    description.Name);
            }

            if (!m_descriptions.ContainsKey(field.TypeName))
            {
                throw Exception(
                    "Field '{0}' in structured type '{1}' has an unrecognized type '{2}'.",
                    field.Name,
                    description.Name,
                    field.TypeName);
            }

            if (!string.IsNullOrEmpty(field.LengthField))
            {
                if (!fields.TryGetValue(field.LengthField, out FieldType value))
                {
                    throw Exception(
                        "Field '{0}' in structured type '{1}' references an unknownn length field '{2}'.",
                        field.Name,
                        description.Name,
                        field.LengthField);
                }

                if (!IsIntegerType(value))
                {
                    throw Exception(
                        "Field '{0}' in structured type '{1}' references a length field '{2}' which is not an integer value.",
                        field.Name,
                        description.Name,
                        field.SwitchField);
                }
            }

            if (!string.IsNullOrEmpty(field.SwitchField))
            {
                if (!fields.TryGetValue(field.SwitchField, out FieldType value))
                {
                    throw Exception(
                        "Field '{0}' in structured type '{1}' references an unknownn switch field '{2}'.",
                        field.Name,
                        description.Name,
                        field.SwitchField);
                }

                if (!IsIntegerType(value))
                {
                    throw Exception(
                        "Field '{0}' in structured type '{1}' references a switch field '{2}' which is not an integer value.",
                        field.Name,
                        description.Name,
                        field.SwitchField);
                }
            }
        }

        /// <summary>
        /// Adds the standard type imports to the externally provided import table.
        /// </summary>
        /// <param name="importTable"></param>
        /// <returns></returns>
        private static IReadOnlyDictionary<string, byte[]> AndStandardTypeImports(
            IReadOnlyDictionary<string, byte[]> importTable)
        {
            if (importTable == null)
            {
                return StandardTypeImports;
            }
            var clone = importTable.ToDictionary(k => k.Key, k => k.Value);
            foreach (KeyValuePair<string, byte[]> kv in StandardTypeImports)
            {
                clone.TryAdd(kv.Key, kv.Value);
            }
            return clone;
        }

        /// <summary>
        /// Get the built-in types bsd as an import table. Since this never changes
        /// it will be more stable than using the named file in the source generator.
        /// </summary>
        private static IReadOnlyDictionary<string, byte[]> StandardTypeImports
        {
            get
            {
                if (field == null)
                {
                    var dictionary = new Dictionary<string, byte[]>();
                    Assembly resourceAssembly = typeof(TypeDictionaryValidator).Assembly;
                    using (Stream stream = resourceAssembly.GetManifestResourceStream(
                        "Opc.Ua.Schema.BuiltInTypes.bsd"))
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        dictionary[Namespaces.OpcUaBuiltInTypes] = ms.ToArray();
                    }
                    using (Stream stream = resourceAssembly.GetManifestResourceStream(
                      "Opc.Ua.Schema.StandardTypes.bsd"))
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        dictionary[Namespaces.OpcBinarySchema] = ms.ToArray();
                    }
                    field = dictionary;
                }
                return field;
            }
        }

        private Dictionary<XmlQualifiedName, TypeDescription> m_descriptions;
        private List<TypeDescription> m_validatedDescriptions;
        private List<string> m_warnings;
    }
}
