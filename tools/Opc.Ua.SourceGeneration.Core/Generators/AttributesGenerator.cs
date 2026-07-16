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
    internal class AttributesGenerator : IGenerator
    {
        /// <summary>
        /// Generates the attributes constants for nodes
        /// </summary>
        public AttributesGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            string fileName = Path.Combine(m_context.OutputFolder,
                CoreUtils.Format("{0}.Attributes.g.cs", Constants.CoreNamespacePrefix));
            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);

            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, CodeTemplates.StatusCode_Attributes_File);

            template.AddReplacement(Tokens.Prefix, Constants.CoreNamespacePrefix);
            template.AddReplacement(Tokens.ClassName, "Attributes");
            template.AddReplacement(Tokens.IdType, "uint");

            // load and validate type dictionary.
            var nodeDictionaries = new Dictionary<string, string>();
            var validator = new TypeDictionaryValidator(
                m_context.FileSystem,
                nodeDictionaries);
            validator.Validate(BuiltInDesignFiles.UAAttributesXml);
            Dictionary<string, int> identifiers =
                LoadIdentifiers(BuiltInDesignFiles.AttributesCsv);

            var constants = new List<Constant>();
            foreach (DataType datatype in validator.Dictionary.Items)
            {
                if (!TypeDictionaryValidator.IsExcluded(m_context.Options.Exclusions, datatype) &&
                    datatype is Constant constant &&
                    identifiers.TryGetValue(constant.Name, out int id))
                {
                    constant.Identifier = id;
                    constant.IdentifierSpecified = true;
                    constants.Add(constant);
                }
            }

            template.AddReplacement(
                Tokens.ListOfIdentifiers,
                CodeTemplates.StatusCode_Attributes_Constant,
                constants,
                WriteTemplate_AttributeConstant);

            template.AddReplacement(
                Tokens.IdentifierReflection,
                NodeIdTemplates.Reflection,
                [constants],
                WriteTemplate_ReflectionHelpers);

            template.Render();
            return [fileName.AsTextFileResource()];
        }

        /// <summary>
        /// Writes a constant.
        /// </summary>
        private bool WriteTemplate_AttributeConstant(IWriteContext context)
        {
            if (context.Target is not Constant constant)
            {
                return false;
            }

            if (string.IsNullOrEmpty(constant.Value))
            {
                // Other
                context.Template.AddReplacement(Tokens.IdType, "uint");
                context.Template.AddReplacement(Tokens.Identifier, constant.Identifier);
            }
            else
            {
                context.Template.AddReplacement(Tokens.IdType, "string"); // Never hit
                context.Template.AddReplacement(
                    Tokens.Identifier,
                    CoreUtils.Format("\"{0}\"", constant.Value)); // TODO: Make string resource
            }

            context.Template.AddReplacement(Tokens.SymbolicId, constant.Name);
            string description = constant.Documentation.GetDescription();
            context.Template.AddReplacement(Tokens.Description, description);

            return context.Template.Render();
        }

        /// <summary>
        /// Write reflection helpers for identifiers.
        /// </summary>
        private bool WriteTemplate_ReflectionHelpers(IWriteContext context)
        {
            if (context.Target is not List<Constant> constants)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.IdType, "uint");

            context.Template.AddReplacement(
                Tokens.ListOfIdentifersToNames,
                constants,
                LoadTemplate_IdentifierLookup);

            context.Template.AddReplacement(
                Tokens.ListOfNamesToIdentifiers,
                constants,
                LoadTemplate_IdentifierLookup);

            return context.Template.Render();
        }

        /// <summary>
        /// Write lookup entries for identifiers.
        /// </summary>
        private TemplateString LoadTemplate_IdentifierLookup(ILoadContext context)
        {
            if (context.Target is Constant constant)
            {
                string symbolicId = constant.Name;
                if (context.Token == Tokens.ListOfIdentifersToNames)
                {
                    context.Out.WriteLine("lookup[{0}] = \"{0}\";", symbolicId);
                }
                else if (context.Token == Tokens.ListOfNamesToIdentifiers)
                {
                    context.Out.WriteLine("lookup[\"{0}\"] = {0};", symbolicId);
                }
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
