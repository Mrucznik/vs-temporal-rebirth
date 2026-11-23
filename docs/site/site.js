// The mod's page: fills docs/index.html from a language file (site/i18n/<lang>.js) and the configurator's schema.
(function () {
  const LANGS = Object.keys(window.TR_SITE);
  const store = {
    get(k) { try { return localStorage.getItem("tr." + k); } catch { return null; } },
    set(k, v) { try { localStorage.setItem("tr." + k, v); } catch { /* private window */ } },
  };
  // Shares "tr.lang" with the configurator, so a choice made there carries over once the page has that language.
  let lang = [store.get("lang"), (navigator.language || "").slice(0, 2)].find((l) => LANGS.includes(l)) || (LANGS.includes("en") ? "en" : LANGS[0]);
  let L;

  const $ = (id) => document.getElementById(id);
  const get = (path) => path.split(".").reduce((o, k) => o?.[k], L);
  const el = (tag, props = {}, ...kids) => {
    const n = document.createElement(tag);
    for (const [k, v] of Object.entries(props)) {
      if (k === "class") n.className = v;
      else if (k === "html") n.innerHTML = v;
      else if (k === "textContent") n.textContent = v;
      else if (k.startsWith("on")) n.addEventListener(k.slice(2), v);
      else if (k in n && typeof v !== "string") n[k] = v;
      else n.setAttribute(k, v);
    }
    n.append(...kids.filter((x) => x != null && x !== false));
    return n;
  };
  const reduced = matchMedia("(prefers-reduced-motion: reduce)");
  const WIKI = window.TR_SCHEMA.wikiBase;

  /* ---------- videos: play while visible, click to enlarge ---------- */
  const seen = new IntersectionObserver((entries) => {
    for (const e of entries) {
      const v = e.target;
      v.dataset.visible = e.isIntersecting ? "1" : "";
      if (e.isIntersecting && !reduced.matches && !document.hidden) v.play().catch(() => {});
      else v.pause();
    }
  }, { threshold: 0.35 });

  function setClip(video, name) {
    if (video.dataset.clip === name) return;
    video.dataset.clip = name;
    video.poster = "media/" + name + ".jpg";
    video.src = "media/" + name + ".mp4";
    if (video.dataset.visible && !reduced.matches) video.play().catch(() => {});
  }

  /** A looping, muted clip that opens larger on click. caption may hold HTML. */
  function clip(name, caption, cls = "") {
    const video = el("video", { muted: true, loop: true, playsInline: true, preload: "none", disablePictureInPicture: true });
    video.setAttribute("muted", "");
    video.setAttribute("playsinline", "");
    setClip(video, name);
    seen.observe(video);
    const cap = el("figcaption", { html: caption || "" });
    const btn = el("button", { type: "button", class: "clip-btn", "aria-label": L.video.enlarge }, video,
      el("span", { class: "clip-zoom", "aria-hidden": "true" }, "⤢"));
    btn.addEventListener("click", () => openLightbox(video.dataset.clip, cap.innerHTML));
    const fig = el("figure", { class: "clip " + cls }, btn, cap);
    fig.set = (n, c) => { setClip(video, n); cap.innerHTML = c || ""; };
    return fig;
  }

  function openLightbox(name, caption) {
    const d = $("lightbox"), v = $("lightboxVideo");
    v.poster = "media/" + name + ".jpg";
    v.src = "media/" + name + ".mp4";
    $("lightboxCaption").innerHTML = caption;
    d.showModal();
    v.play().catch(() => {});
  }
  $("lightboxClose").addEventListener("click", () => $("lightbox").close());
  $("lightbox").addEventListener("close", () => { $("lightboxVideo").pause(); $("lightboxVideo").removeAttribute("src"); $("lightboxVideo").load(); });
  $("lightbox").addEventListener("click", (e) => { if (e.target === $("lightbox")) $("lightbox").close(); });
  document.addEventListener("visibilitychange", () => {
    for (const v of document.querySelectorAll("main video")) if (document.hidden) v.pause(); else if (v.dataset.visible && !reduced.matches) v.play().catch(() => {});
  });

  /** Tabs that move with arrow keys; pick(i) shows tab i. */
  function tablist(list, buttons, pick) {
    buttons.forEach((b, i) => {
      b.setAttribute("role", "tab");
      b.addEventListener("click", () => pick(i));
      b.addEventListener("keydown", (e) => {
        const d = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key];
        if (!d) return;
        e.preventDefault();
        const j = (i + d + buttons.length) % buttons.length;
        pick(j);
        buttons[j].focus();
      });
    });
    list.replaceChildren(...buttons.map((b) => b.closest("li") || b));
  }
  const select = (buttons, i) => buttons.forEach((b, j) => { b.setAttribute("aria-selected", String(i === j)); b.tabIndex = i === j ? 0 : -1; });

  /* ---------- sections ---------- */
  function texts() {
    document.documentElement.lang = lang;
    document.title = L.meta.title;
    document.querySelector('meta[name="description"]').content = L.meta.description;
    for (const n of document.querySelectorAll("[data-t]")) n.textContent = get(n.dataset.t);
    for (const n of document.querySelectorAll("[data-h]")) n.innerHTML = get(n.dataset.h);
    $("toc").setAttribute("aria-label", L.nav.menu);
    const ids = { how: "jak-to-dziala", presets: "presety", forms: "formy", rituals: "rytualy", faq: "faq" };
    $("toc").replaceChildren(...Object.entries(L.nav.sections).map(([k, label]) => el("a", { href: "#" + ids[k], textContent: label })));
    $("facts").replaceChildren(...L.hero.facts.map((f) => el("li", { textContent: f })));
    $("heroMedia").replaceChildren(clip("M1", L.hero.video, "hero-clip"));
  }

  function journey() {
    const steps = L.how.steps, stage = $("stage");
    const fig = clip(steps[0].video, steps[0].caption);
    const title = el("h3"), text = el("p"), more = el("p", { class: "more" }), count = el("span", { class: "count" });
    stage.replaceChildren(fig, el("div", { class: "stage-text" }, count, title, text, more));
    let at = 0;
    const buttons = steps.map((s, i) => {
      const b = el("button", { type: "button", id: "step-" + i, "aria-controls": "stage" },
        el("span", { class: "dot", "aria-hidden": "true" }, el("svg", {}), String(i + 1)), el("span", { class: "label", textContent: s.short }));
      b.querySelector("svg").outerHTML = '<svg aria-hidden="true"><use href="#gear"/></svg>';
      const li = el("li", { class: s.alt ? "alt" : "" }, b);
      if (s.alt) li.prepend(el("span", { class: "or", textContent: L.how.or }));
      return b;
    });
    const pick = (i) => {
      at = i;
      const s = steps[i];
      select(buttons, i);
      fig.set(s.video, s.caption);
      count.textContent = i + 1 + " / " + steps.length;
      title.textContent = s.title;
      text.innerHTML = s.text;
      more.innerHTML = s.more;
      stage.setAttribute("aria-labelledby", "step-" + i);
      buttons.forEach((b, j) => b.parentElement.classList.toggle("done", j < i));
    };
    tablist($("path"), buttons, pick);
    pick(0);
  }

  function presets() {
    const P = L.presets, list = window.TR_SCHEMA.presets;
    const fig = clip(P.videos[list[0].id], P.captions[list[0].id]);
    const head = el("div", { class: "pv-head" }), dl = el("dl", { class: "facts-table" });
    const link = el("a", { class: "btn", textContent: P.configure });
    $("presetView").replaceChildren(el("div", { class: "pv-text" }, head, dl, link), fig);
    const buttons = list.map((p) => el("button", { type: "button", id: "preset-" + p.id, class: "preset", "aria-controls": "presetView" },
      el("svg", { "aria-hidden": "true" }), el("b", { textContent: p.id }), el("span", { class: "tag", textContent: p.tag })));
    buttons.forEach((b, i) => { b.querySelector("svg").outerHTML = `<svg aria-hidden="true" viewBox="0 0 130 130"><use href="#i-${list[i].icon}" width="130" height="130"/></svg>`; });
    const pick = (i) => {
      const p = list[i], s = window.TR.applyPreset(p.id), d = window.TR.parseDuration(s["Time.Duration"]);
      select(buttons, i);
      head.replaceChildren(el("h3", { textContent: p.id }), el("p", { class: "sub", textContent: p.text }));
      const rows = [["form", P.form(s)], ["where", P.where(s)], ["time", P.time(d)], ["back", P.back(s, d.kind === "forever")], ["ritual", P.ritual(s)]];
      dl.replaceChildren(...rows.flatMap(([k, v]) => [el("dt", { textContent: P.rows[k] }), el("dd", { html: v })]));
      link.href = "configurator/index.html#c=" + window.TR.encode(p.id, s);
      fig.set(P.videos[p.id], P.captions[p.id]);
      $("presetView").setAttribute("aria-labelledby", "preset-" + p.id);
    };
    tablist($("presetList"), buttons, pick);
    pick(0);
  }

  function forms() {
    $("forms").replaceChildren(...L.forms.list.map((f) => {
      const fig = clip(f.media[0].v, f.media[0].caption);
      const chips = f.media.length > 1 ? el("div", { class: "chips" }, ...f.media.map((m, i) => {
        const b = el("button", { type: "button", class: "chip", "aria-pressed": String(i === 0), textContent: m.label || f.name });
        b.addEventListener("click", () => {
          fig.set(m.v, m.caption);
          b.parentElement.querySelectorAll("button").forEach((x) => x.setAttribute("aria-pressed", String(x === b)));
        });
        return b;
      })) : null;
      const icon = el("span", { class: "form-icon", html: `<svg aria-hidden="true" viewBox="0 0 130 130"><use href="#i-${f.id}" width="130" height="130"/></svg>` });
      return el("article", { class: "form", id: "form-" + f.id },
        el("div", { class: "form-head" }, icon, el("h3", { textContent: f.name }), f.tag ? el("span", { class: "badge", textContent: f.tag }) : null),
        fig, chips,
        ...f.text.map((t) => el("p", { html: t })),
        el("a", { class: "wiki", href: WIKI + f.wiki, target: "_blank", rel: "noopener", textContent: L.forms.more + " ↗" }));
    }));
  }

  function rituals() {
    const R = L.rituals;
    $("ritualSteps").replaceChildren(...R.steps.map((s) => el("li", { html: s })));
    const first = R.places[0];
    const fig = clip(first.video, first.name + ": " + first.text);
    $("ritualMedia").replaceChildren(fig);
    $("places").replaceChildren(...R.places.map((p, i) => {
      const b = el("button", { type: "button", class: "place", "aria-pressed": String(i === 0), disabled: !p.video },
        el("b", { textContent: p.name }), el("span", { html: p.text }));
      if (p.video) b.addEventListener("click", () => {
        fig.set(p.video, p.name + ": " + p.text);
        $("places").querySelectorAll("button").forEach((x) => x.setAttribute("aria-pressed", String(x === b)));
        if (matchMedia("(max-width: 860px)").matches) fig.scrollIntoView({ behavior: reduced.matches ? "auto" : "smooth", block: "center" });
      });
      if (p.video) b.append(el("span", { class: "watch", "aria-hidden": "true", textContent: "▶" }));
      return b;
    }));
  }

  function cmd(text) {
    const b = el("button", { type: "button", class: "copy", "aria-label": "Copy", textContent: "⧉" });
    b.addEventListener("click", async () => {
      try { await navigator.clipboard.writeText(text); b.textContent = "✓"; } catch { b.textContent = "✕"; }
      setTimeout(() => { b.textContent = "⧉"; }, 1400);
    });
    return el("span", { class: "cmd" }, el("code", { textContent: text }), b);
  }

  function faq() {
    const item = (f) => el("details", { class: "qa" }, el("summary", { textContent: f.q }), el("p", { html: f.a }));
    $("faqPlayers").replaceChildren(...L.faq.list.filter((f) => f.who === "players").map(item));
    $("faqOwners").replaceChildren(...L.faq.list.filter((f) => f.who === "owners").map(item));
  }

  function footer() {
    const F = L.footer.links;
    const links = [["https://mods.vintagestory.at/show/mod/73648", F.moddb], ["https://github.com/Mrucznik/vs-temporal-rebirth", F.github],
      ["https://github.com/Mrucznik/vs-temporal-rebirth/issues", F.issues], [WIKI.replace(/\/$/, ""), F.wiki], ["configurator/index.html", F.config]];
    $("footLinks").replaceChildren(...links.map(([href, label]) => el("a", href.startsWith("http") ? { href, target: "_blank", rel: "noopener", textContent: label } : { href, textContent: label })));
  }

  /** Flags only once a second language exists. */
  function languages() {
    const box = $("lang");
    box.hidden = LANGS.length < 2;
    if (box.hidden) return;
    box.setAttribute("aria-label", L.nav.language);
    box.replaceChildren(...LANGS.map((code) => el("button", { type: "button", "aria-pressed": String(code === lang), textContent: code.toUpperCase(), title: window.TR_SITE[code].name,
      onclick: () => { store.set("lang", code); render(code); } })));
  }

  function render(code) {
    lang = code;
    L = window.TR_SITE[lang];
    texts(); journey(); presets(); forms(); rituals(); faq(); footer(); languages();
    document.dispatchEvent(new CustomEvent("tr:lang", { detail: L }));
  }

  window.TR_PAGE = { get L() { return L; }, reduced };
  render(lang);

  // Highlights the section in view in the table of contents.
  const marks = new IntersectionObserver((entries) => {
    for (const e of entries) if (e.isIntersecting) for (const a of $("toc").querySelectorAll("a")) a.toggleAttribute("aria-current", a.hash === "#" + e.target.id);
  }, { rootMargin: "-45% 0px -50% 0px" });
  document.querySelectorAll("main > section[id]").forEach((s) => marks.observe(s));
})();
