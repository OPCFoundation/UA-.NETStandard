/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging;
using Opc.Ua.Schema;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// A class that holds the configuration for a UA service.
    /// </summary>
    public sealed class DataDictionary
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        internal DataDictionary()
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            DataTypes = [];
            m_validator = null;
            TypeSystemId = null;
            TypeSystemName = null;
            DictionaryId = null;
            Name = null;
        }

        /// <summary>
        /// The node id for the dictionary.
        /// </summary>
        public NodeId DictionaryId { get; internal set; }

        /// <summary>
        /// The display name for the dictionary.
        /// </summary>
        public string Name { get; internal set; }

        /// <summary>
        /// The node id for the type system.
        /// </summary>
        public NodeId TypeSystemId { get; internal set; }

        /// <summary>
        /// The display name for the type system.
        /// </summary>
        public string TypeSystemName { get; internal set; }

        /// <summary>
        /// The type dictionary.
        /// </summary>
        public Schema.Binary.TypeDictionary TypeDictionary { get; internal set; }

        /// <summary>
        /// The data type dictionary DataTypes
        /// </summary>
        public NodeIdDictionary<QualifiedName> DataTypes { get; internal set; }

        /// <summary>
        /// Returns true if the dictionary contains the data type description.
        /// </summary>
        public bool Contains(NodeId descriptionId)
        {
            return DataTypes.ContainsKey(descriptionId);
        }

        /// <summary>
        /// Returns the schema for the specified type (returns the entire dictionary if null).
        /// </summary>
        public string GetSchema(NodeId descriptionId)
        {
            if (descriptionId != null)
            {
                if (!DataTypes.TryGetValue(descriptionId, out QualifiedName browseName))
                {
                    return null;
                }

                return m_validator.GetSchema(browseName.Name);
            }

            return m_validator.GetSchema(null);
        }

        /// <summary>
        /// Validates the type dictionary.
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="dictionary">The encoded dictionary to validate.</param>
        /// <param name="throwOnError">Throw if an error occurred.</param>
        internal void Validate(ILogger logger, byte[] dictionary, bool throwOnError)
        {
            Validate(logger, dictionary, null, throwOnError);
        }

        /// <summary>
        /// Validates the type dictionary.
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="dictionary">The encoded dictionary to validate.</param>
        /// <param name="imports">A table of imported namespace schemas.</param>
        /// <param name="throwOnError">Throw if an error occurred.</param>
        internal void Validate(
            ILogger logger,
            byte[] dictionary,
            IDictionary<string, byte[]> imports = null,
            bool throwOnError = false)
        {
            var istrm = new MemoryStream(dictionary);

            if (TypeSystemId == Objects.XmlSchema_TypeSystem)
            {
                var validator = new Schema.Xml.XmlSchemaValidator(imports);

                try
                {
                    validator.Validate(istrm);
                }
                catch (Exception e) when (!throwOnError)
                {
                    logger.LogWarning(e, "Could not validate XML schema, error is ignored.");
                }

                m_validator = validator;
            }

            if (TypeSystemId == Objects.OPCBinarySchema_TypeSystem)
            {
                var validator = new Schema.Binary.BinarySchemaValidator(imports);
                try
                {
                    validator.Validate(istrm);
                }
                catch (Exception e) when (!throwOnError)
                {
                    logger.LogWarning(e, "Could not validate binary schema, error is ignored.");
                }

                m_validator = validator;
                TypeDictionary = validator.Dictionary;
            }
        }

        private SchemaValidator m_validator;
    }
}
