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
import static org.eclipse.milo.opcua.stack.core.types.builtin.unsigned.Unsigned.ubyte;
import static org.eclipse.milo.opcua.stack.core.types.builtin.unsigned.Unsigned.ulong;
import static org.eclipse.milo.opcua.stack.core.types.builtin.unsigned.Unsigned.ushort;

import java.security.SecureRandom;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import org.eclipse.milo.opcua.sdk.server.nodes.AttributeObserver;
import org.eclipse.milo.opcua.stack.core.AttributeId;
import java.util.UUID;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import org.eclipse.milo.opcua.sdk.core.AccessLevel;
import org.eclipse.milo.opcua.sdk.core.Reference;
import org.eclipse.milo.opcua.sdk.core.ValueRanks;
import org.eclipse.milo.opcua.sdk.server.ManagedNamespaceWithLifecycle;
import org.eclipse.milo.opcua.sdk.server.OpcUaServer;
import org.eclipse.milo.opcua.sdk.server.items.DataItem;
import org.eclipse.milo.opcua.sdk.server.items.MonitoredItem;
import org.eclipse.milo.opcua.sdk.server.methods.AbstractMethodInvocationHandler;
import org.eclipse.milo.opcua.sdk.server.model.objects.BaseEventTypeNode;
import org.eclipse.milo.opcua.sdk.server.model.objects.ServerTypeNode;
import org.eclipse.milo.opcua.stack.core.UaException;
import org.eclipse.milo.opcua.sdk.server.nodes.UaFolderNode;
import org.eclipse.milo.opcua.sdk.server.nodes.UaMethodNode;
import org.eclipse.milo.opcua.sdk.server.nodes.UaVariableNode;
import org.eclipse.milo.opcua.sdk.server.util.SubscriptionModel;
import org.eclipse.milo.opcua.stack.core.NodeIds;
import org.eclipse.milo.opcua.stack.core.types.builtin.ByteString;
import org.eclipse.milo.opcua.stack.core.types.builtin.DataValue;
import org.eclipse.milo.opcua.stack.core.types.builtin.DateTime;
import org.eclipse.milo.opcua.stack.core.types.builtin.ExtensionObject;
import org.eclipse.milo.opcua.stack.core.types.builtin.LocalizedText;
import org.eclipse.milo.opcua.stack.core.types.builtin.NodeId;
import org.eclipse.milo.opcua.stack.core.types.builtin.QualifiedName;
import org.eclipse.milo.opcua.stack.core.types.builtin.Variant;
import org.eclipse.milo.opcua.stack.core.types.builtin.unsigned.UInteger;
import org.eclipse.milo.opcua.stack.core.types.structured.Argument;
import org.eclipse.milo.opcua.stack.core.types.structured.Range;

/**
 * The address space of the interop server, the same as the 1.5.378 peer's
 * InteropNodeManager: a folder "Interop" below Objects holding scalar, array and
 * structure variables, a Counter that changes every 100 ms and an Add method.
 */
final class InteropNamespace extends ManagedNamespaceWithLifecycle {

  private final SubscriptionModel subscriptionModel;
  private final ScheduledExecutorService timer = Executors.newSingleThreadScheduledExecutor();
  private UaVariableNode counter;

  InteropNamespace(OpcUaServer server) {
    super(server, Peer.INTEROP_NAMESPACE);
    subscriptionModel = new SubscriptionModel(server, this);
    getLifecycleManager().addLifecycle(subscriptionModel);
    getLifecycleManager().addStartupTask(this::createNodes);
    getLifecycleManager().addShutdownTask(timer::shutdownNow);
  }

  private void createNodes() {
    var folder =
        new UaFolderNode(
            getNodeContext(),
            newNodeId("Interop"),
            newQualifiedName("Interop"),
            LocalizedText.english("Interop"));
    getNodeManager().addNode(folder);
    folder.addReference(
        new Reference(
            folder.getNodeId(), NodeIds.Organizes, NodeIds.ObjectsFolder.expanded(), false));

    int ns = getNamespaceIndex().intValue();
    variable(folder, "Boolean", NodeIds.Boolean, true);
    variable(folder, "Int32", NodeIds.Int32, 42);
    variable(folder, "UInt64", NodeIds.UInt64, ulong(-1L));
    variable(folder, "Double", NodeIds.Double, 3.25);
    variable(folder, "String", NodeIds.String, "legacy");
    variable(
        folder,
        "DateTime",
        NodeIds.DateTime,
        new DateTime(Instant.parse("2024-01-02T03:04:05Z")));
    variable(folder, "Guid", NodeIds.Guid, UUID.fromString("8d2b5c6e-6c0e-4a4c-9f2b-0b6a3a6b1e01"));
    variable(folder, "ByteString", NodeIds.ByteString, ByteString.of(new byte[] {1, 2, 3, 4, 5}));
    variable(folder, "LocalizedText", NodeIds.LocalizedText, new LocalizedText("de", "Hallo"));
    variable(folder, "QualifiedName", NodeIds.QualifiedName, new QualifiedName(ns, "Name"));
    variable(folder, "NodeId", NodeIds.NodeId, new NodeId(ns, "Interop"));
    variable(folder, "Int32Array", NodeIds.Int32, new Integer[] {1, 2, 3});
    variable(folder, "StringArray", NodeIds.String, new String[] {"a", "b", "c"});
    variable(
        folder,
        "Range",
        NodeIds.Range,
        ExtensionObject.encode(getServer().getStaticEncodingContext(), new Range(0.0, 100.0)));
    counter = variable(folder, "Counter", NodeIds.Int32, 0);
    counter.setAccessLevel(AccessLevel.toValue(AccessLevel.READ_ONLY));
    counter.setUserAccessLevel(AccessLevel.toValue(AccessLevel.READ_ONLY));

    addMethod(folder);
    addRaiseEventMethod(folder);

    // Events are reported through the Server object.
    getServer()
        .getAddressSpaceManager()
        .getManagedNode(NodeIds.Server)
        .ifPresent(
            node -> {
              if (node instanceof ServerTypeNode server) {
                server.setEventNotifier(ubyte(1));
              }
            });

    var ticks = new int[] {0};
    timer.scheduleAtFixedRate(
        () -> counter.setValue(new DataValue(new Variant(++ticks[0]))),
        100,
        100,
        TimeUnit.MILLISECONDS);
  }

  private UaVariableNode variable(UaFolderNode folder, String name, NodeId dataType, Object value) {
    boolean array = value.getClass().isArray();
    var node =
        new UaVariableNode.UaVariableNodeBuilder(getNodeContext())
            .setNodeId(newNodeId(name))
            .setBrowseName(newQualifiedName(name))
            .setDisplayName(LocalizedText.english(name))
            .setDataType(dataType)
            .setTypeDefinition(NodeIds.BaseDataVariableType)
            .setValueRank(array ? ValueRanks.OneDimension : ValueRanks.Scalar)
            .setArrayDimensions(array ? new UInteger[] {uint(0)} : null)
            .setAccessLevel(AccessLevel.READ_WRITE)
            .setUserAccessLevel(AccessLevel.READ_WRITE)
            .build();
    node.setValue(new DataValue(new Variant(value)));
    // In memory, changed by writes: exception based (Part 3 5.6.2).
    node.setMinimumSamplingInterval(0.0);
    getNodeManager().addNode(node);
    folder.addOrganizes(node);
    return node;
  }

  private void addMethod(UaFolderNode folder) {
    var method =
        UaMethodNode.builder(getNodeContext())
            .setNodeId(newNodeId("Add"))
            .setBrowseName(newQualifiedName("Add"))
            .setDisplayName(LocalizedText.english("Add"))
            .build();
    var handler = new AddMethod(method);
    method.setInputArguments(handler.getInputArguments());
    method.setOutputArguments(handler.getOutputArguments());
    method.setInvocationHandler(handler);
    getNodeManager().addNode(method);
    method.addReference(
        new Reference(
            method.getNodeId(), NodeIds.HasComponent, folder.getNodeId().expanded(), false));
  }

  /**
   * RaiseEvent() reports one BaseEventType event through the Server object with the Interop
   * folder as its SourceNode.
   */
  private void addRaiseEventMethod(UaFolderNode folder) {
    var method =
        UaMethodNode.builder(getNodeContext())
            .setNodeId(newNodeId("RaiseEvent"))
            .setBrowseName(newQualifiedName("RaiseEvent"))
            .setDisplayName(LocalizedText.english("RaiseEvent"))
            .build();
    method.setInvocationHandler(
        new AbstractMethodInvocationHandler(method) {
          @Override
          public Argument[] getInputArguments() {
            return new Argument[0];
          }

          @Override
          public Argument[] getOutputArguments() {
            return new Argument[0];
          }

          @Override
          protected Variant[] invoke(InvocationContext context, Variant[] inputs)
              throws UaException {
            BaseEventTypeNode event =
                getServer()
                    .getEventFactory()
                    .createEvent(newNodeId(UUID.randomUUID()), NodeIds.BaseEventType);
            try {
              byte[] eventId = new byte[16];
              RANDOM.nextBytes(eventId);
              event.setBrowseName(new QualifiedName(0, "InteropEvent"));
              event.setDisplayName(LocalizedText.english("InteropEvent"));
              event.setEventId(ByteString.of(eventId));
              event.setEventType(NodeIds.BaseEventType);
              event.setSourceNode(folder.getNodeId());
              event.setSourceName(folder.getDisplayName().text());
              event.setTime(DateTime.now());
              event.setReceiveTime(DateTime.now());
              event.setMessage(LocalizedText.english("interop event"));
              event.setSeverity(ushort(500));
              getServer().getEventNotifier().fire(event);
            } finally {
              event.delete();
            }
            return new Variant[0];
          }
        });
    getNodeManager().addNode(method);
    method.addReference(
        new Reference(
            method.getNodeId(), NodeIds.HasComponent, folder.getNodeId().expanded(), false));
  }

  private static final SecureRandom RANDOM = new SecureRandom();

  /** Add(a Int32, b Int32) returns sum Int32. */
  private static final class AddMethod extends AbstractMethodInvocationHandler {
    AddMethod(UaMethodNode node) {
      super(node);
    }

    @Override
    public Argument[] getInputArguments() {
      return new Argument[] {argument("a"), argument("b")};
    }

    @Override
    public Argument[] getOutputArguments() {
      return new Argument[] {argument("sum")};
    }

    @Override
    protected Variant[] invoke(InvocationContext context, Variant[] inputs) {
      int sum = (Integer) inputs[0].value() + (Integer) inputs[1].value();
      return new Variant[] {new Variant(sum)};
    }

    private static Argument argument(String name) {
      return new Argument(name, NodeIds.Int32, ValueRanks.Scalar, null, new LocalizedText(name));
    }
  }

  // The variables hold their values in memory and change only when written
  // (MinimumSamplingInterval 0): a Value item with the sampling interval 0 is
  // reported on exception, every change goes to the item's queue. All other
  // items are sampled by the SDK's SubscriptionModel.
  private final Map<DataItem, AttributeObserver> exceptionItems = new ConcurrentHashMap<>();

  private boolean isExceptionBased(DataItem item) {
    return item.getSamplingInterval() == 0.0
        && AttributeId.Value.isEqual(item.getReadValueId().getAttributeId())
        && (item.getReadValueId().getIndexRange() == null
            || item.getReadValueId().getIndexRange().isEmpty())
        && getNodeManager().getNode(item.getReadValueId().getNodeId()).orElse(null)
            instanceof UaVariableNode;
  }

  private void addItems(List<DataItem> dataItems) {
    var sampled = new ArrayList<DataItem>();
    for (DataItem item : dataItems) {
      if (!isExceptionBased(item)) {
        sampled.add(item);
        continue;
      }
      var node = (UaVariableNode) getNodeManager().getNode(item.getReadValueId().getNodeId()).get();
      AttributeObserver observer =
          (n, attributeId, value) -> {
            if (attributeId == AttributeId.Value
                && value instanceof DataValue dv
                && item.isSamplingEnabled()) {
              item.setValue(DataValue.derivedValue(dv, item.getTimestampsToReturn()));
            }
          };
      exceptionItems.put(item, observer);
      node.addAttributeObserver(observer);
      if (item.isSamplingEnabled()) {
        item.setValue(DataValue.derivedValue(node.getValue(), item.getTimestampsToReturn()));
      }
    }
    if (!sampled.isEmpty()) {
      subscriptionModel.onDataItemsCreated(sampled);
    }
  }

  private void removeItems(List<DataItem> dataItems) {
    for (DataItem item : dataItems) {
      AttributeObserver observer = exceptionItems.remove(item);
      if (observer != null) {
        getNodeManager()
            .getNode(item.getReadValueId().getNodeId())
            .ifPresent(node -> node.removeAttributeObserver(observer));
      }
    }
    subscriptionModel.onDataItemsDeleted(dataItems);
  }

  @Override
  public void onDataItemsCreated(List<DataItem> dataItems) {
    addItems(dataItems);
  }

  @Override
  public void onDataItemsModified(List<DataItem> dataItems) {
    removeItems(dataItems);
    addItems(dataItems);
  }

  @Override
  public void onDataItemsDeleted(List<DataItem> dataItems) {
    removeItems(dataItems);
  }

  @Override
  public void onMonitoringModeChanged(List<MonitoredItem> monitoredItems) {
    subscriptionModel.onMonitoringModeChanged(monitoredItems);
  }
}
