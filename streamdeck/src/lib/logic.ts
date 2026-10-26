// Small pure helpers shared by the actions.

export type StatusLike = { profiles: string[]; current: string | null; lastApplied: string | null };

/** The profile after the active one, wrapping around. Starts from the first profile when none is active. */
export function nextProfile(status: StatusLike): string | null {
	const { profiles } = status;
	if (profiles.length === 0) return null;
	const base = status.current ?? status.lastApplied;
	const index = base === null ? -1 : profiles.indexOf(base);
	return profiles[(index + 1) % profiles.length] ?? null;
}

/**
 * Whether a profile should be shown as the active one: it matches the live layout, or, when the live
 * layout matches nothing, it is the last one applied.
 */
export function isActiveProfile(status: StatusLike, name: string | undefined): boolean {
	if (!name) return false;
	if (status.current !== null) return status.current === name;
	return status.lastApplied === name;
}

/** The setting values the Audio action stores. "keep" leaves the spatial format as it is. */
export function spatialArgument(setting: string | undefined): string | undefined {
	if (setting === undefined || setting === "" || setting === "keep") return undefined;
	return setting;
}
