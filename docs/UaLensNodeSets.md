# Exploring NodeSet2 files in UaLens

Choose **File > Open NodeSet2 files...** or **Open NodeSet2 files...** in the
welcome area. Select one or more XML files. Lens imports their nodes into one
read-only address space and reuses the existing tree, attributes and references
panels. No OPC UA server, listening endpoint or client session is started.

The OPC UA core model shipped with the stack is bundled with Lens. A model
that depends only on that core model can be opened without internet access.

## Missing dependencies

Lens resolves dependencies by model URI, not by filename or namespace index.
Files selected together can refer to one another even when their namespace
tables have different orders. Lens also looks for matching XML files beside
the selected files.

For a missing dependency, the dialog shows its required model URI and revision
metadata. Choose **Browse local file** to select the dependency,
or **Search and download** to permit access to
[OPCFoundation/UA-Nodeset](https://github.com/OPCFoundation/UA-Nodeset).
The lookup uses the models actually published in that repository; an
`opcfoundation.org` namespace alone is not proof that a file is available.
No local model contents are uploaded.

If the repository has no matching model, Lens opens a file picker for that
dependency. Repository failures are reported explicitly and also allow a local
file selection. A file for the wrong model or an insufficient model revision
does not satisfy the dependency. Dependencies can themselves require other
models, so more than one selection may be necessary.

Revision checks use `ModelVersion` and `PublicationDate` as defined in Annex F.
The human-readable `Version` field is a label, not a version number to order.
Files defining complementary parts of the same model revision can be selected
together; conflicting revisions or duplicate NodeIds are rejected.

Imports are prepared before the address space is replaced. Cancelling file
selection or dependency resolution, or encountering an invalid model, leaves
the previous address space in place. Once the complete graph is ready, Lens
closes the primary live connection and switches to the offline graph. Existing
tool documents retain their configuration but cannot run session-bound actions.

Downloads are used in memory; Lens does not overwrite the selected files.
An import is limited to 128 documents, 500,000 nodes, 64 dependency levels and
256 MiB of document content. Each document is limited to 64 MiB. Oversized
documents or catalogs fail explicitly rather than producing a partial graph.

## Invalid files

Import errors appear in the error banner and the **Log** panel. Diagnostics,
Log and the current address space remain usable after a failure. Correct the
file and open it again; a successful import clears the previous error.

A name used in place of a NodeId must be declared in that document's
`<Aliases>` table, even for standard names such as `HasProperty`:

```xml
<Aliases>
  <Alias Alias="HasProperty">i=46</Alias>
</Aliases>
```

Alternatively, use the NodeId directly, for example
`<Reference ReferenceType="i=46">ns=1;i=2</Reference>`.
Lens does not infer undeclared aliases or rewrite the source XML. If a
generator emits names without declarations, correct its output.

## Exploring the graph

The offline banner reports document, node and unresolved-reference counts.
Its tooltip lists source documents and model URIs. Namespace indexes in the
explorer belong to the combined graph, not necessarily to the original files.

Select an index and URI in the **Namespace** dropdown to highlight nodes from
that namespace. The highlight follows the combined NodeId namespace index,
including for children expanded later; it does not hide other namespaces or
change the selected node. Choose **None (no highlighting)** to clear it.
The same picker uses the server's namespace table in live mode.

Use the address-space **View** selector for Objects, ObjectTypes, VariableTypes,
DataTypes, ReferenceTypes or Views. Offline type browsing includes type members,
not just subtypes. **AllNodes** groups every imported node by namespace,
including nodes that are not reachable from the standard Root folder.

In offline mode, the search box searches all imported display names, browse
names and NodeIds, including unexpanded branches. **Enter** starts a search;
**F3** advances to the next match. A match is added to the tree as an inspection
root when necessary. Double-click a reference target to inspect a locally
resolved node.

The attributes panel shows imported metadata and values, not live values.
The references panel includes forward and inverse links, including reverse
links reconstructed from references declared only at the other endpoint.
Missing targets remain visible as unresolved references rather than being
silently removed.

**Find by path...** resolves OPC UA relative paths against the imported graph.
Use the combined namespace indexes, for example `/2:Machine/3:Reading`.
**View NodeState...** shows attributes and references and provides a
**NodeSet2 XML** branch containing the complete authored node definition,
including data-type fields, values and XML extensions. That XML retains the
source file's namespace table and aliases.

Write, Call, Monitor, Events and History actions are unavailable for offline
nodes. Imported access flags describe the model; they do not grant permission
to modify a server.

## Returning to live mode and persistence

**Close models** clears the offline graph. Connecting successfully to a server
also replaces it with the live address space. A cancelled or failed connection
attempt does not discard the offline graph.

Offline model selections and downloaded documents are not serialized into a
workspace file. Reopen the XML files after restarting Lens. Saving a workspace
continues to save tool configuration and connection policy, not model contents.

This is an information-model explorer, not an OPC UA server simulator or a
conformance validator. Values and executable/access flags are static metadata.
Remote-server reference targets cannot be followed offline.

The model-file format and namespace/reference mapping follow
[OPC UA Part 6, Annex F](https://reference.opcfoundation.org/Core/Part6/v105/docs/F).
