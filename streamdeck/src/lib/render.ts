// Draws key faces as SVG data URIs, so each key can show its profile name and whether it is active.

export type KeyFace = {
	label: string;
	caption?: string;
	active?: boolean;
	offline?: boolean;
	/** A short pictogram name; see ICONS. */
	icon?: keyof typeof ICONS;
};

// Pictograms drawn in a 100x100 box, the same family as the plugin and app icons: two offset "screens" for a
// layout (an outlined one behind, a lit one in front), and simple strokes for the rest.
const ICONS = {
	layout:
		'<rect x="3.75" y="9.75" width="54.5" height="31.5" rx="3.75" fill="none" stroke="currentColor" stroke-opacity=".75" stroke-width="7.5"/>' +
		'<rect x="38" y="52" width="62" height="39" rx="7.5" fill="#22c55e"/>',
	next:
		'<path d="M12 50h74M58 22l29 28-29 28" fill="none" stroke="currentColor" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"/>',
	undo:
		'<path d="M32 18 12 38l20 20M12 38h50M62 38a22 22 0 0 1 0 44H36" fill="none" stroke="currentColor" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"/>',
	speaker:
		'<path d="M10 38h18l22-20v64L28 62H10z" fill="currentColor"/>' +
		'<path d="M64 36a20 20 0 0 1 0 28M76 22a38 38 0 0 1 0 56" fill="none" stroke="currentColor" stroke-width="7" stroke-linecap="round"/>',
} as const;

export function escapeXml(text: string): string {
	return text.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&apos;" })[c] as string);
}

/** Splits a label into at most maxLines lines of at most maxChars characters, breaking at spaces where it can. */
export function wrapLabel(text: string, maxChars: number, maxLines: number): string[] {
	const words = text.trim().split(/\s+/).filter(Boolean);
	const lines: string[] = [];
	let line = "";
	for (const word of words) {
		let w = word;
		while (w.length > maxChars) {
			if (line) { lines.push(line); line = ""; }
			lines.push(w.slice(0, maxChars));
			w = w.slice(maxChars);
		}
		if (!w) continue;
		if (line && (line + " " + w).length > maxChars) { lines.push(line); line = w; }
		else line = line ? line + " " + w : w;
	}
	if (line) lines.push(line);
	if (lines.length > maxLines) {
		const kept = lines.slice(0, maxLines);
		const last = kept[maxLines - 1] ?? "";
		kept[maxLines - 1] = (last.length >= maxChars ? last.slice(0, maxChars - 1) : last) + "…";
		return kept;
	}
	return lines;
}

/** A 144x144 key face (Stream Deck scales it to the key). */
export function keyImage(face: KeyFace): string {
	const fg = face.offline ? "#71717a" : "#fafafa";
	const accent = "#22c55e";
	const lines = wrapLabel(face.label, 9, 3);
	// With a pictogram there is less room: use smaller text for two lines so the caption never collides.
	const size = lines.length > 2 || (face.icon && lines.length > 1) ? 17 : 20;
	const top = face.icon ? (lines.length > 1 ? 93 : 100) : 66 - ((lines.length - 1) * (size + 3)) / 2;
	const text = lines
		.map((l, i) => `<text x="72" y="${top + i * (size + 4)}" text-anchor="middle" font-family="Segoe UI, Arial, sans-serif" font-weight="600" font-size="${size}" fill="${fg}">${escapeXml(l)}</text>`)
		.join("");
	const caption = face.caption
		? `<text x="72" y="134" text-anchor="middle" font-family="Segoe UI, Arial, sans-serif" font-size="13" fill="${face.active ? accent : "#a1a1aa"}">${escapeXml(face.caption)}</text>`
		: "";
	// A 100x100 pictogram scaled to about 56px, centred above the label.
	const icon = face.icon ? `<g color="${fg}" transform="translate(44,14) scale(0.56)">${ICONS[face.icon]}</g>` : "";
	const ring = face.active ? `<rect x="5" y="5" width="134" height="134" rx="16" fill="none" stroke="${accent}" stroke-width="6"/>` : "";
	const bang = face.offline ? `<text x="124" y="30" text-anchor="middle" font-family="Segoe UI, Arial, sans-serif" font-weight="700" font-size="24" fill="#f59e0b">!</text>` : "";
	const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="144" height="144" viewBox="0 0 144 144"><rect width="144" height="144" rx="16" fill="#18181b"/>${ring}${icon}${text}${caption}${bang}</svg>`;
	return "data:image/svg+xml;charset=utf8," + encodeURIComponent(svg);
}
