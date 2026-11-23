// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
            return ToCSharpIdentifierCore(name, upperCamelCase);
        }

        /// <summary>
        /// Converts an authored name to a valid C# identifier but leaves the casing
        /// of the name untouched. Used where the identifier is part of the public
        /// surface of generated code and must keep the name the model author wrote.
        /// </summary>
        public static string ToCSharpIdentifierPreserveCase(this string name)
        {
            return ToCSharpIdentifierCore(name, null);
        }

        private static string ToCSharpIdentifierCore(string name, bool? upperCamelCase)
        {
            string? source = name?.TrimStart('@');
            if (string.IsNullOrEmpty(source))
            {
                return upperCamelCase == false ? "value" : "Value";
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
                if (applyCasing && upperCamelCase.HasValue && char.IsLetter(identifierCharacter))
                {
                    identifierCharacter = upperCamelCase.Value ?
                        char.ToUpperInvariant(identifierCharacter) :
                        char.ToLowerInvariant(identifierCharacter);
                    applyCasing = false;
                }
                buffer.Append(identifierCharacter);
            }

            if (buffer.Length == 0)
            {
                return upperCamelCase == false ? "value" : "Value";
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
            string? prefix = null)
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
        /// Returns whether <paramref name="c"/> ends a line in C# source: CR,
        /// LF, NEL (U+0085), LINE SEPARATOR (U+2028) or PARAGRAPH SEPARATOR
        /// (U+2029).
        /// </summary>
        public static bool IsCSharpLineTerminator(char c)
        {
            return c is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029';
        }

        /// <summary>
        /// Makes model text safe to place on a single comment line of
        /// generated code: every C# line terminator is replaced with a space,
        /// so the text cannot end the comment and continue as code.
        /// </summary>
        public static string AsSingleLineCommentText(this string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }
            StringBuilder? builder = null;
            for (int ii = 0; ii < value!.Length; ii++)
            {
                if (IsCSharpLineTerminator(value[ii]))
                {
                    builder ??= new StringBuilder(value);
                    builder[ii] = ' ';
                }
            }
            return builder?.ToString() ?? value;
        }

        /// <summary>
        /// Returns whether <paramref name="name"/> can be emitted as a single
        /// C# identifier: a letter or underscore followed by letters, digits
        /// or underscores. Model names (SymbolicName, SymbolicId, ClassName,
        /// namespace Name) are emitted verbatim in identifier positions, so
        /// anything else would either break the generated code or inject
        /// arbitrary code into it.
        /// </summary>
        public static bool IsCodeSafeIdentifier([NotNullWhen(true)] this string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            if (!char.IsLetter(name![0]) && name[0] != '_')
            {
                return false;
            }
            for (int ii = 1; ii < name.Length; ii++)
            {
                char c = name[ii];
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Returns whether <paramref name="name"/> is a dotted sequence of
        /// identifiers (see <see cref="IsCodeSafeIdentifier(string?)"/>) that
        /// can be emitted as a C# namespace name.
        /// </summary>
        public static bool IsCodeSafeNamespace([NotNullWhen(true)] this string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            foreach (string segment in name!.Split('.'))
            {
                if (!segment.IsCodeSafeIdentifier())
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Turns a name derived from model data (e.g. a model URI) into an
        /// identifier, or a dot separated sequence of identifiers when
        /// <paramref name="dotted"/> is set. A name that already is one is
        /// returned unchanged; otherwise every other character becomes an
        /// underscore and a segment that starts with a digit gets a leading
        /// underscore.
        /// </summary>
        public static string ToCodeSafeName(this string? name, bool dotted)
        {
            if (string.IsNullOrEmpty(name) ||
                (dotted ? name.IsCodeSafeNamespace() : name.IsCodeSafeIdentifier()))
            {
                return name ?? string.Empty;
            }

            var segments = new List<string>();
            foreach (string segment in dotted ? name!.Split('.') : [name!])
            {
                if (segment.Length == 0)
                {
                    continue;
                }
                var builder = new StringBuilder(segment.Length + 1);
                foreach (char c in segment)
                {
                    builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
                }
                if (!char.IsLetter(builder[0]) && builder[0] != '_')
                {
                    builder.Insert(0, '_');
                }
                segments.Add(builder.ToString());
            }
            return segments.Count == 0 ? "_" : string.Join(".", segments);
        }

        /// <summary>
        /// Returns whether <paramref name="name"/> is a type name that can be
        /// emitted verbatim: an optional <c>global::</c> alias followed by a
        /// dotted sequence of identifiers (the built-in model names its CLR
        /// types this way, e.g. <c>global::Opc.Ua.IEncodeable</c>).
        /// </summary>
        public static bool IsCodeSafeTypeName([NotNullWhen(true)] this string? name)
        {
            const string kGlobalAlias = "global::";
            if (name != null && name.StartsWith(kGlobalAlias, StringComparison.Ordinal))
            {
                name = name.Substring(kGlobalAlias.Length);
            }
            return name.IsCodeSafeNamespace();
        }

        /// <summary>
        /// Wrap the string as a string literal for generated code.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string AsStringLiteral(this string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "string.Empty";
            }
            return $"\"{Templating.StringLiteralEscaper.AsCSharpStringLiteralContent(value)}\"";
        }

        /// <summary>
        /// Escapes a value so it can be written as XML character data. Used by the
        /// schema generators, which write the BSD and XSD documents through plain
        /// text templates rather than an <see cref="XmlWriter"/>.
        /// </summary>
        public static string AsXmlText(this string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }
            return value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the value can be used as an XML element or attribute name -
        /// an NCName. Escaping cannot rescue a name that is not one: an element
        /// name is not character data, so "Read&amp;Write" has no legal spelling
        /// at all, and both <c>xs:element/@name</c> in the generated XSD and the
        /// element the XML encoder writes require this.
        /// </summary>
        public static bool IsValidXmlName([NotNullWhen(true)] this string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            try
            {
                return string.Equals(
                    XmlConvert.VerifyNCName(value), value, StringComparison.Ordinal);
            }
            catch (XmlException)
            {
                return false;
            }
        }

        /// <summary>
        /// Escapes a value so it can be written as the content of a double quoted
        /// XML attribute.
        /// </summary>
        public static string AsXmlAttributeValue(this string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }
            return value
                .AsXmlText()
                .Replace("\"", "&quot;", StringComparison.Ordinal);
        }

        /// <summary>
        /// Escape string
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static string Escape(this string? value)
        {
            // Line-terminator characters (CR, LF, NEL, LS, PS) end a regular
            // string literal (CS1010), so escape with the shared rule.
            return Templating.StringLiteralEscaper.AsCSharpStringLiteralContent(value);
        }

        /// <summary>
        /// Checks for a null qualified name.
        /// </summary>
        public static bool IsNull([NotNullWhen(false)] this XmlQualifiedName? qname)
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
