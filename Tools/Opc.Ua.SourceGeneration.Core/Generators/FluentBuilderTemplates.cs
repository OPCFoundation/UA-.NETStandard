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

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Top-level templates for the <see cref="FluentBuilderGenerator"/>.
    /// The bulk of the generated code is emitted directly via
    /// <see cref="ITemplateWriter"/> calls from the generator (per-class,
    /// per-accessor, per-method-overload bodies); only the file shell and
    /// the boilerplate per-class skeleton live as template strings.
    /// </summary>
    internal static class FluentBuilderTemplates
    {
        /// <summary>
        /// Single output file template. Hosts the typed manager interface
        /// plus every per-type, per-instance and per-method wrapper class
        /// generated for the model.
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            #pragma warning disable CS0419 // Ambiguous reference in cref attribute
            #pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
            #pragma warning disable CA1707 // Identifiers should not contain underscores
            #pragma warning disable CA1822 // Mark members as static
            #pragma warning disable IDE0008 // Use explicit type
            #pragma warning disable IDE1006 // Naming rule violation

            #nullable enable

            namespace {{Tokens.NamespacePrefix}}
            {
                {{Tokens.ListOfTypes}}
            }

            """);
    }
}
