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
    QualifiedName, resolveNodeId, BinaryStream, VariantArrayType, MonitoringMode, DataChangeFilter, DataChangeTrigger,
    DeadbandType, EventFilter, SimpleAttributeOperand, ContentFilter, ContentFilterElement, LiteralOperand, FilterOperator,
    RepublishRequest, PublishRequest, HistoryReadRequest, ReadRawModifiedDetails, AddNodesRequest, DeleteNodesRequest
} from "node-opcua";
import { ObjectAttributes } from "node-opcua-types";
import { createSelfSignedCertificate, generatePrivateKey, privateKeyToPEM, convertPEMtoDER, CertificatePurpose } from "node-opcua-crypto";

const require = createRequire(import.meta.url);
const VERSION = require("node-opcua/package.json").version;
const INTEROP_NAMESPACE = "urn:opcfoundation.org:interop:legacy";
const REFERENCE_NAMESPACE = "http://opcfoundation.org/Quickstarts/ReferenceServer";
const ALARMS_NAMESPACE = "http://test.org/UA/Alarms/";
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
    // X509 user identity tokens: the secure endpoints advertise a Certificate
    // token policy by default; any (self-signed) user certificate is accepted.
    const userCertificateManager = new OPCUACertificateManager({ rootFolder: path.join(o.pki, "user"), automaticallyAcceptUnknownCertificate: true });
    await userCertificateManager.initialize();
    const server = new OPCUAServer({
        port,
        resourcePath: "/" + name,
        serverCertificateManager,
        userCertificateManager,
        serverInfo: { applicationUri, productUri: "http://opcfoundation.org/UA/Interop/NodeOpcuaServer", applicationName: { text: name } },
        buildInfo: { productName: "node-opcua Interop Server", manufacturerName: "Sterfive", softwareVersion: "node-opcua " + VERSION, buildNumber: "0", buildDate: new Date() },
        securityPolicies: [SecurityPolicy.None, SecurityPolicy.Basic256Sha256, SecurityPolicy.Aes128_Sha256_RsaOaep, SecurityPolicy.Aes256_Sha256_RsaPss],
        securityModes: [MessageSecurityMode.None, MessageSecurityMode.Sign, MessageSecurityMode.SignAndEncrypt],
        allowAnonymous: true,
        userManager: { isValidUser: (user, password) => user === USER_NAME && password === PASSWORD },
        // MinSupportedSampleRate 0: a sampling interval of 0 on an exception-based variable
        // reports every change (node-opcua otherwise folds changes within 50 ms into one).
        serverCapabilities: { minSupportedSampleRate: 0, operationLimits: { maxNodesPerRead: 100, maxNodesPerWrite: 100, maxNodesPerBrowse: 100, maxNodesPerMethodCall: 100 } }
    });
    await server.initialize();
    if (flag(o, "init-only", false)) {
        console.log("PEER-PKI-READY " + applicationUri);
        return EXIT_SUCCESS;
    }
    const addressSpace = server.engine.addressSpace;
    // node-opcua binds the ConditionType methods (ConditionRefresh, ...) only on request;
    // without it a ConditionRefresh call returns BadInternalError.
    addressSpace.installAlarmsAndConditionsService();
    const ns =addressSpace.registerNamespace(INTEROP_NAMESPACE);
    const folder = ns.addFolder(addressSpace.rootFolder.objects, { nodeId: "s=Interop", browseName: "Interop" });
    const rw = { accessLevel: "CurrentRead | CurrentWrite", userAccessLevel: "CurrentRead | CurrentWrite" };
    const variable = (browseName, dataType, value, extra = {}) => ns.addVariable({
        // MinimumSamplingInterval 0: exception-based like the 1.5.378 interop server,
        // so a burst of writes is not collapsed into one sample.
        organizedBy: folder, nodeId: "s=" + browseName, browseName, dataType, minimumSamplingInterval: 0, ...rw, ...extra,
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
    const counter = variable("Counter", DataType.Int32, 0, { accessLevel: "CurrentRead", userAccessLevel: "CurrentRead", minimumSamplingInterval: 100 });
    const add = ns.addMethod(folder, {
        nodeId: "s=Add", browseName: "Add",
        inputArguments: [{ name: "a", dataType: DataType.Int32 }, { name: "b", dataType: DataType.Int32 }],
        outputArguments: [{ name: "sum", dataType: DataType.Int32 }]
    });
    add.bindMethod(async (inputs, context) => ({
        statusCode: StatusCodes.Good,
        outputArguments: [{ dataType: DataType.Int32, value: inputs[0].value + inputs[1].value }]
    }));
    // RaiseEvent() reports one BaseEventType event through the Server object.
    const raiseEvent = ns.addMethod(folder, { nodeId: "s=RaiseEvent", browseName: "RaiseEvent" });
    raiseEvent.bindMethod(async (_inputs, _context) => { // node-opcua tells async from callback methods by arity
        folder.raiseEvent("BaseEventType", {
            message: { dataType: DataType.LocalizedText, value: new LocalizedText({ text: "interop event" }) },
            severity: { dataType: DataType.UInt16, value: 500 },
            sourceNode: { dataType: DataType.NodeId, value: folder.nodeId },
            sourceName: { dataType: DataType.String, value: "Interop" }
        });
        return { statusCode: StatusCodes.Good };
    });
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
    },

    // ------------------------------------------------------------------- events
    // Fields of the event filter: 0 EventId, 1 EventType, 2 SourceNode, 3 Time,
    // 4 Message, 5 Severity (+ 6 ConditionId, 7 AckedState/Id, 8 Retain).
    async EventSubscription(c) {
        const events = [];
        const sub = await createEventSubscription(c, resolveNodeId("Server"), events);
        try {
            await writeValue(c, c.id("NodeIds_Events_TriggerNode01"), { dataType: DataType.Int32, value: 1 });
            const received = await waitFor(events, (e) => (e[4]?.value?.text ?? "").includes("Trigger event"));
            require_(received, `no trigger event in 10 s; received ${events.length} events`);
            require_(Buffer.isBuffer(received[0].value) && received[0].value.length > 0, "the event has no EventId");
            require_(received[5].value > 0, "the event has no Severity: " + received[5].value);
        } finally {
            await terminate(sub);
        }
    },
    async ConditionRefresh(c) {
        const events = [];
        const sub = await createEventSubscription(c, resolveNodeId("Server"), events);
        try {
            const result = await call(c, coerceNodeId("i=2782"), coerceNodeId("i=3875"), { dataType: DataType.UInt32, value: sub.subscriptionId });
            require_(result.statusCode.isGood(), "ConditionRefresh returned " + result.statusCode.toString());
            const isType = (e, id) => e[1]?.value?.toString() === id;
            require_(await waitFor(events, (e) => isType(e, "ns=0;i=2788")), "no RefreshEndEvent received");
            require_(events.some((e) => isType(e, "ns=0;i=2787")), "no RefreshStartEvent received");
        } finally {
            await terminate(sub);
        }
    },
    async AlarmAcknowledge(c) {
        require_(c.alarmsNs > 0, "the Alarms namespace is missing");
        const folder = new NodeId(NodeId.NodeIdType.STRING, "Alarms", c.alarmsNs);
        const events = [];
        const sub = await createEventSubscription(c, folder, events, true);
        try {
            const started = await call(c, folder, new NodeId(NodeId.NodeIdType.STRING, "Alarms.Start", c.alarmsNs), { dataType: DataType.UInt32, value: 30 });
            require_(started.statusCode.isGood(), "Alarms.Start returned " + started.statusCode.toString());
            const unacked = await waitFor(events, (e) => e[6]?.value instanceof NodeId && e[7]?.value === false && e[8]?.value === true, 20_000);
            if (!unacked) {
                const last = events[events.length - 1];
                throw new Error(`no unacknowledged condition in 20 s; received ${events.length} events, last: ` +
                    (last ? last.map((f) => `${DataType[f.dataType]}:${f.value}`).join(" | ") : "none"));
            }
            const ack = await call(c, unacked[6].value, coerceNodeId("i=9111"),
                { dataType: DataType.ByteString, value: unacked[0].value },
                { dataType: DataType.LocalizedText, value: new LocalizedText({ text: "acknowledged by the interop test" }) });
            require_(ack.statusCode.isGood(), "Acknowledge returned " + ack.statusCode.toString());
        } finally {
            await call(c, folder, new NodeId(NodeId.NodeIdType.STRING, "Alarms.End", c.alarmsNs)).catch(() => { });
            await terminate(sub);
        }
    },

    // ------------------------------------------------------------ subscriptions
    async DeadbandFilter(c) {
        const analog = c.id("DataAccess_AnalogType_Double");
        for (const type of [DeadbandType.Absolute, DeadbandType.Percent]) {
            await writeValue(c, analog, { dataType: DataType.Double, value: 0 });
            const values = [];
            const sub = await createSubscription(c, 100);
            try {
                await monitor(sub, analog, { samplingInterval: 50, queueSize: 10, discardOldest: true,
                    filter: new DataChangeFilter({ trigger: DataChangeTrigger.StatusValue, deadbandType: type, deadbandValue: 10 }) },
                    (dv) => values.push(dv.value?.value));
                await delay(500);
                for (const v of [5, 20, 25, 40]) {
                    await writeValue(c, analog, { dataType: DataType.Double, value: v });
                    await delay(400);
                }
                await delay(800);
                const name = DeadbandType[type], list = values.join(", ");
                require_(values.includes(20) && values.includes(40), `${name}: 20 and 40 not reported (${list})`);
                require_(!values.includes(5) && !values.includes(25), `${name}: changes inside the deadband reported (${list})`);
            } finally {
                await terminate(sub);
            }
        }
    },
    async QueueOverflow(c) {
        const node = c.id("Scalar_Static_Int32");
        await writeValue(c, node, { dataType: DataType.Int32, value: 0 });
        const values = [];
        const sub = await createSubscription(c, 2000);
        try {
            await monitor(sub, node, { samplingInterval: 0, queueSize: 2, discardOldest: true }, (dv) => values.push(dv));
            await delay(2500);
            values.length = 0;
            for (let ii = 1; ii <= 5; ii++) await writeValue(c, node, { dataType: DataType.Int32, value: ii });
            await delay(3000);
            const list = values.map((v) => `${v.value?.value}:0x${(v.statusCode.value >>> 0).toString(16)}`).join(", ");
            require_(values.length === 2, `expected the last 2 of 5 values, got ${values.length} (${list})`);
            require_(values[0].value.value === 4 && values[1].value.value === 5, "expected 4 and 5, got " + list);
            require_(values.some((v) => (v.statusCode.value & 0x0480) === 0x0480), `no value carries the Overflow bit (${list})`);
        } finally {
            await terminate(sub);
        }
    },
    async Triggering(c) {
        const sub = await createSubscription(c, 100);
        try {
            let linkedReports = 0;
            const trigger = await monitor(sub, c.id("Scalar_Static_Int32"), { samplingInterval: 50, queueSize: 10, discardOldest: true }, () => { });
            const linked = await monitor(sub, c.id("Scalar_Static_String"), { samplingInterval: 50, queueSize: 10, discardOldest: true },
                () => linkedReports++, MonitoringMode.Sampling);
            const set = await c.session.setTriggering({ subscriptionId: sub.subscriptionId, triggeringItemId: trigger.monitoredItemId,
                linksToAdd: [linked.monitoredItemId], linksToRemove: [] });
            require_(set.addResults?.[0]?.isGood(), "SetTriggering returned " + set.addResults?.[0]?.toString());
            await delay(500);
            const before = linkedReports;
            await writeValue(c, c.id("Scalar_Static_String"), { dataType: DataType.String, value: "linked " + Math.random() });
            await delay(500);
            require_(linkedReports === before, "the sampling item reported without its trigger");
            await writeValue(c, c.id("Scalar_Static_Int32"), { dataType: DataType.Int32, value: Date.now() & 0x7fffffff });
            await delay(1000);
            require_(linkedReports > before, "the triggered item did not report");
        } finally {
            await terminate(sub);
        }
    },
    async Republish(c) {
        const sub = await createSubscription(c, 100);
        try {
            await monitor(sub, c.id("Scalar_Static_Int32"), { samplingInterval: 50, queueSize: 10, discardOldest: true }, () => { });
            const notSent = await republishStatus(c, sub.subscriptionId, Math.max(sub.lastSequenceNumber, 0) + 1000);
            require_(notSent === "BadMessageNotAvailable", "Republish of an unsent message returned " + notSent);
            const unknown = await republishStatus(c, sub.subscriptionId + 100_000, 1);
            require_(unknown === "BadSubscriptionIdInvalid", "Republish of an unknown subscription returned " + unknown);
        } finally {
            await terminate(sub);
        }
    },
    // Session B takes the subscription over with TransferSubscriptions; node-opcua
    // has no client-side object for a transferred subscription, so B publishes itself.
    async TransferSubscription(c) {
        const node = c.id("Scalar_Static_Int32");
        const sub = await createSubscription(c, 100);
        let target = null;
        try {
            await monitor(sub, node, { samplingInterval: 50, queueSize: 10, discardOldest: true }, () => { });
            target = await c.client.createSession({ type: UserTokenType.Anonymous });
            const transfer = await target.transferSubscriptions({ subscriptionIds: [sub.subscriptionId], sendInitialValues: true });
            require_(transfer.results?.[0]?.statusCode?.isGood(), "TransferSubscriptions returned " + transfer.results?.[0]?.statusCode?.toString());
            const values = [];
            let acks = [];
            let stop = false;
            const publishing = (async () => {
                while (!stop) {
                    const response = await transaction(target, new PublishRequest({ subscriptionAcknowledgements: acks }));
                    const message = response.notificationMessage;
                    acks = message?.notificationData?.length ? [{ subscriptionId: response.subscriptionId, sequenceNumber: message.sequenceNumber }] : [];
                    for (const data of message?.notificationData ?? []) {
                        for (const item of data?.monitoredItems ?? []) values.push(item.value?.value?.value);
                    }
                }
            })();
            publishing.catch(() => { });
            await delay(300);
            const value = (Date.now() & 0x7fffffff) | 1;
            const [status] = await target.write([{ nodeId: node, attributeId: AttributeIds.Value, value: { value: { dataType: DataType.Int32, value } } }]);
            require_(status.isGood(), "write returned " + status.toString());
            const deadline = Date.now() + 10_000;
            while (!values.includes(value) && Date.now() < deadline) await delay(100);
            stop = true;
            require_(values.includes(value), `the transferred subscription did not report the write (received ${values.join(", ")})`);
        } finally {
            if (target) await target.close(true).catch(() => { });
            await terminate(sub);
        }
    },

    // ----------------------------------------------------------------- identity
    async WrongPasswordRejected(c) {
        let session;
        try {
            session = await c.client.createSession({ type: UserTokenType.UserName, userName: "user1", password: "wrong password" });
        } catch (e) {
            const code = /Bad\w+/.exec(e.message)?.[0] ?? e.message;
            require_(code === "BadUserAccessDenied" || code === "BadIdentityTokenRejected", "the wrong password was rejected with " + code);
            return;
        }
        await session.close().catch(() => { });
        throw new Error("a session with a wrong password was activated");
    },
    async X509UserToken(c) {
        const privateKey = await generatePrivateKey(2048);
        const { privPem } = await privateKeyToPEM(privateKey);
        const { cert } = await createSelfSignedCertificate({ privateKey, subject: "/CN=InteropUser/O=OPC Foundation",
            purpose: CertificatePurpose.ForUserAuthentication, validity: 365 });
        const session = await c.client.createSession({ type: UserTokenType.Certificate, certificateData: convertPEMtoDER(cert), privateKey: privPem });
        try {
            const state = await session.read({ nodeId: resolveNodeId("Server_ServerStatus_State"), attributeId: AttributeIds.Value });
            require_(state.statusCode.isGood(), "reading with the X509 user returned " + state.statusCode.toString());
        } finally {
            await session.close().catch(() => { });
        }
    },

    // ----------------------------------------------------------------- services
    async RegisterNodes(c) {
        const registered = await c.session.registerNodes([c.id("Scalar_Static_Int32"), c.id("Scalar_Static_String")]);
        require_(registered?.length === 2, "RegisterNodes returned " + registered?.length + " ids");
        const read = await c.session.read(registered.map((nodeId) => ({ nodeId, attributeId: AttributeIds.Value })));
        require_(read.every((dv) => dv.statusCode.isGood()), "reading registered nodes returned " + read.map((dv) => dv.statusCode.toString()).join(", "));
        await c.session.unregisterNodes(registered);
    },
    async HistoryReadRaw(c) {
        const nodeId = c.id("Scalar_Static_Double");
        const details = new ReadRawModifiedDetails({ isReadModified: false, startTime: new Date(Date.now() - 3 * 3600_000), endTime: new Date(),
            numValuesPerNode: 10, returnBounds: false });
        const response = await c.session.historyRead(new HistoryReadRequest({ historyReadDetails: details, timestampsToReturn: TimestampsToReturn.Source,
            releaseContinuationPoints: false, nodesToRead: [{ nodeId }] }));
        const result = response.results[0];
        require_(result.statusCode.isGood(), "HistoryRead returned " + result.statusCode.toString());
        const count = result.historyData?.dataValues?.length ?? 0;
        require_(count > 0, "HistoryRead returned no values");
        require_(count <= 10, `${count} values despite NumValuesPerNode 10`);
        if (result.continuationPoint?.length > 0) {
            await c.session.historyRead(new HistoryReadRequest({ historyReadDetails: details, timestampsToReturn: TimestampsToReturn.Source,
                releaseContinuationPoints: true, nodesToRead: [{ nodeId, continuationPoint: result.continuationPoint }] }));
        }
    },
    async NodeManagement(c) {
        const name = "InteropAdded_" + Math.floor(Math.random() * 0x100000000).toString(16).padStart(8, "0");
        const added = await transaction(c.session, new AddNodesRequest({ nodesToAdd: [{
            parentNodeId: resolveNodeId("ObjectsFolder"),
            referenceTypeId: resolveNodeId("Organizes"),
            requestedNewNodeId: c.id(name),
            browseName: new QualifiedName({ name, namespaceIndex: c.ns }),
            nodeClass: NodeClass.Object,
            nodeAttributes: new ObjectAttributes({ displayName: new LocalizedText({ text: name }), specifiedAttributes: 0x40 }),
            typeDefinition: resolveNodeId("BaseObjectType")
        }] }));
        const result = added.results[0];
        require_(result.statusCode.isGood(), "AddNodes returned " + result.statusCode.toString());
        const browsed = await c.session.browse(browseDescription(resolveNodeId("ObjectsFolder")));
        const references = [...(browsed.references ?? [])];
        for (let r = browsed; r.continuationPoint?.length > 0;) {
            [r] = await c.session.browseNext([r.continuationPoint], false);
            references.push(...(r.references ?? []));
        }
        require_(references.some((r) => r.browseName.name === name), "the added node is not organized by the Objects folder");
        const deleted = await transaction(c.session, new DeleteNodesRequest({ nodesToDelete: [{ nodeId: result.addedNodeId, deleteTargetReferences: true }] }));
        require_(deleted.results[0].isGood(), "DeleteNodes returned " + deleted.results[0].toString());
    },
    async IndexRange(c) {
        const nodeId = c.id("Scalar_Static_Arrays_Int32");
        await writeValue(c, nodeId, { dataType: DataType.Int32, arrayType: VariantArrayType.Array, value: Int32Array.from({ length: 10 }, (_, i) => i) });
        const [status] = await c.session.write([{ nodeId, attributeId: AttributeIds.Value, indexRange: "2:3",
            value: { value: { dataType: DataType.Int32, arrayType: VariantArrayType.Array, value: new Int32Array([20, 30]) } } }]);
        require_(status.isGood(), "writing the index range 2:3 returned " + status.toString());
        const dv = await c.session.read({ nodeId, attributeId: AttributeIds.Value, indexRange: "1:4" });
        require_(dv.statusCode.isGood(), "reading the index range 1:4 returned " + dv.statusCode.toString());
        const part = Array.from(dv.value?.value ?? []);
        require_(part.join() === "1,20,30,4", "index range 1:4 read " + part.join());
    },
    async FindServers(c) {
        const discovery = OPCUAClient.create({ applicationName: "NodeOpcuaInteropDiscovery", clientCertificateManager: c.client.clientCertificateManager,
            securityMode: MessageSecurityMode.None, securityPolicy: SecurityPolicy.None, endpointMustExist: false, connectionStrategy: { maxRetry: 0 } });
        await discovery.connect(c.url);
        try {
            const servers = await discovery.findServers();
            const serverUri = c.client.endpoint?.server?.applicationUri
                ?? (await c.session.read({ nodeId: resolveNodeId("Server_ServerArray"), attributeId: AttributeIds.Value })).value.value[0];
            require_(servers.some((s) => s.applicationUri === serverUri), `FindServers does not list ${serverUri}: ${servers.map((s) => s.applicationUri).join(", ")}`);
            const endpoints = await discovery.getEndpoints();
            require_(endpoints.some((e) => e.securityPolicyUri === SecurityPolicy.Basic256Sha256 && e.securityMode === MessageSecurityMode.SignAndEncrypt),
                "GetEndpoints does not offer Basic256Sha256/SignAndEncrypt");
        } finally {
            await discovery.disconnect();
        }
    },
    // Opens a new secure channel with a second client and activates the existing
    // session on it (OPCUAClient.reactivateSession).
    async SessionReconnect(c) {
        const before = c.session.sessionId.toString();
        const client = c.createClient();
        await client.connect(c.url);
        c.clients.push(client);
        await client.reactivateSession(c.session);
        require_(c.session.sessionId.toString() === before, `the session id changed from ${before} to ${c.session.sessionId}`);
        const state = await c.session.read({ nodeId: resolveNodeId("Server_ServerStatus_State"), attributeId: AttributeIds.Value });
        require_(state.statusCode.isGood(), "reading after the reconnect returned " + state.statusCode.toString());
    }
};

const delay = (ms) => new Promise((r) => setTimeout(r, ms));

async function waitFor(list, match, timeout = 10_000) {
    const deadline = Date.now() + timeout;
    while (Date.now() < deadline) {
        const found = list.find(match);
        if (found) return found;
        await delay(100);
    }
    return null;
}

// Promise wrapper of the callback-only ClientSession.performMessageTransaction.
function transaction(session, request) {
    return new Promise((resolve, reject) => session.performMessageTransaction(request, (err, response) => (err ? reject(err) : resolve(response))));
}

async function writeValue(c, nodeId, variant) {
    const [status] = await c.session.write([{ nodeId, attributeId: AttributeIds.Value, value: { value: variant } }]);
    require_(status.isGood(), `write of ${nodeId} returned ${status.toString()}`);
}

async function call(c, objectId, methodId, ...inputArguments) {
    return await c.session.call({ objectId, methodId, inputArguments });
}

async function createSubscription(c, publishingInterval) {
    return await c.session.createSubscription2({ requestedPublishingInterval: publishingInterval, requestedMaxKeepAliveCount: 10,
        requestedLifetimeCount: 100, maxNotificationsPerPublish: 0, publishingEnabled: true, priority: 0 });
}

async function terminate(sub) {
    try { await sub.terminate(); } catch { /* the check already reported its outcome */ }
}

// Creates a monitored item, attaching the listener before the item is created
// so no early notification is lost; resolves once the server created it.
async function monitor(sub, nodeId, parameters, onChange, mode = MonitoringMode.Reporting, attributeId = AttributeIds.Value) {
    const item = ClientMonitoredItem.create(sub, { nodeId, attributeId }, parameters, TimestampsToReturn.Both, mode);
    item.on("changed", onChange);
    await new Promise((resolve, reject) => {
        item.once("initialized", resolve);
        item.once("err", (message) => reject(new Error("monitored item " + message)));
    });
    return item;
}

const operand = (typeDefinition, ...path) => new SimpleAttributeOperand({ typeDefinitionId: coerceNodeId(typeDefinition),
    browsePath: path.map((name) => new QualifiedName({ name })), attributeId: AttributeIds.Value });

async function createEventSubscription(c, notifier, events, condition = false) {
    const selectClauses = ["EventId", "EventType", "SourceNode", "Time", "Message", "Severity"].map((n) => operand("i=2041", n));
    if (condition) {
        selectClauses.push(new SimpleAttributeOperand({ typeDefinitionId: coerceNodeId("i=2782"), browsePath: [], attributeId: AttributeIds.NodeId }));
        selectClauses.push(operand("i=2881", "AckedState", "Id"));
        selectClauses.push(operand("i=2782", "Retain"));
    }
    const filter = new EventFilter({ selectClauses, whereClause: new ContentFilter({ elements: [new ContentFilterElement({
        filterOperator: FilterOperator.OfType,
        filterOperands: [new LiteralOperand({ value: new Variant({ dataType: DataType.NodeId, value: coerceNodeId(condition ? "i=2881" : "i=2041") }) })]
    })] }) });
    const sub = await createSubscription(c, 100);
    try {
        await monitor(sub, notifier, { samplingInterval: 0, queueSize: 100, discardOldest: true, filter }, (fields) => events.push(fields),
            MonitoringMode.Reporting, AttributeIds.EventNotifier);
    } catch (e) {
        await terminate(sub);
        throw e;
    }
    return sub;
}

// Republish answers with a service fault or a response header status; both count.
function republishStatus(c, subscriptionId, retransmitSequenceNumber) {
    return new Promise((resolve) => c.session.republish(new RepublishRequest({ subscriptionId, retransmitSequenceNumber }), (err, response) => {
        if (err) resolve(/Bad\w+/.exec(err.message)?.[0] ?? response?.responseHeader?.serviceResult?.name ?? err.message);
        else resolve(response.responseHeader.serviceResult.name);
    }));
}

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

const identityOf = (o) => (o.user ? { type: UserTokenType.UserName, userName: o.user, password: o.password ?? "" } : { type: UserTokenType.Anonymous });

// A check that lost the connection must not fail the later ones: open a new
// channel and session for them.
async function reconnectIfLost(ctx, o) {
    if (ctx.session.isChannelValid()) return;
    try {
        console.log("INFO the connection was lost; reconnecting for the remaining checks");
        const client = ctx.createClient();
        ctx.clients.push(client);
        await client.connect(o.url);
        ctx.session = await client.createSession(identityOf(o));
        ctx.client = client;
    } catch (e) {
        console.log("INFO reconnect failed: " + e.message);
    }
}

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
    const createClient = () => OPCUAClient.create({
        applicationName: name, applicationUri: `urn:localhost:opcfoundation.org:${name}`, clientCertificateManager,
        securityPolicy: policy, securityMode: mode, endpointMustExist: false, connectionStrategy: { maxRetry: 0 },
        transportSettings: { maxMessageSize: 16 * 1024 * 1024, maxChunkCount: 0 },
        ...(o["token-lifetime"] ? { defaultSecureTokenLifetime: parseInt(o["token-lifetime"], 10) } : {})
    });
    let client = createClient();
    const clients = [client];
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
            session = await client.createSession(identityOf(o));
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
        const ctx = {
            session, client, clients, createClient, url: o.url, ns, alarmsNs: nsArray.indexOf(ALARMS_NAMESPACE), options: o,
            id: (s) => new NodeId(NodeId.NodeIdType.STRING, s, ns)
        };
        try {
            for (const [checkName, fn] of Object.entries(CHECKS)) {
                if (!selected.includes(checkName)) continue;
                await reconnectIfLost(ctx, o);
                await checks.run(checkName, () => fn(ctx));
            }
        } finally {
            await checks.run("CloseSession", () => ctx.session.close());
        }
    }
    for (const c of clients) await c.disconnect().catch(() => { });
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
