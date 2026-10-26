// A dial's "turned but not yet applied" choice. It expires after a while, so a dial left alone goes back to
// showing the real state instead of a choice nobody confirmed.

export class PendingStore {
	private readonly values = new Map<string, string>();
	private readonly timers = new Map<string, ReturnType<typeof setTimeout>>();
	private readonly ttlMs: number;
	private readonly onExpire: (id: string) => void;

	constructor(ttlMs: number, onExpire: (id: string) => void) {
		this.ttlMs = ttlMs;
		this.onExpire = onExpire;
	}

	get(id: string): string | null {
		return this.values.get(id) ?? null;
	}

	set(id: string, value: string): void {
		this.values.set(id, value);
		this.restart(id);
	}

	clear(id: string): void {
		this.values.delete(id);
		const timer = this.timers.get(id);
		if (timer) clearTimeout(timer);
		this.timers.delete(id);
	}

	private restart(id: string): void {
		const old = this.timers.get(id);
		if (old) clearTimeout(old);
		this.timers.set(id, setTimeout(() => {
			this.values.delete(id);
			this.timers.delete(id);
			this.onExpire(id);
		}, this.ttlMs));
	}
}
