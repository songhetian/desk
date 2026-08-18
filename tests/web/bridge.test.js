// TDD for the WebView2 host bridge (src/web-common/bridge.js).
// This is the contract that bit us historically: pages MUST post structured
// objects (never a JSON.stringify string) so C#'s WebMessageAsJson parses them.
const test = require('node:test');
const assert = require('node:assert');

// RED step: this require fails until bridge.js exists.
const Bridge = require('../../src/web-common/bridge.js');

function fakeHost() {
  return {
    messages: [],
    listeners: {},
    postMessage(m) { this.messages.push(m); },
    addEventListener(t, fn) { (this.listeners[t] = this.listeners[t] || []).push(fn); },
    dispatch(data) { (this.listeners.message || []).forEach(fn => fn({ data })); }
  };
}

// Slice 1 — the tracer bullet. The #1 historical regression was pages sending
// `postMessage(JSON.stringify(obj))` which C# received as a string and failed to parse.
test('ready(page) posts a structured OBJECT {type:"ready", page}, never a JSON string', () => {
  const host = fakeHost();
  Bridge._installHost(host);
  Bridge.ready('orb');
  assert.strictEqual(host.messages.length, 1, 'exactly one message posted');
  const m = host.messages[0];
  assert.strictEqual(typeof m, 'object', 'message must be an object, NOT a string');
  assert.strictEqual(m.type, 'ready');
  assert.strictEqual(m.page, 'orb');
});

// Slice 2 — arbitrary page->host messages go through untouched.
test('send(msg) posts the object as-is (no JSON.stringify wrapping)', () => {
  const host = fakeHost();
  Bridge._installHost(host);
  Bridge.send({ type: 'menu', action: 'exit' });
  assert.deepStrictEqual(host.messages[0], { type: 'menu', action: 'exit' });
});

// Slice 3 — host->page handlers receive dispatched messages.
test('on(type, fn) fires the handler when the host dispatches a matching message', () => {
  const host = fakeHost();
  Bridge._installHost(host);
  let received = null;
  Bridge.on('state', d => { received = d; });
  host.dispatch({ type: 'state', state: 'alert', count: 3 });
  assert.strictEqual(received.state, 'alert');
  assert.strictEqual(received.count, 3);
});

// Slice 4 — the host may deliver a JSON string; the bridge must normalize it.
test('on() normalizes a JSON-string payload from the host into an object', () => {
  const host = fakeHost();
  Bridge._installHost(host);
  let received = null;
  Bridge.on('state', d => { received = d; });
  host.dispatch(JSON.stringify({ type: 'state', state: 'offline' }));
  assert.strictEqual(received.state, 'offline');
});

// Slice 5 — unknown types must be ignored without throwing.
test('unknown message types are ignored (no handler throws)', () => {
  const host = fakeHost();
  Bridge._installHost(host);
  assert.doesNotThrow(() => host.dispatch({ type: 'somethingElse', x: 1 }));
});

// Slice 6 — send() must fail loud when there is no host (never silently no-op).
test('send() throws when the host bridge is unavailable', () => {
  Bridge._installHost(null);
  assert.throws(() => Bridge.send({ type: 'x' }), /not available/);
});

// Slice 7 — unsubscribe stops further delivery.
test('on() returns an unsubscribe function', () => {
  const host = fakeHost();
  Bridge._installHost(host);
  let count = 0;
  const off = Bridge.on('ping', () => count++);
  host.dispatch({ type: 'ping' });
  off();
  host.dispatch({ type: 'ping' });
  assert.strictEqual(count, 1);
});
