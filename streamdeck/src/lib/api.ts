// Client for the local DisplayDeck server. No Stream Deck imports, so it can be tested with a fake fetch.

export type Status = {
	profiles: string[];
	current: string | null;
	lastApplied: string | null;
	liveAvailable: boolean;
};

export type AudioFormat = { name: string; guid: string };

export type AudioDevice = {
	id: string;
	name: string;
	deviceName: string;
	isDefault: boolean;
	formats: AudioFormat[];
};

export class DisplayDeckError extends Error {
	/** True when the server could not be reached at all (DisplayDeck is not running). */
	readonly offline: boolean;

	constructor(message: string, offline = false) {
		super(message);
		this.name = "DisplayDeckError";
		this.offline = offline;
	}
}

type FetchLike = (input: string, init?: { method?: string; headers?: Record<string, string>; body?: string; signal?: AbortSignal }) => Promise<{
	ok: boolean;
	status: number;
	json(): Promise<unknown>;
}>;

export const DEFAULT_BASE_URL = "http://127.0.0.1:8765";

export class DisplayDeckApi {
	private readonly base: string;
	private readonly fetchImpl: FetchLike;

	constructor(base: string = DEFAULT_BASE_URL, fetchImpl?: FetchLike) {
		this.base = base.replace(/\/+$/, "");
		this.fetchImpl = fetchImpl ?? ((input, init) => fetch(input, init));
	}

	private async request<T>(path: string, method: "GET" | "POST", body?: unknown, timeoutMs = 15000): Promise<T> {
		let response;
		try {
			response = await this.fetchImpl(this.base + path, {
				method,
				headers: body === undefined ? undefined : { "Content-Type": "application/json" },
				body: body === undefined ? undefined : JSON.stringify(body),
				signal: AbortSignal.timeout(timeoutMs),
			});
		} catch {
			throw new DisplayDeckError("DisplayDeck is not running", true);
		}

		let payload: unknown = {};
		try {
			payload = await response.json();
		} catch {
			/* error bodies are optional */
		}
		if (!response.ok) {
			const message = (payload as { error?: string }).error;
			throw new DisplayDeckError(message || `DisplayDeck returned ${response.status}`);
		}
		return payload as T;
	}

	status(): Promise<Status> {
		return this.request<Status>("/api/current", "GET");
	}

	apply(profile: string): Promise<{ ok: boolean; warnings?: string[] }> {
		// Applying changes the display configuration, which can take a while on large setups.
		return this.request("/api/apply/" + encodeURIComponent(profile), "POST", undefined, 60000);
	}

	undo(): Promise<{ ok: boolean }> {
		return this.request("/api/undo-layout", "POST", undefined, 60000);
	}

	async audioDevices(): Promise<AudioDevice[]> {
		const { devices } = await this.request<{ devices: AudioDevice[] }>("/api/audio-devices", "GET");
		return devices;
	}

	applyAudio(device: string, spatial?: string): Promise<{ ok: boolean }> {
		return this.request("/api/audio/apply", "POST", spatial === undefined ? { device } : { device, spatial }, 120000);
	}
}
