import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const distDir = path.resolve(__dirname, '../dist');

const manifestPath = fs.existsSync(path.join(distDir, '.vite/manifest.json'))
  ? path.join(distDir, '.vite/manifest.json')
  : path.join(distDir, 'manifest.json');

if (!fs.existsSync(manifestPath)) {
  console.error(`[BUNDLE-GATE ERROR] Manifest file not found at ${manifestPath}. Ensure build.manifest is enabled.`);
  process.exit(1);
}

const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));

// Find entry point
const entryKeys = Object.keys(manifest).filter((key) => manifest[key].isEntry);
if (entryKeys.length === 0) {
  console.error('[BUNDLE-GATE ERROR] No entry point found in manifest.');
  process.exit(1);
}

// Trace all synchronous JS imports from entry points
const syncJsFiles = new Set();
const visited = new Set();

function traceSyncImports(key) {
  if (visited.has(key) || !manifest[key]) return;
  visited.add(key);
  const chunk = manifest[key];
  if (chunk.file && chunk.file.endsWith('.js')) {
    syncJsFiles.add(chunk.file);
  }
  if (Array.isArray(chunk.imports)) {
    for (const impKey of chunk.imports) {
      traceSyncImports(impKey);
    }
  }
}

for (const entryKey of entryKeys) {
  traceSyncImports(entryKey);
}

let totalInitialBytes = 0;
console.log('\n================ BUNDLE SIZE INSPECTION ================');
console.log('--- Initial Synchronous JavaScript Entry Chunks ---');

for (const file of syncJsFiles) {
  const filePath = path.join(distDir, file);
  if (fs.existsSync(filePath)) {
    const stat = fs.statSync(filePath);
    totalInitialBytes += stat.size;
    console.log(`  * ${file}: ${(stat.size / 1024).toFixed(2)} kB`);
  }
}

const totalInitialKb = totalInitialBytes / 1024;
console.log(`--------------------------------------------------------`);
console.log(`Total Initial Entry JS: ${totalInitialKb.toFixed(2)} kB (${totalInitialBytes.toLocaleString()} bytes)`);

console.log('\n--- Dynamic / Async Chunks (Loaded on Demand) ---');
for (const [key, item] of Object.entries(manifest)) {
  if (item.file && item.file.endsWith('.js') && !syncJsFiles.has(item.file)) {
    const filePath = path.join(distDir, item.file);
    if (fs.existsSync(filePath)) {
      const stat = fs.statSync(filePath);
      console.log(`  * [async] ${item.file} (${key}): ${(stat.size / 1024).toFixed(2)} kB`);
    }
  }
}

const MAX_ALLOWED_KB = 500;
console.log('========================================================');

if (totalInitialKb >= MAX_ALLOWED_KB) {
  console.error(`[BUNDLE-GATE FAILED] Initial entry JS (${totalInitialKb.toFixed(2)} kB) exceeds the ${MAX_ALLOWED_KB} kB limit!`);
  process.exit(1);
} else {
  console.log(`[BUNDLE-GATE PASSED] Initial entry JS is within ${MAX_ALLOWED_KB} kB limit! (${totalInitialKb.toFixed(2)} kB < ${MAX_ALLOWED_KB} kB)\n`);
  process.exit(0);
}
