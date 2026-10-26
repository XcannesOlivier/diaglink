import { readFileSync, readdirSync } from "node:fs";
import { extname, resolve } from "node:path";
import type { Plugin } from "vite";

const publicDirectory = "pdfjs-wasm";

function contentType(fileName: string) {
  switch (extname(fileName)) {
    case ".wasm":
      return "application/wasm";
    case ".js":
      return "text/javascript; charset=utf-8";
    default:
      return "application/octet-stream";
  }
}

export function pdfjsWasmPlugin(projectDirectory: string): Plugin {
  const sourceDirectory = resolve(projectDirectory, "node_modules/pdfjs-dist/wasm");
  const files = readdirSync(sourceDirectory, { withFileTypes: true })
    .filter(entry => entry.isFile())
    .map(entry => entry.name);
  const knownFiles = new Set(files);
  let base = "/";

  return {
    name: "pdfjs-wasm-assets",
    configResolved(config) {
      base = config.base.endsWith("/") ? config.base : `${config.base}/`;
    },
    configureServer(server) {
      server.middlewares.use((request, response, next) => {
        const pathname = new URL(request.url ?? "/", "http://localhost").pathname;
        const prefixes = new Set([`/${publicDirectory}/`, `${base}${publicDirectory}/`]);
        const prefix = [...prefixes].find(candidate => pathname.startsWith(candidate));
        if (!prefix) {
          next();
          return;
        }

        const fileName = decodeURIComponent(pathname.slice(prefix.length));
        if (!knownFiles.has(fileName)) {
          next();
          return;
        }

        response.statusCode = 200;
        response.setHeader("Content-Type", contentType(fileName));
        response.end(readFileSync(resolve(sourceDirectory, fileName)));
      });
    },
    generateBundle() {
      for (const fileName of files) {
        this.emitFile({
          type: "asset",
          fileName: `${publicDirectory}/${fileName}`,
          source: readFileSync(resolve(sourceDirectory, fileName)),
        });
      }
    },
  };
}
