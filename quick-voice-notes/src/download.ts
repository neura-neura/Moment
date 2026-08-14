import { createHash } from "node:crypto";
import { createWriteStream } from "node:fs";
import { mkdir, rename, rm } from "node:fs/promises";
import { request as httpRequest } from "node:http";
import type { IncomingMessage } from "node:http";
import { request as httpsRequest } from "node:https";
import { dirname } from "node:path";
import { pipeline } from "node:stream/promises";

export interface DownloadProgress {
  receivedBytes: number;
  totalBytes: number | null;
}

export async function downloadVerifiedFile(
  url: string,
  destination: string,
  expectedSha256: string,
  onProgress?: (progress: DownloadProgress) => void,
  signal?: AbortSignal
): Promise<void> {
  signal?.throwIfAborted();
  await mkdir(dirname(destination), { recursive: true });
  const temporary = `${destination}.download`;
  await rm(temporary, { force: true });
  const response = await requestDownload(url, signal);
  const totalHeader = response.headers["content-length"];
  const parsedTotal = totalHeader === undefined ? Number.NaN : Number.parseInt(totalHeader, 10);
  const totalBytes = Number.isFinite(parsedTotal) ? parsedTotal : null;
  let receivedBytes = 0;
  const hash = createHash("sha256");
  try {
    const chunks = async function* (): AsyncGenerator<Buffer> {
      for await (const chunk of response) {
        signal?.throwIfAborted();
        const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk);
        receivedBytes += buffer.byteLength;
        hash.update(buffer);
        onProgress?.({ receivedBytes, totalBytes });
        yield buffer;
      }
    };
    await pipeline(chunks(), createWriteStream(temporary, { flags: "wx" }));
    const digest = hash.digest("hex");
    if (digest.toLowerCase() !== expectedSha256.toLowerCase()) {
      throw new Error(`Checksum mismatch. Expected ${expectedSha256}, received ${digest}.`);
    }
    await rm(destination, { force: true });
    await rename(temporary, destination);
  } catch (error) {
    response.destroy();
    await rm(temporary, { force: true });
    throw error;
  }
}

const MAX_REDIRECTS = 8;

/**
 * Obsidian desktop runs plugin code in Chromium, where cross-origin fetches can
 * fail before the request reaches the network. Node's HTTPS client is available
 * in desktop-only plugins and gives us a redirect-following binary stream.
 */
async function requestDownload(url: string, signal?: AbortSignal, redirectCount = 0): Promise<IncomingMessage> {
  signal?.throwIfAborted();
  if (redirectCount > MAX_REDIRECTS) throw new Error("Download failed: too many redirects.");

  const target = new URL(url);
  if (target.protocol !== "https:" && target.protocol !== "http:") {
    throw new Error(`Download failed: unsupported URL protocol ${target.protocol}.`);
  }

  const requestFunction = target.protocol === "https:" ? httpsRequest : httpRequest;
  return new Promise<IncomingMessage>((resolve, reject) => {
    let settled = false;
    const request = requestFunction(target, {
      method: "GET",
      headers: {
        Accept: "*/*",
        "User-Agent": "quick-voice-notes/1.0"
      }
    }, (response) => {
      const status = response.statusCode ?? 0;
      const location = response.headers.location;
      const redirectLocation = typeof location === "string" ? location : location?.[0];
      if (status >= 300 && status < 400 && redirectLocation !== undefined) {
        response.resume();
        if (signal !== undefined) signal.removeEventListener("abort", abort);
        settled = true;
        void requestDownload(new URL(redirectLocation, target).toString(), signal, redirectCount + 1)
          .then(resolve, reject);
        return;
      }
      if (status < 200 || status >= 300) {
        response.resume();
        if (signal !== undefined) signal.removeEventListener("abort", abort);
        settled = true;
        reject(new Error(`Download failed with HTTP ${String(status)} ${response.statusMessage ?? "Unknown status"}.`));
        return;
      }
      settled = true;
      if (signal !== undefined) {
        const abort = (): void => { response.destroy(new Error("Download aborted.")); };
        const cleanup = (): void => signal.removeEventListener("abort", abort);
        signal.addEventListener("abort", abort, { once: true });
        response.once("end", cleanup);
        response.once("close", cleanup);
      }
      if (signal !== undefined) {
        const cleanupRequest = (): void => signal.removeEventListener("abort", abort);
        response.once("end", cleanupRequest);
        response.once("close", cleanupRequest);
      }
      resolve(response);
    });

    const abort = (): void => { request.destroy(new Error("Download aborted.")); };
    if (signal !== undefined) signal.addEventListener("abort", abort, { once: true });
    request.setTimeout(30_000, () => { request.destroy(new Error("Download timed out after 30 seconds.")); });
    request.once("error", (error: Error) => {
      if (signal !== undefined) signal.removeEventListener("abort", abort);
      if (!settled) reject(signal?.aborted === true ? new Error("Download aborted.") : error);
    });
    request.end();
  });
}
