'use strict';

const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

function createNexaPlayServer(options = {}) {
  const dataDir = path.resolve(options.dataDir || process.env.NEXAPLAY_DATA_DIR || path.join(__dirname, 'data'));
  const seedCatalog = path.resolve(options.seedCatalog || path.join(__dirname, '..', 'NexaPlay', 'default-catalog.json'));
  const adminKey = options.adminKey ?? process.env.NEXAPLAY_ADMIN_KEY ?? '';
  const catalogFile = path.join(dataDir, 'catalog.json');
  const ratingsFile = path.join(dataDir, 'ratings.json');
  const reportsFile = path.join(dataDir, 'reports.jsonl');
  fs.mkdirSync(dataDir, { recursive: true });
  if (!fs.existsSync(catalogFile)) {
    if (!fs.existsSync(seedCatalog)) throw new Error(`Seed catalog was not found: ${seedCatalog}`);
    fs.copyFileSync(seedCatalog, catalogFile);
  }
  if (!fs.existsSync(ratingsFile)) writeJsonAtomic(ratingsFile, {});

  return http.createServer(async (request, response) => {
    setHeaders(response);
    if (request.method === 'OPTIONS') return send(response, 204, '');
    try {
      const url = new URL(request.url || '/', 'http://localhost');
      if (request.method === 'GET' && url.pathname === '/health') return sendJson(response, 200, { ok: true, service: 'NexaPlay Community' });
      if (request.method === 'GET' && url.pathname === '/api/catalog') return sendJson(response, 200, readJson(catalogFile, { schemaVersion: 1, games: [] }));

      if (request.method === 'GET' && url.pathname === '/api/admin/reports') {
        if (!adminKey || !constantTimeEqual(getBearerToken(request), adminKey)) return sendJson(response, 401, { error: 'Owner authorization required.' });
        return sendJson(response, 200, { reports: readReports(reportsFile).slice(-500).reverse() });
      }

      const ratingMatch = url.pathname.match(/^\/api\/games\/([^/]+)\/rating$/);
      if (request.method === 'GET' && ratingMatch) {
        const gameId = decodeSegment(ratingMatch[1]);
        const userId = (url.searchParams.get('userId') || '').trim();
        return sendJson(response, 200, getRatingSummary(readJson(ratingsFile, {}), gameId, userId));
      }

      const ratingsMatch = url.pathname.match(/^\/api\/games\/([^/]+)\/ratings$/);
      if (request.method === 'POST' && ratingsMatch) {
        const gameId = decodeSegment(ratingsMatch[1]);
        ensureGameExists(catalogFile, gameId);
        const body = await readBody(request, 16 * 1024);
        const userId = typeof body.userId === 'string' ? body.userId.trim() : '';
        const score = Number(body.score);
        if (userId.length < 12 || userId.length > 200 || !Number.isInteger(score) || score < 1 || score > 5) return sendJson(response, 400, { error: 'A valid userId and a score from 1 to 5 are required.' });
        const ratings = readJson(ratingsFile, {});
        ratings[gameId] ||= {};
        ratings[gameId][hashUser(userId)] = score;
        writeJsonAtomic(ratingsFile, ratings);
        return sendJson(response, 200, getRatingSummary(ratings, gameId, userId));
      }

      const reportMatch = url.pathname.match(/^\/api\/games\/([^/]+)\/reports$/);
      if (request.method === 'POST' && reportMatch) {
        const gameId = decodeSegment(reportMatch[1]);
        const game = ensureGameExists(catalogFile, gameId);
        const body = await readBody(request, 32 * 1024);
        const message = typeof body.message === 'string' ? body.message.trim() : '';
        const playerName = typeof body.playerName === 'string' ? body.playerName.trim().slice(0, 40) : 'Player';
        const userId = typeof body.userId === 'string' ? body.userId.trim() : '';
        if (message.length < 3 || message.length > 4000) return sendJson(response, 400, { error: 'Report message must be between 3 and 4000 characters.' });
        fs.appendFileSync(reportsFile, JSON.stringify({ id: crypto.randomUUID(), gameId, gameTitle: game.title, playerName: playerName || 'Player', playerHash: userId ? hashUser(userId) : '', message, version: String(body.version || ''), createdUtc: new Date().toISOString() }) + '\n', 'utf8');
        return sendJson(response, 201, { accepted: true });
      }

      if (request.method === 'PUT' && url.pathname === '/api/admin/catalog') {
        if (!adminKey || !constantTimeEqual(getBearerToken(request), adminKey)) return sendJson(response, 401, { error: 'Owner authorization required.' });
        const catalog = await readBody(request, 2 * 1024 * 1024);
        validateCatalog(catalog);
        catalog.updatedUtc = new Date().toISOString();
        writeJsonAtomic(catalogFile, catalog);
        return sendJson(response, 200, { saved: true, games: catalog.games.length, updatedUtc: catalog.updatedUtc });
      }

      return sendJson(response, 404, { error: 'Not found.' });
    } catch (error) {
      const status = Number(error.statusCode) || 500;
      return sendJson(response, status, { error: status === 500 ? 'Server error.' : error.message });
    }
  });
}

function setHeaders(response) {
  response.setHeader('Access-Control-Allow-Origin', '*');
  response.setHeader('Access-Control-Allow-Headers', 'Authorization, Content-Type');
  response.setHeader('Access-Control-Allow-Methods', 'GET, POST, PUT, OPTIONS');
  response.setHeader('Cache-Control', 'no-store');
  response.setHeader('X-Content-Type-Options', 'nosniff');
}

function sendJson(response, status, value) { response.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8' }); response.end(JSON.stringify(value)); }
function send(response, status, value) { response.writeHead(status); response.end(value); }
function readJson(file, fallback) { try { return JSON.parse(fs.readFileSync(file, 'utf8')); } catch { return fallback; } }
function readReports(file) {
  if (!fs.existsSync(file)) return [];
  return fs.readFileSync(file, 'utf8').split(/\r?\n/).filter(Boolean).flatMap(line => { try { return [JSON.parse(line)]; } catch { return []; } });
}
function writeJsonAtomic(file, value) { const temp = `${file}.${process.pid}.${crypto.randomBytes(4).toString('hex')}.tmp`; fs.writeFileSync(temp, JSON.stringify(value, null, 2), 'utf8'); fs.renameSync(temp, file); }
function hashUser(userId) { return crypto.createHash('sha256').update(userId, 'utf8').digest('hex'); }
function decodeSegment(value) { const decoded = decodeURIComponent(value); if (!decoded || decoded.length > 200) { const error = new Error('Invalid game ID.'); error.statusCode = 400; throw error; } return decoded; }
function getBearerToken(request) { const value = request.headers.authorization || ''; return value.startsWith('Bearer ') ? value.slice(7) : ''; }
function constantTimeEqual(left, right) { const a = Buffer.from(left); const b = Buffer.from(right); return a.length === b.length && crypto.timingSafeEqual(a, b); }

function getRatingSummary(ratings, gameId, userId) {
  const votes = ratings[gameId] || {};
  const scores = Object.values(votes).filter(value => Number.isInteger(value) && value >= 1 && value <= 5);
  const average = scores.length ? scores.reduce((sum, value) => sum + value, 0) / scores.length : 0;
  const userScore = userId ? votes[hashUser(userId)] ?? null : null;
  return { average: Math.round(average * 100) / 100, count: scores.length, userScore };
}

function ensureGameExists(catalogFile, gameId) {
  const catalog = readJson(catalogFile, { games: [] });
  const game = Array.isArray(catalog.games) ? catalog.games.find(item => item && item.id === gameId) : null;
  if (!game) { const error = new Error('Unknown game.'); error.statusCode = 404; throw error; }
  return game;
}

function validateCatalog(catalog) {
  if (!catalog || catalog.schemaVersion !== 1 || !Array.isArray(catalog.games) || catalog.games.length > 5000) { const error = new Error('Invalid NexaPlay catalog.'); error.statusCode = 400; throw error; }
  const ids = new Set();
  for (const game of catalog.games) {
    if (!game || typeof game.id !== 'string' || !game.id.trim() || typeof game.title !== 'string' || !game.title.trim() || ids.has(game.id.toLowerCase())) { const error = new Error('Every game needs a unique ID and title.'); error.statusCode = 400; throw error; }
    ids.add(game.id.toLowerCase());
  }
}

function readBody(request, limit) {
  return new Promise((resolve, reject) => {
    const chunks = []; let total = 0;
    request.on('data', chunk => { total += chunk.length; if (total > limit) { const error = new Error('Request body is too large.'); error.statusCode = 413; reject(error); request.destroy(); } else chunks.push(chunk); });
    request.on('end', () => { try { resolve(JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}')); } catch { const error = new Error('Invalid JSON body.'); error.statusCode = 400; reject(error); } });
    request.on('error', reject);
  });
}

if (require.main === module) {
  const port = Number(process.env.PORT || 3214);
  createNexaPlayServer().listen(port, '127.0.0.1', () => console.log(`NexaPlay Community listening on http://127.0.0.1:${port}`));
}

module.exports = { createNexaPlayServer };
