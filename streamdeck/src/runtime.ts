import streamDeck from "@elgato/streamdeck";

import { DisplayDeckApi } from "./lib/api";
import { StatusHub } from "./lib/hub";

/**
 * Talks to the DisplayDeck server running on this PC. DISPLAYDECK_URL overrides the address (used by the
 * plugin's own tests, so they never touch a real installation).
 */
export const api = new DisplayDeckApi(process.env.DISPLAYDECK_URL || undefined);

/** One shared poller for every key. */
export const hub = new StatusHub(api);

export const log = streamDeck.logger.createScope("DisplayDeck");

/**
 * Runs a key press: shows the green tick on success, and the yellow alert on failure (with the reason in the log).
 * `onDone` runs afterwards either way, so keys can refresh.
 */
export async function press(action: { showOk(): Promise<void>; showAlert(): Promise<void> }, work: () => Promise<unknown>): Promise<void> {
	try {
		await work();
		await action.showOk();
	} catch (error) {
		log.error(`Key press failed: ${error instanceof Error ? error.message : String(error)}`);
		await action.showAlert();
	} finally {
		await hub.refresh();
	}
}
