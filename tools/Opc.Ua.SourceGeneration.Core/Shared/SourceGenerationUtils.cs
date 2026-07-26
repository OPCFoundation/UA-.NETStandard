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
using System.Collections.Generic;
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
        /// Ensures the first character is upper case.
        /// </summary>
        public static string ToUpperCamelCase(this string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            if (char.IsUpper(name[0]))
            {
                return name;
            }

            return CoreUtils.Format("{0}{1}", char.ToUpperInvariant(name[0]), name[1..]);
        }

        /// <summary>
        /// Converts an authored name to a valid C# identifier.
        /// </summary>
        public static string ToCSharpIdentifier(
            this string name,
            bool upperCamelCase = false)
        {
            string source = name?.TrimStart('@');
            if (string.IsNullOrEmpty(source))
            {
                return upperCamelCase ? "Value" : "value";
            }

            var buffer = new StringBuilder(source.Length);
            bool applyCasing = true;
            foreach (char character in source)
            {
                if (char.IsWhiteSpace(character))
                {
                    continue;
                }

                char identifierCharacter =
                    char.IsLetterOrDigit(character) || character == '_' ?
                        character :
                        '_';
                if (buffer.Length == 0 && char.IsDigit(identifierCharacter))
                {
                    buffer.Append('_');
                }
                if (applyCasing && char.IsLetter(identifierCharacter))
                {
                    identifierCharacter = upperCamelCase ?
                        char.ToUpperInvariant(identifierCharacter) :
                        char.ToLowerInvariant(identifierCharacter);
                    applyCasing = false;
                }
                buffer.Append(identifierCharacter);
            }

            if (buffer.Length == 0)
            {
                return upperCamelCase ? "Value" : "value";
            }

            string identifier = buffer.ToString();
            return s_csharpKeywords.Contains(identifier) ? "@" + identifier : identifier;
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
                .Replace("\t", "\\t", StringComparison.Ordinal)
                .Replace("\u0085", "\\u0085", StringComparison.Ordinal)
                .Replace("\u2028", "\\u2028", StringComparison.Ordinal)
                .Replace("\u2029", "\\u2029", StringComparison.Ordinal);
            return $"\"{value}\"";
        }

        /// <summary>
        /// Escape string
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static string Escape(this string value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
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

        private static readonly HashSet<string> s_csharpKeywords =
        [
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch",
            "char", "checked", "class", "const", "continue", "decimal", "default",
            "delegate", "do", "double", "else", "enum", "event", "explicit",
            "extern", "false", "finally", "fixed", "float", "for", "foreach",
            "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
            "lock", "long", "namespace", "new", "null", "object", "operator",
            "out", "override", "params", "private", "protected", "public",
            "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
            "stackalloc", "static", "string", "struct", "switch", "this",
            "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
            "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
            "__arglist", "__makeref", "__reftype", "__refvalue"
        ];
    }
}
