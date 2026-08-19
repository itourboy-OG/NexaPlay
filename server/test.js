'use strict';

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const assert = require('node:assert/strict');
const { createNexaPlayServer } = require('./server');

async function main() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'nexaplay-server-'));
  const seed = path.join(root, 'seed.json');
  fs.writeFileSync(seed, JSON.stringify({ schemaVersion: 1, updatedUtc: new Date().toISOString(), communityApiUrl: '', games: [{ id: 'peak', title: 'PEAK' }] }), 'utf8');
  const server = createNexaPlayServer({ dataDir: path.join(root, 'data'), seedCatalog: seed, adminKey: 'owner-test-key' });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  try {
    assert.equal((await (await fetch(`${base}/health`)).json()).ok, true);
    assert.equal((await (await fetch(`${base}/api/catalog`)).json()).games[0].title, 'PEAK');
    let response = await fetch(`${base}/api/games/peak/ratings`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ userId: 'player-one-12345', score: 5 }) });
    assert.equal(response.status, 200);
    response = await fetch(`${base}/api/games/peak/ratings`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ userId: 'player-two-12345', score: 3 }) });
    assert.deepEqual(await response.json(), { average: 4, count: 2, userScore: 3 });
    response = await fetch(`${base}/api/games/peak/ratings`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ userId: 'player-one-12345', score: 4 }) });
    assert.deepEqual(await response.json(), { average: 3.5, count: 2, userScore: 4 });
    assert.equal((await fetch(`${base}/api/admin/catalog`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ schemaVersion: 1, games: [] }) })).status, 401);
    assert.equal((await fetch(`${base}/api/admin/catalog`, { method: 'PUT', headers: { Authorization: 'Bearer owner-test-key', 'Content-Type': 'application/json' }, body: JSON.stringify({ schemaVersion: 1, games: [{ id: 'peak', title: 'PEAK' }] }) })).status, 200);
    console.log('NexaPlay Community server tests passed: health, catalog read, one-vote-per-player ratings, vote replacement, and owner-only catalog writes.');
  } finally {
    await new Promise(resolve => server.close(resolve));
    fs.rmSync(root, { recursive: true, force: true });
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
