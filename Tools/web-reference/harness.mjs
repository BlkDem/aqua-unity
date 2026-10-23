// Runs the web version's world + lifecycle code in Node to produce reference data for the C# port.
const store = new Map();
globalThis.localStorage = { getItem: (k) => store.get(k) ?? null, setItem: (k, v) => store.set(k, String(v)), removeItem: (k) => store.delete(k) };
globalThis.location = { search: '' };
const SRC = '/home/maksim/projects/aqua/src';
const { World, DAY } = await import(`${SRC}/life/world.js`);
const L = await import(`${SRC}/life/lifecycle.js`);
const { mulberry32, hashStr } = await import(`${SRC}/fx/shaderPatch.js`);
const fs = await import('fs');
const out = process.argv[2];

// 1. RNG reference values
const rng = {};
for (const s of ['', 'abc', 'k3x9neon-0', 'живой']) rng[s] = hashStr(s);
const R = mulberry32(hashStr('k3x9neon-0'));
const seq = [R(), R(), R(), R(), R()];
fs.writeFileSync(`${out}/rng.json`, JSON.stringify({ hash: rng, seq }, null, 1));

// 2. a save after 30 days of life (catch-up), for format compatibility
store.clear();
let w = new World();
L.catchUp(w, w.now() + 30 * DAY);
w.save();
fs.writeFileSync(`${out}/web-save.json`, store.get('living-aquarium.v2'));

// 3. statistics of 60 days offline over many runs
const N = 60, acc = { fish: [], births: [], kids: [], pairs: [], farewell: [] };
for (let i = 0; i < N; i++) {
  store.clear();
  const w = new World();
  const a = L.catchUp(w, w.now() + 60 * DAY);
  acc.fish.push(w.fish.length);
  acc.births.push(a.births.length);
  acc.kids.push(a.births.reduce((n, e) => n + e.kids.length, 0));
  acc.pairs.push(a.pairs.length);
  acc.farewell.push(w.state.history.filter((h) => h.kind === 'farewell').length);
}
const stat = (a) => { const m = a.reduce((x, y) => x + y, 0) / a.length; const sd = Math.sqrt(a.reduce((x, y) => x + (y - m) ** 2, 0) / a.length); return { mean: +m.toFixed(2), sd: +sd.toFixed(2), min: Math.min(...a), max: Math.max(...a) }; };
const stats = Object.fromEntries(Object.entries(acc).map(([k, v]) => [k, stat(v)]));
fs.writeFileSync(`${out}/stats-60d.json`, JSON.stringify({ runs: N, ...stats }, null, 1));
console.log(JSON.stringify(stats));
