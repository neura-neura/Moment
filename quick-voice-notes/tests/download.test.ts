import { createHash } from "node:crypto";
import { createServer, type Server } from "node:http";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import { downloadVerifiedFile } from "../src/download";

const payload = Buffer.from("desktop-native-download-check");
const checksum = createHash("sha256").update(payload).digest("hex");
const servers: Server[] = [];

afterEach(async () => {
  await Promise.all(servers.splice(0).map(async (server) => new Promise<void>((resolve) => server.close(() => resolve()))));
});

describe("downloadVerifiedFile", () => {
  it("follows redirects and verifies the streamed payload", async () => {
    const server = createServer((request, response) => {
      if (request.url === "/redirect") {
        response.writeHead(302, { Location: "/payload" });
        response.end();
        return;
      }
      response.writeHead(200, { "Content-Length": payload.length });
      response.end(payload);
    });
    servers.push(server);
    await listen(server);
    const directory = await mkdtemp(join(tmpdir(), "quick-voice-download-test-"));
    try {
      const destination = join(directory, "payload.bin");
      await downloadVerifiedFile(`http://127.0.0.1:${String(portOf(server))}/redirect`, destination, checksum);
      await expect(readFile(destination)).resolves.toEqual(payload);
    } finally {
      await rm(directory, { recursive: true, force: true });
    }
  });

  it("removes a partial file when the checksum is wrong", async () => {
    const server = createServer((_request, response) => {
      response.writeHead(200, { "Content-Length": payload.length });
      response.end(payload);
    });
    servers.push(server);
    await listen(server);
    const directory = await mkdtemp(join(tmpdir(), "quick-voice-download-test-"));
    try {
      const destination = join(directory, "payload.bin");
      await expect(downloadVerifiedFile(`http://127.0.0.1:${String(portOf(server))}`, destination, "0".repeat(64))).rejects.toThrow("Checksum mismatch");
      await expect(readFile(destination)).rejects.toThrow();
    } finally {
      await rm(directory, { recursive: true, force: true });
    }
  });
});

function listen(server: Server): Promise<void> {
  return new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", () => resolve());
  });
}

function portOf(server: Server): number {
  const address = server.address();
  if (address === null || typeof address === "string") throw new Error("Test server has no TCP port.");
  return address.port;
}
