// Copyright (c) OPC Foundation. Licensed under the MIT License.
//! Versioned C ABI; caller-owned buffers, serialized handles and no callbacks.

use std::panic::{AssertUnwindSafe, catch_unwind};
use std::ptr;

use crate::config::{Config, Policy};
use crate::error::{Code, ErrorInfo, Failure, Scope};
use crate::{Peer, ResourceSnapshot, TransportFacts};

/// Compatible 48-byte event metadata; payload is copied separately.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
#[repr(C)]
pub struct EventHeader {
    pub kind: u32,
    pub flags: u32,
    pub stream: u64,
    pub session: u64,
    pub arg: u64,
    pub aux: u64,
    pub data_len: u64,
}

/// Sizes for fail-closed NativeAOT/FFI startup checks.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
#[repr(C)]
pub struct Layout {
    pub struct_size: u32,
    pub abi_version: u32,
    pub api_revision: u32,
    pub pointer_size: u32,
    pub facts_size: u32,
    pub config_size: u32,
    pub event_size: u32,
    pub error_size: u32,
    pub resource_size: u32,
    pub reserved: u32,
}

#[unsafe(no_mangle)]
pub extern "C" fn opcua_http3_abi_version() -> u32 {
    1
}

#[unsafe(no_mangle)]
pub extern "C" fn opcua_http3_api_revision() -> u32 {
    2
}

/// # Safety
/// `out` must be aligned and writable for `size` bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn opcua_http3_layout(out: *mut Layout, size: usize) -> i32 {
    if out.is_null() || size != size_of::<Layout>() {
        return -1;
    }
    unsafe {
        *out = Layout {
            struct_size: size_of::<Layout>() as u32,
            abi_version: 1,
            api_revision: 2,
            pointer_size: size_of::<usize>() as u32,
            facts_size: size_of::<TransportFacts>() as u32,
            config_size: size_of::<Config>() as u32,
            event_size: size_of::<EventHeader>() as u32,
            error_size: size_of::<ErrorInfo>() as u32,
            resource_size: size_of::<ResourceSnapshot>() as u32,
            reserved: 0,
        };
    }
    0
}

/// # Safety
/// Typed pointers must be valid/aligned; nonempty config strings must be readable.
/// The out handle and error must not overlap input. All memory is caller-owned.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn opcua_http3_create(
    facts: *const TransportFacts,
    config: *const Config,
    out: *mut *mut Peer,
    error: *mut ErrorInfo,
) -> i32 {
    if out.is_null() || error.is_null() {
        return -1;
    }
    unsafe {
        *out = ptr::null_mut();
        *error = ErrorInfo::default();
    }
    if facts.is_null() || config.is_null() {
        unsafe { *error = Failure::config(Code::InvalidArgument, "null creation input").info() };
        return -1;
    }
    match catch_unwind(AssertUnwindSafe(|| {
        let facts = unsafe { *facts };
        let policy = unsafe { Policy::from_config(&*config, &facts)? };
        Peer::new(facts, policy)
    })) {
        Ok(Ok(peer)) => {
            unsafe { *out = Box::into_raw(Box::new(peer)) };
            0
        }
        Ok(Err(failure)) => {
            unsafe { *error = failure.info() };
            -2
        }
        Err(_) => {
            unsafe { *error = panic_error().info() };
            -3
        }
    }
}

/// # Safety
/// Handle is live, exclusively borrowed and non-reentrant; input readable for length.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn opcua_http3_command(
    handle: *mut Peer,
    operation: u32,
    id: u64,
    arg: u64,
    aux: u64,
    data: *const u8,
    length: usize,
) -> i32 {
    if handle.is_null() {
        return -1;
    }
    let peer = unsafe { &mut *handle };
    if (data.is_null() && length != 0) || length > 1024 * 1024 {
        peer.last_error = Some(Failure::local(
            Code::InvalidArgument,
            "invalid input length or pointer",
        ));
        return -1;
    }
    let bytes = if length == 0 {
        &[]
    } else {
        unsafe { std::slice::from_raw_parts(data, length) }
    };
    match catch_unwind(AssertUnwindSafe(|| peer.command(operation, id, arg, aux, bytes))) {
        Ok(Ok(())) => 0,
        Ok(Err(_)) => -2,
        Err(_) => {
            poison(peer);
            -3
        }
    }
}

/// Return 1 for an event, 0 for empty, 2 for capacity retry (event not consumed).
/// Successful send polling leases the token; it is not successful transport completion.
///
/// # Safety
/// Exclusive handle and writable metadata; nonzero capacity requires writable buffer.
/// The output memory must not alias the handle, metadata, or any library storage.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn opcua_http3_poll(
    handle: *mut Peer,
    event: *mut EventHeader,
    buffer: *mut u8,
    capacity: usize,
) -> i32 {
    if handle.is_null() || event.is_null() || (buffer.is_null() && capacity != 0) {
        return -1;
    }
    let peer = unsafe { &mut *handle };
    match catch_unwind(AssertUnwindSafe(|| {
        let Some(output) = peer.output() else {
            unsafe { *event = EventHeader::default() };
            return 0;
        };
        unsafe {
            *event = EventHeader {
                kind: output.kind,
                flags: output.flags,
                stream: output.stream,
                session: output.session,
                arg: output.arg,
                aux: output.aux,
                data_len: output.data.len() as u64,
            };
        }
        if capacity < output.data.len() {
            return 2;
        }
        if !output.data.is_empty() {
            unsafe { ptr::copy_nonoverlapping(output.data.as_ptr(), buffer, output.data.len()) };
        }
        peer.pop_output();
        1
    })) {
        Ok(result) => result,
        Err(_) => {
            poison(peer);
            -3
        }
    }
}

/// # Safety
/// Live serialized handle, writable out and optional writable diagnostic buffer.
/// Return 2 when text capacity is insufficient; typed error and required length are always written.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn opcua_http3_last_error(
    handle: *const Peer,
    out: *mut ErrorInfo,
    buffer: *mut u8,
    capacity: usize,
) -> i32 {
    if handle.is_null() || out.is_null() || (buffer.is_null() && capacity != 0) {
        return -1;
    }
    let Some(error) = (unsafe { &*handle }).last_error.as_ref() else {
        unsafe { *out = ErrorInfo::default() };
        return 0;
    };
    unsafe { *out = error.info() };
    if capacity < error.message.len() {
        return 2;
    }
    if !error.message.is_empty() {
        unsafe { ptr::copy_nonoverlapping(error.message.as_ptr(), buffer, error.message.len()) };
    }
    1
}

/// # Safety
/// Live serialized handle and writable output.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn opcua_http3_resource_snapshot(
    handle: *const Peer,
    out: *mut ResourceSnapshot,
) -> i32 {
    if handle.is_null() || out.is_null() {
        return -1;
    }
    unsafe { *out = (*handle).resource_snapshot() };
    0
}

/// # Safety
/// Destroy exactly once, after native calls and transport operations finish. Null is accepted.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn opcua_http3_destroy(handle: *mut Peer) {
    if !handle.is_null() {
        drop(unsafe { Box::from_raw(handle) });
    }
}

fn panic_error() -> Failure {
    Failure::new(
        Scope::Connection,
        Code::Panic,
        "native panic contained; destroy handle",
    )
}

fn poison(peer: &mut Peer) {
    let error = panic_error();
    peer.fatal = Some(error.clone());
    peer.last_error = Some(error);
}
