// Copyright (c) OPC Foundation. Licensed under the MIT License.
//! Immutable, explicitly supplied connection policy. No endpoint or identity defaults.

use crate::TransportFacts;
use crate::error::{Code, Failure};

/// Maximum QUIC stream identifier and flow-control integer.
pub const MAX_VARINT: u64 = (1 << 62) - 1;

/// Caller-owned UTF8 view, read only during creation.
#[derive(Clone, Copy, Debug)]
#[repr(C)]
pub struct Utf8 {
    pub data: *const u8,
    pub length: usize,
}

impl Utf8 {
    /// Create a borrowed view. The caller must keep the source alive through the native call.
    pub fn borrowed(text: &str) -> Self {
        Self {
            data: text.as_ptr(),
            length: text.len(),
        }
    }
}

/// Exact Origin policy. Values are C integers to permit validation of unknown discriminators.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[repr(u32)]
pub enum OriginPolicy {
    AbsentOnly = 0,
    OptionalExact = 1,
    RequiredExact = 2,
}

/// Finite local limits. These do not grant peer transport credit or process-wide admission.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[repr(C)]
pub struct Limits {
    pub input_bytes: u64,
    pub output_bytes: u64,
    pub pending_sends: u64,
    pub output_events: u64,
    pub lifetime_streams: u64,
    pub header_count: u64,
    pub header_bytes: u64,
    pub control_bytes: u64,
    pub request_bytes: u64,
    pub capsule_bytes: u64,
    pub pending_bytes: u64,
    pub receipts: u64,
}

impl Default for Limits {
    fn default() -> Self {
        Self {
            input_bytes: 1024 * 1024,
            output_bytes: 1024 * 1024,
            pending_sends: 512,
            output_events: 4096,
            lifetime_streams: 4096,
            header_count: 32,
            header_bytes: 16 * 1024,
            control_bytes: 64 * 1024,
            request_bytes: 64 * 1024,
            capsule_bytes: 64 * 1024,
            pending_bytes: 128 * 1024,
            receipts: 256,
        }
    }
}

impl Limits {
    pub fn validate(&self) -> Result<(), Failure> {
        for (value, minimum, maximum) in [
            (self.input_bytes, 16, 1024 * 1024),
            (self.output_bytes, 4096, 1024 * 1024),
            (self.pending_sends, 8, 512),
            (self.output_events, 64, 4096),
            (self.lifetime_streams, 7, 4096),
            (self.header_count, 8, 128),
            (self.header_bytes, 1024, 16 * 1024),
            (self.control_bytes, 128, 64 * 1024),
            (self.request_bytes, 1024, 64 * 1024),
            (self.capsule_bytes, 1024, 64 * 1024),
            (self.pending_bytes, 1024, 128 * 1024),
            (self.receipts, 1, 256),
        ] {
            if !(minimum..=maximum).contains(&value) {
                return Err(Failure::config(
                    Code::InvalidLimits,
                    "local limit outside reviewed range",
                ));
            }
        }
        if self.header_bytes > self.request_bytes
            || self.header_bytes > self.output_bytes
            || self.output_events < self.header_bytes / 32 + 8
        {
            return Err(Failure::config(
                Code::InvalidLimits,
                "header budget exceeds framing/output byte or event budget",
            ));
        }
        Ok(())
    }
}

/// Versioned C creation input; all three local unidirectional IDs must already be allocated.
#[derive(Clone, Copy, Debug)]
#[repr(C)]
pub struct Config {
    pub struct_size: u32,
    pub abi_version: u32,
    pub origin_policy: u32,
    pub maximum_sessions: u32,
    pub authority: Utf8,
    pub path: Utf8,
    pub origin: Utf8,
    pub application_protocol: Utf8,
    pub control_stream: u64,
    pub encoder_stream: u64,
    pub decoder_stream: u64,
    pub connect_stream: u64,
    pub connection_tag: u64,
    pub application_pair_tag: u64,
    pub limits: Limits,
}

/// Owned policy after syntactic and resource validation.
#[derive(Clone, Debug)]
pub struct Policy {
    pub(crate) authority: String,
    pub(crate) path: String,
    pub(crate) origin: String,
    pub(crate) application_protocol: String,
    pub(crate) origin_policy: OriginPolicy,
    pub(crate) control_streams: [u64; 3],
    pub(crate) connection_tag: u64,
    pub(crate) application_pair_tag: u64,
    pub(crate) limits: Limits,
}

impl Policy {
    /// # Safety
    /// Every nonempty UTF8 view must be readable for its declared length during this call.
    /// Lengths and all scalar bounds are checked before any view is read or copied.
    pub unsafe fn from_config(config: &Config, facts: &TransportFacts) -> Result<Self, Failure> {
        if config.abi_version != 1 || config.struct_size as usize != size_of::<Config>() {
            return Err(Failure::config(
                Code::InvalidAbi,
                "unsupported configuration layout",
            ));
        }
        facts.validate()?;
        config.limits.validate()?;
        if config.maximum_sessions != 1 {
            return Err(Failure::config(
                Code::SessionLimit,
                "one CONNECT lifetime per authenticated application pair",
            ));
        }
        if config.connection_tag == 0 || config.application_pair_tag == 0 {
            return Err(Failure::config(
                Code::InvalidTransportEvidence,
                "external connection/application-pair tags required",
            ));
        }
        let streams = [
            config.control_stream,
            config.encoder_stream,
            config.decoder_stream,
        ];
        if config.connect_stream != 0
            || streams
                .iter()
                .any(|id| *id > MAX_VARINT || *id & 3 != u64::from(facts.role) + 2)
            || streams[0] == streams[1]
            || streams[0] == streams[2]
            || streams[1] == streams[2]
        {
            return Err(Failure::config(
                Code::InvalidStreamId,
                "reserve CONNECT zero and three distinct local uni streams",
            ));
        }
        let mode = match config.origin_policy {
            0 => OriginPolicy::AbsentOnly,
            1 => OriginPolicy::OptionalExact,
            2 => OriginPolicy::RequiredExact,
            _ => return Err(Failure::config(Code::InvalidOrigin, "unknown Origin policy")),
        };
        for (text, maximum, code) in [
            (config.authority, 255, Code::InvalidAuthority),
            (config.path, 2048, Code::InvalidPath),
            (config.origin, 263, Code::InvalidOrigin),
            (config.application_protocol, 128, Code::InvalidProtocol),
        ] {
            if text.length > maximum || (text.data.is_null() && text.length != 0) {
                return Err(Failure::config(
                    code,
                    "invalid configuration string length/pointer",
                ));
            }
        }
        let authority = unsafe { read(config.authority, Code::InvalidAuthority)? };
        let path = unsafe { read(config.path, Code::InvalidPath)? };
        let origin = unsafe { read(config.origin, Code::InvalidOrigin)? };
        let protocol = unsafe { read(config.application_protocol, Code::InvalidProtocol)? };
        if !valid_authority(authority) {
            return Err(Failure::config(
                Code::InvalidAuthority,
                "authority must be a canonical ASCII host and optional port",
            ));
        }
        if !valid_path(path) {
            return Err(Failure::config(
                Code::InvalidPath,
                "path must be an absolute ASCII URI path with optional query",
            ));
        }
        if (mode == OriginPolicy::AbsentOnly && !origin.is_empty())
            || (mode != OriginPolicy::AbsentOnly
                && !origin.strip_prefix("https://").is_some_and(valid_authority))
        {
            return Err(Failure::config(
                Code::InvalidOrigin,
                "Origin must match the selected exact HTTPS policy",
            ));
        }
        if protocol.is_empty()
            || !protocol
                .bytes()
                .all(|b| b.is_ascii_alphanumeric() || b"-._+/".contains(&b))
        {
            return Err(Failure::config(
                Code::InvalidProtocol,
                "application protocol must be a nonempty ASCII identifier",
            ));
        }
        if authority.len() + path.len() + origin.len() + protocol.len() + 512
            > config.limits.header_bytes as usize
        {
            return Err(Failure::config(
                Code::InvalidLimits,
                "configured CONNECT cannot fit the header budget",
            ));
        }
        Ok(Self {
            authority: authority.into(),
            path: path.into(),
            origin: origin.into(),
            application_protocol: protocol.into(),
            origin_policy: mode,
            control_streams: streams,
            connection_tag: config.connection_tag,
            application_pair_tag: config.application_pair_tag,
            limits: config.limits,
        })
    }

    pub fn limits(&self) -> Limits {
        self.limits
    }

    pub fn connection_tag(&self) -> u64 {
        self.connection_tag
    }

    pub fn application_pair_tag(&self) -> u64 {
        self.application_pair_tag
    }
}

unsafe fn read<'a>(view: Utf8, code: Code) -> Result<&'a str, Failure> {
    let bytes = if view.length == 0 {
        &[]
    } else {
        unsafe { std::slice::from_raw_parts(view.data, view.length) }
    };
    std::str::from_utf8(bytes).map_err(|_| Failure::config(code, "configuration must be valid UTF8"))
}

fn valid_authority(authority: &str) -> bool {
    if authority.is_empty() || !authority.is_ascii() {
        return false;
    }
    let (host, port) = if let Some(rest) = authority.strip_prefix('[') {
        let Some(end) = rest.find(']') else {
            return false;
        };
        if rest[..end].parse::<std::net::Ipv6Addr>().is_err() {
            return false;
        }
        let tail = &rest[end + 1..];
        if !tail.is_empty() && !tail.starts_with(':') {
            return false;
        }
        ("", tail.strip_prefix(':'))
    } else {
        let (host, port) = authority
            .split_once(':')
            .map_or((authority, None), |(h, p)| (h, Some(p)));
        if host.is_empty()
            || host.len() > 253
            || !host.split('.').all(|label| {
                !label.is_empty()
                    && label.len() <= 63
                    && label.as_bytes()[0].is_ascii_alphanumeric()
                    && label.as_bytes()[label.len() - 1].is_ascii_alphanumeric()
                    && label
                        .bytes()
                        .all(|b| b.is_ascii_lowercase() || b.is_ascii_digit() || b == b'-')
            })
        {
            return false;
        }
        (host, port)
    };
    let _ = host;
    port.is_none_or(|p| {
        !p.is_empty()
            && !p.starts_with('0')
            && p.bytes().all(|b| b.is_ascii_digit())
            && p.parse::<u16>().is_ok_and(|value| value != 0)
    })
}

fn valid_path(path: &str) -> bool {
    if !path.starts_with('/') || path.starts_with("//") || !path.is_ascii() {
        return false;
    }
    let bytes = path.as_bytes();
    let mut index = 0;
    while index < bytes.len() {
        let value = bytes[index];
        if value == b'%' {
            if index + 2 >= bytes.len() || !bytes[index + 1..index + 3].iter().all(u8::is_ascii_hexdigit) {
                return false;
            }
            index += 3;
        } else if value.is_ascii_alphanumeric() || b"/-._~!$&'()*+,;=:@?".contains(&value) {
            index += 1;
        } else {
            return false;
        }
    }
    true
}
