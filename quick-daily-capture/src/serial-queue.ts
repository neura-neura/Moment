/** Serializes tasks per key without retaining completed keys. */
export class KeyedSerialQueue {
  private readonly tails = new Map<string, Promise<unknown>>();

  public run<T>(key: string, task: () => Promise<T>): Promise<T> {
    const prior = this.tails.get(key) ?? Promise.resolve();
    const result = prior.then(task);
    const tail = result.then(
      () => undefined,
      () => undefined
    ).finally(() => {
      if (this.tails.get(key) === tail) this.tails.delete(key);
    });
    this.tails.set(key, tail);
    return result;
  }

  public get pendingKeys(): number {
    return this.tails.size;
  }
}
