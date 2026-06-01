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
using System.Threading;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generator options
    /// </summary>
    public sealed class GeneratorOptions
    {
        /// <summary>
        /// Optimize generated code for compile speed.
        /// </summary>
        public bool OptimizeForCompileSpeed { get; set; }

        /// <summary>
        /// Exclusions to apply on the input
        /// </summary>
        public IReadOnlyList<string> Exclusions { get; set; } = [];

        /// <summary>
        /// Generation should be cancelled
        /// </summary>
        public CancellationToken Cancellation { get; set; }

        /// <summary>
        /// Write utf8 string literals when needed
        /// </summary>
        public bool UseUtf8StringLiterals { get; set; } = true;

        /// <summary>
        /// When set to <c>true</c>, the
        /// <see cref="ObjectTypeProxyGenerator"/> is suppressed and no
        /// <c>*TypeClient</c> proxy classes are emitted. Off by default —
        /// proxies are emitted for every <c>ObjectType</c> in the model
        /// alongside the standard model output.
        /// </summary>
        public bool OmitObjectTypeProxies { get; set; }

        /// <summary>
        /// When set to <c>true</c>, the
        /// <see cref="StateMachineIdsGenerator"/> is suppressed and no
        /// <c>{TypeName}Ids</c> classes (nested <c>StateIds</c> /
        /// <c>StateNumbers</c> / <c>TransitionIds</c> /
        /// <c>TransitionNumbers</c>) are emitted. Off by default — IDs
        /// are emitted for every concrete <c>FiniteStateMachineType</c>
        /// subtype declared in the model.
        /// </summary>
        public bool OmitStateMachineIds { get; set; }

        /// <summary>
        /// Optional override for the C# namespace used by classes emitted
        /// by the <see cref="ObjectTypeProxyGenerator"/>. When unset,
        /// the model's target namespace prefix is used.
        /// </summary>
        public string ObjectTypeProxyNamespace { get; set; }

        /// <summary>
        /// When set to <c>true</c>, the <see cref="NodeStateGenerator"/>
        /// uses the modelling rules from the referenced type definition
        /// unconditionally, rather than the overridden rules on instance
        /// definitions, for all structural code generation decisions
        /// (child inclusion, optional vs mandatory classification) and
        /// the emitted runtime <c>ModellingRuleId</c>.
        /// <para>
        /// Off by default.  When off, the generator still enforces OPC UA
        /// modelling rule promotion semantics — instances may only
        /// <em>promote</em> the type definition's rule:
        /// <c>Optional → Mandatory</c>,
        /// <c>OptionalPlaceholder → Mandatory | MandatoryPlaceholder</c>,
        /// <c>MandatoryPlaceholder → Mandatory</c>.
        /// Any demotion is silently rejected and the type definition's
        /// rule is used instead.
        /// </para>
        /// </summary>
        public bool UseTypeDefinitionModellingRules { get; set; }

        /// <summary>
        /// Maps an OPC UA namespace URI (key) to the C# namespace (value)
        /// in which the corresponding source-generated <c>*TypeClient</c>
        /// proxies live. Used by the
        /// <see cref="ObjectTypeProxyGenerator"/> when a generated
        /// proxy must derive from a base proxy that is defined in a
        /// different (referenced) assembly.
        /// </summary>
        /// <remarks>
        /// The standard mapping
        /// <c>http://opcfoundation.org/UA/ -&gt; Opc.Ua.Client</c> is
        /// always added by the generator and does not need to be
        /// configured explicitly.
        /// </remarks>
        public IDictionary<string, string> ObjectTypeProxyExternalNamespaces { get; }
            = new Dictionary<string, string>();

        /// <summary>
        /// Optional override for the C# namespace used by classes
        /// emitted by the <see cref="EventRecordGenerator"/>. When
        /// unset, the model's target namespace prefix is used.
        /// </summary>
        public string EventRecordNamespace { get; set; }

        /// <summary>
        /// Maps an OPC UA namespace URI (key) to the C# namespace
        /// (value) in which the corresponding source-generated
        /// <c>*Record</c> classes live. Used by the
        /// <see cref="EventRecordGenerator"/> when a record must
        /// derive from a parent record defined in a different
        /// (referenced) assembly.
        /// </summary>
        /// <remarks>
        /// The standard mapping
        /// <c>http://opcfoundation.org/UA/ -&gt; Opc.Ua.Client.Alarms</c>
        /// is always added by the generator and does not need to be
        /// configured explicitly.
        /// </remarks>
        public IDictionary<string, string> EventRecordExternalNamespaces { get; }
            = new Dictionary<string, string>();

        /// <summary>
        /// When <c>false</c>, suppress the emission of the
        /// <c>{prefix}.ModelDependencies.g.cs</c> and
        /// <c>{prefix}.ModelSnapshot.g.cs</c> files. The consuming
        /// generator decides whether emission is appropriate
        /// (typically only for assemblies that will be referenced as
        /// libraries). Default <c>true</c> preserves existing
        /// behaviour for direct invokers of the Core API (tests, the
        /// model-compiler CLI).
        /// </summary>
        public bool EmitDependencyMetadata { get; set; } = true;
    }
}
