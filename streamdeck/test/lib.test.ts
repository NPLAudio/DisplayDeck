// Unit tests for the plugin's pure modules. Run with Node 24's built-in TypeScript support: npm test
import test from "node:test";
import assert from "node:assert/strict";

import { DisplayDeckApi, DisplayDeckError } from "../src/lib/api.ts";
import { isActiveProfile, nextProfile, spatialArgument } from "../src/lib/logic.ts";
import { escapeXml, keyImage, wrapLabel } from "../src/lib/render.ts";

// ---------- logic ----------

test("nextProfile moves to the following profile and wraps", () => {
	const s = { profiles: ["Den TV", "Desk", "Living Room"], current: "Desk", lastApplied: null };
	assert.equal(nextProfile(s), "Living Room");
	assert.equal(nextProfile({ ...s, current: "Living Room" }), "Den TV");
});

test("nextProfile falls back to the last applied profile, then to the first", () => {
	const s = { profiles: ["A", "B", "C"], current: null, lastApplied: "B" };
	assert.equal(nextProfile(s), "C");
	assert.equal(nextProfile({ ...s, lastApplied: null }), "A");
	assert.equal(nextProfile({ ...s, lastApplied: "gone" }), "A");
	assert.equal(nextProfile({ profiles: [], current: null, lastApplied: null }), null);
});

test("isActiveProfile trusts the live match first, then the last applied one", () => {
	assert.equal(isActiveProfile({ profiles: [], current: "Desk", lastApplied: "Den TV" }, "Desk"), true);
	assert.equal(isActiveProfile({ profiles: [], current: "Desk", lastApplied: "Den TV" }, "Den TV"), false);
	assert.equal(isActiveProfile({ profiles: [], current: null, lastApplied: "Den TV" }, "Den TV"), true);
	assert.equal(isActiveProfile({ profiles: [], current: null, lastApplied: null }, "Den TV"), false);
	assert.equal(isActiveProfile({ profiles: [], current: "Desk", lastApplied: null }, undefined), false);
});

test("spatialArgument treats keep and empty as 'do not change'", () => {
	assert.equal(spatialArgument(undefined), undefined);
	assert.equal(spatialArgument(""), undefined);
	assert.equal(spatialArgument("keep"), undefined);
	assert.equal(spatialArgument("off"), "off");
	assert.equal(spatialArgument("Dolby Atmos for Home Theater"), "Dolby Atmos for Home Theater");
});

// ---------- render ----------

test("wrapLabel breaks at spaces, splits long words, and caps the line count", () => {
	assert.deepEqual(wrapLabel("Living Room", 9, 3), ["Living", "Room"]);
	assert.deepEqual(wrapLabel("Desk", 9, 3), ["Desk"]);
	assert.deepEqual(wrapLabel("Extraordinarily", 9, 3), ["Extraordi", "narily"]);
	const capped = wrapLabel("one two three four five six seven eight nine", 9, 2);
	assert.equal(capped.length, 2);
	assert.ok(capped[1]!.endsWith("…"));
	assert.deepEqual(wrapLabel("   ", 9, 3), []);
});

test("escapeXml neutralizes markup in profile names", () => {
	assert.equal(escapeXml(`<b>"R&D" 'x'</b>`), "&lt;b&gt;&quot;R&amp;D&quot; &apos;x&apos;&lt;/b&gt;");
});

test("keyImage produces a data URI with the label, and marks active and offline keys", () => {
	const decode = (uri: string) => decodeURIComponent(uri.replace("data:image/svg+xml;charset=utf8,", ""));
	const plain = keyImage({ label: "Desk" });
	assert.ok(plain.startsWith("data:image/svg+xml;charset=utf8,"));
	const svg = decode(plain);
	assert.ok(svg.includes(">Desk<"));
	assert.ok(!svg.includes("#22c55e"), "inactive keys have no accent ring");

	assert.ok(decode(keyImage({ label: "Desk", active: true })).includes("#22c55e"));
	assert.ok(decode(keyImage({ label: "Desk", offline: true })).includes(">!<"));
	assert.ok(decode(keyImage({ label: "Undo", icon: "undo" })).includes("<path"));

	const hostile = decode(keyImage({ label: "<script>alert(1)</script>" }));
	assert.ok(!hostile.includes("<script>"), "labels must be escaped");
	// the result must be well-formed enough to parse as XML-ish: every tag opened is closed
	assert.equal((svg.match(/<svg/g) ?? []).length, 1);
	assert.equal((svg.match(/<\/svg>/g) ?? []).length, 1);
});

// ---------- api ----------

type Call = { url: string; method?: string; body?: string };

function fakeFetch(responses: Array<{ ok?: boolean; status?: number; body?: unknown } | "offline">, calls: Call[]) {
	let i = 0;
	return async (url: string, init?: { method?: string; body?: string }) => {
		calls.push({ url, method: init?.method, body: init?.body });
		const r = responses[i++] ?? { body: {} };
		if (r === "offline") throw new TypeError("fetch failed");
		return { ok: r.ok ?? true, status: r.status ?? 200, json: async () => r.body ?? {} };
	};
}

test("api builds the right requests", async () => {
	const calls: Call[] = [];
	const api = new DisplayDeckApi("http://127.0.0.1:8765/", fakeFetch([{ body: { profiles: ["A"], current: "A", lastApplied: null, liveAvailable: true } }, {}, {}, { body: { devices: [] } }, {}, {}], calls));
	assert.equal((await api.status()).current, "A");
	await api.apply("Living Room/TV");
	await api.undo();
	await api.audioDevices();
	await api.applyAudio("UltraLite-mk5\\Device\\Main Out 1-2\\Render", "off");
	await api.applyAudio("Dev");

	assert.equal(calls[0]!.url, "http://127.0.0.1:8765/api/current");
	assert.equal(calls[0]!.method, "GET");
	assert.equal(calls[1]!.url, "http://127.0.0.1:8765/api/apply/Living%20Room%2FTV");
	assert.equal(calls[1]!.method, "POST");
	assert.equal(calls[2]!.url, "http://127.0.0.1:8765/api/undo-layout");
	assert.equal(calls[3]!.url, "http://127.0.0.1:8765/api/audio-devices");
	assert.deepEqual(JSON.parse(calls[4]!.body!), { device: "UltraLite-mk5\\Device\\Main Out 1-2\\Render", spatial: "off" });
	assert.deepEqual(JSON.parse(calls[5]!.body!), { device: "Dev" }, "spatial is omitted when not given");
});

test("api reports the server's own error message", async () => {
	const api = new DisplayDeckApi("http://x", fakeFetch([{ ok: false, status: 404, body: { error: "No such profile: Nope" } }], []));
	await assert.rejects(() => api.apply("Nope"), (e: unknown) => e instanceof DisplayDeckError && e.message === "No such profile: Nope" && e.offline === false);
});

test("api falls back to the status code when the error has no message", async () => {
	const api = new DisplayDeckApi("http://x", fakeFetch([{ ok: false, status: 500, body: {} }], []));
	await assert.rejects(() => api.undo(), /returned 500/);
});

test("api marks an unreachable server as offline", async () => {
	const api = new DisplayDeckApi("http://x", fakeFetch(["offline"], []));
	await assert.rejects(() => api.status(), (e: unknown) => e instanceof DisplayDeckError && e.offline === true && /not running/.test(e.message));
});

test("every key pictogram renders, and the layout pictogram carries the green active pane", () => {
	const decode = (uri: string) => decodeURIComponent(uri.replace("data:image/svg+xml;charset=utf8,", ""));
	for (const icon of ["layout", "next", "undo", "speaker"] as const) {
		const svg = decode(keyImage({ label: "X", icon }));
		assert.ok(svg.includes('transform="translate(44,14) scale(0.56)"'), `${icon} is placed in the key`);
	}
	const layout = decode(keyImage({ label: "Choose a profile", icon: "layout" }));
	assert.equal((layout.match(/<rect x="/g) ?? []).length, 3, "three panes (the key background has no x attribute)");
	assert.ok(layout.includes("#22c55e"), "one pane is the green active accent");
});
