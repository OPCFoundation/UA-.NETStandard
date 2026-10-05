// Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
// OPC Foundation MIT License 1.00 - http://opcfoundation.org/License/MIT/1.00/

//! async-opcua interop peer: the same command line and stdout contract as
//! tests/Opc.Ua.Interop.LegacyPeer, so the 2.0 interop tests can drive it.
//!
//! server --port <p> --pki <dir> [--kind interop] [--init-only true]
//! client --url <u> --pki <dir> [--policy <uri>] [--mode <m>] [--user u --password p]
//!        [--checks a,b] [--expect-connect-error A,B] [--token-lifetime ms]
//!        [--token-test-seconds s] [--timeout-seconds s]
//!
//! async-opcua keeps its own PKI in <pki>/async-opcua (own/cert.der,
//! private/private.pem); the certificate is mirrored to <pki>/own/certs/<name>.der,
//! where the 2.0 tests look for it.

use std::collections::{HashMap, HashSet, VecDeque};
use std::io::BufRead;
use std::path::{Path, PathBuf};
use std::str::FromStr;
use std::sync::atomic::{AtomicI32, Ordering};
use std::sync::Arc;
use std::time::{Duration, Instant};

use opcua::client::custom_types::DataTypeTreeBuilder;
use opcua::client::{ClientBuilder, DataChangeCallback, IdentityToken, Session};
use opcua::server::address_space::{MethodBuilder, Variable};
use opcua::server::diagnostics::NamespaceMetadata;
use opcua::server::node_manager::memory::{simple_node_manager, SimpleNodeManager};
use opcua::crypto::{SecurityPolicy, Thumbprint};
use opcua::nodes::{BaseEventType, Event};
use opcua::server::authenticator::{AuthManager, DefaultAuthenticator, Password, UserToken};
use opcua::server::{ServerBuilder, ServerEndpoint, ServerUserToken, ANONYMOUS_USER_TOKEN_ID};
use opcua::types::{Error, ObjectTypeId, UserTokenPolicy, UserTokenType};

mod features;
use opcua::types::custom::DynamicTypeLoader;
use opcua::types::{
    AttributeId, BrowseDescription, BrowseDirection, BrowsePath, BrowseResultMask, BuildInfo,
    ByteString, CallMethodRequest, DataTypeId, DataValue, DateTime, ExtensionObject, Guid,
    LocalizedText, MessageSecurityMode, MonitoredItemCreateRequest, NodeId, ObjectId, QualifiedName,
    Range, ReadValueId, ReferenceTypeId, RelativePath, RelativePathElement, ServerState,
    ServerStatusDataType, StatusCode, TimestampsToReturn, TypeLoader, UAString, Variant, VariableId,
    WriteValue,
};
use serde_json::json;

const INTEROP_NAMESPACE: &str = "urn:opcfoundation.org:interop:legacy";
const REFERENCE_NAMESPACE: &str = "http://opcfoundation.org/Quickstarts/ReferenceServer";
const USER_NAME: &str = "interop";
const PASSWORD: &str = "interop-password";
const POLICY_NONE: &str = "http://opcfoundation.org/UA/SecurityPolicy#None";
const VERSION: &str = "0.19.0";

type CheckResult = Result<(), String>;

fn main() {
    let args: Vec<String> = std::env::args().collect();
    if args.len() < 2 {
        eprintln!("error: missing command");
        std::process::exit(2);
    }
    let mut options = HashMap::new();
    let mut ii = 2;
    while ii < args.len() {
        if !args[ii].starts_with("--") || ii + 1 >= args.len() {
            eprintln!("error: expected --name value, got {}", args[ii]);
            std::process::exit(2);
        }
        options.insert(args[ii][2..].to_lowercase(), args[ii + 1].clone());
        ii += 2;
    }
    // Stack logging goes to stderr (RUST_LOG, default: errors only); stdout carries the protocol.
    env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("error"))
        .target(env_logger::Target::Stderr)
        .init();
    let runtime = tokio::runtime::Runtime::new().expect("tokio runtime");
    let code = match args[1].as_str() {
        "server" => runtime.block_on(run_server(options)),
        "client" => runtime.block_on(run_client(options)),
        other => {
            eprintln!("error: unknown command {other}");
            2
        }
    };
    runtime.shutdown_timeout(Duration::from_secs(2));
    std::process::exit(code);
}

fn flag(options: &HashMap<String, String>, name: &str) -> bool {
    options.get(name).map(|v| v.eq_ignore_ascii_case("true")).unwrap_or(false)
}

/// Mirrors async-opcua's own certificate into the .NET layout the tests read.
fn mirror_certificate(pki: &Path, name: &str) {
    let own = pki.join("async-opcua").join("own").join("cert.der");
    let target = pki.join("own").join("certs");
    let _ = std::fs::create_dir_all(&target);
    let _ = std::fs::copy(own, target.join(format!("{name}.der")));
}

// ------------------------------------------------------------------ server

/// The default authenticator (anonymous, user name) plus X509 user tokens:
/// any user certificate whose token signature verifies is accepted, like the
/// auto-accepted application certificates. The default X509 policy of
/// async-opcua only accepts configured thumbprints and advertises
/// Basic128Rsa15 as the token signing policy; this one signs with the
/// endpoint's policy.
struct InteropAuthenticator(DefaultAuthenticator);

#[async_trait::async_trait]
impl AuthManager for InteropAuthenticator {
    async fn authenticate_anonymous_token(&self, endpoint: &ServerEndpoint) -> Result<(), Error> {
        self.0.authenticate_anonymous_token(endpoint).await
    }

    async fn authenticate_username_identity_token(&self, endpoint: &ServerEndpoint, username: &str,
                                                  password: &Password) -> Result<UserToken, Error> {
        self.0.authenticate_username_identity_token(endpoint, username, password).await
    }

    async fn authenticate_x509_identity_token(&self, _endpoint: &ServerEndpoint,
                                              signing_thumbprint: &Thumbprint) -> Result<UserToken, Error> {
        Ok(UserToken(format!("x509:{}", signing_thumbprint.as_hex_string())))
    }

    fn user_token_policies(&self, endpoint: &ServerEndpoint) -> Vec<UserTokenPolicy> {
        let mut policies = self.0.user_token_policies(endpoint);
        let policy = endpoint.security_policy();
        if policy != SecurityPolicy::None {
            policies.push(UserTokenPolicy {
                policy_id: "x509".into(),
                token_type: UserTokenType::Certificate,
                issued_token_type: Default::default(),
                issuer_endpoint_url: Default::default(),
                security_policy_uri: policy.to_uri().into(),
            });
        }
        policies
    }
}

async fn run_server(options: HashMap<String, String>) -> i32 {
    let port: u16 = options.get("port").and_then(|p| p.parse().ok()).unwrap_or(0);
    let Some(pki) = options.get("pki").map(PathBuf::from) else {
        eprintln!("error: missing --pki");
        return 2;
    };
    if options.get("kind").map(|k| k != "interop").unwrap_or(false) {
        eprintln!("error: --kind is not supported by the async-opcua peer");
        return 2;
    }
    let name = "AsyncOpcuaInteropServer";
    let application_uri = format!("urn:localhost:opcfoundation.org:{name}");
    let software_version = format!("async-opcua {VERSION}");
    let url = format!("opc.tcp://localhost:{port}/");

    let token_ids = vec![ANONYMOUS_USER_TOKEN_ID, USER_NAME];
    let users = std::collections::BTreeMap::from([(USER_NAME.to_string(), ServerUserToken::user_pass(USER_NAME, PASSWORD))]);
    let mut builder = ServerBuilder::new()
        .with_authenticator(Arc::new(InteropAuthenticator(DefaultAuthenticator::new(users))))
        .application_name(name)
        .application_uri(&application_uri)
        .product_uri("http://opcfoundation.org/UA/Interop/AsyncOpcuaServer")
        .build_info(BuildInfo {
            product_uri: "http://opcfoundation.org/UA/Interop/AsyncOpcuaServer".into(),
            manufacturer_name: "async-opcua".into(),
            product_name: "async-opcua Interop Server".into(),
            software_version: software_version.as_str().into(),
            build_number: "0".into(),
            build_date: DateTime::now(),
        })
        .pki_dir(pki.join("async-opcua"))
        .create_sample_keypair(true)
        .trust_client_certs(true)
        .host("localhost")
        .discovery_urls(vec![url.clone()])
        .add_user_token(USER_NAME, ServerUserToken::user_pass(USER_NAME, PASSWORD))
        .with_node_manager(simple_node_manager(
            NamespaceMetadata { namespace_uri: INTEROP_NAMESPACE.to_owned(), ..Default::default() },
            "interop",
        ));
    use opcua::crypto::SecurityPolicy as P;
    for (id, policy, mode) in [
        ("none", P::None, MessageSecurityMode::None),
        ("b256s256_s", P::Basic256Sha256, MessageSecurityMode::Sign),
        ("b256s256_se", P::Basic256Sha256, MessageSecurityMode::SignAndEncrypt),
        ("aes128_s", P::Aes128Sha256RsaOaep, MessageSecurityMode::Sign),
        ("aes128_se", P::Aes128Sha256RsaOaep, MessageSecurityMode::SignAndEncrypt),
        ("aes256_s", P::Aes256Sha256RsaPss, MessageSecurityMode::Sign),
        ("aes256_se", P::Aes256Sha256RsaPss, MessageSecurityMode::SignAndEncrypt),
    ] {
        builder = builder.add_endpoint(id, ("/", policy, mode, &token_ids as &[&str]));
    }
    // InteropLimits of the 1.5.378 interop server.
    {
        let limits = builder.limits_mut();
        limits.max_array_length = 100_000;
        limits.max_byte_string_length = 1024 * 1024;
        limits.max_string_length = 1024 * 1024;
        limits.max_message_size = 4 * 1024 * 1024;
        limits.max_chunk_count = 4 * 1024 * 1024 / 8192 + 1;
        limits.operational.max_nodes_per_read = 100;
        limits.operational.max_nodes_per_write = 100;
        limits.operational.max_nodes_per_browse = 100;
        limits.operational.max_nodes_per_method_call = 100;
        // Like the 1.5.378 server: a sampling interval of 0 reports every
        // change (async-opcua otherwise revises 0 to its 100 ms timer rate and
        // keeps only the last value of each interval).
        limits.subscriptions.min_sampling_interval_ms = 0.0;
    }
    let (server, handle) = match builder.build() {
        Ok(s) => s,
        Err(e) => {
            eprintln!("FATAL building the server failed: {e}");
            return 3;
        }
    };
    mirror_certificate(&pki, name);
    if flag(&options, "init-only") {
        println!("PEER-PKI-READY {application_uri}");
        return 0;
    }

    let manager = handle.node_managers().get_of_type::<SimpleNodeManager>().unwrap();
    let ns = handle.get_namespace_index(INTEROP_NAMESPACE).unwrap();
    let folder = NodeId::new(ns, "Interop");
    let counter = NodeId::new(ns, "Counter");
    {
        let address_space = manager.address_space();
        let mut space = address_space.write();
        space.add_folder(&folder, QualifiedName::new(ns, "Interop"), "Interop", &NodeId::objects_folder_id());
        let range = ExtensionObject::from_message(Range { low: 0.0, high: 100.0 });
        let values: Vec<(&str, Variant, Option<DataTypeId>, bool)> = vec![
            ("Boolean", Variant::from(true), None, true),
            ("Int32", Variant::from(42i32), None, true),
            ("UInt64", Variant::from(u64::MAX), None, true),
            ("Double", Variant::from(3.25f64), None, true),
            ("String", Variant::from("legacy"), None, true),
            ("DateTime", Variant::from(DateTime::ymd_hms(2024, 1, 2, 3, 4, 5)), None, true),
            ("Guid", Variant::from(Guid::from_str("8d2b5c6e-6c0e-4a4c-9f2b-0b6a3a6b1e01").unwrap()), None, true),
            ("ByteString", Variant::from(ByteString::from(vec![1u8, 2, 3, 4, 5])), None, true),
            ("LocalizedText", Variant::from(LocalizedText::new("de", "Hallo")), None, true),
            ("QualifiedName", Variant::from(QualifiedName::new(ns, "Name")), None, true),
            ("NodeId", Variant::from(NodeId::new(ns, "Interop")), None, true),
            ("Int32Array", Variant::from(vec![1i32, 2, 3]), None, true),
            ("StringArray", Variant::from(vec![UAString::from("a"), UAString::from("b"), UAString::from("c")]), None, true),
            ("Range", Variant::from(range), Some(DataTypeId::Range), true),
            ("Counter", Variant::from(0i32), None, false),
        ];
        let mut variables = Vec::new();
        for (name, value, data_type, writable) in values {
            let is_array = matches!(value, Variant::Array(_));
            let mut v = Variable::new(&NodeId::new(ns, name), QualifiedName::new(ns, name), name, value);
            if let Some(dt) = data_type {
                v.set_data_type(dt);
            }
            if is_array {
                v.set_value_rank(1);
                v.set_array_dimensions(&[0]);
            }
            v.set_writable(writable);
            v.set_user_access_level(v.access_level());
            variables.push(v);
        }
        let _ = space.add_variables(variables, &folder);
        let add = NodeId::new(ns, "Add");
        MethodBuilder::new(&add, QualifiedName::new(ns, "Add"), "Add")
            .component_of(folder.clone())
            .input_args(
                &mut *space,
                &NodeId::new(ns, "Add_InputArguments"),
                &[("a", DataTypeId::Int32).into(), ("b", DataTypeId::Int32).into()],
            )
            .output_args(&mut *space, &NodeId::new(ns, "Add_OutputArguments"), &[("sum", DataTypeId::Int32).into()])
            .insert(&mut *space);
    }
    // RaiseEvent(): one BaseEventType event reported through the Server object.
    {
        let address_space = manager.address_space();
        let mut space = address_space.write();
        MethodBuilder::new(&NodeId::new(ns, "RaiseEvent"), QualifiedName::new(ns, "RaiseEvent"), "RaiseEvent")
            .component_of(folder.clone())
            .insert(&mut *space);
    }
    let events = handle.subscriptions().clone();
    let source = folder.clone();
    manager.inner().add_method_callback(NodeId::new(ns, "RaiseEvent"), move |_| {
        let event_id = ByteString::from(Guid::new().to_string().into_bytes());
        let event = BaseEventType::new_now(ObjectTypeId::BaseEventType, event_id, LocalizedText::new("", "interop event"))
            .set_source_node(source.clone())
            .set_source_name("Interop".into())
            .set_severity(500);
        let emitter: NodeId = ObjectId::Server.into();
        events.notify_events([(&event as &dyn Event, &emitter)].into_iter());
        Ok(vec![])
    });
    manager.inner().add_method_callback(NodeId::new(ns, "Add"), |args| {
        match (args.first(), args.get(1)) {
            (Some(Variant::Int32(a)), Some(Variant::Int32(b))) => Ok(vec![Variant::Int32(a + b)]),
            _ if args.len() < 2 => Err(StatusCode::BadArgumentsMissing),
            _ => Err(StatusCode::BadTypeMismatch),
        }
    });
    let subscriptions = handle.subscriptions().clone();
    let ticking = manager.clone();
    tokio::spawn(async move {
        let value = AtomicI32::new(0);
        let mut interval = tokio::time::interval(Duration::from_millis(100));
        loop {
            interval.tick().await;
            let v = value.fetch_add(1, Ordering::Relaxed) + 1;
            let _ = ticking.set_values(&subscriptions, [(&counter, None, DataValue::new_now(v))].into_iter());
        }
    });

    let listener = match tokio::net::TcpListener::bind(("0.0.0.0", port)).await {
        Ok(l) => l,
        Err(e) => {
            eprintln!("FATAL listening on port {port} failed: {e}");
            return 3;
        }
    };
    let stop = handle.clone();
    std::thread::spawn(move || {
        for line in std::io::stdin().lock().lines() {
            match line {
                Ok(l) if l.trim().eq_ignore_ascii_case("stop") => break,
                Ok(_) => continue,
                Err(_) => break,
            }
        }
        stop.cancel();
    });
    println!(
        "PEER-INFO {}",
        json!({"stack": "async-opcua", "version": VERSION, "applicationUri": application_uri, "softwareVersion": software_version})
    );
    println!("PEER-SERVER-READY {url}");
    println!("LEGACY-SERVER-READY {url}");
    let _ = server.run_with(listener).await;
    println!("PEER-SERVER-STOPPED");
    0
}

// ------------------------------------------------------------------ client

struct Ctx {
    session: Arc<Session>,
    ns: u16,
    options: HashMap<String, String>,
    client: tokio::sync::Mutex<opcua::client::Client>,
    url: String,
    policy: String,
    mode: MessageSecurityMode,
}

impl Ctx {
    fn id(&self, name: &str) -> NodeId {
        NodeId::new(self.ns, name)
    }

    /// Opens another session on the same endpoint with the given identity.
    async fn new_session(&self, identity: IdentityToken) -> Result<Arc<Session>, String> {
        let mut client = self.client.lock().await;
        connect(&mut client, &self.url, &self.policy, self.mode, identity).await
    }
}

fn require(condition: bool, message: impl FnOnce() -> String) -> CheckResult {
    if condition {
        Ok(())
    } else {
        Err(message())
    }
}

fn read_id(node_id: NodeId, attribute: AttributeId) -> ReadValueId {
    ReadValueId { node_id, attribute_id: attribute as u32, ..Default::default() }
}

async fn read(c: &Ctx, ids: Vec<ReadValueId>) -> Result<Vec<DataValue>, String> {
    let mut results = Vec::new();
    for chunk in ids.chunks(500) {
        results.extend(
            c.session
                .read(chunk, TimestampsToReturn::Both, 0.0)
                .await
                .map_err(|e| format!("read failed: {e}"))?,
        );
    }
    Ok(results)
}

async fn read_value(c: &Ctx, node_id: NodeId) -> Result<DataValue, String> {
    Ok(read(c, vec![read_id(node_id, AttributeId::Value)]).await?.remove(0))
}

async fn write(c: &Ctx, node_id: NodeId, value: Variant) -> Result<StatusCode, String> {
    let wv = WriteValue {
        node_id,
        attribute_id: AttributeId::Value as u32,
        value: DataValue::value_only(value),
        ..Default::default()
    };
    c.session
        .write(&[wv])
        .await
        .map(|r| r[0])
        .map_err(|e| format!("{}", e.status()))
}

fn browse_all(node_id: NodeId) -> BrowseDescription {
    BrowseDescription {
        node_id,
        browse_direction: BrowseDirection::Forward,
        reference_type_id: ReferenceTypeId::HierarchicalReferences.into(),
        include_subtypes: true,
        node_class_mask: 0,
        result_mask: BrowseResultMask::All as u32,
    }
}

/// Browses one node following continuation points; returns the target NodeIds and names.
async fn browse(c: &Ctx, node_id: &NodeId, max_refs: u32) -> Result<Vec<(NodeId, String, i32)>, String> {
    let mut result = c
        .session
        .browse(&[browse_all(node_id.clone())], max_refs, None)
        .await
        .map_err(|e| format!("browse failed: {e}"))?
        .remove(0);
    require(result.status_code.is_good(), || format!("browse of {node_id} returned {}", result.status_code))?;
    let mut refs = Vec::new();
    loop {
        for r in result.references.take().unwrap_or_default() {
            if r.node_id.server_index == 0 && r.node_id.namespace_uri.is_null() {
                refs.push((r.node_id.node_id.clone(), r.browse_name.name.as_ref().to_string(), r.node_class as i32));
            }
        }
        if result.continuation_point.is_null_or_empty() {
            break;
        }
        result = c
            .session
            .browse_next(false, &[result.continuation_point.clone()])
            .await
            .map_err(|e| format!("browse next failed: {e}"))?
            .remove(0);
        require(result.status_code.is_good(), || format!("browse next of {node_id} returned {}", result.status_code))?;
    }
    Ok(refs)
}

async fn check_namespace_array(c: &Ctx) -> CheckResult {
    require(c.ns > 0, || "reference server namespace is missing".into())
}

async fn check_server_status(c: &Ctx) -> CheckResult {
    let dv = read_value(c, VariableId::Server_ServerStatus.into()).await?;
    let Some(Variant::ExtensionObject(xo)) = dv.value else {
        return Err(format!("ServerStatus is not a structure: {:?}", dv.status));
    };
    let Some(status) = xo.inner_as::<ServerStatusDataType>() else {
        return Err("ServerStatus did not decode to ServerStatusDataType".into());
    };
    require(status.state == ServerState::Running, || format!("server state {:?}", status.state))?;
    require(!status.build_info.product_uri.is_empty(), || "BuildInfo is empty".into())
}

async fn check_browse_objects(c: &Ctx) -> CheckResult {
    let refs = browse(c, &ObjectId::ObjectsFolder.into(), 0).await?;
    require(refs.iter().any(|r| r.1 == "Server"), || "Server object not found".into())?;
    require(refs.iter().any(|r| r.1 == "CTT"), || "CTT folder not found".into())
}

async fn check_translate(c: &Ctx) -> CheckResult {
    let elements = ["Server", "ServerStatus", "CurrentTime"]
        .iter()
        .map(|n| RelativePathElement {
            reference_type_id: ReferenceTypeId::HierarchicalReferences.into(),
            is_inverse: false,
            include_subtypes: true,
            target_name: QualifiedName::new(0, *n),
        })
        .collect();
    let path = BrowsePath { starting_node: ObjectId::ObjectsFolder.into(), relative_path: RelativePath { elements: Some(elements) } };
    let result = c
        .session
        .translate_browse_paths_to_node_ids(&[path])
        .await
        .map_err(|e| format!("translate failed: {e}"))?
        .remove(0);
    require(result.status_code.is_good(), || format!("status {}", result.status_code))?;
    let target = &result.targets.unwrap_or_default()[0].target_id.node_id;
    let expected: NodeId = VariableId::Server_ServerStatus_CurrentTime.into();
    require(*target == expected, || format!("unexpected target {target}"))
}

async fn check_read_scalars(c: &Ctx) -> CheckResult {
    let names = ["Boolean", "Int32", "Double", "String", "DateTime", "Guid", "ByteString", "LocalizedText",
                 "QualifiedName", "NodeId", "Arrays_Int32", "Arrays_String"];
    let values = read(c, names.iter().map(|n| read_id(c.id(&format!("Scalar_Static_{n}")), AttributeId::Value)).collect()).await?;
    for (name, dv) in names.iter().zip(values.iter()) {
        let status = dv.status.unwrap_or(StatusCode::Good);
        require(status.is_good(), || format!("{name}: {status}"))?;
    }
    Ok(())
}

async fn check_read_attributes(c: &Ctx) -> CheckResult {
    let id = c.id("Scalar_Static_Int32");
    let values = read(c, vec![read_id(id.clone(), AttributeId::NodeClass), read_id(id.clone(), AttributeId::BrowseName),
                              read_id(id, AttributeId::DataType)]).await?;
    require(matches!(values[0].value, Some(Variant::Int32(2))), || format!("NodeClass {:?}", values[0].value))?;
    require(matches!(&values[1].value, Some(Variant::QualifiedName(q)) if q.name.as_ref() == "Scalar_Static_Int32"),
            || format!("BrowseName {:?}", values[1].value))?;
    let int32: NodeId = DataTypeId::Int32.into();
    require(matches!(&values[2].value, Some(Variant::NodeId(n)) if **n == int32), || format!("DataType {:?}", values[2].value))
}

async fn check_write_read_back(c: &Ctx) -> CheckResult {
    let text = "written by async-opcua äöü";
    let doubles = vec![1.5f64, -2.25, 1e300];
    for (name, value) in [("Scalar_Static_Int32", Variant::from(1234567i32)), ("Scalar_Static_String", Variant::from(text)),
                          ("Scalar_Static_Arrays_Double", Variant::from(doubles.clone()))] {
        let status = write(c, c.id(name), value.clone()).await?;
        require(status.is_good(), || format!("{name} write returned {status}"))?;
        let dv = read_value(c, c.id(name)).await?;
        require(dv.value.as_ref() == Some(&value), || format!("{name} read back {:?}", dv.value))?;
    }
    Ok(())
}

async fn check_call_methods(c: &Ctx) -> CheckResult {
    let results = c
        .session
        .call(vec![
            CallMethodRequest { object_id: c.id("Methods"), method_id: c.id("Methods_Hello"),
                                input_arguments: Some(vec![Variant::from("async-opcua")]) },
            CallMethodRequest { object_id: c.id("Methods"), method_id: c.id("Methods_Add"),
                                input_arguments: Some(vec![Variant::Float(1.5), Variant::UInt32(2)]) },
        ])
        .await
        .map_err(|e| format!("call failed: {e}"))?;
    require(results[0].status_code.is_good(), || format!("Hello {}", results[0].status_code))?;
    let hello = results[0].output_arguments.clone().unwrap_or_default();
    require(hello.first() == Some(&Variant::from("hello async-opcua")), || format!("Hello returned {hello:?}"))?;
    require(results[1].status_code.is_good(), || format!("Add {}", results[1].status_code))?;
    let add = results[1].output_arguments.clone().unwrap_or_default();
    require(add.first() == Some(&Variant::Float(3.5)), || format!("Add returned {add:?}"))
}

async fn check_subscription(c: &Ctx) -> CheckResult {
    let count = Arc::new(AtomicI32::new(0));
    let counted = count.clone();
    let sub = c
        .session
        .create_subscription(Duration::from_millis(100), 100, 10, 0, 0, true,
                             DataChangeCallback::new(move |_, _| { counted.fetch_add(1, Ordering::Relaxed); }))
        .await
        .map_err(|e| format!("CreateSubscription failed: {e}"))?;
    let mut item = MonitoredItemCreateRequest::from(NodeId::from(VariableId::Server_ServerStatus_CurrentTime));
    item.requested_parameters.sampling_interval = 100.0;
    item.requested_parameters.queue_size = 10;
    let created = c
        .session
        .create_monitored_items(sub, TimestampsToReturn::Both, vec![item])
        .await
        .map_err(|e| format!("CreateMonitoredItems failed: {e}"))?;
    let status = created[0].result.status_code;
    let started = Instant::now();
    while count.load(Ordering::Relaxed) < 3 && started.elapsed() < Duration::from_secs(15) {
        tokio::time::sleep(Duration::from_millis(100)).await;
    }
    let _ = c.session.delete_subscription(sub).await;
    require(status.is_good(), || format!("monitored item {status}"))?;
    let n = count.load(Ordering::Relaxed);
    require(n >= 3, || format!("only {n} data change notifications in 15 s"))
}

/// Loads the server's DataTypeDefinitions, decodes every custom-typed variable
/// below Objects and writes up to 50 structures back.
async fn check_complex_types(c: &Ctx) -> CheckResult {
    let tree = DataTypeTreeBuilder::new(|_| true).build(&c.session).await.map_err(|e| format!("loading the type tree failed: {e}"))?;
    let loader = Arc::new(DynamicTypeLoader::new(Arc::new(tree))) as Arc<dyn TypeLoader>;
    c.session.add_type_loader(loader);

    let mut variables = Vec::new();
    let server: NodeId = ObjectId::Server.into();
    let mut visited: HashSet<NodeId> = HashSet::new();
    let mut queue: VecDeque<NodeId> = VecDeque::from([ObjectId::ObjectsFolder.into()]);
    while let Some(node) = queue.pop_front() {
        if node == server || visited.len() > 30_000 {
            continue;
        }
        for (target, _, node_class) in browse(c, &node, 0).await? {
            if visited.insert(target.clone()) {
                if node_class == 2 && target.namespace != 0 {
                    variables.push(target.clone());
                }
                queue.push_back(target);
            }
        }
    }
    let data_types = read(c, variables.iter().map(|v| read_id(v.clone(), AttributeId::DataType)).collect()).await?;
    let custom: Vec<NodeId> = variables.into_iter().zip(data_types)
        .filter(|(_, dt)| matches!(&dt.value, Some(Variant::NodeId(n)) if n.namespace != 0))
        .map(|(v, _)| v)
        .collect();
    require(custom.len() >= 10, || format!("only {} variables with a custom data type found", custom.len()))?;
    let values = read(c, custom.iter().map(|v| read_id(v.clone(), AttributeId::Value)).collect()).await?;
    let access = read(c, custom.iter().map(|v| read_id(v.clone(), AttributeId::UserAccessLevel)).collect()).await?;
    let (mut structures, mut undecoded, mut writes) = (0, Vec::new(), Vec::new());
    for ((node, dv), level) in custom.iter().zip(values).zip(access) {
        let items: Vec<ExtensionObject> = match &dv.value {
            Some(Variant::ExtensionObject(xo)) => vec![xo.clone()],
            Some(Variant::Array(a)) => a.values.iter().filter_map(|v| match v {
                Variant::ExtensionObject(xo) => Some(xo.clone()),
                _ => None,
            }).collect(),
            _ => continue,
        };
        if items.is_empty() {
            continue;
        }
        for xo in &items {
            structures += 1;
            if xo.type_name().map(|n| n.contains("ByteStringBody")).unwrap_or(false) {
                undecoded.push(node.to_string());
            }
        }
        let writable = matches!(level.value, Some(Variant::Byte(b)) if b & 2 != 0);
        if writes.len() < 50 && writable {
            writes.push((node.clone(), dv.value.clone().unwrap()));
        }
    }
    require(structures > 0, || "no structure values were read".into())?;
    require(undecoded.is_empty(), || format!("{} of {structures} structures were not decoded: {}", undecoded.len(),
                                             undecoded.iter().take(10).cloned().collect::<Vec<_>>().join("; ")))?;
    require(!writes.is_empty(), || "no writable structure variable found".into())?;
    let mut problems = Vec::new();
    for (node, value) in &writes {
        match write(c, node.clone(), value.clone()).await {
            Ok(s) if s.is_good() => {
                let back = read_value(c, node.clone()).await?;
                if back.value.as_ref() != Some(value) {
                    problems.push(format!("{node} changed in a round trip"));
                }
            }
            Ok(s) => problems.push(format!("{node}: {s}")),
            Err(e) => problems.push(format!("{node}: {e} for the request")),
        }
    }
    require(problems.is_empty(), || format!("{} of {} structure writes failed: {}", problems.len(), writes.len(),
                                            problems.iter().take(10).cloned().collect::<Vec<_>>().join("; ")))
}

async fn write_and_compare(c: &Ctx, node: NodeId, value: Variant) -> CheckResult {
    let status = write(c, node.clone(), value.clone()).await?;
    require(status.is_good(), || format!("write of {node} returned {status}"))?;
    let dv = read_value(c, node.clone()).await?;
    require(dv.value.as_ref() == Some(&value), || format!("{node} read back a different value"))
}

async fn check_large_array(c: &Ctx) -> CheckResult {
    let values: Vec<i32> = (0..200_000i64).map(|i| ((i * 7919) ^ 0x5A5A) as i32).collect();
    write_and_compare(c, c.id("Scalar_Static_Arrays_Int32"), Variant::from(values)).await
}

async fn check_large_bytestring(c: &Ctx) -> CheckResult {
    let mut x: u32 = 4711;
    let bytes: Vec<u8> = (0..3 * 1024 * 1024).map(|_| { x = x.wrapping_mul(1103515245).wrapping_add(12345); (x >> 24) as u8 }).collect();
    write_and_compare(c, c.id("Scalar_Static_ByteString"), Variant::from(ByteString::from(bytes))).await
}

async fn check_oversized(c: &Ctx) -> CheckResult {
    let result = match write(c, c.id("Scalar_Static_ByteString"), Variant::from(ByteString::from(vec![0u8; 5 * 1024 * 1024]))).await {
        Ok(s) => {
            require(!s.is_good(), || "a 5 MB ByteString write was accepted".into())?;
            s.to_string()
        }
        Err(e) => e,
    };
    let state = read_value(c, VariableId::Server_ServerStatus_State.into()).await?;
    let status = state.status.unwrap_or(StatusCode::Good);
    require(status.is_good(), || format!("the session is unusable after the rejected write ({result}): {status}"))?;
    println!("INFO OversizedRequestRejected: {result}");
    Ok(())
}

async fn check_browse_continuation(c: &Ctx) -> CheckResult {
    let folder = c.id("Scalar_Static");
    let all: Vec<NodeId> = browse(c, &folder, 0).await?.into_iter().map(|r| r.0).collect();
    require(all.len() > 10, || format!("only {} references below {folder}", all.len()))?;
    let paged: Vec<NodeId> = browse(c, &folder, 3).await?.into_iter().map(|r| r.0).collect();
    require(paged == all, || format!("paged browse returned {} of {} references or a different order", paged.len(), all.len()))?;
    let first = c.session.browse(&[browse_all(folder)], 3, None).await.map_err(|e| format!("browse failed: {e}"))?.remove(0);
    require(!first.continuation_point.is_null_or_empty(), || "no continuation point returned".into())?;
    let released = c.session.browse_next(true, &[first.continuation_point.clone()]).await.map_err(|e| format!("{e}"))?;
    let released_status = released.first().map(|r| r.status_code).unwrap_or(StatusCode::Good);
    require(released_status.is_good(), || format!("releasing the continuation point returned {released_status}"))?;
    let reused = c.session.browse_next(false, &[first.continuation_point]).await.map_err(|e| format!("{e}"))?;
    let reused_status = reused[0].status_code;
    require(reused_status == StatusCode::BadContinuationPointInvalid, || format!("a released continuation point returned {reused_status}"))
}

async fn check_read_many_nodes(c: &Ctx) -> CheckResult {
    let limit = match read_value(c, VariableId::Server_ServerCapabilities_OperationLimits_MaxNodesPerRead.into()).await?.value {
        Some(Variant::UInt32(l)) => l as usize,
        _ => 0,
    };
    require(limit > 0, || "the server published no MaxNodesPerRead".into())?;
    let ids: Vec<ReadValueId> = (0..limit * 2 + 1).map(|_| read_id(VariableId::Server_ServerStatus_State.into(), AttributeId::Value)).collect();
    let mut good = 0;
    for chunk in ids.chunks(limit) {
        let results = c.session.read(chunk, TimestampsToReturn::Neither, 0.0).await.map_err(|e| format!("{e}"))?;
        good += results.iter().filter(|dv| dv.status.unwrap_or(StatusCode::Good).is_good()).count();
    }
    require(good == ids.len(), || format!("{good} of {} reads split by MaxNodesPerRead {limit} succeeded", ids.len()))?;
    let too_many = match c.session.read(&ids, TimestampsToReturn::Neither, 0.0).await {
        Ok(_) => StatusCode::Good,
        Err(e) => e.status(),
    };
    require(too_many == StatusCode::BadTooManyOperations,
            || format!("reading {} nodes in one request with MaxNodesPerRead {limit} returned {too_many}", ids.len()))
}

async fn check_token_renewal(c: &Ctx) -> CheckResult {
    let seconds: u64 = c.options.get("token-test-seconds").and_then(|s| s.parse().ok()).unwrap_or(65);
    let started = Instant::now();
    let mut reads = 0;
    while started.elapsed() < Duration::from_secs(seconds) {
        let dv = read_value(c, VariableId::Server_ServerStatus_CurrentTime.into()).await?;
        let status = dv.status.unwrap_or(StatusCode::Good);
        require(status.is_good(), || format!("read {reads} returned {status}"))?;
        reads += 1;
        tokio::time::sleep(Duration::from_millis(500)).await;
    }
    Ok(())
}

const CHECKS: [&str; 32] = [
    "NamespaceArray", "ReadServerStatusStructure", "BrowseObjectsFolder", "TranslateBrowsePath", "ReadScalars",
    "ReadAttributes", "WriteAndReadBack", "CallMethods", "Subscription", "ComplexTypes", "LargeArrayRoundTrip",
    "LargeByteStringRoundTrip", "OversizedRequestRejected", "BrowseContinuationPoints", "ReadManyNodes", "TokenRenewal",
    "EventSubscription", "ConditionRefresh", "AlarmAcknowledge", "DeadbandFilter", "QueueOverflow", "Triggering",
    "Republish", "TransferSubscription", "WrongPasswordRejected", "X509UserToken", "RegisterNodes", "HistoryReadRaw",
    "NodeManagement", "IndexRange", "FindServers", "SessionReconnect",
];

async fn run_check(c: &Ctx, name: &str) -> CheckResult {
    match name {
        "NamespaceArray" => check_namespace_array(c).await,
        "ReadServerStatusStructure" => check_server_status(c).await,
        "BrowseObjectsFolder" => check_browse_objects(c).await,
        "TranslateBrowsePath" => check_translate(c).await,
        "ReadScalars" => check_read_scalars(c).await,
        "ReadAttributes" => check_read_attributes(c).await,
        "WriteAndReadBack" => check_write_read_back(c).await,
        "CallMethods" => check_call_methods(c).await,
        "Subscription" => check_subscription(c).await,
        "ComplexTypes" => check_complex_types(c).await,
        "LargeArrayRoundTrip" => check_large_array(c).await,
        "LargeByteStringRoundTrip" => check_large_bytestring(c).await,
        "OversizedRequestRejected" => check_oversized(c).await,
        "BrowseContinuationPoints" => check_browse_continuation(c).await,
        "ReadManyNodes" => check_read_many_nodes(c).await,
        "TokenRenewal" => check_token_renewal(c).await,
        "EventSubscription" => features::check_event_subscription(c).await,
        "ConditionRefresh" => features::check_condition_refresh(c).await,
        "AlarmAcknowledge" => features::check_alarm_acknowledge(c).await,
        "DeadbandFilter" => features::check_deadband(c).await,
        "QueueOverflow" => features::check_queue_overflow(c).await,
        "Triggering" => features::check_triggering(c).await,
        "Republish" => features::check_republish(c).await,
        "TransferSubscription" => features::check_transfer_subscription(c).await,
        "WrongPasswordRejected" => features::check_wrong_password(c).await,
        "X509UserToken" => features::check_x509_user(c).await,
        "RegisterNodes" => features::check_register_nodes(c).await,
        "HistoryReadRaw" => features::check_history_read(c).await,
        "NodeManagement" => features::check_node_management(c).await,
        "IndexRange" => features::check_index_range(c).await,
        "FindServers" => features::check_find_servers(c).await,
        "SessionReconnect" => features::check_session_reconnect(c).await,
        _ => Err(format!("unknown check {name}")),
    }
}

struct Report {
    passed: i32,
    failed: i32,
}

impl Report {
    fn emit(&mut self, check: &str, result: &CheckResult, started: Instant) {
        if result.is_ok() { self.passed += 1 } else { self.failed += 1 }
        println!(
            "RESULT {}",
            json!({"check": check, "outcome": if result.is_ok() { "Passed" } else { "Failed" },
                   "message": result.as_ref().err().cloned().unwrap_or_default(),
                   "milliseconds": started.elapsed().as_millis() as u64})
        );
    }
}

/// Connects a session; with no retries the event loop ends with the reason when the connect fails.
async fn connect(client: &mut opcua::client::Client, url: &str, policy: &str, mode: MessageSecurityMode,
                 identity: IdentityToken) -> Result<Arc<Session>, String> {
    match client.connect_to_matching_endpoint((url, policy, mode), identity).await {
        Ok((session, event_loop)) => {
            let mut ended = event_loop.spawn();
            tokio::select! {
                connected = tokio::time::timeout(Duration::from_secs(30), session.wait_for_connection()) => {
                    if connected.unwrap_or(false) { Ok(session) } else { Err("the session did not connect within 30 s".to_string()) }
                }
                status = &mut ended => Err(match status { Ok(s) => format!("{s}"), Err(e) => format!("{e}") }),
            }
        }
        Err(e) => Err(format!("{}", e.status())),
    }
}

async fn run_client(options: HashMap<String, String>) -> i32 {
    let (Some(url), Some(pki)) = (options.get("url").cloned(), options.get("pki").map(PathBuf::from)) else {
        eprintln!("error: client needs --url and --pki");
        return 2;
    };
    let name = "AsyncOpcuaInteropClient";
    let application_uri = format!("urn:localhost:opcfoundation.org:{name}");
    let policy = options.get("policy").cloned().unwrap_or_else(|| POLICY_NONE.to_string());
    let mode = match options.get("mode").map(String::as_str).unwrap_or("None") {
        "Sign" => MessageSecurityMode::Sign,
        "SignAndEncrypt" => MessageSecurityMode::SignAndEncrypt,
        _ => MessageSecurityMode::None,
    };
    let selected: Vec<String> = options.get("checks").cloned()
        .unwrap_or_else(|| CHECKS[..9].join(","))
        .split(',').filter(|s| !s.is_empty()).map(str::to_string).collect();
    if let Some(unknown) = selected.iter().find(|s| !CHECKS.contains(&s.as_str()) && *s != "Connect" && *s != "CloseSession") {
        eprintln!("error: unknown checks: {unknown}");
        return 2;
    }
    let expected: Vec<String> = options.get("expect-connect-error").map(|e| e.split(',').map(str::to_string).collect()).unwrap_or_default();
    let mut builder = ClientBuilder::new()
        .application_name(name)
        .application_uri(&application_uri)
        .product_uri(&application_uri)
        .pki_dir(pki.join("async-opcua"))
        .create_sample_keypair(true)
        .trust_server_certs(true)
        // Auto-accept (the default of the peers) also skips the host name and
        // application URI checks; trust and key length are always checked.
        .verify_server_certs(options.get("autoaccept").map(|v| !v.eq_ignore_ascii_case("true")).unwrap_or(false))
        .session_retry_limit(0)
        .request_timeout(Duration::from_secs(60))
        .session_timeout(120_000)
        // Mirrors the 2.0 reference server fixture: the large-message checks
        // are bounded by the server, not by this client.
        .max_message_size(16 * 1024 * 1024)
        .max_chunk_count(0)
        .max_array_length(1024 * 1024)
        .max_byte_string_length(16 * 1024 * 1024)
        .max_string_length(4 * 1024 * 1024);
    if let Some(lifetime) = options.get("token-lifetime").and_then(|l| l.parse().ok()) {
        builder = builder.channel_lifetime(lifetime);
    }
    let mut client = match builder.client() {
        Ok(c) => c,
        Err(e) => {
            eprintln!("FATAL creating the client failed: {e:?}");
            return 3;
        }
    };
    mirror_certificate(&pki, name);
    if flag(&options, "init-only") {
        println!("PEER-PKI-READY {application_uri}");
        return 0;
    }
    let identity = match options.get("user") {
        Some(user) => IdentityToken::UserName(user.clone(), options.get("password").cloned().unwrap_or_default().into()),
        None => IdentityToken::Anonymous,
    };
    let deadline = Duration::from_secs(options.get("timeout-seconds").and_then(|s| s.parse().ok()).unwrap_or(300));
    let mut report = Report { passed: 0, failed: 0 };
    let started = Instant::now();
    let connected = connect(&mut client, &url, &policy, mode, identity.clone()).await;

    let session = match (connected, expected.is_empty()) {
        (Ok(session), true) => {
            report.emit("Connect", &Ok(()), started);
            Some(session)
        }
        (Ok(session), false) => {
            report.emit("Connect", &Err(format!("expected {expected:?}, but the connect succeeded")), started);
            let _ = session.disconnect().await;
            None
        }
        (Err(e), true) => {
            report.emit("Connect", &Err(e), started);
            None
        }
        (Err(e), false) => {
            let ok = expected.iter().any(|x| e.contains(x.as_str()));
            report.emit("Connect", &if ok { Ok(()) } else { Err(format!("expected {expected:?}, the connect failed with {e}")) }, started);
            None
        }
    };
    if let Some(session) = session {
        let ns = session.get_namespace_index(REFERENCE_NAMESPACE).await.unwrap_or(0);
        let mut ctx = Ctx {
            session: session.clone(),
            ns,
            options: options.clone(),
            client: tokio::sync::Mutex::new(client),
            url: url.clone(),
            policy: policy.clone(),
            mode,
        };
        for check in CHECKS.iter().filter(|c| selected.iter().any(|s| s == *c)) {
            if started.elapsed() > deadline {
                println!("INFO the checks did not complete in time");
                break;
            }
            let t = Instant::now();
            // A check that hangs (e.g. after the stack closed the connection) is cut off
            // so the remaining checks still run and report.
            let limit = if *check == "TokenRenewal" { Duration::from_secs(180) } else { Duration::from_secs(90) };
            let result = match tokio::time::timeout(limit.min(deadline.saturating_sub(started.elapsed())), run_check(&ctx, check)).await {
                Ok(r) => r,
                Err(_) => Err("the check did not complete before the deadline".into()),
            };
            let mut lost = matches!(&result, Err(e) if e.contains("BadConnectionClosed") || e.contains("did not complete before"));
            report.emit(check, &result, t);
            if !lost && result.is_err() {
                // A failed check may have left the session unusable.
                let probe = tokio::time::timeout(Duration::from_secs(10), read_value(&ctx, VariableId::Server_ServerStatus_State.into())).await;
                lost = !matches!(probe, Ok(Ok(ref dv)) if dv.status.unwrap_or(StatusCode::Good).is_good());
            }
            if lost {
                // The stack closed the connection (e.g. on a value it cannot decode);
                // the remaining checks get a new session.
                println!("INFO reconnecting after {check} lost the connection");
                if let Ok(s) = ctx.new_session(identity.clone()).await {
                    ctx.session = s;
                }
            }
        }
        let t = Instant::now();
        let closed = ctx.session.disconnect().await.map_err(|e| format!("CloseSession returned {}", e.status()));
        report.emit("CloseSession", &closed, t);
    }
    println!("SUMMARY passed={} failed={}", report.passed, report.failed);
    if report.failed == 0 { 0 } else { 1 }
}
