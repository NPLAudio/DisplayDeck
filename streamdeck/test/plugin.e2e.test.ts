// End-to-end tests: runs the real built plugin (bin/plugin.js) against a mock Stream Deck host and a stub
// DisplayDeck server. Nothing real is touched: the plugin is pointed at the stub through DISPLAYDECK_URL.
// Build first (npm run build); `npm test` does that for you.
import test from "node:test";
import assert from "node:assert/strict";
import { spawn, type ChildProcess } from "node:child_process";
import { existsSync } from "node:fs";
import http from "node:http";
import type { AddressInfo } from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { WebSocketServer, type WebSocket } from "ws";

const here = path.dirname(fileURLToPath(import.meta.url));
const pluginDir = path.resolve(here, "..", "com.nplaudio.displaydeck.sdPlugin");
const UUID = "com.nplaudio.displaydeck";
const A = {
	profile: `${UUID}.profile`,
	next: `${UUID}.next`,
	undo: `${UUID}.undo`,
	audio: `${UUID}.audio`,
	profileDial: `${UUID}.profile-dial`,
	audioDial: `${UUID}.audio-dial`,
	status: `${UUID}.status`,
};

type Msg = { event: string; context?: string; payload?: any; [k: string]: unknown };
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));
const decodeImage = (uri: string) => decodeURIComponent(uri.replace("data:image/svg+xml;charset=utf8,", ""));

// ---------- stub DisplayDeck server ----------
type Call = { method: string; path: string; body?: any };

async function startStub(initial: { current?: string | null } = {}) {
	const calls: Call[] = [];
	const state = {
		profiles: ["Den TV", "Desk", "Living Room"],
		current: initial.current === undefined ? "Desk" : initial.current,
		devices: [
			{ id: "UltraLite-mk5\\Device\\Main Out 1-2\\Render", name: "Main Out 1-2", deviceName: "UltraLite-mk5", isDefault: true, formats: [{ name: "Windows Sonic for Headphones", guid: "{B53D940C-B846-4831-9F76-D102B9B725A0}" }] },
			{ id: "NVIDIA\\Device\\LG TV\\Render", name: "LG TV", deviceName: "NVIDIA", isDefault: false, formats: [{ name: "Dolby Atmos for Home Theater", guid: "{A289735D-FA3E-4E35-9D7D-B6F896ACB2E7}" }] },
		],
	};
	const server = http.createServer((req, res) => {
		let raw = "";
		req.on("data", (c) => (raw += c));
		req.on("end", () => {
			const url = new URL(req.url ?? "/", "http://x");
			const body = raw ? JSON.parse(raw) : undefined;
			calls.push({ method: req.method ?? "GET", path: decodeURIComponent(url.pathname), body });
			const json = (code: number, o: unknown) => { res.writeHead(code, { "Content-Type": "application/json" }); res.end(JSON.stringify(o)); };
			if (url.pathname === "/api/current") return json(200, { profiles: state.profiles, current: state.current, lastApplied: null, liveAvailable: true });
			if (url.pathname === "/api/audio-devices") return json(200, { devices: state.devices });
			if (url.pathname.startsWith("/api/apply/")) {
				const name = decodeURIComponent(url.pathname.slice("/api/apply/".length));
				if (!state.profiles.includes(name)) return json(404, { error: `No such profile: ${name}` });
				state.current = name;
				return json(200, { ok: true, warnings: [] });
			}
			if (url.pathname === "/api/undo-layout") return json(200, { ok: true });
			if (url.pathname === "/api/audio/apply") return json(200, { ok: true });
			json(404, { error: "Not found" });
		});
	});
	await new Promise<void>((r) => server.listen(0, "127.0.0.1", r));
	const port = (server.address() as AddressInfo).port;
	return { url: `http://127.0.0.1:${port}`, calls, state, stop: () => new Promise<void>((r) => { server.close(() => r()); server.closeAllConnections(); }) };
}

// ---------- mock Stream Deck host ----------
async function startHost(stubUrl: string) {
	const wss = new WebSocketServer({ port: 0, host: "127.0.0.1" });
	await new Promise<void>((r) => wss.on("listening", r));
	const port = (wss.address() as AddressInfo).port;

	const received: Msg[] = [];
	const settings = new Map<string, any>();
	let socket: WebSocket | undefined;
	let registered: () => void;
	const whenRegistered = new Promise<void>((r) => (registered = r));

	wss.on("connection", (ws) => {
		socket = ws;
		ws.on("message", (data) => {
			const msg = JSON.parse(data.toString()) as Msg;
			if (msg.event === "registerPlugin") { registered(); return; }
			received.push(msg);
			// Answer the requests the SDK waits on.
			if (msg.event === "getSettings") {
				ws.send(JSON.stringify({ event: "didReceiveSettings", context: msg.context, device: "DEV1", action: "x", payload: { settings: settings.get(msg.context!) ?? {}, coordinates: { column: 0, row: 0 }, isInMultiAction: false } }));
			}
			if (msg.event === "getGlobalSettings") {
				ws.send(JSON.stringify({ event: "didReceiveGlobalSettings", payload: { settings: {} } }));
			}
		});
	});

	const info = {
		application: { font: "Segoe UI", language: "en", platform: "windows", platformVersion: "10.0.0", version: "7.6.0" },
		plugin: { uuid: UUID, version: "1.0.0.0" },
		devicePixelRatio: 1,
		colors: {},
		devices: [{ id: "DEV1", name: "Test Deck", size: { columns: 5, rows: 3 }, type: 0 }],
	};
	const child: ChildProcess = spawn(process.execPath, ["bin/plugin.js", "-port", String(port), "-pluginUUID", UUID, "-registerEvent", "registerPlugin", "-info", JSON.stringify(info)], {
		cwd: pluginDir,
		env: { ...process.env, DISPLAYDECK_URL: stubUrl },
		stdio: ["ignore", "pipe", "pipe"],
	});
	let output = "";
	child.stdout?.on("data", (d) => (output += d));
	child.stderr?.on("data", (d) => (output += d));
	await Promise.race([whenRegistered, sleep(10000).then(() => { throw new Error("plugin never registered. Output:\n" + output); })]);

	const send = (msg: object) => socket!.send(JSON.stringify(msg));
	const common = (action: string, context: string) => ({ action, context, device: "DEV1" });

	return {
		received,
		output: () => output,
		appear(action: string, context: string, s: object = {}, controller: "Keypad" | "Encoder" | "Neo" = "Keypad") {
			settings.set(context, s);
			send({ event: "willAppear", ...common(action, context), payload: { controller, coordinates: { column: 0, row: 0 }, isInMultiAction: false, settings: s, state: 0 } });
		},
		keyDown(action: string, context: string, s: object = {}) {
			send({ event: "keyDown", ...common(action, context), payload: { controller: "Keypad", coordinates: { column: 0, row: 0 }, isInMultiAction: false, settings: s, state: 0 } });
		},
		rotate(action: string, context: string, ticks: number) {
			send({ event: "dialRotate", ...common(action, context), payload: { controller: "Encoder", coordinates: { column: 0, row: 0 }, pressed: false, settings: settings.get(context) ?? {}, ticks } });
		},
		dialDown(action: string, context: string) {
			send({ event: "dialDown", ...common(action, context), payload: { controller: "Encoder", coordinates: { column: 0, row: 0 }, settings: settings.get(context) ?? {} } });
		},
		touchTap(action: string, context: string) {
			send({ event: "touchTap", ...common(action, context), payload: { controller: "Encoder", coordinates: { column: 0, row: 0 }, hold: false, settings: settings.get(context) ?? {}, tapPos: [100, 50] } });
		},
		inspectorOpens(action: string, context: string) { send({ event: "propertyInspectorDidAppear", ...common(action, context) }); },
		fromInspector(action: string, context: string, payload: object) { send({ event: "sendToPlugin", ...common(action, context), payload }); },
		changeSettings(action: string, context: string, s: object) {
			settings.set(context, s);
			send({ event: "didReceiveSettings", ...common(action, context), payload: { settings: s, coordinates: { column: 0, row: 0 }, isInMultiAction: false } });
		},
		async waitFor(pred: (m: Msg) => boolean, timeoutMs = 4000): Promise<Msg> {
			const end = Date.now() + timeoutMs;
			while (Date.now() < end) {
				const hit = received.find(pred);
				if (hit) return hit;
				await sleep(20);
			}
			throw new Error("timed out waiting for a message. Received: " + JSON.stringify(received.map((m) => [m.event, m.context])) + "\nPlugin output:\n" + output);
		},
		async close() { child.kill(); await new Promise<void>((r) => wss.close(() => r())); },
	};
}

async function withHarness(fn: (h: Awaited<ReturnType<typeof startHost>>, stub: Awaited<ReturnType<typeof startStub>>) => Promise<void>, stubOptions: { current?: string | null; startStopped?: boolean } = {}) {
	assert.ok(existsSync(path.join(pluginDir, "bin", "plugin.js")), "bin/plugin.js is missing: run npm run build");
	const stub = await startStub(stubOptions);
	if (stubOptions.startStopped) await stub.stop();
	const host = await startHost(stub.url);
	try {
		await fn(host, stub);
	} finally {
		await host.close();
		if (!stubOptions.startStopped) await stub.stop();
	}
}

const imageFor = (ctx: string) => (m: Msg) => m.event === "setImage" && m.context === ctx && typeof m.payload?.image === "string" && m.payload.image.length > 0;

// ---------- tests ----------

test("a profile key shows its name, lit when that profile is the active one", async () => {
	await withHarness(async (h) => {
		h.appear(A.profile, "k-active", { profile: "Desk" });
		h.appear(A.profile, "k-other", { profile: "Den TV" });
		const active = decodeImage((await h.waitFor((m) => imageFor("k-active")(m) && decodeImage(m.payload.image).includes("#22c55e"))).payload.image);
		assert.ok(active.includes(">Desk<"));
		const other = decodeImage((await h.waitFor(imageFor("k-other"))).payload.image);
		assert.ok(other.includes(">Den<") || other.includes(">Den TV<"));
		assert.ok(!other.includes("#22c55e"), "an inactive profile has no ring");
	});
});

test("pressing a profile key applies exactly that profile and shows the tick", async () => {
	await withHarness(async (h, stub) => {
		h.appear(A.profile, "k1", { profile: "Den TV" });
		await h.waitFor(imageFor("k1"));
		h.keyDown(A.profile, "k1", { profile: "Den TV" });
		await h.waitFor((m) => m.event === "showOk" && m.context === "k1");
		assert.deepEqual(stub.calls.filter((c) => c.method === "POST").map((c) => c.path), ["/api/apply/Den TV"]);
	});
});

test("a profile key with no profile chosen alerts and calls nothing", async () => {
	await withHarness(async (h, stub) => {
		h.appear(A.profile, "k1", {});
		await h.waitFor(imageFor("k1"));
		h.keyDown(A.profile, "k1", {});
		await h.waitFor((m) => m.event === "showAlert" && m.context === "k1");
		assert.equal(stub.calls.filter((c) => c.method === "POST").length, 0);
	});
});

test("a failed apply (unknown profile) shows the alert, not the tick", async () => {
	await withHarness(async (h) => {
		h.appear(A.profile, "k1", { profile: "Deleted" });
		await h.waitFor(imageFor("k1"));
		h.keyDown(A.profile, "k1", { profile: "Deleted" });
		await h.waitFor((m) => m.event === "showAlert" && m.context === "k1");
		assert.ok(!h.received.some((m) => m.event === "showOk" && m.context === "k1"));
	});
});

test("the next-profile key applies the profile after the active one", async () => {
	await withHarness(async (h, stub) => {
		h.appear(A.next, "n1");
		await h.waitFor(imageFor("n1"));
		h.keyDown(A.next, "n1");
		await h.waitFor((m) => m.event === "showOk" && m.context === "n1");
		assert.deepEqual(stub.calls.filter((c) => c.method === "POST").map((c) => c.path), ["/api/apply/Living Room"]);
	});
});

test("the undo key goes back one layout", async () => {
	await withHarness(async (h, stub) => {
		h.appear(A.undo, "u1");
		await h.waitFor(imageFor("u1"));
		h.keyDown(A.undo, "u1");
		await h.waitFor((m) => m.event === "showOk" && m.context === "u1");
		assert.deepEqual(stub.calls.filter((c) => c.method === "POST").map((c) => c.path), ["/api/undo-layout"]);
	});
});

test("the audio key is lit for the default device and sends device and spatial format", async () => {
	await withHarness(async (h, stub) => {
		const dev = stub.state.devices[0]!.id;
		h.appear(A.audio, "a1", { device: dev, label: "Main Out 1-2", spatial: "off" });
		await h.waitFor((m) => imageFor("a1")(m) && decodeImage(m.payload.image).includes("#22c55e"));
		h.keyDown(A.audio, "a1", { device: dev, spatial: "off" });
		await h.waitFor((m) => m.event === "showOk" && m.context === "a1");
		const post = stub.calls.find((c) => c.path === "/api/audio/apply")!;
		assert.deepEqual(post.body, { device: dev, spatial: "off" });
	});
});

test("an audio key set to 'leave as it is' does not send a spatial format", async () => {
	await withHarness(async (h, stub) => {
		const dev = stub.state.devices[1]!.id;
		h.appear(A.audio, "a1", { device: dev, spatial: "keep" });
		await h.waitFor(imageFor("a1"));
		h.keyDown(A.audio, "a1", { device: dev, spatial: "keep" });
		await h.waitFor((m) => m.event === "showOk" && m.context === "a1");
		assert.deepEqual(stub.calls.find((c) => c.path === "/api/audio/apply")!.body, { device: dev });
	});
});

test("the profile inspector gets the saved profiles for its dropdown", async () => {
	await withHarness(async (h) => {
		h.appear(A.profile, "k1", {});
		await h.waitFor(imageFor("k1"));
		h.inspectorOpens(A.profile, "k1");
		h.fromInspector(A.profile, "k1", { event: "getProfiles" });
		const reply = await h.waitFor((m) => m.event === "sendToPropertyInspector");
		assert.equal(reply.payload.event, "getProfiles");
		assert.deepEqual(reply.payload.items.map((i: any) => i.value), ["Den TV", "Desk", "Living Room"]);
	});
});

test("the audio inspector gets devices, and spatial formats for the chosen device only", async () => {
	await withHarness(async (h, stub) => {
		const lg = stub.state.devices[1]!.id;
		h.appear(A.audio, "a1", { device: lg });
		await h.waitFor(imageFor("a1"));
		h.inspectorOpens(A.audio, "a1");

		h.fromInspector(A.audio, "a1", { event: "getDevices" });
		const devs = await h.waitFor((m) => m.event === "sendToPropertyInspector" && m.payload?.event === "getDevices");
		assert.equal(devs.payload.items.length, 2);

		h.fromInspector(A.audio, "a1", { event: "getFormats" });
		const fmts = await h.waitFor((m) => m.event === "sendToPropertyInspector" && m.payload?.event === "getFormats");
		const values = fmts.payload.items.map((i: any) => i.value);
		assert.deepEqual(values, ["keep", "off", "Dolby Atmos for Home Theater"], "only the LG's own formats");
	});
});

test("changing the device in the inspector refreshes the spatial list and remembers a readable label", async () => {
	await withHarness(async (h, stub) => {
		const main = stub.state.devices[0]!.id;
		h.appear(A.audio, "a1", {});
		await h.waitFor(imageFor("a1"));
		h.inspectorOpens(A.audio, "a1");
		h.changeSettings(A.audio, "a1", { device: main });
		const saved = await h.waitFor((m) => m.event === "setSettings" && m.context === "a1");
		assert.equal(saved.payload.label, "Main Out 1-2");
	});
});

test("when DisplayDeck is not running, keys show the offline marker and inspectors say so", async () => {
	await withHarness(async (h) => {
		h.appear(A.profile, "k1", { profile: "Desk" });
		// The first paint happens before the first poll finishes; the offline marker follows as a repaint.
		const img = decodeImage((await h.waitFor((m) => imageFor("k1")(m) && decodeImage(m.payload.image).includes(">!<"))).payload.image);
		assert.ok(img.includes("offline"));

		h.inspectorOpens(A.profile, "k1");
		h.fromInspector(A.profile, "k1", { event: "getProfiles" });
		const reply = await h.waitFor((m) => m.event === "sendToPropertyInspector");
		assert.equal(reply.payload.items[0].disabled, true);
		assert.match(reply.payload.items[0].label, /not running/);

		h.keyDown(A.profile, "k1", { profile: "Desk" });
		await h.waitFor((m) => m.event === "showAlert" && m.context === "k1");
	}, { startStopped: true });
});

test("keys repaint on their own when the active profile changes elsewhere", async () => {
	await withHarness(async (h, stub) => {
		h.appear(A.profile, "k1", { profile: "Living Room" });
		await h.waitFor(imageFor("k1"));
		assert.ok(!h.received.some((m) => imageFor("k1")(m) && decodeImage(m.payload.image).includes("#22c55e")));
		stub.state.current = "Living Room"; // someone applied it from the web page
		await h.waitFor((m) => imageFor("k1")(m) && decodeImage(m.payload.image).includes("#22c55e"), 9000);
	});
});

// ---------- dials and the info bar ----------

const feedbackFor = (ctx: string, pred: (p: any) => boolean = () => true) => (m: Msg) => m.event === "setFeedback" && m.context === ctx && pred(m.payload);

test("the profile dial shows the active profile, turning previews, pressing applies", async () => {
	await withHarness(async (h, stub) => {
		h.appear(A.profileDial, "d1", {}, "Encoder");
		await h.waitFor(feedbackFor("d1", (p) => p.value?.value === "Desk" && p.value?.color === "#22c55e"));
		h.rotate(A.profileDial, "d1", 1);
		await h.waitFor(feedbackFor("d1", (p) => p.value?.value === "Living Room" && p.hint?.value === "Press to apply"));
		assert.ok(!stub.calls.some((c) => c.path.startsWith("/api/apply/")), "turning alone applies nothing");
		h.dialDown(A.profileDial, "d1");
		await h.waitFor((m) => feedbackFor("d1", (p) => p.value?.value === "Living Room" && p.value?.color === "#22c55e")(m));
		assert.ok(stub.calls.some((c) => c.method === "POST" && c.path === "/api/apply/Living Room"));
	});
});

test("turning the profile dial the other way wraps, and a touch tap also applies", async () => {
	await withHarness(async (h, stub) => {
		h.appear(A.profileDial, "d1", {}, "Encoder");
		await h.waitFor(feedbackFor("d1", (p) => p.value?.value === "Desk"));
		h.rotate(A.profileDial, "d1", -2); // Desk -> Den TV -> Living Room
		await h.waitFor(feedbackFor("d1", (p) => p.value?.value === "Living Room"));
		h.touchTap(A.profileDial, "d1");
		await h.waitFor(() => stub.calls.some((c) => c.path === "/api/apply/Living Room"));
	});
});

test("pressing the profile dial with nothing chosen does nothing", async () => {
	await withHarness(async (h, stub) => {
		h.appear(A.profileDial, "d1", {}, "Encoder");
		await h.waitFor(feedbackFor("d1", (p) => p.value?.value === "Desk"));
		h.dialDown(A.profileDial, "d1");
		await sleep(400);
		assert.ok(!stub.calls.some((c) => c.path.startsWith("/api/apply/")));
	});
});

test("the audio dial browses devices and makes the chosen one the default, leaving spatial alone", async () => {
	await withHarness(async (h, stub) => {
		h.appear(A.audioDial, "d2", {}, "Encoder");
		await h.waitFor(feedbackFor("d2", (p) => p.value?.value === "Main Out 1-2" && p.value?.color === "#22c55e"));
		h.rotate(A.audioDial, "d2", 1);
		await h.waitFor(feedbackFor("d2", (p) => p.value?.value === "LG TV" && p.hint?.value === "Press to make default"));
		h.dialDown(A.audioDial, "d2");
		await h.waitFor(() => stub.calls.some((c) => c.path === "/api/audio/apply"));
		assert.deepEqual(stub.calls.find((c) => c.path === "/api/audio/apply")!.body, { device: stub.state.devices[1]!.id });
	});
});

test("the undo dial undoes on press", async () => {
	await withHarness(async (h, stub) => {
		h.appear(A.undo, "d3", {}, "Encoder");
		await h.waitFor(feedbackFor("d3", (p) => p.title?.value === "UNDO"));
		h.dialDown(A.undo, "d3");
		await h.waitFor(() => stub.calls.some((c) => c.path === "/api/undo-layout"));
	});
});

test("the status action fills the Neo info bar with the profile and the default device", async () => {
	await withHarness(async (h) => {
		h.appear(A.status, "s1", {}, "Neo");
		await h.waitFor((m) => m.event === "setFeedbackLayout" && m.context === "s1" && m.payload?.layout === "layouts/infobar.json");
		await h.waitFor(feedbackFor("s1", (p) => p.profile?.value === "Desk" && p.audio?.value === "Main Out 1-2"));
	});
});

test("the status action on a dial shows the same, and goes amber when DisplayDeck is offline", async () => {
	await withHarness(async (h) => {
		h.appear(A.status, "s2", {}, "Encoder");
		await h.waitFor(feedbackFor("s2", (p) => p.value?.value === "Desk" && p.hint?.value === "Main Out 1-2"));
	});
	await withHarness(async (h) => {
		h.appear(A.status, "s3", {}, "Neo");
		await h.waitFor(feedbackFor("s3", (p) => p.profile?.value === "DisplayDeck offline" && p.profile?.color === "#f59e0b"));
	}, { startStopped: true });
});

test("dials repaint on their own when the active profile changes elsewhere", async () => {
	await withHarness(async (h, stub) => {
		h.appear(A.profileDial, "d1", {}, "Encoder");
		await h.waitFor(feedbackFor("d1", (p) => p.value?.value === "Desk"));
		stub.state.current = "Den TV";
		await h.waitFor(feedbackFor("d1", (p) => p.value?.value === "Den TV" && p.value?.color === "#22c55e"), 9000);
	});
});

// ---------- optional: read-only check against a real DisplayDeck ----------
// Set DISPLAYDECK_LIVE_URL (for example http://127.0.0.1:8765) to run it. It paints keys and fills the inspector
// dropdowns from the real server, but never presses a key, so nothing on the PC is changed.
test("live (read-only): keys and inspectors work against a real DisplayDeck", { skip: !process.env.DISPLAYDECK_LIVE_URL && "set DISPLAYDECK_LIVE_URL to run" }, async () => {
	const url = process.env.DISPLAYDECK_LIVE_URL!;
	const status = await (await fetch(url + "/api/current")).json() as { profiles: string[]; current: string | null; lastApplied: string | null };
	const devices = (await (await fetch(url + "/api/audio-devices")).json() as { devices: Array<{ id: string; name: string; isDefault: boolean; formats: Array<{ name: string }> }> }).devices;
	assert.ok(status.profiles.length > 0, "the real server should have at least one profile");

	const host = await startHost(url);
	try {
		// every real profile gets a key; the active one is ringed, the others are not
		status.profiles.forEach((p, i) => host.appear(A.profile, `p${i}`, { profile: p }));
		for (const [i, p] of status.profiles.entries()) {
			const wantRing = status.current === p || (status.current === null && status.lastApplied === p);
			const msg = await host.waitFor((m) => imageFor(`p${i}`)(m) && decodeImage(m.payload.image).includes("#22c55e") === wantRing, 8000);
			assert.ok(msg, `profile ${p} should ${wantRing ? "" : "not "}be ringed`);
		}

		// the default audio device is ringed
		const def = devices.find((d) => d.isDefault);
		if (def) {
			host.appear(A.audio, "aud", { device: def.id, label: def.name, spatial: "keep" });
			await host.waitFor((m) => imageFor("aud")(m) && decodeImage(m.payload.image).includes("#22c55e"), 8000);
		}

		// the inspectors list the real data
		host.inspectorOpens(A.profile, "p0");
		host.fromInspector(A.profile, "p0", { event: "getProfiles" });
		const reply = await host.waitFor((m) => m.event === "sendToPropertyInspector" && m.payload?.event === "getProfiles");
		assert.deepEqual(reply.payload.items.map((i: { value: string }) => i.value), status.profiles);

		assert.equal(host.received.filter((m) => m.event === "showOk" || m.event === "showAlert").length, 0, "no key was pressed");
	} finally {
		await host.close();
	}
});
