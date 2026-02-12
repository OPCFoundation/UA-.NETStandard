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
using System.Reflection;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Templating engine
    /// </summary>
    internal sealed class Template
    {
        /// <summary>
        /// Create template
        /// </summary>
        public Template(TemplateWriter writer, TemplateString templateString)
            : this(writer, templateString, null)
        {
        }

        /// <summary>
        /// Create template
        /// </summary>
        private Template(
            TemplateWriter writer,
            TemplateString templateString,
            Template parent)
        {
            m_replacements = [];
            m_templateString = templateString;
            m_outerTemplate = parent;
            m_writer = writer;
            m_replacements.Add(Tokens.CodeHeader, CodeTemplates.CodeHeader);
            m_replacements.Add(Tokens.Tool,
                Assembly.GetExecutingAssembly().GetName().Name);
            m_replacements.Add(
                Tokens.Version,
#if FULL_VERSION
                CoreUtils.Format("{0}.{1}", s_softwareVersion, s_buildVersion));
#else
                s_softwareVersion);
#endif
        }

        /// <summary>
        /// Adds a replacement value for a token.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public void AddReplacement<T>(string token, T replacement)
        {
            m_replacements[token] = CoreUtils.Format("{0}", replacement);
        }

        /// <summary>
        /// Adds a replacement value for a token.
        /// </summary>
        public void AddReplacement(string token, bool replacement)
        {
            m_replacements[token] = replacement ? "true" : "false";
        }

        /// <summary>
        /// Adds a replacement value for a token.
        /// </summary>
        public void AddReplacement(string token, string replacement)
        {
            m_replacements[token] = replacement;
        }

        /// <summary>
        /// Add template definition for a token.
        /// </summary>
        public void AddReplacement(string token, TemplateDefinition templateDefinition)
        {
            m_replacements[token] = templateDefinition;
        }

        /// <summary>
        /// Render the template
        /// </summary>
        public bool Render()
        {
            m_writer.TrimLineBreak(2);
            bool written = false;
            ParsedTemplateString parsed = m_templateString.ParsedTemplate;
            for (int i = 0; i < parsed.Operations.Count; i++)
            {
                ParsedTemplateString.Op op = parsed.Operations[i];
                switch (op.Type)
                {
                    case ParsedTemplateString.OpType.Token:
                        // check if a template substitution is required.
                        if (!TryGetReplacement(op.Item, out object replacement) ||
                            replacement == null)
                        {
                            m_writer.TrimLineBreak(0);
                            break;
                        }
                        if (replacement is not TemplateDefinition definition)
                        {
                            m_writer.Write(replacement.ToString());
                            written = true;
                            break;
                        }
                        if (definition.Targets == null ||
                            definition.Targets.Count == 0)
                        {
                            m_writer.TrimLineBreak(0);
                            break;
                        }
                        written = false;
                        var context = new TemplateContext(m_writer, op.Item, definition.TemplateString);
                        m_writer.PushIndentChars(op.Offset);
                        bool writeNewLineBetweenTargets = false;
                        for (int j = 0; j < definition.Targets.Count; j++)
                        {
                            context.Target = definition.Targets[j];

                            // get the template path name.
                            TemplateString templateString = definition.Load(context);
                            // skip item if no template specified.
                            if (templateString == null)
                            {
                                m_writer.TrimLineBreak(writeNewLineBetweenTargets ? 0 : 1);
                                context.Index++;
                                continue;
                            }

                            // begin new line between multi line items if needed.
                            if (writeNewLineBetweenTargets)
                            {
                                m_writer.WriteNewLine(2);
                                m_writer.WriteNewLine(2);
                            }

                            // load the template.
                            var template = new Template(
                                m_writer,
                                templateString,
                                this);
                            if (definition.Render(context with { Template = template }))
                            {
                                writeNewLineBetweenTargets =
                                    templateString.ParsedTemplate.IsMultiLine;
                                written = true;
                            }
                            else
                            {
                                writeNewLineBetweenTargets = false;
                            }
                            context.Index++;
                        }
                        m_writer.PopIndentation();
                        break;
                    case ParsedTemplateString.OpType.LineBreak:
                        m_writer.WriteNewLine(int.MaxValue);
                        written = true;
                        break;
                    // Not a token, e.g. a date time or value that was appended
                    case ParsedTemplateString.OpType.Value:
                    case ParsedTemplateString.OpType.Literal:
                        m_writer.Write(op.Item);
                        written = true;
                        break;
                    case ParsedTemplateString.OpType.WhiteSpace:
                        m_writer.WriteWhiteSpace(op.Item.Length);
                        break;
                }
            }
            return written;
        }

        /// <summary>
        /// Try get replacement and fall back to outer scope
        /// </summary>
        private bool TryGetReplacement(string token, out object replacement)
        {
            if (!m_replacements.TryGetValue(token, out replacement))
            {
                return m_outerTemplate != null &&
                    m_outerTemplate.TryGetReplacement(token, out replacement);
            }
            return true;
        }

        private static readonly string s_softwareVersion = CoreUtils.GetAssemblySoftwareVersion();
#if FULL_VERSION
        private static readonly string s_buildVersion = CoreUtils.GetAssemblyBuildNumber();
#endif
        private readonly TemplateString m_templateString;
        private readonly Template m_outerTemplate;
        private readonly TemplateWriter m_writer;
        private readonly Dictionary<string, object> m_replacements;
    }
}
