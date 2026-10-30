/* Copyright (c) OPC Foundation. Licensed under the MIT License. */
#include "opcua_http3.h"

_Static_assert(sizeof(opcua_http3_transport_facts) == 80, "facts layout");
_Static_assert(sizeof(opcua_http3_event) == 48, "event layout");
_Static_assert(sizeof(opcua_http3_resource_snapshot_data) == 64, "resource layout");
_Static_assert(sizeof(opcua_http3_config) == 224, "config layout");
_Static_assert(sizeof(opcua_http3_error) == 48, "error layout");
_Static_assert(sizeof(opcua_http3_layout_data) == 40, "layout discovery");
_Static_assert(offsetof(opcua_http3_config, limits) == 128, "config limits offset");
_Static_assert(offsetof(opcua_http3_config, authority) == 16, "config string offset");
_Static_assert(offsetof(opcua_http3_error, operation) == 40, "error operation offset");
_Static_assert(OPCUA_HTTP3_INVALID_RESET_EVIDENCE == 19, "typed reset code");
_Static_assert(OPCUA_HTTP3_SCOPE_CONNECTION == 4, "typed connection scope");

int main(void)
{
    return 0;
}
