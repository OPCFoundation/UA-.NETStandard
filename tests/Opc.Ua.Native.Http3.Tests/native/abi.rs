// Copyright (c) OPC Foundation. Licensed under the MIT License.
//! Direct production ABI tests; pointer failure cases never pass unreadable accepted memory.

use opcua_http3::config::{Config, Utf8};
use opcua_http3::error::{Code, ErrorInfo, Scope};
use opcua_http3::ffi::*;
use opcua_http3::{ResourceSnapshot, TransportFacts};

pub mod support;
use support::{configuration, facts};

#[test]
fn ffi_buffers_are_caller_owned_retryable_and_checked() {
    let config = configuration(0);
    let mut error = ErrorInfo::default();
    let mut handle = std::ptr::null_mut();
    unsafe {
        assert_eq!(
            opcua_http3_create(std::ptr::null(), &config, &mut handle, &mut error),
            -1
        );
        assert!(handle.is_null());
        assert_eq!(error.code, Code::InvalidArgument as u32);
        assert_eq!(
            opcua_http3_create(&facts(0), &config, &mut handle, &mut error),
            0
        );
        assert!(!handle.is_null());
        assert_eq!(opcua_http3_abi_version(), 1);
        assert_eq!(opcua_http3_api_revision(), 2);
        let mut resources = ResourceSnapshot::default();
        assert_eq!(opcua_http3_resource_snapshot(handle, &mut resources), 0);
        assert_eq!(
            (
                resources.revision,
                resources.pending_sends,
                resources.leased_bytes
            ),
            (2, 3, 0)
        );
        let before = resources;
        let mut header = EventHeader::default();
        assert_eq!(
            opcua_http3_poll(handle, &mut header, std::ptr::null_mut(), 0),
            2
        );
        let expected = header;
        assert!(header.data_len > 0);
        let mut short = vec![0x7a; header.data_len as usize - 1];
        assert_eq!(
            opcua_http3_poll(handle, &mut header, short.as_mut_ptr(), short.len()),
            2
        );
        assert_eq!(short, vec![0x7a; short.len()]);
        assert_eq!(header, expected);
        opcua_http3_resource_snapshot(handle, &mut resources);
        assert_eq!(resources, before);
        let mut buffer = vec![0; header.data_len as usize];
        assert_eq!(
            opcua_http3_poll(handle, &mut header, buffer.as_mut_ptr(), buffer.len()),
            1
        );
        assert_eq!(header, expected);
        opcua_http3_resource_snapshot(handle, &mut resources);
        assert_eq!(resources.leased_bytes, header.data_len);
        assert!(
            buffer
                .windows(5)
                .any(|window| window == [0xac, 0x7c, 0xf0, 0, 1])
        );
        assert_eq!(
            opcua_http3_command(
                handle,
                20,
                header.arg,
                header.data_len,
                1,
                std::ptr::null(),
                0
            ),
            0
        );
        buffer.fill(0xff);
        opcua_http3_resource_snapshot(handle, &mut resources);
        assert_eq!(resources.leased_bytes, 0);
        assert_eq!(resources.pending_sends, 2);
        assert_eq!(
            opcua_http3_command(handle, 999, 0, 0, 0, std::ptr::null(), 0),
            -2
        );
        assert_eq!(
            opcua_http3_last_error(handle, &mut error, std::ptr::null_mut(), 0),
            2
        );
        assert_eq!(
            (error.code, error.scope, error.operation),
            (Code::InvalidOperation as u32, Scope::Operation as u32, 999)
        );
        let mut diagnostic = vec![0; error.message_len as usize];
        assert_eq!(
            opcua_http3_last_error(
                handle,
                &mut error,
                diagnostic.as_mut_ptr(),
                diagnostic.len()
            ),
            1
        );
        assert_eq!(diagnostic, b"unknown operation");
        opcua_http3_destroy(handle);
        opcua_http3_destroy(std::ptr::null_mut());
    }
}

#[test]
fn layout_query_matches_blittable_sizes_and_rejects_mismatches() {
    let mut layout = Layout::default();
    unsafe {
        assert_eq!(opcua_http3_layout(&mut layout, size_of::<Layout>() - 1), -1);
        assert_eq!(layout, Layout::default());
        assert_eq!(
            opcua_http3_layout(std::ptr::null_mut(), size_of::<Layout>()),
            -1
        );
        assert_eq!(opcua_http3_layout(&mut layout, size_of::<Layout>()), 0);
    }
    assert_eq!(
        (layout.abi_version, layout.api_revision, layout.pointer_size),
        (1, 2, 8)
    );
    assert_eq!(size_of::<TransportFacts>(), 80);
    assert_eq!(size_of::<EventHeader>(), 48);
    assert_eq!(size_of::<ResourceSnapshot>(), 64);
    assert_eq!(size_of::<ErrorInfo>(), 48);
    assert_eq!(size_of::<Config>(), 224);
    assert_eq!(
        (
            layout.facts_size,
            layout.event_size,
            layout.resource_size,
            layout.error_size,
            layout.config_size
        ),
        (80, 48, 64, 48, 224)
    );
}

#[test]
fn create_returns_typed_policy_errors_without_a_handle_or_secret_diagnostic() {
    let mut config = configuration(0);
    config.authority = Utf8::borrowed("credential@not-a-host");
    let mut error = ErrorInfo::default();
    let mut handle = std::ptr::dangling_mut();
    unsafe {
        assert_eq!(
            opcua_http3_create(&facts(0), &config, &mut handle, &mut error),
            -2
        );
    }
    assert!(handle.is_null());
    assert_eq!(
        (error.scope, error.code),
        (Scope::Configuration as u32, Code::InvalidAuthority as u32)
    );
    assert_eq!((error.stream, error.session), (u64::MAX, u64::MAX));
}

#[test]
fn null_pointer_and_oversized_input_failures_leave_live_state_unchanged() {
    let mut handle = std::ptr::null_mut();
    let mut error = ErrorInfo::default();
    unsafe {
        assert_eq!(
            opcua_http3_create(&facts(0), &configuration(0), &mut handle, &mut error),
            0
        );
        let mut before = ResourceSnapshot::default();
        opcua_http3_resource_snapshot(handle, &mut before);
        assert_eq!(
            opcua_http3_command(handle, 1, 3, 0, 0, std::ptr::null(), 1),
            -1
        );
        assert_eq!(
            opcua_http3_command(handle, 1, 3, 0, 0, std::ptr::dangling(), 1048577),
            -1
        );
        assert_eq!(
            opcua_http3_poll(handle, std::ptr::null_mut(), std::ptr::null_mut(), 0),
            -1
        );
        assert_eq!(
            opcua_http3_poll(handle, &mut EventHeader::default(), std::ptr::null_mut(), 1),
            -1
        );
        assert_eq!(
            opcua_http3_resource_snapshot(handle, std::ptr::null_mut()),
            -1
        );
        let mut after = ResourceSnapshot::default();
        opcua_http3_resource_snapshot(handle, &mut after);
        assert_eq!(before, after);
        opcua_http3_destroy(handle);
    }
}
