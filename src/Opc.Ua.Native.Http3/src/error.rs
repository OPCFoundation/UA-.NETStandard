// Copyright (c) OPC Foundation. Licensed under the MIT License.
//! Stable machine-readable failures. Diagnostic text is never a protocol discriminator.

use shiguredo_http3::{Error, ErrorCode};

/// The smallest object whose operation must be stopped.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
#[repr(u32)]
pub enum Scope {
    #[default]
    None = 0,
    Operation = 1,
    Stream = 2,
    Session = 3,
    Connection = 4,
    Configuration = 5,
}

/// Stable adapter codes, independent of the upstream Rust enum layout.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
#[repr(u32)]
pub enum Code {
    #[default]
    None = 0,
    InvalidAbi = 1,
    InvalidArgument = 2,
    InvalidAuthority = 3,
    InvalidPath = 4,
    InvalidOrigin = 5,
    InvalidProtocol = 6,
    InvalidLimits = 7,
    InvalidTransportEvidence = 8,
    InvalidStreamId = 9,
    InvalidOperation = 10,
    WrongRole = 11,
    InvalidState = 12,
    UnknownStream = 13,
    UnknownSession = 14,
    LimitExceeded = 15,
    Backpressure = 16,
    FlowControlBlocked = 17,
    InvalidCompletion = 18,
    InvalidResetEvidence = 19,
    SendFailed = 20,
    PeerProtocol = 21,
    SettingsMismatch = 22,
    AuthorityDenied = 23,
    PathDenied = 24,
    OriginDenied = 25,
    ApplicationProtocolDenied = 26,
    UnsupportedDraft = 27,
    SessionLimit = 28,
    Panic = 29,
}

/// Fixed C layout. `protocol_code` is an HTTP3/WT wire code, or zero for local errors.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
#[repr(C)]
pub struct ErrorInfo {
    pub scope: u32,
    pub code: u32,
    pub protocol_code: u64,
    pub stream: u64,
    pub session: u64,
    pub message_len: u64,
    pub operation: u32,
    pub flags: u32,
}

/// Typed native failure; strings contain no request headers, identities, or user payload.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Failure {
    pub scope: Scope,
    pub code: Code,
    pub protocol_code: u64,
    pub stream: u64,
    pub session: u64,
    pub operation: u32,
    pub message: &'static str,
}

impl Failure {
    pub const fn new(scope: Scope, code: Code, message: &'static str) -> Self {
        Self {
            scope,
            code,
            protocol_code: 0,
            stream: u64::MAX,
            session: u64::MAX,
            operation: 0,
            message,
        }
    }

    pub const fn local(code: Code, message: &'static str) -> Self {
        Self::new(Scope::Operation, code, message)
    }

    pub const fn config(code: Code, message: &'static str) -> Self {
        Self::new(Scope::Configuration, code, message)
    }

    pub fn at(mut self, stream: u64, session: u64) -> Self {
        self.stream = stream;
        self.session = session;
        self
    }

    pub fn wire(mut self, code: u64) -> Self {
        self.protocol_code = code;
        self
    }

    pub fn info(&self) -> ErrorInfo {
        ErrorInfo {
            scope: self.scope as u32,
            code: self.code as u32,
            protocol_code: self.protocol_code,
            stream: self.stream,
            session: self.session,
            message_len: self.message.len() as u64,
            operation: self.operation,
            flags: if self.scope == Scope::Connection {
                2
            } else if matches!(self.code, Code::Backpressure | Code::FlowControlBlocked) {
                1
            } else {
                0
            },
        }
    }
}

impl From<Error> for Failure {
    fn from(error: Error) -> Self {
        match error {
            Error::ConnectionError(code) => {
                Self::new(Scope::Connection, Code::PeerProtocol, "HTTP3 connection error").wire(code.code())
            }
            Error::StreamError(code) => {
                Self::new(Scope::Stream, Code::PeerProtocol, "HTTP3 stream error").wire(code.code())
            }
            Error::StreamNotFound(id) => {
                Self::local(Code::UnknownStream, "unknown HTTP3 stream").at(id, u64::MAX)
            }
            Error::StreamClosed(id) => {
                Self::local(Code::InvalidState, "HTTP3 stream closed").at(id, u64::MAX)
            }
            Error::WtSessionDraining(id) => Self::new(
                Scope::Session,
                Code::InvalidState,
                "WebTransport session draining",
            )
            .at(id, id),
            Error::WtSetup(_) => {
                Self::local(Code::SettingsMismatch, "WebTransport prerequisites not satisfied")
            }
            Error::FrameDecode(_) => Self::new(
                Scope::Connection,
                Code::PeerProtocol,
                "HTTP3 frame decoding failed",
            )
            .wire(ErrorCode::FrameError.code()),
            Error::Qpack(_) => Self::new(
                Scope::Connection,
                Code::PeerProtocol,
                "HTTP3 QPACK decoding failed",
            )
            .wire(ErrorCode::QpackDecompressionFailed.code()),
        }
    }
}

impl std::fmt::Display for Failure {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(formatter, "{:?}/{:?}: {}", self.scope, self.code, self.message)
    }
}

impl std::error::Error for Failure {}

/// Only conversion/range failures use this helper; upstream protocol errors use `From<Error>`.
pub(crate) fn argument_error(_: impl std::fmt::Debug) -> Failure {
    Failure::local(Code::InvalidArgument, "invalid encoding or numeric range")
}
