// Copyright (c) OPC Foundation. Licensed under the MIT License.
//! Generic caller-policy, resource and state-boundary tests. No real transport is impersonated.

use opcua_http3::config::{Config, Limits, Policy, Utf8};
use opcua_http3::error::{Code, Scope};
use opcua_http3::{Peer, strict_settings};
use shiguredo_http3::webtransport::ConnectRequest;
use shiguredo_http3::{ClientConnection, ErrorCode, Header, Role, ServerConnection};

pub mod support;
use support::{configuration, established, facts, peer, pump};

fn configuration_error(config: &Config) -> opcua_http3::error::Failure {
    unsafe { Policy::from_config(config, &facts(0)) }.unwrap_err()
}

#[test]
fn every_configuration_text_field_rejects_invalid_partitions() {
    for value in [
        "",
        "UPPER.example",
        "user@plant.example",
        "host/path",
        "a..b",
        "-host",
        "host-",
        "host:0",
        "host:65536",
        "host:0443",
        "[broken]",
        "[::1]/x",
        "host\r\nx",
        "café.example",
    ] {
        let mut config = configuration(0);
        config.authority = Utf8::borrowed(value);
        assert_eq!(
            configuration_error(&config).code,
            Code::InvalidAuthority,
            "{value:?}"
        );
    }
    for value in [
        "",
        "relative",
        "//host/path",
        "/x#fragment",
        "/x y",
        "/x\\y",
        "/%z0",
        "/%0",
        "/x\0",
    ] {
        let mut config = configuration(0);
        config.path = Utf8::borrowed(value);
        assert_eq!(
            configuration_error(&config).code,
            Code::InvalidPath,
            "{value:?}"
        );
    }
    for value in [
        "",
        "http://plant.example",
        "https://plant.example/path",
        "null",
        "https://a b",
        "https://user@host",
    ] {
        let mut config = configuration(0);
        config.origin_policy = 2;
        config.origin = Utf8::borrowed(value);
        assert_eq!(
            configuration_error(&config).code,
            Code::InvalidOrigin,
            "{value:?}"
        );
    }
    for value in [
        "",
        "quoted\"value",
        "white space",
        "opcua\\1",
        "opcua;v=1",
        "\0",
        "prøtocol",
    ] {
        let mut config = configuration(0);
        config.application_protocol = Utf8::borrowed(value);
        assert_eq!(
            configuration_error(&config).code,
            Code::InvalidProtocol,
            "{value:?}"
        );
    }
}

#[test]
fn oversized_configuration_views_are_rejected_before_dereference() {
    for (index, maximum, expected) in [
        (0, 255, Code::InvalidAuthority),
        (1, 2048, Code::InvalidPath),
        (2, 263, Code::InvalidOrigin),
        (3, 128, Code::InvalidProtocol),
    ] {
        let mut config = configuration(0);
        let value = Utf8 {
            data: std::ptr::dangling(),
            length: maximum + 1,
        };
        match index {
            0 => config.authority = value,
            1 => config.path = value,
            2 => config.origin = value,
            _ => config.application_protocol = value,
        }
        assert_eq!(configuration_error(&config).code, expected);
    }
    let mut config = configuration(0);
    config.authority = Utf8 {
        data: std::ptr::null(),
        length: 1,
    };
    assert_eq!(configuration_error(&config).code, Code::InvalidAuthority);
    let invalid = [0xff];
    config.authority = Utf8 {
        data: invalid.as_ptr(),
        length: 1,
    };
    assert_eq!(configuration_error(&config).code, Code::InvalidAuthority);
}

#[test]
fn configuration_versions_roles_sessions_origins_and_allocator_ids_fail_closed() {
    for (index, expected) in [
        (0, Code::InvalidAbi),
        (1, Code::InvalidAbi),
        (2, Code::InvalidOrigin),
        (3, Code::SessionLimit),
        (4, Code::SessionLimit),
        (5, Code::InvalidTransportEvidence),
        (6, Code::InvalidTransportEvidence),
        (7, Code::InvalidStreamId),
        (8, Code::InvalidStreamId),
        (9, Code::InvalidStreamId),
        (10, Code::InvalidStreamId),
        (11, Code::InvalidOrigin),
    ] {
        let mut config = configuration(0);
        match index {
            0 => config.abi_version = 3,
            1 => config.struct_size -= 1,
            2 => config.origin_policy = 3,
            3 => config.maximum_sessions = 0,
            4 => config.maximum_sessions = 2,
            5 => config.connection_tag = 0,
            6 => config.application_pair_tag = 0,
            7 => config.connect_stream = 4,
            8 => config.control_stream = 3,
            9 => config.encoder_stream = config.decoder_stream,
            10 => config.decoder_stream = u64::MAX,
            _ => config.origin = Utf8::borrowed("https://plant.example"),
        }
        let error = configuration_error(&config);
        assert_eq!(
            (error.scope, error.code),
            (Scope::Configuration, expected),
            "partition {index}"
        );
    }
}

fn limit_slot(limits: &mut Limits, index: usize) -> &mut u64 {
    match index {
        0 => &mut limits.input_bytes,
        1 => &mut limits.output_bytes,
        2 => &mut limits.pending_sends,
        3 => &mut limits.output_events,
        4 => &mut limits.lifetime_streams,
        5 => &mut limits.header_count,
        6 => &mut limits.header_bytes,
        7 => &mut limits.control_bytes,
        8 => &mut limits.request_bytes,
        9 => &mut limits.capsule_bytes,
        10 => &mut limits.pending_bytes,
        _ => &mut limits.receipts,
    }
}

#[test]
fn every_local_limit_has_exact_lower_and_upper_boundaries() {
    for (index, minimum, maximum) in [
        (0, 16, 1048576),
        (1, 4096, 1048576),
        (2, 8, 512),
        (3, 64, 4096),
        (4, 7, 4096),
        (5, 8, 128),
        (6, 1024, 16384),
        (7, 128, 65536),
        (8, 1024, 65536),
        (9, 1024, 65536),
        (10, 1024, 131072),
        (11, 1, 256),
    ] {
        for (value, valid) in [
            (minimum - 1, false),
            (minimum, true),
            (maximum, true),
            (maximum + 1, false),
        ] {
            let mut limits = Limits {
                header_bytes: 1024,
                ..Limits::default()
            };
            *limit_slot(&mut limits, index) = value;
            assert_eq!(limits.validate().is_ok(), valid, "limit {index}={value}");
        }
    }
    let limits = Limits {
        header_bytes: 8192,
        request_bytes: 4096,
        ..Limits::default()
    };
    assert_eq!(limits.validate().unwrap_err().code, Code::InvalidLimits);
    let limits = Limits {
        header_bytes: 8192,
        output_bytes: 4096,
        ..Limits::default()
    };
    assert_eq!(limits.validate().unwrap_err().code, Code::InvalidLimits);
    let limits = Limits {
        header_bytes: 4096,
        output_events: 64,
        ..Limits::default()
    };
    assert_eq!(limits.validate().unwrap_err().code, Code::InvalidLimits);
}

#[test]
fn alternate_ipv6_resource_origin_and_protocol_are_used_on_the_wire() {
    let (mut client, mut server) = established(
        |config| {
            config.authority = Utf8::borrowed("[2001:db8::1]:9443");
            config.path = Utf8::borrowed("/factory/line%202?mode=flow");
            config.origin_policy = 2;
            config.origin = Utf8::borrowed("https://controller.example:9443");
            config.application_protocol = Utf8::borrowed("plant-data/2");
            config.control_stream += 12;
            config.encoder_stream += 12;
            config.decoder_stream += 12;
        },
        4096,
    );
    client.command(12, 0, 4, 0, &[]).unwrap();
    client.command(13, 4, 1, 0, b"opaque UA message").unwrap();
    let events = pump(&mut client, &mut server);
    assert!(
        events.iter().any(|(side, event)| *side == 1
            && event.kind == 15
            && event.data == b"opaque UA message")
    );
    assert!(
        events
            .iter()
            .any(|(side, event)| *side == 1 && event.kind == 16 && event.stream == 4)
    );
    server.command(13, 4, 1, 0, b"reverse after FIN").unwrap();
    let events = pump(&mut client, &mut server);
    assert!(
        events.iter().any(|(side, event)| *side == 0
            && event.kind == 15
            && event.data == b"reverse after FIN")
    );
}

fn pending(client_config: Config, server_config: Config) -> (Peer, Peer) {
    let mut client = peer(&client_config, facts(0));
    let mut server = peer(&server_config, facts(1));
    pump(&mut client, &mut server);
    client.command(10, 0, 0, 0, &[]).unwrap();
    pump(&mut client, &mut server);
    (client, server)
}

#[test]
fn exact_authority_path_and_application_protocol_denials_allow_explicit_rejection() {
    for (index, expected) in [
        (0, Code::AuthorityDenied),
        (1, Code::PathDenied),
        (2, Code::ApplicationProtocolDenied),
    ] {
        let mut config = configuration(0);
        match index {
            0 => config.authority = Utf8::borrowed("another.example"),
            1 => config.path = Utf8::borrowed("/other"),
            _ => config.application_protocol = Utf8::borrowed("different/1"),
        }
        let (mut client, mut server) = pending(config, configuration(1));
        let before = server.resource_snapshot();
        let error = server.command(11, 0, 200, 0, &[]).unwrap_err();
        assert_eq!(
            (error.scope, error.code, error.session),
            (Scope::Session, expected, 0)
        );
        assert_eq!(server.resource_snapshot(), before);
        server.command(11, 0, 403, 0, &[]).unwrap();
        let events = pump(&mut client, &mut server);
        assert!(
            events
                .iter()
                .any(|(side, event)| *side == 0 && event.kind == 23 && event.arg == 403)
        );
        assert!(!events.iter().any(|(_, event)| event.kind == 13));
        assert_eq!(
            client.command(10, 0, 0, 0, &[]).unwrap_err().code,
            Code::SessionLimit
        );
    }
}

#[test]
fn origin_policy_partitions_are_enforced_before_success() {
    for (mode, client_origin, permitted) in [
        (0, None, true),
        (0, Some("https://controller.example"), false),
        (1, None, true),
        (1, Some("https://controller.example"), true),
        (1, Some("https://other.example"), false),
        (2, None, false),
        (2, Some("https://controller.example"), true),
        (2, Some("https://other.example"), false),
    ] {
        let mut client_config = configuration(0);
        if let Some(origin) = client_origin {
            client_config.origin_policy = 2;
            client_config.origin = Utf8::borrowed(origin);
        }
        let mut server_config = configuration(1);
        server_config.origin_policy = mode;
        if mode != 0 {
            server_config.origin = Utf8::borrowed("https://controller.example");
        }
        let (mut client, mut server) = pending(client_config, server_config);
        let result = server.command(11, 0, 200, 0, &[]);
        if permitted {
            result.unwrap();
            assert_eq!(
                pump(&mut client, &mut server)
                    .iter()
                    .filter(|(_, event)| event.kind == 13)
                    .count(),
                2
            );
        } else {
            assert_eq!(result.unwrap_err().code, Code::OriginDenied);
            assert!(server.session_state().is_none());
        }
    }
}

#[test]
fn caller_strings_and_tags_are_copied_at_creation_and_cannot_rebind_a_connection() {
    let mut authority = String::from("plant.example:8443");
    let mut config = configuration(0);
    config.authority = Utf8::borrowed(&authority);
    let mut client = peer(&config, facts(0));
    authority.clear();
    authority.push_str("malicious.example");
    config.connection_tag = 999;
    config.application_pair_tag = 999;
    let mut server = peer(&configuration(1), facts(1));
    assert_eq!(
        (config.connection_tag, config.application_pair_tag),
        (999, 999)
    );
    assert_eq!(client.policy().connection_tag(), 17);
    assert_eq!(client.policy().application_pair_tag(), 19);
    pump(&mut client, &mut server);
    client.command(10, 0, 0, 0, &[]).unwrap();
    pump(&mut client, &mut server);
    server.command(11, 0, 200, 0, &[]).unwrap();
    assert_eq!(
        pump(&mut client, &mut server)
            .iter()
            .filter(|(_, event)| event.kind == 13)
            .count(),
        2
    );
}

fn raw_request(mut headers: Vec<Header>) -> Peer {
    raw_request_with_configuration(configuration(1), &mut headers, Some("opcua-flow/1"))
}

fn raw_request_with_configuration(
    config: Config,
    headers: &mut Vec<Header>,
    protocol: Option<&str>,
) -> Peer {
    let mut server = peer(&config, facts(1));
    let mut client = ClientConnection::new(strict_settings(Role::Client, 8, 4096));
    client
        .set_webtransport_transport_verified(true, true)
        .unwrap();
    let init = client.init_h3_streams(2, 6, 10).unwrap();
    server.command(1, 2, 0, 0, &init.control_data).unwrap();
    while let Some(output) = server.pop_output() {
        if output.kind == 1 {
            client
                .feed_stream(output.stream, &output.data, false)
                .unwrap();
            server
                .command(20, output.arg, output.data.len() as u64, 1, &[])
                .unwrap();
        }
    }
    let mut request = ConnectRequest::new("https", "plant.example:8443", "/ua/wt?resource=line-2");
    if let Some(protocol) = protocol {
        request = request.available_protocols(vec![protocol.into()]);
    }
    let mut request = request.to_headers().unwrap();
    request.append(headers);
    let id = client.send_request(&request, false).unwrap();
    let (bytes, _) = client.take_stream_data(id).unwrap();
    server.command(1, id, 0, 0, &bytes).unwrap();
    server
}

#[test]
fn duplicate_origin_protocol_and_obsolete_headers_are_never_accepted() {
    for (headers, expected) in [
        (
            vec![
                Header::new(b"origin", b"https://a.example").unwrap(),
                Header::new(b"origin", b"https://b.example").unwrap(),
            ],
            Code::PeerProtocol,
        ),
        (
            vec![Header::new(b"wt-available-protocols", b"\"opcua-flow/1\"").unwrap()],
            Code::PeerProtocol,
        ),
        (
            vec![Header::new(b"sec-webtransport-http3-draft", b"draft02").unwrap()],
            Code::UnsupportedDraft,
        ),
    ] {
        let mut server = raw_request(headers);
        let error = server.command(11, 0, 200, 0, &[]).unwrap_err();
        assert_eq!((error.scope, error.code), (Scope::Session, expected));
        assert!(server.session_state().is_none());
    }
}

#[test]
fn successful_connect_response_must_explicitly_select_the_configured_protocol() {
    rejected_response(
        &[Header::new(b":status", b"200").unwrap()],
        Code::ApplicationProtocolDenied,
    );
}

#[test]
fn non_200_success_cannot_establish_a_session_even_with_the_right_protocol() {
    rejected_response(
        &[
            Header::new(b":status", b"201").unwrap(),
            Header::new(b"wt-protocol", b"\"opcua-flow/1\"").unwrap(),
        ],
        Code::PeerProtocol,
    );
}

#[test]
fn valid_response_releases_buffered_streams_only_after_policy_establishment() {
    let (mut client, mut server) = pending(configuration(0), configuration(1));
    server.command(11, 0, 200, 0, &[]).unwrap();
    server.command(12, 0, 1, 0, &[]).unwrap();
    server.command(13, 1, 0, 0, &[0xAA, 0xBB]).unwrap();
    let mut response = None;
    while let Some(output) = server.pop_output() {
        if output.kind == 1 && output.stream == 0 {
            response = Some(output);
        } else if output.kind == 1 {
            client
                .command(1, output.stream, 0, 0, &output.data)
                .unwrap();
            server
                .command(20, output.arg, output.data.len() as u64, 1, &[])
                .unwrap();
        }
    }
    assert!(client.output().is_none());
    assert!(client.session_state().is_none());
    let response = response.unwrap();
    client.command(1, 0, 0, 0, &response.data).unwrap();
    server
        .command(20, response.arg, response.data.len() as u64, 1, &[])
        .unwrap();
    let established = client.pop_output().unwrap();
    let opened = client.pop_output().unwrap();
    let data = client.pop_output().unwrap();
    assert_eq!((established.kind, opened.kind, data.kind), (13, 14, 15));
    assert_eq!((opened.stream, data.stream, data.session), (1, 1, 0));
    assert_eq!(data.data, [0xAA, 0xBB]);
    assert!(client.output().is_none());
    assert_eq!(server.resource_snapshot().pending_sends, 0);
}

fn rejected_response(headers: &[Header], expected: Code) {
    rejected_response_with_configuration(configuration(0), headers, expected);
}

fn rejected_response_with_configuration(config: Config, headers: &[Header], expected: Code) {
    let mut client = peer(&config, facts(0));
    let mut server = ServerConnection::new(strict_settings(Role::Server, 8, 4096));
    server
        .set_webtransport_transport_verified(true, true)
        .unwrap();
    let init = server.init_h3_streams(3, 7, 11).unwrap();
    client.command(1, 3, 0, 0, &init.control_data).unwrap();
    while let Some(output) = client.pop_output() {
        if output.kind == 1 {
            server
                .feed_stream(output.stream, &output.data, false)
                .unwrap();
            client
                .command(20, output.arg, output.data.len() as u64, 1, &[])
                .unwrap();
        }
    }
    client.command(10, 0, 0, 0, &[]).unwrap();
    let mut completed_write = (0, 0);
    while let Some(output) = client.pop_output() {
        if output.kind == 1 {
            server
                .feed_stream(output.stream, &output.data, false)
                .unwrap();
            client
                .command(20, output.arg, output.data.len() as u64, 1, &[])
                .unwrap();
            completed_write = (output.arg, output.data.len() as u64);
        }
    }
    server.drain_events().unwrap();
    client
        .command(1, 1, 0, 0, &[0x40, 0x41, 0, 0xAA, 0xBB])
        .unwrap();
    assert!(
        client.output().is_none(),
        "early payload must wait for CONNECT policy"
    );
    let mut encoder = shiguredo_http3::QpackEncoder::new();
    let mut encoded = [0; 128];
    let length = encoder.encode(&mut encoded, headers, 0).unwrap();
    let mut bytes = vec![1];
    shiguredo_http3::varint::encode_into_vec(
        &mut bytes,
        shiguredo_http3::VarInt::new(length as u64).unwrap(),
    );
    bytes.extend_from_slice(&encoded[..length]);
    let error = client.command(1, 0, 0, 0, &bytes).unwrap_err();
    assert_eq!(
        (error.scope, error.code, error.protocol_code),
        (Scope::Connection, expected, ErrorCode::MessageError.code())
    );
    assert!(client.session_state().is_none());
    while let Some(output) = client.pop_output() {
        assert!(
            !matches!(output.kind, 13 | 14 | 15 | 17),
            "rejected response published application data"
        );
    }
    client
        .command(20, completed_write.0, completed_write.1, 1, &[])
        .unwrap();
    assert!(
        client.output().is_none(),
        "cleanup acknowledgement must not resume protocol delivery"
    );
    assert_eq!(client.command(12, 0, 4, 0, &[]).unwrap_err().code, expected);
}

#[test]
fn baseline_configuration_requires_explicit_version_without_a_private_subprotocol() {
    let mut config = configuration(0);
    config.application_protocol = Utf8::borrowed("");
    assert_eq!(configuration_error(&config).code, Code::InvalidProtocol);
    config.abi_version = 2;
    config.application_pair_tag = 0;
    unsafe { Policy::from_config(&config, &facts(0)) }.unwrap();
    config.application_protocol = Utf8 {
        data: std::ptr::null(),
        length: 0,
    };
    unsafe { Policy::from_config(&config, &facts(0)) }.unwrap();
    for value in ["opcua-flow/1", "opcua-uasc-draft", "white space", "\0"] {
        config.application_protocol = Utf8::borrowed(value);
        assert_eq!(configuration_error(&config).code, Code::InvalidProtocol);
    }
    config.application_protocol = Utf8::borrowed("");
    for version in [0, 3, u32::MAX] {
        config.abi_version = version;
        assert_eq!(configuration_error(&config).code, Code::InvalidAbi);
    }
    config.abi_version = 1;
    assert_eq!(
        configuration_error(&config).code,
        Code::InvalidTransportEvidence,
        "legacy configuration still requires an application-pair correlation"
    );
}

#[test]
fn baseline_connect_without_application_subprotocol_carries_opaque_stream_bytes() {
    for transport_only in [false, true] {
        let mut client_config = configuration(0);
        client_config.abi_version = 2;
        client_config.application_protocol = Utf8::borrowed("");
        let mut server_config = configuration(1);
        server_config.abi_version = 2;
        server_config.application_protocol = Utf8::borrowed("");
        let mut client_facts = facts(0);
        let mut server_facts = facts(1);
        if transport_only {
            for transport in [&mut client_facts, &mut server_facts] {
                transport.abi_version = 2;
                transport.authentication_policy = 1;
                transport.mutual_tls_authenticated = 0;
            }
        }
        let mut client = peer(&client_config, client_facts);
        let mut server = peer(&server_config, server_facts);
        pump(&mut client, &mut server);
        client.command(10, 0, 0, 0, &[]).unwrap();
        let pending = pump(&mut client, &mut server);
        assert_eq!(
            pending.iter().filter(|(_, event)| event.kind == 12).count(),
            1
        );
        assert!(!pending.iter().any(|(_, event)| event.kind == 13));
        server.command(11, 0, 200, 0, &[]).unwrap();
        let connected = pump(&mut client, &mut server);
        assert_eq!(
            connected
                .iter()
                .filter(|(_, event)| event.kind == 13)
                .count(),
            2
        );
        client.command(12, 0, 4, 0, &[]).unwrap();
        client.command(13, 4, 0, 0, b"\0baseline\xffbytes").unwrap();
        let events = pump(&mut client, &mut server);
        assert!(events.iter().any(|(side, event)| *side == 1
            && event.kind == 15
            && event.stream == 4
            && event.data == b"\0baseline\xffbytes"));
        server.command(13, 4, 0, 0, b"\xffreply\0").unwrap();
        let events = pump(&mut client, &mut server);
        assert!(events.iter().any(|(side, event)| *side == 0
            && event.kind == 15
            && event.stream == 4
            && event.data == b"\xffreply\0"));
    }
}

#[test]
fn baseline_authority_path_query_and_origin_remain_mandatory() {
    for (partition, expected) in [
        (0, Code::AuthorityDenied),
        (1, Code::PathDenied),
        (2, Code::PathDenied),
        (3, Code::OriginDenied),
        (4, Code::OriginDenied),
    ] {
        let mut client_config = configuration(0);
        client_config.abi_version = 2;
        client_config.application_protocol = Utf8::borrowed("");
        client_config.origin_policy = 2;
        client_config.origin = Utf8::borrowed("https://controller.example");
        match partition {
            0 => client_config.authority = Utf8::borrowed("wrong.example:8443"),
            1 => client_config.path = Utf8::borrowed("/other?resource=line-2"),
            2 => client_config.path = Utf8::borrowed("/ua/wt?resource=other"),
            3 => client_config.origin_policy = 0,
            _ => client_config.origin = Utf8::borrowed("https://wrong.example"),
        }
        if partition == 3 {
            client_config.origin = Utf8::borrowed("");
        }
        let mut server_config = configuration(1);
        server_config.abi_version = 2;
        server_config.application_protocol = Utf8::borrowed("");
        server_config.origin_policy = 2;
        server_config.origin = Utf8::borrowed("https://controller.example");
        let (mut client, mut server) = pending(client_config, server_config);
        let before = server.resource_snapshot();
        let error = server.command(11, 0, 200, 0, &[]).unwrap_err();
        assert_eq!((error.scope, error.code), (Scope::Session, expected));
        assert_eq!(server.resource_snapshot(), before);
        assert!(server.session_state().is_none());
        server.command(11, 0, 403, 0, &[]).unwrap();
        assert!(
            !pump(&mut client, &mut server)
                .iter()
                .any(|(_, event)| event.kind == 13)
        );
    }
}

#[test]
fn baseline_omits_http_credentials_and_refuses_private_or_duplicate_negotiation_headers() {
    for (mut headers, expected) in [
        (
            vec![Header::new(b"cookie", b"test=value").unwrap()],
            Code::PeerProtocol,
        ),
        (
            vec![Header::new(b"authorization", b"Bearer test").unwrap()],
            Code::PeerProtocol,
        ),
        (
            vec![Header::new(b"proxy-authorization", b"Basic test").unwrap()],
            Code::PeerProtocol,
        ),
        (
            vec![Header::new(b"wt-available-protocols", b"\"opcua-flow/1\"").unwrap()],
            Code::ApplicationProtocolDenied,
        ),
        (
            vec![
                Header::new(b"wt-available-protocols", b"\"a\"").unwrap(),
                Header::new(b"wt-available-protocols", b"\"b\"").unwrap(),
            ],
            Code::PeerProtocol,
        ),
        (
            vec![
                Header::new(b"origin", b"https://a.example").unwrap(),
                Header::new(b"origin", b"https://b.example").unwrap(),
            ],
            Code::PeerProtocol,
        ),
        (
            vec![Header::new(b"sec-webtransport-http3-draft", b"draft02").unwrap()],
            Code::UnsupportedDraft,
        ),
    ] {
        let mut config = configuration(1);
        config.abi_version = 2;
        config.application_protocol = Utf8::borrowed("");
        let mut server = raw_request_with_configuration(config, &mut headers, None);
        let before = server.resource_snapshot();
        let error = server.command(11, 0, 200, 0, &[]).unwrap_err();
        assert_eq!((error.scope, error.code), (Scope::Session, expected));
        assert_eq!(server.resource_snapshot(), before);
        assert!(server.session_state().is_none());
    }
}

#[test]
fn baseline_client_rejects_unsolicited_selected_subprotocol_and_obsolete_headers() {
    for extra in [
        vec![Header::new(b"wt-protocol", b"\"opcua-flow/1\"").unwrap()],
        vec![Header::new(b"wt-protocol", b"\"\"").unwrap()],
        vec![
            Header::new(b"wt-protocol", b"\"a\"").unwrap(),
            Header::new(b"wt-protocol", b"\"b\"").unwrap(),
        ],
        vec![Header::new(b"sec-webtransport-http3-draft", b"draft02").unwrap()],
    ] {
        let mut config = configuration(0);
        config.abi_version = 2;
        config.application_protocol = Utf8::borrowed("");
        let mut headers = vec![Header::new(b":status", b"200").unwrap()];
        headers.extend(extra);
        rejected_response_with_configuration(config, &headers, Code::ApplicationProtocolDenied);
    }
}

#[test]
fn baseline_and_legacy_subprotocol_policies_cannot_silently_fallback() {
    for baseline_client in [false, true] {
        let mut client_config = configuration(0);
        let mut server_config = configuration(1);
        let baseline = if baseline_client {
            &mut client_config
        } else {
            &mut server_config
        };
        baseline.abi_version = 2;
        baseline.application_protocol = Utf8::borrowed("");
        let (mut client, mut server) = pending(client_config, server_config);
        let error = server.command(11, 0, 200, 0, &[]).unwrap_err();
        assert_eq!(error.code, Code::ApplicationProtocolDenied);
        assert!(server.session_state().is_none());
        server.command(11, 0, 403, 0, &[]).unwrap();
        assert!(
            !pump(&mut client, &mut server)
                .iter()
                .any(|(_, event)| event.kind == 13)
        );
    }
}

#[test]
fn local_stream_allocator_is_role_checked_and_ids_are_not_reused_after_fin() {
    let (mut client, mut server) = established(|_| {}, 4096);
    for id in [0, 1, 2, 3, u64::MAX] {
        assert_eq!(
            client.command(12, 0, id, 0, &[]).unwrap_err().code,
            Code::InvalidStreamId
        );
    }
    client.command(12, 0, 4, 0, &[]).unwrap();
    client.command(13, 4, 1, 0, &[]).unwrap();
    pump(&mut client, &mut server);
    assert_eq!(
        client.command(12, 0, 4, 0, &[]).unwrap_err().code,
        Code::InvalidStreamId
    );
    server.command(12, 0, 1, 0, &[]).unwrap();
    server.command(13, 1, 0, 0, b"server initiated").unwrap();
    assert!(
        pump(&mut client, &mut server)
            .iter()
            .any(|(side, event)| *side == 0 && event.kind == 15 && event.stream == 1)
    );
}

#[test]
fn one_session_lifetime_survives_close_drain_and_goaway_without_pooling() {
    for operation in [15, 16, 19] {
        let (mut client, mut server) = established(|_| {}, 4096);
        client.command(operation, 0, 0, 0, &[]).unwrap();
        let events = pump(&mut client, &mut server);
        let expected = match operation {
            15 => 19,
            16 => 20,
            _ => 28,
        };
        assert!(events.iter().any(|(_, event)| event.kind == expected));
        assert_eq!(
            client.command(10, 0, 0, 0, &[]).unwrap_err().code,
            Code::SessionLimit
        );
        assert!(client.command(12, 0, 4, 0, &[]).is_err() || operation == 19);
    }
}

#[test]
fn input_and_lifetime_limits_reject_before_output_or_credit_mutation() {
    let (mut client, _) = established(|config| config.limits.input_bytes = 512, 4096);
    client.command(12, 0, 4, 0, &[]).unwrap();
    let before = client.resource_snapshot();
    assert_eq!(
        client.command(13, 4, 0, 0, &[0; 513]).unwrap_err().code,
        Code::LimitExceeded
    );
    assert_eq!(client.resource_snapshot(), before);
    client.command(13, 4, 0, 0, &[0; 512]).unwrap();

    let (mut client, _) = established(|config| config.limits.lifetime_streams = 7, 4096);
    let before = client.resource_snapshot();
    assert_eq!(
        client.command(12, 0, 4, 0, &[]).unwrap_err().code,
        Code::LimitExceeded
    );
    assert_eq!(client.resource_snapshot(), before);
}

#[test]
fn pending_stream_and_datagram_session_ids_cannot_create_hidden_sessions() {
    let (mut client, mut server) = established(|_| {}, 4096);
    let before = server.resource_snapshot();
    let error = server.command(2, 0, 0, 0, &[1, 99]).unwrap_err();
    assert_eq!(
        (error.code, error.scope, error.session),
        (Code::UnknownSession, Scope::Session, 4)
    );
    assert_eq!(server.resource_snapshot(), before);
    assert_eq!(
        server
            .command(1, 4, 0, 0, &[0x40, 0x41, 4])
            .unwrap_err()
            .code,
        Code::SessionLimit
    );
    assert_eq!(
        client.command(10, 4, 0, 0, &[]).unwrap_err().code,
        Code::InvalidStreamId
    );
}

#[test]
fn receipt_retention_is_bounded_and_conflicting_replays_never_release_credit() {
    let (mut client, _) = established(|config| config.limits.receipts = 2, 4096);
    client.command(14, 0, 0, 0, b"one").unwrap();
    let first = client.pop_output().unwrap();
    client
        .command(20, first.arg, first.data.len() as u64, 1, &[])
        .unwrap();
    for _ in 0..2 {
        client.command(14, 0, 0, 0, b"next").unwrap();
        let output = client.pop_output().unwrap();
        client
            .command(20, output.arg, output.data.len() as u64, 1, &[])
            .unwrap();
        client
            .command(20, output.arg, output.data.len() as u64, 1, &[])
            .unwrap();
        assert_eq!(
            client.command(20, output.arg, 0, 5, &[]).unwrap_err().code,
            Code::InvalidCompletion
        );
    }
    let before = client.resource_snapshot();
    assert_eq!(
        client
            .command(20, first.arg, first.data.len() as u64, 1, &[])
            .unwrap_err()
            .code,
        Code::InvalidCompletion
    );
    assert_eq!(client.resource_snapshot(), before);
}

#[test]
fn cancellation_before_first_poll_preserves_the_reliable_prefix() {
    let (mut client, _) = established(|_| {}, 4);
    client.command(12, 0, 4, 0, &[]).unwrap();
    client.command(13, 4, 1, 0, b"body").unwrap();
    client.command(17, 4, 0, 0, &[]).unwrap();
    let prefix = client.pop_output().unwrap();
    assert_eq!(
        (prefix.kind, prefix.flags, prefix.data.as_slice()),
        (1, 2, [0x40, 0x41, 0].as_slice())
    );
    assert!(client.output().is_none());
    assert_eq!(client.resource_snapshot().reserved_payload, 4);
    client.command(20, prefix.arg, 3, 1, &[]).unwrap();
    let reset = client.pop_output().unwrap();
    assert_eq!((reset.kind, reset.stream, reset.aux), (3, 4, 3));
    client.command(22, 4, 3, 3, &[]).unwrap();
    assert_eq!(client.resource_snapshot().reserved_payload, 0);
    assert_eq!(client.resource_snapshot().pending_sends, 0);
}

#[test]
fn invalid_command_arguments_do_not_consume_admitted_sends() {
    let (mut client, _) = established(|_| {}, 4096);
    client.command(12, 0, 4, 0, &[]).unwrap();
    let before = client.resource_snapshot();
    for (op, id, arg, aux, payload) in [
        (13, 4, 2, 0, b"".as_slice()),
        (13, 4, 0, 1, b""),
        (20, 1, 0, 0, b"not allowed"),
        (14, 0, 1, 0, b""),
        (2, 1, 0, 0, b""),
    ] {
        assert_eq!(
            client.command(op, id, arg, aux, payload).unwrap_err().code,
            Code::InvalidArgument
        );
        assert_eq!(client.resource_snapshot(), before);
    }
    assert_eq!(
        client.command(999, 0, 0, 0, &[]).unwrap_err().code,
        Code::InvalidOperation
    );
}

#[test]
fn local_allocator_cannot_rewind_even_into_a_previously_unused_gap() {
    let (mut client, _) = established(|_| {}, 4096);
    client.command(12, 0, 12, 0, &[]).unwrap();
    let before = client.resource_snapshot();
    assert_eq!(
        client.command(12, 0, 4, 0, &[]).unwrap_err().code,
        Code::InvalidStreamId
    );
    assert_eq!(
        client.command(12, 0, 8, 0, &[]).unwrap_err().code,
        Code::InvalidStreamId
    );
    assert_eq!(client.resource_snapshot(), before);
    client.command(12, 0, 16, 0, &[]).unwrap();
}

#[test]
fn closed_session_discards_unleased_datagrams_but_keeps_native_leases() {
    let (mut client, _) = established(|_| {}, 4096);
    client.command(14, 0, 0, 0, b"leased").unwrap();
    client.command(14, 0, 0, 0, b"discard").unwrap();
    let lease = client.pop_output().unwrap();
    assert_eq!(lease.kind, 2);
    client.command(16, 0, 7, 0, b"closed").unwrap();
    assert_eq!(client.resource_snapshot().leased_bytes, 7);
    while let Some(output) = client.pop_output() {
        assert_ne!(
            output.kind, 2,
            "an unleased datagram survived session close"
        );
        if output.kind == 1 {
            client
                .command(
                    20,
                    output.arg,
                    output.data.len() as u64,
                    if output.flags & 1 == 0 { 1 } else { 3 },
                    &[],
                )
                .unwrap();
        }
    }
    assert_eq!(client.resource_snapshot().pending_sends, 1);
    client.command(20, lease.arg, 0, 5, &[]).unwrap();
    assert_eq!(client.resource_snapshot().pending_sends, 0);
    assert_eq!(client.resource_snapshot().leased_bytes, 0);
}

#[test]
fn large_session_close_streams_reset_actions_through_a_bounded_queue() {
    let (mut client, _) = established(|config| config.limits.output_events = 1024, 4096);
    let mut credit = Vec::new();
    shiguredo_http3::webtransport::Capsule::MaxStreams {
        bidirectional: true,
        maximum: 1024,
    }
    .encode_as_data_frame(&mut credit);
    client.command(1, 0, 0, 0, &credit).unwrap();
    while let Some(event) = client.pop_output() {
        assert_eq!(event.kind, 18);
    }
    for index in 1..=600 {
        client.command(12, 0, index * 4, 0, &[]).unwrap();
        let prefix = client.pop_output().unwrap();
        assert_eq!((prefix.kind, prefix.flags), (1, 2));
        client.command(20, prefix.arg, 3, 1, &[]).unwrap();
    }
    client.command(16, 0, 0, 0, &[]).unwrap();
    let mut stops = 0;
    let mut resets = 0;
    let mut closed = 0;
    while let Some(output) = client.pop_output() {
        match output.kind {
            1 => client
                .command(
                    20,
                    output.arg,
                    output.data.len() as u64,
                    if output.flags & 1 == 0 { 1 } else { 3 },
                    &[],
                )
                .unwrap(),
            3 => {
                assert_eq!(output.aux, 3);
                client.command(22, output.stream, 3, 3, &[]).unwrap();
                resets += 1;
            }
            4 => stops += 1,
            20 => {
                assert_eq!((output.session, output.arg), (0, 0));
                closed += 1;
            }
            kind => panic!("unexpected close output {kind}"),
        }
        assert!(stops <= 600 && resets <= 600);
    }
    assert_eq!((stops, resets), (600, 600));
    assert_eq!(closed, 1);
    assert_eq!(client.resource_snapshot().pending_sends, 0);
    assert_eq!(client.resource_snapshot().reserved_payload, 0);
}

#[test]
fn incoming_backpressure_is_retryable_without_consuming_or_duplicating_bytes() {
    let (mut client, mut server) = established(
        |config| {
            config.limits.output_bytes = 4096;
            config.limits.header_bytes = 4096;
        },
        8192,
    );
    client.command(12, 0, 4, 0, &[]).unwrap();
    pump(&mut client, &mut server);
    client.command(13, 4, 0, 0, &[1; 4096]).unwrap();
    let before = client.resource_snapshot();
    let error = client.command(1, 4, 0, 0, b"incoming").unwrap_err();
    assert_eq!(
        (error.code, error.scope, error.info().flags),
        (Code::Backpressure, Scope::Operation, 1)
    );
    assert_eq!(client.resource_snapshot(), before);
    let send = client.pop_output().unwrap();
    client
        .command(20, send.arg, send.data.len() as u64, 1, &[])
        .unwrap();
    client.command(1, 4, 0, 0, b"incoming").unwrap();
    let event = client.pop_output().unwrap();
    assert_eq!(
        (event.kind, event.data.as_slice()),
        (15, b"incoming".as_slice())
    );
    assert!(client.output().is_none());
    client.command(18, 0, 8, 0, &[]).unwrap();
    assert_eq!(
        client.command(18, 0, 1, 0, &[]).unwrap_err().code,
        Code::InvalidArgument
    );
}

#[test]
fn unknown_control_streams_have_a_cumulative_preallocation_budget() {
    let (mut client, _) = established(|config| config.limits.control_bytes = 128, 4096);
    let mut unknown = [0; 128];
    unknown[0] = 0x21;
    client.command(1, 15, 0, 0, &unknown).unwrap();
    let before = client.resource_snapshot();
    let error = client.command(1, 15, 0, 0, &[0]).unwrap_err();
    assert_eq!(
        (error.scope, error.code, error.protocol_code),
        (
            Scope::Connection,
            Code::LimitExceeded,
            ErrorCode::ExcessiveLoad.code()
        )
    );
    assert_eq!(client.resource_snapshot(), before);
}

#[test]
fn production_association_prefix_is_fragmented_bounded_and_never_delivered_as_payload() {
    let (_, mut server) = established(|_| {}, 4096);
    for byte in [0x40, 0x41] {
        server.command(1, 4, 0, 0, &[byte]).unwrap();
        assert!(server.output().is_none());
    }
    server.command(1, 4, 0, 0, &[0]).unwrap();
    let opened = server.pop_output().unwrap();
    assert_eq!((opened.kind, opened.stream, opened.session), (14, 4, 0));
    server.command(1, 4, 1, 0, b"body").unwrap();
    let mut payloads = Vec::new();
    let mut fins = 0;
    while let Some(output) = server.pop_output() {
        if output.kind == 15 {
            payloads.push(output.data);
        }
        if output.kind == 16 {
            fins += 1;
        }
    }
    assert_eq!(payloads, [b"body".to_vec()]);
    assert_eq!(fins, 1);
    let error = server.command(1, 4, 0, 0, b"late").unwrap_err();
    assert_eq!(
        (error.scope, error.code),
        (Scope::Stream, Code::InvalidState)
    );
}

#[test]
fn maximum_and_minimum_policy_string_lengths_are_usable() {
    let host = format!(
        "{}.{}.{}.{}",
        "a".repeat(63),
        "b".repeat(63),
        "c".repeat(63),
        "d".repeat(57)
    );
    let authority = format!("{host}:65535");
    let origin = format!("https://{authority}");
    let path = format!("/{}", "a".repeat(2047));
    let protocol = "p".repeat(128);
    assert_eq!(
        (authority.len(), origin.len(), path.len(), protocol.len()),
        (255, 263, 2048, 128)
    );
    let mut config = configuration(0);
    config.authority = Utf8::borrowed(&authority);
    config.origin = Utf8::borrowed(&origin);
    config.origin_policy = 2;
    config.path = Utf8::borrowed(&path);
    config.application_protocol = Utf8::borrowed(&protocol);
    assert!(unsafe { Policy::from_config(&config, &facts(0)) }.is_ok());
    config.authority = Utf8::borrowed("a");
    config.path = Utf8::borrowed("/");
    config.origin = Utf8::borrowed("https://a");
    config.application_protocol = Utf8::borrowed("p");
    assert!(unsafe { Policy::from_config(&config, &facts(0)) }.is_ok());
}

#[test]
fn unleased_and_invalid_flag_completions_cannot_change_accounting() {
    let (mut client, _) = established(|_| {}, 4096);
    client.command(12, 0, 4, 0, &[]).unwrap();
    let token = client.output().unwrap().arg;
    let before = client.resource_snapshot();
    assert_eq!(
        client.command(20, token, 3, 1, &[]).unwrap_err().code,
        Code::InvalidCompletion
    );
    assert_eq!(client.resource_snapshot(), before);
    let prefix = client.pop_output().unwrap();
    let leased = client.resource_snapshot();
    for flags in [2, 3, 4, 6, 7, 8, u64::MAX] {
        assert_eq!(
            client
                .command(20, prefix.arg, 3, flags, &[])
                .unwrap_err()
                .code,
            Code::InvalidCompletion
        );
        assert_eq!(client.resource_snapshot(), leased, "flags {flags}");
    }
    client.command(20, prefix.arg, 3, 0, &[]).unwrap();
    assert_eq!(client.resource_snapshot().leased_bytes, 3);
    client.command(20, prefix.arg, 3, 1, &[]).unwrap();
    assert_eq!(client.resource_snapshot().leased_bytes, 0);
}

#[test]
fn pending_session_ingress_rejects_the_next_byte_before_any_output_change() {
    let mut config = configuration(1);
    config.limits.pending_bytes = 1024;
    let mut server = peer(&config, facts(1));
    server.command(2, 0, 0, 0, &[0; 1024]).unwrap();
    let before = server.resource_snapshot();
    let error = server.command(2, 0, 0, 0, &[0]).unwrap_err();
    assert_eq!(
        (error.code, error.scope, error.protocol_code),
        (
            Code::LimitExceeded,
            Scope::Connection,
            ErrorCode::ExcessiveLoad.code()
        )
    );
    assert_eq!(server.resource_snapshot(), before);
}

#[test]
fn receive_fin_reserves_control_headroom_before_accepting_a_full_budget_payload() {
    let (mut client, mut server) = established(
        |config| {
            config.limits.output_bytes = 4096;
            config.limits.header_bytes = 4096;
        },
        8192,
    );
    client.command(12, 0, 4, 0, &[]).unwrap();
    pump(&mut client, &mut server);
    let before = server.resource_snapshot();
    assert_eq!(
        server.command(1, 4, 1, 0, &[7; 4096]).unwrap_err().code,
        Code::LimitExceeded
    );
    assert_eq!(server.resource_snapshot(), before);
    server.command(1, 4, 0, 0, &[7; 2048]).unwrap();
    let first = server.pop_output().unwrap();
    assert_eq!((first.kind, first.data.len()), (15, 2048));
    server.command(1, 4, 1, 0, &[7; 2048]).unwrap();
    let last = server.pop_output().unwrap();
    assert_eq!((last.kind, last.data.len()), (15, 2048));
    assert!(first.data.iter().chain(&last.data).all(|byte| *byte == 7));
    assert_eq!(server.pop_output().unwrap().kind, 16);
    server.command(13, 4, 0, 0, b"reverse").unwrap();
}

#[test]
fn received_stop_is_observable_without_releasing_a_native_body_lease() {
    let (mut client, mut server) = established(|_| {}, 4096);
    client.command(12, 0, 4, 0, &[]).unwrap();
    pump(&mut client, &mut server);
    client.command(13, 4, 0, 0, b"body").unwrap();
    let body = client.pop_output().unwrap();
    client.command(4, 4, 0x52e4a40fa8e0, 0, &[]).unwrap();
    let stop = client.pop_output().unwrap();
    assert_eq!((stop.kind, stop.stream, stop.arg), (22, 4, 0x52e4a40fa8e0));
    assert_eq!(client.resource_snapshot().leased_bytes, 4);
    client.command(17, 4, 5, 0, &[]).unwrap();
    let reset = client.pop_output().unwrap();
    assert_eq!((reset.kind, reset.arg, reset.aux), (3, 0x52e4a40fa8e0, 3));
    client.command(20, body.arg, 0, 5, &[]).unwrap();
    client.command(22, 4, 3, 3, &[]).unwrap();
    assert_eq!(client.resource_snapshot().reserved_payload, 0);
    assert_eq!(client.resource_snapshot().leased_bytes, 0);
}
