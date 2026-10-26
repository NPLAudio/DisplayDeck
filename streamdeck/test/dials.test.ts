import test from "node:test";
import assert from "node:assert/strict";

import { dialFeedback, infobarFeedback, selectorView } from "../src/lib/feedback.ts";
import { stepSelection } from "../src/lib/logic.ts";
import { PendingStore } from "../src/lib/pending.ts";
import { iconImage } from "../src/lib/render.ts";

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));
const items = ["Den TV", "Desk", "Living Room"];

// ---------- stepSelection ----------

test("stepSelection starts from the active item and wraps both ways", () => {
	assert.equal(stepSelection(items, null, "Desk", 1), "Living Room");
	assert.equal(stepSelection(items, null, "Desk", -1), "Den TV");
	assert.equal(stepSelection(items, null, "Living Room", 1), "Den TV", "wraps forward");
	assert.equal(stepSelection(items, null, "Den TV", -1), "Living Room", "wraps backward");
});

test("stepSelection continues from the pending choice, and handles big or fast turns", () => {
	assert.equal(stepSelection(items, "Living Room", "Desk", 1), "Den TV");
	assert.equal(stepSelection(items, "Den TV", "Desk", 5), "Living Room"); // 0 + 5 -> index 2
	assert.equal(stepSelection(items, "Den TV", "Desk", -7), "Living Room");
});

test("stepSelection with nothing active picks the first (or last, turning back) item", () => {
	assert.equal(stepSelection(items, null, null, 1), "Den TV");
	assert.equal(stepSelection(items, null, null, -1), "Living Room");
	assert.equal(stepSelection(items, null, "Gone", 1), "Den TV", "an active item that no longer exists is ignored");
	assert.equal(stepSelection([], null, null, 1), null);
});

// ---------- selectorView ----------

const base = { noun: "profile", labels: {}, items, active: "Desk", pending: null, offline: false };

test("an idle dial shows the active item in green and invites a turn", () => {
	assert.deepEqual(selectorView(base), { value: "Desk", hint: "Turn to choose", tone: "active" });
});

test("a turned dial shows the pending item and asks for a press", () => {
	assert.deepEqual(selectorView({ ...base, pending: "Living Room" }), { value: "Living Room", hint: "Press to apply", tone: "normal" });
	assert.deepEqual(selectorView({ ...base, noun: "device", pending: "Living Room" }).hint, "Press to make default");
});

test("turning back to the active item reads as active, not as a pending choice", () => {
	assert.deepEqual(selectorView({ ...base, pending: "Desk" }), { value: "Desk", hint: "Active", tone: "active" });
});

test("selectorView uses readable labels, a busy state, and the edge cases", () => {
	assert.equal(selectorView({ ...base, noun: "device", labels: { Desk: "Desk speakers" }, pending: null }).value, "Desk speakers");
	assert.equal(selectorView({ ...base, pending: "Living Room", busy: true }).hint, "Applying...");
	assert.deepEqual(selectorView({ ...base, offline: true }), { value: "DisplayDeck offline", hint: "Start DisplayDeck", tone: "warn" });
	assert.equal(selectorView({ ...base, items: [] }).value, "No profiles");
	assert.equal(selectorView({ ...base, active: null }).value, "Choose a profile");
});

// ---------- feedback payloads ----------

test("dialFeedback fills the dial layout's items with colors for the tone", () => {
	const f = dialFeedback({ title: "Profile", value: "Desk", hint: "Active", tone: "active", icon: "data:x" }) as Record<string, { value: string; color?: string }>;
	assert.equal(f.title!.value, "PROFILE");
	assert.equal(f.value!.value, "Desk");
	assert.equal(f.value!.color, "#22c55e");
	assert.equal(f.hint!.value, "Active");
	assert.equal(f.icon!.value, "data:x");
	assert.equal((dialFeedback({ title: "t", value: "v", hint: "h" }) as any).icon, undefined, "no icon item unless given");
	assert.equal((dialFeedback({ title: "t", value: "v", hint: "h", tone: "warn" }) as any).hint.color, "#f59e0b");
});

test("infobarFeedback shows the profile and the audio device, coloured by state", () => {
	const ok = infobarFeedback({ profile: "Desk", audio: "Main Out 1-2", active: true }) as Record<string, { value: string; color?: string }>;
	assert.deepEqual([ok.profile!.value, ok.profile!.color, ok.audio!.value], ["Desk", "#22c55e", "Main Out 1-2"]);
	const off = infobarFeedback({ profile: "DisplayDeck offline", audio: "", offline: true }) as Record<string, { value: string; color?: string }>;
	assert.equal(off.profile!.color, "#f59e0b");
});

test("iconImage returns an SVG data URI that carries the pictogram", () => {
	const uri = iconImage("speaker", "#22c55e");
	assert.ok(uri.startsWith("data:image/svg+xml;charset=utf8,"));
	assert.ok(decodeURIComponent(uri).includes('color="#22c55e"'));
});

// ---------- PendingStore ----------

test("a pending choice expires on its own and tells the dial to repaint", async () => {
	const expired: string[] = [];
	const store = new PendingStore(30, (id) => expired.push(id));
	store.set("d1", "Desk");
	assert.equal(store.get("d1"), "Desk");
	await sleep(80);
	assert.equal(store.get("d1"), null);
	assert.deepEqual(expired, ["d1"]);
});

test("turning again restarts the clock, and clearing cancels the expiry", async () => {
	const expired: string[] = [];
	const store = new PendingStore(60, (id) => expired.push(id));
	store.set("d1", "A");
	await sleep(40);
	store.set("d1", "B"); // restarts
	await sleep(40);
	assert.equal(store.get("d1"), "B", "still pending: the clock restarted");
	store.clear("d1");
	await sleep(100);
	assert.deepEqual(expired, [], "a cleared choice never expires");
	assert.equal(store.get("d1"), null);
});
