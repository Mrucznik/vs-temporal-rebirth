// Every language must have every text, and the same placeholders as the Polish original.
import assert from "assert";
import fs from "fs";
import vm from "vm";

const D = new URL("../../docs/configurator/", import.meta.url).pathname;
const w = {};
const ctx = vm.createContext({ window: w, TextEncoder, TextDecoder, btoa, atob, Uint8Array });
for (const f of ["schema.js", "logic.js", "i18n/pl.js", "i18n/en.js", "i18n/de.js", "i18n/ru.js", "i18n.js"]) vm.runInContext(fs.readFileSync(D + f, "utf8"), ctx);
const { TR, TR_I18N, TR_SCHEMA } = w;
const shape = (o) => typeof o === "function" ? "function" : Array.isArray(o) ? o.map(shape) : o && typeof o === "object" ? Object.fromEntries(Object.keys(o).sort().map((k) => [k, shape(o[k])])) : typeof o;
const json = (o) => JSON.parse(JSON.stringify(o));
const polish = json(TR_SCHEMA);
const polishFile = TR.toFile(TR.defaults(), true);
const placeholders = (t) => [...(t || "").matchAll(/\{\w+\}/g)].map((m) => m[0]).sort().join();

for (const lang of ["en", "de", "ru"]) {
  const l = TR_I18N[lang];
  for (const part of ["ui", "file", "story"]) assert.deepEqual(json(shape(l[part])), json(shape(TR_I18N.pl[part])), lang + " " + part);
  assert.deepEqual(Object.keys(l.schema.fields).sort(), polish.fields.map((f) => f.key).sort(), lang + " fields");
  assert.deepEqual(Object.keys(l.schema.presets).sort(), polish.presets.map((p) => p.id).sort(), lang + " presets");
  for (const f of polish.fields) {
    const t = l.schema.fields[f.key], at = lang + " " + f.key;
    for (const k of ["label", "help", "file", "builtin"]) assert.equal(!!t[k], !!f[k], at + " " + k);
    assert.equal(placeholders(t.builtin), placeholders(f.builtin), at + " placeholders");
    assert.deepEqual(Object.keys(t.options || {}), (f.options || []).map((o) => o.value), at + " options");
    for (const o of f.options || []) assert.equal(!!t.options[o.value][1], !!o.help, at + "." + o.value + " help");
    assert.equal(!!t.special, !!f.special, at + " special");
    if (f.special) assert.equal(!!t.special[2], !!f.special.help, at + " special help");
    if (f.group) assert(l.schema.groups[f.group], at + " group");
  }
  TR.localize(lang);
  const text = JSON.stringify(TR_SCHEMA) + TR.toFile(TR.defaults(), true);
  assert(!/[ąćęłńśżź]/i.test(text), lang + ": Polish left: " + (text.match(/.{30}[ąćęłńśżź].{20}/i) || [""])[0]);
  for (const preset of TR_SCHEMA.presets) {
    const s = TR.applyPreset(preset.id);
    assert.deepEqual(json(TR.importText(TR.toFile(s, true)).state), json(s), lang + " file of " + preset.id);
  }
}
TR.localize("pl");
assert.deepEqual(json(TR_SCHEMA), polish, "back to Polish exactly");
assert.equal(TR.toFile(TR.defaults(), true), polishFile, "the Polish file is what the mod writes");

const lang = (code) => JSON.parse(fs.readFileSync(new URL(`../../assets/temporalrebirth/lang/${code}.json`, import.meta.url)));
for (const code of ["en", "de", "ru"]) {
  const texts = lang(code);
  for (const f of polish.fields.filter((x) => x.builtin)) {
    const expected = f.vars.reduce((t, v, i) => t.replaceAll(`{${v}}`, `{${i}}`), TR_I18N[code].schema.fields[f.key].builtin);
    assert.equal(texts["msg-" + f.key.split(".")[1].toLowerCase()], expected, code + " " + f.key + " differs from the mod");
  }
  for (const [k, v] of Object.entries(lang("pl"))) assert.equal(placeholders((texts[k] || "").replace(/\{(\d)\}/g, "{a$1}")), placeholders(v.replace(/\{(\d)\}/g, "{a$1}")), code + " " + k);
}
console.log("I18N OK");
