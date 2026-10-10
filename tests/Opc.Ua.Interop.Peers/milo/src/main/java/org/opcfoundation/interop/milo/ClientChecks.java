/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

package org.opcfoundation.interop.milo;

import static org.eclipse.milo.opcua.stack.core.types.builtin.unsigned.Unsigned.ubyte;
import static org.eclipse.milo.opcua.stack.core.types.builtin.unsigned.Unsigned.uint;

import java.nio.file.Path;
import java.security.KeyPair;
import java.security.cert.X509Certificate;
import java.util.UUID;
import org.eclipse.milo.opcua.sdk.client.DiscoveryClient;
import org.eclipse.milo.opcua.sdk.client.SessionActivityListener;
import org.eclipse.milo.opcua.sdk.client.identity.AnonymousProvider;
import org.eclipse.milo.opcua.sdk.client.identity.IdentityProvider;
import org.eclipse.milo.opcua.sdk.client.identity.UsernameProvider;
import org.eclipse.milo.opcua.sdk.client.identity.X509IdentityProvider;
import org.eclipse.milo.opcua.sdk.client.subscriptions.MonitoredItemSynchronizationException;
import org.eclipse.milo.opcua.stack.core.types.builtin.LocalizedText;
import org.eclipse.milo.opcua.stack.core.types.builtin.unsigned.UShort;
import org.eclipse.milo.opcua.stack.core.types.enumerated.DataChangeTrigger;
import org.eclipse.milo.opcua.stack.core.types.enumerated.DeadbandType;
import org.eclipse.milo.opcua.stack.core.types.enumerated.FilterOperator;
import org.eclipse.milo.opcua.stack.core.types.enumerated.MonitoringMode;
import org.eclipse.milo.opcua.stack.core.types.structured.AddNodesItem;
import org.eclipse.milo.opcua.stack.core.types.structured.AddNodesResult;
import org.eclipse.milo.opcua.stack.core.types.structured.ApplicationDescription;
import org.eclipse.milo.opcua.stack.core.types.structured.ContentFilter;
import org.eclipse.milo.opcua.stack.core.types.structured.ContentFilterElement;
import org.eclipse.milo.opcua.stack.core.types.structured.DataChangeFilter;
import org.eclipse.milo.opcua.stack.core.types.structured.DataChangeNotification;
import org.eclipse.milo.opcua.stack.core.types.structured.DeleteNodesItem;
import org.eclipse.milo.opcua.stack.core.types.structured.EndpointDescription;
import org.eclipse.milo.opcua.stack.core.types.structured.EventFilter;
import org.eclipse.milo.opcua.stack.core.types.structured.HistoryData;
import org.eclipse.milo.opcua.stack.core.types.structured.HistoryReadResult;
import org.eclipse.milo.opcua.stack.core.types.structured.HistoryReadValueId;
import org.eclipse.milo.opcua.stack.core.types.structured.LiteralOperand;
import org.eclipse.milo.opcua.stack.core.types.structured.MonitoredItemNotification;
import org.eclipse.milo.opcua.stack.core.types.structured.NotificationMessage;
import org.eclipse.milo.opcua.stack.core.types.structured.ObjectAttributes;
import org.eclipse.milo.opcua.stack.core.types.structured.PublishResponse;
import org.eclipse.milo.opcua.stack.core.types.structured.ReadRawModifiedDetails;
import org.eclipse.milo.opcua.stack.core.types.structured.RepublishResponse;
import org.eclipse.milo.opcua.stack.core.types.structured.SetTriggeringResponse;
import org.eclipse.milo.opcua.stack.core.types.structured.SimpleAttributeOperand;
import org.eclipse.milo.opcua.stack.core.types.structured.SubscriptionAcknowledgement;
import org.eclipse.milo.opcua.stack.core.types.structured.TransferSubscriptionsResponse;
import org.eclipse.milo.opcua.stack.core.util.SelfSignedCertificateBuilder;
import org.eclipse.milo.opcua.stack.core.util.SelfSignedCertificateGenerator;
import org.eclipse.milo.opcua.stack.transport.client.tcp.OpcTcpClientTransport;
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
    // Event, subscription, identity and service checks (LegacyClientFeatures.cs).
    CHECKS.put("EventSubscription", ClientChecks::eventSubscription);
    CHECKS.put("ConditionRefresh", ClientChecks::conditionRefresh);
    CHECKS.put("AlarmAcknowledge", ClientChecks::alarmAcknowledge);
    CHECKS.put("DeadbandFilter", ClientChecks::deadbandFilter);
    CHECKS.put("QueueOverflow", ClientChecks::queueOverflow);
    CHECKS.put("Triggering", ClientChecks::triggering);
    CHECKS.put("Republish", ClientChecks::republish);
    CHECKS.put("TransferSubscription", ClientChecks::transferSubscription);
    CHECKS.put("WrongPasswordRejected", ClientChecks::wrongPasswordRejected);
    CHECKS.put("X509UserToken", ClientChecks::x509UserToken);
    CHECKS.put("RegisterNodes", ClientChecks::registerNodes);
    CHECKS.put("HistoryReadRaw", ClientChecks::historyReadRaw);
    CHECKS.put("NodeManagement", ClientChecks::nodeManagement);
    CHECKS.put("IndexRange", ClientChecks::indexRange);
    CHECKS.put("FindServers", ClientChecks::findServers);
    CHECKS.put("SessionReconnect", ClientChecks::sessionReconnect);
  }

  private final OpcUaClient client;
  private final Map<String, String> options;
  private int ns;
  private Path pki;
  private String policy;
  private MessageSecurityMode mode;
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
    checks.pki = pki;
    checks.policy = policy;
    checks.mode = mode;

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

  // ------------------------------------------------------------------ events

  static final String ALARMS_NAMESPACE = "http://test.org/UA/Alarms/";
  private static final long EVENT_WAIT_MS = 10_000;

  /**
   * Subscribes to BaseEventType events of the Server object and writes the reference server's
   * event trigger node, which reports one event.
   */
  private static void eventSubscription(ClientChecks c) throws Exception {
    var events = new ArrayList<Variant[]>();
    OpcUaSubscription subscription = c.createEventSubscription(NodeIds.Server, events, false);
    try {
      StatusCode write = c.write(c.id("NodeIds_Events_TriggerNode01"), new Variant(1));
      require(write.isGood(), "writing the event trigger returned " + statusName(write));
      Variant[] received =
          waitFor(events, e -> e[4].value() instanceof LocalizedText m && m.text() != null
              && m.text().contains("Trigger event"), EVENT_WAIT_MS);
      require(received != null, "no trigger event in 10 s; received " + count(events) + " events");
      require(received[0].value() instanceof ByteString id && id.length() > 0, "the event has no EventId");
      require(received[5].value() instanceof UShort severity && severity.intValue() > 0,
          "the event has no Severity: " + received[5]);
    } finally {
      deleteQuietly(subscription);
    }
  }

  /** Calls ConditionRefresh and expects the RefreshStartEvent and RefreshEndEvent pair. */
  private static void conditionRefresh(ClientChecks c) throws Exception {
    var events = new ArrayList<Variant[]>();
    OpcUaSubscription subscription = c.createEventSubscription(NodeIds.Server, events, false);
    try {
      UInteger id = subscription.getSubscriptionId().orElseThrow();
      CallMethodResult result =
          c.call(NodeIds.ConditionType, NodeIds.ConditionType_ConditionRefresh, new Variant(id));
      require(result.getStatusCode().isGood(), "ConditionRefresh returned " + statusName(result.getStatusCode()));
      Variant[] end = waitFor(events, e -> NodeIds.RefreshEndEventType.equals(e[1].value()), EVENT_WAIT_MS);
      require(end != null, "no RefreshEndEvent received; received " + count(events) + " events");
      synchronized (events) {
        require(events.stream().anyMatch(e -> NodeIds.RefreshStartEventType.equals(e[1].value())),
            "no RefreshStartEvent received");
      }
    } finally {
      deleteQuietly(subscription);
    }
  }

  /**
   * Starts the alarm simulation of the reference server, waits for an unacknowledged condition
   * event and acknowledges it.
   */
  private static void alarmAcknowledge(ClientChecks c) throws Exception {
    UShort alarmsNs = c.client.getNamespaceTable().getIndex(ALARMS_NAMESPACE);
    require(alarmsNs != null, "the Alarms namespace is missing");
    var folder = new NodeId(alarmsNs, "Alarms");
    var events = new ArrayList<Variant[]>();
    OpcUaSubscription subscription = c.createEventSubscription(folder, events, true);
    try {
      CallMethodResult started = c.call(folder, new NodeId(alarmsNs, "Alarms.Start"), new Variant(uint(30)));
      require(started.getStatusCode().isGood(), "Alarms.Start returned " + statusName(started.getStatusCode()));
      // Fields: 0 EventId, 1 EventType, 2 SourceNode, 3 Time, 4 Message,
      // 5 Severity, 6 ConditionId, 7 AckedState/Id, 8 Retain.
      Variant[] unacked =
          waitFor(events, e -> e[6].value() instanceof NodeId
              && Boolean.FALSE.equals(e[7].value()) && Boolean.TRUE.equals(e[8].value()), 20_000);
      if (unacked == null) {
        Variant[] sample;
        synchronized (events) {
          sample = events.isEmpty() ? null : events.get(events.size() - 1);
        }
        throw new IllegalStateException("no unacknowledged condition in 20 s; received " + count(events)
            + " events, last: " + (sample == null ? "none" : Arrays.toString(sample)));
      }
      CallMethodResult ack =
          c.call((NodeId) unacked[6].value(), NodeIds.AcknowledgeableConditionType_Acknowledge,
              new Variant(unacked[0].value()),
              new Variant(LocalizedText.english("acknowledged by the interop test")));
      require(ack.getStatusCode().isGood(), "Acknowledge returned " + statusName(ack.getStatusCode()));
    } finally {
      try {
        c.call(folder, new NodeId(alarmsNs, "Alarms.End"));
      } catch (Exception e) {
        // The check already reported its outcome.
      }
      deleteQuietly(subscription);
    }
  }

  private OpcUaSubscription createEventSubscription(NodeId notifier, List<Variant[]> events, boolean condition)
      throws Exception {
    var subscription = new OpcUaSubscription(client, 100.0);
    subscription.setMaxKeepAliveCount(uint(10));
    subscription.setLifetimeCount(uint(100));
    subscription.create();
    try {
      var selects = new ArrayList<SimpleAttributeOperand>();
      for (String field : List.of("EventId", "EventType", "SourceNode", "Time", "Message", "Severity")) {
        selects.add(select(NodeIds.BaseEventType, field));
      }
      if (condition) {
        selects.add(new SimpleAttributeOperand(
            NodeIds.ConditionType, new QualifiedName[0], AttributeId.NodeId.uid(), null));
        selects.add(select(NodeIds.AcknowledgeableConditionType, "AckedState", "Id"));
        selects.add(select(NodeIds.ConditionType, "Retain"));
      }
      ExtensionObject ofType =
          ExtensionObject.encode(client.getStaticEncodingContext(), new LiteralOperand(new Variant(
              condition ? NodeIds.AcknowledgeableConditionType : NodeIds.BaseEventType)));
      var filter =
          new EventFilter(
              selects.toArray(SimpleAttributeOperand[]::new),
              new ContentFilter(new ContentFilterElement[] {
                new ContentFilterElement(FilterOperator.OfType, new ExtensionObject[] {ofType})
              }));
      var item = OpcUaMonitoredItem.newEventItem(notifier, filter);
      item.setSamplingInterval(0.0);
      item.setQueueSize(uint(100));
      item.setEventValueListener((i, values) -> {
        synchronized (events) {
          events.add(values);
        }
      });
      subscription.addMonitoredItem(item);
      synchronizeQuietly(subscription);
      require(item.getCreateResult().map(StatusCode::isGood).orElse(false),
          "event monitored item " + item.getCreateResult().map(ClientChecks::statusName).orElse("not created"));
      return subscription;
    } catch (Exception | Error e) {
      deleteQuietly(subscription);
      throw e;
    }
  }

  private static SimpleAttributeOperand select(NodeId type, String... path) {
    return new SimpleAttributeOperand(
        type,
        Arrays.stream(path).map(p -> new QualifiedName(0, p)).toArray(QualifiedName[]::new),
        AttributeId.Value.uid(),
        null);
  }

  private static <T> T waitFor(List<T> items, java.util.function.Predicate<T> match, long timeoutMs)
      throws InterruptedException {
    long deadline = System.nanoTime() + TimeUnit.MILLISECONDS.toNanos(timeoutMs);
    while (System.nanoTime() < deadline) {
      synchronized (items) {
        for (T item : items) {
          if (match.test(item)) {
            return item;
          }
        }
      }
      Thread.sleep(100);
    }
    return null;
  }

  private static int count(List<?> items) {
    synchronized (items) {
      return items.size();
    }
  }

  private CallMethodResult call(NodeId objectId, NodeId methodId, Variant... arguments) throws UaException {
    return client.call(List.of(new CallMethodRequest(objectId, methodId, arguments))).getResults()[0];
  }

  /** Item level failures are checked through the items' create results. */
  private static void synchronizeQuietly(OpcUaSubscription subscription) {
    try {
      subscription.synchronizeMonitoredItems();
    } catch (MonitoredItemSynchronizationException e) {
      // Reported by the create result of the item.
    }
  }

  private static void deleteQuietly(OpcUaSubscription subscription) {
    try {
      subscription.delete();
    } catch (Exception e) {
      // The check already reported its outcome.
    }
  }

  // ------------------------------------------------------------------ subscriptions

  /**
   * Absolute and percent deadbands on the static AnalogItem DataAccess_AnalogType_Double (EURange
   * 0..100): changes inside the deadband are not reported, larger ones are.
   */
  private static void deadbandFilter(ClientChecks c) throws Exception {
    NodeId analog = c.id("DataAccess_AnalogType_Double");
    for (DeadbandType type : List.of(DeadbandType.Absolute, DeadbandType.Percent)) {
      c.writeValue(analog, new Variant(0.0));
      var values = new ArrayList<DataValue>();
      OpcUaSubscription subscription =
          c.createDataSubscription(analog, values, 100.0, item ->
              // 10 units, or 10 % of the EURange 0..100.
              item.setFilter(new DataChangeFilter(DataChangeTrigger.StatusValue, uint(type.getValue()), 10.0)));
      try {
        Thread.sleep(500);
        for (double v : new double[] {5.0, 20.0, 25.0, 40.0}) {
          c.writeValue(analog, new Variant(v));
          Thread.sleep(400);
        }
        Thread.sleep(800);
        List<Object> seen;
        synchronized (values) {
          seen = values.stream().map(v -> v.value().value()).toList();
        }
        require(seen.contains(20.0) && seen.contains(40.0), type + ": 20 and 40 not reported (" + seen + ")");
        require(!seen.contains(5.0) && !seen.contains(25.0),
            type + ": changes inside the deadband reported (" + seen + ")");
      } finally {
        deleteQuietly(subscription);
      }
    }
  }

  /**
   * A queue of 2 with DiscardOldest and a slow publishing interval: five quick writes deliver the
   * last two values, with the Overflow bit set.
   */
  private static void queueOverflow(ClientChecks c) throws Exception {
    NodeId node = c.id("Scalar_Static_Int32");
    c.writeValue(node, new Variant(0));
    var values = new ArrayList<DataValue>();
    OpcUaSubscription subscription =
        c.createDataSubscription(node, values, 2000.0, item -> {
          item.setSamplingInterval(0.0);
          item.setQueueSize(uint(2));
          item.setDiscardOldest(true);
        });
    try {
      // Let the initial value go out first.
      Thread.sleep(2500);
      synchronized (values) {
        values.clear();
      }
      for (int ii = 1; ii <= 5; ii++) {
        c.writeValue(node, new Variant(ii));
      }
      Thread.sleep(3000);
      List<DataValue> seen;
      synchronized (values) {
        seen = new ArrayList<>(values);
      }
      String list = String.join(", ", seen.stream()
          .map(v -> v.value().value() + ":" + String.format("0x%08X", v.statusCode().value())).toList());
      require(seen.size() == 2, "expected the last 2 of 5 values, got " + seen.size() + " (" + list + ")");
      require(Integer.valueOf(4).equals(seen.get(0).value().value())
          && Integer.valueOf(5).equals(seen.get(1).value().value()), "expected 4 and 5, got " + list);
      require(seen.stream().anyMatch(v -> (v.statusCode().value() & 0x0480L) == 0x0480L),
          "no value carries the Overflow bit (" + list + ")");
    } finally {
      deleteQuietly(subscription);
    }
  }

  /** A sampling item linked to a reporting item by SetTriggering reports only with its trigger. */
  private static void triggering(ClientChecks c) throws Exception {
    var subscription = new OpcUaSubscription(c.client, 100.0);
    subscription.create();
    try {
      var linkedReports = new AtomicInteger();
      var trigger = OpcUaMonitoredItem.newDataItem(c.id("Scalar_Static_Int32"), MonitoringMode.Reporting);
      trigger.setSamplingInterval(50.0);
      var linked = OpcUaMonitoredItem.newDataItem(c.id("Scalar_Static_String"), MonitoringMode.Sampling);
      linked.setSamplingInterval(50.0);
      linked.setDataValueListener((i, v) -> linkedReports.incrementAndGet());
      subscription.addMonitoredItems(List.of(trigger, linked));
      synchronizeQuietly(subscription);
      require(trigger.getCreateResult().map(StatusCode::isGood).orElse(false)
          && linked.getCreateResult().map(StatusCode::isGood).orElse(false), "creating the items failed");
      SetTriggeringResponse set =
          c.client.setTriggering(
              subscription.getSubscriptionId().orElseThrow(),
              trigger.getMonitoredItemId().orElseThrow(),
              List.of(linked.getMonitoredItemId().orElseThrow()),
              List.of());
      require(set.getAddResults() != null && set.getAddResults().length == 1 && set.getAddResults()[0].isGood(),
          "SetTriggering returned " + (set.getAddResults() == null || set.getAddResults().length == 0
              ? "no add results" : statusName(set.getAddResults()[0])));

      Thread.sleep(500);
      int before = linkedReports.get();
      c.writeValue(c.id("Scalar_Static_String"), new Variant("linked " + UUID.randomUUID()));
      Thread.sleep(500);
      require(linkedReports.get() == before, "the sampling item reported without its trigger");
      c.writeValue(c.id("Scalar_Static_Int32"), new Variant((int) System.nanoTime()));
      Thread.sleep(1000);
      require(linkedReports.get() > before, "the triggered item did not report");
    } finally {
      deleteQuietly(subscription);
    }
  }

  /**
   * Republish of a sequence number the subscription never sent returns BadMessageNotAvailable, and
   * of an unknown subscription BadSubscriptionIdInvalid.
   */
  private static void republish(ClientChecks c) throws Exception {
    NodeId node = c.id("Scalar_Static_Int32");
    var values = new ArrayList<DataValue>();
    OpcUaSubscription subscription = c.createDataSubscription(node, values, 100.0, item -> {});
    try {
      UInteger id = subscription.getSubscriptionId().orElseThrow();
      // The subscription is new: its last sequence number is far below 10000.
      String notSent = c.republishStatus(id, uint(10_000));
      require(notSent.equals("Bad_MessageNotAvailable"), "Republish of an unsent message returned " + notSent);
      String unknown = c.republishStatus(uint(id.longValue() + 100_000), uint(1));
      require(unknown.equals("Bad_SubscriptionIdInvalid"), "Republish of an unknown subscription returned " + unknown);
    } finally {
      deleteQuietly(subscription);
    }
  }

  private String republishStatus(UInteger subscriptionId, UInteger sequence) {
    try {
      RepublishResponse response = client.republish(subscriptionId, sequence);
      return statusName(response.getResponseHeader().getServiceResult());
    } catch (UaException e) {
      return statusName(e.getStatusCode());
    }
  }

  /**
   * Transfers a subscription to a second session of the same (anonymous) user, which then
   * receives its data changes. Session B publishes with the raw Publish service.
   */
  private static void transferSubscription(ClientChecks c) throws Exception {
    NodeId node = c.id("Scalar_Static_Int32");
    var values = new ArrayList<DataValue>();
    OpcUaSubscription subscription = c.createDataSubscription(node, values, 100.0, item -> {});
    UInteger id = subscription.getSubscriptionId().orElseThrow();
    OpcUaClient target = c.newClient(new AnonymousProvider());
    try {
      target.connect();
      TransferSubscriptionsResponse transfer = target.transferSubscriptions(List.of(id), true);
      StatusCode result = transfer.getResults()[0].getStatusCode();
      require(result.isGood(), "TransferSubscriptions returned " + statusName(result));
      // The initial values (sendInitialValues) first.
      var acks = new ArrayList<SubscriptionAcknowledgement>();
      publishDataChanges(target, id, acks, 1500);
      int written = (int) (System.nanoTime() & 0x7fffffff);
      c.writeValue(node, new Variant(written));
      long deadline = System.nanoTime() + TimeUnit.MILLISECONDS.toNanos(EVENT_WAIT_MS);
      boolean received = false;
      while (!received && System.nanoTime() < deadline) {
        received = publishDataChanges(target, id, acks, 1000).stream()
            .anyMatch(v -> Integer.valueOf(written).equals(v.value().value()));
      }
      require(received, "the transferred subscription reported nothing to the new session");
    } finally {
      try {
        target.deleteSubscriptions(List.of(id));
      } catch (Exception e) {
        // Not transferred: deleted with the original session's subscription.
      }
      try {
        target.disconnect();
      } catch (Exception e) {
        // Closing only.
      }
      c.client.removeSubscription(subscription);
      deleteQuietly(subscription);
    }
  }

  /** Publishes on a session without a publishing manager for up to timeoutMs. */
  private static List<DataValue> publishDataChanges(
      OpcUaClient client, UInteger subscriptionId, List<SubscriptionAcknowledgement> acks, long timeoutMs)
      throws UaException {
    var values = new ArrayList<DataValue>();
    long deadline = System.nanoTime() + TimeUnit.MILLISECONDS.toNanos(timeoutMs);
    while (System.nanoTime() < deadline) {
      PublishResponse response = client.publish(new ArrayList<>(acks));
      acks.clear();
      NotificationMessage message = response.getNotificationMessage();
      ExtensionObject[] data = message.getNotificationData();
      if (data == null || data.length == 0) {
        continue;
      }
      acks.add(new SubscriptionAcknowledgement(response.getSubscriptionId(), message.getSequenceNumber()));
      if (!subscriptionId.equals(response.getSubscriptionId())) {
        continue;
      }
      for (ExtensionObject xo : data) {
        if (xo.decode(client.getStaticEncodingContext()) instanceof DataChangeNotification change
            && change.getMonitoredItems() != null) {
          for (MonitoredItemNotification n : change.getMonitoredItems()) {
            values.add(n.getValue());
          }
        }
      }
      if (!values.isEmpty()) {
        break;
      }
    }
    return values;
  }

  private OpcUaSubscription createDataSubscription(
      NodeId node, List<DataValue> values, double publishingInterval,
      java.util.function.Consumer<OpcUaMonitoredItem> configure) throws Exception {
    var subscription = new OpcUaSubscription(client, publishingInterval);
    subscription.setMaxKeepAliveCount(uint(10));
    subscription.setLifetimeCount(uint(100));
    subscription.create();
    try {
      var item = OpcUaMonitoredItem.newDataItem(node);
      item.setSamplingInterval(50.0);
      item.setQueueSize(uint(10));
      configure.accept(item);
      item.setDataValueListener((i, v) -> {
        synchronized (values) {
          values.add(v);
        }
      });
      subscription.addMonitoredItem(item);
      synchronizeQuietly(subscription);
      require(item.getCreateResult().map(StatusCode::isGood).orElse(false),
          "monitored item " + item.getCreateResult().map(ClientChecks::statusName).orElse("not created"));
      return subscription;
    } catch (Exception | Error e) {
      deleteQuietly(subscription);
      throw e;
    }
  }

  private void writeValue(NodeId node, Variant value) throws UaException {
    StatusCode status = write(node, value);
    require(status.isGood(), "write of " + node + " returned " + statusName(status));
  }

  // ------------------------------------------------------------------ identity

  private OpcUaClient newClient(IdentityProvider identity) throws Exception {
    return Peer.createClient(options, pki, policy, mode, identity);
  }

  /** A second session with a wrong password is rejected. */
  private static void wrongPasswordRejected(ClientChecks c) throws Exception {
    OpcUaClient other = c.newClient(new UsernameProvider("user1", "wrong password"));
    try {
      other.connect();
    } catch (UaException e) {
      String status = statusName(e.getStatusCode());
      require(status.equals("Bad_UserAccessDenied") || status.equals("Bad_IdentityTokenRejected"),
          "the wrong password was rejected with " + status);
      return;
    } finally {
      try {
        other.disconnect();
      } catch (Exception e) {
        // Closing only.
      }
    }
    throw new IllegalStateException("a session with a wrong password was activated");
  }

  /** A second session with an X509 user identity token (self-signed) reads the server state. */
  private static void x509UserToken(ClientChecks c) throws Exception {
    KeyPair keyPair = SelfSignedCertificateGenerator.generateRsaKeyPair(2048);
    X509Certificate certificate =
        new SelfSignedCertificateBuilder(keyPair)
            .setCommonName("InteropUser")
            .setOrganization("OPC Foundation")
            .build();
    OpcUaClient other = c.newClient(new X509IdentityProvider(certificate, keyPair.getPrivate()));
    try {
      other.connect();
      DataValue state = other.readValue(0.0, TimestampsToReturn.Neither, NodeIds.Server_ServerStatus_State);
      require(state.statusCode().isGood(), "reading with the X509 user returned " + statusName(state.statusCode()));
    } finally {
      try {
        other.disconnect();
      } catch (Exception e) {
        // Closing only.
      }
    }
  }

  // ------------------------------------------------------------------ services

  /** Registers two nodes, reads through the registered ids, unregisters. */
  private static void registerNodes(ClientChecks c) throws Exception {
    NodeId[] registered =
        c.client.registerNodes(List.of(c.id("Scalar_Static_Int32"), c.id("Scalar_Static_String")))
            .getRegisteredNodeIds();
    require(registered != null && registered.length == 2,
        "RegisterNodes returned " + (registered == null ? 0 : registered.length) + " ids");
    DataValue[] values = c.read(Arrays.stream(registered).map(ClientChecks::value).toList());
    require(Arrays.stream(values).allMatch(v -> v.statusCode().isGood()),
        "reading registered nodes returned " + Arrays.toString(Arrays.stream(values).map(DataValue::statusCode).toArray()));
    c.client.unregisterNodes(List.of(registered));
  }

  /** Reads the raw history of a historizing variable of the last 3 hours. */
  private static void historyReadRaw(ClientChecks c) throws Exception {
    long now = System.currentTimeMillis();
    var details =
        new ReadRawModifiedDetails(
            false,
            new DateTime(java.time.Instant.ofEpochMilli(now - 3 * 3600_000L)),
            new DateTime(java.time.Instant.ofEpochMilli(now)),
            uint(10),
            false);
    NodeId node = c.id("Scalar_Static_Double");
    HistoryReadResult result =
        c.client.historyRead(details, TimestampsToReturn.Source, false,
            List.of(new HistoryReadValueId(node, null, QualifiedName.NULL_VALUE, ByteString.NULL_VALUE)))
            .getResults()[0];
    require(result.getStatusCode().isGood(), "HistoryRead returned " + statusName(result.getStatusCode()));
    Object decoded = result.getHistoryData() == null ? null
        : result.getHistoryData().decode(c.client.getStaticEncodingContext());
    require(decoded instanceof HistoryData data && data.getDataValues() != null && data.getDataValues().length > 0,
        "HistoryRead returned no values: " + decoded);
    int count = ((HistoryData) decoded).getDataValues().length;
    require(count <= 10, count + " values despite NumValuesPerNode 10");
    if (hasContinuationPoint(result.getContinuationPoint())) {
      c.client.historyRead(details, TimestampsToReturn.Source, true,
          List.of(new HistoryReadValueId(node, null, QualifiedName.NULL_VALUE, result.getContinuationPoint())));
    }
  }

  /** Adds an object below the Objects folder, finds it by browsing and deletes it again. */
  private static void nodeManagement(ClientChecks c) throws Exception {
    String name = "InteropAdded_" + UUID.randomUUID().toString().replace("-", "").substring(0, 8);
    var attributes =
        new ObjectAttributes(uint(0x40), LocalizedText.english(name), LocalizedText.NULL_VALUE,
            uint(0), uint(0), ubyte(0));
    AddNodesResult added =
        c.client.addNodes(List.of(new AddNodesItem(
            NodeIds.ObjectsFolder.expanded(),
            NodeIds.Organizes,
            c.id(name).expanded(),
            new QualifiedName(c.ns, name),
            NodeClass.Object,
            ExtensionObject.encode(c.client.getStaticEncodingContext(), attributes),
            NodeIds.BaseObjectType.expanded()))).getResults()[0];
    require(added.getStatusCode().isGood(), "AddNodes returned " + statusName(added.getStatusCode()));
    require(c.browse(NodeIds.ObjectsFolder, 0).stream().anyMatch(r -> r.getBrowseName().name().equals(name)),
        "the added node is not organized by the Objects folder");
    StatusCode deleted =
        c.client.deleteNodes(List.of(new DeleteNodesItem(added.getAddedNodeId(), true))).getResults()[0];
    require(deleted.isGood(), "DeleteNodes returned " + statusName(deleted));
  }

  /** Writes and reads parts of an array with an IndexRange. */
  private static void indexRange(ClientChecks c) throws Exception {
    NodeId node = c.id("Scalar_Static_Arrays_Int32");
    c.writeValue(node, new Variant(new Integer[] {0, 1, 2, 3, 4, 5, 6, 7, 8, 9}));
    StatusCode write =
        c.client.write(List.of(new WriteValue(node, AttributeId.Value.uid(), "2:3",
            DataValue.valueOnly(new Variant(new Integer[] {20, 30}))))).getResults()[0];
    require(write.isGood(), "writing the index range 2:3 returned " + statusName(write));
    DataValue read =
        c.client.read(0.0, TimestampsToReturn.Neither,
            List.of(new ReadValueId(node, AttributeId.Value.uid(), "1:4", QualifiedName.NULL_VALUE)))
            .getResults()[0];
    require(read.statusCode().isGood(), "reading the index range 1:4 returned " + statusName(read.statusCode()));
    Object part = read.value().value();
    require(part instanceof Integer[] ints && Arrays.equals(ints, new Integer[] {1, 20, 30, 4}),
        "index range 1:4 read " + (part instanceof Object[] a ? Arrays.toString(a) : part));
  }

  /** FindServers lists the server and GetEndpoints offers the endpoint of this session. */
  private static void findServers(ClientChecks c) throws Exception {
    String url = Peer.required(c.options, "url");
    EndpointDescription endpoint = c.client.getConfig().getEndpoint();
    String serverUri = endpoint.getServer().getApplicationUri();
    List<ApplicationDescription> servers = DiscoveryClient.findServers(url).get(30, TimeUnit.SECONDS);
    require(servers.stream().anyMatch(s -> serverUri.equals(s.getApplicationUri())),
        "FindServers does not list " + serverUri + ": "
            + servers.stream().map(ApplicationDescription::getApplicationUri).toList());
    List<EndpointDescription> endpoints = DiscoveryClient.getEndpoints(url).get(30, TimeUnit.SECONDS);
    require(endpoints.stream().anyMatch(e -> endpoint.getSecurityPolicyUri().equals(e.getSecurityPolicyUri())
        && e.getSecurityMode() == endpoint.getSecurityMode()), "GetEndpoints does not offer the session's endpoint");
  }

  /**
   * Drops the TCP connection of the session's secure channel; the Milo session state machine
   * opens a new secure channel and re-activates the existing session on it (ActivateSession on
   * the new channel, same SessionId), then reads go on.
   */
  private static void sessionReconnect(ClientChecks c) throws Exception {
    NodeId before = c.client.getSession().getSessionId();
    require(c.client.getTransport() instanceof OpcTcpClientTransport,
        "the transport is " + c.client.getTransport().getClass().getSimpleName());
    var transport = (OpcTcpClientTransport) c.client.getTransport();
    io.netty.channel.Channel oldChannel = transport.getChannelFsm().getChannel().get(10, TimeUnit.SECONDS);
    var activations = new AtomicInteger();
    SessionActivityListener listener = new SessionActivityListener() {
      @Override
      public void onSessionActive(org.eclipse.milo.opcua.sdk.client.UaSession session) {
        activations.incrementAndGet();
      }
    };
    c.client.addSessionActivityListener(listener);
    try {
      oldChannel.close().await(10, TimeUnit.SECONDS);
      long deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(30);
      String last = "no read";
      boolean good = false;
      while (!good && System.nanoTime() < deadline) {
        try {
          DataValue state = c.client.readValue(0.0, TimestampsToReturn.Neither, NodeIds.Server_ServerStatus_State);
          good = state.statusCode().isGood();
          last = statusName(state.statusCode());
        } catch (UaException e) {
          last = statusName(e.getStatusCode());
        }
        if (!good) {
          Thread.sleep(250);
        }
      }
      require(good, "reading after the reconnect returned " + last);
      io.netty.channel.Channel newChannel = transport.getChannelFsm().getChannel().get(10, TimeUnit.SECONDS);
      require(newChannel != oldChannel, "the secure channel was not replaced");
      NodeId after = c.client.getSession().getSessionId();
      require(before.equals(after), "the session id changed from " + before + " to " + after
          + " (a new session was created instead of re-activating the session)");
      System.out.println("INFO SessionReconnect: session " + after + " re-activated on a new channel ("
          + activations.get() + " session activity notifications)");
    } finally {
      c.client.removeSessionActivityListener(listener);
    }
  }
}
