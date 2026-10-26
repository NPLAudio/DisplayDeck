import { action, DialAction, DialDownEvent, KeyDownEvent, SingletonAction, TouchTapEvent, WillAppearEvent } from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";

import { dialFeedback } from "../lib/feedback";
import { iconImage, keyImage } from "../lib/render";
import { api, press } from "../runtime";

/** Goes back to the display layout from before the last change. Works on keys and on Stream Deck + dials. */
@action({ UUID: "com.nplaudio.displaydeck.undo" })
export class UndoLayout extends SingletonAction {
	override async onWillAppear(ev: WillAppearEvent): Promise<void> {
		if (ev.action.isKey()) {
			await ev.action.setTitle("");
			await ev.action.setImage(keyImage({ label: "Undo layout", icon: "undo" }));
		} else if (ev.action.isDial()) {
			await this.paint(ev.action, "Press to go back");
		}
	}

	override async onKeyDown(ev: KeyDownEvent): Promise<void> {
		await press(ev.action, () => api.undo());
	}

	override async onDialDown(ev: DialDownEvent): Promise<void> {
		await this.undoFromDial(ev.action);
	}

	override async onTouchTap(ev: TouchTapEvent): Promise<void> {
		if (ev.action.isDial()) await this.undoFromDial(ev.action);
	}

	private async undoFromDial(dial: DialAction<JsonObject>): Promise<void> {
		await press(dial, () => api.undo());
		await this.paint(dial, "Done");
		setTimeout(() => void this.paint(dial, "Press to go back"), 1500);
	}

	private async paint(dial: DialAction<JsonObject>, hint: string): Promise<void> {
		await dial.setFeedback(dialFeedback({ title: "Undo", value: "Previous layout", hint, icon: iconImage("undo") }));
	}
}
