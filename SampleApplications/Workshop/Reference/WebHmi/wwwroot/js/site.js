// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

const ReadRequestId = 629;
const ReadResponseId = 632;
const WriteRequestId = 671;
const WriteResponseId = 674;
const CallRequestId = 710;
const CallResponseId = 713;
const ServiceFaultId = 395;

const ValueAttributeId = 13;
const DataTypeAttributeId = 14;
const ValueRankAttributeId = 15;

var socket;
var nextRequestId = 0;
var authenticationToken;
var sessionId;

// This function creates a new request object for use with a session.
function createRequest(typeId) {
    var request = {};
    request.TypeId = typeId;

    request.Body = {};
    request.Body.RequestHeader = {};
    request.Body.RequestHeader.Timestamp = new Date().toISOString();
    request.Body.RequestHeader.RequestHandle = ++nextRequestId;
    request.Body.RequestHeader.TimeoutHint = 30000;
    request.Body.RequestHeader.ReturnDiagnostics = 2; // return text associated with service level errors.
    request.Body.RequestHeader.AuthenticationToken = authenticationToken;

    return request;
}

// This function converts a Part 6 NodeId string to JSON.
function parseNodeId(nodeId) {

    var namespaceIndex = 0;
    var idType = 0;
    var id = 0;

    var elements = nodeId.split(';');

    if (!elements.length) {
        return undefined;
    }

    var element = elements[0];

    var pos = element.indexOf('ns=');

    if (pos == 0) {
        namespaceIndex = element.substring(3);

        if (!$.isNumeric(namespaceIndex)) {
            return undefined;
        }

        if (elements.length < 2) {
            return undefined;
        }

        element = elements[1];
    }

    pos = element.indexOf('i=');

    if (pos == 0) {
        id = element.substring(2);

        if (!$.isNumeric(id)) {
            return undefined;
        }

        return JSON.parse('{"Id":' + id + ',"Namespace":' + namespaceIndex + '}');
    }

    pos = element.indexOf('s=');

    if (pos == 0) {
        id = element.substring(2);
        return JSON.parse('{"IdType":1,"Id":"' + id + '","Namespace":' + namespaceIndex + '}');
    }

    return undefined;
}

// reads the specified attribute from the server.
function readAttribute(nodeId, attributeId, accessToken, callback) {

    // construct the JSON that will be sent as the call request.
    var request = {};
    request.ServiceId = ReadRequestId;

    request.Body = {};
    request.Body.RequestHeader = {};
    request.Body.RequestHeader.Timestamp = new Date().toISOString();
    request.Body.RequestHeader.RequestHandle = ++nextRequestId;
    request.Body.RequestHeader.TimeoutHint = 30000;
    request.Body.RequestHeader.ReturnDiagnostics = 2; // return text associated with service level errors.
    request.Body.RequestHeader.AuthenticationToken = null;

    request.Body.MaxAge = 0; // read from device always
    request.Body.TimestampsToReturn = 0; // source only

    var pnid = parseNodeId(nodeId);

    if (!pnid) {
        console.log("[Read InvalidNodeId] " + nodeId);
        callback(undefined);
        return;
    }

    var nodeToRead = {};
    nodeToRead.NodeId = pnid;
    nodeToRead.AttributeId = parseInt(attributeId);

    request.Body.NodesToRead = [];
    request.Body.NodesToRead.push(nodeToRead);

    var data = JSON.stringify(request, null, 4);

    // post the JSON to the server and update controls with the read response.
    $.ajax("/Home/Invoke/", {
        type: "POST",
        data: data,
        contentType: "application/json; charset=utf-8",
        beforeSend: function (xhr) {
            if (accessToken) {
                xhr.setRequestHeader("Authorization", "Bearer " + accessToken);
            }
        }
    })
    .done(function (response, textStatus, jqXHR) {

        if (response.ServiceId === ReadResponseId) {
            var results = response.Body.Results;

            if (results.length) {
                if (!results[0].StatusCode || results[0].StatusCode === 0) {
                    callback(results[0].Value);
                    return;
                }
                else {
                    console.log("[Read Error] " + getErrorString(results[0].StatusCode));
                    callback(undefined);
                    return;
                }
            }
        }
        else if (response.ServiceId === ServiceFaultId) {
            console.log("[Read Error] " + getErrorString(response.Body.ResponseHeader.ServiceResult));
            callback(undefined);
        }
        else {
            target.val("Unknown ResponseType: " + response.ServiceId);
            callback(undefined);
        }
    })
    .fail(function (jqxhr, settings, error) {

        console.log("[Read Server Failure] " + error);
        callback(undefined);
        return;
    });
}

function getErrorString(result) {

    if (result || result === 0) {
        switch (result) {
            case 0: { return "Good"; }
            case 2155085824: { return "BadTypeMismatch"; }
            case 2149646336: { return "BadIdentityTokenRejected"; }
            default: { return "0x" + result.toString(16); }
        }
    }
    else {
        return "Unknown Error";
    }
}