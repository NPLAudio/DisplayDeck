import { action, KeyDownEvent, SingletonAction, WillAppearEvent } from "@elgato/streamdeck";

import { keyImage } from "../lib/render";
import { api, press } from "../runtime";

/** A key that goes back to the display layout from before the last change. */
@action({ UUID: "com.nplaudio.displaydeck.undo" })
export class UndoLayout extends SingletonAction {
	override async onWillAppear(ev: WillAppearEvent): Promise<void> {
		if (!ev.action.isKey()) return;
		await ev.action.setTitle("");
		await ev.action.setImage(keyImage({ label: "Undo layout", icon: "undo" }));
	}

	override async onKeyDown(ev: KeyDownEvent): Promise<void> {
		await press(ev.action, () => api.undo());
	}
}
