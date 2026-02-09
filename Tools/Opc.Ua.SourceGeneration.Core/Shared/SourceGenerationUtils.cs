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
using System.Text;
using System.Xml;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Common utils for all generators
    /// </summary>
    internal static class SourceGenerationUtils
    {
        /// <summary>
        /// Ensures the first character is lower case.
        /// </summary>
        public static string ToLowerCamelCase(this string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            if (char.IsLower(name[0]))
            {
                return name;
            }

            return CoreUtils.Format("{0}{1}", char.ToLowerInvariant(name[0]), name[1..]);
        }

        /// <summary>
        /// Convert string to a safe symbol for dotnet use
        /// </summary>
        /// <returns></returns>
        public static string ToSafeSymbolName(
            this string name,
            bool toLowerCamelCase = false,
            string prefix = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }
            var buffer = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }
                if (toLowerCamelCase)
                {
                    buffer.Append(char.ToLowerInvariant(c));
                    toLowerCamelCase = false;
                    continue;
                }
                buffer.Append(c);
            }
            string symbol = buffer.ToString();
            if (!string.IsNullOrEmpty(prefix))
            {
                return prefix + symbol;
            }
            switch (symbol)
            {
                case "event":
                case "params":
                case "object":
                case "class":
                case "struct":
                case "record":
                case "void":
                case "private":
                case "protected":
                case "public":
                case "internal":
                case "static":
                case "readonly":
                case "const":
                case "null":
                case "sealed":
                case "override":
                case "virtual":
                case "interface":
                case "enum":
                case "namespace":
                case "using":
                case "new":
                case "this":
                case "base":
                case "if":
                case "else":
                case "for":
                case "foreach":
                case "while":
                case "do":
                case "switch":
                case "case":
                case "default":
                case "break":
                case "continue":
                case "return":
                case "try":
                case "catch":
                case "finally":
                case "throw":
                case "in":
                case "ref":
                case "out":
                case "set":
                case "get":
                case "value":
                case "var":
                case "dynamic":
                case "async":
                case "await":
                case "string":
                case "byte":
                case "sbyte":
                case "char":
                case "bool":
                case "short":
                case "ushort":
                case "uint":
                case "ulong":
                case "int":
                case "long":
                case "float":
                case "double":
                case "decimal":
                    return "@" + symbol;
            }
            return symbol;
        }

        /// <summary>
        /// Wrap the string as a string literal for generated code.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string AsStringLiteral(this string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "string.Empty";
            }
            value = value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\t", "\\t", StringComparison.Ordinal);
            return $"\"{value}\"";
        }

        /// <summary>
        /// Checks for a null qualified name.
        /// </summary>
        public static bool IsNull(this XmlQualifiedName qname)
        {
            if (qname == null)
            {
                return true;
            }

            if (string.IsNullOrEmpty(qname.Name))
            {
                return true;
            }

            return false;
        }
    }
}
