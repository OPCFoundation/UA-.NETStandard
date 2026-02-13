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
using System.Globalization;
using System.IO;
using Opc.Ua.Schema.Types;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates code based on a UA Type Dictionary.
    /// </summary>
    internal class StatusCodesGenerator : IGenerator
    {
        /// <summary>
        /// Create status codes generator
        /// </summary>
        public StatusCodesGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            string fileName = Path.Combine(m_context.OutputFolder,
                CoreUtils.Format("{0}.StatusCodes.g.cs", Constants.CoreNamespacePrefix));
            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);

            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, CodeTemplates.StatusCode_Attributes_File);

            template.AddReplacement(Tokens.Prefix, Constants.CoreNamespacePrefix);
            template.AddReplacement(Tokens.ClassName, "StatusCodes");

            var nodeDictionaries = new Dictionary<string, string>();
            var validator = new TypeDictionaryValidator(
                m_context.FileSystem,
                nodeDictionaries);
            validator.Validate(BuiltInDesignFiles.UAStatusCodesXml);
            Dictionary<string, int> identifiers = LoadIdentifiers(
                BuiltInDesignFiles.StatusCodesCsv);
            var constants = new List<Constant>
            {
                new()
                {
                    Severity = Severity.Good,
                    Name = nameof(Severity.Good),
                    Documentation = new Documentation { Text = ["Success"] }
                },
                new()
                {
                    Severity = Severity.Bad,
                    Name = nameof(Severity.Bad),
                    Documentation = new Documentation { Text = ["Bad status"] }
                },
                new()
                {
                    Severity = Severity.Uncertain,
                    Name = nameof(Severity.Uncertain),
                    Documentation = new Documentation { Text = ["Uncertain status"] }
                }
            };

            foreach (DataType datatype in validator.Dictionary.Items)
            {
                if (!TypeDictionaryValidator.IsExcluded(m_context.Options.Exclusions, datatype) &&
                    datatype is Constant constant &&
                    identifiers.TryGetValue(constant.Name, out int id))
                {
                    if (constant.Name.StartsWith(
                        nameof(Severity.Bad),
                        StringComparison.Ordinal))
                    {
                        constant.Severity = Severity.Bad;
                    }
                    else if (constant.Name.StartsWith(
                        nameof(Severity.Good),
                        StringComparison.Ordinal))
                    {
                        constant.Severity = Severity.Good;
                    }
                    else if (constant.Name.StartsWith(
                        nameof(Severity.Uncertain),
                        StringComparison.Ordinal))
                    {
                        constant.Severity = Severity.Uncertain;
                    }
                    constant.Identifier = id;
                    constant.IdentifierSpecified = true;
                    constants.Add(constant);
                }
            }

            // collect datatypes with the specified type.
            template.AddReplacement(
                Tokens.ListOfIdentifiers,
                CodeTemplates.StatusCodes_Declaration,
                constants,
                WriteTemplate_StatusCodeDeclaration);

            template.AddReplacement(
                Tokens.IdentifierReflection,
                CodeTemplates.StatusCode_TypeInterning,
                [constants],
                WriteTemplate_StatusCodeInterning);

            template.Render();
            return [fileName.AsTextFileResource()];
        }

        /// <summary>
        /// Writes the status code declaration
        /// </summary>
        private bool WriteTemplate_StatusCodeDeclaration(IWriteContext context)
        {
            if (context.Target is not Constant constant)
            {
                return false;
            }

            // Status codes
            uint id = Convert.ToUInt32(constant.Identifier, CultureInfo.InvariantCulture);
            id <<= 16;

            switch (constant.Severity)
            {
                case Severity.Bad:
                    id += 0x80000000;
                    break;
                case Severity.Uncertain:
                    id += 0x40000000;
                    break;
            }
            context.Template.AddReplacement(Tokens.Identifier, CoreUtils.Format("0x{0:X8}", id));

            string symbolicId = constant.Name;
            if (constant.Identifier != 0)
            {
                // Status codes
                string name = constant.Name;
                int index = name.IndexOf('_', StringComparison.Ordinal);
                if (index != -1)
                {
                    name = name[(index + 1)..];
                }
                symbolicId = CoreUtils.Format("{0}{1}", constant.Severity, name);
            }
            context.Template.AddReplacement(Tokens.SymbolicId, symbolicId);

            string description = constant.Documentation.GetDescription();
            context.Template.AddReplacement(Tokens.Description, description);

            return context.Template.Render();
        }

        /// <summary>
        /// Write Status code interning context.Template
        /// </summary>
        private bool WriteTemplate_StatusCodeInterning(IWriteContext context)
        {
            if (context.Target is not List<Constant> constants)
            {
                return false;
            }
            context.Template.AddReplacement(Tokens.IdType, "global::Opc.Ua.StatusCode");
            context.Template.AddReplacement(
                Tokens.ListOfIdentifiers,
                constants,
                LoadTemplate_StatusCodeIdentifier);
            return context.Template.Render();
        }

        /// <summary>
        /// Write identifiers for interning
        /// </summary>
        private TemplateString LoadTemplate_StatusCodeIdentifier(ILoadContext context)
        {
            if (context.Target is Constant constant)
            {
                string symbolicId = constant.Name;
                if (constant.Severity != Severity.None && constant.Identifier != 0)
                {
                    // Status codes
                    string name = constant.Name;
                    int index = name.IndexOf('_', StringComparison.Ordinal);
                    if (index != -1)
                    {
                        name = name[(index + 1)..];
                    }
                    symbolicId = CoreUtils.Format("{0}{1}", constant.Severity, name);
                }
                context.Out.Write(symbolicId);
                context.Out.WriteLine(",");
            }
            return null;
        }

        /// <summary>
        /// Loads the identifiers from a CSV file.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private Dictionary<string, int> LoadIdentifiers(string identifiersFile)
        {
            var identifiers = new Dictionary<string, int>();
            int maxId = 1;

            using TextReader reader = m_context.FileSystem.CreateTextReader(identifiersFile);
            while (true)
            {
                string line = reader.ReadLine();
                if (line == null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int index = line.IndexOf(',', StringComparison.Ordinal);

                if (index == -1)
                {
                    continue;
                }

                // remove the node class if it is present.
                int lastIndex = line.LastIndexOf(',');

                if (lastIndex != -1 && index != lastIndex)
                {
                    line = line[..lastIndex];
                }

                string name = line[..index].Trim();

                int uid = Convert.ToInt32(
                    line[(index + 1)..].Trim(),
                    CultureInfo.InvariantCulture);

                if (maxId <= uid)
                {
                    maxId = uid + 1;
                }

                identifiers[name] = uid;
            }
            return identifiers;
        }

        private readonly IGeneratorContext m_context;
    }
}
