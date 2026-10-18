// Copyright (c) OPC Foundation. Licensed under the MIT License.
//! Optional, bounded WebTransport draft-16 Sans-I/O backend.
//!
//! The companion transport owns QUIC, TLS, certificate validation and stream allocation.
//! This library neither establishes authentication nor converts unreliable traffic into streams.

use std::collections::{BTreeMap, VecDeque};

use shiguredo_http3::webtransport::{
    Capsule, ConnectRequest, FlowControlLimits, Session, SessionState, StreamHeader, StreamHeaderDecodeError,
};
use shiguredo_http3::{
    ClientConnection, ErrorCode, Event, H3InitData, Header, Role, ServerConnection, Settings, VarInt,
    WebTransportEvent,
};

pub mod config;
pub mod error;
pub mod ffi;
mod send;

use config::{MAX_VARINT, OriginPolicy, Policy};
use error::{Code, Failure, Scope, argument_error};
use send::SendBook;
pub use send::{MAX_OUTPUT_BYTES, MAX_PENDING_SENDS};

/// Actual transport evidence from one fresh, confirmed, authenticated QUIC connection.
/// The 80-byte layout is compatible with the adopted operation-revision-2 interface.
#[derive(Clone, Copy, Debug)]
#[repr(C)]
pub struct TransportFacts {
    pub abi_version: u32,
    pub role: u32,
    pub local_max_datagram: u64,
    pub peer_max_datagram: u64,
    pub reliable_reset_parameter: u64,
    pub reliable_reset_frame: u64,
    pub initial_max_bidi: u64,
    pub initial_max_data: u64,
    pub local_reset_parameter_empty: u32,
    pub peer_reset_parameter_empty: u32,
    pub handshake_confirmed: u32,
    pub mutual_tls_authenticated: u32,
    pub alpn_is_h3: u32,
    pub reserved: u32,
}

impl TransportFacts {
    pub fn validate(&self) -> Result<(), Failure> {
        if self.abi_version != 1 || self.role > 1 || self.reserved != 0 {
            return Err(Failure::config(
                Code::InvalidAbi,
                "invalid facts version, role or reserved field",
            ));
        }
        if self.local_max_datagram == 0
            || self.peer_max_datagram == 0
            || self.local_max_datagram > MAX_VARINT
            || self.peer_max_datagram > MAX_VARINT
            || self.reliable_reset_parameter != 0x1d
            || self.reliable_reset_frame != 0x24
            || self.local_reset_parameter_empty != 1
            || self.peer_reset_parameter_empty != 1
            || self.handshake_confirmed != 1
            || self.mutual_tls_authenticated != 1
            || self.alpn_is_h3 != 1
        {
            return Err(Failure::config(
                Code::InvalidTransportEvidence,
                "confirmed authenticated h3, datagrams and exact reset09 evidence required",
            ));
        }
        if self.initial_max_bidi == 0
            || self.initial_max_bidi > 128
            || self.initial_max_data == 0
            || self.initial_max_data > 16 * 1024 * 1024
        {
            return Err(Failure::config(
                Code::InvalidLimits,
                "initial WT flow-control limits outside reviewed bounds",
            ));
        }
        Ok(())
    }
}

/// Construct only draft-16 settings, with QPACK dynamic allocation disabled.
pub fn strict_settings(role: Role, bidi_limit: u64, data_limit: u64) -> Settings {
    let wt = shiguredo_http3::webtransport::Settings::new()
        .wt_enabled(VarInt::from_static(1))
        .wt_initial_max_streams_bidi(VarInt::new(bidi_limit).expect("validated limit"))
        .wt_initial_max_streams_uni(VarInt::ZERO)
        .wt_initial_max_data(VarInt::new(data_limit).expect("validated limit"));
    let settings = Settings::new()
        .qpack_max_table_capacity(VarInt::ZERO)
        .qpack_blocked_streams(VarInt::ZERO)
        .max_field_section_size(VarInt::from_static(16 * 1024));
    match role {
        Role::Client => settings.enable_webtransport_client(wt),
        Role::Server => settings.enable_webtransport_server(wt),
    }
}

/// Events 1..4 require transport actions; 10..29 are application observations.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Output {
    pub kind: u32,
    pub flags: u32,
    pub stream: u64,
    pub session: u64,
    pub arg: u64,
    pub aux: u64,
    pub data: Vec<u8>,
}

impl Output {
    fn new(kind: u32, stream: u64, session: u64, data: Vec<u8>) -> Self {
        Self {
            kind,
            flags: 0,
            stream,
            session,
            arg: 0,
            aux: 0,
            data,
        }
    }
}

enum HttpPeer {
    Client(ClientConnection),
    Server(ServerConnection),
}

macro_rules! dispatch {
    ($peer:expr, $method:ident $(, $arg:expr)* $(,)?) => {
        match $peer {
            HttpPeer::Client(c) => c.$method($($arg),*),
            HttpPeer::Server(s) => s.$method($($arg),*),
        }
    };
}

#[derive(Debug)]
struct DataStream {
    session: u64,
    reliable_size: u64,
    sent: u64,
    send_closed: bool,
    reset_pending: bool,
    reset_issued: bool,
    reset_final: Option<(u64, u64)>,
    stop_queued: bool,
}

/// Prefixes and closed streams stay as bounded lifetime tombstones; IDs are never recycled.
#[derive(Debug, Default)]
struct Ingress {
    prefix: [u8; 16],
    prefix_len: usize,
    ready: bool,
    terminal: bool,
    control_bytes: u64,
}

/// Compatible 64-byte accounting snapshot; pending/closed metadata has separate configured limits.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
#[repr(C)]
pub struct ResourceSnapshot {
    pub capsule_bytes: u64,
    pub request_bytes: u64,
    pub queued_bytes: u64,
    pub leased_bytes: u64,
    pub pending_sends: u64,
    pub reserved_payload: u64,
    pub maximum_output_bytes: u64,
    pub revision: u64,
}

/// Serialized, nonblocking state for one immutable application pair and one CONNECT lifetime.
pub struct Peer {
    http: HttpPeer,
    facts: TransportFacts,
    policy: Policy,
    out: VecDeque<Output>,
    sends: SendBook,
    headers: Vec<Header>,
    header_bytes: usize,
    headers_ready: bool,
    headers_complete: bool,
    response_gate: bool,
    gated_events: VecDeque<WebTransportEvent>,
    gated_bytes: usize,
    session: Option<Session>,
    streams: BTreeMap<u64, DataStream>,
    ingress: BTreeMap<u64, Ingress>,
    unconsumed: u64,
    pending_bytes: u64,
    connect_started: bool,
    connect_responded: bool,
    local_bidi_high_water: Option<u64>,
    close_actions: Option<u64>,
    goaway: bool,
    fatal: Option<Failure>,
    pub(crate) last_error: Option<Failure>,
}

impl Peer {
    pub fn new(facts: TransportFacts, policy: Policy) -> Result<Self, Failure> {
        facts.validate()?;
        let role = if facts.role == 0 {
            Role::Client
        } else {
            Role::Server
        };
        let settings = strict_settings(role, facts.initial_max_bidi, facts.initial_max_data)
            .max_field_section_size(VarInt::new(policy.limits.header_bytes).map_err(argument_error)?);
        let mut http = match role {
            Role::Client => HttpPeer::Client(ClientConnection::new(settings)),
            Role::Server => HttpPeer::Server(ServerConnection::new(settings)),
        };
        dispatch!(&mut http, set_webtransport_transport_verified, true, true).map_err(Failure::from)?;
        let [control, encoder, decoder] = policy.control_streams;
        let init = dispatch!(&mut http, init_h3_streams, control, encoder, decoder).map_err(Failure::from)?;
        let sends = SendBook::new(policy.limits);
        let mut peer = Self {
            http,
            facts,
            policy,
            out: VecDeque::new(),
            sends,
            headers: Vec::new(),
            header_bytes: 0,
            headers_ready: false,
            headers_complete: false,
            response_gate: false,
            gated_events: VecDeque::new(),
            gated_bytes: 0,
            session: None,
            streams: BTreeMap::new(),
            ingress: BTreeMap::new(),
            unconsumed: 0,
            pending_bytes: 0,
            connect_started: false,
            connect_responded: false,
            local_bidi_high_water: (facts.role == 0).then_some(0),
            close_actions: None,
            goaway: false,
            fatal: None,
            last_error: None,
        };
        for id in [0, control, encoder, decoder] {
            peer.ingress.insert(
                id,
                Ingress {
                    ready: true,
                    ..Ingress::default()
                },
            );
        }
        peer.queue_init(init)?;
        Ok(peer)
    }

    pub fn policy(&self) -> &Policy {
        &self.policy
    }

    pub fn session_state(&self) -> Option<SessionState> {
        self.session.as_ref().map(Session::state)
    }

    pub fn last_error(&self) -> Option<&Failure> {
        self.last_error.as_ref()
    }

    fn queue_init(&mut self, init: H3InitData) -> Result<(), Failure> {
        for (id, bytes) in [
            (init.control_stream_id, init.control_data),
            (init.encoder_stream_id, init.encoder_data),
            (init.decoder_stream_id, init.decoder_data),
        ] {
            self.enqueue(Output::new(1, id, u64::MAX, bytes))?;
        }
        Ok(())
    }

    pub fn output(&self) -> Option<&Output> {
        self.out.iter().find(|output| self.output_ready(output))
    }

    pub fn pop_output(&mut self) -> Option<Output> {
        let index = self.out.iter().position(|output| self.output_ready(output))?;
        let output = self.out.remove(index)?;
        if output.kind == 1 || output.kind == 2 {
            self.sends.lease(output.arg);
        } else if output.kind == 3
            && let Some(stream) = self.streams.get_mut(&output.stream)
        {
            stream.reset_issued = true;
        }
        self.advance_close_actions();
        Some(output)
    }

    pub fn resource_snapshot(&self) -> ResourceSnapshot {
        ResourceSnapshot {
            capsule_bytes: dispatch!(&self.http, wt_buffered_capsule_bytes) as u64,
            request_bytes: dispatch!(&self.http, buffered_request_bytes) as u64,
            queued_bytes: self.queued_bytes() as u64,
            leased_bytes: self.sends.leased_bytes() as u64,
            pending_sends: self.sends.pending_count() as u64,
            reserved_payload: self
                .session
                .as_ref()
                .map_or(0, |session| session.flow_state().data_sent),
            maximum_output_bytes: self.policy.limits.output_bytes,
            revision: 2,
        }
    }

    fn queued_bytes(&self) -> usize {
        self.out
            .iter()
            .map(|output| output.data.capacity())
            .sum::<usize>()
            + self.gated_bytes
    }

    fn admit_events(&self, count: usize) -> Result<(), Failure> {
        if count > self.policy.limits.output_events as usize {
            return Err(Failure::local(
                Code::LimitExceeded,
                "split input to fit the event budget",
            ));
        }
        if count
            > (self.policy.limits.output_events as usize)
                .saturating_sub(self.out.len() + self.gated_events.len())
        {
            return Err(Failure::local(
                Code::Backpressure,
                "drain output events before retrying",
            ));
        }
        Ok(())
    }

    fn admit_output(&self, bytes: usize) -> Result<(), Failure> {
        self.admit_events(1)?;
        if bytes > self.policy.limits.output_bytes as usize {
            return Err(Failure::local(
                Code::LimitExceeded,
                "split input to fit the output byte budget",
            ));
        }
        if bytes
            > (self.policy.limits.output_bytes as usize)
                .saturating_sub(self.queued_bytes() + self.sends.leased_bytes())
        {
            return Err(Failure::local(
                Code::Backpressure,
                "complete/drain output before retrying",
            ));
        }
        Ok(())
    }

    fn admit_send(&self, bytes: usize, count: usize) -> Result<(), Failure> {
        self.admit_output(bytes)?;
        self.admit_events(count)?;
        if count > (self.policy.limits.pending_sends as usize).saturating_sub(self.sends.pending_count()) {
            return Err(Failure::local(
                Code::Backpressure,
                "send descriptor admission limit",
            ));
        }
        Ok(())
    }

    fn enqueue(&mut self, mut output: Output) -> Result<(), Failure> {
        self.admit_output(output.data.capacity()).map_err(|_| {
            Failure::new(
                Scope::Connection,
                Code::LimitExceeded,
                "generated output exceeded reserved budget",
            )
        })?;
        if output.kind == 1 || output.kind == 2 {
            let (token, offset) = self
                .sends
                .register(
                    output.stream,
                    output.data.len(),
                    output.kind == 2,
                    output.flags & 2 != 0,
                    output.flags & 1 != 0,
                )
                .map_err(|_| {
                    Failure::new(
                        Scope::Connection,
                        Code::LimitExceeded,
                        "generated sends exceeded budget",
                    )
                })?;
            output.arg = token;
            output.aux = offset;
        }
        self.out.push_back(output);
        Ok(())
    }

    fn output_ready(&self, output: &Output) -> bool {
        match output.kind {
            1 | 2 => self.sends.can_lease(output.arg),
            3 => self
                .streams
                .get(&output.stream)
                .is_none_or(|stream| self.sends.completed(output.stream) >= stream.reliable_size),
            _ => true,
        }
    }

    fn cancel_unleased(&mut self, stream: u64) {
        let cancelled = self.sends.cancel_unleased(stream);
        self.out
            .retain(|output| output.kind != 1 || !cancelled.contains(&output.arg));
    }

    fn request_reset(&mut self, id: u64, code: u64) -> Result<(), Failure> {
        self.admit_events(1)?;
        let stream = self
            .streams
            .get_mut(&id)
            .ok_or_else(|| Failure::local(Code::UnknownStream, "unknown reset stream"))?;
        if stream.reset_pending || stream.reset_final.is_some() || self.sends.fin_completed(id) {
            return Err(Failure::local(
                Code::InvalidState,
                "reset pending or send direction completed",
            ));
        }
        stream.send_closed = true;
        stream.reset_pending = true;
        let mut output = Output::new(3, id, stream.session, Vec::new());
        output.arg = code;
        output.aux = stream.reliable_size;
        self.cancel_unleased(id);
        self.enqueue(output)
    }

    fn complete_send(&mut self, token: u64, accepted: u64, flags: u64) -> Result<(), Failure> {
        if let Some(completion) =
            self.sends
                .acknowledge(token, usize::try_from(accepted).map_err(argument_error)?, flags)?
        {
            if completion.partial_terminal {
                self.cancel_unleased(completion.stream);
                if let Some(stream) = self.streams.get_mut(&completion.stream) {
                    stream.send_closed = true;
                } else {
                    return Err(Failure::new(
                        Scope::Connection,
                        Code::SendFailed,
                        "HTTP3 critical/CONNECT write failed",
                    )
                    .at(completion.stream, u64::MAX));
                }
            }
            if completion.prefix_failed {
                return Err(Failure::new(
                    Scope::Connection,
                    Code::SendFailed,
                    "association prefix failed; no reset downgrade",
                )
                .at(completion.stream, 0));
            }
        }
        Ok(())
    }

    fn confirm_reset(&mut self, id: u64, final_size: u64, reliable_size: u64) -> Result<(), Failure> {
        let invalid = || Failure::local(Code::InvalidResetEvidence, "contradictory local reset completion");
        let stream = self
            .streams
            .get(&id)
            .ok_or_else(|| Failure::local(Code::UnknownStream, "unknown reset confirmation stream"))?;
        if let Some(recorded) = stream.reset_final {
            return if recorded == (final_size, reliable_size) {
                Ok(())
            } else {
                Err(invalid())
            };
        }
        if !stream.reset_pending || !stream.reset_issued || self.sends.has_leases(id) {
            return Err(invalid());
        }
        if reliable_size < stream.reliable_size
            || reliable_size > final_size
            || final_size > self.sends.accepted(id)
            || final_size > self.sends.leased_end(id)
            || final_size > stream.sent
        {
            return Err(invalid());
        }
        let returned = stream.sent - final_size;
        let session = self.session.as_mut().ok_or_else(invalid)?;
        session.flow_state_mut().data_sent = session
            .flow_state()
            .data_sent
            .checked_sub(returned)
            .ok_or_else(invalid)?;
        self.streams.get_mut(&id).expect("stream retained").reset_final = Some((final_size, reliable_size));
        Ok(())
    }

    fn active(&mut self, id: u64) -> Result<&mut Session, Failure> {
        if id != 0 {
            return Err(Failure::local(
                Code::UnknownSession,
                "unknown WebTransport session",
            ));
        }
        let session = self
            .session
            .as_mut()
            .ok_or_else(|| Failure::local(Code::UnknownSession, "unknown WebTransport session"))?;
        if session.is_closed() {
            return Err(
                Failure::new(Scope::Session, Code::InvalidState, "WebTransport session closed").at(id, id),
            );
        }
        Ok(session)
    }

    fn reserve_stream(&mut self, id: u64, ready: bool) -> Result<(), Failure> {
        if id > MAX_VARINT || self.ingress.contains_key(&id) {
            return Err(Failure::local(
                Code::InvalidStreamId,
                "invalid or reused stream ID",
            ));
        }
        if self.ingress.len() >= self.policy.limits.lifetime_streams as usize {
            return Err(Failure::new(
                Scope::Connection,
                Code::LimitExceeded,
                "connection stream lifetime exhausted",
            )
            .wire(ErrorCode::ExcessiveLoad.code()));
        }
        self.ingress.insert(
            id,
            Ingress {
                ready,
                ..Ingress::default()
            },
        );
        Ok(())
    }

    /// Run one synchronous state operation. On retryable failure no input bytes were consumed.
    pub fn command(&mut self, op: u32, id: u64, arg: u64, aux: u64, bytes: &[u8]) -> Result<(), Failure> {
        let mut result = self.run(op, id, arg, aux, bytes);
        if result.as_ref().is_err_and(|error| error.scope == Scope::Stream)
            && let Err(error) = self.collect()
        {
            result = Err(error);
        }
        match result {
            Ok(()) => {
                self.last_error = None;
                Ok(())
            }
            Err(mut error) => {
                error.operation = op;
                if error.stream == u64::MAX && matches!(op, 1 | 3 | 4 | 11 | 13 | 17 | 22) {
                    error.stream = id;
                }
                if error.session == u64::MAX && (id == 0 || self.streams.contains_key(&id)) {
                    error.session = 0;
                }
                if error.scope == Scope::Connection {
                    self.gated_events.clear();
                    self.gated_bytes = 0;
                    self.response_gate = false;
                    self.fatal = Some(error.clone());
                }
                self.last_error = Some(error.clone());
                Err(error)
            }
        }
    }

    fn run(&mut self, op: u32, id: u64, arg: u64, aux: u64, bytes: &[u8]) -> Result<(), Failure> {
        if let Some(fatal) = &self.fatal
            && !matches!(op, 20 | 22)
        {
            return Err(fatal.clone());
        }
        if bytes.len() > self.policy.limits.input_bytes as usize {
            return Err(Failure::local(
                Code::LimitExceeded,
                "input exceeds configured call limit",
            ));
        }
        if (!matches!(op, 1 | 2 | 13 | 14 | 16) && !bytes.is_empty())
            || (!matches!(op, 3 | 20 | 22) && aux != 0)
            || (matches!(op, 1 | 13) && arg > 1)
            || (matches!(op, 10 | 14 | 15 | 19) && arg != 0)
            || (op == 2 && (id != 0 || arg != 0))
        {
            return Err(Failure::local(
                Code::InvalidArgument,
                "invalid command payload, flags or reserved argument",
            ));
        }
        match op {
            1 => self.receive_stream(id, bytes, arg == 1)?,
            2 => self.receive_datagram(bytes)?,
            3 => {
                if id > MAX_VARINT || arg > MAX_VARINT || aux > MAX_VARINT {
                    return Err(Failure::local(
                        Code::InvalidArgument,
                        "reset values exceed QUIC range",
                    ));
                }
                self.admit_events(8)?;
                if !self.ingress.contains_key(&id) {
                    if id & 1 == u64::from(self.facts.role) {
                        return Err(Failure::local(
                            Code::UnknownStream,
                            "reset for unallocated local stream",
                        ));
                    }
                    self.reserve_stream(id, false)?;
                }
                let ingress = self.ingress.get_mut(&id).expect("registered ingress");
                ingress.terminal = true;
                dispatch!(&mut self.http, stream_reset, id, arg, aux).map_err(Failure::from)?;
            }
            4 => {
                if arg > MAX_VARINT || !self.ingress.contains_key(&id) {
                    return Err(Failure::local(
                        Code::UnknownStream,
                        "STOP_SENDING for unknown stream",
                    ));
                }
                self.admit_events(1)?;
                dispatch!(&mut self.http, stop_sending, id, arg).map_err(Failure::from)?;
            }
            10 => self.connect(id)?,
            11 => self.respond(id, arg)?,
            12 => self.open_stream(id, arg)?,
            13 => self.send_stream(id, bytes, arg == 1)?,
            14 => {
                if !self.active(id)?.is_established() {
                    return Err(
                        Failure::new(Scope::Session, Code::InvalidState, "session not established")
                            .at(id, id),
                    );
                }
                if bytes.len() as u64 + 10 > self.facts.peer_max_datagram {
                    return Err(Failure::local(
                        Code::LimitExceeded,
                        "datagram exceeds peer transport limit",
                    ));
                }
                self.admit_send(bytes.len() + 1, 1)?;
                let encoded = dispatch!(&self.http, send_datagram, id, bytes).map_err(Failure::from)?;
                self.enqueue(Output::new(2, 0, id, encoded))?;
            }
            15 => {
                self.admit_send(32, 2)?;
                self.active(id)?.drain();
                self.flush_capsules(id)?;
            }
            16 => {
                let code = u32::try_from(arg).map_err(argument_error)?;
                let reason = std::str::from_utf8(bytes).map_err(argument_error)?;
                if reason.len() > 1024 {
                    return Err(Failure::local(
                        Code::LimitExceeded,
                        "close reason exceeds 1024 bytes",
                    ));
                }
                self.admit_events(4)?;
                self.admit_send(reason.len() + 128, 2)?;
                self.active(id)?.close_with_error(code, reason);
                self.flush_capsules(id)?;
                self.queue_session_resets(id, 0x170d7b68)?;
            }
            17 => {
                let code = u32::try_from(arg).map_err(argument_error)?;
                self.request_reset(id, 0x52e4a40fa8db + u64::from(code) + u64::from(code) / 0x1e)?;
            }
            18 => {
                if id != 0 || self.session.is_none() || arg > self.unconsumed {
                    return Err(Failure::local(
                        Code::InvalidArgument,
                        "cannot consume unreceived bytes",
                    ));
                }
                self.admit_send(32, 1)?;
                self.unconsumed -= arg;
                dispatch!(&mut self.http, wt_data_consumed, id, arg);
                self.flush_capsules(id)?;
            }
            19 => {
                self.admit_send(16, 1)?;
                dispatch!(
                    &mut self.http,
                    send_goaway,
                    VarInt::new(id).map_err(argument_error)?
                )
                .map_err(Failure::from)?;
                self.goaway = true;
            }
            20 => self.complete_send(id, arg, aux)?,
            22 => self.confirm_reset(id, arg, aux)?,
            _ => return Err(Failure::local(Code::InvalidOperation, "unknown operation")),
        }
        if self.fatal.is_some() {
            Ok(())
        } else {
            self.collect()
        }
    }

    fn connect(&mut self, allocated_id: u64) -> Result<(), Failure> {
        if self.facts.role != 0 {
            return Err(Failure::local(
                Code::WrongRole,
                "only the client initiates CONNECT",
            ));
        }
        if allocated_id != 0 {
            return Err(Failure::local(
                Code::InvalidStreamId,
                "CONNECT requires the reserved client stream zero",
            ));
        }
        if self.goaway || self.connect_started {
            return Err(Failure::local(
                Code::SessionLimit,
                "one CONNECT lifetime; no pooling or retry on this connection",
            ));
        }
        self.admit_send(self.policy.limits.header_bytes as usize, 2)?;
        let mut request = ConnectRequest::new("https", &self.policy.authority, &self.policy.path)
            .available_protocols(vec![self.policy.application_protocol.clone()]);
        if self.policy.origin_policy != OriginPolicy::AbsentOnly {
            request = request.origin(&self.policy.origin);
        }
        let HttpPeer::Client(client) = &mut self.http else {
            unreachable!()
        };
        let id = client
            .send_request(&request.to_headers().map_err(argument_error)?, false)
            .map_err(Failure::from)?;
        if id != allocated_id {
            return Err(Failure::new(
                Scope::Connection,
                Code::InvalidStreamId,
                "HTTP3 and QUIC allocators disagree",
            ));
        }
        self.connect_started = true;
        self.enqueue(Output::new(11, id, id, Vec::new()))
    }

    fn open_stream(&mut self, session: u64, allocated_id: u64) -> Result<(), Failure> {
        self.admit_send(3, 1)?;
        if allocated_id > MAX_VARINT
            || allocated_id & 3 != u64::from(self.facts.role)
            || self.ingress.contains_key(&allocated_id)
            || self
                .local_bidi_high_water
                .is_some_and(|last| allocated_id <= last)
        {
            return Err(Failure::local(
                Code::InvalidStreamId,
                "data stream must be a fresh locally allocated bidi ID",
            ));
        }
        if self.ingress.len() >= self.policy.limits.lifetime_streams as usize {
            return Err(Failure::local(
                Code::LimitExceeded,
                "stream lifetime admission limit",
            ));
        }
        if self.goaway || !self.active(session)?.is_established() {
            return Err(
                Failure::new(Scope::Session, Code::InvalidState, "session must be established")
                    .at(session, session),
            );
        }
        if !self.active(session)?.try_open_stream(true) {
            self.flush_capsules(session)?;
            return Err(Failure::local(
                Code::FlowControlBlocked,
                "WebTransport stream credit exhausted",
            ));
        }
        dispatch!(&mut self.http, register_local_wt_stream, session, allocated_id).map_err(Failure::from)?;
        self.reserve_stream(allocated_id, true)?;
        self.local_bidi_high_water = Some(allocated_id);
        let mut header = Vec::with_capacity(3);
        StreamHeader::new(session)
            .map_err(argument_error)?
            .encode_bidirectional(&mut header);
        self.streams.insert(
            allocated_id,
            DataStream {
                session,
                reliable_size: header.len() as u64,
                sent: header.len() as u64,
                send_closed: false,
                reset_pending: false,
                reset_issued: false,
                reset_final: None,
                stop_queued: false,
            },
        );
        let mut output = Output::new(1, allocated_id, session, header);
        output.flags = 2;
        self.enqueue(output)
    }

    fn send_stream(&mut self, id: u64, bytes: &[u8], fin: bool) -> Result<(), Failure> {
        self.admit_send(bytes.len(), 1)?;
        let stream = self
            .streams
            .get(&id)
            .ok_or_else(|| Failure::local(Code::UnknownStream, "unknown data stream"))?;
        if stream.send_closed {
            return Err(Failure::local(Code::InvalidState, "stream send side closed"));
        }
        let sent = stream
            .sent
            .checked_add(bytes.len() as u64)
            .filter(|value| *value <= MAX_VARINT)
            .ok_or_else(|| Failure::local(Code::LimitExceeded, "stream offset exhausted"))?;
        let session = stream.session;
        if !self.active(session)?.try_send_data(bytes.len() as u64) {
            self.flush_capsules(session)?;
            return Err(Failure::local(
                Code::FlowControlBlocked,
                "WebTransport data credit exhausted",
            ));
        }
        let stream = self.streams.get_mut(&id).expect("stream retained");
        stream.sent = sent;
        stream.send_closed = fin;
        let mut output = Output::new(1, id, session, bytes.to_vec());
        output.flags = u32::from(fin);
        self.enqueue(output)
    }

    fn receive_stream(&mut self, id: u64, bytes: &[u8], fin: bool) -> Result<(), Failure> {
        if id > MAX_VARINT || (id & 2 != 0 && id & 1 == u64::from(self.facts.role)) {
            return Err(Failure::local(
                Code::InvalidStreamId,
                "invalid received stream direction/identifier",
            ));
        }
        if !self.ingress.contains_key(&id) && id & 1 == u64::from(self.facts.role) {
            return Err(Failure::local(
                Code::UnknownStream,
                "peer bytes on an unallocated local stream",
            ));
        }
        let data_stream = self.streams.contains_key(&id);
        let events = if data_stream {
            3
        } else {
            bytes.len() / 2 + self.policy.limits.header_bytes as usize / 32 + 8
        };
        self.admit_events(events)?;
        self.admit_output(
            bytes
                .len()
                .saturating_add(if data_stream && !fin { 0 } else { 128 }),
        )?;
        if id == 0 {
            let request = dispatch!(&self.http, buffered_request_bytes) as u64;
            let capsule = dispatch!(&self.http, wt_buffered_capsule_bytes) as u64;
            if bytes.len() as u64 > self.policy.limits.request_bytes.saturating_sub(request)
                || bytes.len() as u64 > self.policy.limits.capsule_bytes.saturating_sub(capsule)
            {
                return Err(Failure::new(
                    Scope::Connection,
                    Code::LimitExceeded,
                    "request/capsule ingress budget",
                )
                .wire(ErrorCode::ExcessiveLoad.code()));
            }
        }
        if self.session.is_none()
            && bytes.len() as u64
                > self
                    .policy
                    .limits
                    .pending_bytes
                    .saturating_sub(self.pending_bytes)
        {
            return Err(Failure::new(
                Scope::Connection,
                Code::LimitExceeded,
                "pending session ingress budget",
            )
            .wire(ErrorCode::ExcessiveLoad.code()));
        }
        if !self.ingress.contains_key(&id) {
            self.reserve_stream(id, false)?;
        }
        let input = self.ingress.get_mut(&id).expect("registered ingress");
        if input.terminal {
            return Err(
                Failure::new(Scope::Stream, Code::InvalidState, "bytes after receive FIN/reset")
                    .wire(ErrorCode::MessageError.code()),
            );
        }
        if id & 2 != 0 {
            if bytes.len() as u64
                > self
                    .policy
                    .limits
                    .control_bytes
                    .saturating_sub(input.control_bytes)
            {
                return Err(Failure::new(
                    Scope::Connection,
                    Code::LimitExceeded,
                    "unidirectional control lifetime budget",
                )
                .wire(ErrorCode::ExcessiveLoad.code()));
            }
            input.control_bytes += bytes.len() as u64;
        }
        if self.session.is_none() {
            self.pending_bytes += bytes.len() as u64;
        }
        input.terminal = fin;
        if input.ready {
            return dispatch!(&mut self.http, feed_stream, id, bytes, fin).map_err(Failure::from);
        }
        // Inspect only the maintained decoder's bounded association/type prefix. No body copy.
        let mut consumed = 0;
        while consumed < bytes.len() && !input.ready {
            input.prefix[input.prefix_len] = bytes[consumed];
            input.prefix_len += 1;
            consumed += 1;
            let prefix = &input.prefix[..input.prefix_len];
            if id & 2 != 0 {
                if let Ok((kind, _)) = shiguredo_http3::varint::decode(prefix) {
                    if kind.get() == 0x54 {
                        return Err(Failure::new(
                            Scope::Connection,
                            Code::PeerProtocol,
                            "UA profile forbids WT uni streams",
                        )
                        .wire(ErrorCode::StreamCreationError.code()));
                    }
                    input.ready = true;
                }
            } else {
                match StreamHeader::decode_bidirectional_checked(prefix) {
                    Ok((header, _)) if header.session_id() == 0 => input.ready = true,
                    Ok(_) => {
                        return Err(Failure::new(
                            Scope::Connection,
                            Code::SessionLimit,
                            "cross-session stream refused",
                        )
                        .wire(ErrorCode::IdError.code()));
                    }
                    Err(StreamHeaderDecodeError::BufferTooShort) => {}
                    Err(_) => {
                        return Err(Failure::new(
                            Scope::Connection,
                            Code::PeerProtocol,
                            "invalid WT bidi prefix",
                        )
                        .wire(ErrorCode::FrameUnexpected.code()));
                    }
                }
            }
        }
        if !input.ready {
            return if fin {
                Err(Failure::new(
                    Scope::Stream,
                    Code::PeerProtocol,
                    "truncated stream association prefix",
                )
                .wire(ErrorCode::FrameError.code()))
            } else {
                Ok(())
            };
        }
        dispatch!(
            &mut self.http,
            feed_stream,
            id,
            &input.prefix[..input.prefix_len],
            fin && consumed == bytes.len()
        )
        .map_err(Failure::from)?;
        if consumed < bytes.len() {
            dispatch!(&mut self.http, feed_stream, id, &bytes[consumed..], fin).map_err(Failure::from)?;
        }
        Ok(())
    }

    fn receive_datagram(&mut self, bytes: &[u8]) -> Result<(), Failure> {
        if bytes.len() as u64 + 9 > self.facts.local_max_datagram {
            return Err(Failure::local(
                Code::LimitExceeded,
                "received datagram exceeds local transport limit",
            ));
        }
        let (quarter, _) = shiguredo_http3::varint::decode(bytes).map_err(|_| {
            Failure::new(
                Scope::Connection,
                Code::PeerProtocol,
                "missing HTTP datagram quarter ID",
            )
            .wire(ErrorCode::H3DatagramError.code())
        })?;
        if quarter.get() > MAX_VARINT / 4 {
            return Err(Failure::new(
                Scope::Connection,
                Code::PeerProtocol,
                "HTTP datagram quarter ID overflow",
            )
            .wire(ErrorCode::H3DatagramError.code()));
        }
        if quarter.get() != 0 {
            return Err(Failure::new(
                Scope::Session,
                Code::UnknownSession,
                "datagram for a different session",
            )
            .at(u64::MAX, quarter.get() * 4));
        }
        self.admit_output(bytes.len())?;
        if self.session.is_none() {
            if bytes.len() as u64
                > self
                    .policy
                    .limits
                    .pending_bytes
                    .saturating_sub(self.pending_bytes)
            {
                return Err(Failure::new(
                    Scope::Connection,
                    Code::LimitExceeded,
                    "pending datagram budget exhausted",
                )
                .wire(ErrorCode::ExcessiveLoad.code()));
            }
            self.pending_bytes += bytes.len() as u64;
        }
        dispatch!(&mut self.http, feed_datagram, bytes).map_err(Failure::from)
    }

    fn validate_request(&self) -> Result<(), Failure> {
        let denied = |code, message| Failure::new(Scope::Session, code, message).at(0, 0);
        for name in [b"origin".as_slice(), b"wt-available-protocols"] {
            if self.headers.iter().filter(|header| header.name() == name).count() > 1 {
                return Err(denied(Code::PeerProtocol, "duplicate negotiation header")
                    .wire(ErrorCode::MessageError.code()));
            }
        }
        if self
            .headers
            .iter()
            .any(|header| header.name().starts_with(b"sec-webtransport-"))
            || !self
                .headers
                .iter()
                .any(|header| header.name() == b":protocol" && header.value() == b"webtransport-h3")
        {
            return Err(denied(
                Code::UnsupportedDraft,
                "only the draft16 WebTransport token is accepted",
            ));
        }
        let refs: Vec<_> = self
            .headers
            .iter()
            .map(|header| (header.name(), header.value()))
            .collect();
        let request = ConnectRequest::from_headers(&refs).map_err(|_| {
            denied(Code::PeerProtocol, "invalid Extended CONNECT").wire(ErrorCode::MessageError.code())
        })?;
        request.validate().map_err(|_| {
            denied(Code::PeerProtocol, "CONNECT must use HTTPS").wire(ErrorCode::MessageError.code())
        })?;
        if request.authority != self.policy.authority {
            return Err(denied(Code::AuthorityDenied, "CONNECT authority is not allowed"));
        }
        if request.path != self.policy.path {
            return Err(denied(Code::PathDenied, "CONNECT resource is not allowed"));
        }
        let origin_ok = match (self.policy.origin_policy, request.origin.as_deref()) {
            (OriginPolicy::AbsentOnly | OriginPolicy::OptionalExact, None) => true,
            (OriginPolicy::OptionalExact | OriginPolicy::RequiredExact, Some(origin)) => {
                origin == self.policy.origin
            }
            _ => false,
        };
        if !origin_ok {
            return Err(denied(Code::OriginDenied, "CONNECT Origin is not allowed"));
        }
        if request.available_protocols != [self.policy.application_protocol.as_str()] {
            return Err(denied(
                Code::ApplicationProtocolDenied,
                "exact configured application protocol required",
            ));
        }
        Ok(())
    }

    fn respond(&mut self, id: u64, status: u64) -> Result<(), Failure> {
        if self.facts.role != 1 {
            return Err(Failure::local(
                Code::WrongRole,
                "only the server responds to CONNECT",
            ));
        }
        if id != 0 || !self.headers_ready || self.connect_responded || self.goaway {
            return Err(Failure::local(
                Code::InvalidState,
                "CONNECT response is not admissible",
            ));
        }
        if !matches!(status, 200 | 400 | 403 | 405 | 406 | 421 | 429 | 503) {
            return Err(Failure::local(
                Code::InvalidArgument,
                "unsupported CONNECT response status",
            ));
        }
        if status == 200 {
            self.validate_request()?;
        }
        self.admit_send(self.policy.limits.header_bytes as usize, 3)?;
        let mut headers =
            vec![Header::new(b":status", status.to_string().as_bytes()).map_err(argument_error)?];
        if status == 200 {
            headers.push(
                Header::new(
                    b"wt-protocol",
                    format!("\"{}\"", self.policy.application_protocol).as_bytes(),
                )
                .map_err(argument_error)?,
            );
        }
        let HttpPeer::Server(server) = &mut self.http else {
            unreachable!()
        };
        server
            .send_response(id, &headers, status != 200)
            .map_err(Failure::from)?;
        self.connect_responded = true;
        self.headers = Vec::new();
        self.header_bytes = 0;
        Ok(())
    }

    fn validate_response(&mut self) -> Result<(), Failure> {
        let status = self
            .headers
            .iter()
            .find(|header| header.name() == b":status")
            .and_then(|header| std::str::from_utf8(header.value()).ok())
            .and_then(|value| value.parse::<u64>().ok())
            .ok_or_else(|| {
                Failure::new(
                    Scope::Session,
                    Code::PeerProtocol,
                    "missing CONNECT response status",
                )
            })?;
        if status == 200 {
            let protocol: Vec<_> = self
                .headers
                .iter()
                .filter(|header| header.name() == b"wt-protocol")
                .collect();
            let expected = format!("\"{}\"", self.policy.application_protocol);
            if protocol.len() != 1
                || protocol[0].value() != expected.as_bytes()
                || self
                    .headers
                    .iter()
                    .any(|header| header.name().starts_with(b"sec-webtransport-"))
            {
                return Err(Failure::new(
                    Scope::Connection,
                    Code::ApplicationProtocolDenied,
                    "CONNECT response did not select the exact protocol",
                )
                .wire(ErrorCode::MessageError.code()));
            }
        } else if !(300..=599).contains(&status) {
            return Err(Failure::new(
                Scope::Connection,
                Code::PeerProtocol,
                "only status 200 can establish the configured CONNECT session",
            )
            .wire(ErrorCode::MessageError.code()));
        } else {
            let mut output = Output::new(23, 0, 0, Vec::new());
            output.arg = status;
            self.enqueue(output)?;
        }
        self.headers = Vec::new();
        self.header_bytes = 0;
        Ok(())
    }

    fn flush_capsules(&mut self, id: u64) -> Result<(), Failure> {
        let mut capsules = dispatch!(&mut self.http, take_wt_pending_capsules, id);
        if let Some(session) = self.session.as_mut() {
            capsules.extend(session.take_pending_capsules());
        }
        for capsule in capsules {
            let fin = matches!(capsule, Capsule::CloseSession { .. });
            let mut bytes = Vec::new();
            capsule.encode(&mut bytes);
            dispatch!(&mut self.http, send_body, id, &bytes, fin).map_err(Failure::from)?;
        }
        self.collect_writes()
    }

    fn queue_session_resets(&mut self, session: u64, code: u64) -> Result<(), Failure> {
        if session != 0 {
            return Err(Failure::new(
                Scope::Connection,
                Code::SessionLimit,
                "cross-session close refused",
            ));
        }
        self.close_actions = Some(code);
        let datagrams = self.sends.cancel_unleased_datagrams();
        self.out
            .retain(|output| output.kind != 2 || !datagrams.contains(&output.arg));
        let ids: Vec<_> = self.streams.keys().copied().collect();
        for id in ids {
            self.streams.get_mut(&id).expect("retained stream").send_closed = true;
            self.cancel_unleased(id);
        }
        Ok(())
    }

    /// Closing a large session must not allocate one unbounded batch of reset actions.
    /// The retained stream records are the work list; each successful poll opens one slot.
    fn advance_close_actions(&mut self) {
        let Some(code) = self.close_actions else { return };
        while self.out.len() < self.policy.limits.output_events as usize {
            let Some((&id, stream)) = self.streams.iter().find(|(id, stream)| {
                !stream.stop_queued
                    || (!stream.reset_pending
                        && stream.reset_final.is_none()
                        && !self.sends.fin_completed(**id))
            }) else {
                self.close_actions = None;
                break;
            };
            if !stream.stop_queued {
                self.streams.get_mut(&id).expect("retained stream").stop_queued = true;
                let mut stop = Output::new(4, id, 0, Vec::new());
                stop.arg = code;
                self.out.push_back(stop);
            } else if let Err(error) = self.request_reset(id, code) {
                self.fatal = Some(error.clone());
                self.last_error = Some(error);
                break;
            }
        }
    }

    fn collect_writes(&mut self) -> Result<(), Failure> {
        let mut ids: Vec<_> = match &self.http {
            HttpPeer::Client(client) => client.writable_streams().collect(),
            HttpPeer::Server(server) => server.writable_streams().collect(),
        };
        ids.sort_unstable();
        for id in ids {
            while let Some((bytes, fin)) = dispatch!(&mut self.http, take_stream_data, id) {
                let mut output = Output::new(1, id, if id == 0 { 0 } else { u64::MAX }, bytes);
                output.flags = u32::from(fin);
                self.enqueue(output)?;
                if fin {
                    break;
                }
            }
        }
        Ok(())
    }

    fn collect(&mut self) -> Result<(), Failure> {
        while let Some(event) = dispatch!(&mut self.http, poll_event).map_err(Failure::from)? {
            match event {
                Event::SettingsReceived {
                    settings,
                    wt_settings,
                } => {
                    if wt_settings.is_none_or(|wt| wt.wt_enabled.get() != 1)
                        || settings.h3_datagram != Some(true)
                        || (self.facts.role == 0 && settings.enable_connect_protocol != Some(true))
                    {
                        return Err(Failure::new(
                            Scope::Connection,
                            Code::SettingsMismatch,
                            "required draft16 settings missing",
                        )
                        .wire(ErrorCode::SettingsError.code()));
                    }
                    self.enqueue(Output::new(10, 0, u64::MAX, Vec::new()))?;
                }
                Event::HeadersBegin { stream_id } => {
                    if stream_id != 0 || self.headers_ready || self.headers_complete {
                        return Err(Failure::new(
                            Scope::Connection,
                            Code::SessionLimit,
                            "only one CONNECT header block allowed",
                        )
                        .wire(ErrorCode::RequestRejected.code()));
                    }
                    self.headers.clear();
                    self.header_bytes = 0;
                }
                Event::Header {
                    stream_id,
                    name,
                    value,
                } => {
                    let bytes = name.len() + value.len() + 32;
                    if stream_id != 0
                        || self.headers.len() >= self.policy.limits.header_count as usize
                        || bytes
                            > (self.policy.limits.header_bytes as usize).saturating_sub(self.header_bytes)
                    {
                        return Err(Failure::new(
                            Scope::Connection,
                            Code::LimitExceeded,
                            "CONNECT header budget exceeded",
                        )
                        .wire(ErrorCode::ExcessiveLoad.code()));
                    }
                    self.header_bytes += bytes;
                    self.headers
                        .push(Header::new(&name, &value).map_err(argument_error)?);
                }
                Event::HeadersEnd { stream_id } => {
                    if stream_id != 0 {
                        return Err(Failure::new(
                            Scope::Connection,
                            Code::SessionLimit,
                            "unexpected CONNECT stream",
                        ));
                    }
                    self.headers_ready = true;
                    self.headers_complete = true;
                    if self.facts.role == 1 {
                        self.connect_started = true;
                        self.enqueue(Output::new(12, stream_id, stream_id, Vec::new()))?;
                    } else {
                        self.validate_response()?;
                        self.response_gate = false;
                        while let Some(event) = self.gated_events.pop_front() {
                            self.gated_bytes -= retained_event_bytes(&event);
                            self.collect_wt(event)?;
                        }
                    }
                }
                Event::GoawayReceived { id } => {
                    self.goaway = true;
                    self.enqueue(Output::new(28, id.get(), u64::MAX, Vec::new()))?;
                }
                Event::WebTransport(event) => {
                    // The maintained engine emits establishment/buffered data before the
                    // response header events. Never publish them before caller-policy validation.
                    if self.facts.role == 0
                        && !self.headers_complete
                        && (self.response_gate
                            || matches!(event, WebTransportEvent::SessionEstablished { .. }))
                    {
                        self.response_gate = true;
                        let bytes = retained_event_bytes(&event);
                        self.admit_output(bytes).map_err(|_| {
                            Failure::new(
                                Scope::Connection,
                                Code::LimitExceeded,
                                "response gate output budget exceeded",
                            )
                        })?;
                        self.gated_bytes += bytes;
                        self.gated_events.push_back(event);
                    } else {
                        self.collect_wt(event)?;
                    }
                }
                _ => {}
            }
        }
        if self.session.is_some() {
            self.flush_capsules(0)?;
        }
        self.collect_writes()?;
        self.advance_close_actions();
        Ok(())
    }

    fn collect_wt(&mut self, event: WebTransportEvent) -> Result<(), Failure> {
        match event {
            WebTransportEvent::SessionEstablished {
                session_id,
                flow_control_enabled,
            } => {
                if session_id != 0 || self.session.is_some() || !flow_control_enabled {
                    return Err(Failure::new(
                        Scope::Connection,
                        Code::SessionLimit,
                        "unexpected session or absent capsule credit",
                    ));
                }
                let settings = dispatch!(&self.http, peer_settings)
                    .and_then(|settings| settings.wt_settings.as_ref())
                    .ok_or_else(|| {
                        Failure::new(
                            Scope::Connection,
                            Code::SettingsMismatch,
                            "missing peer WT settings",
                        )
                    })?;
                let mut session = Session::new(session_id);
                session.set_flow_control_enabled(true);
                *session.remote_limits_mut() = FlowControlLimits {
                    max_streams_uni: settings.wt_initial_max_streams_uni.get(),
                    max_streams_bidi: settings.wt_initial_max_streams_bidi.get(),
                    max_data: settings.wt_initial_max_data.get(),
                };
                session.set_established();
                self.session = Some(session);
                self.pending_bytes = 0;
                let mut output = Output::new(13, session_id, session_id, Vec::new());
                output.flags = 1;
                self.enqueue(output)?;
            }
            WebTransportEvent::BidiStreamOpen {
                stream_id,
                session_id,
            } => {
                if session_id != 0
                    || self.streams.contains_key(&stream_id)
                    || !self.ingress.contains_key(&stream_id)
                {
                    return Err(Failure::new(
                        Scope::Connection,
                        Code::InvalidStreamId,
                        "unexpected WT stream association",
                    ));
                }
                self.streams.insert(
                    stream_id,
                    DataStream {
                        session: session_id,
                        reliable_size: 0,
                        sent: 0,
                        send_closed: false,
                        reset_pending: false,
                        reset_issued: false,
                        reset_final: None,
                        stop_queued: false,
                    },
                );
                self.enqueue(Output::new(14, stream_id, session_id, Vec::new()))?;
            }
            WebTransportEvent::BidiStreamData { stream_id, data } => {
                if !self.streams.contains_key(&stream_id) {
                    return Err(Failure::new(
                        Scope::Connection,
                        Code::UnknownStream,
                        "unassociated WT stream data",
                    ));
                }
                self.unconsumed = self
                    .unconsumed
                    .checked_add(data.len() as u64)
                    .filter(|value| *value <= MAX_VARINT)
                    .ok_or_else(|| {
                        Failure::new(
                            Scope::Connection,
                            Code::LimitExceeded,
                            "receive counter exhausted",
                        )
                    })?;
                self.enqueue(Output::new(15, stream_id, 0, data))?;
            }
            WebTransportEvent::BidiStreamEnd { stream_id } => {
                self.enqueue(Output::new(16, stream_id, 0, Vec::new()))?;
            }
            WebTransportEvent::Datagram { session_id, payload } => {
                self.enqueue(Output::new(17, 0, session_id, payload))?;
            }
            WebTransportEvent::Capsule { session_id, capsule } => {
                self.active(session_id)?.process_capsule(&capsule).map_err(|_| {
                    Failure::new(Scope::Session, Code::PeerProtocol, "invalid flow-control capsule")
                        .at(session_id, session_id)
                })?;
                let mut encoded = Vec::new();
                capsule.encode(&mut encoded);
                self.enqueue(Output::new(18, session_id, session_id, encoded))?;
            }
            WebTransportEvent::SessionDraining { session_id } => {
                self.active(session_id)?.set_draining();
                self.enqueue(Output::new(19, session_id, session_id, Vec::new()))?;
            }
            WebTransportEvent::SessionClosed {
                session_id,
                error_code,
                close_error_code,
                close_message,
                ..
            } => {
                if let Some(session) = self.session.as_mut() {
                    session.close(None);
                }
                self.queue_session_resets(session_id, error_code)?;
                let mut output = Output::new(20, session_id, session_id, close_message.into_bytes());
                output.arg = u64::from(close_error_code);
                output.aux = error_code;
                self.enqueue(output)?;
            }
            WebTransportEvent::StreamReset {
                session_id,
                stream_id,
                error_code,
                final_size,
            } => {
                let mut output = Output::new(21, stream_id, session_id, Vec::new());
                output.arg = error_code;
                output.aux = final_size;
                self.enqueue(output)?;
            }
            WebTransportEvent::StreamStopSending {
                session_id,
                stream_id,
                error_code,
            } => {
                let mut output = Output::new(22, stream_id, session_id, Vec::new());
                output.arg = error_code;
                self.enqueue(output)?;
            }
            WebTransportEvent::BufferedStreamRejected {
                stream_id,
                error_code,
            } => {
                let mut output = Output::new(4, stream_id, u64::MAX, Vec::new());
                output.arg = error_code;
                self.enqueue(output)?;
            }
            WebTransportEvent::UniStreamOpen { .. }
            | WebTransportEvent::UniStreamData { .. }
            | WebTransportEvent::UniStreamEnd { .. } => {
                return Err(Failure::new(
                    Scope::Connection,
                    Code::PeerProtocol,
                    "UA profile uses bidirectional WT streams",
                ));
            }
        }
        Ok(())
    }
}

fn retained_event_bytes(event: &WebTransportEvent) -> usize {
    match event {
        WebTransportEvent::BidiStreamData { data, .. } | WebTransportEvent::UniStreamData { data, .. } => {
            data.capacity()
        }
        WebTransportEvent::Datagram { payload, .. } => payload.capacity(),
        WebTransportEvent::SessionClosed {
            close_message,
            reset_streams,
            ..
        } => close_message.capacity() + vector_bytes(reset_streams),
        WebTransportEvent::Capsule {
            capsule: Capsule::CloseSession { message, .. },
            ..
        } => message.capacity(),
        WebTransportEvent::Capsule {
            capsule: Capsule::Unknown { payload, .. },
            ..
        } => payload.capacity(),
        _ => 0,
    }
}

fn vector_bytes<T>(values: &Vec<T>) -> usize {
    values.capacity() * size_of::<T>()
}
