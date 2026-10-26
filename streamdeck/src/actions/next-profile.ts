import { action, KeyDownEvent, SingletonAction, WillAppearEvent, WillDisappearEvent } from "@elgato/streamdeck";

import { nextProfile } from "../lib/logic";
import { keyImage } from "../lib/render";
import { api, hub, press } from "../runtime";

/** A key that applies the next saved profile, wrapping around. Handy when there are more profiles than keys. */
@action({ UUID: "com.nplaudio.displaydeck.next" })
export class NextProfile extends SingletonAction {
	private readonly releases = new Map<string, () => void>();

	constructor() {
		super();
		hub.onChange(() => void this.repaintAll());
	}

	override async onWillAppear(ev: WillAppearEvent): Promise<void> {
		if (!ev.action.isKey()) return;
		this.releases.get(ev.action.id)?.();
		this.releases.set(ev.action.id, hub.retain("status"));
		await this.paint();
	}

	override onWillDisappear(ev: WillDisappearEvent): void {
		this.releases.get(ev.action.id)?.();
		this.releases.delete(ev.action.id);
	}

	override async onKeyDown(ev: KeyDownEvent): Promise<void> {
		await press(ev.action, async () => {
			const status = await api.status();
			const next = nextProfile(status);
			if (next === null) throw new Error("There are no saved profiles");
			await api.apply(next);
		});
	}

	private async repaintAll(): Promise<void> {
		await this.paint();
	}

	private async paint(): Promise<void> {
		const { status, offline } = hub.state;
		const now = status?.current ?? status?.lastApplied ?? null;
		const image = keyImage({
			label: "Next",
			caption: offline ? "offline" : (now ?? undefined),
			icon: "next",
			offline,
		});
		for (const a of this.actions) {
			if (a.isKey()) {
				await a.setTitle("");
				await a.setImage(image);
			}
		}
	}
}
