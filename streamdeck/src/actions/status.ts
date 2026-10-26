import { action, DialAction, DialDownEvent, NeoInfobarAction, SingletonAction, TouchTapEvent, WillAppearEvent, WillDisappearEvent } from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";

import { dialFeedback, infobarFeedback } from "../lib/feedback";
import { iconImage } from "../lib/render";
import { hub } from "../runtime";

type Surface = DialAction<JsonObject> | NeoInfobarAction<JsonObject>;

/**
 * A read-only status display: the current profile and the default playback device. It works on the Stream Deck Neo
 * info bar and on a Stream Deck + dial (where pressing or tapping refreshes it).
 */
@action({ UUID: "com.nplaudio.displaydeck.status" })
export class Status extends SingletonAction {
	// Our own registry: the SDK's `actions` list is typed for keys and dials only.
	private readonly surfaces = new Map<string, Surface>();
	private readonly releases = new Map<string, Array<() => void>>();

	constructor() {
		super();
		hub.onChange(() => void this.repaintAll());
	}

	override async onWillAppear(ev: WillAppearEvent): Promise<void> {
		const a = ev.action;
		if (!a.isDial() && !a.isNeoInfobar()) return;
		this.release(a.id);
		this.surfaces.set(a.id, a);
		this.releases.set(a.id, [hub.retain("status"), hub.retain("audio")]);
		if (a.isNeoInfobar()) await a.setFeedbackLayout("layouts/infobar.json");
		await this.paint(a);
	}

	override onWillDisappear(ev: WillDisappearEvent): void {
		this.release(ev.action.id);
	}

	override async onDialDown(_ev: DialDownEvent): Promise<void> {
		await hub.refresh();
	}

	override async onTouchTap(_ev: TouchTapEvent): Promise<void> {
		await hub.refresh();
	}

	private release(id: string): void {
		for (const r of this.releases.get(id) ?? []) r();
		this.releases.delete(id);
		this.surfaces.delete(id);
	}

	private async repaintAll(): Promise<void> {
		for (const s of this.surfaces.values()) await this.paint(s);
	}

	private async paint(surface: Surface): Promise<void> {
		const { status, devices, offline } = hub.state;
		const audio = offline ? "Start DisplayDeck" : (devices?.find((d) => d.isDefault)?.name ?? "");
		let profile: string;
		if (offline) profile = "DisplayDeck offline";
		else if (!status) profile = "...";
		else if (status.current) profile = status.current;
		else if (status.lastApplied) profile = `${status.lastApplied} (last)`;
		else profile = "No active profile";
		const active = !offline && !!status?.current;
		const icon = iconImage("layout");

		if (surface.isNeoInfobar()) {
			await surface.setFeedback(infobarFeedback({ profile, audio, active, offline, icon }));
		} else if (surface.isDial()) {
			await surface.setFeedback(dialFeedback({ title: "Current", value: profile, hint: audio, tone: offline ? "warn" : active ? "active" : "normal", icon }));
		}
	}
}
