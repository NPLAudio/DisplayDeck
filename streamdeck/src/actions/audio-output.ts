import streamDeck, { action, DidReceiveSettingsEvent, KeyAction, KeyDownEvent, SendToPluginEvent, SingletonAction, WillAppearEvent, WillDisappearEvent } from "@elgato/streamdeck";
import type { JsonObject, JsonValue } from "@elgato/utils";

import { spatialArgument } from "../lib/logic";
import { keyImage } from "../lib/render";
import { api, hub, press } from "../runtime";

type Settings = { device?: string; label?: string; spatial?: string };

/** Dropdown items for the spatial format: what the chosen device offers (or every device's, if none chosen). */
function formatItems(devices: Array<{ id: string; formats: Array<{ name: string }> }>, deviceId: string | undefined): JsonObject[] {
	const chosen = devices.find((d) => d.id === deviceId);
	const names = new Set<string>();
	for (const d of chosen ? [chosen] : devices) for (const f of d.formats) names.add(f.name);
	return [
		{ label: "Leave as it is", value: "keep" },
		{ label: "Off", value: "off" },
		...[...names].map((n) => ({ label: n, value: n })),
	];
}

/**
 * A key that makes one playback device the default (and optionally sets its spatial sound format),
 * independent of any display profile. Shows a green ring while that device is the default.
 */
@action({ UUID: "com.nplaudio.displaydeck.audio" })
export class AudioOutput extends SingletonAction<Settings> {
	private readonly releases = new Map<string, () => void>();

	constructor() {
		super();
		hub.onChange(() => void this.repaintAll());
	}

	override async onWillAppear(ev: WillAppearEvent<Settings>): Promise<void> {
		if (!ev.action.isKey()) return;
		this.releases.get(ev.action.id)?.();
		this.releases.set(ev.action.id, hub.retain("audio"));
		await this.paint(ev.action, ev.payload.settings);
	}

	override onWillDisappear(ev: WillDisappearEvent<Settings>): void {
		this.releases.get(ev.action.id)?.();
		this.releases.delete(ev.action.id);
	}

	override async onDidReceiveSettings(ev: DidReceiveSettingsEvent<Settings>): Promise<void> {
		if (!ev.action.isKey()) return;
		// Remember a readable name for the chosen device, so the key can show it even when the device is unplugged.
		const settings = ev.payload.settings;
		const device = hub.state.devices?.find((d) => d.id === settings.device);
		if (device && settings.label !== device.name) {
			await ev.action.setSettings({ ...settings, label: device.name });
			return; // the settings change triggers another event, which paints
		}
		await this.paint(ev.action, settings);

		// If the inspector is open on this key, refresh its spatial list for the newly chosen device.
		if (streamDeck.ui.action?.id === ev.action.id && hub.state.devices) {
			await streamDeck.ui.sendToPropertyInspector({ event: "getFormats", items: formatItems(hub.state.devices, settings.device) });
		}
	}

	override async onKeyDown(ev: KeyDownEvent<Settings>): Promise<void> {
		const { device, spatial } = ev.payload.settings;
		if (!device) {
			await ev.action.showAlert();
			return;
		}
		await press(ev.action, () => api.applyAudio(device, spatialArgument(spatial)));
	}

	/** Feeds the device and spatial-format dropdowns in the property inspector. */
	override async onSendToPlugin(ev: SendToPluginEvent<JsonValue, Settings>): Promise<void> {
		const payload = ev.payload as JsonObject;
		const event = payload?.event;
		if (event !== "getDevices" && event !== "getFormats") return;

		let devices;
		try {
			devices = await api.audioDevices();
		} catch {
			const unavailable = [{ label: "DisplayDeck is not running", value: "", disabled: true }];
			await streamDeck.ui.sendToPropertyInspector({ event, items: unavailable });
			return;
		}

		if (event === "getDevices") {
			const items = devices.map((d) => ({ label: `${d.name} · ${d.deviceName}`, value: d.id }));
			await streamDeck.ui.sendToPropertyInspector({ event, items });
			return;
		}

		const settings = await ev.action.getSettings();
		await streamDeck.ui.sendToPropertyInspector({ event, items: formatItems(devices, settings.device) });
	}

	private async repaintAll(): Promise<void> {
		for (const a of this.actions) {
			if (a.isKey()) await this.paint(a, await a.getSettings());
		}
	}

	private async paint(key: KeyAction<Settings>, settings: Settings): Promise<void> {
		const { devices, offline } = hub.state;
		const device = devices?.find((d) => d.id === settings.device);
		const active = !!device?.isDefault && !offline;
		await key.setTitle("");
		await key.setImage(
			keyImage({
				label: settings.label ?? (settings.device ? "Audio output" : "Choose a device"),
				caption: offline ? "offline" : devices && settings.device && !device ? "not found" : active ? "default" : undefined,
				active,
				offline: offline || (!!devices && !!settings.device && !device),
				icon: "speaker",
			}),
		);
	}
}
