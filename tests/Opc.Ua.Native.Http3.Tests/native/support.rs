// Copyright (c) OPC Foundation. Licensed under the MIT License.
//! Synthetic state-test inputs only. These are not authenticated network evidence.

use opcua_http3::config::{Config, Limits, Policy, Utf8};
use opcua_http3::{Peer, TransportFacts};

pub fn configuration(role: u32) -> Config {
    let base = u64::from(role) + 2;
    Config {
        struct_size: size_of::<Config>() as u32,
        abi_version: 1,
        origin_policy: 0,
        maximum_sessions: 1,
        authority: Utf8::borrowed("plant.example:8443"),
        path: Utf8::borrowed("/ua/wt?resource=line-2"),
        origin: Utf8::borrowed(""),
        application_protocol: Utf8::borrowed("opcua-flow/1"),
        control_stream: base,
        encoder_stream: base + 4,
        decoder_stream: base + 8,
        connect_stream: 0,
        connection_tag: 17,
        application_pair_tag: 19,
        limits: Limits::default(),
    }
}

pub fn facts(role: u32) -> TransportFacts {
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

pub fn peer(config: &Config, facts: TransportFacts) -> Peer {
    let policy = unsafe { Policy::from_config(config, &facts) }.unwrap();
    Peer::new(facts, policy).unwrap()
}

pub fn pump(client: &mut Peer, server: &mut Peer) -> Vec<(u32, opcua_http3::Output)> {
    let mut events = Vec::new();
    for _ in 0..64 {
        let mut progress = false;
        for side in [0, 1] {
            let (source, target) = if side == 0 {
                (&mut *client, &mut *server)
            } else {
                (&mut *server, &mut *client)
            };
            while let Some(output) = source.pop_output() {
                match output.kind {
                    1 => {
                        target
                            .command(
                                1,
                                output.stream,
                                u64::from(output.flags & 1),
                                0,
                                &output.data,
                            )
                            .unwrap();
                        source
                            .command(
                                20,
                                output.arg,
                                output.data.len() as u64,
                                if output.flags & 1 == 0 { 1 } else { 3 },
                                &[],
                            )
                            .unwrap();
                        progress = true;
                    }
                    2 => {
                        target.command(2, 0, 0, 0, &output.data).unwrap();
                        source
                            .command(20, output.arg, output.data.len() as u64, 1, &[])
                            .unwrap();
                        progress = true;
                    }
                    _ => events.push((side, output)),
                }
            }
        }
        if !progress {
            return events;
        }
    }
    panic!("bounded state pump did not quiesce");
}

pub fn established(configure: impl Fn(&mut Config), data: u64) -> (Peer, Peer) {
    let mut config = configuration(0);
    configure(&mut config);
    let mut transport = facts(0);
    transport.initial_max_data = data;
    let mut client = peer(&config, transport);
    config.control_stream += 1;
    config.encoder_stream += 1;
    config.decoder_stream += 1;
    transport.role = 1;
    let mut server = peer(&config, transport);
    pump(&mut client, &mut server);
    client.command(10, 0, 0, 0, &[]).unwrap();
    let pending = pump(&mut client, &mut server);
    assert_eq!(
        pending
            .iter()
            .filter(|(_, output)| output.kind == 12)
            .count(),
        1
    );
    assert!(!pending.iter().any(|(_, output)| output.kind == 13));
    server.command(11, 0, 200, 0, &[]).unwrap();
    let connected = pump(&mut client, &mut server);
    assert_eq!(
        connected
            .iter()
            .filter(|(_, output)| output.kind == 13 && output.flags == 1)
            .count(),
        2
    );
    (client, server)
}
