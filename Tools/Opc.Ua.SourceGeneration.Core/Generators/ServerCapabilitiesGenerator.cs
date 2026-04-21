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
using System.IO;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates the ServerCapabilities catalog from
    /// the embedded ServerCapabilities.csv design file.
    /// </summary>
    internal class ServerCapabilitiesGenerator : IGenerator
    {
        /// <summary>
        /// Create the server capabilities generator.
        /// </summary>
        public ServerCapabilitiesGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            string fileName = Path.Combine(
                m_context.OutputFolder,
                CoreUtils.Format(
                    "{0}.ServerCapabilities.g.cs",
                    Constants.CoreNamespacePrefix));

            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, CodeTemplates.StatusCode_Attributes_File);

            template.AddReplacement(Tokens.Prefix, Constants.CoreNamespacePrefix + ".Gds");
            template.AddReplacement(Tokens.ClassName, "ServerCapabilities");

            List<Capability> capabilities = LoadCapabilities(
                BuiltInDesignFiles.ServerCapabilitiesCsv);

            template.AddReplacement(
                Tokens.ListOfIdentifiers,
                CodeTemplates.StatusCode_Attributes_Constant,
                capabilities,
                WriteTemplate_CapabilityConstant);

            template.AddReplacement(
                Tokens.IdentifierReflection,
                s_allDictionaryTemplate,
                [capabilities],
                WriteTemplate_AllDictionary);

            template.Render();
            return [fileName.AsTextFileResource()];
        }

        /// <summary>
        /// Writes a single capability constant declaration.
        /// </summary>
        private bool WriteTemplate_CapabilityConstant(IWriteContext context)
        {
            if (context.Target is not Capability capability)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.IdType, "string");
            context.Template.AddReplacement(Tokens.SymbolicId, ToSymbol(capability.Id));
            context.Template.AddReplacement(
                Tokens.Identifier,
                CoreUtils.Format("\"{0}\"", EscapeString(capability.Id)));
            context.Template.AddReplacement(Tokens.Description, ToSummary(capability.Description));

            return context.Template.Render();
        }

        /// <summary>
        /// Writes the static <c>All</c> dictionary that maps every capability
        /// identifier to its description.
        /// </summary>
        private bool WriteTemplate_AllDictionary(IWriteContext context)
        {
            if (context.Target is not List<Capability> capabilities)
            {
                return false;
            }

            context.Template.AddReplacement(
                Tokens.ListOfIdentifiers,
                capabilities,
                LoadTemplate_AllEntry);

            return context.Template.Render();
        }

        /// <summary>
        /// Writes a single entry for the <c>All</c> dictionary initializer.
        /// </summary>
        private TemplateString LoadTemplate_AllEntry(ILoadContext context)
        {
            if (context.Target is Capability capability)
            {
                context.Out.WriteLine(
                    "{{ \"{0}\", \"{1}\" }},",
                    EscapeString(capability.Id),
                    EscapeString(capability.Description));
            }
            return null;
        }

        /// <summary>
        /// Loads the (id, description) capability entries from a CSV file.
        /// </summary>
        private List<Capability> LoadCapabilities(string capabilitiesFile)
        {
            var capabilities = new List<Capability>();

            using TextReader reader = m_context.FileSystem.CreateTextReader(capabilitiesFile);
            while (true)
            {
                string line = reader.ReadLine();
                if (line == null)
                {
                    break;
                }

                // strip a UTF-8 BOM if present on the first row.
                if (line.Length > 0 && line[0] == '\uFEFF')
                {
                    line = line.Substring(1);
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int index = line.IndexOf(',');
                if (index < 0)
                {
                    continue;
                }

                string id = line.Substring(0, index).Trim();
                string description = line.Substring(index + 1).Trim();
                if (id.Length == 0)
                {
                    continue;
                }

                capabilities.Add(new Capability(id, description));
            }

            return capabilities;
        }

        /// <summary>
        /// Maps a CSV id to a valid C# identifier. Ids that begin with a
        /// digit (e.g., "61850") are prefixed with "Iec".
        /// </summary>
        private static string ToSymbol(string id)
        {
            if (id.Length == 0)
            {
                return id;
            }
            if (char.IsDigit(id[0]))
            {
                return "Iec" + id;
            }
            return id;
        }

        /// <summary>
        /// Produces a short single-line XML summary text from a description.
        /// </summary>
        private static string ToSummary(string description)
        {
            if (string.IsNullOrEmpty(description))
            {
                return string.Empty;
            }

            string text = description.Trim();

            // first sentence wins, otherwise the first 80 chars.
            int dot = text.IndexOf('.');
            if (dot > 0)
            {
                text = text.Substring(0, dot + 1);
            }
            else if (text.Length > 80)
            {
                text = text.Substring(0, 80);
            }

            return EscapeXml(text);
        }

        private static string EscapeString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        /// <summary>
        /// Inline template for the static <c>All</c> dictionary block emitted
        /// in the <see cref="Tokens.IdentifierReflection"/> placeholder of
        /// <see cref="CodeTemplates.StatusCode_Attributes_File"/>.
        /// </summary>
        private static readonly TemplateString s_allDictionaryTemplate = TemplateString.Parse(
            $$"""
            /// <summary>
            /// All known capability identifiers keyed by their identifier.
            /// </summary>
            public static readonly global::System.Collections.Generic.IReadOnlyDictionary<string, string> All =
                new global::System.Collections.Generic.Dictionary<string, string>
                {
                    {{Tokens.ListOfIdentifiers}}
                };
            """);

        private sealed class Capability
        {
            public Capability(string id, string description)
            {
                Id = id;
                Description = description;
            }

            public string Id { get; }
            public string Description { get; }
        }

        private readonly IGeneratorContext m_context;
    }
}
