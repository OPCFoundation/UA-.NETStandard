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

            // Suppress CS8600 for the generated TryGetStructure calls.
            // TryGetStructure uses [MaybeNullWhen(false)] but the analyzer still
            // flags assignment of the maybe-null out parameter to a non-nullable
            // local; the generated code always checks the bool and throws when
            // the call returns false, so the local is non-null after the if.
            #pragma warning disable CS8600

            namespace {{Tokens.Namespace}}
            {
                {{Tokens.ListOfTypes}}
            }

            #pragma warning restore CS8600

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
                {{Tokens.ListOfChildren}}
            }

            """);

        /// <summary>
        /// One lazy + cached typed accessor for an <c>&lt;opc:Object&gt;</c>
        /// child of a parent <c>ObjectType</c>. The browse round-trip
        /// is delegated to
        /// <c>ObjectTypeClient.ResolveChildNodeIdAsync</c> so the
        /// generated body stays minimal and language-version-portable.
        /// Returns <c>null</c> for optional children the server does
        /// not expose.
        /// </summary>
        public static readonly TemplateString ChildAccessor = TemplateString.Parse(
            $$"""

            /// <summary>
            /// Returns the typed proxy for the <c>{{Tokens.BrowseName}}</c> child Object
            /// of type <c>{{Tokens.TypeName}}</c>. Lazily resolved on first call (one
            /// TranslateBrowsePath round-trip) and cached for
            /// subsequent calls. Returns <c>null</c> when the server
            /// does not expose the child (Optional children, missing
            /// namespace, BadNotFound result, ...).
            /// </summary>
            /// <param name="telemetry">Telemetry context for the
            /// returned child proxy.</param>
            /// <param name="ct">Cancellation token.</param>
            public {{Tokens.AccessModifier}}async global::System.Threading.Tasks.ValueTask<{{Tokens.ClassName}}?> Get{{Tokens.BrowseName}}Async(
                global::Opc.Ua.ITelemetryContext telemetry,
                global::System.Threading.CancellationToken ct = default)
            {
                if ({{Tokens.FieldName}} != null)
                {
                    return {{Tokens.FieldName}};
                }
                global::Opc.Ua.NodeId childId = await ResolveChildNodeIdAsync(
                    "{{Tokens.BrowseNameNamespaceUri}}", "{{Tokens.BrowseNameLiteral}}", ct).ConfigureAwait(false);
                if (childId.IsNull)
                {
                    return null;
                }
                var proxy = new {{Tokens.ClassName}}(Session, childId, telemetry);
                return global::System.Threading.Interlocked.CompareExchange(
                    ref {{Tokens.FieldName}}, proxy, null) ?? proxy;
            }
            #pragma warning disable CS0649  // field never assigned — assigned in Get*Async via Interlocked.CompareExchange
            private {{Tokens.ClassName}}? {{Tokens.FieldName}};
            #pragma warning restore CS0649
            """);
    }
}
