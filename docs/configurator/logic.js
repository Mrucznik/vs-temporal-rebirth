// Pure config logic: no DOM here, so it can be tested with node.
(function (root) {
  const schema = root.TR_SCHEMA;
  const fields = schema.fields;
  const byKey = Object.fromEntries(fields.map((f) => [f.key, f]));
  /** Sections a preset never touches: lives and administration. */
  const isKept = (f) => Boolean(schema.sections.find((s) => s.id === f.section).keep);
  const presetById = Object.fromEntries(schema.presets.map((p) => [p.id, p]));

  /** Settings the mod accepts only from the file, never from commands: an admin must not lift own limits. */
  const isFileOnly = (f) => Boolean(schema.sections.find((s) => s.id === f.section).fileOnly);

  const defaults = () => Object.fromEntries(fields.map((f) => [f.key, f.default]));

  function applyPreset(id, current) {
    const next = { ...defaults(), ...presetById[id].set };
    if (current) {
      for (const f of fields) if (isKept(f)) next[f.key] = current[f.key];
    }
    return next;
  }

  const presetValue = (id, key) => {
    const set = presetById[id].set;
    return key in set ? set[key] : byKey[key].default;
  };

  function parseDuration(text) {
    const t = String(text).trim().toLowerCase();
    if (t === "forever") return { kind: "forever" };
    let m = /^(\d+(?:\.\d+)?)\s*([hd])$/.exec(t);
    if (m && +m[1] > 0 && +m[1] <= 10000) return { kind: m[2], n: +m[1] };
    m = /^next day (\d{1,2}):(\d{2})$/.exec(t);
    if (m && +m[1] < 24 && +m[2] < 60) return { kind: "next", time: m[1].padStart(2, "0") + ":" + m[2] };
    return null;
  }

  function formatDuration(d) {
    if (d.kind === "forever") return "forever";
    if (d.kind === "next") return "next day " + d.time;
    return d.n + d.kind;
  }

  /** Returns { ok, value } or { ok: false, allowed } with a short description of what is accepted. */
  function validate(key, value) {
    const f = byKey[key];
    switch (f.type) {
      case "bool":
        return typeof value === "boolean" ? { ok: true, value } : { ok: false, allowed: "true, false" };
      case "enum": {
        const hit = f.options.find((o) => o.value.toLowerCase() === String(value).toLowerCase());
        return hit ? { ok: true, value: hit.value } : { ok: false, allowed: f.options.map((o) => o.value).join(", ") };
      }
      case "duration": {
        const d = typeof value === "string" ? parseDuration(value) : null;
        return d ? { ok: true, value: formatDuration(d) } : { ok: false, allowed: '"12h", "7d", "next day 06:00", "forever"' };
      }
      case "text":
        // A mistyped {placeholder} would reach players as is.
        if (f.vars && typeof value === "string") {
          const unknown = [...value.matchAll(/\{([^{}]*)\}/g)].filter((m) => !f.vars.includes(m[1]));
          if (unknown.length) return { ok: false, allowed: f.vars.map((v) => "{" + v + "}").join(", ") || "tekst bez { }" };
        }
        return typeof value === "string" && value.length <= f.maxLength && (!f.pattern || new RegExp(f.pattern).test(value))
          ? { ok: true, value }
          : { ok: false, allowed: (f.single ? "jedno słowo, do " + f.maxLength + " znaków" : "tekst do " + f.maxLength + " znaków") };
      default: {
        const ok =
          typeof value === "number" &&
          Number.isFinite(value) &&
          (f.type !== "int" || Number.isInteger(value)) &&
          value >= f.min &&
          value <= f.max &&
          !(f.not || []).includes(value);
        const except = f.not ? " (" + f.not.map((n) => "≠ " + n).join(", ") + ")" : "";
        return ok ? { ok: true, value } : { ok: false, allowed: f.min + " … " + f.max + except };
      }
    }
  }

  /** A number typed by hand: the special value has its own button, so it is not accepted here. */
  function validateCustom(key, value) {
    const f = byKey[key];
    const r = validate(key, value);
    const s = f.special;
    if (!s) return r;
    return r.ok && value !== s.value && value >= s.min ? r : { ok: false, allowed: s.min + " … " + f.max };
  }

  const isVisible = (f, state) =>
    Object.entries(f.when || {}).every(([key, values]) => values.includes(state[key])) &&
    !Object.entries(f.whenNot || {}).some(([key, values]) => values.includes(state[key]));

  /** Removes // and block comments and trailing commas, leaving strings alone. */
  function stripComments(text) {
    let out = "";
    for (let i = 0; i < text.length; i++) {
      const c = text[i];
      if (c === '"') {
        let j = i + 1;
        while (j < text.length && text[j] !== '"') j += text[j] === "\\" ? 2 : 1;
        out += text.slice(i, j + 1);
        i = j;
      } else if (c === "/" && text[i + 1] === "/") {
        while (i < text.length && text[i] !== "\n") i++;
        out += "\n";
      } else if (c === "/" && text[i + 1] === "*") {
        const end = text.indexOf("*/", i + 2);
        i = end < 0 ? text.length : end + 1;
      } else if (c === "," && /^(\s|\/\/[^\n]*(\n|$)|\/\*[\s\S]*?\*\/)*[}\]]/.test(text.slice(i + 1))) {
        // trailing comma
      } else {
        out += c;
      }
    }
    return out;
  }

  const OLD_KEYS = [
    "LivesPerGear", "ReturnMode", "ReturnAfterHours", "NextDayResetHour", "GhostNoClip", "GhostTetherRadius",
    "RitualRiftRange", "RitualReviveAt", "RitualCooldownHours", "ReturnHealthPercent", "ReturnSatietyPercent",
  ];

  /** Flat config of mod 0.6.x -> dotted keys of the new one. */
  function migrateOld(old) {
    const out = {};
    const copy = (from, to) => { if (from in old) out[to] = old[from]; };
    // 0.6.x took any number, e.g. a negative distance for no limit.
    const copyWithin = (from, to) => {
      copy(from, to);
      if (typeof old[from] === "number") out[to] = Math.min(byKey[to].max, Math.max(byKey[to].min, old[from]));
    };
    copy("LivesPerGear", "Lives.PerGear");
    // A ghost that passes through walls is what the new config calls a spectator.
    if (old.GhostNoClip === true) out.OutOfLives = "Spectator";
    copyWithin("GhostTetherRadius", "Place.Radius");
    // Neither existed in 0.6.x; an update must not change what players see.
    out["Ghost.Visible"] = "Nobody";
    out["Time.ShowTimer"] = false;
    // 0 was the only way to turn rituals off in 0.6.x.
    if (old.RitualRiftRange <= 0) out["Ritual.Where"] = "Disabled";
    else copyWithin("RitualRiftRange", "Ritual.Range");
    copyWithin("RitualCooldownHours", "Ritual.CooldownHours");
    // Old percentages only lowered what the game gives (full health, 50% satiety); new ones set it exactly.
    if (old.ReturnHealthPercent < 100) out["Return.HealthPercent"] = Math.max(1, old.ReturnHealthPercent);
    if (old.ReturnSatietyPercent < 50) out["Return.SatietyPercent"] = Math.max(0, old.ReturnSatietyPercent);
    if ("RitualReviveAt" in old) out["Ritual.ReviveAt"] = old.RitualReviveAt === "DeathSpot" ? "Body" : old.RitualReviveAt;
    const hour = Math.min(23, Math.max(0, Number(old.NextDayResetHour ?? 6)));
    const mode = old.ReturnMode ?? "NextDay";
    out["Time.Duration"] =
      mode === "Disabled" ? "forever"
      : mode === "Hours" ? Math.min(10000, Math.max(0.01, old.ReturnAfterHours ?? 24)) + "h"
      : "next day " + String(hour).padStart(2, "0") + ":00";
    // 0.6.x stored decimals ("24.0"); the new fields are whole numbers.
    for (const key of Object.keys(out)) {
      if (byKey[key].type === "int" && typeof out[key] === "number") out[key] = Math.round(out[key]);
    }
    return out;
  }

  function flatten(obj, prefix = "") {
    const out = {};
    for (const [k, v] of Object.entries(obj)) {
      if (v && typeof v === "object" && !Array.isArray(v)) Object.assign(out, flatten(v, prefix + k + "."));
      else out[prefix + k] = v;
    }
    return out;
  }

  /** Preset with the fewest differences from the given settings. */
  function nearestPreset(state) {
    let best = null;
    for (const p of schema.presets) {
      const n = changedKeys(p.id, state).filter((k) => !isKept(byKey[k])).length;
      if (!best || n < best.n) best = { id: p.id, n };
    }
    return best.id;
  }

  const changedKeys = (presetId, state) =>
    fields.map((f) => f.key).filter((k) => state[k] !== presetValue(presetId, k));

  const toBase64Url = (s) => {
    let bin = "";
    for (const b of new TextEncoder().encode(s)) bin += String.fromCharCode(b);
    return btoa(bin).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  };
  const fromBase64Url = (s) =>
    new TextDecoder("utf-8", { fatal: true }).decode(
      Uint8Array.from(atob(s.replace(/-/g, "+").replace(/_/g, "/")), (c) => c.charCodeAt(0)),
    );

  /** Short code: the preset plus only what differs from it. */
  function encode(presetId, state) {
    const set = Object.fromEntries(changedKeys(presetId, state).map((k) => [k, state[k]]));
    return toBase64Url(JSON.stringify({ v: schema.version, p: presetId, s: set }));
  }

  /**
   * Reads a config file (new, old 0.6.x, with or without comments), an import command, a share link or a bare code.
   * Returns { preset, state, problems, old }, throws { code } when the text cannot be read at all.
   */
  function importText(text) {
    const t = text.trim();
    if (!t) throw { code: "empty" };
    const problems = [];
    let values;
    let base = defaults();
    let preset = null;
    let old = false;

    if (t.startsWith("{")) {
      let obj;
      try {
        obj = JSON.parse(stripComments(t));
      } catch (e) {
        throw { code: "json", detail: e.message };
      }
      old = OLD_KEYS.some((k) => k in obj) && !("OutOfLives" in obj);
      if (old) {
        for (const k of Object.keys(obj)) {
          if (!OLD_KEYS.includes(k)) problems.push({ code: "unknown", key: k });
        }
        values = migrateOld(obj);
      } else {
        values = flatten(obj);
        if (typeof values.ConfigVersion === "number" && values.ConfigVersion > schema.version) problems.push({ code: "newer" });
        delete values.ConfigVersion;
        const wanted = values.ApplyPreset;
        delete values.ApplyPreset;
        if (wanted) {
          const hit = schema.presets.find((p) => p.id.toLowerCase() === String(wanted).toLowerCase());
          if (hit) {
            // Same as the mod on startup: the preset replaces the settings below it.
            preset = hit.id;
            const server = {};
            for (const f of fields) if (isKept(f) && f.key in values) server[f.key] = values[f.key];
            values = server;
            base = applyPreset(preset);
          } else {
            problems.push({ code: "badvalue", key: "ApplyPreset", allowed: schema.presets.map((p) => p.id).join(", ") });
          }
        }
      }
    } else {
      const code = (/#c=([\w-]+)/.exec(t) || /import\s+([\w-]+)/.exec(t) || /^([\w-]+)$/.exec(t) || [])[1];
      let data;
      try {
        data = JSON.parse(fromBase64Url(code));
      } catch {
        throw { code: "code" };
      }
      if (!data || typeof data !== "object" || !presetById[data.p]) throw { code: "code" };
      if (data.v > schema.version) problems.push({ code: "newer" });
      preset = data.p;
      base = applyPreset(preset);
      values = data.s || {};
    }

    const state = { ...base };
    for (const [key, value] of Object.entries(values)) {
      if (!byKey[key]) {
        problems.push({ code: "unknown", key });
        continue;
      }
      const r = validate(key, value);
      if (r.ok) state[key] = r.value;
      else problems.push({ code: "badvalue", key, allowed: r.allowed });
    }
    return { preset: preset || nearestPreset(state), state, problems, old };
  }

  /** The config file as the mod writes it. */
  function toFile(state, withComments) {
    const lines = ["{"];
    const comment = (indent, f) => {
      if (!withComments) return;
      const parts = [f.label + ".", f.special && f.special.value + " = " + f.special.label.toLowerCase() + ".", f.help, f.file];
      if (f.type === "enum") parts.push(f.options.map((o) => o.value + " = " + o.label.toLowerCase()).join(", ") + ".");
      if (f.builtin) parts.push((f.vars.length ? "Można użyć: " + f.vars.map((v) => "{" + v + "}").join(", ") + ". " : "") + "Puste = tekst moda.");
      if (f.privilege) parts.push("Uprawnienie: " + f.privilege + ".");
      lines.push(indent + "// " + parts.filter(Boolean).join(" "));
    };
    lines.push('  "ConfigVersion": ' + schema.version + ",");
    if (withComments) {
      const names = schema.presets.map((p) => p.id).join(", ");
      lines.push("  // Gotowy preset: " + names + ".");
      lines.push("  // Wpisz nazwę i zrestartuj serwer. Mod wypełni resztę i wyczyści to pole.");
    }
    lines.push('  "ApplyPreset": "",');
    const blocks = [...new Set(fields.map((f) => f.key.split(".")[0]))];
    blocks.forEach((name, bi) => {
      const end = bi < blocks.length - 1 ? "," : "";
      if (withComments) lines.push("");
      if (byKey[name]) {
        comment("  ", byKey[name]);
        lines.push("  " + JSON.stringify(name) + ": " + JSON.stringify(state[name]) + end);
        return;
      }
      lines.push("  " + JSON.stringify(name) + ": {");
      const members = fields.filter((f) => f.key.startsWith(name + "."));
      members.forEach((f, i) => {
        comment("    ", f);
        lines.push("    " + JSON.stringify(f.key.slice(name.length + 1)) + ": " + JSON.stringify(state[f.key]) + (i < members.length - 1 ? "," : ""));
      });
      lines.push("  }" + end);
    });
    lines.push("}");
    return lines.join("\n") + "\n";
  }

  /** Points an admin has to mark in game for these settings. */
  function neededPoints(state) {
    const points = [];
    if (state.OutOfLives !== "Ban" && ["Fixed", "Zone"].includes(state["Place.At"])) points.push("start");
    if (state["Return.At"] === "Fixed") points.push("return");
    if (state["Ritual.Where"] === "FixedPlace") points.push("ritual");
    return points;
  }

  /** Fields whose selected option needs another mod. */
  const needsMod = (state, mod) =>
    fields.filter((f) => f.type === "enum" && f.options.find((o) => o.value === state[f.key]).requires === mod);

  /** Whether a ritual can bring the player back at all with these settings. */
  function ritualWorks(state) {
    const where = state["Ritual.Where"];
    if (where === "Disabled") return false;
    // A banned player is not in the world, so nobody can stand next to them.
    return state.OutOfLives !== "Ban" || (where !== "NearPlayer" && state["Ritual.CanLiftBan"]);
  }

  function warnings(state) {
    const out = [];
    const ban = state.OutOfLives === "Ban";
    const form = state.OutOfLives;
    const at = state["Place.At"];
    const free = !ban && at !== "Zone" && state["Place.Radius"] === 0;
    const forever = state["Time.Duration"] === "forever";
    const ritualHelps = ritualWorks(state);
    if (ban && state["Ritual.Where"] === "NearPlayer") out.push({ level: "warn", code: "banNearPlayer" });
    // setlives above 0 brings a player back too, so revive alone being off is not a lock.
    const adminHelps = state["Admin.AllowRevive"] || state["Admin.AllowSetLives"];
    if (!state["Admin.AllowPoints"] && neededPoints(state).length) out.push({ level: "warn", code: "pointsOff" });
    if (!state["Admin.AllowRevive"] && state["Admin.AllowSetLives"]) out.push({ level: "note", code: "setLivesRevives" });
    if (state["Lives.Max"] !== -1 && state["Return.Lives"] > state["Lives.Max"]) out.push({ level: "note", code: "livesOverMax" });
    if (forever) out.push(ritualHelps ? { level: "note", code: adminHelps ? "onlyRitual" : "onlyRitualNoAdmin" } : { level: "bad", code: adminHelps ? "never" : "neverNoAdmin" });
    if (form === "Player" && free && at !== "Fixed") out.push({ level: "warn", code: "noPenalty" });
    if (!ban && at === "Zone") out.push({ level: "note", code: "zoneClaims" });
    if (free && at === "Fixed") out.push({ level: "note", code: "walkAway" });
    const banText = state["Messages.Ban"];
    if (ban && !forever && banText.trim() && !banText.includes("{time}")) out.push({ level: "warn", code: "noTime" });
    return out;
  }

  /** Commands that mark an exile zone out of land claims, in order. */
  const claimCommands = (group) => [
    "/group create " + group,
    "/land claim new",
    "/land claim start",
    "/land claim end",
    "/land claim fullheight",
    "/land claim add",
    // The level given here is for members of the group; players in the zone get Place.Access from the mod.
    "/land claim grantgroup " + group + " traverse",
    "/land claim save " + group,
  ];

  /** Commands giving a role or a single player the game privileges for these actions. */
  const rightsCommands = (who, name, keys) => keys.map((k) => "/" + who + " " + name + " privilege grant " + byKey[k].privilege);

  root.TR = {
    rightsCommands,
    claimCommands,
    schema, fields, byKey, presetById, defaults, applyPreset, presetValue, parseDuration, formatDuration, validate, validateCustom,
    isVisible, isKept, isFileOnly, nearestPreset, changedKeys, encode, importText, toFile, neededPoints, needsMod, ritualWorks, warnings,
  };
})(typeof window !== "undefined" ? window : globalThis);
