#!/usr/bin/env python3
"""Builds the reduced Machinery NodeSet2 this sample source-generates.

Why a reduced set at all
------------------------
The full official Machinery nodeset (OPCFoundation/UA-Nodeset) does not survive
the model source generator - it fails with MODELGEN003 / NullReferenceException -
so every sample in this repository that needs Machinery vendors a reduced set
carrying only the types it actually uses. The pump sample's reduced set covers
the identification/nameplate types.

The Generators companion specification needs three types that the pump's reduced
set does not define:

    MachineryItemState_StateMachineType      (ns=1;i=1002)
    MachineryOperationModeStateMachineType   (ns=1;i=1008)
    MachineIdentificationType                (ns=1;i=1012)

so this script regenerates a reduced set from the official nodeset with a
whitelist covering both. Deriving it from the official source (rather than
hand-merging two reduced copies) keeps a single, checkable provenance.

It also drops the IA namespace: the only thing that referenced it was the
optional `Stacklight` member of MachineryBuildingBlocksType, which a generator
set does not have. Removing the IA URI is index-safe because it is the last
entry in NamespaceUris, so the Machinery (1) and DI (2) indices used throughout
the file are unaffected.

Usage:
    python prepare_machinery_nodeset.py <official-nodeset.xml> <output.xml>

Fetch the input from:
    https://raw.githubusercontent.com/OPCFoundation/UA-Nodeset/<ref>/Machinery/Opc.Ua.Machinery.NodeSet2.xml

Never hand-edit the output; change the whitelist and re-run.
"""

import sys
import xml.etree.ElementTree as ET

UA_NODESET_NS = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
IA_URI = "http://opcfoundation.org/UA/IA/"

# Types this sample needs generated, by Machinery NodeId. Descendants of each
# (states, transitions, properties) come along automatically.
WANTED = [
    "ns=1;i=1002",  # MachineryItemState_StateMachineType
    "ns=1;i=1003",  # IMachineryItemVendorNameplateType
    "ns=1;i=1004",  # MachineryItemIdentificationType
    "ns=1;i=1008",  # MachineryOperationModeStateMachineType
    "ns=1;i=1010",  # IMachineVendorNameplateType
    "ns=1;i=1011",  # IMachineTagNameplateType
    "ns=1;i=1012",  # MachineIdentificationType
]


def qname(tag):
    return f"{{{UA_NODESET_NS}}}{tag}"


def with_descendants(root, seeds):
    """Expands seed node ids with everything parented beneath them."""
    keep = set(seeds)
    changed = True
    while changed:
        changed = False
        for node in root:
            parent = node.get("ParentNodeId")
            nid = node.get("NodeId")
            if parent in keep and nid and nid not in keep:
                keep.add(nid)
                changed = True
    return keep


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 2

    source, target = sys.argv[1], sys.argv[2]
    ET.register_namespace("", UA_NODESET_NS)
    tree = ET.parse(source)
    root = tree.getroot()

    missing = [w for w in WANTED if not any(n.get("NodeId") == w for n in root)]
    if missing:
        print(f"ERROR: wanted node(s) not present in {source}: {', '.join(missing)}")
        return 1

    keep = with_descendants(root, WANTED)

    # Structural elements (NamespaceUris, Models, Aliases) carry no NodeId and
    # are always preserved.
    kept = dropped = 0
    for node in list(root):
        nid = node.get("NodeId")
        if nid is None:
            continue
        if nid in keep:
            kept += 1
        else:
            root.remove(node)
            dropped += 1

    # Strip references to nodes that are no longer present, so the reduced set
    # has no dangling ids for the generator to resolve.
    dangling = 0
    for node in root:
        refs = node.find(qname("References"))
        if refs is None:
            continue
        for ref in list(refs):
            target_id = (ref.text or "").strip()
            if target_id.startswith("ns=1;") and target_id not in keep:
                refs.remove(ref)
                dangling += 1

    uris = root.find(qname("NamespaceUris"))
    if uris is not None:
        entries = list(uris)
        for index, uri in enumerate(entries):
            if (uri.text or "").strip() != IA_URI:
                continue
            # NodeIds are encoded as ns=<1-based index into NamespaceUris>, so
            # removing anything but the last entry silently renumbers every
            # namespace after it and rebinds every NodeId that referenced them.
            # Fail loudly rather than emit a quietly corrupted model.
            if index != len(entries) - 1:
                print(
                    f"ERROR: {IA_URI} is entry {index + 1} of {len(entries)} in "
                    "NamespaceUris, not the last. Removing it would renumber the "
                    "namespaces that follow and rebind their NodeIds. Drop the "
                    "namespace by rewriting the affected NodeIds instead."
                )
                return 1
            uris.remove(uri)

    models = root.find(qname("Models"))
    if models is not None:
        for model in models:
            for required in list(model.findall(qname("RequiredModel"))):
                if required.get("ModelUri") == IA_URI:
                    model.remove(required)

    tree.write(target, encoding="utf-8", xml_declaration=True)
    print(f"wrote {target}: kept {kept} node(s), dropped {dropped}, "
          f"stripped {dangling} dangling reference(s), removed the IA dependency")
    return 0


if __name__ == "__main__":
    sys.exit(main())
