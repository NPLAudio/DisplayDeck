// Content for dial touchscreens (Stream Deck +) and the Stream Deck Neo info bar. Pure: no Stream Deck imports.
// The layouts these fill in are layouts/dial.json (200x100) and layouts/infobar.json (232x50).

export type Tone = "normal" | "active" | "muted" | "warn";

const COLORS: Record<Tone, string> = {
	normal: "#fafafa",
	active: "#22c55e",
	muted: "#a1a1aa",
	warn: "#f59e0b",
};

export type DialFace = {
	/** Small caption at the top, e.g. "PROFILE". */
	title: string;
	/** The main text, e.g. the profile name. */
	value: string;
	/** A short hint underneath, e.g. "Press to apply". */
	hint: string;
	tone?: Tone;
	/** An SVG/PNG data URI for the icon, if any. */
	icon?: string;
};

type TextItem = { value: string; color?: string };
type PixmapItem = { value: string };

/** The setFeedback payload for layouts/dial.json. */
export function dialFeedback(face: DialFace): Record<string, TextItem | PixmapItem> {
	const out: Record<string, TextItem | PixmapItem> = {
		title: { value: face.title.toUpperCase(), color: COLORS.muted },
		value: { value: face.value, color: COLORS[face.tone ?? "normal"] },
		hint: { value: face.hint, color: face.tone === "warn" ? COLORS.warn : COLORS.muted },
	};
	if (face.icon) out.icon = { value: face.icon };
	return out;
}

export type InfobarFace = {
	profile: string;
	audio: string;
	active?: boolean;
	offline?: boolean;
	icon?: string;
};

/** The setFeedback payload for layouts/infobar.json. */
export function infobarFeedback(face: InfobarFace): Record<string, TextItem | PixmapItem> {
	const out: Record<string, TextItem | PixmapItem> = {
		profile: { value: face.profile, color: face.offline ? COLORS.warn : face.active ? COLORS.active : COLORS.normal },
		audio: { value: face.audio, color: COLORS.muted },
	};
	if (face.icon) out.icon = { value: face.icon };
	return out;
}

export type SelectorView = { value: string; hint: string; tone: Tone };

/**
 * What a selector dial shows. `noun` is "profile" or "device". With nothing pending, the dial shows the active
 * item; once the user turns it, it shows the pending item and asks for a press.
 */
export function selectorView(input: {
	noun: string;
	labels: Record<string, string>;
	items: string[];
	active: string | null;
	pending: string | null;
	offline: boolean;
	busy?: boolean;
}): SelectorView {
	if (input.offline) return { value: "DisplayDeck offline", hint: "Start DisplayDeck", tone: "warn" };
	if (input.items.length === 0) return { value: `No ${input.noun}s`, hint: input.noun === "profile" ? "Save a layout first" : "Nothing to choose", tone: "muted" };
	const label = (id: string | null) => (id === null ? "" : (input.labels[id] ?? id));
	if (input.busy) return { value: label(input.pending ?? input.active), hint: "Applying...", tone: "normal" };
	if (input.pending !== null && input.pending !== input.active) {
		return { value: label(input.pending), hint: input.noun === "profile" ? "Press to apply" : "Press to make default", tone: "normal" };
	}
	const shown = input.pending ?? input.active;
	if (shown === null) return { value: `Choose a ${input.noun}`, hint: "Turn the dial", tone: "muted" };
	return { value: label(shown), hint: input.pending === null ? "Turn to choose" : "Active", tone: input.active === shown ? "active" : "normal" };
}
