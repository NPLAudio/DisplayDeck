// One shared poller for every key. It only polls while at least one key that needs the data is visible,
// and it only tells listeners about changes, so keys are not repainted needlessly.

export type StatusLike = { profiles: string[]; current: string | null; lastApplied: string | null; liveAvailable?: boolean };
export type DeviceLike = { id: string; name: string; deviceName: string; isDefault: boolean; formats: Array<{ name: string; guid: string }> };

export type HubApi = {
	status(): Promise<StatusLike>;
	audioDevices(): Promise<DeviceLike[]>;
};

export type HubState = {
	status: StatusLike | null;
	devices: DeviceLike[] | null;
	/** True when the last poll could not reach DisplayDeck. */
	offline: boolean;
};

export type Kind = "status" | "audio";

export class StatusHub {
	private readonly api: HubApi;
	private readonly intervalMs: number;
	private readonly refs: Record<Kind, number> = { status: 0, audio: 0 };
	private readonly listeners = new Set<(state: HubState) => void>();
	private timer: ReturnType<typeof setTimeout> | undefined;
	private running = false;
	private lastJson = "";

	state: HubState = { status: null, devices: null, offline: false };

	constructor(api: HubApi, intervalMs = 4000) {
		this.api = api;
		this.intervalMs = intervalMs;
	}

	/** Start polling for the given kind of data; call the returned function when the key goes away. */
	retain(kind: Kind): () => void {
		this.refs[kind]++;
		if (this.refs.status + this.refs.audio === 1) void this.loop();
		else void this.refresh();
		let released = false;
		return () => {
			if (released) return;
			released = true;
			this.refs[kind]--;
			if (this.refs.status + this.refs.audio === 0 && this.timer) { clearTimeout(this.timer); this.timer = undefined; }
		};
	}

	onChange(listener: (state: HubState) => void): () => void {
		this.listeners.add(listener);
		return () => this.listeners.delete(listener);
	}

	get active(): boolean {
		return this.refs.status + this.refs.audio > 0;
	}

	/** Poll right now (used after an action changes something). */
	async refresh(): Promise<void> {
		if (this.running) return;
		this.running = true;
		try {
			let offline = false;
			let status = this.state.status;
			let devices = this.state.devices;
			if (this.refs.status > 0 || status === null) {
				try { status = await this.api.status(); } catch { offline = true; }
			}
			if (this.refs.audio > 0) {
				try { devices = await this.api.audioDevices(); } catch { offline = true; }
			}
			this.state = { status, devices, offline };
			const json = JSON.stringify(this.state);
			if (json !== this.lastJson) {
				this.lastJson = json;
				for (const l of [...this.listeners]) l(this.state);
			}
		} finally {
			this.running = false;
		}
	}

	private async loop(): Promise<void> {
		await this.refresh();
		if (this.active) this.timer = setTimeout(() => void this.loop(), this.intervalMs);
	}
}
