import { action, DialAction, DialDownEvent, DialRotateEvent, SingletonAction, TouchTapEvent, WillAppearEvent, WillDisappearEvent } from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";

import { dialFeedback, selectorView } from "../lib/feedback";
import { stepSelection } from "../lib/logic";
import { PendingStore } from "../lib/pending";
import { iconImage } from "../lib/render";
import { api, hub, log } from "../runtime";

/**
 * A Stream Deck + dial: turn to browse your playback devices, press (or tap the touch strip) to make the one shown
 * the default. The name is green while it is the default. The spatial format is left as it is.
 */
@action({ UUID: "com.nplaudio.displaydeck.audio-dial" })
export class AudioDial extends SingletonAction {
	private readonly releases = new Map<string, () => void>();
	private readonly busy = new Set<string>();
	private readonly pending = new PendingStore(6000, () => void this.repaintAll());

	constructor() {
		super();
		hub.onChange(() => void this.repaintAll());
	}

	override async onWillAppear(ev: WillAppearEvent): Promise<void> {
		if (!ev.action.isDial()) return;
		this.releases.get(ev.action.id)?.();
		this.releases.set(ev.action.id, hub.retain("audio"));
		await this.paint(ev.action);
	}

	override onWillDisappear(ev: WillDisappearEvent): void {
		this.releases.get(ev.action.id)?.();
		this.releases.delete(ev.action.id);
		this.pending.clear(ev.action.id);
	}

	override async onDialRotate(ev: DialRotateEvent): Promise<void> {
		const devices = hub.state.devices;
		if (!devices || hub.state.offline) return;
		const active = devices.find((d) => d.isDefault)?.id ?? null;
		const next = stepSelection(devices.map((d) => d.id), this.pending.get(ev.action.id), active, ev.payload.ticks);
		if (next !== null) this.pending.set(ev.action.id, next);
		await this.paint(ev.action);
	}

	override async onDialDown(ev: DialDownEvent): Promise<void> {
		await this.commit(ev.action);
	}

	override async onTouchTap(ev: TouchTapEvent): Promise<void> {
		if (ev.action.isDial()) await this.commit(ev.action);
	}

	private async commit(dial: DialAction<JsonObject>): Promise<void> {
		const choice = this.pending.get(dial.id);
		const current = hub.state.devices?.find((d) => d.isDefault)?.id ?? null;
		if (choice === null || choice === current) return;
		this.busy.add(dial.id);
		await this.paint(dial);
		try {
			await api.applyAudio(choice);
			this.pending.clear(dial.id);
		} catch (error) {
			log.error(`Setting the default device failed: ${error instanceof Error ? error.message : String(error)}`);
			await dial.showAlert();
		} finally {
			this.busy.delete(dial.id);
			await hub.refresh();
			await this.paint(dial);
		}
	}

	private async repaintAll(): Promise<void> {
		for (const a of this.actions) if (a.isDial()) await this.paint(a);
	}

	private async paint(dial: DialAction<JsonObject>): Promise<void> {
		const { devices, offline } = hub.state;
		const view = selectorView({
			noun: "device",
			labels: Object.fromEntries((devices ?? []).map((d) => [d.id, d.name])),
			items: (devices ?? []).map((d) => d.id),
			active: devices?.find((d) => d.isDefault)?.id ?? null,
			pending: this.pending.get(dial.id),
			offline,
			busy: this.busy.has(dial.id),
		});
		await dial.setFeedback(dialFeedback({ title: "Audio output", icon: iconImage("speaker"), ...view }));
	}
}
