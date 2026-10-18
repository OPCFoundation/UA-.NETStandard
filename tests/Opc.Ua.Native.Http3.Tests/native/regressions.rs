// Copyright (c) OPC Foundation. Licensed under the MIT License.
//! Retained candidate and operation-revision-2 regressions against the accepted overlay.
//! Transport facts in these tests are synthetic, never network evidence.

use opcua_http3 as opcua_wt16_proof;
use opcua_http3::error::{Code, Failure, Scope};
use opcua_http3::{Peer, TransportFacts, strict_settings};
pub mod support;
use support::configuration;

fn new_peer(facts: TransportFacts) -> Result<Peer, Failure> {
    let mut config = configuration(facts.role);
    config.authority = opcua_http3::config::Utf8::borrowed("localhost");
    config.path = opcua_http3::config::Utf8::borrowed("/proof");
    config.origin = opcua_http3::config::Utf8::borrowed("https://localhost");
    config.origin_policy = 1;
    let policy = unsafe { opcua_http3::config::Policy::from_config(&config, &facts)? };
    Peer::new(facts, policy)
}
use shiguredo_http3::webtransport::{
    Capsule, ConnectRequest, FlowControlLimits, Session, StreamHeader,
};
use shiguredo_http3::{
    ClientConnection, Error, ErrorCode, Event, Header, QpackEncoder, Role, ServerConnection,
    VarInt, WebTransportEvent,
};

fn facts(role: u32) -> TransportFacts {
    TransportFacts {
        abi_version: 1,
        role,
        local_max_datagram: 65535,
        peer_max_datagram: 65535,
        reliable_reset_parameter: 0x1d,
        reliable_reset_frame: 0x24,
        initial_max_bidi: 8,
        initial_max_data: 4096,
        local_reset_parameter_empty: 1,
        peer_reset_parameter_empty: 1,
        handshake_confirmed: 1,
        mutual_tls_authenticated: 1,
        alpn_is_h3: 1,
        reserved: 0,
    }
}

fn request() -> Vec<Header> {
    ConnectRequest::new("https", "localhost", "/proof")
        .to_headers()
        .unwrap()
}

struct Pair {
    client: ClientConnection,
    server: ServerConnection,
}

impl Pair {
    fn new(bidi: u64, data: u64) -> Self {
        let mut client = ClientConnection::new(strict_settings(Role::Client, bidi, data));
        let mut server = ServerConnection::new(strict_settings(Role::Server, bidi, data));
        client
            .set_webtransport_transport_verified(true, true)
            .unwrap();
        server
            .set_webtransport_transport_verified(true, true)
            .unwrap();
        let c = client.init_h3_streams(2, 6, 10).unwrap();
        let s = server.init_h3_streams(3, 7, 11).unwrap();
        for (id, bytes) in [
            (c.control_stream_id, c.control_data),
            (6, c.encoder_data),
            (10, c.decoder_data),
        ] {
            server.feed_stream(id, &bytes, false).unwrap();
        }
        for (id, bytes) in [
            (s.control_stream_id, s.control_data),
            (7, s.encoder_data),
            (11, s.decoder_data),
        ] {
            client.feed_stream(id, &bytes, false).unwrap();
        }
        client.drain_events().unwrap();
        server.drain_events().unwrap();
        Self { client, server }
    }

    fn pump(&mut self) {
        for _ in 0..32 {
            let mut writes = 0;
            let ids: Vec<_> = self.client.writable_streams().collect();
            for id in ids {
                while let Some((bytes, fin)) = self.client.take_stream_data(id) {
                    self.server.feed_stream(id, &bytes, fin).unwrap();
                    writes += 1;
                }
            }
            let ids: Vec<_> = self.server.writable_streams().collect();
            for id in ids {
                while let Some((bytes, fin)) = self.server.take_stream_data(id) {
                    self.client.feed_stream(id, &bytes, fin).unwrap();
                    writes += 1;
                }
            }
            if writes == 0 {
                return;
            }
        }
        panic!("finite pump did not quiesce");
    }

    fn connect(&mut self) -> u64 {
        let id = self.client.send_request(&request(), false).unwrap();
        self.pump();
        let events = self.server.drain_events().unwrap();
        let headers: Vec<_> = events
            .iter()
            .filter_map(|e| match e {
                Event::Header {
                    stream_id,
                    name,
                    value,
                } if *stream_id == id => Some((name.as_slice(), value.as_slice())),
                _ => None,
            })
            .collect();
        assert!(headers.contains(&(b":method".as_slice(), b"CONNECT".as_slice())));
        assert!(headers.contains(&(b":protocol".as_slice(), b"webtransport-h3".as_slice())));
        assert!(headers.contains(&(b":scheme".as_slice(), b"https".as_slice())));
        assert!(headers.contains(&(b":authority".as_slice(), b"localhost".as_slice())));
        assert!(headers.contains(&(b":path".as_slice(), b"/proof".as_slice())));
        assert!(!events.iter().any(|e| matches!(
            e,
            Event::WebTransport(WebTransportEvent::SessionEstablished { .. })
        )));
        self.server
            .send_response(id, &[Header::new(b":status", b"200").unwrap()], false)
            .unwrap();
        assert!(self.server.drain_events().unwrap().iter().any(|e|
            matches!(e, Event::WebTransport(WebTransportEvent::SessionEstablished { session_id, flow_control_enabled: true }) if *session_id == id)));
        self.pump();
        assert!(self.client.drain_events().unwrap().iter().any(|e|
            matches!(e, Event::WebTransport(WebTransportEvent::SessionEstablished { session_id, flow_control_enabled: true }) if *session_id == id)));
        id
    }
}

fn varint(value: u64) -> Vec<u8> {
    let mut bytes = Vec::new();
    shiguredo_http3::varint::encode_into_vec(&mut bytes, VarInt::new(value).unwrap());
    bytes
}

fn raw_frame(kind: u64, payload: &[u8]) -> Vec<u8> {
    let mut bytes = varint(kind);
    bytes.extend(varint(payload.len() as u64));
    bytes.extend_from_slice(payload);
    bytes
}

fn raw_settings(pairs: &[(u64, u64)]) -> Vec<u8> {
    let mut payload = Vec::new();
    for &(id, value) in pairs {
        payload.extend(varint(id));
        payload.extend(varint(value));
    }
    let mut out = vec![0];
    out.extend(raw_frame(4, &payload));
    out
}

// Independent test-only decoding prevents the candidate encoder/decoder sharing an error.
fn read_integer(bytes: &[u8], position: &mut usize) -> u64 {
    let width = 1 << (bytes[*position] >> 6);
    let mut value = u64::from(bytes[*position] & 0x3f);
    for b in &bytes[*position + 1..*position + width] {
        value = value * 256 + u64::from(*b);
    }
    *position += width;
    value
}

fn read_settings(bytes: &[u8]) -> Vec<(u64, u64)> {
    let mut p = 0;
    assert_eq!(read_integer(bytes, &mut p), 0);
    assert_eq!(read_integer(bytes, &mut p), 4);
    let length = read_integer(bytes, &mut p) as usize;
    assert_eq!(p + length, bytes.len());
    let mut settings = Vec::new();
    while p < bytes.len() {
        let id = read_integer(bytes, &mut p);
        settings.push((id, read_integer(bytes, &mut p)));
    }
    settings
}

fn wt_bytes(session: u64, payload: &[u8]) -> Vec<u8> {
    let mut bytes = Vec::new();
    StreamHeader::new(session)
        .unwrap()
        .encode_bidirectional(&mut bytes);
    bytes.extend_from_slice(payload);
    bytes
}

fn capsule_frame(capsule: Capsule) -> Vec<u8> {
    let mut bytes = Vec::new();
    capsule.encode_as_data_frame(&mut bytes);
    bytes
}

#[test]
fn exact_settings_both_sides_no_obsolete_draft_advertisement() {
    for role in [0, 1] {
        let mut peer = new_peer(facts(role)).unwrap();
        let control = peer.pop_output().unwrap();
        assert_eq!(control.stream, if role == 0 { 2 } else { 3 });
        let settings = read_settings(&control.data);
        assert!(settings.contains(&(0x2c7cf000, 1)));
        assert!(settings.contains(&(0x33, 1)));
        assert!(settings.contains(&(0x2b65, 8)));
        assert!(settings.contains(&(0x2b61, 4096)));
        assert_eq!(settings.contains(&(8, 1)), role == 1);
        for obsolete in [0x2b603742, 0xc671706a, 0x14e9cd29] {
            assert!(!settings.iter().any(|&(id, _)| id == obsolete));
        }
        assert!(
            control
                .data
                .windows(5)
                .any(|w| w == [0xac, 0x7c, 0xf0, 0x00, 0x01])
        );
        assert_eq!(peer.pop_output().unwrap().data, vec![2]);
        assert_eq!(peer.pop_output().unwrap().data, vec![3]);
    }
}

#[test]
fn exact_reset09_and_authenticated_transport_facts_fail_closed() {
    let good = facts(0);
    assert!(good.validate().is_ok());
    for change in 0..10 {
        let mut bad = good;
        match change {
            0 => bad.local_max_datagram = 0,
            1 => bad.peer_max_datagram = 0,
            2 => bad.reliable_reset_parameter = 0x17f7586d2cb571,
            3 => bad.reliable_reset_frame = 0x21,
            4 => bad.local_reset_parameter_empty = 0,
            5 => bad.peer_reset_parameter_empty = 0,
            6 => bad.handshake_confirmed = 0,
            7 => bad.mutual_tls_authenticated = 0,
            8 => bad.alpn_is_h3 = 0,
            _ => bad.initial_max_data = 0,
        }
        assert!(
            new_peer(bad).is_err(),
            "invalid capability partition {change}"
        );
    }
}

#[test]
fn client_requires_settings_datagrams_connect_and_reset() {
    let required = [(0x2c7cf000, 1), (0x33, 1), (8, 1)];
    for missing in 0..5 {
        let mut client = ClientConnection::new(strict_settings(Role::Client, 8, 4096));
        client.init_h3_streams(2, 6, 10).unwrap();
        if missing != 3 {
            client
                .set_webtransport_transport_verified(true, missing != 4)
                .unwrap();
        }
        let settings: Vec<_> = required
            .iter()
            .enumerate()
            .filter_map(|(i, p)| (i != missing).then_some(*p))
            .collect();
        client
            .feed_stream(3, &raw_settings(&settings), false)
            .unwrap();
        assert!(
            client.send_request(&request(), false).is_err(),
            "missing capability {missing}"
        );
    }
    let mut no_settings = ClientConnection::new(strict_settings(Role::Client, 8, 4096));
    no_settings.init_h3_streams(2, 6, 10).unwrap();
    no_settings
        .set_webtransport_transport_verified(true, true)
        .unwrap();
    assert!(no_settings.send_request(&request(), false).is_err());
    let mut no_datagram = ClientConnection::new(strict_settings(Role::Client, 8, 4096));
    assert!(
        no_datagram
            .set_webtransport_transport_verified(false, true)
            .is_err()
    );
}

#[test]
fn server_does_not_process_connect_before_client_settings() {
    let mut client = ClientConnection::new(strict_settings(Role::Client, 8, 4096));
    let mut server = ServerConnection::new(strict_settings(Role::Server, 8, 4096));
    client
        .set_webtransport_transport_verified(true, true)
        .unwrap();
    server
        .set_webtransport_transport_verified(true, true)
        .unwrap();
    let cinit = client.init_h3_streams(2, 6, 10).unwrap();
    let sinit = server.init_h3_streams(3, 7, 11).unwrap();
    client.feed_stream(3, &sinit.control_data, false).unwrap();
    let id = client.send_request(&request(), false).unwrap();
    let (headers, _) = client.take_stream_data(id).unwrap();
    server.feed_stream(id, &headers, false).unwrap();
    assert!(!server.drain_events().unwrap().iter().any(|e| matches!(
        e,
        Event::HeadersEnd { .. }
            | Event::WebTransport(WebTransportEvent::SessionEstablished { .. })
    )));
    server.feed_stream(2, &cinit.control_data, false).unwrap();
    assert!(
        server
            .drain_events()
            .unwrap()
            .contains(&Event::HeadersEnd { stream_id: id })
    );
}

#[test]
fn invalid_wt_setting_value_and_duplicates_are_settings_errors() {
    for settings in [
        vec![(0x2c7cf000, 2), (0x33, 1), (8, 1)],
        vec![(0x2c7cf000, 1), (0x2c7cf000, 1)],
    ] {
        let mut client = ClientConnection::new(strict_settings(Role::Client, 8, 4096));
        assert!(matches!(
            client.feed_stream(3, &raw_settings(&settings), false),
            Err(Error::ConnectionError(ErrorCode::SettingsError))
        ));
    }
}

#[test]
fn old_draft_settings_and_token_cannot_negotiate_with_strict_peer() {
    for old in [0x2b603742, 0xc671706a, 0x14e9cd29] {
        let mut client = ClientConnection::new(strict_settings(Role::Client, 8, 4096));
        client
            .set_webtransport_transport_verified(true, true)
            .unwrap();
        client.init_h3_streams(2, 6, 10).unwrap();
        client
            .feed_stream(3, &raw_settings(&[(old, 1), (0x33, 1), (8, 1)]), false)
            .unwrap();
        assert!(
            client.send_request(&request(), false).is_err(),
            "old setting {old:x}"
        );
    }
    let mut pair = Pair::new(8, 4096);
    let old = ConnectRequest::new("https", "localhost", "/proof")
        .draft_version(shiguredo_http3::webtransport::DraftVersion::Draft02)
        .to_headers()
        .unwrap();
    assert!(pair.client.send_request(&old, false).is_err());
}

#[test]
fn connect_rejects_bad_scheme_and_duplicate_pseudo_headers() {
    // Connection::send_request is a generic HTTP/3 API. WT scheme validation is
    // explicitly performed by ConnectRequest::validate and the native adapter.
    assert!(
        ConnectRequest::new("http", "localhost", "/proof")
            .validate()
            .is_err()
    );
    let mut duplicate = request();
    duplicate.push(Header::new(b":protocol", b"webtransport-h3").unwrap());
    let mut pair = Pair::new(8, 4096);
    assert!(pair.client.send_request(&duplicate, false).is_err());
}

#[test]
fn bidi_prefix_fragmentation_and_reverse_direction_have_no_extra_header() {
    let mut pair = Pair::new(8, 4096);
    assert_eq!(pair.connect(), 0);
    let id = pair.connect();
    assert_eq!(id, 4);
    pair.client.register_local_wt_stream(id, 8).unwrap();
    let bytes = wt_bytes(id, b"native-bidi");
    assert_eq!(&bytes[..3], &[0x40, 0x41, 0x04]);
    for byte in &bytes {
        pair.server.feed_stream(8, &[*byte], false).unwrap();
    }
    let events = pair.server.drain_events().unwrap();
    assert!(
        events.contains(&Event::WebTransport(WebTransportEvent::BidiStreamOpen {
            stream_id: 8,
            session_id: 4
        }))
    );
    let data: Vec<_> = events
        .iter()
        .flat_map(|e| match e {
            Event::WebTransport(WebTransportEvent::BidiStreamData { stream_id: 8, data }) => {
                data.clone()
            }
            _ => Vec::new(),
        })
        .collect();
    assert_eq!(data, b"native-bidi");
    pair.client.feed_stream(8, b"reverse", true).unwrap();
    let reverse = pair.client.drain_events().unwrap();
    assert!(
        reverse.contains(&Event::WebTransport(WebTransportEvent::BidiStreamData {
            stream_id: 8,
            data: b"reverse".to_vec(),
        }))
    );
    assert!(
        reverse.contains(&Event::WebTransport(WebTransportEvent::BidiStreamEnd {
            stream_id: 8
        }))
    );
}

#[test]
fn datagrams_use_quarter_stream_id_and_keep_ua_channel_bytes_unchanged() {
    let mut pair = Pair::new(8, 4096);
    pair.connect();
    pair.connect();
    let payload = [7, 0, 0, 0, 0xaa, 0xbb, 0xcc];
    let datagram = pair.client.send_datagram(4, &payload).unwrap();
    assert_eq!(datagram, [1, 7, 0, 0, 0, 0xaa, 0xbb, 0xcc]);
    pair.server.feed_datagram(&datagram).unwrap();
    assert_eq!(
        pair.server.drain_events().unwrap(),
        vec![Event::WebTransport(WebTransportEvent::Datagram {
            session_id: 4,
            payload: payload.to_vec()
        }),]
    );
    assert!(pair.client.send_datagram(1, &payload).is_err());
    assert!(pair.client.send_datagram(400, &payload).is_err());
    assert!(matches!(
        pair.server.feed_datagram(&[]),
        Err(Error::ConnectionError(ErrorCode::H3DatagramError))
    ));
}

#[test]
fn invalid_and_unrequested_sessions_never_deliver_data() {
    assert!(StreamHeader::new(1).is_err());
    let mut pair = Pair::new(8, 4096);
    pair.connect();
    pair.client
        .feed_stream(1, &wt_bytes(4, b"wrong-session"), false)
        .unwrap();
    let events = pair.client.drain_events().unwrap();
    assert!(events.contains(&Event::WebTransport(
        WebTransportEvent::BufferedStreamRejected {
            stream_id: 1,
            error_code: 0x170d7b68,
        }
    )));
    assert!(!events.iter().any(|e| matches!(
        e,
        Event::WebTransport(WebTransportEvent::BidiStreamData { .. })
    )));
    pair.client.feed_datagram(&[1, 0xaa]).unwrap();
    assert!(pair.client.drain_events().unwrap().is_empty());
    assert!(pair.server.feed_stream(8, &[0x40, 0x41, 1], false).is_err());
}

#[test]
fn unknown_session_stream_and_datagram_buffering_is_bounded() {
    let mut pair = Pair::new(128, 4096);
    for index in 0..101 {
        pair.server
            .feed_stream(4 + index * 4, &wt_bytes(0, &[]), false)
            .unwrap();
    }
    let events = pair.server.drain_events().unwrap();
    assert_eq!(
        events
            .iter()
            .filter(|e| matches!(
                e,
                Event::WebTransport(WebTransportEvent::BufferedStreamRejected {
                    error_code: 0x3994bd84,
                    ..
                })
            ))
            .count(),
        1
    );
    assert!(!events.iter().any(|e| matches!(
        e,
        Event::WebTransport(WebTransportEvent::BidiStreamOpen { .. })
    )));

    let mut pair = Pair::new(8, 4096);
    for value in 0..101 {
        pair.server.feed_datagram(&[0, value]).unwrap();
    }
    assert!(pair.server.drain_events().unwrap().is_empty());
    let id = pair.client.send_request(&request(), false).unwrap();
    pair.pump();
    pair.server.drain_events().unwrap();
    pair.server
        .send_response(id, &[Header::new(b":status", b"200").unwrap()], false)
        .unwrap();
    let events = pair.server.drain_events().unwrap();
    let datagrams: Vec<_> = events
        .into_iter()
        .filter_map(|e| match e {
            Event::WebTransport(WebTransportEvent::Datagram {
                session_id: 0,
                payload,
            }) => Some(payload),
            _ => None,
        })
        .collect();
    assert_eq!(datagrams, (0..100).map(|i| vec![i]).collect::<Vec<_>>());
}

#[test]
fn session_send_flow_control_emits_blocked_and_updates_from_capsules() {
    let mut session = Session::new(4);
    session.set_established();
    *session.remote_limits_mut() = FlowControlLimits {
        max_streams_bidi: 1,
        max_streams_uni: 0,
        max_data: 3,
    };
    assert!(session.try_open_stream(true));
    assert!(!session.try_open_stream(true));
    assert!(!session.try_open_stream(false));
    assert!(session.try_send_data(3));
    assert!(!session.try_send_data(1));
    assert_eq!(
        session.take_pending_capsules(),
        vec![
            Capsule::StreamsBlocked {
                bidirectional: true,
                maximum: 1
            },
            Capsule::StreamsBlocked {
                bidirectional: false,
                maximum: 0
            },
            Capsule::DataBlocked { maximum: 3 },
        ]
    );
    assert!(!session.try_send_data(1));
    assert!(session.take_pending_capsules().is_empty());
    session
        .process_capsule(&Capsule::MaxData { maximum: 4 })
        .unwrap();
    session
        .process_capsule(&Capsule::MaxStreams {
            bidirectional: true,
            maximum: 2,
        })
        .unwrap();
    assert!(session.try_send_data(1));
    assert!(session.try_open_stream(true));
    assert_eq!(session.flow_state().data_sent, 4);
    assert!(
        session
            .process_capsule(&Capsule::MaxData { maximum: 3 })
            .is_err()
    );
}

#[test]
fn receive_data_limit_error_is_session_isolated_and_header_excluded() {
    let mut pair = Pair::new(8, 3);
    pair.connect();
    pair.connect();
    pair.server
        .feed_stream(8, &wt_bytes(0, b"abc"), false)
        .unwrap();
    assert!(
        pair.server
            .drain_events()
            .unwrap()
            .iter()
            .any(|e| matches!(e,
        Event::WebTransport(WebTransportEvent::BidiStreamData { data, .. }) if data == b"abc"))
    );
    pair.server.feed_stream(8, b"d", false).unwrap();
    assert!(pair.server.drain_events().unwrap().iter().any(|e| matches!(
        e,
        Event::WebTransport(WebTransportEvent::SessionClosed {
            session_id: 0,
            error_code: 0x45d4487,
            ..
        })
    )));
    pair.server.feed_datagram(&[1, 0xcc]).unwrap();
    assert_eq!(
        pair.server.drain_events().unwrap(),
        vec![Event::WebTransport(WebTransportEvent::Datagram {
            session_id: 4,
            payload: vec![0xcc]
        }),]
    );
}

#[test]
fn receive_stream_limit_error_and_reset_final_size_charge_session() {
    let mut pair = Pair::new(1, 8);
    pair.connect();
    pair.server
        .feed_stream(4, &wt_bytes(0, b""), false)
        .unwrap();
    pair.server
        .feed_stream(8, &wt_bytes(0, b""), false)
        .unwrap();
    assert!(pair.server.drain_events().unwrap().iter().any(|e| matches!(
        e,
        Event::WebTransport(WebTransportEvent::SessionClosed {
            error_code: 0x45d4487,
            ..
        })
    )));
    let mut pair = Pair::new(8, 3);
    pair.connect();
    pair.server
        .feed_stream(4, &wt_bytes(0, b"a"), false)
        .unwrap();
    pair.server.drain_events().unwrap();
    pair.server.stream_reset(4, 0x52e4a40fa8db + 5, 7).unwrap();
    assert!(pair.server.drain_events().unwrap().iter().any(|e| matches!(
        e,
        Event::WebTransport(WebTransportEvent::SessionClosed {
            error_code: 0x45d4487,
            ..
        })
    )));
}

#[test]
fn data_consumption_returns_max_data_capsule_without_waiting_for_blocked() {
    let mut pair = Pair::new(8, 4);
    pair.connect();
    pair.server
        .feed_stream(4, &wt_bytes(0, b"abc"), false)
        .unwrap();
    pair.server.wt_data_consumed(0, 3);
    assert_eq!(
        pair.server.take_wt_pending_capsules(0),
        vec![Capsule::MaxData { maximum: 7 }]
    );
}

#[test]
fn malformed_capsules_truncation_and_prohibited_capsules_are_rejected() {
    let mut drain = varint(0x78ae);
    drain.extend_from_slice(&[1, 0]);
    for payload in [drain, vec![0x68, 0x43, 4, 0, 0]] {
        let mut pair = Pair::new(8, 4096);
        pair.connect();
        assert!(matches!(
            pair.server.feed_stream(0, &raw_frame(0, &payload), true),
            Err(Error::StreamError(ErrorCode::MessageError))
        ));
    }
    let mut pair = Pair::new(8, 4096);
    pair.connect();
    pair.server
        .feed_stream(
            0,
            &capsule_frame(Capsule::Unknown {
                capsule_type: 0x42,
                payload: vec![1, 2, 3],
            }),
            false,
        )
        .unwrap();
    assert!(pair.server.drain_events().unwrap().is_empty());
    pair.server
        .feed_stream(
            0,
            &capsule_frame(Capsule::Unknown {
                capsule_type: 0x190b4d3e,
                payload: vec![0],
            }),
            false,
        )
        .unwrap();
    assert!(pair.server.drain_events().unwrap().iter().any(|e| matches!(
        e,
        Event::WebTransport(WebTransportEvent::SessionClosed { session_id: 0, .. })
    )));
}

#[test]
fn close_capsule_exact_bytes_fin_and_reset_preserve_other_session() {
    let close = capsule_frame(Capsule::CloseSession {
        error_code: 42,
        message: "bye".into(),
    });
    assert_eq!(
        close,
        vec![0, 10, 0x68, 0x43, 7, 0, 0, 0, 42, b'b', b'y', b'e']
    );
    let mut pair = Pair::new(8, 4096);
    pair.connect();
    pair.connect();
    pair.server
        .feed_stream(8, &wt_bytes(0, b"data"), false)
        .unwrap();
    pair.server.drain_events().unwrap();
    pair.server.feed_stream(0, &close, true).unwrap();
    let events = pair.server.drain_events().unwrap();
    assert!(events.iter().any(|e| matches!(e,
        Event::WebTransport(WebTransportEvent::SessionClosed {
            session_id: 0, close_error_code: 42, close_message, reset_streams, ..
        }) if close_message == "bye" && reset_streams.iter().any(|r| r.stream_id == 8))));
    pair.server.feed_datagram(&[0, 9]).unwrap();
    assert!(pair.server.drain_events().unwrap().is_empty());
    pair.server.feed_datagram(&[1, 8]).unwrap();
    assert_eq!(
        pair.server.drain_events().unwrap(),
        vec![Event::WebTransport(WebTransportEvent::Datagram {
            session_id: 4,
            payload: vec![8]
        }),]
    );
    pair.server.stream_reset(4, 0x10c, 0).unwrap();
    assert!(pair.server.drain_events().unwrap().iter().any(|e| matches!(
        e,
        Event::WebTransport(WebTransportEvent::SessionClosed { session_id: 4, .. })
    )));
}

#[test]
fn close_capsule_forbids_trailing_bytes_and_invalid_utf8() {
    let mut valid_plus_extra = capsule_frame(Capsule::CloseSession {
        error_code: 0,
        message: "".into(),
    });
    valid_plus_extra.extend(capsule_frame(Capsule::DrainSession));
    let malformed_utf8 = raw_frame(0, &[0x68, 0x43, 5, 0, 0, 0, 0, 0xff]);
    for frame in [valid_plus_extra, malformed_utf8] {
        let mut pair = Pair::new(8, 4096);
        pair.connect();
        assert!(matches!(
            pair.server.feed_stream(0, &frame, false),
            Err(Error::StreamError(ErrorCode::MessageError))
        ));
    }
}

#[test]
fn drain_and_goaway_are_observable_and_prevent_new_connect() {
    let mut pair = Pair::new(8, 4096);
    pair.connect();
    pair.server
        .feed_stream(0, &capsule_frame(Capsule::DrainSession), false)
        .unwrap();
    assert!(
        pair.server
            .drain_events()
            .unwrap()
            .contains(&Event::WebTransport(WebTransportEvent::SessionDraining {
                session_id: 0
            }))
    );
    pair.server.send_goaway(VarInt::from_static(4)).unwrap();
    pair.pump();
    assert!(
        pair.client
            .drain_events()
            .unwrap()
            .iter()
            .any(|e| matches!(e, Event::GoawayReceived { id } if id.get() == 4))
    );
    assert!(pair.client.send_request(&request(), false).is_err());
}

#[test]
fn reliable_reset_boundary_and_error_are_not_lost_at_http3_boundary() {
    let mut pair = Pair::new(8, 4096);
    pair.connect();
    pair.client.register_local_wt_stream(0, 4).unwrap();
    pair.server
        .feed_stream(4, &wt_bytes(0, b"x"), false)
        .unwrap();
    pair.server.drain_events().unwrap();
    let code = 0x52e4a40fa8db + 0x1f;
    pair.server.stream_reset(4, code, 4).unwrap();
    assert!(
        pair.server
            .drain_events()
            .unwrap()
            .contains(&Event::WebTransport(WebTransportEvent::StreamReset {
                session_id: 0,
                stream_id: 4,
                error_code: code,
                final_size: 4
            }))
    );
    pair.client
        .feed_stream(
            0,
            &capsule_frame(Capsule::CloseSession {
                error_code: 0,
                message: "".into(),
            }),
            true,
        )
        .unwrap();
    assert!(
        pair.client
            .drain_events()
            .unwrap()
            .iter()
            .any(|e| matches!(e,
        Event::WebTransport(WebTransportEvent::SessionClosed { reset_streams, .. })
            if reset_streams.iter().any(|r| r.stream_id == 4 && r.reliable_size == 3)))
    );
}

fn native_pump(client: &mut Peer, server: &mut Peer) -> Vec<(u32, opcua_wt16_proof::Output)> {
    let mut apps = Vec::new();
    for _ in 0..32 {
        let mut writes = 0;
        for direction in [0, 1] {
            let (source, target) = if direction == 0 {
                (&mut *client, &mut *server)
            } else {
                (&mut *server, &mut *client)
            };
            while let Some(out) = source.pop_output() {
                match out.kind {
                    1 => {
                        target
                            .command(1, out.stream, u64::from(out.flags & 1), 0, &out.data)
                            .unwrap();
                        source
                            .command(
                                20,
                                out.arg,
                                out.data.len() as u64,
                                if out.flags & 1 != 0 { 3 } else { 1 },
                                &[],
                            )
                            .unwrap();
                        writes += 1;
                    }
                    2 => {
                        target.command(2, 0, 0, 0, &out.data).unwrap();
                        source
                            .command(20, out.arg, out.data.len() as u64, 1, &[])
                            .unwrap();
                        writes += 1;
                    }
                    3 | 4 => apps.push((direction, out)),
                    _ => apps.push((direction, out)),
                }
            }
        }
        if writes == 0 {
            return apps;
        }
    }
    panic!("finite native pump did not quiesce");
}

#[test]
fn native_adapter_roundtrip_credit_reset_and_caller_controlled_accept() {
    let mut client = new_peer(facts(0)).unwrap();
    let mut server = new_peer(facts(1)).unwrap();
    native_pump(&mut client, &mut server);
    client.command(10, 0, 0, 0, &[]).unwrap();
    let events = native_pump(&mut client, &mut server);
    assert!(
        events
            .iter()
            .any(|(side, e)| *side == 1 && e.kind == 12 && e.session == 0)
    );
    assert!(!events.iter().any(|(_, e)| e.kind == 13));
    server.command(11, 0, 200, 0, &[]).unwrap();
    let events = native_pump(&mut client, &mut server);
    assert_eq!(
        events
            .iter()
            .filter(|(_, e)| e.kind == 13 && e.flags == 1)
            .count(),
        2
    );
    client.command(12, 0, 4, 0, &[]).unwrap();
    client.command(13, 4, 0, 0, b"request").unwrap();
    client.command(14, 0, 0, 0, b"datagram").unwrap();
    let events = native_pump(&mut client, &mut server);
    assert!(
        events
            .iter()
            .any(|(side, e)| *side == 1 && e.kind == 15 && e.data == b"request")
    );
    assert!(
        events
            .iter()
            .any(|(side, e)| *side == 1 && e.kind == 17 && e.data == b"datagram")
    );
    server.command(13, 4, 0, 0, b"response").unwrap();
    let events = native_pump(&mut client, &mut server);
    assert!(
        events
            .iter()
            .any(|(side, e)| *side == 0 && e.kind == 15 && e.data == b"response")
    );
    assert!(server.command(18, 0, 8, 0, &[]).is_err());
    server.command(18, 0, 7, 0, &[]).unwrap();
    client.command(17, 4, 30, 0, &[]).unwrap();
    let reset = client.pop_output().unwrap();
    assert_eq!((reset.kind, reset.stream, reset.aux), (3, 4, 3));
    assert_eq!(reset.arg, 0x52e4a40fa8db + 31);
    assert!(client.command(13, 4, 0, 0, b"late").is_err());
}

#[test]
fn native_adapter_rejects_disallowed_origin_before_200() {
    let mut client = ClientConnection::new(strict_settings(Role::Client, 8, 4096));
    let mut server = new_peer(facts(1)).unwrap();
    client
        .set_webtransport_transport_verified(true, true)
        .unwrap();
    let init = client.init_h3_streams(2, 6, 10).unwrap();
    server.command(1, 2, 0, 0, &init.control_data).unwrap();
    while let Some(out) = server.pop_output() {
        if out.kind == 1 {
            client.feed_stream(out.stream, &out.data, false).unwrap();
        }
    }

    let headers = ConnectRequest::new("https", "localhost", "/proof")
        .origin("https://untrusted.invalid")
        .to_headers()
        .unwrap();
    let id = client.send_request(&headers, false).unwrap();
    let (bytes, _) = client.take_stream_data(id).unwrap();
    server.command(1, id, 0, 0, &bytes).unwrap();
    assert_eq!(
        server.command(11, id, 200, 0, &[]).unwrap_err().code,
        Code::OriginDenied
    );
    server.command(11, id, 403, 0, &[]).unwrap();
    while let Some(out) = server.pop_output() {
        assert_ne!(out.kind, 13);
    }
}

#[test]
fn native_adapter_rejects_non_https_connect_before_200() {
    let mut client = ClientConnection::new(strict_settings(Role::Client, 8, 4096));
    let mut server = new_peer(facts(1)).unwrap();
    client
        .set_webtransport_transport_verified(true, true)
        .unwrap();
    let init = client.init_h3_streams(2, 6, 10).unwrap();
    server.command(1, 2, 0, 0, &init.control_data).unwrap();
    while let Some(out) = server.pop_output() {
        if out.kind == 1 {
            client.feed_stream(out.stream, &out.data, false).unwrap();
        }
    }
    let headers = ConnectRequest::new("http", "localhost", "/proof")
        .to_headers()
        .unwrap();
    let id = client.send_request(&headers, false).unwrap();
    let (bytes, _) = client.take_stream_data(id).unwrap();
    let error = server.command(1, id, 0, 0, &bytes).unwrap_err();
    assert_eq!(error.protocol_code, ErrorCode::MessageError.code());
    assert!(server.command(11, id, 200, 0, &[]).is_err());
    while let Some(out) = server.pop_output() {
        assert_ne!(out.kind, 13);
    }
}

#[test]
fn malformed_raw_headers_are_not_accepted_as_session() {
    let mut pair = Pair::new(8, 4096);
    let headers = vec![
        Header::new(b":method", b"CONNECT").unwrap(),
        Header::new(b":protocol", b"webtransport").unwrap(),
        Header::new(b":scheme", b"https").unwrap(),
        Header::new(b":authority", b"localhost").unwrap(),
        Header::new(b":path", b"/proof").unwrap(),
    ];
    let mut encoder = QpackEncoder::new();
    let mut encoded = vec![0; 1024];
    let len = encoder.encode(&mut encoded, &headers, 0).unwrap();
    let result = pair
        .server
        .feed_stream(0, &raw_frame(1, &encoded[..len]), false);
    assert!(result.is_err());
}

#[test]
fn native_adapter_rejects_id_reuse_without_spending_session_credit() {
    let mut f = facts(0);
    f.initial_max_bidi = 1;
    let mut client = new_peer(f).unwrap();
    f.role = 1;
    let mut server = new_peer(f).unwrap();
    native_pump(&mut client, &mut server);
    client.command(10, 0, 0, 0, &[]).unwrap();
    assert!(client.command(10, 0, 0, 0, &[]).is_err());
    native_pump(&mut client, &mut server);
    server.command(11, 0, 200, 0, &[]).unwrap();
    native_pump(&mut client, &mut server);
    assert!(client.command(12, 0, 0, 0, &[]).is_err());
    client.command(12, 0, 4, 0, &[]).unwrap();
    assert!(client.command(12, 0, 4, 0, &[]).is_err());
    assert_eq!(
        client.command(12, 0, 8, 0, &[]).unwrap_err().code,
        Code::FlowControlBlocked
    );
}

#[test]
fn association_and_quarter_stream_id_varint_boundaries_are_exact() {
    for (session, expected) in [
        (0, vec![0x40, 0x41, 0]),
        (4, vec![0x40, 0x41, 4]),
        (252, vec![0x40, 0x41, 0x40, 0xfc]),
        (16384, vec![0x40, 0x41, 0x80, 0, 0x40, 0]),
    ] {
        assert_eq!(wt_bytes(session, &[]), expected);
    }
}

fn fill_incomplete_capsule(pair: &mut Pair, session: u64, buffered: usize) {
    let mut prefix = varint(0x42);
    prefix.extend(varint(256 * 1024));
    assert!(buffered >= prefix.len());
    pair.server
        .feed_stream(session, &raw_frame(0, &prefix), false)
        .unwrap();
    let mut remaining = buffered - prefix.len();
    while remaining != 0 {
        let count = remaining.min(1024);
        pair.server
            .feed_stream(session, &raw_frame(0, &vec![0; count]), false)
            .unwrap();
        remaining -= count;
    }
}

#[test]
fn established_capsule_budget_rejects_next_byte_and_isolates_session() {
    let mut pair = Pair::new(8, 4096);
    pair.connect();
    pair.connect();
    fill_incomplete_capsule(&mut pair, 0, 64 * 1024);
    assert!(matches!(
        pair.server.feed_stream(0, &raw_frame(0, &[0]), false),
        Err(Error::StreamError(ErrorCode::ExcessiveLoad))
    ));
    pair.server.drain_events().unwrap();
    pair.server.feed_datagram(&[1, 0xaa]).unwrap();
    assert_eq!(
        pair.server.drain_events().unwrap(),
        vec![Event::WebTransport(WebTransportEvent::Datagram {
            session_id: 4,
            payload: vec![0xaa]
        }),]
    );
}

#[test]
fn reset_confirmation_reclaims_only_untransmitted_payload_credit() {
    let mut f = facts(0);
    f.initial_max_data = 4;
    let mut client = new_peer(f).unwrap();
    f.role = 1;
    let mut server = new_peer(f).unwrap();
    native_pump(&mut client, &mut server);
    client.command(10, 0, 0, 0, &[]).unwrap();
    native_pump(&mut client, &mut server);
    server.command(11, 0, 200, 0, &[]).unwrap();
    native_pump(&mut client, &mut server);
    client.command(12, 0, 4, 0, &[]).unwrap();
    client.command(13, 4, 0, 0, b"abcd").unwrap();
    client.command(17, 4, 30, 0, &[]).unwrap();
    let header = client.pop_output().unwrap();
    assert_eq!(header.data, [0x40, 0x41, 0]);
    client.command(20, header.arg, 3, 1, &[]).unwrap();
    let reset = client.pop_output().unwrap();
    assert_eq!(reset.kind, 3);
    client.command(22, 4, 3, 3, &[]).unwrap();
    client.command(12, 0, 8, 0, &[]).unwrap();
    client.command(13, 8, 0, 0, b"abcd").unwrap();
}
#[test]
fn datagram_quarter_id_boundary_encoding_is_exact() {
    use shiguredo_http3::webtransport::Datagram;
    for (session, prefix) in [(252, vec![0x3f]), (256, vec![0x40, 0x40])] {
        let datagram = Datagram::new(session, vec![0xaa]).unwrap();
        let mut encoded = Vec::new();
        datagram.encode(&mut encoded);
        let mut expected = prefix;
        expected.push(0xaa);
        assert_eq!(encoded, expected);
        assert_eq!(Datagram::decode(&encoded).unwrap().0.session_id, session);
    }
}

#[test]
fn capsule_aggregate_capacity_is_bounded_and_released_on_reset_and_fin() {
    let mut pair = Pair::new(8, 4096);
    for _ in 0..3 {
        pair.connect();
    }
    fill_incomplete_capsule(&mut pair, 0, 64 * 1024);
    fill_incomplete_capsule(&mut pair, 4, 32 * 1024);
    fill_incomplete_capsule(&mut pair, 8, 32 * 1024);
    assert_eq!(pair.server.wt_buffered_capsule_bytes(), 128 * 1024);
    assert!(matches!(
        pair.server.feed_stream(8, &raw_frame(0, &[0]), false),
        Err(Error::StreamError(ErrorCode::ExcessiveLoad))
    ));
    assert_eq!(pair.server.wt_buffered_capsule_bytes(), 96 * 1024);
    pair.server.stream_reset(0, 0x10c, 0).unwrap();
    assert_eq!(pair.server.wt_buffered_capsule_bytes(), 32 * 1024);
    assert!(matches!(
        pair.server.feed_stream(4, &[], true),
        Err(Error::StreamError(ErrorCode::MessageError))
    ));
    assert_eq!(pair.server.wt_buffered_capsule_bytes(), 0);
    assert_eq!(pair.server.buffered_request_bytes(), 0);
}

#[test]
fn incomplete_http_data_frame_cannot_bypass_capsule_bounds() {
    let mut pair = Pair::new(8, 4096);
    pair.connect();
    pair.connect();
    let mut wire = varint(0);
    wire.extend(varint(1 << 30));
    pair.server.feed_stream(0, &wire, false).unwrap();
    let mut remaining = 64 * 1024 - wire.len();
    while remaining != 0 {
        let len = remaining.min(1024);
        pair.server.feed_stream(0, &vec![0; len], false).unwrap();
        remaining -= len;
    }
    assert_eq!(pair.server.buffered_request_bytes(), 64 * 1024);
    assert_eq!(pair.server.wt_buffered_capsule_bytes(), 0);
    assert!(matches!(
        pair.server.feed_stream(0, &[0], false),
        Err(Error::StreamError(ErrorCode::ExcessiveLoad))
    ));
    assert_eq!(pair.server.buffered_request_bytes(), 0);
    pair.server.drain_events().unwrap();
    pair.server.feed_datagram(&[1, 0x55]).unwrap();
    assert_eq!(
        pair.server.drain_events().unwrap(),
        vec![Event::WebTransport(WebTransportEvent::Datagram {
            session_id: 4,
            payload: vec![0x55]
        }),]
    );
}

#[test]
fn completed_unknown_capsules_release_capacity_and_known_capsules_still_validate() {
    let mut pair = Pair::new(8, 4096);
    pair.connect();
    let mut capsule = varint(0x42);
    capsule.extend(varint(4096));
    capsule.extend(vec![0; 4096]);
    for _ in 0..4 {
        for chunk in capsule.chunks(17) {
            pair.server
                .feed_stream(0, &raw_frame(0, chunk), false)
                .unwrap();
        }
        assert_eq!(pair.server.wt_buffered_capsule_bytes(), 0);
        assert_eq!(pair.server.buffered_request_bytes(), 0);
        assert!(pair.server.drain_events().unwrap().is_empty());
    }
    let mut small = Vec::new();
    Capsule::DataBlocked { maximum: 4096 }.encode(&mut small);
    pair.server
        .feed_stream(0, &raw_frame(0, &small.repeat(500)), false)
        .unwrap();
    assert_eq!(pair.server.drain_events().unwrap().len(), 500);
    assert_eq!(pair.server.wt_buffered_capsule_bytes(), 0);
    let malformed = raw_frame(0, &[0x80, 0, 0x78, 0xae, 1, 0]);
    assert!(matches!(
        pair.server.feed_stream(0, &malformed, false),
        Err(Error::StreamError(ErrorCode::MessageError))
    ));
    assert_eq!(pair.server.wt_buffered_capsule_bytes(), 0);
}

#[test]
fn pending_connect_capsules_use_the_same_aggregate_budget() {
    let mut pair = Pair::new(8, 4096);
    for id in [0, 4, 8] {
        assert_eq!(pair.client.send_request(&request(), false).unwrap(), id);
        pair.pump();
        pair.server.drain_events().unwrap();
    }
    fill_incomplete_capsule(&mut pair, 0, 64 * 1024);
    fill_incomplete_capsule(&mut pair, 4, 64 * 1024);
    assert_eq!(pair.server.wt_buffered_capsule_bytes(), 128 * 1024);
    assert!(matches!(
        pair.server.feed_stream(8, &raw_frame(0, &[0]), false),
        Err(Error::StreamError(ErrorCode::ExcessiveLoad))
    ));
    assert_eq!(pair.server.wt_buffered_capsule_bytes(), 128 * 1024);
    pair.server.stream_reset(0, 0x10c, 0).unwrap();
    pair.server.stream_reset(4, 0x10c, 0).unwrap();
    assert_eq!(pair.server.wt_buffered_capsule_bytes(), 0);
}

fn established_native(max_data: u64) -> (Peer, Peer) {
    let mut f = facts(0);
    f.initial_max_data = max_data;
    let mut client = new_peer(f).unwrap();
    f.role = 1;
    let mut server = new_peer(f).unwrap();
    native_pump(&mut client, &mut server);
    client.command(10, 0, 0, 0, &[]).unwrap();
    native_pump(&mut client, &mut server);
    server.command(11, 0, 200, 0, &[]).unwrap();
    native_pump(&mut client, &mut server);
    (client, server)
}

fn open_and_accept_prefix(client: &mut Peer, stream: u64) {
    client.command(12, 0, stream, 0, &[]).unwrap();
    let prefix = client.pop_output().unwrap();
    assert_eq!(prefix.kind, 1);
    assert_eq!(prefix.flags, 2);
    assert_eq!(prefix.aux, 0);
    assert_eq!(prefix.data, [0x40, 0x41, 0]);
    client.command(20, prefix.arg, 3, 1, &[]).unwrap();
}

#[test]
fn partial_send_ack_is_monotonic_and_does_not_release_native_buffer_early() {
    let (mut client, _) = established_native(8);
    client.command(12, 0, 4, 0, &[]).unwrap();
    let prefix = client.pop_output().unwrap();
    client.command(13, 4, 0, 0, b"abcd").unwrap();
    client.command(20, prefix.arg, 1, 0, &[]).unwrap();
    assert_eq!(client.resource_snapshot().leased_bytes, 3);
    assert!(client.output().is_none());
    let before = client.resource_snapshot();
    assert!(client.command(20, prefix.arg, 0, 0, &[]).is_err());
    assert!(client.command(20, prefix.arg, 4, 1, &[]).is_err());
    assert_eq!(client.resource_snapshot(), before);
    client.command(20, prefix.arg, 3, 1, &[]).unwrap();
    let body = client.pop_output().unwrap();
    assert_eq!(body.aux, 3);
    client.command(20, body.arg, 2, 0, &[]).unwrap();
    assert_eq!(client.resource_snapshot().leased_bytes, 4);
    assert_eq!(client.resource_snapshot().reserved_payload, 4);
    client.command(20, body.arg, 2, 5, &[]).unwrap();
    assert_eq!(client.resource_snapshot().leased_bytes, 0);
    assert!(client.command(13, 4, 0, 0, b"late").is_err());
    client.command(17, 4, 0, 0, &[]).unwrap();
    assert_eq!(client.pop_output().unwrap().kind, 3);
    assert!(client.command(22, 4, 6, 3, &[]).is_err());
    assert_eq!(client.resource_snapshot().reserved_payload, 4);
    client.command(22, 4, 5, 3, &[]).unwrap();
    assert_eq!(client.resource_snapshot().reserved_payload, 2);
}

#[test]
fn reset_waits_for_native_buffer_release_but_cancel_action_does_not_deadlock() {
    let (mut client, _) = established_native(4);
    open_and_accept_prefix(&mut client, 4);
    client.command(13, 4, 0, 0, b"abcd").unwrap();
    let body = client.pop_output().unwrap();
    client.command(20, body.arg, 2, 0, &[]).unwrap();
    client.command(17, 4, 0, 0, &[]).unwrap();
    let reset = client.pop_output().unwrap();
    assert_eq!((reset.kind, reset.aux), (3, 3));
    assert_eq!(
        client.command(22, 4, 5, 3, &[]).unwrap_err().code,
        Code::InvalidResetEvidence
    );
    assert_eq!(client.resource_snapshot().reserved_payload, 4);
    assert_eq!(client.resource_snapshot().leased_bytes, 4);
    client.command(20, body.arg, 2, 5, &[]).unwrap();
    client.command(22, 4, 5, 3, &[]).unwrap();
    assert_eq!(client.resource_snapshot().reserved_payload, 2);
    assert_eq!(client.resource_snapshot().leased_bytes, 0);
}

#[test]
fn authoritative_reset_final_may_be_smaller_than_accepted_and_is_idempotent() {
    let (mut client, _) = established_native(4);
    open_and_accept_prefix(&mut client, 4);
    client.command(13, 4, 0, 0, b"abcd").unwrap();
    let body = client.pop_output().unwrap();
    client.command(20, body.arg, 4, 1, &[]).unwrap();
    client.command(17, 4, 0, 0, &[]).unwrap();
    assert!(client.command(22, 4, 3, 3, &[]).is_err());
    assert_eq!(client.resource_snapshot().reserved_payload, 4);
    assert_eq!(client.pop_output().unwrap().kind, 3);
    assert!(client.command(22, 4, 2, 2, &[]).is_err());
    assert!(client.command(22, 4, 8, 3, &[]).is_err());
    client.command(22, 4, 3, 3, &[]).unwrap();
    client.command(22, 4, 3, 3, &[]).unwrap();
    client.command(20, body.arg, 4, 1, &[]).unwrap();
    assert_eq!(client.resource_snapshot().reserved_payload, 0);
    assert!(client.command(22, 4, 4, 3, &[]).is_err());
    assert_eq!(client.resource_snapshot().reserved_payload, 0);
    client.command(12, 0, 8, 0, &[]).unwrap();
    client.command(13, 8, 0, 0, b"abcd").unwrap();
}

#[test]
fn partial_reset_final_is_checked_against_cumulative_offset_not_last_write_count() {
    let (mut client, _) = established_native(12);
    open_and_accept_prefix(&mut client, 4);
    client.command(13, 4, 0, 0, b"abcd").unwrap();
    let first = client.pop_output().unwrap();
    client.command(20, first.arg, 4, 1, &[]).unwrap();
    client.command(13, 4, 0, 0, b"12345678").unwrap();
    let second = client.pop_output().unwrap();
    assert_eq!(second.aux, 7);
    client.command(20, second.arg, 2, 5, &[]).unwrap();
    client.command(17, 4, 0, 0, &[]).unwrap();
    assert_eq!(client.pop_output().unwrap().kind, 3);
    client.command(22, 4, 9, 3, &[]).unwrap();
    assert_eq!(client.resource_snapshot().reserved_payload, 6);
    client.command(12, 0, 8, 0, &[]).unwrap();
    client.command(13, 8, 0, 0, b"abcdef").unwrap();
    assert_eq!(client.resource_snapshot().reserved_payload, 12);
    assert!(client.command(13, 8, 0, 0, b"x").is_err());
}

#[test]
fn fin_requires_explicit_transport_confirmation_and_queued_fin_can_be_cancelled() {
    let (mut client, _) = established_native(4);
    open_and_accept_prefix(&mut client, 4);
    client.command(13, 4, 1, 0, b"abcd").unwrap();
    client.command(17, 4, 0, 0, &[]).unwrap();
    assert_eq!(client.pop_output().unwrap().kind, 3);
    client.command(22, 4, 3, 3, &[]).unwrap();
    assert_eq!(client.resource_snapshot().reserved_payload, 0);

    open_and_accept_prefix(&mut client, 8);
    client.command(13, 8, 1, 0, &[]).unwrap();
    let fin = client.pop_output().unwrap();
    assert_eq!((fin.flags, fin.data.len()), (1, 0));
    client.command(20, fin.arg, 0, 3, &[]).unwrap();
    assert!(client.command(17, 8, 0, 0, &[]).is_err());

    open_and_accept_prefix(&mut client, 12);
    client.command(13, 12, 1, 0, &[]).unwrap();
    let rejected_fin = client.pop_output().unwrap();
    client.command(20, rejected_fin.arg, 0, 5, &[]).unwrap();
    client.command(17, 12, 0, 0, &[]).unwrap();
    assert_eq!(client.pop_output().unwrap().kind, 3);
}

#[test]
fn aggregate_send_budget_counts_queued_and_leased_buffers_until_completion() {
    use opcua_wt16_proof::MAX_OUTPUT_BYTES;
    let (mut client, _) = established_native(16 * 1024 * 1024);
    open_and_accept_prefix(&mut client, 4);
    client
        .command(13, 4, 0, 0, &vec![0x55; MAX_OUTPUT_BYTES])
        .unwrap();
    let before = client.resource_snapshot();
    assert_eq!(before.queued_bytes, MAX_OUTPUT_BYTES as u64);
    assert!(client.command(13, 4, 0, 0, &[0]).is_err());
    assert_eq!(client.resource_snapshot(), before);
    let body = client.pop_output().unwrap();
    assert_eq!(client.resource_snapshot().queued_bytes, 0);
    assert_eq!(
        client.resource_snapshot().leased_bytes,
        MAX_OUTPUT_BYTES as u64
    );
    assert!(client.command(13, 4, 0, 0, &[0]).is_err());
    client.command(20, body.arg, 1, 0, &[]).unwrap();
    assert_eq!(
        client.resource_snapshot().leased_bytes,
        MAX_OUTPUT_BYTES as u64
    );
    client
        .command(20, body.arg, MAX_OUTPUT_BYTES as u64, 1, &[])
        .unwrap();
    assert_eq!(client.resource_snapshot().leased_bytes, 0);
    assert_eq!(
        client.resource_snapshot().reserved_payload,
        MAX_OUTPUT_BYTES as u64
    );
    client.command(13, 4, 0, 0, &[0]).unwrap();
}

#[test]
fn zero_length_sends_have_a_separate_bounded_descriptor_budget() {
    use opcua_wt16_proof::MAX_PENDING_SENDS;
    let (mut client, _) = established_native(4);
    open_and_accept_prefix(&mut client, 4);
    for _ in 0..MAX_PENDING_SENDS {
        client.command(13, 4, 0, 0, &[]).unwrap();
    }
    assert_eq!(
        client.resource_snapshot().pending_sends,
        MAX_PENDING_SENDS as u64
    );
    assert!(client.command(13, 4, 0, 0, &[]).is_err());
    let mut oldest = 0;
    for i in 0..MAX_PENDING_SENDS {
        let output = client.pop_output().unwrap();
        if i == 0 {
            oldest = output.arg;
        }
        client.command(20, output.arg, 0, 1, &[]).unwrap();
    }
    assert_eq!(client.resource_snapshot().pending_sends, 0);
    assert!(client.command(20, oldest, 0, 1, &[]).is_err());
}

#[test]
fn datagram_partial_completion_is_rejected_without_freeing_its_lease() {
    let (mut client, _) = established_native(4);
    client.command(14, 0, 0, 0, &[1, 2, 3]).unwrap();
    let datagram = client.pop_output().unwrap();
    assert_eq!(datagram.data, [0, 1, 2, 3]);
    let before = client.resource_snapshot();
    assert!(client.command(20, datagram.arg, 2, 1, &[]).is_err());
    assert_eq!(client.resource_snapshot(), before);
    client.command(20, datagram.arg, 0, 5, &[]).unwrap();
    assert_eq!(client.resource_snapshot().leased_bytes, 0);
    assert_eq!(client.resource_snapshot().reserved_payload, 0);
}

#[test]
fn incomplete_reliable_prefix_failure_aborts_without_downgraded_reset() {
    let (mut client, _) = established_native(4);
    client.command(12, 0, 4, 0, &[]).unwrap();
    let prefix = client.pop_output().unwrap();
    let error = client.command(20, prefix.arg, 2, 5, &[]).unwrap_err();
    assert_eq!(
        (error.code, error.scope),
        (Code::SendFailed, Scope::Connection)
    );
    assert_eq!(client.resource_snapshot().leased_bytes, 0);
    assert!(client.command(17, 4, 0, 0, &[]).is_err());
    assert!(client.output().is_none());
}
