import { describe, expect, it } from "vitest";
import { microphoneErrorMessage } from "../src/errors";

describe("microphone errors", () => {
  it("explains denied and missing microphones", () => {
    expect(microphoneErrorMessage(new DOMException("Denied", "NotAllowedError"))).toContain("denied");
    expect(microphoneErrorMessage(new DOMException("Missing", "NotFoundError"))).toContain("No microphone");
  });
});

