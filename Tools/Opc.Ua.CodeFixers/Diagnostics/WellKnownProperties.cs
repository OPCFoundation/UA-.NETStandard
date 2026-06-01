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

namespace Opc.Ua.CodeFixers.Diagnostics
{
    /// <summary>
    /// Shared <c>DiagnosticDescriptor.Properties</c> keys passed from analyzers
    /// to their companion code-fix providers.
    /// </summary>
    /// <remarks>
    /// Defined here (in the analyzer DLL) and linked into the companion CodeFixes
    /// project so both assemblies can use the exact same string constants without
    /// the CodeFixes assembly needing a ProjectReference back to the analyzer
    /// (which would create a NuGet restore cycle).
    /// </remarks>
    internal static class WellKnownProperties
    {
        /// <summary>UA0008: method name extracted from a <c>Session.Call</c> invocation.</summary>
        public const string MethodName = "MethodName";

        /// <summary>UA0020: form discriminator key for <c>EncodeableFactory</c> rename.</summary>
        public const string Form = "Form";

        /// <summary>UA0020 form: legacy <c>EncodeableFactory.GlobalFactory</c> getter.</summary>
        public const string FormGlobalFactory = "A";

        /// <summary>UA0020 form: legacy <c>factory.Create()</c> instance call.</summary>
        public const string FormCreate = "B";
    }
}
