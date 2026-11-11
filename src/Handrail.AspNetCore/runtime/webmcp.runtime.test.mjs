import { test } from "node:test";
import assert from "node:assert/strict";
import { register, getModelContext } from "./webmcp.runtime.js";

class FakeModelContext {
  tools = new Map();
  async registerTool(tool, options = {}) {
    for (const key of ["name", "description", "execute"]) assert.ok(tool[key], "tool needs " + key);
    assert.match(tool.name, /^[A-Za-z0-9_.-]{1,128}$/);
    assert.equal(typeof tool.execute, "function");
    if (this.tools.has(tool.name)) throw new Error("InvalidStateError");
    this.tools.set(tool.name, tool);
    options.signal?.addEventListener("abort", () => this.tools.delete(tool.name));
  }
  async executeTool(name, input) {
    return this.tools.get(name).execute(input, { signal: new AbortController().signal });
  }
}

class FakeElement {
  constructor(tag) { this.tag = tag; this.children = []; this.attrs = {}; this.handlers = {}; this.textContent = ""; this.open = false; }
  setAttribute(k, v) { this.attrs[k] = v; }
  appendChild(c) { this.children.push(c); c.parent = this; return c; }
  addEventListener(type, fn) { (this.handlers[type] ||= []).push(fn); }
  dispatch(type, event = {}) { for (const fn of this.handlers[type] || []) fn(event); }
  showModal() { this.open = true; }
  close() { if (this.open) { this.open = false; this.dispatch("close"); } }
  remove() { if (this.parent) this.parent.children = this.parent.children.filter((c) => c !== this); }
  find(tag, text) { return this.children.find((c) => c.tag === tag && (text === undefined || c.textContent === text)); }
}

function makeEnv({ modelContext, navModelContext, responses = [] } = {}) {
  const warnings = [];
  const calls = [];
  const body = new FakeElement("body");
  const listeners = {};
  const env = {
    document: { modelContext, body, createElement: (t) => new FakeElement(t) },
    navigator: navModelContext ? { modelContext: navModelContext } : undefined,
    window: { addEventListener: (t, fn) => { listeners[t] = fn; } },
    console: { warn: (m) => warnings.push(m) },
    fetch: async (url, init) => {
      calls.push({ url, init, body: JSON.parse(init.body) });
      const r = responses.shift() ?? { status: 200, json: { ok: true, result: "{}" } };
      return { ok: r.status < 400, status: r.status, json: async () => r.json };
    },
  };
  return { env, warnings, calls, body, pagehide: () => listeners.pagehide() };
}

const readTool = { name: "search_notes", title: "Search", description: "Search notes", inputSchema: { type: "object" }, annotations: { readOnlyHint: true }, confirm: "none" };
const writeTool = { name: "delete_note", title: "Delete note", description: "Delete a note", inputSchema: { type: "object" }, annotations: { consequentialHint: true }, confirm: "page" };
const config = (tools, extra = {}) => ({ v: 1, endpoint: "/_webmcp", csrf: { header: "X-CSRF-TOKEN", token: "csrf1" }, tools, ...extra });
const tick = () => new Promise((r) => setImmediate(r));

test("does nothing, without throwing, when the browser has no WebMCP", () => {
  const { env } = makeEnv();
  assert.equal(register(config([readTool]), env), null);
});

test("prefers document.modelContext and warns when it falls back to navigator", () => {
  const log = { warn: (m) => log.last = m };
  const mc = {};
  assert.equal(getModelContext({ modelContext: mc }, { modelContext: {} }, log), mc);
  assert.equal(log.last, undefined);
  const nav = {};
  assert.equal(getModelContext({}, { modelContext: nav }, log), nav);
  assert.match(log.last, /navigator\.modelContext/);
});

test("registers each tool with a signal, and aborting the signal unregisters them", async () => {
  const mc = new FakeModelContext();
  const { env, pagehide } = makeEnv({ modelContext: mc });
  register(config([readTool, writeTool]), env);
  await tick();
  assert.deepEqual([...mc.tools.keys()], ["search_notes", "delete_note"]);
  assert.deepEqual(mc.tools.get("search_notes").annotations, { readOnlyHint: true });
  assert.equal(mc.unregisterTool, undefined);
  pagehide();
  assert.equal(mc.tools.size, 0);
});

test("passes exposedTo only when the page configures origins", async () => {
  const seen = [];
  const mc = { registerTool: async (tool, options) => { seen.push(options); } };
  register(config([readTool]), makeEnv({ modelContext: mc }).env);
  register(config([readTool], { exposedTo: ["https://agent.example"] }), makeEnv({ modelContext: mc }).env);
  assert.equal("exposedTo" in seen[0], false);
  assert.deepEqual(seen[1].exposedTo, ["https://agent.example"]);
});

test("one tool that fails to register does not stop the others", async () => {
  const names = [];
  const mc = { registerTool: (tool) => { if (tool.name === "search_notes") throw new Error("boom"); names.push(tool.name); return Promise.reject(new Error("late")); } };
  const { env, warnings } = makeEnv({ modelContext: mc });
  register(config([readTool, writeTool]), env);
  await tick();
  assert.deepEqual(names, ["delete_note"]);
  assert.equal(warnings.length, 2);
});

test("a read-only tool calls the server with the antiforgery header and same-origin credentials", async () => {
  const mc = new FakeModelContext();
  const { env, calls } = makeEnv({ modelContext: mc, responses: [{ status: 200, json: { ok: true, result: "[1]" } }] });
  register(config([readTool]), env);
  await tick();
  assert.equal(await mc.executeTool("search_notes", { query: "a" }), "[1]");
  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, "/_webmcp/invoke/search_notes");
  assert.equal(calls[0].init.credentials, "same-origin");
  assert.equal(calls[0].init.headers["X-CSRF-TOKEN"], "csrf1");
  assert.deepEqual(calls[0].body, { arguments: { query: "a" } });
});

test("a server error rejects with the server's message", async () => {
  const mc = new FakeModelContext();
  const { env } = makeEnv({ modelContext: mc, responses: [{ status: 400, json: { ok: false, error: { code: "invalid_input", message: "Argument 'q' is required." } } }] });
  register(config([readTool]), env);
  await tick();
  await assert.rejects(mc.executeTool("search_notes", {}), /Argument 'q' is required/);
});

test("a write shows a dialog with the parameters as text and waits for a trusted Allow", async () => {
  const mc = new FakeModelContext();
  const { env, calls, body } = makeEnv({ modelContext: mc, responses: [{ status: 200, json: { ok: true, token: "tok1" } }, { status: 200, json: { ok: true, result: "{\"ok\":true}" } }] });
  register(config([writeTool]), env);
  await tick();
  const run = mc.executeTool("delete_note", { id: "<img src=x onerror=alert(1)>" });
  await tick();

  const dialog = body.find("dialog");
  assert.ok(dialog.open);
  assert.equal(dialog.find("h2").textContent, 'Allow "Delete note"?');
  assert.match(dialog.find("pre").textContent, /<img src=x onerror=alert\(1\)>/);
  assert.equal(calls.length, 0);

  dialog.find("button", "Allow").dispatch("click", { isTrusted: false });
  await tick();
  assert.ok(dialog.open, "a script-made click must not allow");
  assert.equal(calls.length, 0);

  dialog.find("button", "Allow").dispatch("click", { isTrusted: true });
  assert.equal(await run, "{\"ok\":true}");
  assert.deepEqual(calls.map((c) => c.url), ["/_webmcp/confirm/delete_note", "/_webmcp/invoke/delete_note"]);
  assert.equal(calls[1].body.confirmation, "tok1");
  assert.equal(body.children.length, 0);
});

test("cancel and Escape decline without touching the server", async () => {
  const mc = new FakeModelContext();
  const { env, calls, body } = makeEnv({ modelContext: mc });
  register(config([writeTool]), env);
  await tick();

  let run = mc.executeTool("delete_note", {});
  await tick();
  body.find("dialog").find("button", "Cancel").dispatch("click", { isTrusted: true });
  await assert.rejects(run, /declined/);

  run = mc.executeTool("delete_note", {});
  await tick();
  body.find("dialog").dispatch("cancel");
  await assert.rejects(run, /declined/);
  assert.equal(calls.length, 0);
});

test("dialogs queue: the second waits until the first is answered", async () => {
  const mc = new FakeModelContext();
  const { env, body } = makeEnv({ modelContext: mc, responses: [{ status: 200, json: { ok: true, token: "t" } }, { status: 200, json: { ok: true, result: "1" } }] });
  register(config([writeTool]), env);
  await tick();
  const first = mc.executeTool("delete_note", { n: 1 });
  const second = mc.executeTool("delete_note", { n: 2 });
  await tick();
  assert.equal(body.children.length, 1);
  body.find("dialog").find("button", "Cancel").dispatch("click", { isTrusted: true });
  await assert.rejects(first);
  await tick();
  assert.equal(body.children.length, 1);
  assert.match(body.find("dialog").find("pre").textContent, /"n": 2/);
  body.find("dialog").find("button", "Cancel").dispatch("click", { isTrusted: true });
  await assert.rejects(second);
});

test("aborting the call closes an open dialog", async () => {
  const mc = new FakeModelContext();
  const { env, body } = makeEnv({ modelContext: mc });
  register(config([writeTool]), env);
  await tick();
  const controller = new AbortController();
  const run = mc.tools.get("delete_note").execute({}, { signal: controller.signal });
  await tick();
  controller.abort();
  await assert.rejects(run, /declined/);
  assert.equal(body.children.length, 0);
});
