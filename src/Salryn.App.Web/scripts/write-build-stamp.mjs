import { writeFileSync, mkdirSync } from "node:fs";
import { join } from "node:path";

const distDir = join(import.meta.dirname, "..", "dist");
mkdirSync(distDir, { recursive: true });
writeFileSync(join(distDir, "salryn-build-stamp.txt"), `${new Date().toISOString()}\n`, "utf8");