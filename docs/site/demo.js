// "See it yourself": the mod's own shaders (site/shaders.js, made by site/build.py) over a made-up blocky world.
// Like test/shaders/live.html: the world is drawn into a colour and a depth texture, then the effect shaders draw
// over it with the same uniforms the game gives them (EchoLook, WispOrb, LeashLook).
(function () {
  const W = 960, H = 540, EYE = [0, 1.6, 0], FAR = 1500, NEAR = 0.1;
  const $ = (id) => document.getElementById(id);
  const page = window.TR_PAGE, SHADERS = window.TR_SHADERS || {};
  const canvas = $("demoCanvas");
  let mode = "ghost", paused = page.reduced.matches, visible = false, shown = 0, last = performance.now(), hitAt = -1, wallWas = 0;

  function fail() {
    canvas.hidden = true;
    $("echoName").hidden = true;
    $("demoFallback").hidden = false;
    for (const id of ["ghostControls", "leashControls", "demoTabs", "pause"]) $(id).hidden = true;
  }

  function multiply(a, b) {
    const r = new Float32Array(16);
    for (let i = 0; i < 4; i++) for (let j = 0; j < 4; j++) for (let k = 0; k < 4; k++) r[i * 4 + j] += a[k * 4 + j] * b[i * 4 + k];
    return r;
  }

  function invert(m) {
    const rows = [0, 1, 2, 3].map((i) => [m[i], m[4 + i], m[8 + i], m[12 + i], ...[0, 1, 2, 3].map((j) => Number(i === j))]);
    for (let i = 0; i < 4; i++) {
      let p = i;
      for (let k = i; k < 4; k++) if (Math.abs(rows[k][i]) > Math.abs(rows[p][i])) p = k;
      [rows[i], rows[p]] = [rows[p], rows[i]];
      rows[i] = rows[i].map((x) => x / rows[i][i]);
      for (let k = 0; k < 4; k++) if (k !== i) { const f = rows[k][i]; rows[k] = rows[k].map((x, j) => x - f * rows[i][j]); }
    }
    const r = new Float32Array(16);
    for (let i = 0; i < 4; i++) for (let j = 0; j < 4; j++) r[j * 4 + i] = rows[i][4 + j];
    return r;
  }

  function viewProjection(pitch) {
    const f = 1 / Math.tan(0.6), c = Math.cos(pitch), s = Math.sin(pitch);
    const projection = [f / (W / H), 0, 0, 0, 0, f, 0, 0, 0, 0, (FAR + NEAR) / (NEAR - FAR), -1, 0, 0, 2 * FAR * NEAR / (NEAR - FAR), 0];
    const view = [1, 0, 0, 0, 0, c, s, 0, 0, -s, c, 0, 0, 0, 0, 1];
    for (let i = 0; i < 3; i++) view[12 + i] = -(view[i] * EYE[0] + view[4 + i] * EYE[1] + view[8 + i] * EYE[2]);
    return multiply(projection, view);
  }

  // The made-up world of test/shaders/live.html: grass, stone and dirt pillars, sky; light goes from night to day.
  const WORLD_VERTEX = `#version 300 es
precision highp float;
layout(location = 0) in vec3 position;
layout(location = 1) in vec2 uv;
uniform mat4 inverseViewProjection;
out vec3 farPoint;
void main() {
  vec4 p = inverseViewProjection * vec4(uv * 2.0 - 1.0, 1.0, 1.0);
  farPoint = p.xyz / p.w;
  gl_Position = vec4(uv * 2.0 - 1.0, 0.0, 1.0);
}`;
  const WORLD_FRAGMENT = `#version 300 es
precision highp float;
in vec3 farPoint;
uniform vec3 camera;
uniform mat4 viewProjection;
uniform float light;
layout(location = 0) out vec4 color;
layout(location = 1) out vec4 depth;
float hash(vec2 p) { return fract(sin(dot(floor(p), vec2(12.9, 78.2))) * 43758.5); }
float hash3(vec3 p) { return fract(sin(dot(floor(p), vec3(12.9, 78.2, 37.7))) * 43758.5); }
void main() {
  vec3 d = normalize(farPoint - camera);
  vec3 dusk = mix(vec3(0.55, 0.25, 0.22), vec3(0.75, 0.85, 0.95), smoothstep(0.3, 0.8, light));
  vec3 sky = mix(dusk, mix(vec3(0.05, 0.06, 0.14), vec3(0.35, 0.55, 0.85), light), clamp(d.y * 2.5, 0.0, 1.0));
  vec3 c = sky;
  float t = 1e9;
  if (d.y < 0.0) {
    t = -camera.y / d.y;
    vec3 p = camera + d * t;
    c = mix(vec3(0.25, 0.42, 0.16), vec3(0.36, 0.52, 0.22), hash(p.xz * 2.0)) * mix(0.9, 1.1, hash(p.xz * 16.0));
  }
  for (int i = 0; i < 6; i++) {
    vec3 lo = vec3(float(i * 4 - 11), 0.0, -14.0 - float(i % 3) * 5.0), hi = lo + vec3(3.0, 3.0 + float(i % 2) * 3.0, 3.0);
    vec3 m = (lo - camera) / d, M = (hi - camera) / d, a = min(m, M), b = max(m, M);
    float enter = max(max(a.x, a.y), a.z), leave = min(min(b.x, b.y), b.z);
    if (enter > 0.0 && enter < leave && enter < t) {
      t = enter;
      vec3 p = camera + d * t;
      float n = hash3(p * 16.0 + 0.001);
      c = (i % 2 == 0 ? vec3(0.5, 0.48, 0.45) : vec3(0.42, 0.3, 0.2)) * mix(0.85, 1.1, n);
      if (p.y > hi.y - 0.06) c = vec3(0.3, 0.48, 0.2);
    }
  }
  if (t < 1e8) {
    c *= mix(vec3(0.25, 0.3, 0.5) * 0.35, vec3(1.0), light);
    c = mix(c, sky, 1.0 - exp(-t * 0.01));
  }
  color = vec4(c, 1.0);
  vec4 p = viewProjection * vec4(camera + d * min(t, 1400.0), 1.0);
  depth = vec4(t > 1400.0 ? 1.0 : p.z / p.w * 0.5 + 0.5);
}`;

  const gl = canvas.getContext("webgl2", { antialias: false, alpha: false, preserveDrawingBuffer: false });
  if (!gl || !SHADERS["echo.fsh"] || !gl.getExtension("EXT_color_buffer_float")) { fail(); return; }

  const problems = [];
  for (const [place, size, data] of [[0, 3, [-1, -1, 0, 1, -1, 0, 1, 1, 0, -1, -1, 0, 1, 1, 0, -1, 1, 0]], [1, 2, [0, 0, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1]]]) {
    gl.bindBuffer(gl.ARRAY_BUFFER, gl.createBuffer());
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(data), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(place);
    gl.vertexAttribPointer(place, size, gl.FLOAT, false, 0, 0);
  }

  function program(vertex, fragment, name) {
    const p = gl.createProgram();
    for (const [type, source] of [[gl.VERTEX_SHADER, vertex], [gl.FRAGMENT_SHADER, fragment]]) {
      const shader = gl.createShader(type);
      gl.shaderSource(shader, source);
      gl.compileShader(shader);
      if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) problems.push(name + ": " + gl.getShaderInfoLog(shader));
      gl.attachShader(p, shader);
    }
    gl.linkProgram(p);
    if (!gl.getProgramParameter(p, gl.LINK_STATUS)) problems.push(name + ": " + gl.getProgramInfoLog(p));
    return p;
  }

  function texture(format) {
    const t = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, t);
    gl.texStorage2D(gl.TEXTURE_2D, 1, format, W, H);
    for (const key of [gl.TEXTURE_MIN_FILTER, gl.TEXTURE_MAG_FILTER]) gl.texParameteri(gl.TEXTURE_2D, key, gl.NEAREST);
    return t;
  }

  const world = program(WORLD_VERTEX, WORLD_FRAGMENT, "world");
  const echo = program(SHADERS["echo.vsh"], SHADERS["echo.fsh"], "echo");
  const wisp = program(SHADERS["wisp.vsh"], SHADERS["wisp.fsh"], "wisp");
  const leash = program(SHADERS["leash.vsh"], SHADERS["leash.fsh"], "leash");
  const color = texture(gl.RGBA8), depth = texture(gl.R32F), target = gl.createFramebuffer();
  gl.bindFramebuffer(gl.FRAMEBUFFER, target);
  gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, color, 0);
  gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT1, gl.TEXTURE_2D, depth, 0);
  gl.drawBuffers([gl.COLOR_ATTACHMENT0, gl.COLOR_ATTACHMENT1]);
  if (gl.checkFramebufferStatus(gl.FRAMEBUFFER) !== gl.FRAMEBUFFER_COMPLETE) problems.push("framebuffer incomplete");
  gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  if (problems.length) { console.warn("Temporal Rebirth shaders:", problems.join("\n")); fail(); return; }
  canvas.dataset.ready = "1";

  const vp = viewProjection(0.12), inverse = invert(vp);
  function use(p, time, light) {
    gl.useProgram(p);
    const at = (name) => gl.getUniformLocation(p, name);
    gl.uniformMatrix4fv(at("viewProjection"), false, vp);
    gl.uniformMatrix4fv(at("inverseViewProjection"), false, inverse);
    gl.uniform3fv(at("camera"), EYE);
    gl.uniform1f(at("time"), time);
    gl.uniform1i(at("sceneColor"), 0);
    gl.uniform1i(at("sceneDepth"), 1);
    gl.uniform1f(at("light"), light);
    return at;
  }

  // EchoLook gives half width 0.7 and height 2.1, WispOrb a radius of 0.45; both face the camera.
  function billboard(at, x, y, z, half, height, seed) {
    const dx = EYE[0] - x, dz = EYE[2] - z, length = Math.max(0.001, Math.hypot(dx, dz));
    gl.uniform3f(at("feet"), x, y, z);
    gl.uniform3f(at("side"), dz / length * half, 0, -dx / length * half);
    gl.uniform1f(at("height"), height);
    gl.uniform1f(at("seed"), seed);
    gl.drawArrays(gl.TRIANGLES, 0, 6);
  }

  const wallDistance = () => 0.3 + 29.7 * (1 - +$("wall").value) ** 2;
  const blocks = (n) => page.L.demo.blocks(n);

  /** Screen position of a world point, in % of the canvas, or null behind the camera. */
  function project(x, y, z) {
    const m = vp, w = m[3] * x + m[7] * y + m[11] * z + m[15];
    if (w <= 0) return null;
    return [((m[0] * x + m[4] * y + m[8] * z + m[12]) / w * 0.5 + 0.5) * 100, (1 - ((m[1] * x + m[5] * y + m[9] * z + m[13]) / w * 0.5 + 0.5)) * 100];
  }

  function frame(now) {
    if (!paused) shown += Math.min(0.1, (now - last) / 1000);
    last = now;
    const t = shown;
    const light = +$(mode === "ghost" ? "light" : "light2").value;
    gl.viewport(0, 0, W, H);
    gl.disable(gl.BLEND);
    gl.bindFramebuffer(gl.FRAMEBUFFER, target);
    use(world, t, light);
    gl.drawArrays(gl.TRIANGLES, 0, 6);
    gl.bindFramebuffer(gl.READ_FRAMEBUFFER, target);
    gl.bindFramebuffer(gl.DRAW_FRAMEBUFFER, null);
    gl.readBuffer(gl.COLOR_ATTACHMENT0);
    gl.blitFramebuffer(0, 0, W, H, 0, 0, W, H, gl.COLOR_BUFFER_BIT, gl.NEAREST);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, color);
    gl.activeTexture(gl.TEXTURE1); gl.bindTexture(gl.TEXTURE_2D, depth);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);

    const name = $("echoName");
    if (mode === "ghost") {
      const dist = +$("dist").value, gear = $("gearOn").checked;
      const x = 0.4 + Math.sin(t * 0.35) * 0.8, y = Math.max(0, Math.sin(t * 0.22)) * 1.2, z = -dist;
      // The game culls the echo beyond 48 blocks and the wisp beyond 96; the shaders fade them before that.
      if (gear && dist < 48) billboard(use(echo, t, light), x, y, z, 0.7, 2.1, 7);
      if (dist < 96) billboard(use(wisp, t, light), x, y + 1.2 + 0.12 * Math.sin(t * 1.7) - 0.45, z, 0.45, 0.9, 7);
      // The name over the echo, like EchoLook.DrawNames, within 24 blocks.
      const at = gear && dist < 24 ? project(x, y + 2.25, z) : null;
      name.hidden = !at;
      if (at) { name.style.left = at[0] + "%"; name.style.top = at[1] + "%"; name.style.opacity = String(Math.min(1, (24 - dist) / 4)); }
    } else {
      name.hidden = true;
      // LeashLook: a sphere of radius 24 whose wall is `wall` blocks ahead of the camera.
      const wall = wallDistance(), radius = 24, fromCenter = [EYE[0], EYE[1], EYE[2] + wall - radius], away = Math.hypot(...fromCenter);
      const at = use(leash, t, light);
      gl.uniform3fv(at("fromCenter"), fromCenter);
      gl.uniform1f(at("outside"), (away - radius) * (away + radius));
      gl.uniform3f(at("player"), 0, -0.6, 0);
      const age = hitAt < 0 ? -1 : (now - hitAt) / 1000;
      gl.uniform4f(at("hit0"), 0.3, -0.4, -wall, age >= 0 && age < 2.5 ? age : -1);
      for (const h of ["hit1", "hit2"]) gl.uniform4f(at(h), 0, 0, 0, -1);
      gl.drawArrays(gl.TRIANGLES, 0, 6);
    }
  }

  // Runs only while the canvas is on screen and the tab is visible; a paused demo redraws only on input.
  let pending = false;
  function loop(now) {
    pending = false;
    frame(now);
    if (visible && !paused && !document.hidden) { pending = true; requestAnimationFrame(loop); }
  }
  const kick = () => { if (!pending) { pending = true; last = performance.now(); requestAnimationFrame(loop); } };
  new IntersectionObserver(([e]) => { visible = e.isIntersecting; if (visible) kick(); }, { threshold: 0.05 }).observe(canvas);
  document.addEventListener("visibilitychange", () => { if (!document.hidden && visible) kick(); });

  function outputs() {
    $("distOut").textContent = blocks(+$("dist").value);
    $("wallOut").textContent = blocks(wallDistance());
  }
  for (const id of ["dist", "light", "light2", "gearOn"]) $(id).addEventListener("input", () => { outputs(); kick(); });
  $("wall").addEventListener("input", () => {
    outputs();
    // Pressing right up to the wall hits it, as the game does when the ghost is stopped.
    const v = +$("wall").value;
    if (v > 0.985 && wallWas <= 0.985) hitAt = performance.now();
    wallWas = v;
    kick();
  });
  $("bump").addEventListener("click", () => {
    if (+$("wall").value < 0.75) { $("wall").value = "0.9"; wallWas = 0.9; outputs(); }
    hitAt = performance.now();
    // Even when paused, let the flash play out.
    const until = hitAt + 2600, run = (n) => { frame(n); if (n < until && paused && visible) requestAnimationFrame(run); };
    if (paused) requestAnimationFrame(run); else kick();
  });
  $("pause").addEventListener("click", () => {
    paused = !paused;
    texts();
    kick();
  });

  function texts() {
    const D = page.L.demo;
    canvas.setAttribute("aria-label", D.title);
    $("pause").textContent = paused ? D.play : D.pause;
    $("pause").setAttribute("aria-pressed", String(paused));
    $("echoName").textContent = D.name;
    const tabs = Object.entries(D.tabs).map(([id, label]) => {
      const b = document.createElement("button");
      b.type = "button";
      b.setAttribute("role", "tab");
      b.textContent = label;
      b.setAttribute("aria-selected", String(id === mode));
      b.addEventListener("click", () => {
        mode = id;
        $("ghostControls").hidden = id !== "ghost";
        $("leashControls").hidden = id !== "leash";
        texts();
        kick();
      });
      return b;
    });
    $("demoTabs").replaceChildren(...tabs);
    outputs();
  }
  document.addEventListener("tr:lang", texts);
  texts();
  kick();
})();
