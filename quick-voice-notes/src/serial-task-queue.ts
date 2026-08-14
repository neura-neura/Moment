/** Keeps resource-heavy jobs in order and continues after individual failures. */
export class SerialTaskQueue {
  private tail: Promise<void> = Promise.resolve();

  public enqueue<T>(task: () => Promise<T>): Promise<T> {
    const result = this.tail.then(task);
    this.tail = result.then(
      () => undefined,
      () => undefined
    );
    return result;
  }
}

