// Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
// OPC Foundation MIT License 1.00 - http://opcfoundation.org/License/MIT/1.00/

// The event, subscription, identity and service checks of the gopcua client
// against the 2.0 reference server (Alarms, TestData and history node
// managers); a port of tests/Opc.Ua.Interop.LegacyPeer/LegacyClientFeatures.cs.
package main

import (
	"context"
	"crypto/rand"
	"crypto/rsa"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/hex"
	"errors"
	"fmt"
	"math/big"
	"strings"
	"sync"
	"time"

	"github.com/gopcua/opcua"
	"github.com/gopcua/opcua/id"
	"github.com/gopcua/opcua/ua"
)

const alarmsNamespace = "http://test.org/UA/Alarms/"

const eventWait = 10 * time.Second

// ensureConnected replaces the client when a failed check left it unusable.
func (x *ctx) ensureConnected(c context.Context) {
	if x.c != nil && x.c.State() == opcua.Connected {
		if values, err := x.read(c, valueID(ua.NewNumericNodeID(0, id.Server_ServerStatus_State))); err == nil &&
			values[0].Status == ua.StatusOK {
			return
		}
	}
	if x.c != nil {
		_ = x.c.Close(context.Background())
	}
	if cl, err := x.reconnect(c); err == nil {
		x.c = cl
	} else {
		fmt.Println("INFO reconnect after a failed check failed: " + err.Error())
	}
}

func statusOf(err error) ua.StatusCode {
	var sc ua.StatusCode
	if errors.As(err, &sc) {
		return sc
	}
	return ua.StatusBad
}

// ------------------------------------------------------------------ subscriptions

// recorder is a subscription with the notifications it has received.
type recorder struct {
	sub      *opcua.Subscription
	mu       sync.Mutex
	data     []*ua.MonitoredItemNotification
	events   []*ua.EventFieldList
	messages int
}

func (x *ctx) subscribe(c context.Context, interval time.Duration) (*recorder, error) {
	ch := make(chan *opcua.PublishNotificationData, 256)
	sub, err := x.c.Subscribe(c, &opcua.SubscriptionParameters{Interval: interval, LifetimeCount: 100,
		MaxKeepAliveCount: 10}, ch)
	if err != nil {
		return nil, err
	}
	r := &recorder{sub: sub}
	// Drains for the lifetime of the process: gopcua blocks its publish loop
	// on a full notification channel.
	go func() {
		for n := range ch {
			r.mu.Lock()
			switch v := n.Value.(type) {
			case *ua.DataChangeNotification:
				r.messages++
				r.data = append(r.data, v.MonitoredItems...)
			case *ua.EventNotificationList:
				r.messages++
				r.events = append(r.events, v.Events...)
			}
			r.mu.Unlock()
		}
	}()
	return r, nil
}

func (r *recorder) cancel() {
	if r != nil {
		c, done := cleanupContext()
		defer done()
		// Subscription.Cancel can block beyond its context while the publish
		// loop is paused (e.g. after the subscription was transferred away).
		finished := make(chan struct{})
		go func() {
			_ = r.sub.Cancel(c)
			close(finished)
		}()
		select {
		case <-finished:
		case <-c.Done():
		}
	}
}

// cleanupContext bounds a deferred cleanup call, so a cleanup that the server
// never answers cannot stall the next checks.
func cleanupContext() (context.Context, context.CancelFunc) {
	return context.WithTimeout(context.Background(), 10*time.Second)
}

func (r *recorder) dataOf(handle uint32) []*ua.DataValue {
	r.mu.Lock()
	defer r.mu.Unlock()
	var out []*ua.DataValue
	for _, n := range r.data {
		if n.ClientHandle == handle {
			out = append(out, n.Value)
		}
	}
	return out
}

func (r *recorder) clear() {
	r.mu.Lock()
	r.data = nil
	r.events = nil
	r.mu.Unlock()
}

func (r *recorder) waitEvent(timeout time.Duration, match func([]*ua.Variant) bool) []*ua.Variant {
	end := time.Now().Add(timeout)
	for time.Now().Before(end) {
		r.mu.Lock()
		for _, e := range r.events {
			if match(e.EventFields) {
				r.mu.Unlock()
				return e.EventFields
			}
		}
		r.mu.Unlock()
		time.Sleep(100 * time.Millisecond)
	}
	return nil
}

func (r *recorder) monitor(c context.Context, items ...*ua.MonitoredItemCreateRequest) ([]uint32, error) {
	res, err := r.sub.Monitor(c, ua.TimestampsToReturnBoth, items...)
	if err != nil {
		return nil, err
	}
	ids := []uint32{}
	for _, it := range res.Results {
		if it.StatusCode != ua.StatusOK {
			return nil, fmt.Errorf("monitored item %s", it.StatusCode.Error())
		}
		ids = append(ids, it.MonitoredItemID)
	}
	return ids, nil
}

func dataItem(n *ua.NodeID, handle uint32) *ua.MonitoredItemCreateRequest {
	item := opcua.NewMonitoredItemCreateRequestWithDefaults(n, ua.AttributeIDValue, handle)
	item.RequestedParameters.SamplingInterval = 50
	item.RequestedParameters.QueueSize = 10
	return item
}

func selectClause(typeID uint32, attr ua.AttributeID, path ...string) *ua.SimpleAttributeOperand {
	names := []*ua.QualifiedName{}
	for _, p := range path {
		names = append(names, &ua.QualifiedName{Name: p})
	}
	return &ua.SimpleAttributeOperand{TypeDefinitionID: ua.NewNumericNodeID(0, typeID), BrowsePath: names,
		AttributeID: attr}
}

// eventSubscription subscribes to events of a notifier. Fields: 0 EventId,
// 1 EventType, 2 SourceNode, 3 Time, 4 Message, 5 Severity and with
// condition 6 ConditionId, 7 AckedState/Id, 8 Retain.
func (x *ctx) eventSubscription(c context.Context, notifier *ua.NodeID, condition bool) (*recorder, error) {
	r, err := x.subscribe(c, 100*time.Millisecond)
	if err != nil {
		return nil, err
	}
	filter := &ua.EventFilter{}
	for _, f := range []string{"EventId", "EventType", "SourceNode", "Time", "Message", "Severity"} {
		filter.SelectClauses = append(filter.SelectClauses, selectClause(id.BaseEventType, ua.AttributeIDValue, f))
	}
	ofType := uint32(id.BaseEventType)
	if condition {
		ofType = id.AcknowledgeableConditionType
		filter.SelectClauses = append(filter.SelectClauses,
			selectClause(id.ConditionType, ua.AttributeIDNodeID),
			selectClause(id.AcknowledgeableConditionType, ua.AttributeIDValue, "AckedState", "Id"),
			selectClause(id.ConditionType, ua.AttributeIDValue, "Retain"))
	}
	filter.WhereClause = &ua.ContentFilter{Elements: []*ua.ContentFilterElement{{
		FilterOperator: ua.FilterOperatorOfType,
		FilterOperands: []*ua.ExtensionObject{ua.NewExtensionObject(&ua.LiteralOperand{
			Value: ua.MustVariant(ua.NewNumericNodeID(0, ofType))})},
	}}}
	item := opcua.NewMonitoredItemCreateRequestWithDefaults(notifier, ua.AttributeIDEventNotifier, 1)
	item.RequestedParameters.QueueSize = 100
	item.RequestedParameters.Filter = ua.NewExtensionObject(filter)
	if _, err := r.monitor(c, item); err != nil {
		r.cancel()
		return nil, fmt.Errorf("event monitored item: %w", err)
	}
	return r, nil
}

func (x *ctx) call(c context.Context, object, method *ua.NodeID, args ...*ua.Variant) (*ua.CallMethodResult, error) {
	return x.c.Call(c, &ua.CallMethodRequest{ObjectID: object, MethodID: method, InputArguments: args})
}

func (x *ctx) writeValue(c context.Context, n *ua.NodeID, value any) error {
	status, err := x.write(c, n, ua.MustVariant(value))
	if err != nil {
		return err
	}
	if status != ua.StatusOK {
		return fmt.Errorf("write of %s returned %s", n, status.Error())
	}
	return nil
}

func checkEventSubscription(c context.Context, x *ctx) error {
	r, err := x.eventSubscription(c, ua.NewNumericNodeID(0, id.Server), false)
	if err != nil {
		return err
	}
	defer r.cancel()
	if err := x.writeValue(c, x.id("NodeIds_Events_TriggerNode01"), int32(1)); err != nil {
		return err
	}
	e := r.waitEvent(eventWait, func(f []*ua.Variant) bool {
		m, _ := f[4].Value().(*ua.LocalizedText)
		return m != nil && strings.Contains(m.Text, "Trigger event")
	})
	if e == nil {
		r.mu.Lock()
		defer r.mu.Unlock()
		return fmt.Errorf("no trigger event in 10 s; received %d events", len(r.events))
	}
	if b, _ := e[0].Value().([]byte); len(b) == 0 {
		return errors.New("the event has no EventId")
	}
	if s, _ := e[5].Value().(uint16); s == 0 {
		return fmt.Errorf("the event has no Severity (%v)", e[5].Value())
	}
	return nil
}

func isType(v *ua.Variant, typeID uint32) bool {
	n, _ := v.Value().(*ua.NodeID)
	return n != nil && n.Namespace() == 0 && n.IntID() == typeID
}

func checkConditionRefresh(c context.Context, x *ctx) error {
	r, err := x.eventSubscription(c, ua.NewNumericNodeID(0, id.Server), false)
	if err != nil {
		return err
	}
	defer r.cancel()
	res, err := x.call(c, ua.NewNumericNodeID(0, id.ConditionType), ua.NewNumericNodeID(0, id.ConditionType_ConditionRefresh),
		ua.MustVariant(r.sub.SubscriptionID))
	if err != nil {
		return err
	}
	if res.StatusCode != ua.StatusOK {
		return fmt.Errorf("ConditionRefresh returned %s", res.StatusCode.Error())
	}
	if r.waitEvent(eventWait, func(f []*ua.Variant) bool { return isType(f[1], id.RefreshEndEventType) }) == nil {
		return errors.New("no RefreshEndEvent received")
	}
	if r.waitEvent(0, func(f []*ua.Variant) bool { return isType(f[1], id.RefreshStartEventType) }) == nil &&
		r.waitEvent(100*time.Millisecond, func(f []*ua.Variant) bool { return isType(f[1], id.RefreshStartEventType) }) == nil {
		return errors.New("no RefreshStartEvent received")
	}
	return nil
}

func checkAlarmAcknowledge(c context.Context, x *ctx) error {
	alarmsNs, err := x.c.FindNamespace(c, alarmsNamespace)
	if err != nil {
		return fmt.Errorf("the Alarms namespace is missing: %w", err)
	}
	folder := ua.NewStringNodeID(alarmsNs, "Alarms")
	r, err := x.eventSubscription(c, folder, true)
	if err != nil {
		return err
	}
	defer r.cancel()
	defer func() { _, _ = x.call(context.Background(), folder, ua.NewStringNodeID(alarmsNs, "Alarms.End")) }()
	started, err := x.call(c, folder, ua.NewStringNodeID(alarmsNs, "Alarms.Start"), ua.MustVariant(uint32(30)))
	if err != nil {
		return err
	}
	if started.StatusCode != ua.StatusOK {
		return fmt.Errorf("Alarms.Start returned %s", started.StatusCode.Error())
	}
	e := r.waitEvent(20*time.Second, func(f []*ua.Variant) bool {
		cond, _ := f[6].Value().(*ua.NodeID)
		acked, ok1 := f[7].Value().(bool)
		retain, ok2 := f[8].Value().(bool)
		return cond != nil && ok1 && !acked && ok2 && retain
	})
	if e == nil {
		r.mu.Lock()
		defer r.mu.Unlock()
		last := "none"
		if len(r.events) > 0 {
			parts := []string{}
			for _, f := range r.events[len(r.events)-1].EventFields {
				parts = append(parts, fmt.Sprintf("%T:%v", f.Value(), f.Value()))
			}
			last = strings.Join(parts, " | ")
		}
		return fmt.Errorf("no unacknowledged condition in 20 s; received %d events, last: %s", len(r.events), last)
	}
	eventID, _ := e[0].Value().([]byte)
	ack, err := x.call(c, e[6].Value().(*ua.NodeID), ua.NewNumericNodeID(0, id.AcknowledgeableConditionType_Acknowledge),
		ua.MustVariant(eventID), ua.MustVariant(ua.NewLocalizedText("acknowledged by the interop test")))
	if err != nil {
		return err
	}
	if ack.StatusCode != ua.StatusOK {
		return fmt.Errorf("Acknowledge returned %s", ack.StatusCode.Error())
	}
	return nil
}

func checkDeadbandFilter(c context.Context, x *ctx) error {
	analog := x.id("DataAccess_AnalogType_Double")
	for _, t := range []ua.DeadbandType{ua.DeadbandTypeAbsolute, ua.DeadbandTypePercent} {
		if err := x.writeValue(c, analog, 0.0); err != nil {
			return err
		}
		err := func() error {
			r, err := x.subscribe(c, 100*time.Millisecond)
			if err != nil {
				return err
			}
			defer r.cancel()
			item := dataItem(analog, 1)
			item.RequestedParameters.Filter = ua.NewExtensionObject(&ua.DataChangeFilter{
				Trigger: ua.DataChangeTriggerStatusValue, DeadbandType: uint32(t), DeadbandValue: 10})
			if _, err := r.monitor(c, item); err != nil {
				return err
			}
			time.Sleep(500 * time.Millisecond)
			for _, v := range []float64{5, 20, 25, 40} {
				if err := x.writeValue(c, analog, v); err != nil {
					return err
				}
				time.Sleep(400 * time.Millisecond)
			}
			time.Sleep(800 * time.Millisecond)
			seen := map[float64]bool{}
			list := []string{}
			for _, dv := range r.dataOf(1) {
				if dv.Value != nil {
					if f, ok := dv.Value.Value().(float64); ok {
						seen[f] = true
						list = append(list, fmt.Sprint(f))
					}
				}
			}
			if !seen[20] || !seen[40] {
				return fmt.Errorf("%s: 20 and 40 not reported (%s)", t, strings.Join(list, ", "))
			}
			if seen[5] || seen[25] {
				return fmt.Errorf("%s: changes inside the deadband reported (%s)", t, strings.Join(list, ", "))
			}
			return nil
		}()
		if err != nil {
			return err
		}
	}
	return nil
}

func checkQueueOverflow(c context.Context, x *ctx) error {
	node := x.id("Scalar_Static_Int32")
	if err := x.writeValue(c, node, int32(0)); err != nil {
		return err
	}
	r, err := x.subscribe(c, 2000*time.Millisecond)
	if err != nil {
		return err
	}
	defer r.cancel()
	item := dataItem(node, 1)
	item.RequestedParameters.SamplingInterval = 0
	item.RequestedParameters.QueueSize = 2
	item.RequestedParameters.DiscardOldest = true
	if _, err := r.monitor(c, item); err != nil {
		return err
	}
	// Let the initial value go out first.
	time.Sleep(2500 * time.Millisecond)
	r.clear()
	for i := int32(1); i <= 5; i++ {
		if err := x.writeValue(c, node, i); err != nil {
			return err
		}
	}
	time.Sleep(3000 * time.Millisecond)
	seen := r.dataOf(1)
	list := []string{}
	overflow := false
	for _, dv := range seen {
		list = append(list, fmt.Sprintf("%v:0x%08X", dv.Value.Value(), uint32(dv.Status)))
		overflow = overflow || uint32(dv.Status)&0x0480 == 0x0480
	}
	if len(seen) != 2 {
		return fmt.Errorf("expected the last 2 of 5 values, got %d (%s)", len(seen), strings.Join(list, ", "))
	}
	if seen[0].Value.Value() != int32(4) || seen[1].Value.Value() != int32(5) {
		return fmt.Errorf("expected 4 and 5, got %s", strings.Join(list, ", "))
	}
	if !overflow {
		return fmt.Errorf("no value carries the Overflow bit (%s)", strings.Join(list, ", "))
	}
	return nil
}

func checkTriggering(c context.Context, x *ctx) error {
	r, err := x.subscribe(c, 100*time.Millisecond)
	if err != nil {
		return err
	}
	defer r.cancel()
	trigger := dataItem(x.id("Scalar_Static_Int32"), 1)
	linked := dataItem(x.id("Scalar_Static_String"), 2)
	linked.MonitoringMode = ua.MonitoringModeSampling
	ids, err := r.monitor(c, trigger, linked)
	if err != nil {
		return err
	}
	set, err := r.sub.SetTriggering(c, ids[0], []uint32{ids[1]}, nil)
	if err != nil {
		return err
	}
	if len(set.AddResults) != 1 || set.AddResults[0] != ua.StatusOK {
		return fmt.Errorf("SetTriggering returned %v", set.AddResults)
	}
	time.Sleep(500 * time.Millisecond)
	before := len(r.dataOf(2))
	if err := x.writeValue(c, x.id("Scalar_Static_String"), fmt.Sprintf("linked %d", time.Now().UnixNano())); err != nil {
		return err
	}
	time.Sleep(500 * time.Millisecond)
	if len(r.dataOf(2)) != before {
		return errors.New("the sampling item reported without its trigger")
	}
	if err := x.writeValue(c, x.id("Scalar_Static_Int32"), int32(time.Now().UnixNano()&0x7fffffff)); err != nil {
		return err
	}
	time.Sleep(1000 * time.Millisecond)
	if len(r.dataOf(2)) <= before {
		return errors.New("the triggered item did not report")
	}
	return nil
}

func (x *ctx) republishStatus(c context.Context, subscriptionID, sequence uint32) ua.StatusCode {
	var res *ua.RepublishResponse
	err := x.c.Send(c, &ua.RepublishRequest{SubscriptionID: subscriptionID, RetransmitSequenceNumber: sequence},
		func(v ua.Response) error {
			var ok bool
			if res, ok = v.(*ua.RepublishResponse); !ok {
				return fmt.Errorf("unexpected response %T", v)
			}
			return nil
		})
	if err != nil {
		return statusOf(err)
	}
	return res.ResponseHeader.ServiceResult
}

func checkRepublish(c context.Context, x *ctx) error {
	r, err := x.subscribe(c, 100*time.Millisecond)
	if err != nil {
		return err
	}
	defer r.cancel()
	if _, err := r.monitor(c, dataItem(x.id("Scalar_Static_Int32"), 1)); err != nil {
		return err
	}
	time.Sleep(500 * time.Millisecond)
	// gopcua keeps the last sequence number private; every data message
	// advances it by one, so this is at least last + 1000.
	r.mu.Lock()
	sequence := uint32(r.messages) + 1000
	r.mu.Unlock()
	if s := x.republishStatus(c, r.sub.SubscriptionID, sequence); s != ua.StatusBadMessageNotAvailable {
		return fmt.Errorf("Republish of an unsent message returned %s", s.Error())
	}
	if s := x.republishStatus(c, r.sub.SubscriptionID+100000, 1); s != ua.StatusBadSubscriptionIDInvalid {
		return fmt.Errorf("Republish of an unknown subscription returned %s", s.Error())
	}
	return nil
}

func checkTransferSubscription(c context.Context, x *ctx) error {
	node := x.id("Scalar_Static_Int32")
	// The subscription is created on a session of its own: once it has been
	// transferred away, gopcua's client state for it is stale (Cancel blocks
	// and the channel can be dropped), so the source session is discarded
	// instead of the session the other checks use.
	source, err := x.connect(c, true, ua.UserTokenTypeAnonymous, opcua.AuthAnonymous())
	if err != nil {
		return fmt.Errorf("opening the source session failed: %w", err)
	}
	defer func() {
		cc, done := cleanupContext()
		defer done()
		closed := make(chan struct{})
		go func() {
			_ = source.Close(cc)
			close(closed)
		}()
		select {
		case <-closed:
		case <-cc.Done():
		}
	}()
	sx := *x
	sx.c = source
	r, err := sx.subscribe(c, 100*time.Millisecond)
	if err != nil {
		return err
	}
	if _, err := r.monitor(c, dataItem(node, 7)); err != nil {
		return err
	}
	target, err := x.connect(c, true, ua.UserTokenTypeAnonymous, opcua.AuthAnonymous())
	if err != nil {
		return fmt.Errorf("opening the second session failed: %w", err)
	}
	defer func() {
		cc, done := cleanupContext()
		defer done()
		_ = target.Close(cc)
	}()
	// gopcua has no API to adopt a transferred subscription: its publish loop
	// only dispatches subscriptions it created (and pauses on
	// BadNoSubscription), so session B sends its own Publish requests.
	time.Sleep(300 * time.Millisecond)
	var transfer *ua.TransferSubscriptionsResponse
	err = target.Send(c, &ua.TransferSubscriptionsRequest{SubscriptionIDs: []uint32{r.sub.SubscriptionID},
		SendInitialValues: true}, func(v ua.Response) error {
		var ok bool
		if transfer, ok = v.(*ua.TransferSubscriptionsResponse); !ok {
			return fmt.Errorf("unexpected response %T", v)
		}
		return nil
	})
	if err != nil {
		return fmt.Errorf("TransferSubscriptions failed: %w", err)
	}
	if len(transfer.Results) != 1 || transfer.Results[0].StatusCode != ua.StatusOK {
		return fmt.Errorf("TransferSubscriptions returned %v", transfer.Results)
	}
	defer func() {
		cc, done := cleanupContext()
		defer done()
		_ = target.Send(cc, &ua.DeleteSubscriptionsRequest{SubscriptionIDs: []uint32{r.sub.SubscriptionID}},
			func(ua.Response) error { return nil })
	}()
	written := int32(time.Now().UnixNano() & 0x7fffffff)
	status, err := target.Write(c, &ua.WriteRequest{NodesToWrite: []*ua.WriteValue{{NodeID: node,
		AttributeID: ua.AttributeIDValue, Value: &ua.DataValue{EncodingMask: ua.DataValueValue, Value: ua.MustVariant(written)}}}})
	if err != nil {
		return err
	}
	if status.Results[0] != ua.StatusOK {
		return fmt.Errorf("write returned %s", status.Results[0].Error())
	}
	acks := []*ua.SubscriptionAcknowledgement{}
	end := time.Now().Add(eventWait)
	for time.Now().Before(end) {
		var pub *ua.PublishResponse
		err := target.Send(c, &ua.PublishRequest{SubscriptionAcknowledgements: acks}, func(v ua.Response) error {
			var ok bool
			if pub, ok = v.(*ua.PublishResponse); !ok {
				return fmt.Errorf("unexpected response %T", v)
			}
			return nil
		})
		if err != nil {
			return fmt.Errorf("Publish on the second session failed: %w", err)
		}
		acks = []*ua.SubscriptionAcknowledgement{}
		if pub.SubscriptionID != r.sub.SubscriptionID || pub.NotificationMessage == nil {
			continue
		}
		if len(pub.NotificationMessage.NotificationData) > 0 {
			acks = append(acks, &ua.SubscriptionAcknowledgement{SubscriptionID: pub.SubscriptionID,
				SequenceNumber: pub.NotificationMessage.SequenceNumber})
		}
		for _, d := range pub.NotificationMessage.NotificationData {
			if dc, ok := d.Value.(*ua.DataChangeNotification); ok {
				for _, n := range dc.MonitoredItems {
					if n.ClientHandle == 7 && n.Value != nil && n.Value.Value != nil && n.Value.Value.Value() == written {
						return nil
					}
				}
			}
		}
	}
	return errors.New("the transferred subscription reported nothing")
}

// ------------------------------------------------------------------ identity

func checkWrongPassword(c context.Context, x *ctx) error {
	cl, err := x.connect(c, true, ua.UserTokenTypeUserName, opcua.AuthUsername("user1", "wrong password"))
	if err == nil {
		_ = cl.Close(context.Background())
		return errors.New("a session with a wrong password was activated")
	}
	if s := statusOf(err); s != ua.StatusBadUserAccessDenied && s != ua.StatusBadIdentityTokenRejected {
		return fmt.Errorf("the wrong password was rejected with %s", statusName(err))
	}
	return nil
}

func checkX509UserToken(c context.Context, x *ctx) error {
	key, err := rsa.GenerateKey(rand.Reader, 2048)
	if err != nil {
		return err
	}
	serial, _ := rand.Int(rand.Reader, big.NewInt(1<<62))
	template := &x509.Certificate{
		SerialNumber:          serial,
		Subject:               pkix.Name{CommonName: "InteropUser", Organization: []string{"OPC Foundation"}},
		NotBefore:             time.Now().Add(-time.Hour),
		NotAfter:              time.Now().AddDate(1, 0, 0),
		KeyUsage:              x509.KeyUsageDigitalSignature | x509.KeyUsageContentCommitment | x509.KeyUsageKeyEncipherment | x509.KeyUsageDataEncipherment,
		ExtKeyUsage:           []x509.ExtKeyUsage{x509.ExtKeyUsageClientAuth},
		BasicConstraintsValid: true,
	}
	cert, err := x509.CreateCertificate(rand.Reader, template, template, &key.PublicKey, key)
	if err != nil {
		return err
	}
	cl, err := x.connect(c, true, ua.UserTokenTypeCertificate, opcua.AuthCertificate(cert), opcua.AuthPrivateKey(key))
	if err != nil {
		return fmt.Errorf("the X509 user session failed: %s", statusName(err))
	}
	defer cl.Close(context.Background())
	resp, err := cl.Read(c, &ua.ReadRequest{NodesToRead: []*ua.ReadValueID{valueID(ua.NewNumericNodeID(0, id.Server_ServerStatus_State))}})
	if err != nil {
		return err
	}
	if resp.Results[0].Status != ua.StatusOK {
		return fmt.Errorf("reading with the X509 user returned %s", resp.Results[0].Status.Error())
	}
	return nil
}

// ------------------------------------------------------------------ services

func checkRegisterNodes(c context.Context, x *ctx) error {
	registered, err := x.c.RegisterNodes(c, &ua.RegisterNodesRequest{
		NodesToRegister: []*ua.NodeID{x.id("Scalar_Static_Int32"), x.id("Scalar_Static_String")}})
	if err != nil {
		return err
	}
	if len(registered.RegisteredNodeIDs) != 2 {
		return fmt.Errorf("RegisterNodes returned %d ids", len(registered.RegisteredNodeIDs))
	}
	values, err := x.read(c, valueID(registered.RegisteredNodeIDs[0]), valueID(registered.RegisteredNodeIDs[1]))
	if err != nil {
		return err
	}
	for _, v := range values {
		if v.Status != ua.StatusOK {
			return fmt.Errorf("reading registered nodes returned %s", v.Status.Error())
		}
	}
	_, err = x.c.UnregisterNodes(c, &ua.UnregisterNodesRequest{NodesToUnregister: registered.RegisteredNodeIDs})
	return err
}

func (x *ctx) historyRead(c context.Context, details *ua.ReadRawModifiedDetails, release bool, node *ua.HistoryReadValueID) (*ua.HistoryReadResponse, error) {
	// Client.HistoryReadRawModified always asks for both timestamps.
	req := &ua.HistoryReadRequest{
		TimestampsToReturn:        ua.TimestampsToReturnSource,
		ReleaseContinuationPoints: release,
		NodesToRead:               []*ua.HistoryReadValueID{node},
		HistoryReadDetails: &ua.ExtensionObject{
			TypeID:       ua.NewFourByteExpandedNodeID(0, id.ReadRawModifiedDetails_Encoding_DefaultBinary),
			EncodingMask: ua.ExtensionObjectBinary,
			Value:        details,
		},
	}
	var res *ua.HistoryReadResponse
	err := x.c.Send(c, req, func(v ua.Response) error {
		var ok bool
		if res, ok = v.(*ua.HistoryReadResponse); !ok {
			return fmt.Errorf("unexpected response %T", v)
		}
		return nil
	})
	return res, err
}

func checkHistoryReadRaw(c context.Context, x *ctx) error {
	details := &ua.ReadRawModifiedDetails{StartTime: time.Now().UTC().Add(-3 * time.Hour), EndTime: time.Now().UTC(),
		NumValuesPerNode: 10, ReturnBounds: false}
	node := x.id("Scalar_Static_Double")
	res, err := x.historyRead(c, details, false, &ua.HistoryReadValueID{NodeID: node, DataEncoding: &ua.QualifiedName{}})
	if err != nil {
		return err
	}
	result := res.Results[0]
	if result.StatusCode != ua.StatusOK {
		return fmt.Errorf("HistoryRead returned %s", result.StatusCode.Error())
	}
	var data *ua.HistoryData
	if result.HistoryData != nil {
		data, _ = result.HistoryData.Value.(*ua.HistoryData)
	}
	if data == nil || len(data.DataValues) == 0 {
		return errors.New("HistoryRead returned no values")
	}
	if len(data.DataValues) > 10 {
		return fmt.Errorf("%d values despite NumValuesPerNode 10", len(data.DataValues))
	}
	if len(result.ContinuationPoint) > 0 {
		_, _ = x.historyRead(c, details, true, &ua.HistoryReadValueID{NodeID: node, DataEncoding: &ua.QualifiedName{},
			ContinuationPoint: result.ContinuationPoint})
	}
	return nil
}

func checkNodeManagement(c context.Context, x *ctx) error {
	suffix := make([]byte, 4)
	_, _ = rand.Read(suffix)
	name := "InteropAdded_" + hex.EncodeToString(suffix)
	var added *ua.AddNodesResponse
	err := x.c.Send(c, &ua.AddNodesRequest{NodesToAdd: []*ua.AddNodesItem{{
		ParentNodeID:       ua.NewNumericExpandedNodeID(0, id.ObjectsFolder),
		ReferenceTypeID:    ua.NewNumericNodeID(0, id.Organizes),
		RequestedNewNodeID: ua.NewStringExpandedNodeID(x.ns, name),
		BrowseName:         &ua.QualifiedName{NamespaceIndex: x.ns, Name: name},
		NodeClass:          ua.NodeClassObject,
		NodeAttributes: ua.NewExtensionObject(&ua.ObjectAttributes{SpecifiedAttributes: uint32(ua.NodeAttributesMaskDisplayName),
			// gopcua cannot encode a nil LocalizedText.
			DisplayName: ua.NewLocalizedText(name), Description: &ua.LocalizedText{}}),
		TypeDefinition: ua.NewNumericExpandedNodeID(0, id.BaseObjectType),
	}}}, func(v ua.Response) error {
		var ok bool
		if added, ok = v.(*ua.AddNodesResponse); !ok {
			return fmt.Errorf("unexpected response %T", v)
		}
		return nil
	})
	if err != nil {
		return err
	}
	if added.Results[0].StatusCode != ua.StatusOK {
		return fmt.Errorf("AddNodes returned %s", added.Results[0].StatusCode.Error())
	}
	refs, err := x.browse(c, ua.NewNumericNodeID(0, id.ObjectsFolder), 0)
	if err != nil {
		return err
	}
	found := false
	for _, r := range refs {
		found = found || r.BrowseName.Name == name
	}
	if !found {
		return errors.New("the added node is not organized by the Objects folder")
	}
	var deleted *ua.DeleteNodesResponse
	err = x.c.Send(c, &ua.DeleteNodesRequest{NodesToDelete: []*ua.DeleteNodesItem{{NodeID: added.Results[0].AddedNodeID,
		DeleteTargetReferences: true}}}, func(v ua.Response) error {
		var ok bool
		if deleted, ok = v.(*ua.DeleteNodesResponse); !ok {
			return fmt.Errorf("unexpected response %T", v)
		}
		return nil
	})
	if err != nil {
		return err
	}
	if deleted.Results[0] != ua.StatusOK {
		return fmt.Errorf("DeleteNodes returned %s", deleted.Results[0].Error())
	}
	return nil
}

func checkIndexRange(c context.Context, x *ctx) error {
	node := x.id("Scalar_Static_Arrays_Int32")
	if err := x.writeValue(c, node, []int32{0, 1, 2, 3, 4, 5, 6, 7, 8, 9}); err != nil {
		return err
	}
	write, err := x.c.Write(c, &ua.WriteRequest{NodesToWrite: []*ua.WriteValue{{NodeID: node, AttributeID: ua.AttributeIDValue,
		IndexRange: "2:3", Value: &ua.DataValue{EncodingMask: ua.DataValueValue, Value: ua.MustVariant([]int32{20, 30})}}}})
	if err != nil {
		return err
	}
	if write.Results[0] != ua.StatusOK {
		return fmt.Errorf("writing the index range 2:3 returned %s", write.Results[0].Error())
	}
	values, err := x.read(c, &ua.ReadValueID{NodeID: node, AttributeID: ua.AttributeIDValue, IndexRange: "1:4"})
	if err != nil {
		return err
	}
	if values[0].Status != ua.StatusOK {
		return fmt.Errorf("reading the index range 1:4 returned %s", values[0].Status.Error())
	}
	part, _ := values[0].Value.Value().([]int32)
	if fmt.Sprint(part) != fmt.Sprint([]int32{1, 20, 30, 4}) {
		return fmt.Errorf("index range 1:4 read %v", values[0].Value.Value())
	}
	return nil
}

func checkFindServers(c context.Context, x *ctx) error {
	servers, err := opcua.FindServers(c, x.url)
	if err != nil {
		return err
	}
	serverURI := x.ep.Server.ApplicationURI
	uris := []string{}
	found := false
	for _, s := range servers {
		uris = append(uris, s.ApplicationURI)
		found = found || s.ApplicationURI == serverURI
	}
	if !found {
		return fmt.Errorf("FindServers does not list %s: %s", serverURI, strings.Join(uris, ", "))
	}
	endpoints, err := opcua.GetEndpoints(c, x.url)
	if err != nil {
		return err
	}
	for _, e := range endpoints {
		if e.SecurityPolicyURI == ua.SecurityPolicyURIBasic256Sha256 && e.SecurityMode == ua.MessageSecurityModeSignAndEncrypt {
			return nil
		}
	}
	return errors.New("GetEndpoints does not offer Basic256Sha256/SignAndEncrypt")
}

// checkSessionReconnect activates the session of this run on a new secure
// channel (a second client that only dials) and keeps reading through it.
func checkSessionReconnect(c context.Context, x *ctx) error {
	next, err := x.connect(c, false, ua.UserTokenTypeAnonymous, opcua.AuthAnonymous())
	if err != nil {
		return err
	}
	if err := next.Dial(c); err != nil {
		return fmt.Errorf("opening the new secure channel failed: %w", err)
	}
	session, _ := x.c.DetachSession(c)
	if session == nil {
		_ = next.Close(context.Background())
		return errors.New("the client has no session")
	}
	if err := next.ActivateSession(c, session); err != nil {
		_ = next.Close(context.Background())
		return fmt.Errorf("ActivateSession on the new channel failed: %s", statusName(err))
	}
	// The old channel carries no session any more; closing it keeps the session.
	_ = x.c.Close(context.Background())
	x.c = next
	if next.Session() != session {
		return errors.New("the new channel does not use the existing session")
	}
	values, err := x.read(c, valueID(ua.NewNumericNodeID(0, id.Server_ServerStatus_State)))
	if err != nil {
		return err
	}
	if values[0].Status != ua.StatusOK {
		return fmt.Errorf("reading after the reconnect returned %s", values[0].Status.Error())
	}
	return nil
}
