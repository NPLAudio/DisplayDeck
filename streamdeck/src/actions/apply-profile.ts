import streamDeck, { action, KeyAction, KeyDownEvent, SendToPluginEvent, SingletonAction, WillAppearEvent, WillDisappearEvent, DidReceiveSettingsEvent } from "@elgato/streamdeck";
import type { JsonObject, JsonValue } from "@elgato/utils";

import { isActiveProfile } from "../lib/logic";
import { keyImage } from "../lib/render";
import { api, hub, press } from "../runtime";

type Settings = { profile?: string };

/**
 * A key that applies one saved DisplayDeck profile. It shows the profile's name and a green ring while that
 * profile is the active one.
 */
@action({ UUID: "com.nplaudio.displaydeck.profile" })
export class ApplyProfile extends SingletonAction<Settings> {
	private readonly releases = new Map<string, () => void>();

	constructor() {
		super();
		hub.onChange(() => void this.repaintAll());
	}

	override async onWillAppear(ev: WillAppearEvent<Settings>): Promise<void> {
		if (!ev.action.isKey()) return;
		this.releases.get(ev.action.id)?.();
		this.releases.set(ev.action.id, hub.retain("status"));
		await this.paint(ev.action, ev.payload.settings);
	}

	override onWillDisappear(ev: WillDisappearEvent<Settings>): void {
		this.releases.get(ev.action.id)?.();
		this.releases.delete(ev.action.id);
	}

	override async onDidReceiveSettings(ev: DidReceiveSettingsEvent<Settings>): Promise<void> {
		if (ev.action.isKey()) await this.paint(ev.action, ev.payload.settings);
	}

	override async onKeyDown(ev: KeyDownEvent<Settings>): Promise<void> {
		const profile = ev.payload.settings.profile;
		if (!profile) {
			await ev.action.showAlert();
			return;
		}
		await press(ev.action, () => api.apply(profile));
	}

	/** Feeds the profile dropdown in the property inspector. */
	override async onSendToPlugin(ev: SendToPluginEvent<JsonValue, Settings>): Promise<void> {
		const payload = ev.payload as JsonObject;
		if (payload?.event !== "getProfiles") return;

		let items: JsonObject[];
		try {
			const status = await api.status();
			items = status.profiles.map((name) => ({ label: name, value: name }));
			if (items.length === 0) items = [{ label: "No saved profiles yet", value: "", disabled: true }];
		} catch {
			items = [{ label: "DisplayDeck is not running", value: "", disabled: true }];
		}
		await streamDeck.ui.sendToPropertyInspector({ event: "getProfiles", items });
	}

	private async repaintAll(): Promise<void> {
		for (const a of this.actions) {
			if (a.isKey()) await this.paint(a, await a.getSettings());
		}
	}

	private async paint(key: KeyAction<Settings>, settings: Settings): Promise<void> {
		const { status, offline } = hub.state;
		const profile = settings.profile;
		const active = !!status && isActiveProfile(status, profile);
		const missing = !!status && !!profile && !status.profiles.includes(profile);
		await key.setTitle("");
		await key.setImage(
			keyImage({
				label: profile ?? "Choose a profile",
				caption: offline ? "offline" : missing ? "missing" : active ? "active" : undefined,
				active: active && !offline,
				offline: offline || missing,
				icon: profile ? undefined : "layout",
			}),
		);
	}
}
