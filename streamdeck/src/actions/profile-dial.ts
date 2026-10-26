import { action, DialAction, DialDownEvent, DialRotateEvent, SingletonAction, TouchTapEvent, WillAppearEvent, WillDisappearEvent } from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";

import { dialFeedback, selectorView } from "../lib/feedback";
import { stepSelection } from "../lib/logic";
import { PendingStore } from "../lib/pending";
import { iconImage } from "../lib/render";
import { api, hub, log } from "../runtime";

/**
 * A Stream Deck + dial: turn to browse your saved profiles, press (or tap the touch strip) to apply the one shown.
 * The name is green while it is the active profile. A choice that is never confirmed expires after a few seconds.
 */
@action({ UUID: "com.nplaudio.displaydeck.profile-dial" })
export class ProfileDial extends SingletonAction {
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
		this.releases.set(ev.action.id, hub.retain("status"));
		await this.paint(ev.action);
	}

	override onWillDisappear(ev: WillDisappearEvent): void {
		this.releases.get(ev.action.id)?.();
		this.releases.delete(ev.action.id);
		this.pending.clear(ev.action.id);
	}

	override async onDialRotate(ev: DialRotateEvent): Promise<void> {
		const status = hub.state.status;
		if (!status || hub.state.offline) return;
		const active = status.current ?? status.lastApplied;
		const next = stepSelection(status.profiles, this.pending.get(ev.action.id), active, ev.payload.ticks);
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
		const status = hub.state.status;
		if (choice === null || !status || choice === (status.current ?? status.lastApplied)) return; // nothing new to apply
		this.busy.add(dial.id);
		await this.paint(dial);
		try {
			await api.apply(choice);
			this.pending.clear(dial.id);
		} catch (error) {
			log.error(`Applying "${choice}" failed: ${error instanceof Error ? error.message : String(error)}`);
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
		const { status, offline } = hub.state;
		const view = selectorView({
			noun: "profile",
			labels: {},
			items: status?.profiles ?? [],
			active: status ? (status.current ?? status.lastApplied) : null,
			pending: this.pending.get(dial.id),
			offline,
			busy: this.busy.has(dial.id),
		});
		await dial.setFeedback(dialFeedback({ title: "Profile", icon: iconImage("layout"), ...view }));
	}
}
