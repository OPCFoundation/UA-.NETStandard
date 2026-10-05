// Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
// OPC Foundation MIT License 1.00 - http://opcfoundation.org/License/MIT/1.00/

//! The event, subscription, identity and service checks of the peer client
//! (see tests/Opc.Ua.Interop.LegacyPeer/LegacyClientFeatures.cs) against a
//! server with the Quickstarts reference server address space.

use std::str::FromStr;
use std::sync::atomic::{AtomicU32, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use opcua::client::{IdentityToken, MonitoredItemMap, OnSubscriptionNotificationCore, Session, Subscription};
use opcua::client::HistoryReadAction;
use opcua::crypto::{SecurityPolicy, X509Data, X509};
use opcua::types::{
    AddNodesItem, AttributeId, CallMethodRequest, ContentFilter, ContentFilterElement,
    DataChangeFilter, DataChangeNotification, DataChangeTrigger, DataValue, DateTime, DeleteNodesItem,
    EventFieldList, EventFilter, EventNotificationList, ExtensionObject, FilterOperator, HistoryData,
    HistoryReadValueId, LiteralOperand, LocalizedText, MessageSecurityMode, MonitoredItemCreateRequest,
    MonitoringMode, MonitoringParameters, NodeClass, NodeId, NotificationMessage, NumericRange, ObjectAttributes,
    ObjectId, QualifiedName, ReadRawModifiedDetails, ReadValueId, ReferenceTypeId, SimpleAttributeOperand,
    StatusCode, TimestampsToReturn, Variant, VariableId, WriteValue,
};

use crate::{browse, read, read_id, read_value, require, write, CheckResult, Ctx};

const ALARMS_NAMESPACE: &str = "http://test.org/UA/Alarms/";
const EVENT_WAIT: Duration = Duration::from_secs(10);

// Well-known NodeIds (namespace 0).
const BASE_EVENT_TYPE: u32 = 2041;
const CONDITION_TYPE: u32 = 2782;
const ACKNOWLEDGEABLE_CONDITION_TYPE: u32 = 2881;
const REFRESH_START_EVENT_TYPE: u32 = 2787;
const REFRESH_END_EVENT_TYPE: u32 = 2788;
const CONDITION_REFRESH: u32 = 3875;
const ACKNOWLEDGE: u32 = 9111;
const BASE_OBJECT_TYPE: u32 = 58;

/// Client handles of the items these checks create; far above the handles the
/// session hands out itself.
static NEXT_HANDLE: AtomicU32 = AtomicU32::new(1_000_000);

fn ns0(id: u32) -> NodeId {
    NodeId::new(0, id)
}

/// What a subscription received: data values by client handle, event fields
/// and the last sequence number of a notification message.
#[derive(Default)]
pub(crate) struct Notes {
    values: Vec<(u32, DataValue)>,
    events: Vec<Vec<Variant>>,
    last_sequence: u32,
}

type Shared = Arc<Mutex<Notes>>;

/// A subscription callback that records the raw notification messages, so it
/// also works for subscriptions this session did not create (transfer).
struct Recorder(Shared);

impl OnSubscriptionNotificationCore for Recorder {
    fn on_subscription_notification(&mut self, message: NotificationMessage, _items: MonitoredItemMap<'_>) {
        let mut notes = self.0.lock().unwrap();
        for data in message.notification_data.iter().flatten() {
            if let Some(changes) = data.inner_as::<DataChangeNotification>() {
                notes.last_sequence = notes.last_sequence.max(message.sequence_number);
                for item in changes.monitored_items.iter().flatten() {
                    notes.values.push((item.client_handle, item.value.clone()));
                }
            } else if let Some(events) = data.inner_as::<EventNotificationList>() {
                notes.last_sequence = notes.last_sequence.max(message.sequence_number);
                for event in events.events.iter().flatten() {
                    let EventFieldList { event_fields, .. } = event;
                    notes.events.push(event_fields.clone().unwrap_or_default());
                }
            }
        }
    }
}

async fn create_subscription(session: &Session, publishing_ms: u64, notes: &Shared) -> Result<u32, String> {
    session
        .create_subscription(Duration::from_millis(publishing_ms), 100, 10, 0, 0, true, Recorder(notes.clone()))
        .await
        .map_err(|e| format!("CreateSubscription failed: {e}"))
}

/// Creates the items and returns their server ids; any bad item result fails.
async fn create_items(session: &Session, subscription: u32, items: Vec<MonitoredItemCreateRequest>) -> Result<Vec<u32>, String> {
    let created = session
        .create_monitored_items(subscription, TimestampsToReturn::Both, items)
        .await
        .map_err(|e| format!("CreateMonitoredItems failed: {e}"))?;
    let mut ids = Vec::new();
    for item in created {
        require(item.result.status_code.is_good(), || format!("monitored item {}", item.result.status_code))?;
        ids.push(item.result.monitored_item_id);
    }
    Ok(ids)
}

fn data_item(node: NodeId, sampling: f64, queue: u32, mode: MonitoringMode) -> (MonitoredItemCreateRequest, u32) {
    let handle = NEXT_HANDLE.fetch_add(1, Ordering::Relaxed);
    let item = MonitoredItemCreateRequest::new(
        ReadValueId { node_id: node, attribute_id: AttributeId::Value as u32, ..Default::default() },
        mode,
        MonitoringParameters {
            client_handle: handle,
            sampling_interval: sampling,
            filter: ExtensionObject::null(),
            queue_size: queue,
            discard_oldest: true,
        },
    );
    (item, handle)
}

async fn delete_subscription(session: &Session, id: u32) {
    let _ = session.delete_subscription(id).await;
}

async fn write_ok(c: &Ctx, node: NodeId, value: Variant) -> CheckResult {
    let status = write(c, node.clone(), value).await?;
    require(status.is_good(), || format!("write of {node} returned {status}"))
}

async fn call(session: &Session, object: NodeId, method: NodeId, args: Vec<Variant>) -> Result<StatusCode, String> {
    let results = session
        .call(vec![CallMethodRequest { object_id: object, method_id: method, input_arguments: Some(args) }])
        .await
        .map_err(|e| format!("Call failed: {e}"))?;
    Ok(results.first().map(|r| r.status_code).unwrap_or(StatusCode::BadUnexpectedError))
}

fn values_of(notes: &Shared, handle: u32) -> Vec<DataValue> {
    notes.lock().unwrap().values.iter().filter(|(h, _)| *h == handle).map(|(_, v)| v.clone()).collect()
}

// ------------------------------------------------------------------ events

fn select(type_id: u32, path: &[&str]) -> SimpleAttributeOperand {
    SimpleAttributeOperand {
        type_definition_id: ns0(type_id),
        browse_path: Some(path.iter().map(|p| QualifiedName::new(0, *p)).collect()),
        attribute_id: AttributeId::Value as u32,
        index_range: NumericRange::None,
    }
}

/// Event subscription with the select clauses EventId, EventType, SourceNode,
/// Time, Message, Severity (+ ConditionId, AckedState/Id, Retain for conditions).
async fn create_event_subscription(c: &Ctx, notifier: NodeId, notes: &Shared, condition: bool) -> Result<u32, String> {
    let subscription = create_subscription(&c.session, 100, notes).await?;
    let mut clauses: Vec<SimpleAttributeOperand> = ["EventId", "EventType", "SourceNode", "Time", "Message", "Severity"]
        .iter()
        .map(|n| select(BASE_EVENT_TYPE, &[n]))
        .collect();
    if condition {
        clauses.push(SimpleAttributeOperand {
            type_definition_id: ns0(CONDITION_TYPE),
            browse_path: None,
            attribute_id: AttributeId::NodeId as u32,
            index_range: NumericRange::None,
        });
        clauses.push(select(ACKNOWLEDGEABLE_CONDITION_TYPE, &["AckedState", "Id"]));
        clauses.push(select(CONDITION_TYPE, &["Retain"]));
    }
    let of_type = if condition { ACKNOWLEDGEABLE_CONDITION_TYPE } else { BASE_EVENT_TYPE };
    let filter = EventFilter {
        select_clauses: Some(clauses),
        where_clause: ContentFilter {
            elements: Some(vec![ContentFilterElement {
                filter_operator: FilterOperator::OfType,
                filter_operands: Some(vec![ExtensionObject::from_message(LiteralOperand {
                    value: Variant::from(ns0(of_type)),
                })]),
            }]),
        },
    };
    let item = MonitoredItemCreateRequest::new(
        ReadValueId { node_id: notifier, attribute_id: AttributeId::EventNotifier as u32, ..Default::default() },
        MonitoringMode::Reporting,
        MonitoringParameters {
            client_handle: NEXT_HANDLE.fetch_add(1, Ordering::Relaxed),
            sampling_interval: 0.0,
            filter: ExtensionObject::from_message(filter),
            queue_size: 100,
            discard_oldest: true,
        },
    );
    if let Err(e) = create_items(&c.session, subscription, vec![item]).await {
        delete_subscription(&c.session, subscription).await;
        return Err(format!("event {e}"));
    }
    Ok(subscription)
}

async fn wait_for_event(notes: &Shared, timeout: Duration, matches: impl Fn(&[Variant]) -> bool) -> Option<Vec<Variant>> {
    let deadline = Instant::now() + timeout;
    loop {
        if let Some(found) = notes.lock().unwrap().events.iter().find(|e| matches(e)) {
            return Some(found.clone());
        }
        if Instant::now() > deadline {
            return None;
        }
        tokio::time::sleep(Duration::from_millis(100)).await;
    }
}

fn message_text(fields: &[Variant]) -> String {
    match fields.get(4) {
        Some(Variant::LocalizedText(t)) => t.text.as_ref().to_string(),
        _ => String::new(),
    }
}

fn is_type(fields: &[Variant], type_id: u32) -> bool {
    matches!(fields.get(1), Some(Variant::NodeId(n)) if **n == ns0(type_id))
}

pub(crate) async fn check_event_subscription(c: &Ctx) -> CheckResult {
    let notes = Shared::default();
    let subscription = create_event_subscription(c, ObjectId::Server.into(), &notes, false).await?;
    let result = async {
        write_ok(c, c.id("NodeIds_Events_TriggerNode01"), Variant::Int32(1)).await?;
        let received = wait_for_event(&notes, EVENT_WAIT, |e| message_text(e).contains("Trigger event")).await;
        let count = notes.lock().unwrap().events.len();
        let Some(event) = received else {
            return Err(format!("no trigger event in {} s; received {count} events", EVENT_WAIT.as_secs()));
        };
        require(matches!(event.first(), Some(Variant::ByteString(b)) if !b.is_null_or_empty()), || "the event has no EventId".into())?;
        require(matches!(event.get(5), Some(Variant::UInt16(s)) if *s > 0), || format!("the event has no Severity: {:?}", event.get(5)))
    }
    .await;
    delete_subscription(&c.session, subscription).await;
    result
}

pub(crate) async fn check_condition_refresh(c: &Ctx) -> CheckResult {
    let notes = Shared::default();
    let subscription = create_event_subscription(c, ObjectId::Server.into(), &notes, false).await?;
    let result = async {
        let status = call(&c.session, ns0(CONDITION_TYPE), ns0(CONDITION_REFRESH), vec![Variant::UInt32(subscription)]).await?;
        require(status.is_good(), || format!("ConditionRefresh returned {status}"))?;
        let end = wait_for_event(&notes, EVENT_WAIT, |e| is_type(e, REFRESH_END_EVENT_TYPE)).await;
        require(end.is_some(), || "no RefreshEndEvent received".into())?;
        let start = notes.lock().unwrap().events.iter().any(|e| is_type(e, REFRESH_START_EVENT_TYPE));
        require(start, || "no RefreshStartEvent received".into())
    }
    .await;
    delete_subscription(&c.session, subscription).await;
    result
}

pub(crate) async fn check_alarm_acknowledge(c: &Ctx) -> CheckResult {
    let alarms_ns = c.session.get_namespace_index(ALARMS_NAMESPACE).await.map_err(|e| format!("the Alarms namespace is missing: {e}"))?;
    let folder = NodeId::new(alarms_ns, "Alarms");
    let notes = Shared::default();
    let subscription = create_event_subscription(c, folder.clone(), &notes, true).await?;
    let result = async {
        let started = call(&c.session, folder.clone(), NodeId::new(alarms_ns, "Alarms.Start"), vec![Variant::UInt32(30)]).await?;
        require(started.is_good(), || format!("Alarms.Start returned {started}"))?;
        // Fields: 0 EventId, 1 EventType, 2 SourceNode, 3 Time, 4 Message,
        // 5 Severity, 6 ConditionId, 7 AckedState/Id, 8 Retain.
        let unacked = wait_for_event(&notes, Duration::from_secs(20), |e| {
            matches!(e.get(6), Some(Variant::NodeId(_)))
                && matches!(e.get(7), Some(Variant::Boolean(false)))
                && matches!(e.get(8), Some(Variant::Boolean(true)))
        })
        .await;
        let Some(event) = unacked else {
            let n = notes.lock().unwrap();
            return Err(format!("no unacknowledged condition in 20 s; received {} events, last: {:?}", n.events.len(), n.events.last()));
        };
        let (Some(Variant::NodeId(condition)), Some(Variant::ByteString(event_id))) = (event.get(6), event.first()) else {
            return Err(format!("the condition event has no ConditionId or EventId: {event:?}"));
        };
        let ack = call(&c.session, (**condition).clone(), ns0(ACKNOWLEDGE), vec![
            Variant::ByteString(event_id.clone()),
            Variant::from(LocalizedText::new("", "acknowledged by the interop test")),
        ])
        .await?;
        require(ack.is_good(), || format!("Acknowledge returned {ack}"))
    }
    .await;
    let _ = call(&c.session, folder, NodeId::new(alarms_ns, "Alarms.End"), vec![]).await;
    delete_subscription(&c.session, subscription).await;
    result
}

// ------------------------------------------------------------------ subscriptions

fn as_f64(v: &DataValue) -> Option<f64> {
    match v.value {
        Some(Variant::Double(d)) => Some(d),
        Some(Variant::Float(f)) => Some(f as f64),
        Some(Variant::Int32(i)) => Some(i as f64),
        _ => None,
    }
}

pub(crate) async fn check_deadband(c: &Ctx) -> CheckResult {
    let analog = c.id("DataAccess_AnalogType_Double");
    for (name, kind) in [("Absolute", 1u32), ("Percent", 2u32)] {
        write_ok(c, analog.clone(), Variant::Double(0.0)).await?;
        let notes = Shared::default();
        let subscription = create_subscription(&c.session, 100, &notes).await?;
        let result = async {
            let (mut item, handle) = data_item(analog.clone(), 50.0, 10, MonitoringMode::Reporting);
            item.requested_parameters.filter = ExtensionObject::from_message(DataChangeFilter {
                trigger: DataChangeTrigger::StatusValue,
                deadband_type: kind,
                deadband_value: 10.0,
            });
            create_items(&c.session, subscription, vec![item]).await.map_err(|e| format!("{name}: {e}"))?;
            tokio::time::sleep(Duration::from_millis(500)).await;
            for v in [5.0, 20.0, 25.0, 40.0] {
                write_ok(c, analog.clone(), Variant::Double(v)).await?;
                tokio::time::sleep(Duration::from_millis(400)).await;
            }
            tokio::time::sleep(Duration::from_millis(800)).await;
            let seen: Vec<f64> = values_of(&notes, handle).iter().filter_map(as_f64).collect();
            let list = format!("{seen:?}");
            require(seen.contains(&20.0) && seen.contains(&40.0), || format!("{name}: 20 and 40 not reported ({list})"))?;
            require(!seen.contains(&5.0) && !seen.contains(&25.0), || format!("{name}: changes inside the deadband reported ({list})"))
        }
        .await;
        delete_subscription(&c.session, subscription).await;
        result?;
    }
    Ok(())
}

pub(crate) async fn check_queue_overflow(c: &Ctx) -> CheckResult {
    let node = c.id("Scalar_Static_Int32");
    write_ok(c, node.clone(), Variant::Int32(0)).await?;
    let notes = Shared::default();
    let subscription = create_subscription(&c.session, 2000, &notes).await?;
    let result = async {
        let (item, handle) = data_item(node.clone(), 0.0, 2, MonitoringMode::Reporting);
        create_items(&c.session, subscription, vec![item]).await?;
        // Let the initial value go out first.
        tokio::time::sleep(Duration::from_millis(2500)).await;
        notes.lock().unwrap().values.clear();
        for ii in 1..=5 {
            write_ok(c, node.clone(), Variant::Int32(ii)).await?;
        }
        tokio::time::sleep(Duration::from_millis(3000)).await;
        let seen = values_of(&notes, handle);
        let status = |v: &DataValue| v.status.unwrap_or(StatusCode::Good).bits();
        let list = seen.iter().map(|v| format!("{:?}:0x{:08X}", v.value, status(v))).collect::<Vec<_>>().join(", ");
        require(seen.len() == 2, || format!("expected the last 2 of 5 values, got {} ({list})", seen.len()))?;
        require(seen[0].value == Some(Variant::Int32(4)) && seen[1].value == Some(Variant::Int32(5)), || format!("expected 4 and 5, got {list}"))?;
        require(seen.iter().any(|v| status(v) & 0x0480 == 0x0480), || format!("no value carries the Overflow bit ({list})"))
    }
    .await;
    delete_subscription(&c.session, subscription).await;
    result
}

pub(crate) async fn check_triggering(c: &Ctx) -> CheckResult {
    let notes = Shared::default();
    let subscription = create_subscription(&c.session, 100, &notes).await?;
    let result = async {
        let (trigger, _) = data_item(c.id("Scalar_Static_Int32"), 50.0, 10, MonitoringMode::Reporting);
        let (linked, linked_handle) = data_item(c.id("Scalar_Static_String"), 50.0, 10, MonitoringMode::Sampling);
        let ids = create_items(&c.session, subscription, vec![trigger, linked]).await?;
        let (adds, _) = c
            .session
            .set_triggering(subscription, ids[0], &[ids[1]], &[])
            .await
            .map_err(|e| format!("SetTriggering failed: {e}"))?;
        let add = adds.unwrap_or_default().first().copied().unwrap_or(StatusCode::BadUnexpectedError);
        require(add.is_good(), || format!("SetTriggering returned {add}"))?;

        tokio::time::sleep(Duration::from_millis(500)).await;
        let before = values_of(&notes, linked_handle).len();
        let stamp = DateTime::now().ticks();
        write_ok(c, c.id("Scalar_Static_String"), Variant::from(format!("linked {stamp}"))).await?;
        tokio::time::sleep(Duration::from_millis(500)).await;
        require(values_of(&notes, linked_handle).len() == before, || "the sampling item reported without its trigger".into())?;
        write_ok(c, c.id("Scalar_Static_Int32"), Variant::Int32((stamp % 1_000_000) as i32)).await?;
        tokio::time::sleep(Duration::from_millis(1000)).await;
        require(values_of(&notes, linked_handle).len() > before, || "the triggered item did not report".into())
    }
    .await;
    delete_subscription(&c.session, subscription).await;
    result
}

async fn republish_status(session: &Session, subscription: u32, sequence: u32) -> StatusCode {
    match session.republish(subscription, sequence).await {
        Ok(_) => StatusCode::Good,
        Err(e) => e.status(),
    }
}

pub(crate) async fn check_republish(c: &Ctx) -> CheckResult {
    let notes = Shared::default();
    let subscription = create_subscription(&c.session, 100, &notes).await?;
    let result = async {
        let (item, _) = data_item(c.id("Scalar_Static_Int32"), 50.0, 10, MonitoringMode::Reporting);
        create_items(&c.session, subscription, vec![item]).await?;
        tokio::time::sleep(Duration::from_millis(500)).await;
        let last = notes.lock().unwrap().last_sequence;
        let not_sent = republish_status(&c.session, subscription, last + 1000).await;
        require(not_sent == StatusCode::BadMessageNotAvailable, || format!("Republish of an unsent message returned {not_sent}"))?;
        let unknown = republish_status(&c.session, subscription + 100_000, 1).await;
        require(unknown == StatusCode::BadSubscriptionIdInvalid, || format!("Republish of an unknown subscription returned {unknown}"))
    }
    .await;
    delete_subscription(&c.session, subscription).await;
    result
}

pub(crate) async fn check_transfer_subscription(c: &Ctx) -> CheckResult {
    let node = c.id("Scalar_Static_Int32");
    let notes = Shared::default();
    let subscription = create_subscription(&c.session, 100, &notes).await?;
    let target = match c.new_session(IdentityToken::Anonymous).await {
        Ok(s) => s,
        Err(e) => {
            delete_subscription(&c.session, subscription).await;
            return Err(format!("opening the second session failed: {e}"));
        }
    };
    let result = async {
        let (item, handle) = data_item(node.clone(), 50.0, 10, MonitoringMode::Reporting);
        create_items(&c.session, subscription, vec![item]).await?;
        let results = target
            .transfer_subscriptions(&[subscription], true)
            .await
            .map_err(|e| format!("TransferSubscriptions failed: {e}"))?;
        let status = results.first().map(|r| r.status_code).unwrap_or(StatusCode::BadUnexpectedError);
        require(status.is_good(), || format!("TransferSubscriptions returned {status}"))?;
        // The session of a transferred subscription must know it to publish
        // for it (async-opcua: "register the subscriptions in the subscription state").
        let received = Shared::default();
        target.subscription_state().lock().add_subscription(Subscription::new(
            subscription,
            Duration::from_millis(100),
            100,
            10,
            0,
            0,
            true,
            Box::new(Recorder(received.clone())),
        ));
        target.trigger_publish_now();
        // The source session no longer owns the subscription; drop it from its state.
        let _ = c.session.delete_subscriptions(&[subscription]).await;
        tokio::time::sleep(Duration::from_millis(300)).await;
        let before = values_of(&received, handle).len();
        let value = (DateTime::now().ticks() % 1_000_000) as i32;
        let wv = WriteValue {
            node_id: node.clone(),
            attribute_id: AttributeId::Value as u32,
            value: DataValue::value_only(Variant::Int32(value)),
            ..Default::default()
        };
        let written = target.write(&[wv]).await.map_err(|e| format!("write failed: {e}"))?;
        require(written[0].is_good(), || format!("write returned {}", written[0]))?;
        let deadline = Instant::now() + EVENT_WAIT;
        while values_of(&received, handle).len() == before && Instant::now() < deadline {
            tokio::time::sleep(Duration::from_millis(100)).await;
        }
        require(values_of(&received, handle).len() > before, || "the transferred subscription reported nothing".into())
    }
    .await;
    // Closing the target session deletes the transferred subscription.
    let _ = target.disconnect().await;
    if c.session.subscription_state().lock().subscription_exists(subscription) {
        delete_subscription(&c.session, subscription).await;
    }
    result
}

// ------------------------------------------------------------------ identity

pub(crate) async fn check_wrong_password(c: &Ctx) -> CheckResult {
    match c.new_session(IdentityToken::UserName("user1".into(), "wrong password".into())).await {
        Ok(session) => {
            let _ = session.disconnect().await;
            Err("a session with a wrong password was activated".into())
        }
        Err(e) => require(e.contains("BadUserAccessDenied") || e.contains("BadIdentityTokenRejected"),
                          || format!("the wrong password was rejected with {e}")),
    }
}

pub(crate) async fn check_x509_user(c: &Ctx) -> CheckResult {
    let data = X509Data {
        key_size: 2048,
        common_name: "InteropUser".into(),
        organization: "OPC Foundation".into(),
        organizational_unit: String::new(),
        country: String::new(),
        state: String::new(),
        alt_host_names: vec!["urn:localhost:opcfoundation.org:InteropUser".to_string()].into(),
        certificate_duration_days: 365,
    };
    let (cert, key) = X509::cert_and_pkey(&data).map_err(|e| format!("creating the user certificate failed: {e}"))?;
    let session = c.new_session(IdentityToken::new_x509(cert, key)).await?;
    let state = session
        .read(&[read_id(VariableId::Server_ServerStatus_State.into(), AttributeId::Value)], TimestampsToReturn::Neither, 0.0)
        .await
        .map_err(|e| format!("reading with the X509 user failed: {e}"));
    let _ = session.disconnect().await;
    let status = state?.remove(0).status.unwrap_or(StatusCode::Good);
    require(status.is_good(), || format!("reading with the X509 user returned {status}"))
}

// ------------------------------------------------------------------ services

pub(crate) async fn check_register_nodes(c: &Ctx) -> CheckResult {
    let registered = c
        .session
        .register_nodes(&[c.id("Scalar_Static_Int32"), c.id("Scalar_Static_String")])
        .await
        .map_err(|e| format!("RegisterNodes failed: {e}"))?;
    require(registered.len() == 2, || format!("RegisterNodes returned {} ids", registered.len()))?;
    let values = read(c, registered.iter().map(|n| read_id(n.clone(), AttributeId::Value)).collect()).await?;
    let statuses: Vec<StatusCode> = values.iter().map(|v| v.status.unwrap_or(StatusCode::Good)).collect();
    require(statuses.iter().all(|s| s.is_good()), || format!("reading registered nodes returned {statuses:?}"))?;
    c.session.unregister_nodes(&registered).await.map_err(|e| format!("UnregisterNodes failed: {e}"))
}

pub(crate) async fn check_history_read(c: &Ctx) -> CheckResult {
    const HOUR_TICKS: i64 = 3600 * 10_000_000;
    let now = DateTime::now();
    let details = ReadRawModifiedDetails {
        is_read_modified: false,
        start_time: DateTime::from(now.ticks() - 3 * HOUR_TICKS),
        end_time: now,
        num_values_per_node: 10,
        return_bounds: false,
    };
    let node = HistoryReadValueId { node_id: c.id("Scalar_Static_Double"), ..Default::default() };
    let result = c
        .session
        .history_read(HistoryReadAction::ReadRawModifiedDetails(details.clone()), TimestampsToReturn::Source, false, &[node.clone()])
        .await
        .map_err(|e| format!("HistoryRead failed: {e}"))?
        .remove(0);
    require(result.status_code.is_good(), || format!("HistoryRead returned {}", result.status_code))?;
    let count = result.history_data.inner_as::<HistoryData>().and_then(|d| d.data_values.as_ref().map(Vec::len)).unwrap_or(0);
    require(count > 0, || "HistoryRead returned no values".into())?;
    require(count <= 10, || format!("{count} values despite NumValuesPerNode 10"))?;
    if !result.continuation_point.is_null_or_empty() {
        let release = HistoryReadValueId { continuation_point: result.continuation_point.clone(), ..node };
        let _ = c
            .session
            .history_read(HistoryReadAction::ReadRawModifiedDetails(details), TimestampsToReturn::Source, true, &[release])
            .await;
    }
    Ok(())
}

pub(crate) async fn check_node_management(c: &Ctx) -> CheckResult {
    let name = format!("InteropAdded_{:08x}", (DateTime::now().ticks() as u64 & 0xFFFF_FFFF) as u32);
    let item = AddNodesItem {
        parent_node_id: NodeId::from(ObjectId::ObjectsFolder).into(),
        reference_type_id: ReferenceTypeId::Organizes.into(),
        requested_new_node_id: c.id(&name).into(),
        browse_name: QualifiedName::new(c.ns, name.as_str()),
        node_class: NodeClass::Object,
        node_attributes: ExtensionObject::from_message(ObjectAttributes {
            specified_attributes: 0x40,
            display_name: LocalizedText::new("", &name),
            ..Default::default()
        }),
        type_definition: ns0(BASE_OBJECT_TYPE).into(),
    };
    let added = c.session.add_nodes(&[item]).await.map_err(|e| format!("AddNodes failed: {e}"))?.remove(0);
    require(added.status_code.is_good(), || format!("AddNodes returned {}", added.status_code))?;
    let refs = browse(c, &ObjectId::ObjectsFolder.into(), 0).await?;
    let found = refs.iter().any(|r| r.1 == name);
    let deleted = c
        .session
        .delete_nodes(&[DeleteNodesItem { node_id: added.added_node_id, delete_target_references: true }])
        .await
        .map_err(|e| format!("DeleteNodes failed: {e}"))?;
    require(found, || "the added node is not organized by the Objects folder".into())?;
    require(deleted[0].is_good(), || format!("DeleteNodes returned {}", deleted[0]))
}

pub(crate) async fn check_index_range(c: &Ctx) -> CheckResult {
    let node = c.id("Scalar_Static_Arrays_Int32");
    write_ok(c, node.clone(), Variant::from((0..10).collect::<Vec<i32>>())).await?;
    let wv = WriteValue {
        node_id: node.clone(),
        attribute_id: AttributeId::Value as u32,
        index_range: NumericRange::from_str("2:3").map_err(|e| format!("{e:?}"))?,
        value: DataValue::value_only(Variant::from(vec![20i32, 30])),
    };
    let written = c.session.write(&[wv]).await.map_err(|e| format!("write failed: {}", e.status()))?;
    require(written[0].is_good(), || format!("writing the index range 2:3 returned {}", written[0]))?;
    let rv = ReadValueId {
        node_id: node,
        attribute_id: AttributeId::Value as u32,
        index_range: NumericRange::from_str("1:4").map_err(|e| format!("{e:?}"))?,
        ..Default::default()
    };
    let dv = read(c, vec![rv]).await?.remove(0);
    let status = dv.status.unwrap_or(StatusCode::Good);
    require(status.is_good(), || format!("reading the index range 1:4 returned {status}"))?;
    require(dv.value == Some(Variant::from(vec![1i32, 20, 30, 4])), || format!("index range 1:4 read {:?}", dv.value))
}

pub(crate) async fn check_find_servers(c: &Ctx) -> CheckResult {
    let server_uri = c.session.endpoint_info().endpoint.server.application_uri.as_ref().to_string();
    let client = c.client.lock().await;
    let servers = client.find_servers(c.url.as_str(), None, None).await.map_err(|e| format!("FindServers failed: {e}"))?;
    let uris: Vec<String> = servers.iter().map(|s| s.application_uri.as_ref().to_string()).collect();
    require(uris.contains(&server_uri), || format!("FindServers does not list {server_uri}: {}", uris.join(", ")))?;
    let endpoints = client.get_server_endpoints_from_url(c.url.as_str()).await.map_err(|e| format!("GetEndpoints failed: {e}"))?;
    let b256 = SecurityPolicy::Basic256Sha256.to_uri();
    require(
        endpoints.iter().any(|e| e.security_policy_uri.as_ref() == b256 && e.security_mode == MessageSecurityMode::SignAndEncrypt),
        || "GetEndpoints does not offer Basic256Sha256/SignAndEncrypt".into(),
    )
}

/// async-opcua has no explicit reconnect call; closing the secure channel makes
/// the session event loop open a new channel and re-activate the existing
/// session on it (SessionConnector::ensure_and_activate_session).
pub(crate) async fn check_session_reconnect(c: &Ctx) -> CheckResult {
    let before = c.session.server_session_id();
    c.session.channel().close_channel().await;
    // Let the event loop see the closed transport before waiting for the reconnect.
    tokio::time::sleep(Duration::from_millis(1000)).await;
    let connected = tokio::time::timeout(Duration::from_secs(30), c.session.wait_for_connection()).await.unwrap_or(false);
    require(connected, || "the session did not reconnect within 30 s".into())?;
    let after = c.session.server_session_id();
    require(after == before, || format!("the session id changed from {before} to {after}"))?;
    let state = read_value(c, VariableId::Server_ServerStatus_State.into()).await?;
    let status = state.status.unwrap_or(StatusCode::Good);
    require(status.is_good(), || format!("reading after the reconnect returned {status}"))
}
