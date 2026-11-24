import { spawn } from "child_process";
import fs from "fs";
const [, , url, out] = process.argv;
const port = 9333;
const c = spawn(
  "google-chrome",
  [
    "--headless=new",
    "--remote-debugging-port=" + port,
    "--user-data-dir=" + out + "/prof",
    "--no-first-run",
    "--lang=pl",
    "about:blank",
  ],
  { stdio: "ignore" },
);
// Runs a function in the page: its source is sent as text, so it may use the page's globals (state, refresh, load…).
const page = (f) => "(" + f + ")()";
const sl = (ms) => new Promise((r) => setTimeout(r, ms));
let ws;
for (let i = 0; i < 40; i++) {
  try {
    const l = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
    const p = l.find((x) => x.type === "page");
    if (p) {
      ws = new WebSocket(p.webSocketDebuggerUrl);
      break;
    }
  } catch {}
  await sl(250);
}
await new Promise((r) => (ws.onopen = r));
let id = 0;
const pend = {};
const logs = [];
ws.onmessage = (m) => {
  const d = JSON.parse(m.data);
  if (d.id) {
    pend[d.id](d.result || d.error);
  } else if (d.method === "Runtime.exceptionThrown")
    logs.push(d.params.exceptionDetails.exception?.description || d.params.exceptionDetails.text);
  else if (d.method === "Log.entryAdded") logs.push(d.params.entry.text);
};
const S = (method, params = {}) =>
  new Promise((r) => {
    pend[++id] = r;
    ws.send(JSON.stringify({ id, method, params }));
  });
const ev = async (e) => {
  const r = await S("Runtime.evaluate", { expression: e, returnByValue: true, awaitPromise: true });
  if (r.exceptionDetails) logs.push("EVAL " + r.exceptionDetails.exception?.description);
  return r.result?.value;
};
const shot = async (n, w, h) => {
  await S("Emulation.setDeviceMetricsOverride", { width: w, height: h, deviceScaleFactor: 1, mobile: false });
  await sl(200);
  const r = await S("Page.captureScreenshot", { captureBeyondViewport: true, format: "png" });
  fs.writeFileSync(out + "/" + n + ".png", Buffer.from(r.data, "base64"));
  console.log(n, "overflow", await ev("document.documentElement.scrollWidth-innerWidth"));
};
await S("Page.enable");
await S("Runtime.enable");
await S("Log.enable");
await S("Emulation.setFocusEmulationEnabled", { enabled: true });
await S("Emulation.setDeviceMetricsOverride", { width: 1280, height: 1000, deviceScaleFactor: 1, mobile: false });
await S("Page.navigate", { url });
await sl(900);
const sent = () =>
  ev(
    '[...document.querySelectorAll("#sentences dt")].map(x=>x.textContent+": "+x.nextSibling.textContent).join("\\n   ")',
  );

const full = async (n, w) => {
  await S("Emulation.setDeviceMetricsOverride", { width: w, height: 900, deviceScaleFactor: 1, mobile: false });
  await sl(150);
  const h = await ev("document.documentElement.scrollHeight");
  await shot(n, w, Math.min(h, 4200));
};
await full("ghost", 1280);
{
  for (const p of ["Ghost", "Rift", "Undead", "Exile", "Timeout", "Hardcore"]) {
    await ev(`document.getElementById("preset-${p}").click()`);
    console.log(p, "\n  ", await sent());
  }
}
await ev(
  'document.getElementById("preset-Exile").click();document.querySelectorAll("details.section").forEach(d=>d.open=true)',
);
// typing keeps focus; bad value is rejected then restored
await ev('document.getElementById("f-Place-At-Zone").click()');
for (const a of ["Traverse", "Use", "All"])
  console.log(
    "access",
    await ev(
      `(()=>{document.getElementById('f-Place-Access-${a}').click();return [...document.querySelectorAll('#sentences dd')][1].textContent})()`,
    ),
  );
console.log(
  "claims",
  await sent(),
  "| steps:",
  await ev('[...document.querySelectorAll("#steps code")].map(x=>x.textContent).join(" ; ")'),
  await ev(
    page(() => {
      const i = document.getElementById("f-Place-Group");
      i.focus();
      i.value = "dwa slowa";
      i.dispatchEvent(new Event("input"));
      const a = state["Place.Group"] + i.getAttribute("aria-invalid");
      i.blur();
      return a + i.value;
    }),
  ),
);
await full("claims", 1280);
await ev('document.getElementById("f-Place-At-Body").click()');
console.log(
  "special",
  await ev(
    page(() => {
      const n = (k) => document.getElementById("f-" + k);
      n("Return-SatietyPercent-custom").click();
      const a =
        state["Return.SatietyPercent"] + " " + n("Return-SatietyPercent").hidden + " " + document.activeElement.id;
      const dd = [...document.querySelectorAll("#sentences dd")]
        .map((x) => x.textContent)
        .find((x) => /Sytość/.test(x));
      n("Return-SatietyPercent-special").click();
      const b = state["Return.SatietyPercent"] + " " + n("Return-SatietyPercent").hidden;
      const w = document.getElementById("worldLives");
      w.value = "7";
      w.dispatchEvent(new Event("input"));
      const c =
        document.querySelector("#steps code").textContent +
        " / " +
        document.querySelector("#sec-lives .now").textContent;
      w.value = "3";
      w.dispatchEvent(new Event("input"));
      return [a, dd, b, c, [...document.querySelectorAll("details.section")].map((x) => x.id.slice(4)).join()].join(
        " | ",
      );
    }),
  ),
);
console.log(
  "type",
  await ev(
    page(() => {
      document.getElementById("f-Place-Radius-custom").click();
      const i = document.getElementById("f-Place-Radius");
      i.focus();
      i.value = "7";
      i.dispatchEvent(new Event("input"));
      const a = state["Place.Radius"] + " " + (document.activeElement === i);
      i.value = "-3";
      i.dispatchEvent(new Event("input"));
      const b = state["Place.Radius"] + " " + i.getAttribute("aria-invalid");
      i.blur();
      return (
        a +
        " | " +
        b +
        " | " +
        i.value +
        " " +
        i.getAttribute("aria-invalid") +
        " | " +
        document.querySelector("#preset-Exile .changes").textContent
      );
    }),
  ),
);
console.log(
  "duration",
  await ev(
    page(() => {
      document.getElementById("f-Time-Duration-next").click();
      const a = state["Time.Duration"];
      const t = document.getElementById("f-Time-Duration-time");
      t.value = "07:30";
      t.dispatchEvent(new Event("input"));
      const b = state["Time.Duration"];
      document.getElementById("f-Time-Duration-d").click();
      return [a, b, state["Time.Duration"]].join(" | ");
    }),
  ),
);
console.log(
  "reset",
  await ev(
    page(() => {
      document.querySelector('[data-key="Time.Duration"] .reset').click();
      return state["Time.Duration"];
    }),
  ),
);
console.log(
  "import",
  await ev(
    page(() => {
      document.getElementById("importToggle").click();
      document.getElementById("importText").value = '{"ReturnMode":"Hours","ReturnAfterHours":12,"Foo":1}';
      document.getElementById("importGo").click();
      return preset + " " + state["Time.Duration"] + " " + document.getElementById("importReport").innerText;
    }),
  ),
);
console.log(
  "draft survives reload",
  await (async () => {
    await S("Page.reload");
    await sl(700);
    return ev('preset+" "+state["Time.Duration"]');
  })(),
);
console.log(
  "link",
  await (async () => {
    const c = await ev('TR.encode("Hardcore",TR.applyPreset("Hardcore"))');
    await S("Page.navigate", { url: url + "#c=" + c });
    await sl(500);
    return ev('preset+" "+state.OutOfLives');
  })(),
);
console.log(
  "admin",
  await ev(
    page(() => {
      for (const k of ["AllowEdit", "AllowPoints", "AllowRevive", "AllowSetLives"])
        document.getElementById("f-Admin-" + k).click();
      document.getElementById("f-Time-Duration-forever").click();
      document.getElementById("f-Ritual-Where-Disabled").click();
      document.getElementById("f-Place-At-Fixed").click();
      const r =
        document.querySelector("#sec-admin .now").textContent +
        " | " +
        [...document.querySelectorAll("#notes li")].map((x) => x.textContent).join(" / ") +
        " | " +
        [...document.querySelectorAll(".or .hint")].map((x) => x.textContent).join();
      return r;
    }),
  ),
);
console.log(
  "rights",
  await ev(
    page(() => {
      document.getElementById("sec-admin").open = true;
      const c = () => [...document.querySelectorAll(".cmds code, .cmds p")].map((x) => x.textContent).join(" ; ");
      for (const k of ["AllowRevive", "AllowPoints"])
        if (!state["Admin." + k]) document.getElementById("f-Admin-" + k).click();
      const a = c();
      const b = "";
      document.getElementById("rights-player").click();
      const d = c();
      const n = document.getElementById("rightsName");
      n.focus();
      n.value = "Kasia";
      n.dispatchEvent(new Event("input"));
      const e = c() + " focus=" + (document.activeElement === n);
      document.getElementById("f-Admin-AllowPoints").click();
      return [a, b, d, e, c()].join(" | ");
    }),
  ),
);
console.log(
  "gate",
  await ev(
    page(() => {
      const b = document.getElementById("f-Lives-Count-Downs"),
        sw = document.getElementById("hasDowned");
      const r = ["start " + sw.checked + " " + b.getAttribute("aria-disabled") + " " + b.title];
      b.click();
      r.push("klik " + state["Lives.Count"]);
      sw.click();
      b.click();
      r.push("on " + state["Lives.Count"] + " " + b.getAttribute("aria-disabled") + " [" + b.title + "]");
      sw.click();
      r.push("off " + state["Lives.Count"]);
      load('{"OutOfLives":"Ghost","Lives":{"Count":"DownsFinalDeath"}}');
      r.push("import " + state["Lives.Count"] + " " + sw.checked);
      const a = document.querySelector(".server a");
      r.push(a.href + " " + a.target);
      document.getElementById("f-Lives-Count-Deaths").click();
      return r.join(" | ");
    }),
  ),
);
console.log(
  "downs",
  await ev(
    page(() => {
      document.getElementById("f-Lives-Count-Downs").click();
      const a = document.querySelector("#sentences dd").textContent;
      document.getElementById("f-Lives-Count-DownsFinalDeath").click();
      const r = [
        a,
        document.querySelector("#sentences dd").textContent,
        document.querySelector("#sec-lives .now").textContent,
        "",
      ].join(" | ");
      document.getElementById("f-Lives-Count-Deaths").click();
      return r;
    }),
  ),
);
console.log(
  "ritualhelp",
  await ev(
    page(() => {
      const h = () =>
        [...document.querySelectorAll('[data-key="Ritual.Where"] .help')]
          .filter((x) => !x.hidden)
          .map((x) => x.textContent)
          .join(" / ");
      const r = [];
      for (const v of ["Disabled", "Rift", "NearPlayer", "FixedPlace", "Anywhere"]) {
        document.getElementById("f-Ritual-Where-" + v).click();
        r.push(v + ": " + h());
      }
      return r.join(" | ");
    }),
  ),
);
console.log(
  "rightsoff",
  await ev(
    page(() => {
      const ks = ["AllowEdit", "AllowPoints", "AllowRevive", "AllowSetLives"];
      const was = ks.map((k) => state["Admin." + k]);
      ks.forEach((k) => {
        if (state["Admin." + k]) document.getElementById("f-Admin-" + k).click();
      });
      const t = document.getElementById("rights").innerText.replace(/\n+/g, " / ");
      ks.forEach((k, i) => {
        if (state["Admin." + k] !== was[i]) document.getElementById("f-Admin-" + k).click();
      });
      return t;
    }),
  ),
);
console.log(
  "teleport",
  await ev(
    page(() => {
      document.getElementById("preset-Ghost").click();
      const h = () =>
        [...document.querySelectorAll('[data-key="Ritual.ReviveAt"] .help')]
          .filter((x) => !x.hidden)
          .map((x) => x.textContent)
          .join() || "-";
      return ["Rift", "NearPlayer", "FixedPlace", "Anywhere"]
        .map((v) => {
          document.getElementById("f-Ritual-Where-" + v).click();
          return v + ": " + h();
        })
        .join(" | ");
    }),
  ),
);
console.log(
  "undead",
  await ev(
    page(() => {
      document.getElementById("preset-Undead").click();
      return (
        [...document.querySelectorAll("#sentences dd")]
          .slice(-2)
          .map((x) => x.textContent)
          .join(" | ") +
        " | ostrz: " +
        document.querySelectorAll("#notes li").length
      );
    }),
  ),
);
await ev(
  'document.getElementById("preset-Ghost").click();document.getElementById("f-Time-Duration-forever").click();document.querySelectorAll("details.section").forEach(d=>d.open=true)',
);
await full("warn", 1280);
console.log(
  "resettext",
  await ev(
    page(() => {
      document.getElementById("preset-Ghost").click();
      document.getElementById("f-Time-Duration-forever").click();
      const a = document.querySelector('[data-key="Time.Duration"] .reset').textContent;
      document.getElementById("preset-Exile").click();
      document.getElementById("f-Return-Lives-custom").click();
      return a + " | " + document.querySelector('[data-key="Return.Lives"] .reset').textContent;
    }),
  ),
);
console.log(
  "visible",
  await ev(
    page(() => {
      document.getElementById("preset-Ghost").click();
      return ["Nobody", "Wisp", "WispAndEcho"]
        .map((v) => {
          document.getElementById("f-Ghost-Visible-" + v).click();
          return document.querySelector("#sentences dd").textContent;
        })
        .join(" | ");
    }),
  ),
);
console.log(
  "msgs",
  await ev(
    page(() => {
      document.getElementById("tab-messages").click();
      const vis = [...document.querySelectorAll("details.section")]
        .filter((x) => !x.hidden)
        .map((x) => x.id)
        .join();
      const i = document.getElementById("f-Messages-Returned");
      const r = [vis, i.placeholder];
      i.focus();
      i.value = "Witaj {player}";
      i.dispatchEvent(new Event("input"));
      r.push(
        state["Messages.Returned"],
        document.getElementById("tab-messages").textContent,
        document.querySelector('[data-key="Messages.Returned"] .reset').textContent,
      );
      i.value = "x {foo}";
      i.dispatchEvent(new Event("input"));
      r.push(
        i.getAttribute("aria-invalid"),
        document.querySelector('[data-key="Messages.Returned"] .error').textContent,
      );
      i.blur();
      r.push(i.value);
      return r.join(" | ");
    }),
  ),
);
await full("msgs", 1280);
await ev(
  'document.querySelector(\'[data-key="Messages.Returned"] .reset\').click();document.getElementById("tab-settings").click()',
);
console.log(
  "near",
  await ev(
    page(() => {
      document.getElementById("preset-Ghost").click();
      document.getElementById("f-Ritual-Where-NearPlayer").click();
      const g = () => [...document.querySelectorAll("#sentences dd")].at(-1).textContent;
      const a = g();
      document.getElementById("f-OutOfLives-Ban").click();
      return a + " | " + g() + " | " + [...document.querySelectorAll("#notes li")].map((x) => x.textContent).join();
    }),
  ),
);
console.log(
  "where",
  await ev('document.querySelector("#sec-admin #rights")!==null&&!document.querySelector("#steps #rightsName")'),
);
console.log(
  "allmix",
  await ev(
    page(() => {
      const bad = [];
      {
        for (const f of ["Ghost", "Spectator", "Zombie", "Player", "Ban"])
          for (const a of ["Body", "Spawn", "Fixed", "Zone"])
            for (const d of ["1h", "2d", "next", "forever"])
              for (const w of ["Disabled", "Rift", "NearPlayer", "FixedPlace", "Anywhere"]) {
                state.OutOfLives = f;
                state["Place.At"] = a;
                state["Time.Duration"] = d === "next" ? "next day 06:00" : d;
                state["Ritual.Where"] = w;
                refresh();
                const x = document.querySelector(".story").innerText;
                if (/undefined|NaN|null|\[object/.test(x)) bad.push(f + a + d + w);
              }
      }
      return (
        bad.length +
        " bad; visible sections for Ban: " +
        (() => {
          state.OutOfLives = "Ban";
          refresh();
          return [...document.querySelectorAll("details.section")]
            .filter((x) => !x.hidden)
            .map((x) => x.id)
            .join();
        })()
      );
    }),
  ),
);
await ev(
  'document.getElementById("preset-Exile").click();document.querySelectorAll("details").forEach(d=>d.open=true)',
);
for (const k of ["AllowEdit", "AllowPoints", "AllowRevive", "AllowSetLives", "AllowPoints"]) {
  await ev(`(()=>{const c=document.getElementById('f-Admin-${k}');if(!c.checked)c.click()})()`);
}
await full("exile-open", 1280);
await full("phone", 390);
console.log("errors", JSON.stringify(logs));
c.kill();
process.exit(0);
