// Starts Azurite for local dev, but skips launching a second instance if one
// is already listening (e.g. left running in the background from an earlier
// `npm run dev` session). Exits 0 in that case rather than failing, so
// --kill-others-on-fail in the root "dev" script leaves the functions/swa
// processes alone instead of tearing down the whole session.
const net = require("node:net");
const { spawn } = require("node:child_process");

const HOST = "127.0.0.1";
const PORT = 10002; // Azurite Table service port — same one AuthTokenStoreAzuriteTests probes

function isAzuriteRunning() {
  return new Promise((resolve) => {
    const socket = net.createConnection({ host: HOST, port: PORT });
    const finish = (result) => {
      socket.destroy();
      resolve(result);
    };
    socket.setTimeout(1000);
    socket.once("connect", () => finish(true));
    socket.once("timeout", () => finish(false));
    socket.once("error", () => finish(false));
  });
}

(async () => {
  if (await isAzuriteRunning()) {
    console.log(`[dev:azurite] Azurite already running on ${HOST}:${PORT} — reusing it.`);
    process.exit(0);
  }

  const child = spawn("azurite", ["--silent", "--location", ".azurite", "--debug", ".azurite/debug.log"], {
    stdio: "inherit",
    shell: true,
  });

  child.on("exit", (code) => process.exit(code ?? 0));
})();
