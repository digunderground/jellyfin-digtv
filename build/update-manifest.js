#!/usr/bin/env node
/*
 * Updates manifest.json with a new version entry for the Jellyfin plugin repository.
 * Plain Node (no dependencies). Run from the repo root.
 *
 * Usage:
 *   node build/update-manifest.js <version> <sourceUrl> <zipPath> [changelog] [targetAbi]
 *
 * - version    e.g. 1.0.0.0  (tag without leading "v")
 * - sourceUrl  public download URL of the release zip
 * - zipPath    local path to the zip (used to compute the MD5 checksum)
 * - changelog  optional release notes
 * - targetAbi  optional; defaults to meta.json's targetAbi
 *
 * Plugin identity (guid/name/description/overview/owner/category) is read from meta.json.
 */
const fs = require('fs');
const crypto = require('crypto');

const [, , version, sourceUrl, zipPath, changelog, targetAbiArg] = process.argv;

if (!version || !sourceUrl || !zipPath) {
    console.error('Usage: node build/update-manifest.js <version> <sourceUrl> <zipPath> [changelog] [targetAbi]');
    process.exit(1);
}

const meta = JSON.parse(fs.readFileSync('meta.json', 'utf8'));
const targetAbi = targetAbiArg || meta.targetAbi;

const checksum = crypto.createHash('md5').update(fs.readFileSync(zipPath)).digest('hex');
const timestamp = new Date().toISOString().replace(/\.\d{3}Z$/, 'Z');

const manifestPath = 'manifest.json';
let manifest = [];
try {
    manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
    if (!Array.isArray(manifest)) { manifest = []; }
} catch {
    manifest = [];
}

let plugin = manifest.find((p) => (p.guid || '').toLowerCase() === meta.guid.toLowerCase());
if (!plugin) {
    plugin = {
        category: meta.category || 'General',
        guid: meta.guid,
        name: meta.name,
        description: meta.description || meta.overview || meta.name,
        overview: meta.overview || meta.description || meta.name,
        owner: meta.owner || '',
        imageUrl: meta.imageUrl || undefined,
        versions: []
    };
    manifest.push(plugin);
}

// Replace any existing entry for this version, then put the new one first.
plugin.versions = (plugin.versions || []).filter((v) => v.version !== version);
plugin.versions.unshift({
    version,
    changelog: changelog || ('Release ' + version),
    targetAbi,
    sourceUrl,
    checksum,
    timestamp
});

fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 4) + '\n');
console.log('manifest.json updated: version=' + version + ' md5=' + checksum + ' abi=' + targetAbi);
