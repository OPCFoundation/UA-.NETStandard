/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

package org.opcfoundation.interop.milo;

import static org.eclipse.milo.opcua.stack.core.types.builtin.unsigned.Unsigned.uint;

import java.nio.file.Path;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Random;
import java.util.Set;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import java.util.concurrent.atomic.AtomicInteger;
import org.eclipse.milo.opcua.sdk.client.OpcUaClient;
import org.eclipse.milo.opcua.sdk.client.subscriptions.OpcUaMonitoredItem;
import org.eclipse.milo.opcua.sdk.client.subscriptions.OpcUaSubscription;
import org.eclipse.milo.opcua.sdk.core.types.DynamicType;
import org.eclipse.milo.opcua.stack.core.AttributeId;
import org.eclipse.milo.opcua.stack.core.NodeIds;
import org.eclipse.milo.opcua.stack.core.StatusCodes;
import org.eclipse.milo.opcua.stack.core.UaException;
import org.eclipse.milo.opcua.stack.core.types.UaStructuredType;
import org.eclipse.milo.opcua.stack.core.types.builtin.ByteString;
import org.eclipse.milo.opcua.stack.core.types.builtin.DataValue;
import org.eclipse.milo.opcua.stack.core.types.builtin.DateTime;
import org.eclipse.milo.opcua.stack.core.types.builtin.ExtensionObject;
import org.eclipse.milo.opcua.stack.core.types.builtin.NodeId;
import org.eclipse.milo.opcua.stack.core.types.builtin.QualifiedName;
import org.eclipse.milo.opcua.stack.core.types.builtin.StatusCode;
import org.eclipse.milo.opcua.stack.core.types.builtin.Variant;
import org.eclipse.milo.opcua.stack.core.types.builtin.unsigned.UInteger;
import org.eclipse.milo.opcua.stack.core.types.enumerated.BrowseDirection;
import org.eclipse.milo.opcua.stack.core.types.enumerated.BrowseResultMask;
import org.eclipse.milo.opcua.stack.core.types.enumerated.MessageSecurityMode;
import org.eclipse.milo.opcua.stack.core.types.enumerated.NodeClass;
import org.eclipse.milo.opcua.stack.core.types.enumerated.ServerState;
import org.eclipse.milo.opcua.stack.core.types.enumerated.TimestampsToReturn;
import org.eclipse.milo.opcua.stack.core.types.structured.BrowseDescription;
import org.eclipse.milo.opcua.stack.core.types.structured.BrowsePath;
import org.eclipse.milo.opcua.stack.core.types.structured.BrowseResult;
import org.eclipse.milo.opcua.stack.core.types.structured.BrowsePathResult;
import org.eclipse.milo.opcua.stack.core.types.structured.CallMethodRequest;
import org.eclipse.milo.opcua.stack.core.types.structured.CallMethodResult;
import org.eclipse.milo.opcua.stack.core.types.structured.ReadValueId;
import org.eclipse.milo.opcua.stack.core.types.structured.ReferenceDescription;
import org.eclipse.milo.opcua.stack.core.types.structured.RelativePath;
import org.eclipse.milo.opcua.stack.core.types.structured.RelativePathElement;
import org.eclipse.milo.opcua.stack.core.types.structured.ServerStatusDataType;
import org.eclipse.milo.opcua.stack.core.types.structured.ViewDescription;
import org.eclipse.milo.opcua.stack.core.types.structured.WriteValue;

/**
 * The client checks of the Milo peer against a server with the Quickstarts reference server
 * address space, the same checks as the 1.5.378 peer's LegacyClientChecks.
 */
final class ClientChecks {

  static final String REFERENCE_NAMESPACE = "http://opcfoundation.org/Quickstarts/ReferenceServer";

  interface Check {
    void run(ClientChecks c) throws Exception;
  }

  private static final Map<String, Check> CHECKS = new LinkedHashMap<>();

  static {
    CHECKS.put("NamespaceArray", ClientChecks::namespaceArray);
    CHECKS.put("ReadServerStatusStructure", ClientChecks::readServerStatus);
    CHECKS.put("BrowseObjectsFolder", ClientChecks::browseObjectsFolder);
    CHECKS.put("TranslateBrowsePath", ClientChecks::translateBrowsePath);
    CHECKS.put("ReadScalars", ClientChecks::readScalars);
    CHECKS.put("ReadAttributes", ClientChecks::readAttributes);
    CHECKS.put("WriteAndReadBack", ClientChecks::writeAndReadBack);
    CHECKS.put("CallMethods", ClientChecks::callMethods);
    CHECKS.put("Subscription", ClientChecks::subscription);
    CHECKS.put("ComplexTypes", ClientChecks::complexTypes);
    CHECKS.put("LargeArrayRoundTrip", ClientChecks::largeArrayRoundTrip);
    CHECKS.put("LargeByteStringRoundTrip", ClientChecks::largeByteStringRoundTrip);
    CHECKS.put("OversizedRequestRejected", ClientChecks::oversizedRequestRejected);
    CHECKS.put("BrowseContinuationPoints", ClientChecks::browseContinuationPoints);
    CHECKS.put("ReadManyNodes", ClientChecks::readManyNodes);
    CHECKS.put("TokenRenewal", ClientChecks::tokenRenewal);
  }

  private final OpcUaClient client;
  private final Map<String, String> options;
  private int ns;
  private int passed;
  private int failed;

  private ClientChecks(OpcUaClient client, Map<String, String> options) {
    this.client = client;
    this.options = options;
  }

  static int run(Map<String, String> options) throws Exception {
    Path pki = Path.of(Peer.required(options, "pki"));
    String policy =
        options.getOrDefault("policy", "http://opcfoundation.org/UA/SecurityPolicy#None");
    var mode = MessageSecurityMode.valueOf(options.getOrDefault("mode", "None"));
    List<String> selected =
        Peer.split(
            options.getOrDefault(
                "checks", String.join(",", new ArrayList<>(CHECKS.keySet()).subList(0, 9))));
    List<String> unknown =
        selected.stream()
            .filter(c -> !CHECKS.containsKey(c) && !c.equals("Connect") && !c.equals("CloseSession"))
            .toList();
    if (!unknown.isEmpty()) {
      throw new IllegalArgumentException("unknown checks: " + String.join(", ", unknown));
    }
    List<String> expectedErrors = Peer.split(options.getOrDefault("expect-connect-error", ""));
    long deadline = Long.parseLong(options.getOrDefault("timeout-seconds", "300"));

    OpcUaClient client = Peer.createClient(options, pki, policy, mode);
    if (Peer.flag(options, "init-only", false)) {
      System.out.println("PEER-PKI-READY " + client.getConfig().getApplicationUri());
      return Peer.EXIT_SUCCESS;
    }
    var checks = new ClientChecks(client, options);

    // The checks run on a worker so a hanging check is cut off by the deadline
    // and still reported, before the harness kills the process.
    ExecutorService worker = Executors.newSingleThreadExecutor(r -> {
      var thread = new Thread(r, "checks");
      thread.setDaemon(true);
      return thread;
    });
    Future<?> run = worker.submit(() -> {
      boolean connected = checks.check("Connect", c -> c.connect(expectedErrors));
      if (!connected || !expectedErrors.isEmpty()) {
        return null;
      }
      try {
        checks.ns = client.readNamespaceTable().getIndex(REFERENCE_NAMESPACE) == null
            ? -1
            : client.getNamespaceTable().getIndex(REFERENCE_NAMESPACE).intValue();
        for (var entry : CHECKS.entrySet()) {
          if (selected.contains(entry.getKey())) {
            checks.check(entry.getKey(), entry.getValue());
          }
        }
      } finally {
        checks.check("CloseSession", c -> c.client.disconnect());
      }
      return null;
    });
    try {
      run.get(deadline, TimeUnit.SECONDS);
    } catch (TimeoutException e) {
      System.out.println("INFO the checks did not complete within " + deadline + " s");
    }
    System.out.println("SUMMARY passed=" + checks.passed + " failed=" + checks.failed);
    return checks.failed == 0 ? Peer.EXIT_SUCCESS : Peer.EXIT_CHECKS_FAILED;
  }

  private boolean check(String name, Check check) {
    long started = System.nanoTime();
    String outcome = "Passed";
    String message = "";
    try {
      check.run(this);
      passed++;
    } catch (Throwable t) {
      failed++;
      outcome = "Failed";
      message = describe(t);
    }
    var result = new LinkedHashMap<String, Object>();
    result.put("check", name);
    result.put("outcome", outcome);
    result.put("message", message);
    result.put("milliseconds", (System.nanoTime() - started) / 1_000_000);
    System.out.println("RESULT " + Peer.Json.object(result));
    System.out.flush();
    return outcome.equals("Passed");
  }

  private static String describe(Throwable t) {
    StatusCode status = UaException.extractStatusCode(t).orElse(null);
    String prefix = status == null ? t.getClass().getSimpleName() : statusName(status);
    var sb = new StringBuilder(prefix + " " + t.getMessage());
    if (!(t instanceof IllegalStateException)) {
      // Where an unexpected failure came from, in this peer's frames.
      for (StackTraceElement frame : t.getStackTrace()) {
        if (frame.getClassName().startsWith("org.opcfoundation") || frame.getClassName().contains("typetree")) {
          sb.append("\n   at ").append(frame);
        }
      }
    }
    return sb.toString();
  }

  static String statusName(StatusCode status) {
    return StatusCodes.lookup(status.value()).map(n -> n[0]).orElse(status.toString());
  }

  static void require(boolean condition, String message) {
    if (!condition) {
      throw new IllegalStateException(message);
    }
  }

  private void connect(List<String> expectedErrors) throws Exception {
    try {
      client.connect();
    } catch (Exception e) {
      if (!expectedErrors.isEmpty()) {
        String actual =
            UaException.extractStatusCode(e).map(ClientChecks::statusName).orElse(e.toString());
        require(
            expectedErrors.contains(actual),
            "expected " + expectedErrors + ", the connect failed with " + actual + ": " + e);
        return;
      }
      throw e;
    }
    require(
        expectedErrors.isEmpty(),
        "expected the connect to fail with " + expectedErrors + ", but it succeeded");
  }

  // ------------------------------------------------------------------ helpers

  private NodeId id(String name) {
    return new NodeId(ns, name);
  }

  private static ReadValueId value(NodeId nodeId) {
    return attribute(nodeId, AttributeId.Value);
  }

  private static ReadValueId attribute(NodeId nodeId, AttributeId attributeId) {
    return new ReadValueId(nodeId, attributeId.uid(), null, QualifiedName.NULL_VALUE);
  }

  private DataValue[] read(List<ReadValueId> nodes) throws UaException {
    var results = new ArrayList<DataValue>();
    for (int offset = 0; offset < nodes.size(); offset += 500) {
      results.addAll(
          Arrays.asList(
              client
                  .read(0.0, TimestampsToReturn.Both, nodes.subList(offset, Math.min(nodes.size(), offset + 500)))
                  .getResults()));
    }
    return results.toArray(DataValue[]::new);
  }

  private StatusCode write(NodeId nodeId, Variant value) throws UaException {
    return client
        .write(List.of(new WriteValue(nodeId, AttributeId.Value.uid(), null, DataValue.valueOnly(value))))
        .getResults()[0];
  }

  private static BrowseDescription browseAll(NodeId nodeId) {
    return new BrowseDescription(
        nodeId,
        BrowseDirection.Forward,
        NodeIds.HierarchicalReferences,
        true,
        uint(0),
        uint(BrowseResultMask.All.getValue()));
  }

  private static boolean hasContinuationPoint(ByteString cp) {
    return cp != null && cp.isNotNull() && cp.length() > 0;
  }

  private BrowseResult browseFirst(NodeId nodeId, int maxReferences) throws UaException {
    var view = new ViewDescription(NodeId.NULL_VALUE, DateTime.MIN_DATE_TIME, uint(0));
    BrowseResult result =
        client.browse(view, uint(maxReferences), List.of(browseAll(nodeId))).getResults()[0];
    require(result.getStatusCode().isGood(), "browse of " + nodeId + " returned " + result.getStatusCode());
    return result;
  }

  /** Browses one node, following continuation points. */
  private List<ReferenceDescription> browse(NodeId nodeId, int maxReferences) throws UaException {
    BrowseResult result = browseFirst(nodeId, maxReferences);
    var references = new ArrayList<ReferenceDescription>();
    references.addAll(Arrays.asList(orEmpty(result.getReferences())));
    while (hasContinuationPoint(result.getContinuationPoint())) {
      result = client.browseNext(false, List.of(result.getContinuationPoint())).getResults()[0];
      require(result.getStatusCode().isGood(), "browse next of " + nodeId + " returned " + result.getStatusCode());
      references.addAll(Arrays.asList(orEmpty(result.getReferences())));
    }
    return references;
  }

  private String browseName(NodeId nodeId) {
    try {
      Object name = read(List.of(attribute(nodeId, AttributeId.BrowseName)))[0].value().value();
      return nodeId + " " + (name instanceof QualifiedName qn ? qn.name() : name);
    } catch (UaException e) {
      return nodeId.toString();
    }
  }

  private static ReferenceDescription[] orEmpty(ReferenceDescription[] references) {
    return references == null ? new ReferenceDescription[0] : references;
  }

  // ------------------------------------------------------------------ checks

  private static void namespaceArray(ClientChecks c) {
    require(c.ns > 0, "reference server namespace is missing");
  }

  private static void readServerStatus(ClientChecks c) throws Exception {
    DataValue value = c.client.readValue(0.0, TimestampsToReturn.Both, NodeIds.Server_ServerStatus);
    require(value.statusCode().isGood(), "status " + value.statusCode());
    var status =
        (ServerStatusDataType)
            ((ExtensionObject) value.value().value()).decode(c.client.getStaticEncodingContext());
    require(status.getState() == ServerState.Running, "server state " + status.getState());
    require(
        status.getBuildInfo() != null && status.getBuildInfo().getProductUri() != null
            && !status.getBuildInfo().getProductUri().isEmpty(),
        "BuildInfo is empty");
  }

  private static void browseObjectsFolder(ClientChecks c) throws Exception {
    Set<String> names = new HashSet<>();
    for (ReferenceDescription r : c.browse(NodeIds.ObjectsFolder, 0)) {
      names.add(r.getBrowseName().name());
    }
    require(names.contains("Server"), "Server object not found");
    require(names.contains("CTT"), "CTT folder not found: " + names);
  }

  private static void translateBrowsePath(ClientChecks c) throws Exception {
    RelativePathElement[] elements =
        List.of("Server", "ServerStatus", "CurrentTime").stream()
            .map(n -> new RelativePathElement(NodeIds.HierarchicalReferences, false, true, new QualifiedName(0, n)))
            .toArray(RelativePathElement[]::new);
    BrowsePathResult result =
        c.client
            .translateBrowsePaths(List.of(new BrowsePath(NodeIds.ObjectsFolder, new RelativePath(elements))))
            .getResults()[0];
    require(result.getStatusCode().isGood(), "status " + result.getStatusCode());
    NodeId target =
        result.getTargets()[0].getTargetId().toNodeId(c.client.getNamespaceTable()).orElse(null);
    require(NodeIds.Server_ServerStatus_CurrentTime.equals(target), "unexpected target " + target);
  }

  private static void readScalars(ClientChecks c) throws Exception {
    var nodes = new ArrayList<ReadValueId>();
    for (String name :
        List.of("Boolean", "Int32", "Double", "String", "DateTime", "Guid", "ByteString",
            "LocalizedText", "QualifiedName", "NodeId", "Arrays_Int32", "Arrays_String")) {
      nodes.add(value(c.id("Scalar_Static_" + name)));
    }
    DataValue[] values = c.read(nodes);
    require(values.length == nodes.size(), "result count " + values.length);
    for (int ii = 0; ii < values.length; ii++) {
      require(values[ii].statusCode().isGood(), nodes.get(ii).getNodeId() + ": " + values[ii].statusCode());
    }
  }

  private static void readAttributes(ClientChecks c) throws Exception {
    NodeId nodeId = c.id("Scalar_Static_Int32");
    DataValue[] values =
        c.read(List.of(
            attribute(nodeId, AttributeId.NodeClass),
            attribute(nodeId, AttributeId.BrowseName),
            attribute(nodeId, AttributeId.DataType)));
    require(
        Integer.valueOf(NodeClass.Variable.getValue()).equals(values[0].value().value()),
        "NodeClass " + values[0].value().value());
    require(
        ((QualifiedName) values[1].value().value()).name().equals("Scalar_Static_Int32"),
        "BrowseName " + values[1].value().value());
    require(NodeIds.Int32.equals(values[2].value().value()), "DataType " + values[2].value().value());
  }

  private static void writeAndReadBack(ClientChecks c) throws Exception {
    NodeId int32 = c.id("Scalar_Static_Int32");
    NodeId string = c.id("Scalar_Static_String");
    NodeId doubles = c.id("Scalar_Static_Arrays_Double");
    String text = "written by Milo äöü";
    Double[] values = {1.5, -2.25, 1e300};
    require(c.write(int32, new Variant(1234567)).isGood(), "Int32 write");
    require(c.write(string, new Variant(text)).isGood(), "String write");
    require(c.write(doubles, new Variant(values)).isGood(), "Double[] write");
    DataValue[] read = c.read(List.of(value(int32), value(string), value(doubles)));
    require(Integer.valueOf(1234567).equals(read[0].value().value()), "Int32 read back " + read[0].value());
    require(text.equals(read[1].value().value()), "String read back " + read[1].value());
    require(
        Arrays.equals(values, (Object[]) read[2].value().value()),
        "Double[] read back " + read[2].value());
  }

  private static void callMethods(ClientChecks c) throws Exception {
    NodeId methods = c.id("Methods");
    CallMethodResult[] results =
        c.client
            .call(List.of(
                new CallMethodRequest(methods, c.id("Methods_Hello"), new Variant[] {new Variant("milo")}),
                new CallMethodRequest(methods, c.id("Methods_Add"),
                    new Variant[] {new Variant(1.5f), new Variant(uint(2))})))
            .getResults();
    require(results[0].getStatusCode().isGood(), "Hello " + results[0].getStatusCode());
    require("hello milo".equals(results[0].getOutputArguments()[0].value()),
        "Hello returned " + results[0].getOutputArguments()[0]);
    require(results[1].getStatusCode().isGood(), "Add " + results[1].getStatusCode());
    require(Float.valueOf(3.5f).equals(results[1].getOutputArguments()[0].value()),
        "Add returned " + results[1].getOutputArguments()[0]);
  }

  private static void subscription(ClientChecks c) throws Exception {
    var subscription = new OpcUaSubscription(c.client, 100.0);
    subscription.setMaxKeepAliveCount(uint(10));
    subscription.setLifetimeCount(uint(100));
    subscription.create();
    try {
      var notifications = new AtomicInteger();
      var received = new CountDownLatch(3);
      var item = OpcUaMonitoredItem.newDataItem(NodeIds.Server_ServerStatus_CurrentTime);
      item.setSamplingInterval(100.0);
      item.setQueueSize(uint(10));
      item.setDataValueListener((i, v) -> {
        notifications.incrementAndGet();
        received.countDown();
      });
      subscription.addMonitoredItem(item);
      subscription.synchronizeMonitoredItems();
      require(item.getCreateResult().map(StatusCode::isGood).orElse(false),
          "monitored item " + item.getCreateResult());
      require(received.await(15, TimeUnit.SECONDS),
          "only " + notifications.get() + " data change notifications in 15 s");
    } finally {
      subscription.delete();
    }
  }

  /**
   * Decodes every variable below Objects whose data type is a custom type with the dynamic
   * (DataTypeDefinition based) codecs and writes up to 50 of the structures back re-encoded.
   */
  private static void complexTypes(ClientChecks c) throws Exception {
    var variables = new ArrayList<NodeId>();
    var visited = new HashSet<NodeId>(List.of(NodeIds.ObjectsFolder));
    var queue = new ArrayDeque<NodeId>(List.of(NodeIds.ObjectsFolder));
    while (!queue.isEmpty() && visited.size() < 30_000) {
      NodeId nodeId = queue.poll();
      if (nodeId.equals(NodeIds.Server)) {
        continue;
      }
      for (ReferenceDescription r : c.browse(nodeId, 0)) {
        NodeId target = r.getNodeId().toNodeId(c.client.getNamespaceTable()).orElse(null);
        if (target != null && visited.add(target)) {
          queue.add(target);
          if (r.getNodeClass() == NodeClass.Variable && target.getNamespaceIndex().intValue() != 0) {
            variables.add(target);
          }
        }
      }
    }
    DataValue[] dataTypes = c.read(variables.stream().map(n -> attribute(n, AttributeId.DataType)).toList());
    var custom = new ArrayList<NodeId>();
    for (int ii = 0; ii < variables.size(); ii++) {
      if (dataTypes[ii].value().value() instanceof NodeId dt && dt.getNamespaceIndex().intValue() != 0) {
        custom.add(variables.get(ii));
      }
    }
    require(custom.size() >= 10, "only " + custom.size() + " variables with a custom data type found");
    DataValue[] values = c.read(custom.stream().map(ClientChecks::value).toList());
    DataValue[] access = c.read(custom.stream().map(n -> attribute(n, AttributeId.UserAccessLevel)).toList());

    var context = c.client.getDynamicEncodingContext();
    var typeNames = new HashSet<String>();
    var undecoded = new ArrayList<String>();
    var writeIds = new ArrayList<NodeId>();
    var writeValues = new ArrayList<Variant>();
    int structures = 0;
    for (int ii = 0; ii < custom.size(); ii++) {
      Object raw = values[ii].value().value();
      if (!values[ii].statusCode().isGood()) {
        continue;
      }
      ExtensionObject[] items =
          raw instanceof ExtensionObject xo ? new ExtensionObject[] {xo}
              : raw instanceof ExtensionObject[] xos ? xos : new ExtensionObject[0];
      var decoded = new ArrayList<Object>();
      for (ExtensionObject xo : items) {
        if (xo == null) {
          continue;
        }
        structures++;
        try {
          Object value = xo.decode(context);
          decoded.add(value);
          typeNames.add(value instanceof DynamicType dt ? dt.getTypeName() : value.getClass().getSimpleName());
        } catch (Exception e) {
          NodeId dataType = (NodeId) dataTypes[variables.indexOf(custom.get(ii))].value().value();
          undecoded.add(custom.get(ii) + " of DataType " + dataType + " [" + dataType.getType() + "] ("
              + e.getMessage() + ")");
        }
      }
      boolean writable = access[ii].value().value() instanceof Number level && (level.intValue() & 2) != 0;
      if (writeIds.size() < 50 && writable && !decoded.isEmpty() && decoded.size() == items.length) {
        Object reEncoded = raw instanceof ExtensionObject
            ? ExtensionObject.encode(context, (UaStructuredType) decoded.get(0))
            : decoded.stream().map(d -> ExtensionObject.encode(context, (UaStructuredType) d)).toArray(ExtensionObject[]::new);
        writeIds.add(custom.get(ii));
        writeValues.add(new Variant(reEncoded));
      }
    }
    require(structures > 0, "no structure values were read");
    require(undecoded.isEmpty(), undecoded.size() + " of " + structures + " structures were not decoded: "
        + String.join("; ", undecoded.subList(0, Math.min(10, undecoded.size()))));
    for (String required : List.of("ScalarStructureDataType", "VectorUnion", "VectorWithOptionalFields")) {
      require(typeNames.stream().anyMatch(n -> n.endsWith(required)),
          "no " + required + " value was decoded; decoded: " + typeNames);
    }
    require(!writeIds.isEmpty(), "no writable structure variable found");
    var rejected = new ArrayList<String>();
    for (int ii = 0; ii < writeIds.size(); ii++) {
      String status;
      try {
        StatusCode result = c.write(writeIds.get(ii), writeValues.get(ii));
        status = result.isGood() ? null : statusName(result);
      } catch (UaException e) {
        // The whole request was rejected: name the structure and show where
        // this stack's re-encoding differs from the server's encoding.
        status = statusName(e.getStatusCode()) + " for the request; " + encodingDifference(
            values[custom.indexOf(writeIds.get(ii))].value().value(), writeValues.get(ii).value(), context);
      }
      if (status != null) {
        rejected.add(writeIds.get(ii) + ": " + status);
      }
    }
    require(rejected.isEmpty(), rejected.size() + " of " + writeIds.size() + " structure writes were rejected: "
        + String.join("; ", rejected.subList(0, Math.min(10, rejected.size()))));
    DataValue[] readBack = c.read(writeIds.stream().map(ClientChecks::value).toList());
    var changed = new ArrayList<String>();
    for (int ii = 0; ii < writeIds.size(); ii++) {
      if (!String.valueOf(decodeAll(context, writeValues.get(ii).value()))
          .equals(String.valueOf(decodeAll(context, readBack[ii].value().value())))) {
        changed.add(writeIds.get(ii).toString());
      }
    }
    require(changed.isEmpty(), changed.size() + " structures changed in a write and read round trip: "
        + String.join("; ", changed.subList(0, Math.min(10, changed.size()))));
  }

  /** The type and the first differing byte of the server's and this stack's binary bodies. */
  private static String encodingDifference(
      Object original, Object reEncoded, org.eclipse.milo.opcua.stack.core.encoding.EncodingContext context) {
    ExtensionObject[] a = original instanceof ExtensionObject[] xs ? xs : new ExtensionObject[] {(ExtensionObject) original};
    ExtensionObject[] b = reEncoded instanceof ExtensionObject[] ys ? ys : new ExtensionObject[] {(ExtensionObject) reEncoded};
    for (int ii = 0; ii < Math.min(a.length, b.length); ii++) {
      if (a[ii] == null || b[ii] == null) {
        continue;
      }
      byte[] x = ((ByteString) a[ii].getBody()).bytesOrEmpty();
      byte[] y = ((ByteString) b[ii].getBody()).bytesOrEmpty();
      int at = Arrays.mismatch(x, y);
      if (at >= 0) {
        Object decoded = a[ii].decode(context);
        String type = decoded instanceof DynamicType dt ? dt.getTypeName() : decoded.getClass().getSimpleName();
        return type + " element " + ii + ": server " + x.length + " bytes, re-encoded " + y.length
            + " bytes, first difference at byte " + at + " (server " + hex(x, at) + " / re-encoded " + hex(y, at)
            + "); value " + decoded;
      }
    }
    return "the re-encoded bodies equal the server's";
  }

  private static String hex(byte[] bytes, int at) {
    var sb = new StringBuilder();
    for (int ii = Math.max(0, at - 4); ii < Math.min(bytes.length, at + 12); ii++) {
      sb.append(String.format(ii == at ? "[%02x]" : "%02x", bytes[ii]));
    }
    return sb.toString();
  }

  private static Object decodeAll(org.eclipse.milo.opcua.stack.core.encoding.EncodingContext context, Object value) {
    if (value instanceof ExtensionObject xo) {
      return xo.decode(context);
    }
    if (value instanceof ExtensionObject[] xos) {
      return Arrays.stream(xos).map(xo -> xo == null ? null : xo.decode(context)).toList();
    }
    return value;
  }

  private static void largeArrayRoundTrip(ClientChecks c) throws Exception {
    // 200 000 Int32 = 800 kB, far more than one 64 kB message chunk.
    Integer[] values = new Integer[200_000];
    for (int i = 0; i < values.length; i++) {
      values[i] = (i * 7919) ^ 0x5A5A;
    }
    c.writeAndCompare(c.id("Scalar_Static_Arrays_Int32"), new Variant(values));
  }

  private static void largeByteStringRoundTrip(ClientChecks c) throws Exception {
    // 3 MB, below the 4 MB ByteString limit of the reference server fixture.
    byte[] value = new byte[3 * 1024 * 1024];
    new Random(4711).nextBytes(value);
    c.writeAndCompare(c.id("Scalar_Static_ByteString"), new Variant(ByteString.of(value)));
  }

  private void writeAndCompare(NodeId nodeId, Variant value) throws Exception {
    StatusCode status = write(nodeId, value);
    require(status.isGood(), "write of " + nodeId + " returned " + statusName(status));
    DataValue read = read(List.of(value(nodeId)))[0];
    require(read.statusCode().isGood(), "read of " + nodeId + " returned " + read.statusCode());
    Object expected = value.value();
    Object actual = read.value().value();
    boolean equal = expected instanceof Object[] e && actual instanceof Object[] a
        ? Arrays.equals(e, a)
        : expected.equals(actual);
    require(equal, nodeId + " read back a different value");
  }

  /**
   * A ByteString above the server's MaxByteStringLength but inside its MaxMessageSize must be
   * rejected with a status code; the session stays usable.
   */
  private static void oversizedRequestRejected(ClientChecks c) throws Exception {
    String result;
    try {
      StatusCode status = c.write(c.id("Scalar_Static_ByteString"), new Variant(ByteString.of(new byte[5 * 1024 * 1024])));
      require(!status.isGood(), "a 5 MB ByteString write was accepted");
      result = statusName(status);
    } catch (UaException e) {
      result = statusName(e.getStatusCode());
    }
    DataValue state = c.client.readValue(0.0, TimestampsToReturn.Neither, NodeIds.Server_ServerStatus_State);
    require(state.statusCode().isGood(),
        "the session is unusable after the rejected write (" + result + "): " + state.statusCode());
    System.out.println("INFO OversizedRequestRejected: " + result);
  }

  /**
   * Browses a folder three references at a time with BrowseNext, compares with a browse without
   * limit, then releases an open continuation point.
   */
  private static void browseContinuationPoints(ClientChecks c) throws Exception {
    NodeId folder = c.id("Scalar_Static");
    List<String> all = c.browse(folder, 0).stream().map(r -> r.getNodeId().toString()).toList();
    require(all.size() > 10, "only " + all.size() + " references below " + folder);
    List<String> paged = c.browse(folder, 3).stream().map(r -> r.getNodeId().toString()).toList();
    require(paged.equals(all), "paged browse returned " + paged.size() + " of " + all.size() + " references or a different order");
    BrowseResult first = c.browseFirst(folder, 3);
    require(hasContinuationPoint(first.getContinuationPoint()), "no continuation point returned");
    BrowseResult released = c.client.browseNext(true, List.of(first.getContinuationPoint())).getResults()[0];
    require(released.getStatusCode().isGood(), "releasing the continuation point returned " + released.getStatusCode());
    BrowseResult reused = c.client.browseNext(false, List.of(first.getContinuationPoint())).getResults()[0];
    require(reused.getStatusCode().value() == StatusCodes.Bad_ContinuationPointInvalid,
        "a released continuation point returned " + statusName(reused.getStatusCode()));
  }

  /**
   * Reads 2 * MaxNodesPerRead + 1 nodes split by the server's limit, then in one request, which
   * the server must reject with BadTooManyOperations.
   */
  private static void readManyNodes(ClientChecks c) throws Exception {
    Object limitValue = c.client.readValue(0.0, TimestampsToReturn.Neither,
        NodeIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead).value().value();
    int limit = limitValue instanceof UInteger u ? u.intValue() : 0;
    require(limit > 0, "the server published no MaxNodesPerRead");
    var nodes = new ArrayList<ReadValueId>();
    for (int ii = 0; ii < limit * 2 + 1; ii++) {
      nodes.add(value(NodeIds.Server_ServerStatus_State));
    }
    int good = 0;
    for (int offset = 0; offset < nodes.size(); offset += limit) {
      for (DataValue dv : c.client.read(0.0, TimestampsToReturn.Neither,
          nodes.subList(offset, Math.min(nodes.size(), offset + limit))).getResults()) {
        good += dv.statusCode().isGood() ? 1 : 0;
      }
    }
    require(good == nodes.size(), good + " of " + nodes.size() + " reads split by MaxNodesPerRead " + limit + " succeeded");
    String tooMany;
    try {
      c.client.read(0.0, TimestampsToReturn.Neither, nodes);
      tooMany = "Good";
    } catch (UaException e) {
      tooMany = statusName(e.getStatusCode());
    }
    require(tooMany.equals("Bad_TooManyOperations") || tooMany.equals("BadTooManyOperations"),
        "reading " + nodes.size() + " nodes in one request with MaxNodesPerRead " + limit + " returned " + tooMany);
  }

  /** Keeps reading for longer than the revised token lifetime (renewal at 75 %). */
  private static void tokenRenewal(ClientChecks c) throws Exception {
    long seconds = Long.parseLong(c.options.getOrDefault("token-test-seconds", "65"));
    long started = System.nanoTime();
    int reads = 0;
    while (System.nanoTime() - started < TimeUnit.SECONDS.toNanos(seconds)) {
      DataValue value = c.client.readValue(0.0, TimestampsToReturn.Neither, NodeIds.Server_ServerStatus_CurrentTime);
      require(value.statusCode().isGood(), "read " + reads + " returned " + value.statusCode());
      reads++;
      Thread.sleep(500);
    }
  }
}
