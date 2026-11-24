import fs from "fs";
import vm from "vm";
import assert from "assert";
const D = process.argv[2] ?? new URL("../../docs/configurator", import.meta.url).pathname;
const w = {};
const ctx = vm.createContext({ window: w, TextEncoder, TextDecoder, btoa, atob, Uint8Array, console });
for (const f of ["schema.js", "logic.js"]) vm.runInContext(fs.readFileSync(D + "/" + f, "utf8"), ctx);
const T = w.TR,
  eq = (a, b) => assert.deepEqual(JSON.parse(JSON.stringify(a)), JSON.parse(JSON.stringify(b)));
const err = (x) => {
  try {
    T.importText(x);
  } catch (e) {
    return e.code;
  }
};
for (const f of T.fields) assert(T.validate(f.key, f.default).ok, f.key);
for (const p of T.schema.presets) {
  for (const [k, v] of Object.entries(p.set)) assert(T.validate(k, v).ok, k);
  const s = T.applyPreset(p.id);
  s["Messages.Ban"] = 'Żółw "x" // nie komentarz, {time}';
  s["Lives.PerGear"] = 3;
  for (const c of [true, false]) {
    const r = T.importText(T.toFile(s, c));
    eq(r.state, s);
    eq(r.problems, []);
    assert.equal(r.preset, p.id, "nearest " + p.id + " got " + r.preset);
    if (!c) JSON.parse(T.toFile(s, c));
  }
  const code = T.encode(p.id, s);
  for (const t of [code, "/rebirth import " + code, "file:///x/index.html#c=" + code]) {
    const r = T.importText(t);
    eq(r.state, s);
    assert.equal(r.preset, p.id);
  }
  console.log(
    p.id,
    "import cmd length",
    ("/rebirth import " + T.encode(p.id, T.applyPreset(p.id))).length,
    T.warnings(T.applyPreset(p.id))
      .map((x) => x.code)
      .join(),
    T.neededPoints(T.applyPreset(p.id)).join(),
  );
}
const full = Object.fromEntries(T.fields.map((f) => [f.key, f.default]));
// real 0.6.x default file
const old = `{"LivesPerGear":1,"ReturnMode":"NextDay","ReturnAfterHours":24.0,"NextDayResetHour":6,"GhostNoClip":false,"GhostTetherRadius":24.0,"RitualRiftRange":8.0,"RitualReviveAt":"Caster","RitualCooldownHours":0.0,"ReturnHealthPercent":100.0,"ReturnSatietyPercent":100.0}`;
let r = T.importText(old);
eq(r.state, {
  ...full,
  "Ghost.Visible": "Nobody",
  "Time.ShowTimer": false,
  "Ritual.Where": "Rift",
  "Ritual.ReviveAt": "Caster",
});
assert.equal(r.preset, "Rift");
assert(r.old);
eq(r.problems, []);
r = T.importText('{"ReturnMode":"Hours","ReturnAfterHours":1.5,"RitualReviveAt":"DeathSpot","GhostTetherRadius":0}');
assert.equal(r.state["Time.Duration"], "1.5h");
assert.equal(r.state["Ritual.ReviveAt"], "Body");
assert.equal(r.state["Place.Radius"], 0);
for (const [o, k, v] of [
  ['"RitualRiftRange":0', "Ritual.Where", "Disabled"],
  ['"RitualRiftRange":0.4', "Ritual.Range", 1],
  ['"RitualRiftRange":1000.0', "Ritual.Range", 256],
  ['"GhostTetherRadius":-1', "Place.Radius", 0],
  ['"ReturnMode":"Hours","ReturnAfterHours":0', "Time.Duration", "0.01h"],
  ['"LivesPerGear":3', "Lives.PerGear", 3],
]) {
  const x = T.importText("{" + o + "}");
  assert(x.old, o);
  assert.equal(x.state[k], v, o);
  eq(x.problems, []);
}
r = T.importText('{"ReturnMode":"Disabled"}');
assert.equal(r.state["Time.Duration"], "forever");
r = T.importText('{"ApplyPreset":"hardcore","Lives":{"PerGear":5},"OutOfLives":"Ghost"}');
assert.equal(r.state.OutOfLives, "Ban");
assert.equal(r.state["Lives.PerGear"], 5);
r = T.importText('{"OutOfLives":"Wampir","Foo":1,"Return":{"Lives":0},"Time":{"Duration":"2h30m"},}');
console.log(JSON.stringify(r.problems));
assert.equal(r.problems.length, 4);
{
  const x = T.applyPreset("Exile");
  x["Place.At"] = "Zone";
  x["Place.Group"] = "wygnańcy";
  const r2 = T.importText(T.toFile(x, true));
  eq(r2.state, x);
  assert.equal(r2.preset, "Exile");
  eq(T.importText(T.encode("Exile", x)).state, x);
  for (const b of ["", "a b", "x".repeat(41)]) assert(!T.validate("Place.Group", b).ok);
  assert(!T.validate("Place.Radius", -1).ok);
  console.log(
    T.warnings(x)
      .map((w) => w.code)
      .join(),
    T.encode("Exile", x).length,
  );
}
r = T.importText('{"GhostNoClip":true,"GhostTetherRadius":10}');
assert.equal(r.state.OutOfLives, "Spectator");
assert.equal(r.state["Place.Radius"], 10);
let n = 0;
for (const form of ["Ghost", "Spectator", "Zombie", "Player", "Ban"])
  for (const at of ["Body", "Spawn", "Fixed", "Zone"])
    for (const dur of ["12h", "7d", "next day 06:00", "forever"])
      for (const w of ["Disabled", "Rift", "NearPlayer", "FixedPlace", "Anywhere"]) {
        const x = { ...T.defaults(), OutOfLives: form, "Place.At": at, "Time.Duration": dur, "Ritual.Where": w };
        eq(T.importText(T.toFile(x, true)).state, x);
        const pr = T.nearestPreset(x);
        eq(T.importText(T.encode(pr, x)).state, x);
        T.warnings(x);
        n++;
      }
console.log("mixes", n);
{
  const W = (o) => T.warnings({ ...T.defaults(), ...o }).map((w) => w.code);
  const off = { "Admin.AllowRevive": false };
  assert(W({ "Time.Duration": "forever", "Ritual.Where": "Disabled" }).includes("never"));
  assert(
    W({ "Time.Duration": "forever", "Ritual.Where": "Disabled", ...off, "Admin.AllowSetLives": false }).includes(
      "neverNoAdmin",
    ),
  );
  assert(W({ "Time.Duration": "forever", ...off, "Admin.AllowSetLives": false }).includes("onlyRitualNoAdmin"));
  assert(W(off).includes("setLivesRevives"));
  assert(W({ "Place.At": "Fixed", "Admin.AllowPoints": false }).includes("pointsOff"));
  assert(!W({ "Admin.AllowPoints": false }).includes("pointsOff"));
  assert(W({ "Lives.Max": 2, "Return.Lives": 3 }).includes("livesOverMax"));
  assert(!W({ "Lives.Max": 2, "Return.Lives": -1 }).includes("livesOverMax"));
  assert(!T.validate("Lives.Max", 0).ok);
  const k = T.applyPreset("Hardcore", { ...T.defaults(), ...off, "Lives.Max": 5 });
  assert(k["Lives.Max"] === 5 && k["Admin.AllowRevive"] === false);
  console.log("admin ok");
}
{
  const f = T.byKey["Place.Access"];
  for (const [o, at, v] of [
    ["Player", "Zone", true],
    ["Zombie", "Zone", true],
    ["Ghost", "Zone", false],
    ["Player", "Fixed", false],
    ["Ban", "Zone", false],
  ])
    assert.equal(T.isVisible(f, { ...T.defaults(), OutOfLives: o, "Place.At": at }), v);
  for (const a of ["Traverse", "Use", "All"]) {
    const x = { ...T.defaults(), OutOfLives: "Player", "Place.At": "Zone", "Place.Access": a };
    eq(T.importText(T.toFile(x, true)).state, x);
  }
  assert(T.claimCommands("g").includes("/land claim grantgroup g traverse"));
  console.log("access ok");
}
{
  eq(T.rightsCommands("role", "sumod", ["Admin.AllowRevive", "Admin.AllowPoints"]), [
    "/role sumod privilege grant temporalrebirth.revive",
    "/role sumod privilege grant temporalrebirth.points",
  ]);
  assert(!T.byKey["Admin.AllowKill"] && !T.byKey["Ritual.CanFreeKilled"]);
  assert.equal(T.fields.filter((f) => f.section === "admin").length, 4);
  const o = T.importText('{"OutOfLives":"Ghost","Admin":{"AllowKill":true}}');
  assert.equal(o.problems[0].code, "unknown");
  console.log("rights ok");
}
{
  assert(!T.byKey["Lives.IfNotSaved"]);
  assert.equal(T.needsMod(T.defaults(), "downed").length, 0);
  for (const v of ["Downs", "DownsFinalDeath"]) {
    const x = { ...T.defaults(), "Lives.Count": v };
    eq(T.importText(T.toFile(x, true)).state, x);
    eq(T.importText(T.encode("Ghost", x)).state, x);
    assert.equal(T.needsMod(x, "downed")[0].key, "Lives.Count");
    assert.equal(T.applyPreset("Hardcore", x)["Lives.Count"], v);
  }
  console.log("downed ok");
}
{
  const f = T.byKey["Time.ShowTimer"];
  assert(
    !T.byKey["Time.ReminderHours"] && f.default === true && !T.isVisible(f, { ...T.defaults(), OutOfLives: "Ban" }),
  );
  assert.equal(
    T.importText('{"OutOfLives":"Ghost","Time":{"ReminderHours":1,"ShowTimer":false}}').problems[0].key,
    "Time.ReminderHours",
  );
  console.log("timer ok");
}
{
  const N = { ...T.defaults(), "Ritual.Where": "NearPlayer" };
  const W = (x) => T.warnings(x).map((w) => w.code);
  assert(
    T.ritualWorks(N) &&
      !T.ritualWorks({ ...N, OutOfLives: "Ban" }) &&
      T.ritualWorks({ ...T.defaults(), "Ritual.Where": "Rift", OutOfLives: "Ban" }) &&
      !T.ritualWorks({ ...T.defaults(), "Ritual.Where": "Rift", OutOfLives: "Ban", "Ritual.CanLiftBan": false }),
  );
  assert(W({ ...N, OutOfLives: "Ban" }).includes("banNearPlayer") && !W(N).includes("banNearPlayer"));
  assert(W({ ...N, OutOfLives: "Ban", "Time.Duration": "forever" }).includes("never"));
  for (const k of ["Ritual.Range", "Ritual.ReviveAt", "Ritual.CooldownHours"]) assert(T.isVisible(T.byKey[k], N));
  assert(!T.isVisible(T.byKey["Ritual.CanLiftBan"], { ...N, OutOfLives: "Ban" }));
  console.log("near ok");
}
{
  const u = T.applyPreset("Undead");
  assert.deepEqual(
    [u.OutOfLives, u["Ritual.Where"], u["Ritual.ReviveAt"], u["Return.At"]],
    ["Zombie", "NearPlayer", "InPlace", "Spawn"],
  );
  assert(T.ritualWorks(u));
  console.log("undead ok");
}
{
  for (const f of T.fields.filter((f) => f.special)) {
    const sp = f.special;
    assert(T.validate(f.key, sp.value).ok, f.key);
    assert(!T.validateCustom(f.key, sp.value).ok, f.key);
    assert(T.validateCustom(f.key, sp.start).ok, f.key);
    assert(T.validateCustom(f.key, sp.min).ok, f.key);
    assert(!T.validateCustom(f.key, f.max + 1).ok, f.key);
  }
  assert(
    T.validateCustom("Return.SatietyPercent", 100).ok &&
      T.validateCustom("Return.SatietyPercent", 0).ok &&
      !T.validateCustom("Return.HealthPercent", 0).ok &&
      T.validateCustom("Lives.PerGear", 0).ok,
  );
  const F = { ...T.defaults(), "Time.Duration": "forever", OutOfLives: "Zombie" };
  assert(
    !T.isVisible(T.byKey["Time.ShowTimer"], F) &&
      !T.isVisible(T.byKey["Return.DieAtEnd"], F) &&
      T.isVisible(T.byKey["Return.DieAtEnd"], { ...F, "Time.Duration": "7d" }),
  );
  assert(T.neededPoints({ ...F, "Return.At": "Fixed" }).includes("return"));
  const m = (h, s) => {
    const x = T.importText(
      JSON.stringify({ ReturnMode: "NextDay", ReturnHealthPercent: h, ReturnSatietyPercent: s }),
    ).state;
    return [x["Return.HealthPercent"], x["Return.SatietyPercent"]].join();
  };
  assert.deepEqual([m(100, 100), m(40, 70), m(40, 30), m(0, 50)], ["-1,-1", "40,-1", "40,30", "1,-1"]);
  console.log("special ok");
}
{
  const f = T.byKey["Ghost.Visible"];
  assert(f.default === "WispAndEcho");
  for (const o of ["Spectator", "Zombie", "Player", "Ban"]) assert(!T.isVisible(f, { ...T.defaults(), OutOfLives: o }));
  for (const v of ["Nobody", "Wisp", "WispAndEcho"]) {
    const x = { ...T.defaults(), "Ghost.Visible": v };
    eq(T.importText(T.toFile(x, true)).state, x);
    eq(T.importText(T.encode("Ghost", x)).state, x);
  }
  console.log("visible ok");
}
{
  const ms = T.fields.filter((f) => f.section === "messages");
  assert.equal(ms.length, 21);
  for (const f of ms) {
    assert(T.validate(f.key, f.builtin).ok, f.key);
    assert(T.validate(f.key, "").ok);
    assert(!T.validate(f.key, "{zle}").ok);
    assert(T.isKept(f));
  }
  assert(!T.byKey["Ban.Message"]);
  const x = { ...T.defaults(), OutOfLives: "Ban", "Messages.Ban": "Przerwa" };
  assert(T.warnings(x).some((w) => w.code === "noTime"));
  assert(!T.warnings({ ...x, "Messages.Ban": "" }).some((w) => w.code === "noTime"));
  eq(T.importText(T.toFile(x, true)).state, x);
  eq(T.importText(T.encode("Timeout", x)).state, x);
  assert.equal(T.applyPreset("Hardcore", x)["Messages.Ban"], "Przerwa");
  console.log("messages ok");
}
assert.equal(err(""), "empty");
assert.equal(err('{"a":'), "json");
assert.equal(err("zzz!"), "code");
assert.equal(err("abcd"), "code");
for (const [d, ok] of [
  ["0h", 0],
  ["12h", 1],
  ["next day 24:00", 0],
  ["next day 6:00", 1],
  ["forever", 1],
  ["-1d", 0],
])
  assert.equal(!!T.parseDuration(d), !!ok, d);
console.log("LOGIC OK");
