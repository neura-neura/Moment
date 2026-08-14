import { describe, expect, it } from "vitest";
import { encodePcm16Wav, resampleLinear } from "../src/wav";

describe("Whisper WAV conversion", () => {
  it("resamples audio to 16 kHz", () => {
    const source = new Float32Array(48_000).map((_, index) => Math.sin(index / 10));
    const output = resampleLinear(source, 48_000, 16_000);
    expect(output).toHaveLength(16_000);
  });

  it("emits a valid mono PCM16 WAV header", () => {
    const wav = encodePcm16Wav(new Float32Array([0, 1, -1, 0.5]), 16_000);
    const view = new DataView(wav);
    const ascii = (offset: number, length: number): string => String.fromCharCode(
      ...Array.from({ length }, (_, index) => view.getUint8(offset + index))
    );
    expect(ascii(0, 4)).toBe("RIFF");
    expect(ascii(8, 4)).toBe("WAVE");
    expect(view.getUint16(20, true)).toBe(1);
    expect(view.getUint16(22, true)).toBe(1);
    expect(view.getUint32(24, true)).toBe(16_000);
    expect(view.getUint16(34, true)).toBe(16);
    expect(wav.byteLength).toBe(52);
  });
});

