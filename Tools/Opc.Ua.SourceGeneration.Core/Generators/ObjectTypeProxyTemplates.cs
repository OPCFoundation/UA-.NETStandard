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
    /// Templates for the <see cref="ObjectTypeProxyGenerator"/>.
    /// Emits typed C# wrappers around <c>ISession.CallAsync</c> for every
    /// OPC UA <c>ObjectType</c> that declares one or more methods.
    /// </summary>
    internal static class ObjectTypeProxyTemplates
    {
        /// <summary>
        /// Single output file template. Hosts every proxy class
        /// generated for the model.
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            #nullable enable

            namespace {{Tokens.Namespace}}
            {
                {{Tokens.ListOfTypes}}
            }

            """);

        /// <summary>
        /// One proxy class per <c>ObjectType</c>. Always emitted for
        /// every non-excluded <c>ObjectType</c> so the generated
        /// <c>*TypeClient</c> classes form an inheritance chain that
        /// mirrors the OPC UA type hierarchy. Types with no declared
        /// methods produce an empty class body that consumers may extend
        /// via <c>partial</c>.
        /// </summary>
        public static readonly TemplateString ProxyClass = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Strongly typed client wrapper for the OPC UA ObjectType
            /// <c>{{Tokens.SymbolicName}}</c>. Each declared method is
            /// exposed as an asynchronous wrapper around
            /// <c>ISessionClient.CallAsync</c>.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            public partial class {{Tokens.ClassName}} : {{Tokens.BaseClassName}}
            {
                /// <summary>
                /// Initializes a new instance of the <see cref="{{Tokens.ClassName}}"/> class.
                /// </summary>
                /// <param name="session">The OPC UA client session used to invoke the methods.</param>
                /// <param name="objectId">The NodeId of the object on which the methods are invoked.</param>
                /// <param name="telemetry">Telemetry context used for diagnostics.</param>
                public {{Tokens.ClassName}}(
                    global::Opc.Ua.ISessionClient session,
                    global::Opc.Ua.NodeId objectId,
                    global::Opc.Ua.ITelemetryContext telemetry)
                    : base(session, objectId, telemetry)
                {
                }
                {{Tokens.MethodList}}
            }

            """);

    }
}
