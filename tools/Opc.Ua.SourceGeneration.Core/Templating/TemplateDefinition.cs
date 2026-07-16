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

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Stores the information that describes how to initialize and process a template.
    /// </summary>
    internal sealed class TemplateDefinition
    {
        /// <summary>
        /// The template composite string
        /// </summary>
        public TemplateString TemplateString { get; set; }

        /// <summary>
        /// The targets that the template should be applied to.
        /// </summary>
        public IReadOnlyList<object> Targets { get; set; }

        /// <summary>
        /// The callback to call when loading the template.
        /// </summary>
        public LoadTemplateEventHandler OnTemplateLoad { get; set; }

        /// <summary>
        /// The callback to call when writing the template.
        /// </summary>
        public WriteTemplateEventHandler OnTemplateWrite { get; set; }

        /// <summary>
        /// Load the template.
        /// </summary>
        public TemplateString Load(ILoadContext context)
        {
            // check for override.
            if (OnTemplateLoad != null)
            {
                return OnTemplateLoad(context);
            }

            // use the default function to write the template.
            return context.TemplateString;
        }

        /// <summary>
        /// Render the template.
        /// </summary>
        public bool Render(IWriteContext context)
        {
            // check for override.
            if (OnTemplateWrite != null)
            {
                return OnTemplateWrite(context);
            }

            // use the default function to write the template.
            return context.Template.Render();
        }
    }

    /// <summary>
    /// Context
    /// </summary>
    internal interface ILoadContext
    {
        /// <summary>
        /// The index of the current target within the list being processed.
        /// </summary>
        int Index { get; }

        /// <summary>
        /// Get template writer
        /// </summary>
        ITemplateWriter Out { get; }

        /// <summary>
        /// The current iteration variable that is the target of the load callback.
        /// </summary>
        object Target { get; }

        /// <summary>
        /// The interpolated template string passed to AddReplacement method.
        /// </summary>
        TemplateString TemplateString { get; }

        /// <summary>
        /// The token that is to be replaced by the current template evaluation.
        /// </summary>
        string Token { get; }
    }

    /// <summary>
    /// A delegate handle events associated with template.
    /// </summary>
    internal delegate TemplateString LoadTemplateEventHandler(ILoadContext context);

    /// <summary>
    /// Write context
    /// </summary>
    internal interface IWriteContext
    {
        /// <summary>
        /// The current iteration variable that is the target of the write callback
        /// </summary>
        object Target { get; }

        /// <summary>
        /// The index of the current target within the list being processed.
        /// </summary>
        int Index { get; }

        /// <summary>
        /// The template to write
        /// </summary>
        Template Template { get; }
    }

    /// <summary>
    /// A delegate handle events associated with template.
    /// </summary>
    internal delegate bool WriteTemplateEventHandler(IWriteContext context);

    /// <summary>
    /// Contains the current context to use for serialization.
    /// </summary>
    internal sealed record class TemplateContext : ILoadContext, IWriteContext
    {
        /// <summary>
        /// Create the template event handler context
        /// </summary>
        public TemplateContext(
            TemplateWriter writer,
            string token,
            TemplateString templateString)
        {
            Out = writer;
            Token = token;
            TemplateString = templateString;
            Index = 0;
        }

        /// <summary>
        /// Set the template
        /// </summary>
        public Template Template { get; set; }

        /// <inheritdoc/>
        public ITemplateWriter Out { get; }

        /// <inheritdoc/>
        public string Token { get; }

        /// <inheritdoc/>
        public TemplateString TemplateString { get; set; }

        /// <inheritdoc/>
        public int Index { get; set; }

        /// <inheritdoc/>
        public object Target { get; set; }
    }
}
