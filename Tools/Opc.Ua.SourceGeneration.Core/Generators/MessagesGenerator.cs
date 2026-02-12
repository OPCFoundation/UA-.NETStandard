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
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates service message data type annotation for stack code.
    /// </summary>
    internal sealed class MessagesGenerator : IGenerator
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MessagesGenerator"/> class.
        /// </summary>
        public MessagesGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            // get datatypes.
            Service[] serviceTypes = m_context.ModelDesign.GetListOfServices();
            if (serviceTypes.Length == 0)
            {
                return [];
            }

            string fileName = Path.Combine(
                m_context.OutputFolder,
                CoreUtils.Format("{0}.Messages.g.cs", Constants.CoreNamespacePrefix));
            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, MessagesTemplates.File);

            template.AddReplacement(Tokens.Prefix, Constants.CoreNamespacePrefix);

            template.AddReplacement(
                Tokens.TypeList,
                MessagesTemplates.DataTypeAnnotation,
                serviceTypes,
                WriteTemplate_ServiceMessage);

            template.Render();
            return [fileName.AsTextFileResource()];
        }

        /// <summary>
        /// Writes a service type.
        /// </summary>
        private bool WriteTemplate_ServiceMessage(IWriteContext context)
        {
            if (context.Target is not Service serviceType)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.Name, serviceType.Name);
            context.Template.AddReplacement(Tokens.Namespace, Constants.CoreNamespace);

            return context.Template.Render();
        }

        private readonly IGeneratorContext m_context;
    }
}
