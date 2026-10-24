// Copyright (c) OPC Foundation. Licensed under the MIT License.
//! Local resource-admission patch; the upstream protocol decoder is unchanged.

use crate::{Error, ErrorCode};

use super::Connection;

/// Retained capsule storage per session. Exceeding policy is excessive load, not malformed syntax.
pub const MAX_WT_CAPSULE_BYTES_PER_SESSION: usize = 64 * 1024;
/// Retained capsule storage across all pending, established, and draining sessions.
pub const MAX_WT_CAPSULE_BYTES_PER_CONNECTION: usize = 128 * 1024;
/// Retained HTTP request framing/body storage per stream.
pub const MAX_REQUEST_BUFFER_BYTES_PER_STREAM: usize = 64 * 1024;
/// Retained HTTP request framing/body storage across the connection.
pub const MAX_REQUEST_BUFFER_BYTES_PER_CONNECTION: usize = 128 * 1024;

impl Connection {
    /// Returns allocated capsule buffer capacity, not merely live lengths.
    pub fn wt_buffered_capsule_bytes(&self) -> usize {
        self.wt_sessions
            .values()
            .map(|session| session.capsule_buf.capacity())
            .sum()
    }

    /// Returns allocated request-parser/body capacities, including deferred CONNECT bodies.
    pub fn buffered_request_bytes(&self) -> usize {
        self.streams.values().map(|stream| stream.buffered_receive_bytes()).sum()
    }

    /// Rejects one stream and releases its retained buffers without poisoning other sessions.
    pub(crate) fn reject_receive_buffer(
        &mut self,
        stream_id: u64,
        code: ErrorCode,
    ) -> Result<(), Error> {
        self.terminate_wt_session(stream_id);
        self.deferred_wt_connects.remove(&stream_id);
        self.streams.remove(&stream_id);
        Err(Error::StreamError(code))
    }

    /// Reserves capsule storage before copying bytes into the decoder.
    pub(crate) fn append_wt_capsule_data(
        &mut self,
        session_id: u64,
        data: &[u8],
    ) -> Result<(), Error> {
        let Some(session) = self.wt_sessions.get(&session_id) else {
            return Ok(());
        };
        let desired = session.capsule_buf.len().checked_add(data.len());
        let Some(desired) = desired else {
            return self.reject_receive_buffer(session_id, ErrorCode::ExcessiveLoad);
        };
        let additional = desired.saturating_sub(session.capsule_buf.capacity());
        if desired > MAX_WT_CAPSULE_BYTES_PER_SESSION
            || additional > MAX_WT_CAPSULE_BYTES_PER_CONNECTION
                .saturating_sub(self.wt_buffered_capsule_bytes())
        {
            return self.reject_receive_buffer(session_id, ErrorCode::ExcessiveLoad);
        }
        let session = self.wt_sessions.get_mut(&session_id).expect("session retained");
        session.capsule_buf.reserve_exact(data.len());
        session.capsule_buf.extend_from_slice(data);
        Ok(())
    }

    /// Bounds incomplete HTTP DATA/HEADERS framing too, before the capsule layer is reached.
    pub(crate) fn admit_request_data(&mut self, stream_id: u64, bytes: usize) -> Result<(), Error> {
        let current = self.streams.get(&stream_id).map_or(0, |s| s.buffered_receive_bytes());
        if bytes > MAX_REQUEST_BUFFER_BYTES_PER_STREAM.saturating_sub(current)
            || bytes > MAX_REQUEST_BUFFER_BYTES_PER_CONNECTION.saturating_sub(self.buffered_request_bytes())
        {
            return self.reject_receive_buffer(stream_id, ErrorCode::ExcessiveLoad);
        }
        Ok(())
    }
}
