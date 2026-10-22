import { it, expect, vi } from "vitest";
import { scheduleMaintenance } from "../../src/maintenance.js";
it("does not overlap cleanup and waits for its active pass before shutdown", async () => {
  vi.useFakeTimers();
  let release!: () => void,
    calls = 0;
  const pass = new Promise<void>((resolve) => {
    release = resolve;
  });
  const stop = scheduleMaintenance(
    async () => {
      calls++;
      await pass;
    },
    () => undefined
  );
  try {
    await vi.advanceTimersByTimeAsync(6 * 3600000);
    expect(calls).toBe(1);
    await vi.advanceTimersByTimeAsync(6 * 3600000);
    expect(calls).toBe(1);
    let stopped = false;
    const closing = stop().then(() => {
      stopped = true;
    });
    await Promise.resolve();
    expect(stopped).toBe(false);
    release();
    await closing;
    await vi.advanceTimersByTimeAsync(6 * 3600000);
    expect(calls).toBe(1);
  } finally {
    release();
    await stop();
    vi.useRealTimers();
  }
});
