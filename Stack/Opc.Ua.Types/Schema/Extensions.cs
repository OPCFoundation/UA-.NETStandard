/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Xml;
using Opc.Ua.Schema.Model;
using Opc.Ua.Types;

namespace Opc.Ua.Schema
{
    internal static class Extensions
    {
        /// <summary>
        /// Safe version for assignment of InnerXml.
        /// </summary>
        /// <param name="doc">The XmlDocument.</param>
        /// <param name="xml">The Xml document string.</param>
        internal static void LoadInnerXml(this XmlDocument doc, string xml)
        {
            using var sreader = new StringReader(xml);
            using var reader = XmlReader.Create(sreader, CoreUtils.DefaultXmlReaderSettings());
            doc.XmlResolver = null;
            doc.Load(reader);
        }

        /// <summary>
        /// Get basic data type from data type design.
        /// </summary>
        /// <param name="dataType"></param>
        /// <returns></returns>
        public static BasicDataType DetermineBasicDataType(this DataTypeDesign dataType)
        {
            if (dataType == null)
            {
                return BasicDataType.BaseDataType;
            }

            if (dataType.IsOptionSet)
            {
                return BasicDataType.Enumeration;
            }

            // check if it is a built in data type.
            if (dataType.SymbolicName.Namespace == Namespaces.OpcUa)
            {
#if NET8_0_OR_GREATER
                foreach (string name in Enum.GetNames<BasicDataType>())
#else
                foreach (string name in Enum.GetNames(typeof(BasicDataType)))
#endif
                {
                    if (name == dataType.SymbolicName.Name)
                    {
#if NET8_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
                        return Enum.Parse<BasicDataType>(dataType.SymbolicName.Name);
#else
                        return (BasicDataType)Enum.Parse(typeof(BasicDataType), dataType.SymbolicName.Name);
#endif
                    }
                }
            }

            // recursively search hierarchy if conversion to enum fails.
            BasicDataType basicType = DetermineBasicDataType(dataType.BaseTypeNode as DataTypeDesign);

            // data type is user defined if a sub-type of structure.
            if (basicType == BasicDataType.Structure)
            {
                return BasicDataType.UserDefined;
            }

            return basicType;
        }
    }
}
