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
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="NodeManagerGenerator"/>: verify the
    /// opt-in flag, the file pair, and the structural shape of the
    /// emitted partial NodeManager + factory.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable(ParallelScope.All)]
    public class NodeManagerGeneratorTests
    {
        [Test]
        public void Emit_WithoutOptIn_ProducesNoNodeManagerFiles()
        {
            Dictionary<string, string> files = GenerateForTestModel(generateNodeManager: false);

            Assert.That(files.Keys, Has.None.EndsWith(".NodeManager.g.cs"));
            Assert.That(files.Keys, Has.None.EndsWith(".NodeManagerFactory.g.cs"));
        }

        [Test]
        public void Emit_WithOptIn_ProducesNodeManagerAndFactoryFiles()
        {
            Dictionary<string, string> files = GenerateForTestModel(generateNodeManager: true);

            Assert.That(files.Keys, Has.Some.EndsWith(".NodeManager.g.cs"),
                "Generator should emit a NodeManager file when GenerateNodeManager=true");
            Assert.That(files.Keys, Has.Some.EndsWith(".NodeManagerFactory.g.cs"),
                "Generator should emit a NodeManagerFactory file when GenerateNodeManager=true");
        }

        [Test]
        public void EmittedNodeManager_HasRequiredStructuralMembers()
        {
            Dictionary<string, string> files = GenerateForTestModel(generateNodeManager: true);

            string mgr = files.Single(kv => kv.Key.EndsWith(".NodeManager.g.cs", StringComparison.Ordinal)).Value;

            // Inheritance and partial — required so users can extend.
            Assert.That(mgr, Does.Contain(": global::Opc.Ua.Server.Fluent.FluentNodeManagerBase"));
            Assert.That(mgr, Does.Match(@"public\s+partial\s+class\s+\w+NodeManager"));

            // Node-manager lifecycle members emitted by the generated partial.
            Assert.That(mgr, Does.Contain("LoadPredefinedNodesAsync"));
            Assert.That(mgr, Does.Contain("CreateAddressSpaceAsync"));
            Assert.That(mgr, Does.Contain("AddPredefinedNodeAsync"));
            Assert.That(mgr, Does.Contain("RemovePredefinedNodeAsync"));
            Assert.That(
                mgr,
                Does.Not.Contain("OnMonitoredItemCreated"),
                "Monitored-item lifecycle forwarding is owned by FluentNodeManagerBase");

            // The user code-behind hook + the runtime builder type.
            Assert.That(mgr, Does.Contain("partial void Configure("));
            Assert.That(mgr, Does.Contain("global::Opc.Ua.Server.Fluent.NodeManagerBuilder"));
            Assert.That(mgr, Does.Contain("global::Opc.Ua.Server.Fluent.INodeManagerBuilder"));

            // The Configure/CompleteConfigure/Seal sequence inside
            // CreateAddressSpace must be wired before any NotifyNodeAdded
            // replays. Order is part of the contract and is exercised by
            // the hybrid integration test. CompleteConfigureAsync re-runs
            // the reverse-reference pass so configure-created nodes publish
            // references to nodes owned by other managers (issue #4329).
            int idxConfigure = mgr.IndexOf("Configure(__m_builder)", StringComparison.Ordinal);
            int idxComplete = mgr.IndexOf(
                "await CompleteConfigureAsync(externalReferences, cancellationToken)",
                StringComparison.Ordinal);
            int idxSeal = mgr.IndexOf(".Seal()", StringComparison.Ordinal);
            int idxNotify = mgr.IndexOf("NotifyNodeAdded(", StringComparison.Ordinal);
            Assert.That(idxConfigure, Is.GreaterThan(0), "Configure call must be emitted");
            Assert.That(idxComplete, Is.GreaterThan(idxConfigure),
                "CompleteConfigureAsync must run after Configure");
            Assert.That(idxSeal, Is.GreaterThan(idxComplete),
                "Seal must run after CompleteConfigureAsync");
            Assert.That(idxNotify, Is.GreaterThan(idxSeal), "NotifyNodeAdded replay must run after Seal");
        }

        [Test]
        public void EmittedNodeManager_WithoutAdditionalNamespaceUris_ReportsModelNamespaceOnly()
        {
            Dictionary<string, string> files = GenerateForTestModel(generateNodeManager: true);

            string mgr = files.Single(kv => kv.Key.EndsWith(".NodeManager.g.cs", StringComparison.Ordinal)).Value;
            string factory = files.Single(kv => kv.Key.EndsWith(".NodeManagerFactory.g.cs", StringComparison.Ordinal)).Value;

            Assert.That(mgr, Does.Not.Contain("/Instance"));
            Assert.That(factory, Does.Not.Contain("/Instance"));
        }

        [Test]
        public void EmittedNodeManager_WithAdditionalNamespaceUris_ReportsThemAtConstruction()
        {
            const string instanceUri = "http://test.org/UA/TestModel/Instance";
            Dictionary<string, string> files = GenerateForTestModel(
                generateNodeManager: true,
                additionalNamespaceUris: [instanceUri]);

            string mgr = files.Single(kv => kv.Key.EndsWith(".NodeManager.g.cs", StringComparison.Ordinal)).Value;
            string factory = files.Single(kv => kv.Key.EndsWith(".NodeManagerFactory.g.cs", StringComparison.Ordinal)).Value;

            // The constructor must pass the extra namespace to the base
            // manager so the master node manager routes it to this manager
            // from construction (SetNamespaces after the fact is too late).
            Assert.That(mgr, Does.Contain(", \"" + instanceUri + "\")"),
                "Constructor must append the additional namespace URI to the base call");

            // The factory must advertise the same namespace set.
            Assert.That(factory, Does.Contain(", \"" + instanceUri + "\" })"),
                "Factory NamespacesUris must include the additional namespace URI");
        }

        [Test]
        public void EmittedNodeManagerInUnrelatedNamespaceUsesQualifiedModelComposer()
        {
            Dictionary<string, string> files = GenerateForTestModel(
                generateNodeManager: true,
                nodeManagerNamespace: "Unrelated.Managers");

            string mgr = files.Single(
                kv => kv.Key.EndsWith(".NodeManager.g.cs", StringComparison.Ordinal)).Value;

            Assert.That(mgr, Does.Contain("namespace Unrelated.Managers"));
            Assert.That(
                mgr,
                Does.Match(
                    @"global::TestModel\.TestModelExtensions\.AddTestModel\(\s*" +
                    @"new global::Opc\.Ua\.NodeStateCollection\(\),\s*context\)"),
                "LoadPredefinedNodesAsync must invoke the model composer as a fully-qualified static method");
        }

        [Test]
        public void EmittedFactory_IsExtensible()
        {
            Dictionary<string, string> files = GenerateForTestModel(generateNodeManager: true);

            string factory = files.Single(kv => kv.Key.EndsWith(".NodeManagerFactory.g.cs", StringComparison.Ordinal)).Value;

            // Must NOT be sealed: users with Boiler-style customization
            // need to subclass the factory to register additional namespaces
            // or to swap in a hand-written manager subclass.
            Assert.That(factory, Does.Not.Match(@"sealed\s+partial\s+class\s+\w+NodeManagerFactory"),
                "Generated factory must not be sealed so users can subclass it");
            Assert.That(factory, Does.Match(@"public\s+partial\s+class\s+\w+NodeManagerFactory"));

            // Members must be virtual so subclasses can override
            // (extending NamespacesUris or returning a custom manager).
            Assert.That(factory, Does.Match(@"public\s+virtual\s+global::Opc\.Ua\.ArrayOf<string>\s+NamespacesUris"));
            Assert.That(factory, Does.Match(
                @"public\s+virtual\s+global::System\.Threading\.Tasks\.ValueTask<global::Opc\.Ua\.Server\.IAsyncNodeManager>\s+CreateAsync"));

            Assert.That(factory, Does.Contain(": global::Opc.Ua.Server.IAsyncNodeManagerFactory"));
        }

        [Test]
        public void EmittedFiles_AreAutoGenerated_AndUseGlobalQualifiedTypes()
        {
            Dictionary<string, string> files = GenerateForTestModel(generateNodeManager: true);

            foreach (KeyValuePair<string, string> kv in files
                .Where(kv => kv.Key.EndsWith(".NodeManager.g.cs", StringComparison.Ordinal) ||
                    kv.Key.EndsWith(".NodeManagerFactory.g.cs", StringComparison.Ordinal)))
            {
                Assert.That(kv.Value, Does.StartWith("// <auto-generated />"),
                    $"{kv.Key} must start with the auto-generated marker");
                Assert.That(kv.Value, Does.Contain("[global::System.CodeDom.Compiler.GeneratedCodeAttribute("),
                    $"{kv.Key} must carry the GeneratedCode attribute");
                // No bare 'Opc.Ua.' prefixes — everything goes via global::
                // to survive use-site namespace conflicts (matches the rest
                // of the source-generation output).
                int bare = CountUnqualifiedOpcUaUses(kv.Value);
                Assert.That(bare, Is.Zero,
                    $"{kv.Key} must use global:: qualification only ({bare} bare uses found)");
            }
        }

        /// <summary>
        /// CreateInstanceOf&lt;X&gt; factories must emit AccessRestrictions and
        /// RolePermissions inherited from the type's DefaultAccessRestrictions /
        /// DefaultRolePermissions for ObjectType, VariableType and MethodType.
        /// Regression test for the source-generated factory missing these
        /// attributes when the type declares Default* permissions.
        /// </summary>
        [Test]
        public void CreateInstanceOf_EmitsAccessRestrictionsAndRolePermissionsFromType()
        {
            Dictionary<string, string> files = GenerateForTestModel(generateNodeManager: false);

            string ex = files
                .Single(kv => kv.Key.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal))
                .Value;

            string variableFactory = ExtractFactoryBody(ex, "CreateInstanceOfRestrictedVariableType");
            Assert.That(variableFactory, Does.Contain(
                "nodeState.AccessRestrictions = global::Opc.Ua.AccessRestrictionType.EncryptionRequired"));
            Assert.That(variableFactory, Does.Contain(
                "state.RolePermissions = new global::Opc.Ua.RolePermissionType[]"));

            string objectFactory = ExtractFactoryBody(ex, "CreateInstanceOfRestrictedObjectType");
            Assert.That(objectFactory, Does.Contain(
                "nodeState.AccessRestrictions = global::Opc.Ua.AccessRestrictionType.EncryptionRequired"));
            Assert.That(objectFactory, Does.Contain(
                "state.RolePermissions = new global::Opc.Ua.RolePermissionType[]"));

            string methodFactory = ExtractFactoryBody(ex, "CreateInstanceOfRestrictedMethodType");
            Assert.That(methodFactory, Does.Contain(
                "nodeState.AccessRestrictions = global::Opc.Ua.AccessRestrictionType.SigningRequired"));
            Assert.That(methodFactory, Does.Contain(
                "state.RolePermissions = new global::Opc.Ua.RolePermissionType[]"));
        }

        [Test]
        public void CreateInstanceOfFactoriesRebaseOnlyDynamicTypeInstances()
        {
            Dictionary<string, string> files = GenerateForTestModel(generateNodeManager: false);

            string ex = files
                .Single(kv => kv.Key.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal))
                .Value;

            const string rebaseCall =
                "global::Opc.Ua.NodeInstanceExtensions.AssignInstanceChildNodeIds(";
            const string assignNodeId =
                "global::Opc.Ua.NodeInstanceExtensions.AssignInstanceNodeId(context, state);";
            const string captureNodeId = "global::Opc.Ua.NodeId previousNodeId =";

            string variableFactory = ExtractFactoryBody(ex, "CreateInstanceOfRestrictedVariableType");
            string objectFactory = ExtractFactoryBody(ex, "CreateInstanceOfRestrictedObjectType");
            string methodFactory = ExtractFactoryBody(ex, "CreateInstanceOfRestrictedMethodType");

            Assert.That(variableFactory, Does.Contain(captureNodeId));
            Assert.That(variableFactory, Does.Contain(assignNodeId));
            Assert.That(variableFactory, Does.Contain(rebaseCall));
            Assert.That(objectFactory, Does.Contain(captureNodeId));
            Assert.That(objectFactory, Does.Contain(assignNodeId));
            Assert.That(objectFactory, Does.Contain(rebaseCall));
            Assert.That(methodFactory, Does.Contain(captureNodeId));
            Assert.That(methodFactory, Does.Contain(assignNodeId));
            Assert.That(methodFactory, Does.Contain(rebaseCall));

            Assert.That(
                ExtractFactoryBody(ex, "CreateTestObject"),
                Does.Not.Contain(captureNodeId)
                    .And.Not.Contain(assignNodeId)
                    .And.Not.Contain(rebaseCall),
                "Predefined concrete instance factories must retain their standard NodeIds.");
        }

        [Test]
        public void OptionalChildAddersAttachBeforeReferenceRemapping()
        {
            Dictionary<string, string> files = GenerateForTestModel(generateNodeManager: false);
            string source = files.Values.Single(value =>
                value.Contains(" AddX(", StringComparison.Ordinal));
            string addX = ExtractInstanceMethodBody(source, "AddX");

            int attachIndex = addX.IndexOf("X = state;", StringComparison.Ordinal);
            int rebaseIndex = addX.IndexOf(
                "AssignInstanceChildNodeIds(",
                StringComparison.Ordinal);

            Assert.That(attachIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(rebaseIndex, Is.GreaterThan(attachIndex));
        }

        /// <summary>
        /// A child materialised onto an already-instantiated tree (through
        /// NodeState.CreateChild / ReplaceChild or by hand) must receive a
        /// per-instance NodeId, otherwise sibling instances of the same type
        /// collide on the type-level NodeIds.
        /// </summary>
        [Test]
        public void CreateOrReplaceChildAssignsInstanceNodeIdsByDefault()
        {
            Dictionary<string, string> files = GenerateForTestModel(generateNodeManager: false);
            string source = files.Values.Single(value =>
                value.Contains(" CreateOrReplaceRed(", StringComparison.Ordinal));
            string createOrReplace = ExtractInstanceMethodBody(source, "CreateOrReplaceRed");

            Assert.Multiple(() =>
            {
                Assert.That(createOrReplace, Does.Contain("bool assignInstanceNodeIds = true"),
                    "Assignment must be the default so hand-written node managers get it for free.");
                Assert.That(createOrReplace, Does.Contain(
                    "if (assignInstanceNodeIds && context.NodeIdFactory != null)"));
                Assert.That(createOrReplace, Does.Contain("childState.NodeId.IsNull ||"),
                    "A NodeId the caller already assigned must never be overwritten.");
                Assert.That(createOrReplace, Does.Contain(
                    "global::Opc.Ua.NodeInstanceExtensions.AssignInstanceNodeId("));
                Assert.That(createOrReplace, Does.Contain(
                    "global::Opc.Ua.NodeInstanceExtensions.AssignInstanceChildNodeIds("));
            });
        }

        /// <summary>
        /// Every FindChild override a generated type emits carries the
        /// assignment argument with its <c>true</c> default: the caller states
        /// its intent, so no second overload, capability property or context
        /// wrapper is needed to reach the override.
        /// </summary>
        [Test]
        public void GeneratedTypesEmitOneFindChildOverrideCarryingTheAssignmentFlag()
        {
            Dictionary<string, string> files = GenerateForTestModel(generateNodeManager: false);
            string source = files.Values.Single(value =>
                value.Contains(" CreateOrReplaceRed(", StringComparison.Ordinal));

            const string signature =
                "protected override global::Opc.Ua.BaseInstanceState? FindChild(";
            var parameterLists = new List<string>();
            for (int index = source.IndexOf(signature, StringComparison.Ordinal);
                index >= 0;
                index = source.IndexOf(signature, index + signature.Length, StringComparison.Ordinal))
            {
                int start = index + signature.Length;
                int end = source.IndexOf(')', start);
                parameterLists.Add(source.Substring(start, end - start));
            }

            Assert.Multiple(() =>
            {
                Assert.That(parameterLists, Is.Not.Empty,
                    "A type declaring children must resolve them in a FindChild override.");
                Assert.That(parameterLists, Has.All.Contains("bool assignInstanceNodeIds = true"),
                    "Every override must carry the argument, and repeat the default so " +
                    "callers keep the 1.5.378 behaviour.");
                Assert.That(source, Does.Not.Contain("SupportsInstanceNodeIdAssignmentControl"),
                    "Assignment control is stated per call, not advertised per type.");
                Assert.That(source, Does.Contain("return base.FindChild("),
                    "An unmatched browse name must pass the request on to the base.");
                Assert.That(source, Does.Contain(
                    "context, browseName, createOrReplace, replacement, assignInstanceNodeIds);"),
                    "The request must be forwarded verbatim to the base.");
            });
        }

        /// <summary>
        /// The generated factories build declaration subtrees whose NodeIds
        /// must stay at their type-level values - the enclosing
        /// CreateInstanceOf&lt;Type&gt; factory rebases the finished subtree in
        /// a single pass - so every factory call site opts out.
        /// </summary>
        [Test]
        public void TypeFactoriesOptOutOfCreateOrReplaceNodeIdAssignment()
        {
            Dictionary<string, string> files = GenerateForTestModel(generateNodeManager: false);
            string ex = files
                .Single(kv => kv.Key.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal))
                .Value;

            string[] callSites = [.. ex
                .Split('\n')
                .Where(line => line.Contains("state.CreateOrReplace", StringComparison.Ordinal))];

            Assert.That(callSites, Is.Not.Empty);
            Assert.That(callSites, Has.All.Contains("assignInstanceNodeIds: false"));
        }

        /// <summary>
        /// An explicit browse name marks a dynamically materialised instance,
        /// so the subtree is rebased onto per-instance NodeIds even when the
        /// caller attaches the parent itself and passes none to the factory.
        /// </summary>
        [Test]
        public void CreateInstanceOfFactoriesRebaseWithoutAnExplicitParent()
        {
            Dictionary<string, string> files = GenerateForTestModel(generateNodeManager: false);
            string ex = files
                .Single(kv => kv.Key.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal))
                .Value;

            foreach (string factory in new[]
            {
                "CreateInstanceOfRestrictedObjectType",
                "CreateInstanceOfRestrictedVariableType",
                "CreateInstanceOfRestrictedMethodType"
            })
            {
                string body = ExtractFactoryBody(ex, factory);
                Assert.That(body, Does.Contain(
                    "if (!browseName.IsNull && context.NodeIdFactory != null)"),
                    factory + " should rebase whenever a browse name is supplied.");
                Assert.That(body, Does.Not.Contain("parent != null && !browseName.IsNull"),
                    factory + " should no longer require an explicit parent to rebase.");
            }
        }

        /// <summary>
        /// Extracts the body of a generated factory method (from the line that
        /// declares it through the matching closing brace) so individual
        /// factories can be asserted on without false matches from other
        /// factories sharing token names.
        /// </summary>
        private static string ExtractFactoryBody(string source, string methodName)
        {
            string[] lines = source.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(" static ", StringComparison.Ordinal) ||
                    !lines[i].Contains(methodName + "(", StringComparison.Ordinal))
                {
                    continue;
                }

                int braces = 0;
                bool started = false;
                var sb = new StringBuilder();
                for (int j = i; j < lines.Length; j++)
                {
                    sb.AppendLine(lines[j]);
                    foreach (char c in lines[j])
                    {
                        if (c == '{')
                        {
                            braces++;
                            started = true;
                        }
                        else if (c == '}')
                        {
                            braces--;
                        }
                    }
                    if (started && braces == 0)
                    {
                        return sb.ToString();
                    }
                }
            }
            Assert.Fail("Method definition not found: " + methodName);
            return string.Empty;
        }

        private static string ExtractInstanceMethodBody(string source, string methodName)
        {
            string[] lines = source.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(" " + methodName + "(", StringComparison.Ordinal) ||
                    lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                int braces = 0;
                bool started = false;
                var sb = new StringBuilder();
                for (int j = i; j < lines.Length; j++)
                {
                    sb.AppendLine(lines[j]);
                    foreach (char c in lines[j])
                    {
                        if (c == '{')
                        {
                            braces++;
                            started = true;
                        }
                        else if (c == '}')
                        {
                            braces--;
                        }
                    }
                    if (started && braces == 0)
                    {
                        return sb.ToString();
                    }
                }
            }

            Assert.Fail("Instance method definition not found: " + methodName);
            return string.Empty;
        }

        private static Dictionary<string, string> GenerateForTestModel(
            bool generateNodeManager,
            IReadOnlyList<string> additionalNamespaceUris = null,
            string nodeManagerNamespace = null)
        {
            const string designFile = "TestModel.xml";
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var fileSystem = new VirtualFileSystem();
            string resources = Path.Combine(Directory.GetCurrentDirectory(), "Resources");

            Generators.GenerateCode(new DesignFileCollection
            {
                Targets = [Path.Combine(resources, designFile)],
                IdentifierFilePath = Path.Combine(
                    resources,
                    Path.GetFileNameWithoutExtension(designFile) + ".csv"),
                Options = new DesignFileOptions
                {
                    GenerateNodeManager = generateNodeManager,
                    NodeManagerAdditionalNamespaceUris = additionalNamespaceUris,
                    NodeManagerNamespace = nodeManagerNamespace
                }
            }, fileSystem, string.Empty, telemetry);

            return fileSystem.CreatedFiles
                .Where(c => Path.GetExtension(c) == ".cs")
                .ToDictionary(c => c, c => Encoding.UTF8.GetString(fileSystem.Get(c)));
        }

        private static int CountUnqualifiedOpcUaUses(string source)
        {
            int count = 0;
            const string needle = "Opc.Ua.";
            foreach (string line in source.Split('\n'))
            {
                string trimmed = line.TrimStart();
                // Skip XML doc comments and block-comment continuations —
                // they reference type names cosmetically, not as code.
                if (trimmed.StartsWith("///", StringComparison.Ordinal) ||
                    trimmed.StartsWith('*') ||
                    trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }
                int idx = 0;
                while ((idx = line.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
                {
                    if (idx >= 8 && line.Substring(idx - 8, 8) == "global::")
                    {
                        idx += needle.Length;
                        continue;
                    }
                    // Allow the 'Opc.Ua.SourceGeneration.Core' GeneratedCode tool name.
                    if (idx >= 1 && line[idx - 1] == '"')
                    {
                        idx += needle.Length;
                        continue;
                    }
                    count++;
                    idx += needle.Length;
                }
            }
            return count;
        }
    }
}
