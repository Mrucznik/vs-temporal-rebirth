// Puts a language's texts over the schema. Polish is the schema itself; the others live in i18n/.
(function (root) {
  const schema = root.TR_SCHEMA;
  const polish = JSON.parse(JSON.stringify({ sections: schema.sections, fields: schema.fields, presets: schema.presets }));

  const put = (target, source, keys) => {
    for (const k of keys) if (source[k] === undefined) delete target[k]; else target[k] = source[k];
  };

  /** Changes the texts in place, so everything holding a field sees them. Values and keys never change. */
  root.TR.localize = function (lang) {
    const l = root.TR_I18N[lang], s = l.schema;
    schema.sections.forEach((section, i) => {
      put(section, polish.sections[i], ["label", "help"]);
      if (s) put(section, { ...section, ...s.sections[section.id] }, ["label", "help"]);
    });
    schema.fields.forEach((field, i) => {
      const from = polish.fields[i];
      put(field, from, ["label", "help", "file", "group", "builtin"]);
      (field.options || []).forEach((o, n) => put(o, from.options[n], ["label", "help"]));
      if (field.special) put(field.special, from.special, ["label", "custom", "help"]);
      if (!s) return;
      const t = s.fields[field.key];
      for (const k of ["label", "help", "file", "builtin"]) if (t[k] !== undefined) field[k] = t[k];
      if (field.group) field.group = s.groups[field.group];
      for (const o of field.options || []) put(o, { label: t.options[o.value][0], help: t.options[o.value][1] }, ["label", "help"]);
      if (field.special) put(field.special, { label: t.special[0], custom: t.special[1], help: t.special[2] }, ["label", "custom", "help"]);
    });
    schema.presets.forEach((preset, i) => {
      put(preset, polish.presets[i], ["tag", "text"]);
      if (s) [preset.tag, preset.text] = s.presets[preset.id];
    });
    Object.assign(root.TR.texts, l.file);
  };
})(typeof window !== "undefined" ? window : globalThis);
