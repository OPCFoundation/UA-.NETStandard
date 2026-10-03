// node-opcua interop peer: same CLI / stdout contract as tests/Opc.Ua.Interop.LegacyPeer.
//   server --port <p> --pki <dir> [--kind interop] [--autoaccept true|false] [--init-only true]
//   client --url <u> --pki <dir> [--policy <uri>] [--mode <m>] [--user u --password p]
//          [--checks a,b] [--expect-connect-error A,B] [--timeout-seconds N]
import path from "node:path";
import readline from "node:readline";
import { createRequire } from "node:module";
import {
    OPCUAServer, OPCUAClient, OPCUACertificateManager, DataType, Variant, StatusCodes, SecurityPolicy,
    MessageSecurityMode, UserTokenType, AttributeIds, NodeId, NodeClass, ServerState, ClientSubscription,
    ClientMonitoredItem, TimestampsToReturn, BrowseDirection, makeBrowsePath, coerceNodeId, LocalizedText,
    QualifiedName, resolveNodeId, BinaryStream, VariantArrayType
} from "node-opcua";

const require = createRequire(import.meta.url);
const VERSION = require("node-opcua/package.json").version;
const INTEROP_NAMESPACE = "urn:opcfoundation.org:interop:legacy";
const REFERENCE_NAMESPACE = "http://opcfoundation.org/Quickstarts/ReferenceServer";
const USER_NAME = "interop", PASSWORD = "interop-password";
const [EXIT_SUCCESS, EXIT_CHECKS_FAILED, EXIT_USAGE, EXIT_FATAL] = [0, 1, 2, 3];

function parseOptions(args) {
    const options = {};
    for (let ii = 0; ii < args.length; ii += 2) {
        if (!args[ii].startsWith("--") || ii + 1 >= args.length) throw new Error("expected --name value, got " + args[ii]);
        options[args[ii].substring(2).toLowerCase()] = args[ii + 1];
    }
    return options;
}
const flag = (o, n, d) => (n in o ? o[n].toLowerCase() === "true" : d);

// The decoders' size limits are process-wide statics. The server mirrors the
// 1.5.378 interop server (InteropLimits); the client mirrors the 2.0
// reference server fixture so the large-message checks are bounded by the server.
function setDecodingLimits(maxArrayLength, maxByteStringLength) {
    BinaryStream.maxArrayLength = maxArrayLength;
    Variant.maxArrayLength = maxArrayLength;
    Variant.maxTypedArrayLength = maxArrayLength; // Int32[] and other typed arrays have their own cap
    BinaryStream.maxByteStringLength = maxByteStringLength;
    BinaryStream.maxStringLength = maxByteStringLength;
}

// node-opcua's PKI layout is <root>/{own,trusted,rejected,issuers}; the .NET suite uses "issuer".
async function certificateManager(o) {
    const cm = new OPCUACertificateManager({
        rootFolder: o.pki,
        automaticallyAcceptUnknownCertificate: flag(o, "autoaccept", true)
    });
    await cm.initialize();
    return cm;
}

// ----------------------------------------------------------------------------- server

async function runServer(o) {
    if ((o.kind ?? "interop") !== "interop") throw new Error("--kind " + o.kind + " is not supported by the node-opcua peer");
    setDecodingLimits(100_000, 1024 * 1024);
    const port = parseInt(o.port, 10);
    const name = "NodeOpcuaInteropServer";
    const applicationUri = `urn:localhost:opcfoundation.org:${name}`;
    const serverCertificateManager = await certificateManager(o);
    const server = new OPCUAServer({
        port,
        resourcePath: "/" + name,
        serverCertificateManager,
        serverInfo: { applicationUri, productUri: "http://opcfoundation.org/UA/Interop/NodeOpcuaServer", applicationName: { text: name } },
        buildInfo: { productName: "node-opcua Interop Server", manufacturerName: "Sterfive", softwareVersion: "node-opcua " + VERSION, buildNumber: "0", buildDate: new Date() },
        securityPolicies: [SecurityPolicy.None, SecurityPolicy.Basic256Sha256, SecurityPolicy.Aes128_Sha256_RsaOaep, SecurityPolicy.Aes256_Sha256_RsaPss],
        securityModes: [MessageSecurityMode.None, MessageSecurityMode.Sign, MessageSecurityMode.SignAndEncrypt],
        allowAnonymous: true,
        userManager: { isValidUser: (user, password) => user === USER_NAME && password === PASSWORD },
        serverCapabilities: { operationLimits: { maxNodesPerRead: 100, maxNodesPerWrite: 100, maxNodesPerBrowse: 100, maxNodesPerMethodCall: 100 } }
    });
    await server.initialize();
    if (flag(o, "init-only", false)) {
        console.log("PEER-PKI-READY " + applicationUri);
        return EXIT_SUCCESS;
    }
    const addressSpace = server.engine.addressSpace;
    const ns = addressSpace.registerNamespace(INTEROP_NAMESPACE);
    const folder = ns.addFolder(addressSpace.rootFolder.objects, { nodeId: "s=Interop", browseName: "Interop" });
    const rw = { accessLevel: "CurrentRead | CurrentWrite", userAccessLevel: "CurrentRead | CurrentWrite" };
    const variable = (browseName, dataType, value, extra = {}) => ns.addVariable({
        organizedBy: folder, nodeId: "s=" + browseName, browseName, dataType, minimumSamplingInterval: 100, ...rw, ...extra,
        value: new Variant({ dataType: extra.variantType ?? dataType, arrayType: extra.valueRank === 1 ? 1 : 0, value })
    });
    variable("Boolean", DataType.Boolean, true);
    variable("Int32", DataType.Int32, 42);
    variable("UInt64", DataType.UInt64, [0xffffffff, 0xffffffff]);
    variable("Double", DataType.Double, 3.25);
    variable("String", DataType.String, "legacy");
    variable("DateTime", DataType.DateTime, new Date(Date.UTC(2024, 0, 2, 3, 4, 5)));
    variable("Guid", DataType.Guid, "8D2B5C6E-6C0E-4A4C-9F2B-0B6A3A6B1E01");
    variable("ByteString", DataType.ByteString, Buffer.from([1, 2, 3, 4, 5]));
    variable("LocalizedText", DataType.LocalizedText, new LocalizedText({ locale: "de", text: "Hallo" }));
    variable("QualifiedName", DataType.QualifiedName, new QualifiedName({ name: "Name", namespaceIndex: ns.index }));
    variable("NodeId", DataType.NodeId, coerceNodeId("s=Interop", ns.index));
    variable("Int32Array", DataType.Int32, new Int32Array([1, 2, 3]), { valueRank: 1, arrayDimensions: [0] });
    variable("StringArray", DataType.String, ["a", "b", "c"], { valueRank: 1, arrayDimensions: [0] });
    const rangeType = addressSpace.findDataType("Range");
    variable("Range", rangeType, addressSpace.constructExtensionObject(rangeType, { low: 0, high: 100 }), { variantType: DataType.ExtensionObject });
    const counter = variable("Counter", DataType.Int32, 0, { accessLevel: "CurrentRead", userAccessLevel: "CurrentRead" });
    const add = ns.addMethod(folder, {
        nodeId: "s=Add", browseName: "Add",
        inputArguments: [{ name: "a", dataType: DataType.Int32 }, { name: "b", dataType: DataType.Int32 }],
        outputArguments: [{ name: "sum", dataType: DataType.Int32 }]
    });
    add.bindMethod(async (inputs, context) => ({
        statusCode: StatusCodes.Good,
        outputArguments: [{ dataType: DataType.Int32, value: inputs[0].value + inputs[1].value }]
    }));
    let count = 0;
    const timer = setInterval(() => counter.setValueFromSource({ dataType: DataType.Int32, value: ++count }), 100);

    await server.start();
    const url = `opc.tcp://localhost:${port}/${name}`;
    console.log("PEER-INFO " + JSON.stringify({ stack: "node-opcua", version: VERSION, applicationUri, softwareVersion: "node-opcua " + VERSION }));
    console.log("PEER-SERVER-READY " + url);
    console.log("LEGACY-SERVER-READY " + url);
    await new Promise((resolve) => {
        const rl = readline.createInterface({ input: process.stdin });
        rl.on("line", (line) => { if (line.trim().toLowerCase() === "stop") { rl.close(); } });
        rl.on("close", resolve);
    });
    clearInterval(timer);
    await server.shutdown(0);
    console.log("PEER-SERVER-STOPPED");
    return EXIT_SUCCESS;
}

// ----------------------------------------------------------------------------- client

const require_ = (c, m) => { if (!c) throw new Error(m); };

class CheckRunner {
    passed = 0; failed = 0;
    async run(check, fn) {
        const started = Date.now();
        let outcome = "Passed", message = "";
        try { await fn(); this.passed++; } catch (e) { this.failed++; outcome = "Failed"; message = `${e.name ?? "Error"} ${e.message}`; }
        console.log("RESULT " + JSON.stringify({ check, outcome, message, milliseconds: Date.now() - started }));
        return outcome === "Passed";
    }
    finish() { console.log(`SUMMARY passed=${this.passed} failed=${this.failed}`); return this.failed === 0 ? EXIT_SUCCESS : EXIT_CHECKS_FAILED; }
}

const CHECKS = {
    async NamespaceArray(c) {
        require_(c.ns > 0, "reference server namespace is missing");
    },
    async ReadServerStatusStructure(c) {
        const dv = await c.session.read({ nodeId: resolveNodeId("Server_ServerStatus"), attributeId: AttributeIds.Value });
        require_(dv.statusCode.isGood(), "status " + dv.statusCode.toString());
        const status = dv.value.value;
        require_(status?.state === ServerState.Running, "server state " + status?.state);
        require_(!!status.buildInfo?.productUri, "BuildInfo is empty");
    },
    async BrowseObjectsFolder(c) {
        const result = await c.session.browse("ObjectsFolder");
        const names = result.references.map((r) => r.browseName.name);
        require_(names.includes("Server"), "Server object not found");
        require_(names.includes("CTT"), "CTT folder not found: " + names.join(", "));
    },
    async TranslateBrowsePath(c) {
        const r = await c.session.translateBrowsePath(makeBrowsePath("ObjectsFolder", "/Server/ServerStatus/CurrentTime"));
        require_(r.statusCode.isGood(), "status " + r.statusCode.toString());
        require_(r.targets[0].targetId.toString() === resolveNodeId("Server_ServerStatus_CurrentTime").toString(), "target " + r.targets[0].targetId.toString());
    },
    async ReadScalars(c) {
        const names = ["Boolean", "Int32", "Double", "String", "DateTime", "Guid", "ByteString", "LocalizedText", "QualifiedName", "NodeId", "Arrays_Int32", "Arrays_String"];
        const values = await c.session.read(names.map((n) => ({ nodeId: c.id("Scalar_Static_" + n), attributeId: AttributeIds.Value })));
        values.forEach((dv, ii) => require_(dv.statusCode.isGood(), `${names[ii]}: ${dv.statusCode.toString()}`));
    },
    async ReadAttributes(c) {
        const nodeId = c.id("Scalar_Static_Int32");
        const [nc, bn, dt] = await c.session.read([AttributeIds.NodeClass, AttributeIds.BrowseName, AttributeIds.DataType].map((attributeId) => ({ nodeId, attributeId })));
        require_(nc.value.value === NodeClass.Variable, "NodeClass " + nc.value.value);
        require_(bn.value.value.name === "Scalar_Static_Int32", "BrowseName " + bn.value.value.toString());
        require_(dt.value.value.toString() === resolveNodeId("Int32").toString(), "DataType " + dt.value.value.toString());
    },
    async WriteAndReadBack(c) {
        const text = "written by node-opcua äöü";
        const doubles = [1.5, -2.25, 1e300];
        const writes = [
            { nodeId: c.id("Scalar_Static_Int32"), attributeId: AttributeIds.Value, value: { value: { dataType: DataType.Int32, value: 1234567 } } },
            { nodeId: c.id("Scalar_Static_String"), attributeId: AttributeIds.Value, value: { value: { dataType: DataType.String, value: text } } },
            { nodeId: c.id("Scalar_Static_Arrays_Double"), attributeId: AttributeIds.Value, value: { value: { dataType: DataType.Double, arrayType: 1, value: new Float64Array(doubles) } } }
        ];
        const results = await c.session.write(writes);
        results.forEach((s, ii) => require_(s.isGood(), `${writes[ii].nodeId}: ${s.toString()}`));
        const read = await c.session.read(writes.map((w) => ({ nodeId: w.nodeId, attributeId: AttributeIds.Value })));
        require_(read[0].value.value === 1234567, "Int32 read back " + read[0].value.value);
        require_(read[1].value.value === text, "String read back " + read[1].value.value);
        require_(Array.from(read[2].value.value).join() === doubles.join(), "Double[] read back " + Array.from(read[2].value.value));
    },
    async CallMethods(c) {
        const objectId = c.id("Methods");
        const [hello, add] = await c.session.call([
            { objectId, methodId: c.id("Methods_Hello"), inputArguments: [{ dataType: DataType.String, value: "node-opcua" }] },
            { objectId, methodId: c.id("Methods_Add"), inputArguments: [{ dataType: DataType.Float, value: 1.5 }, { dataType: DataType.UInt32, value: 2 }] }
        ]);
        require_(hello.statusCode.isGood(), "Hello " + hello.statusCode.toString());
        require_(hello.outputArguments[0].value === "hello node-opcua", "Hello returned " + hello.outputArguments[0].value);
        require_(add.statusCode.isGood(), "Add " + add.statusCode.toString());
        require_(add.outputArguments[0].value === 3.5, "Add returned " + add.outputArguments[0].value);
    },
    async Subscription(c) {
        const sub = ClientSubscription.create(c.session, { requestedPublishingInterval: 100, requestedMaxKeepAliveCount: 10, requestedLifetimeCount: 100, publishingEnabled: true });
        await new Promise((resolve) => sub.on("started", resolve));
        const item = ClientMonitoredItem.create(sub, { nodeId: resolveNodeId("Server_ServerStatus_CurrentTime"), attributeId: AttributeIds.Value },
            { samplingInterval: 100, queueSize: 10, discardOldest: true }, TimestampsToReturn.Both);
        let count = 0;
        const ok = await new Promise((resolve) => {
            const t = setTimeout(() => resolve(false), 15000);
            item.on("changed", () => { if (++count >= 3) { clearTimeout(t); resolve(true); } });
        });
        await sub.terminate();
        require_(ok, `only ${count} data change notifications in 15 s`);
    },
    // Decodes every variable below Objects whose data type is a custom type
    // (node-opcua promotes opaque structures from DataTypeDefinitions on read)
    // and writes up to 50 of the decoded structures back unchanged.
    async ComplexTypes(c) {
        const variables = (await browseTree(c, resolveNodeId("ObjectsFolder"), 30_000))
            .filter((r) => r.nodeClass === NodeClass.Variable && r.nodeId.namespace !== 0)
            .map((r) => r.nodeId);
        const dataTypes = await readAttribute(c, variables, AttributeIds.DataType);
        const custom = variables.filter((_, ii) => dataTypes[ii].value?.value?.namespace > 0);
        require_(custom.length >= 10, `only ${custom.length} variables with a custom data type found`);
        const values = await readAttribute(c, custom, AttributeIds.Value);
        const access = await readAttribute(c, custom, AttributeIds.UserAccessLevel);
        const typeNames = new Set();
        const undecoded = [];
        const writes = [];
        let structures = 0;
        custom.forEach((nodeId, ii) => {
            const dv = values[ii];
            if (!dv.statusCode.isGood() || dv.value?.dataType !== DataType.ExtensionObject) return;
            const items = dv.value.arrayType === VariantArrayType.Scalar ? [dv.value.value] : Array.from(dv.value.value ?? []);
            for (const item of items.filter((x) => x)) {
                structures++;
                if (item.constructor?.name === "OpaqueStructure") undecoded.push(nodeId.toString());
                else typeNames.add(item.schema?.name ?? item.constructor?.name);
            }
            if (writes.length < 50 && (access[ii].value?.value & 2) !== 0 && items.length > 0) {
                writes.push({ nodeId, attributeId: AttributeIds.Value, value: { value: dv.value } });
            }
        });
        require_(structures > 0, "no structure values were read");
        require_(undecoded.length === 0, `${undecoded.length} of ${structures} structures were not decoded: ${undecoded.slice(0, 10).join("; ")}`);
        for (const required of ["ScalarStructureDataType", "VectorUnion", "VectorWithOptionalFields"]) {
            require_(typeNames.has(required), `no ${required} value was decoded; decoded: ${[...typeNames].join(", ")}`);
        }
        require_(writes.length > 0, "no writable structure variable found");
        const results = await c.session.write(writes);
        const rejected = writes.filter((_, ii) => !results[ii].isGood()).map((w, ii) => `${w.nodeId}: ${results[ii].toString()}`);
        require_(rejected.length === 0, `${rejected.length} of ${writes.length} structure writes were rejected: ${rejected.slice(0, 10).join("; ")}`);
        const readBack = await readAttribute(c, writes.map((w) => w.nodeId), AttributeIds.Value);
        const changed = writes.filter((w, ii) => canonical(w.value.value.value) !== canonical(readBack[ii].value?.value)).map((w) => w.nodeId.toString());
        require_(changed.length === 0, `${changed.length} structures changed in a write and read round trip: ${changed.slice(0, 10).join("; ")}`);
    },
    async LargeArrayRoundTrip(c) {
        // 200 000 Int32 = 800 kB, far more than one 64 kB message chunk.
        const values = Int32Array.from({ length: 200_000 }, (_, i) => Math.imul(i, 7919) ^ 0x5A5A);
        await writeAndCompare(c, c.id("Scalar_Static_Arrays_Int32"), { dataType: DataType.Int32, arrayType: VariantArrayType.Array, value: values });
    },
    async LargeByteStringRoundTrip(c) {
        // 3 MB, below the 4 MB ByteString limit of the reference server fixture.
        const value = Buffer.alloc(3 * 1024 * 1024);
        for (let ii = 0, x = 4711; ii < value.length; ii++) { x = (Math.imul(x, 1103515245) + 12345) | 0; value[ii] = x >>> 24; }
        await writeAndCompare(c, c.id("Scalar_Static_ByteString"), { dataType: DataType.ByteString, value });
    },
    // A ByteString above the server's MaxByteStringLength but inside its
    // MaxMessageSize must be rejected with a status code; the session stays usable.
    async OversizedRequestRejected(c) {
        let result;
        try {
            [result] = await c.session.write([{ nodeId: c.id("Scalar_Static_ByteString"), attributeId: AttributeIds.Value,
                value: { value: { dataType: DataType.ByteString, value: Buffer.alloc(5 * 1024 * 1024) } } }]);
        } catch (e) {
            result = { isGood: () => false, toString: () => e.message };
        }
        require_(!result.isGood(), "a 5 MB ByteString write was accepted");
        const state = await c.session.read({ nodeId: resolveNodeId("Server_ServerStatus_State"), attributeId: AttributeIds.Value });
        require_(state.statusCode.isGood(), `the session is unusable after the rejected write (${result}): ${state.statusCode.toString()}`);
        console.log("INFO OversizedRequestRejected: " + result.toString());
    },
    // Browses a folder three references at a time with BrowseNext, compares
    // with a browse without limit, then releases an open continuation point.
    async BrowseContinuationPoints(c) {
        const folder = c.id("Scalar_Static");
        const all = await browseFull(c, folder, 0);
        require_(all.length > 10, `only ${all.length} references below ${folder}`);
        const paged = await browseFull(c, folder, 3);
        require_(paged.map(String).join() === all.map(String).join(), `paged browse returned ${paged.length} of ${all.length} references or a different order`);
        const first = await browseWithLimit(c, folder, 3);
        require_(first.continuationPoint?.length > 0, "no continuation point returned");
        const [released] = await c.session.browseNext([first.continuationPoint], true);
        require_(released.statusCode.isGood(), "releasing the continuation point returned " + released.statusCode.toString());
        const [reused] = await c.session.browseNext([first.continuationPoint], false);
        require_(reused.statusCode.equals(StatusCodes.BadContinuationPointInvalid), "a released continuation point returned " + reused.statusCode.toString());
    },
    // Reads 2 * MaxNodesPerRead + 1 nodes split by the server's limit, then in
    // one request, which the server must reject with BadTooManyOperations.
    async ReadManyNodes(c) {
        const limitValue = await c.session.read({ nodeId: resolveNodeId("Server_ServerCapabilities_OperationLimits_MaxNodesPerRead"), attributeId: AttributeIds.Value });
        const limit = limitValue.value?.value ?? 0;
        require_(limit > 0, "the server published no MaxNodesPerRead");
        const nodes = Array.from({ length: limit * 2 + 1 }, () => ({ nodeId: resolveNodeId("Server_ServerStatus_State"), attributeId: AttributeIds.Value }));
        let good = 0;
        for (let offset = 0; offset < nodes.length; offset += limit) {
            good += (await c.session.read(nodes.slice(offset, offset + limit))).filter((dv) => dv.statusCode.isGood()).length;
        }
        require_(good === nodes.length, `${good} of ${nodes.length} reads split by MaxNodesPerRead ${limit} succeeded`);
        let tooMany = "Good";
        try {
            await c.session.read(nodes);
        } catch (e) {
            tooMany = /Bad\w+/.exec(e.message)?.[0] ?? e.message;
        }
        require_(tooMany === "BadTooManyOperations", `reading ${nodes.length} nodes in one request with MaxNodesPerRead ${limit} returned ${tooMany}`);
    },
    // Keeps reading for longer than the revised token lifetime (renewal at 75 %).
    async TokenRenewal(c) {
        const seconds = parseInt(c.options["token-test-seconds"] ?? "65", 10);
        const started = Date.now();
        let reads = 0;
        while (Date.now() - started < seconds * 1000) {
            const dv = await c.session.read({ nodeId: resolveNodeId("Server_ServerStatus_CurrentTime"), attributeId: AttributeIds.Value });
            require_(dv.statusCode.isGood(), `read ${reads} after ${Math.round((Date.now() - started) / 1000)} s returned ${dv.statusCode.toString()}`);
            reads++;
            await new Promise((r) => setTimeout(r, 500));
        }
    }
};

function browseDescription(nodeId, referenceTypeId = "HierarchicalReferences") {
    return { nodeId, browseDirection: BrowseDirection.Forward, referenceTypeId, includeSubtypes: true, nodeClassMask: 0, resultMask: 63 };
}

async function browseWithLimit(c, nodeId, maxReferences) {
    const saved = c.session.requestedMaxReferencesPerNode;
    c.session.requestedMaxReferencesPerNode = maxReferences;
    try {
        const result = await c.session.browse(browseDescription(nodeId));
        require_(result.statusCode.isGood(), `browse of ${nodeId} returned ${result.statusCode.toString()}`);
        return result;
    } finally {
        c.session.requestedMaxReferencesPerNode = saved;
    }
}

// Browses one node, following continuation points; returns the target ids.
async function browseFull(c, nodeId, maxReferences) {
    let result = await browseWithLimit(c, nodeId, maxReferences);
    const references = [...(result.references ?? [])];
    while (result.continuationPoint?.length > 0) {
        [result] = await c.session.browseNext([result.continuationPoint], false);
        require_(result.statusCode.isGood(), `browse next of ${nodeId} returned ${result.statusCode.toString()}`);
        references.push(...(result.references ?? []));
    }
    return references.map((r) => r.nodeId.toString());
}

// Breadth-first browse of the hierarchy below a node, without the Server object.
async function browseTree(c, root, maxNodes) {
    const found = [];
    const visited = new Set([root.toString()]);
    let level = [root];
    while (level.length > 0 && found.length < maxNodes) {
        const next = [];
        for (let offset = 0; offset < level.length; offset += 200) {
            const batch = level.slice(offset, offset + 200);
            let results = await c.session.browse(batch.map((n) => browseDescription(n)));
            for (let ii = 0; ii < results.length; ii++) {
                let result = results[ii];
                const references = [...(result.references ?? [])];
                while (result.continuationPoint?.length > 0) {
                    [result] = await c.session.browseNext([result.continuationPoint], false);
                    references.push(...(result.references ?? []));
                }
                for (const r of references) {
                    if (r.nodeId.namespaceUri || r.nodeId.serverIndex) continue;
                    const target = new NodeId(r.nodeId.identifierType, r.nodeId.value, r.nodeId.namespace);
                    const key = target.toString();
                    if (visited.has(key) || key === "ns=0;i=2253") continue;
                    visited.add(key);
                    found.push({ nodeId: target, nodeClass: r.nodeClass });
                    next.push(target);
                }
            }
        }
        level = next;
    }
    return found;
}

async function readAttribute(c, nodes, attributeId) {
    const results = [];
    for (let offset = 0; offset < nodes.length; offset += 500) {
        results.push(...await c.session.read(nodes.slice(offset, offset + 500).map((nodeId) => ({ nodeId, attributeId }))));
    }
    return results;
}

async function writeAndCompare(c, nodeId, variant) {
    const [status] = await c.session.write([{ nodeId, attributeId: AttributeIds.Value, value: { value: variant } }]);
    require_(status.isGood(), `write of ${nodeId} returned ${status.toString()}`);
    const dv = await c.session.read({ nodeId, attributeId: AttributeIds.Value });
    require_(dv.statusCode.isGood(), `read of ${nodeId} returned ${dv.statusCode.toString()}`);
    require_(canonical(dv.value.value) === canonical(variant.value), `${nodeId} read back a different value`);
}

// A comparable form of a decoded value: typed arrays, Buffers and BigInts made plain.
function canonical(value) {
    return JSON.stringify(value, (_, v) =>
        typeof v === "bigint" ? v.toString()
            : ArrayBuffer.isView(v) ? Array.from(v)
                : v?.type === "Buffer" && Array.isArray(v.data) ? v.data : v);
}
const DEFAULT_CHECKS = Object.keys(CHECKS).slice(0, 9);

async function runClient(o) {
    setDecodingLimits(1024 * 1024, 16 * 1024 * 1024);
    const name = "NodeOpcuaInteropClient";
    const clientCertificateManager = await certificateManager(o);
    const policy = o.policy ?? SecurityPolicy.None;
    const mode = MessageSecurityMode[o.mode ?? "None"];
    const selected = (o.checks ?? DEFAULT_CHECKS.join(",")).split(",").filter((x) => x);
    const unknown = selected.filter((x) => !(x in CHECKS) && x !== "Connect" && x !== "CloseSession");
    if (unknown.length) throw new Error("unknown checks: " + unknown.join(", "));
    const expected = (o["expect-connect-error"] ?? "").split(",").filter((x) => x);
    const client = OPCUAClient.create({
        applicationName: name, applicationUri: `urn:localhost:opcfoundation.org:${name}`, clientCertificateManager,
        securityPolicy: policy, securityMode: mode, endpointMustExist: false, connectionStrategy: { maxRetry: 0 },
        transportSettings: { maxMessageSize: 16 * 1024 * 1024, maxChunkCount: 0 },
        ...(o["token-lifetime"] ? { defaultSecureTokenLifetime: parseInt(o["token-lifetime"], 10) } : {})
    });
    if (flag(o, "init-only", false)) {
        await client.createDefaultCertificate?.();
        console.log("PEER-PKI-READY " + client.applicationUri);
        return EXIT_SUCCESS;
    }
    const checks = new CheckRunner();
    let session = null;
    const deadline = setTimeout(() => { console.log("INFO deadline reached"); process.exit(checks.finish()); }, 1000 * parseInt(o["timeout-seconds"] ?? "300", 10));
    await checks.run("Connect", async () => {
        try {
            await client.connect(o.url);
            const identity = o.user ? { type: UserTokenType.UserName, userName: o.user, password: o.password ?? "" } : { type: UserTokenType.Anonymous };
            session = await client.createSession(identity);
        } catch (e) {
            if (expected.length) {
                const actual = /Bad\w+/.exec(e.message)?.[0] ?? e.name;
                require_(expected.includes(actual), `expected ${expected}, the connect failed with ${actual}: ${e.message}`);
                return;
            }
            throw e;
        }
        require_(!expected.length, `expected the connect to fail with ${expected}, but it succeeded`);
    });
    if (session) {
        const nsArray = (await session.readNamespaceArray());
        const ns = nsArray.indexOf(REFERENCE_NAMESPACE);
        const ctx = { session, ns, options: o, id: (s) => new NodeId(NodeId.NodeIdType.STRING, s, ns) };
        try {
            for (const [checkName, fn] of Object.entries(CHECKS)) {
                if (selected.includes(checkName)) await checks.run(checkName, () => fn(ctx));
            }
        } finally {
            await checks.run("CloseSession", () => session.close());
        }
    }
    await client.disconnect();
    clearTimeout(deadline);
    return checks.finish();
}

// ----------------------------------------------------------------------------- main

const [command, ...rest] = process.argv.slice(2);
let exitCode;
try {
    const options = parseOptions(rest);
    exitCode = command === "server" ? await runServer(options)
        : command === "client" ? await runClient(options)
        : (console.error("error: unknown command " + command), EXIT_USAGE);
} catch (e) {
    console.error("FATAL " + (e.stack ?? e));
    exitCode = EXIT_FATAL;
}
process.exit(exitCode);
