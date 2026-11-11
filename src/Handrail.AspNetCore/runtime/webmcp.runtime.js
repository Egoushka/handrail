// Handrail runtime: registers server-backed tools with the browser's WebMCP API.
// Facts from the WebMCP draft (webmachinelearning.github.io/webmcp): the entry point is document.modelContext,
// registerTool(tool, { signal, exposedTo }) returns a Promise, and a tool is removed by aborting its signal.
// Nothing here throws on a browser without WebMCP; it does nothing.

const VERSION = 1;

export function getModelContext(doc, nav, log) {
  if (doc && doc.modelContext) return doc.modelContext;
  if (nav && nav.modelContext) {
    // Early-preview builds used navigator.modelContext; the spec defines document only.
    log.warn("[handrail] using navigator.modelContext; the WebMCP draft defines document.modelContext.");
    return nav.modelContext;
  }
  return null;
}

// A modal <dialog> traps focus and makes the rest of the page inert. Only a trusted click allows: an injected
// script can click(), but cannot make the event trusted.
export function confirmDialog(doc, spec, args, signal) {
  return new Promise((resolve) => {
    const dialog = doc.createElement("dialog");
    dialog.setAttribute("class", "handrail-dialog");
    dialog.setAttribute("aria-label", "Confirm action");

    const heading = doc.createElement("h2");
    heading.textContent = 'Allow "' + (spec.title || spec.name) + '"?';
    const intro = doc.createElement("p");
    intro.textContent = "An assistant wants to run this action with your account.";
    const description = doc.createElement("p");
    description.textContent = spec.description || "";
    const params = doc.createElement("pre");
    let shown = JSON.stringify(args ?? {}, null, 2);
    if (shown.length > 2000) shown = shown.slice(0, 2000) + "\n...";
    params.textContent = shown;

    const cancel = doc.createElement("button");
    cancel.setAttribute("type", "button");
    cancel.setAttribute("autofocus", "");
    cancel.textContent = "Cancel";
    const allow = doc.createElement("button");
    allow.setAttribute("type", "button");
    allow.textContent = "Allow";

    let settled = false;
    const finish = (value) => {
      if (settled) return;
      settled = true;
      if (signal) signal.removeEventListener("abort", onAbort);
      try { dialog.close(); } catch { /* already closed */ }
      dialog.remove();
      resolve(value);
    };
    const onAbort = () => finish(false);

    allow.addEventListener("click", (event) => { if (event.isTrusted === true) finish(true); });
    cancel.addEventListener("click", () => finish(false));
    dialog.addEventListener("cancel", () => finish(false));
    dialog.addEventListener("close", () => finish(false));
    if (signal) {
      if (signal.aborted) return finish(false);
      signal.addEventListener("abort", onAbort);
    }

    for (const node of [heading, intro, description, params, cancel, allow]) dialog.appendChild(node);
    doc.body.appendChild(dialog);
    dialog.showModal();
  });
}

export function register(config, env = defaultEnv()) {
  if (!config || !Array.isArray(config.tools)) return null;
  const log = env.console || console;
  const context = getModelContext(env.document, env.navigator, log);
  if (!context) return null;

  const controller = new AbortController();
  if (env.window) env.window.addEventListener("pagehide", () => controller.abort(), { once: true });

  // Dialogs queue: one question at a time, so an agent cannot stack prompts behind each other.
  let queue = Promise.resolve();
  const ask = (spec, args, signal) => {
    const next = queue.then(() => confirmDialog(env.document, spec, args, signal));
    queue = next.catch(() => {});
    return next;
  };

  const post = async (path, body, signal) => {
    const headers = { "content-type": "application/json" };
    if (config.csrf && config.csrf.token) headers[config.csrf.header] = config.csrf.token;
    const response = await env.fetch(config.endpoint + "/" + path, {
      method: "POST", credentials: "same-origin", headers, body: JSON.stringify(body), signal,
    });
    let payload = null;
    try { payload = await response.json(); } catch { /* not JSON */ }
    if (!response.ok || !payload || payload.ok !== true) {
      throw new Error((payload && payload.error && payload.error.message) || "The request failed (" + response.status + ").");
    }
    return payload;
  };

  const toTool = (spec) => {
    const tool = {
      name: spec.name,
      title: spec.title,
      description: spec.description,
      inputSchema: spec.inputSchema,
      annotations: spec.annotations || {},
      async execute(input, options) {
        const signal = options && options.signal;
        const args = input ?? {};
        let confirmation;
        if (spec.confirm === "page") {
          if (!(await ask(spec, args, signal))) throw new Error("The user declined this action.");
          confirmation = (await post("confirm/" + encodeURIComponent(spec.name), { arguments: args }, signal)).token;
        }
        const reply = await post("invoke/" + encodeURIComponent(spec.name), { arguments: args, confirmation }, signal);
        return reply.result;
      },
    };
    if (spec.outputSchema) tool.outputSchema = spec.outputSchema;
    return tool;
  };

  const options = { signal: controller.signal };
  if (Array.isArray(config.exposedTo) && config.exposedTo.length > 0) options.exposedTo = config.exposedTo;

  // One failing tool must not stop the rest.
  for (const spec of config.tools) {
    try {
      const pending = context.registerTool(toTool(spec), options);
      if (pending && typeof pending.catch === "function") {
        pending.catch((error) => log.warn("[handrail] could not register " + spec.name + ": " + (error && error.message)));
      }
    } catch (error) {
      log.warn("[handrail] could not register " + spec.name + ": " + (error && error.message));
    }
  }
  return { controller, version: VERSION };
}

function defaultEnv() {
  return {
    document: typeof document !== "undefined" ? document : undefined,
    navigator: typeof navigator !== "undefined" ? navigator : undefined,
    window: typeof window !== "undefined" ? window : undefined,
    fetch: typeof fetch !== "undefined" ? fetch.bind(globalThis) : undefined,
    console,
  };
}

if (typeof document !== "undefined") {
  const element = document.getElementById("handrail-config");
  if (element) {
    try { register(JSON.parse(element.textContent)); } catch (error) { console.warn("[handrail] bad config: " + (error && error.message)); }
  }
}
