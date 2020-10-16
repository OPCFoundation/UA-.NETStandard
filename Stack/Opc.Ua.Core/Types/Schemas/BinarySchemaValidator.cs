/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace Opc.Ua.Schema.Binary
{
    /// <summary>
    /// Generates files used to describe data types.
    /// </summary>
    public class BinarySchemaValidator : SchemaValidator
    {
        #region Constructors
        /// <summary>
        /// Intializes the object with default values.
        /// </summary>
        public BinarySchemaValidator()
        {
            SetResourcePaths(WellKnownDictionaries);
        }

        /// <summary>
        /// Intializes the object with a file table.
        /// </summary>
        public BinarySchemaValidator(Dictionary<string, string> fileTable) : base(fileTable)
        {
            SetResourcePaths(WellKnownDictionaries);
        }
        #endregion

        #region Public Members
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
        public async Task Validate(Stream stream)
        {
            // read and parse the file.
            Dictionary = (TypeDictionary)LoadInput(typeof(TypeDictionary), stream);
            await Validate();
        }

        /// <summary>
        /// Generates the code from the contents of the address space.
        /// </summary>
        public async Task Validate(string inputPath)
        {
            // read and parse the file.
            Dictionary = (TypeDictionary)LoadInput(typeof(TypeDictionary), inputPath);
            await Validate();
        }

        /// <summary>
        /// Returns the schema for the specified type (returns the entire schema if null).
        /// </summary>
        public override string GetSchema(string typeName)
        {
            XmlWriterSettings settings = new XmlWriterSettings();

            settings.Encoding = Encoding.UTF8;
            settings.Indent = true;
            settings.IndentChars = "    ";

            MemoryStream ostrm = new MemoryStream();
            XmlWriter writer = XmlWriter.Create(ostrm, settings);

            try
            {
                if (typeName == null)
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(TypeDictionary));
                    serializer.Serialize(writer, Dictionary);
                }
                else
                {
                    TypeDescription description = null;

                    if (!m_descriptions.TryGetValue(new XmlQualifiedName(typeName, Dictionary.TargetNamespace), out description))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(TypeDictionary));
                        serializer.Serialize(writer, Dictionary);
                    }
                    else
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(TypeDescription));
                        serializer.Serialize(writer, description);
                    }
                }
            }
            finally
            {
                writer.Flush();
                writer.Dispose();
            }

            return new UTF8Encoding().GetString(ostrm.ToArray(), 0, (int)ostrm.Length);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Generates the code from the contents of the address space.
        /// </summary>
        private async Task Validate()
        {
            m_descriptions = new Dictionary<XmlQualifiedName, TypeDescription>();
            m_validatedDescriptions = new List<TypeDescription>();
            m_warnings = new List<string>();

            // import types from referenced dictionaries.
            if (Dictionary.Import != null)
            {
                foreach (ImportDirective directive in Dictionary.Import)
                {
                    await Import(directive);
                }
            }
            else
            {
                // always import builtin types, unless wellknown library
                if (!WellKnownDictionaries.Any(n => String.Equals(n[0], Dictionary.TargetNamespace)))
                {
                    ImportDirective directive = new ImportDirective { Namespace = Namespaces.OpcUa };
                    await Import(directive);
                }
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
                    m_warnings.Add(String.Format(CultureInfo.InvariantCulture, "{0} '{1}' validated.", description.GetType().Name, description.Name));
                }
            }
        }

        /// <summary>
        /// Imports a dictionary identified by an import directive.
        /// </summary>
        private async Task Import(ImportDirective directive)
        {
            // check if already loaded.
            if (LoadedFiles.ContainsKey(directive.Namespace))
            {
                return;
            }

            TypeDictionary dictionary = (TypeDictionary)Load(typeof(TypeDictionary), directive.Namespace, directive.Location);

            // verify namespace.
            if (!String.IsNullOrEmpty(dictionary.TargetNamespace) && directive.Namespace != dictionary.TargetNamespace)
            {
                throw Exception("Imported dictionary '{0}' does not match uri specified: '{1}'.", dictionary.TargetNamespace, directive.Namespace);
            }

            // save file.
            LoadedFiles.Add(dictionary.TargetNamespace, dictionary);

            // import nested dictionaries.
            if (dictionary.Import != null)
            {
                for (int ii = 0; ii < dictionary.Import.Length; ii++)
                {
                    await Import(dictionary.Import[ii]);
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
        private static bool IsNull(Documentation documentation)
        {
            if (documentation == null)
            {
                return true;
            }

            if (documentation.Text != null && documentation.Text.Length > 0)
            {
                for (int ii = 0; ii < documentation.Text.Length; ii++)
                {
                    if (!String.IsNullOrEmpty(documentation.Text[ii]))
                    {
                        return false;
                    }
                }
            }

            if (documentation.Items != null && documentation.Items.Length > 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns true if field is an integer type.
        /// </summary>
        private bool IsIntegerType(FieldType field)
        {
            TypeDescription description = null;

            if (!m_descriptions.TryGetValue(field.TypeName, out description))
            {
                return false;
            }

            if (description is EnumeratedType)
            {
                return true;
            }

            OpaqueType opaqueType = description as OpaqueType;

            if (opaqueType != null)
            {
                if (opaqueType.LengthInBitsSpecified)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the length of field in bits (-1 if the length is not fixed).
        /// </summary>
        private int GetFieldLength(FieldType field)
        {
            TypeDescription description = null;

            if (!m_descriptions.TryGetValue(field.TypeName, out description))
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

            EnumeratedType enumerated = description as EnumeratedType;

            if (enumerated != null)
            {
                if (enumerated.LengthInBitsSpecified)
                {
                    return enumerated.LengthInBits * ((int)count);
                }
            }
            else
            {
                OpaqueType opaque = description as OpaqueType;

                if (opaque != null)
                {
                    if (opaque.LengthInBitsSpecified)
                    {
                        return opaque.LengthInBits * ((int)count);
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Checks if a string is a valid part of a qname.
        /// </summary>
        private static bool IsValidName(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                return false;
            }

            if (!Char.IsLetter(name[0]) && name[0] != '_' && name[0] != '"')
            {
                return false;
            }

            for (int ii = 1; ii < name.Length; ii++)
            {
                if (Char.IsLetter(name[ii]) || Char.IsDigit(name[ii]))
                {
                    continue;
                }

                if (name[ii] == '.' || name[ii] == '-' || name[ii] == '_' || name[ii] == '"')
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
                throw Exception("The description name '{0}' already used by another description.", description.Name);
            }

            m_descriptions.Add(description.QName, description);
        }

        /// <summary>
        /// Validates a type description.
        /// </summary>
        private void ValidateDescription(TypeDescription description)
        {
            OpaqueType opaque = description as OpaqueType;

            if (opaque != null)
            {
                if (!opaque.LengthInBitsSpecified)
                {
                    m_warnings.Add(String.Format(CultureInfo.InvariantCulture, "Warning: The opaque type '{0}' does not have a length specified.", description.Name));
                }

                if (IsNull(opaque.Documentation))
                {
                    m_warnings.Add(String.Format(CultureInfo.InvariantCulture, "Warning: The opaque type '{0}' does not have any documentation.", description.Name));
                }
            }

            EnumeratedType enumerated = description as EnumeratedType;

            if (enumerated != null)
            {

                if (!enumerated.LengthInBitsSpecified)
                {
                    throw Exception("The enumerated type '{0}' does not have a length specified.", description.Name);
                }
            }

            StructuredType structure = description as StructuredType;

            if (structure != null)
            {
                if (structure.Field == null || structure.Field.Length == 0)
                {
                    structure.Field = new FieldType[0];
                }

                int bitCount = 0;

                Dictionary<string, FieldType> fields = new Dictionary<string, FieldType>();

                for (int ii = 0; ii < structure.Field.Length; ii++)
                {
                    FieldType field = structure.Field[ii];

                    ValidateField(structure, fields, field);

                    int fieldLength = GetFieldLength(field);

                    if (fieldLength == -1)
                    {
                        if (bitCount % 8 != 0)
                        {
                            throw Exception("Field '{1}' in structured type '{0}' is not aligned on a byte boundary .", description.Name, field.Name);
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
        private void ValidateField(StructuredType description, Dictionary<string, FieldType> fields, FieldType field)
        {
            if (field == null || String.IsNullOrEmpty(field.Name))
            {
                throw Exception("The structured type '{0}' has an unnamed field.", description.Name);
            }

            if (fields.ContainsKey(field.Name))
            {
                throw Exception("The structured type '{0}' has a duplicate field name '{1}'.", description.Name, field.Name);
            }

            if (IsNull(field.TypeName))
            {
                throw Exception("Field '{1}' in structured type '{0}' has no type specified.", description.Name, field.Name);
            }

            if (!m_descriptions.ContainsKey(field.TypeName))
            {
                throw Exception("Field '{1}' in structured type '{0}' has an unrecognized type '{2}'.", description.Name, field.Name, field.TypeName);
            }

            if (!String.IsNullOrEmpty(field.LengthField))
            {
                if (!fields.ContainsKey(field.LengthField))
                {
                    throw Exception("Field '{1}' in structured type '{0}' references an unknownn length field '{2}'.", description.Name, field.Name, field.LengthField);
                }

                if (!IsIntegerType(fields[field.LengthField]))
                {
                    throw Exception("Field '{1}' in structured type '{0}' references a length field '{2}' which is not an integer value.", description.Name, field.Name, field.SwitchField);
                }
            }

            if (!String.IsNullOrEmpty(field.SwitchField))
            {
                if (!fields.ContainsKey(field.SwitchField))
                {
                    throw Exception("Field '{1}' in structured type '{0}' references an unknownn switch field '{2}'.", description.Name, field.Name, field.SwitchField);
                }

                if (!IsIntegerType(fields[field.SwitchField]))
                {
                    throw Exception("Field '{1}' in structured type '{0}' references a switch field '{2}' which is not an integer value.", description.Name, field.Name, field.SwitchField);
                }
            }
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// Well known embedded binary schemas.
        /// </summary>
        protected readonly static string[][] WellKnownDictionaries = new string[][]
        {
            new string[] { Namespaces.OpcBinarySchema,   "Opc.Ua.Types.Schemas.StandardTypes.bsd" },
            new string[] { Namespaces.OpcUaBuiltInTypes, "Opc.Ua.Types.Schemas.BuiltInTypes.bsd"  },
            new string[] { Namespaces.OpcUa, "Opc.Ua.Schema.Opc.Ua.Types.bsd"  }
        };

        private Dictionary<XmlQualifiedName, TypeDescription> m_descriptions;
        private List<TypeDescription> m_validatedDescriptions;
        private List<string> m_warnings;
        #endregion
    }
}
