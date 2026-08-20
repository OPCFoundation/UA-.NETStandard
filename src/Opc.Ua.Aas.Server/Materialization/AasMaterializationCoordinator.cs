/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Aas.Server.Materialization
{
    /// <summary>
    /// Materialization states defined by the updateable AAS registry profile.
    /// </summary>
    public enum AasLoadState
    {
        /// <summary>
        /// The document is stored but has no active projection.
        /// </summary>
        Unloaded,

        /// <summary>
        /// A shadow generation is being prepared.
        /// </summary>
        Loading,

        /// <summary>
        /// The materialized nodes are serving client requests.
        /// </summary>
        Active,

        /// <summary>
        /// A newer generation has been committed.
        /// </summary>
        Superseded,

        /// <summary>
        /// The superseded generation is draining retained work.
        /// </summary>
        Retiring,

        /// <summary>
        /// The generation has been retired.
        /// </summary>
        Retired,

        /// <summary>
        /// The stored document failed validation or projection.
        /// </summary>
        Failed
    }

    /// <summary>
    /// Per-document outcome returned by the Materialize method.
    /// </summary>
    public enum AasMaterializationOutcome
    {
        /// <summary>
        /// The digest was unchanged and no projection work was performed.
        /// </summary>
        Unchanged,

        /// <summary>
        /// A shadow generation was prepared and committed.
        /// </summary>
        Materialized,

        /// <summary>
        /// The projection was retired.
        /// </summary>
        Retired,

        /// <summary>
        /// Validation or projection failed.
        /// </summary>
        Failed
    }

    /// <summary>
    /// Stored AAS document kind used for closure validation.
    /// </summary>
    public enum AasMaterializationDocumentKind
    {
        /// <summary>
        /// A shell document.
        /// </summary>
        Shell,

        /// <summary>
        /// A submodel document.
        /// </summary>
        Submodel,

        /// <summary>
        /// A concept description document.
        /// </summary>
        ConceptDescription,

        /// <summary>
        /// A whole environment document.
        /// </summary>
        Environment
    }

    /// <summary>
    /// Configurable preparation bounds for shadow generations.
    /// </summary>
    public sealed class AasMaterializationBounds
    {
        /// <summary>
        /// Gets or sets the maximum accepted document size.
        /// </summary>
        public int MaxDocumentBytes { get; set; } = 4 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum number of materialized metamodel elements.
        /// </summary>
        public int MaxElements { get; set; } = 100_000;

        /// <summary>
        /// Gets or sets the maximum nested element depth.
        /// </summary>
        public int MaxNestingDepth { get; set; } = 128;

        /// <summary>
        /// Gets or sets the maximum number of retained shadow generations.
        /// </summary>
        public int MaxShadowGenerations { get; set; } = 2;

        /// <summary>
        /// Validates the configured bounds.
        /// </summary>
        public void Validate()
        {
            EnsurePositive(MaxDocumentBytes, nameof(MaxDocumentBytes));
            EnsurePositive(MaxElements, nameof(MaxElements));
            EnsurePositive(MaxNestingDepth, nameof(MaxNestingDepth));
            EnsurePositive(MaxShadowGenerations, nameof(MaxShadowGenerations));
        }

        private static void EnsurePositive(int value, string name)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    name, value, "The configured limit must be a positive value.");
            }
        }
    }

    /// <summary>
    /// Stored registry document considered for materialization.
    /// </summary>
    public sealed class AasMaterializationDocument
    {
        /// <summary>
        /// Gets or sets the registry-relative document path.
        /// </summary>
        public string Xid { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the version the operator wants materialized.
        /// </summary>
        public string VersionId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the source identity carried by the registry.
        /// </summary>
        public string SourceIdentity { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the document kind.
        /// </summary>
        public AasMaterializationDocumentKind Kind { get; set; } = AasMaterializationDocumentKind.Environment;

        /// <summary>
        /// Gets or sets the exact stored document bytes.
        /// </summary>
        public ByteString Content { get; set; } = ByteString.Empty;

        /// <summary>
        /// Gets or sets the stored document format.
        /// </summary>
        public string Format { get; set; } = "aas/3.0+json";

        /// <summary>
        /// Gets or sets registry source identities that must be present for the closure to activate.
        /// </summary>
        public ArrayOf<string> RequiredDocumentIds { get; set; } = [];
    }

    /// <summary>
    /// Persisted materialization metadata for one document.
    /// </summary>
    public sealed class AasMaterializationDocumentState
    {
        /// <summary>
        /// Gets or sets the registry-relative document path.
        /// </summary>
        public string Xid { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the requested version.
        /// </summary>
        public string DesiredVersionId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the active version, if any.
        /// </summary>
        public string ActiveVersionId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the load state.
        /// </summary>
        public AasLoadState LoadState { get; set; }

        /// <summary>
        /// Gets or sets the materialization generation.
        /// </summary>
        public uint MaterializationGeneration { get; set; }

        /// <summary>
        /// Gets or sets the content digest that produced the active generation.
        /// </summary>
        public ByteString ContentDigest { get; set; } = ByteString.Empty;

        /// <summary>
        /// Gets or sets the root node of the active projection.
        /// </summary>
        public NodeId MaterializedNode { get; set; } = NodeId.Null;

        /// <summary>
        /// Gets or sets the diagnostic recorded for a failed state.
        /// </summary>
        public string Diagnostic { get; set; } = string.Empty;
    }

    /// <summary>
    /// Per-document Materialize method result.
    /// </summary>
    public sealed class AasMaterializationResultData
    {
        /// <summary>
        /// Gets or sets the registry-relative document path.
        /// </summary>
        public string Xid { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the materialization outcome.
        /// </summary>
        public AasMaterializationOutcome Outcome { get; set; }

        /// <summary>
        /// Gets or sets the version that is active after the operation.
        /// </summary>
        public string VersionId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the materialized node root.
        /// </summary>
        public NodeId MaterializedNode { get; set; } = NodeId.Null;

        /// <summary>
        /// Gets or sets the diagnostic, if any.
        /// </summary>
        public string Diagnostic { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request for the updateable registry Materialize method.
    /// </summary>
    public sealed class AasMaterializeRequest
    {
        /// <summary>
        /// Gets or sets the selected document xids. Empty means all documents.
        /// </summary>
        public ArrayOf<string> Targets { get; set; } = [];

        /// <summary>
        /// Gets or sets whether unchanged documents are forcibly rebuilt.
        /// </summary>
        public bool Force { get; set; }
    }

    /// <summary>
    /// Result of the updateable registry Materialize method.
    /// </summary>
    public sealed class AasMaterializeResult
    {
        /// <summary>
        /// Initializes a result.
        /// </summary>
        public AasMaterializeResult(uint generation, ArrayOf<AasMaterializationResultData> results)
        {
            Generation = generation;
            Results = results;
        }

        /// <summary>
        /// Gets the committed generation after the operation.
        /// </summary>
        public uint Generation { get; }

        /// <summary>
        /// Gets one result per considered document.
        /// </summary>
        public ArrayOf<AasMaterializationResultData> Results { get; }
    }

    /// <summary>
    /// Request to update a canonical document after a Value node write.
    /// </summary>
    public sealed class AasValueWriteBackRequest
    {
        /// <summary>
        /// Gets or sets the document that owns the value.
        /// </summary>
        public string Xid { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the element idShort path.
        /// </summary>
        public string ElementPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the written member name.
        /// </summary>
        public string MemberName { get; set; } = "Value";

        /// <summary>
        /// Gets or sets the value written by the client.
        /// </summary>
        public Variant Value { get; set; }

        /// <summary>
        /// Gets or sets the generation that produced the writing node.
        /// </summary>
        public uint SourceGeneration { get; set; }
    }

    /// <summary>
    /// Registry seam consumed by the materialization coordinator.
    /// </summary>
    public interface IAasMaterializationDocumentStore
    {
        /// <summary>
        /// Gets stored documents that should be considered.
        /// </summary>
        ValueTask<ArrayOf<AasMaterializationDocument>> GetDocumentsAsync(
            ArrayOf<string> targets,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Applies materialization metadata after a committed or failed attempt.
        /// </summary>
        ValueTask ApplyMaterializationAsync(
            ArrayOf<AasMaterializationDocumentState> states,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the stored canonical document for a value write and returns the updated document.
        /// </summary>
        ValueTask<AasMaterializationDocument> UpdateValueAsync(
            AasValueWriteBackRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Event emitted by the coordinator for committed model changes.
    /// </summary>
    public sealed class AasMaterializationModelChangeEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes an event.
        /// </summary>
        public AasMaterializationModelChangeEventArgs(
            uint generation,
            ArrayOf<AasMaterializationResultData> committedResults)
        {
            Generation = generation;
            CommittedResults = committedResults;
        }

        /// <summary>
        /// Gets the committed generation.
        /// </summary>
        public uint Generation { get; }

        /// <summary>
        /// Gets committed per-document results.
        /// </summary>
        public ArrayOf<AasMaterializationResultData> CommittedResults { get; }
    }

    /// <summary>
    /// Coordinates updateable AAS registry documents and derived projection generations.
    /// </summary>
    public sealed class AasMaterializationCoordinator : IDisposable
    {
        /// <summary>
        /// Initializes a coordinator.
        /// </summary>
        public AasMaterializationCoordinator(
            IAasMaterializationDocumentStore documentStore,
            IAasEnvironmentProjectionHost projectionHost,
            AasMaterializationBounds? bounds = null,
            AasProjectionRetirementPolicy retirementPolicy = AasProjectionRetirementPolicy.Graceful)
        {
            m_documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
            m_projectionHost = projectionHost ?? throw new ArgumentNullException(nameof(projectionHost));
            Bounds = bounds ?? new AasMaterializationBounds();
            Bounds.Validate();
            RetirementPolicy = retirementPolicy;
        }

        /// <summary>
        /// Raised only after a shadow generation has committed.
        /// </summary>
        public event EventHandler<AasMaterializationModelChangeEventArgs>? ModelChangeCommitted;

        /// <summary>
        /// Gets the current materialization generation.
        /// </summary>
        public uint Generation => m_generation;

        /// <summary>
        /// Gets preparation bounds.
        /// </summary>
        public AasMaterializationBounds Bounds { get; }

        /// <summary>
        /// Gets or sets the retirement policy used for replacement generations.
        /// </summary>
        public AasProjectionRetirementPolicy RetirementPolicy { get; set; }

        /// <summary>
        /// Materializes selected documents into an atomic generation.
        /// </summary>
        public async ValueTask<AasMaterializeResult> MaterializeAsync(
            AasMaterializeRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ArrayOf<AasMaterializationDocument> documents = await m_documentStore
                    .GetDocumentsAsync(request.Targets, cancellationToken).ConfigureAwait(false);
                var results = new List<AasMaterializationResultData>();
                var states = new List<AasMaterializationDocumentState>();
                var candidates = new List<PreparedDocument>();

                for (int i = 0; i < documents.Count; i++)
                {
                    AasMaterializationDocument document = documents[i];
                    PreparedDocument prepared = await PrepareDocumentAsync(
                        document, request.Force, cancellationToken).ConfigureAwait(false);
                    candidates.Add(prepared);
                }

                string closureError = ValidateClosure(candidates);
                if (!string.IsNullOrEmpty(closureError))
                {
                    foreach (PreparedDocument candidate in candidates)
                    {
                        AddFailure(candidate.Document, closureError, results, states);
                    }
                    await ApplyStatesAsync(states, cancellationToken).ConfigureAwait(false);
                    return new AasMaterializeResult(m_generation,                     new ArrayOf<AasMaterializationResultData>(results.ToArray()));
                }

                uint candidateGeneration = m_generation + 1;
                bool committed = false;
                foreach (PreparedDocument candidate in candidates)
                {
                    if (!string.IsNullOrEmpty(candidate.Failure))
                    {
                        AddFailure(candidate.Document, candidate.Failure, results, states);
                        continue;
                    }
                    if (candidate.Unchanged && !request.Force)
                    {
                        AddUnchanged(candidate.Document, results, states);
                        continue;
                    }

                    AasEnvironmentProjectionHandle handle = await CommitAsync(
                        candidate.Document.Xid,
                        candidate.Environment!,
                        cancellationToken).ConfigureAwait(false);
                    m_active[candidate.Document.Xid] = new ActiveProjection(
                        candidate.Document.VersionId,
                        candidate.Digest,
                        candidateGeneration,
                        handle,
                        candidate.Environment!);
                    committed = true;
                    var result = new AasMaterializationResultData
                    {
                        Xid = candidate.Document.Xid,
                        Outcome = AasMaterializationOutcome.Materialized,
                        VersionId = candidate.Document.VersionId,
                        MaterializedNode = RootNodeId(candidate.Environment!),
                        Diagnostic = string.Empty
                    };
                    results.Add(result);
                    states.Add(State(
                        candidate.Document,
                        AasLoadState.Active,
                        candidateGeneration,
                        candidate.Digest,
                        result.MaterializedNode,
                        string.Empty,
                        candidate.Document.VersionId));
                }

                if (committed)
                {
                    m_generation = candidateGeneration;
                }

                await ApplyStatesAsync(states, cancellationToken).ConfigureAwait(false);
                ArrayOf<AasMaterializationResultData> resultArray = new(results.ToArray());
                if (committed)
                {
                    ModelChangeCommitted?.Invoke(
                        this,
                        new AasMaterializationModelChangeEventArgs(m_generation, Committed(resultArray)));
                }
                return new AasMaterializeResult(m_generation, resultArray);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <summary>
        /// Updates the canonical document for a value write without refreshing the generation that produced it.
        /// </summary>
        public async ValueTask<AasMaterializationDocument> WriteBackValueAsync(
            AasValueWriteBackRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                AasMaterializationDocument updated = await m_documentStore
                    .UpdateValueAsync(request, cancellationToken).ConfigureAwait(false);
                if (m_active.TryGetValue(request.Xid, out ActiveProjection? active) &&
                    active.Generation == request.SourceGeneration)
                {
                    active.SuppressNextDigest = Digest(updated.Content);
                }
                return updated;
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <summary>
        /// Releases coordinator resources.
        /// </summary>
        public void Dispose()
        {
            m_mutex.Dispose();
        }

        private async ValueTask<PreparedDocument> PrepareDocumentAsync(
            AasMaterializationDocument document,
            bool force,
            CancellationToken cancellationToken)
        {
            if (document.Content.IsNull)
            {
                return PreparedDocument.Fail(document, ByteString.Empty, "The stored document is empty.");
            }
            if (document.Content.Length > Bounds.MaxDocumentBytes)
            {
                return PreparedDocument.Fail(
                    document,
                    Digest(document.Content),
                    "The document exceeds the configured materialization size bound.");
            }

            ByteString digest = Digest(document.Content);
            if (m_active.TryGetValue(document.Xid, out ActiveProjection? active))
            {
                if (!force && active.Digest.Equals(digest))
                {
                    return PreparedDocument.UnchangedDocument(document, digest, active.Environment);
                }
                if (!force && active.SuppressNextDigest.Equals(digest))
                {
                    active.Digest = digest;
                    active.SuppressNextDigest = ByteString.Empty;
                    return PreparedDocument.UnchangedDocument(document, digest, active.Environment);
                }
            }

            try
            {
                using var stream = new MemoryStream(document.Content.Memory.ToArray(), writable: false);
                AasDocumentReadResult read = await new AasJsonReader().ReadAsync(stream, cancellationToken)
                    .ConfigureAwait(false);
                if (!read.Succeeded || read.Environment is null)
                {
                    return PreparedDocument.Fail(document, digest, read.Error ?? "The AAS document is invalid.");
                }

                string? boundDiagnostic = ValidateBounds(read.Environment);
                if (!string.IsNullOrEmpty(boundDiagnostic))
                {
                    return PreparedDocument.Fail(document, digest, boundDiagnostic);
                }

                AasMaterializationResult materialized = AasEnvironmentMaterializer.Materialize(read.Environment);
                if (materialized.HasErrors)
                {
                    return PreparedDocument.Fail(
                        document,
                        digest,
                        string.Join("; ", materialized.Diagnostics.Select(d => d.Message)));
                }

                return PreparedDocument.Ready(document, digest, read.Environment);
            }
            catch (InvalidOperationException ex)
            {
                return PreparedDocument.Fail(document, digest, ex.Message);
            }
        }

        private async ValueTask<AasEnvironmentProjectionHandle> CommitAsync(
            string xid,
            AasEnvironment environment,
            CancellationToken cancellationToken)
        {
            var valueProvider = new DocumentAasValueProvider(new ArrayOf<AasEnvironment>(new[] { environment }));
            var operationHandler = new DefaultAasOperationHandler();
            if (!m_active.TryGetValue(xid, out ActiveProjection? active))
            {
                return await m_projectionHost
                    .AddAsync(environment, valueProvider, operationHandler, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (RetirementPolicy == AasProjectionRetirementPolicy.Immediate)
            {
                return await m_projectionHost
                    .ImmediateReloadAsync(active.Handle, environment, valueProvider, operationHandler, cancellationToken)
                    .ConfigureAwait(false);
            }
            return await m_projectionHost
                .ShadowReloadAsync(active.Handle, environment, valueProvider, operationHandler, cancellationToken)
                .ConfigureAwait(false);
        }

        private string? ValidateBounds(AasEnvironment environment)
        {
            int count = 0;
            int depth = 0;
            if (environment.AssetAdministrationShells.IsPresent)
            {
                count += environment.AssetAdministrationShells.Value.Count;
            }
            if (environment.ConceptDescriptions.IsPresent)
            {
                count += environment.ConceptDescriptions.Value.Count;
            }
            if (environment.Submodels.IsPresent)
            {
                count += environment.Submodels.Value.Count;
                foreach (AasSubmodel submodel in environment.Submodels.Value)
                {
                    if (submodel.SubmodelElements.IsPresent)
                    {
                        CountElements(submodel.SubmodelElements.Value, 1, ref count, ref depth);
                    }
                }
            }
            if (count > Bounds.MaxElements)
            {
                return "The environment exceeds the configured materialization element bound.";
            }
            if (depth > Bounds.MaxNestingDepth)
            {
                return "The environment exceeds the configured materialization nesting bound.";
            }
            return null;
        }

        private void CountElements(ArrayOf<AasSubmodelElement> elements, int currentDepth, ref int count, ref int depth)
        {
            depth = Math.Max(depth, currentDepth);
            count += elements.Count;
            foreach (AasSubmodelElement element in elements)
            {
                if (element is AasSubmodelElementCollection collection && collection.Value.IsPresent)
                {
                    CountElements(collection.Value.Value, currentDepth + 1, ref count, ref depth);
                }
                if (element is AasSubmodelElementList list && list.Value.IsPresent)
                {
                    CountElements(list.Value.Value, currentDepth + 1, ref count, ref depth);
                }
            }
        }

        private string ValidateClosure(List<PreparedDocument> candidates)
        {
            var submodels = new HashSet<string>(StringComparer.Ordinal);
            var concepts = new HashSet<string>(StringComparer.Ordinal);
            var sourceIdentities = new HashSet<string>(StringComparer.Ordinal);
            foreach (PreparedDocument candidate in candidates)
            {
                sourceIdentities.Add(candidate.Document.SourceIdentity);
                AasEnvironment? environment = candidate.Environment;
                if (environment is null)
                {
                    continue;
                }
                if (environment.Submodels.IsPresent)
                {
                    foreach (AasSubmodel submodel in environment.Submodels.Value)
                    {
                        submodels.Add(submodel.Id);
                    }
                }
                if (environment.ConceptDescriptions.IsPresent)
                {
                    foreach (AasConceptDescription concept in environment.ConceptDescriptions.Value)
                    {
                        concepts.Add(concept.Id);
                    }
                }
            }

            foreach (PreparedDocument candidate in candidates)
            {
                for (int i = 0; i < candidate.Document.RequiredDocumentIds.Count; i++)
                {
                    string required = candidate.Document.RequiredDocumentIds[i];
                    if (!sourceIdentities.Contains(required))
                    {
                        return "The closure references missing document '" + required + "'.";
                    }
                }
            }

            string cycle = FindDependencyCycle(candidates);
            if (!string.IsNullOrEmpty(cycle))
            {
                return "The closure dependency graph contains a cycle: " + cycle + ".";
            }

            foreach (PreparedDocument candidate in candidates)
            {
                AasEnvironment? environment = candidate.Environment;
                if (environment is null)
                {
                    continue;
                }
                if (environment.AssetAdministrationShells.IsPresent)
                {
                    foreach (AasShell shell in environment.AssetAdministrationShells.Value)
                    {
                        if (shell.SubmodelReferences.IsPresent)
                        {
                            foreach (AASReferenceDataType reference in shell.SubmodelReferences.Value)
                            {
                                string id = LastKeyValue(reference);
                                if (!string.IsNullOrEmpty(id) && !submodels.Contains(id))
                                {
                                    return "The closure references missing submodel '" + id + "'.";
                                }
                            }
                        }
                    }
                }
                if (environment.Submodels.IsPresent)
                {
                    foreach (AasSubmodel submodel in environment.Submodels.Value)
                    {
                        string id = submodel.SemanticId.IsPresent ? LastKeyValue(submodel.SemanticId.Value) : string.Empty;
                        if (!string.IsNullOrEmpty(id) && !concepts.Contains(id))
                        {
                            return "The closure references missing concept description '" + id + "'.";
                        }
                    }
                }
            }
            return string.Empty;
        }

        private static string FindDependencyCycle(List<PreparedDocument> candidates)
        {
            var dependencies = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (PreparedDocument candidate in candidates)
            {
                string source = NodeOf(candidate);
                if (!dependencies.ContainsKey(source))
                {
                    dependencies[source] = new SortedSet<string>(StringComparer.Ordinal);
                }
            }

            // The edges are added only once every node exists, because an edge
            // to a document that appears later in the closure would otherwise
            // be dropped and the cycle it closes would go unreported.
            foreach (PreparedDocument candidate in candidates)
            {
                SortedSet<string> edges = dependencies[NodeOf(candidate)];
                for (int i = 0; i < candidate.Document.RequiredDocumentIds.Count; i++)
                {
                    string required = candidate.Document.RequiredDocumentIds[i];
                    if (dependencies.ContainsKey(required))
                    {
                        edges.Add(required);
                    }
                }
            }

            var completed = new HashSet<string>(StringComparer.Ordinal);
            foreach (string start in dependencies.Keys)
            {
                if (completed.Contains(start))
                {
                    continue;
                }

                string cycle = FindDependencyCycleFrom(start, dependencies, completed);
                if (!string.IsNullOrEmpty(cycle))
                {
                    return cycle;
                }
            }
            return string.Empty;
        }

        private static string NodeOf(PreparedDocument candidate)
        {
            string source = candidate.Document.SourceIdentity;
            return string.IsNullOrEmpty(source) ? candidate.Document.Xid : source;
        }

        private static string FindDependencyCycleFrom(
            string start,
            SortedDictionary<string, SortedSet<string>> dependencies,
            HashSet<string> completed)
        {
            var path = new List<string>();
            var pathIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var stack = new Stack<DependencyFrame>();
            stack.Push(new DependencyFrame(start, dependencies[start]));

            while (stack.Count > 0)
            {
                DependencyFrame frame = stack.Peek();
                if (!pathIndex.ContainsKey(frame.Node))
                {
                    pathIndex[frame.Node] = path.Count;
                    path.Add(frame.Node);
                }

                if (frame.TryMoveNext(out string next))
                {
                    if (completed.Contains(next))
                    {
                        continue;
                    }
                    if (pathIndex.TryGetValue(next, out int index))
                    {
                        var cycle = new List<string>();
                        for (int i = index; i < path.Count; i++)
                        {
                            cycle.Add(path[i]);
                        }
                        cycle.Add(next);
                        return string.Join(" -> ", cycle);
                    }
                    stack.Push(new DependencyFrame(next, dependencies[next]));
                    continue;
                }

                stack.Pop();
                pathIndex.Remove(frame.Node);
                path.RemoveAt(path.Count - 1);
                completed.Add(frame.Node);
            }
            return string.Empty;
        }

        private static string LastKeyValue(AASReferenceDataType reference)
        {
            if (reference.Keys.IsNull || reference.Keys.Count == 0)
            {
                return string.Empty;
            }
            return reference.Keys[reference.Keys.Count - 1].Value ?? string.Empty;
        }

        private void AddFailure(
            AasMaterializationDocument document,
            string diagnostic,
            List<AasMaterializationResultData> results,
            List<AasMaterializationDocumentState> states)
        {
            string activeVersion = m_active.TryGetValue(document.Xid, out ActiveProjection? active)
                ? active.VersionId
                : string.Empty;
            results.Add(new AasMaterializationResultData
            {
                Xid = document.Xid,
                Outcome = AasMaterializationOutcome.Failed,
                VersionId = activeVersion,
                MaterializedNode = NodeId.Null,
                Diagnostic = diagnostic
            });
            states.Add(State(
                document,
                AasLoadState.Failed,
                m_generation,
                ByteString.Empty,
                NodeId.Null,
                diagnostic,
                activeVersion));
        }

        private void AddUnchanged(
            AasMaterializationDocument document,
            List<AasMaterializationResultData> results,
            List<AasMaterializationDocumentState> states)
        {
            ActiveProjection active = m_active[document.Xid];
            NodeId root = RootNodeId(active.Environment);
            results.Add(new AasMaterializationResultData
            {
                Xid = document.Xid,
                Outcome = AasMaterializationOutcome.Unchanged,
                VersionId = active.VersionId,
                MaterializedNode = root,
                Diagnostic = "Content digest unchanged."
            });
            states.Add(State(
                document,
                AasLoadState.Active,
                active.Generation,
                active.Digest,
                root,
                string.Empty,
                active.VersionId));
        }

        private static AasMaterializationDocumentState State(
            AasMaterializationDocument document,
            AasLoadState loadState,
            uint generation,
            ByteString digest,
            NodeId root,
            string diagnostic,
            string activeVersionId)
        {
            return new AasMaterializationDocumentState
            {
                Xid = document.Xid,
                DesiredVersionId = document.VersionId,
                ActiveVersionId = activeVersionId,
                LoadState = loadState,
                MaterializationGeneration = generation,
                ContentDigest = digest,
                MaterializedNode = root,
                Diagnostic = diagnostic ?? string.Empty
            };
        }

        private async ValueTask ApplyStatesAsync(
            List<AasMaterializationDocumentState> states,
            CancellationToken cancellationToken)
        {
            if (states.Count > 0)
            {
                await m_documentStore.ApplyMaterializationAsync(
                    new ArrayOf<AasMaterializationDocumentState>(states.ToArray()),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private static ArrayOf<AasMaterializationResultData> Committed(
            ArrayOf<AasMaterializationResultData> results)
        {
            var committed = new List<AasMaterializationResultData>();
            for (int i = 0; i < results.Count; i++)
            {
                AasMaterializationResultData result = results[i];
                if (result.Outcome == AasMaterializationOutcome.Materialized)
                {
                    committed.Add(result);
                }
            }
            return new ArrayOf<AasMaterializationResultData>(committed.ToArray());
        }

        private static NodeId RootNodeId(AasEnvironment environment)
        {
            return new NodeId("i4aas3:Environment", 1);
        }

        private static ByteString Digest(ByteString content)
        {
            return Opc.Ua.Aas.Server.Registry.AasRegistryContentDigest.Compute(content);
        }

        private readonly record struct PreparedDocument(
            AasMaterializationDocument Document,
            ByteString Digest,
            AasEnvironment? Environment,
            string Failure,
            bool Unchanged)
        {
            public static PreparedDocument Fail(
                AasMaterializationDocument document,
                ByteString digest,
                string failure)
            {
                return new PreparedDocument(document, digest, null, failure ?? string.Empty, false);
            }

            public static PreparedDocument Ready(
                AasMaterializationDocument document,
                ByteString digest,
                AasEnvironment environment)
            {
                return new PreparedDocument(document, digest, environment, string.Empty, false);
            }

            public static PreparedDocument UnchangedDocument(
                AasMaterializationDocument document,
                ByteString digest,
                AasEnvironment environment)
            {
                return new PreparedDocument(document, digest, environment, string.Empty, true);
            }
        }

        private sealed class ActiveProjection
        {
            public ActiveProjection(
                string versionId,
                ByteString digest,
                uint generation,
                AasEnvironmentProjectionHandle handle,
                AasEnvironment environment)
            {
                VersionId = versionId ?? string.Empty;
                Digest = digest;
                Generation = generation;
                Handle = handle ?? throw new ArgumentNullException(nameof(handle));
                Environment = environment ?? throw new ArgumentNullException(nameof(environment));
            }

            public string VersionId { get; set; }
            public ByteString Digest { get; set; }
            public uint Generation { get; }
            public AasEnvironmentProjectionHandle Handle { get; }
            public AasEnvironment Environment { get; }
            public ByteString SuppressNextDigest { get; set; } = ByteString.Empty;
        }

        private sealed class DependencyFrame
        {
            public DependencyFrame(string node, SortedSet<string> dependencies)
            {
                Node = node;
                m_dependencies = new List<string>(dependencies);
            }

            public string Node { get; }

            public bool TryMoveNext(out string next)
            {
                if (m_index >= m_dependencies.Count)
                {
                    next = string.Empty;
                    return false;
                }
                next = m_dependencies[m_index];
                m_index++;
                return true;
            }

            private readonly List<string> m_dependencies;
            private int m_index;
        }

        private readonly IAasMaterializationDocumentStore m_documentStore;
        private readonly IAasEnvironmentProjectionHost m_projectionHost;
        private readonly SemaphoreSlim m_mutex = new(1, 1);
        private readonly Dictionary<string, ActiveProjection> m_active = new(StringComparer.Ordinal);
        private uint m_generation;
    }
}
