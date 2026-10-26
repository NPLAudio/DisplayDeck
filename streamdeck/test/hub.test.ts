import test from "node:test";
import assert from "node:assert/strict";

import { StatusHub, type HubApi, type HubState } from "../src/lib/hub.ts";

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

function makeApi() {
	const calls = { status: 0, devices: 0 };
	let current: string | null = "Desk";
	let fail = false;
	const api: HubApi = {
		async status() {
			calls.status++;
			if (fail) throw new Error("offline");
			return { profiles: ["Desk", "Den TV"], current, lastApplied: null };
		},
		async audioDevices() {
			calls.devices++;
			if (fail) throw new Error("offline");
			return [{ id: "A", name: "A", deviceName: "Adapter", isDefault: true, formats: [] }];
		},
	};
	return { api, calls, setCurrent: (c: string | null) => (current = c), setFail: (f: boolean) => (fail = f) };
}

test("does not poll until a key needs it, and stops when the last key goes away", async () => {
	const { api, calls } = makeApi();
	const hub = new StatusHub(api, 20);
	await sleep(60);
	assert.equal(calls.status, 0);

	const release = hub.retain("status");
	await sleep(90);
	const during = calls.status;
	assert.ok(during >= 3, `expected several polls, got ${during}`);

	release();
	await sleep(30);
	const after = calls.status;
	await sleep(80);
	assert.equal(calls.status, after, "polling stops after release");
	assert.equal(hub.active, false);
});

test("audio devices are only fetched while an audio key is visible", async () => {
	const { api, calls } = makeApi();
	const hub = new StatusHub(api, 20);
	const r1 = hub.retain("status");
	await sleep(50);
	assert.equal(calls.devices, 0);
	const r2 = hub.retain("audio");
	await sleep(50);
	assert.ok(calls.devices >= 1);
	assert.equal(hub.state.devices?.[0]?.id, "A");
	r1(); r2();
});

test("listeners hear about changes only, not every poll", async () => {
	const { api, setCurrent } = makeApi();
	const hub = new StatusHub(api, 15);
	const seen: HubState[] = [];
	hub.onChange((s) => seen.push(s));
	const release = hub.retain("status");
	await sleep(80);
	assert.equal(seen.length, 1, "unchanged polls are silent");
	assert.equal(seen[0]!.status?.current, "Desk");

	setCurrent("Den TV");
	await sleep(60);
	assert.equal(seen.length, 2);
	assert.equal(seen[1]!.status?.current, "Den TV");
	release();
});

test("an unreachable server is reported as offline, then recovers", async () => {
	const { api, setFail } = makeApi();
	const hub = new StatusHub(api, 15);
	const seen: boolean[] = [];
	hub.onChange((s) => seen.push(s.offline));
	setFail(true);
	const release = hub.retain("status");
	await sleep(50);
	assert.deepEqual(seen, [true]);
	setFail(false);
	await sleep(50);
	assert.deepEqual(seen, [true, false]);
	release();
});

test("release is safe to call twice", async () => {
	const { api } = makeApi();
	const hub = new StatusHub(api, 20);
	const r = hub.retain("status");
	r(); r();
	assert.equal(hub.active, false);
	const r2 = hub.retain("status");
	assert.equal(hub.active, true);
	r2();
});

test("refresh() pushes a fresh state immediately", async () => {
	const { api, setCurrent } = makeApi();
	const hub = new StatusHub(api, 10_000);
	const release = hub.retain("status");
	await sleep(30);
	setCurrent("Den TV");
	await hub.refresh();
	assert.equal(hub.state.status?.current, "Den TV");
	release();
});
