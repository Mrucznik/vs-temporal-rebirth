#version 330 core

in vec2 uv;
in vec3 farPoint;

out vec4 outColor;

uniform sampler2D sceneColor;
uniform sampler2D sceneDepth;
uniform mat4 inverseViewProjection;
uniform vec3 camera;
uniform float time;

uniform vec3 fromCenter; // camera, seen from the middle of the sphere
uniform float outside;   // its distance from the middle squared, minus the radius squared
uniform vec3 player;     // seen from the camera
uniform vec4 hit0;       // seen from the camera; w = seconds ago, negative = none
uniform vec4 hit1;
uniform vec4 hit2;

#include noise3d.ash

// Rift, contour lines and buried gears in one: from afar faint contour lines of a moving map whose rings bend around
// small buried temporal gears; closer, the gears turn faster and the lines crowd; in the last blocks rift smoke and its
// hair-like strands take the wall. Bolts strike in random places and directions, more of them near the ghost.
// Contours after Radiant's Topographic by Paul Bakaus (MIT); SDF and warping after Inigo Quilez (MIT).

const float PI = 3.14159265;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float sceneDistance() {
	vec4 p = inverseViewProjection * vec4(uv * 2.0 - 1.0, texture(sceneDepth, uv).r * 2.0 - 1.0, 1.0);
	return length(p.xyz / p.w - camera);
}

float glowOf(vec4 hit, vec3 p) {
	if (hit.w < 0.0) return 0.0;
	float r = distance(p, hit.xyz);
	float ring = (r - 0.15 - hit.w * 2.4) / (0.10 + hit.w * 0.22);
	return exp(-ring * ring) * exp(-hit.w * 1.5) + 0.8 * exp(-r * r * 2.5) * exp(-hit.w * 4.0);
}

float lines(float x, float width) {
	float f = abs(fract(x) - 0.5);
	return smoothstep(width, 0.0, 0.5 - f);
}

// Signed distance to a whole temporal gear of radius 1 turned by `turn`: rim with `teeth` teeth, a hub hole and
// `holes` round windows (the SDF building blocks follow Inigo Quilez's 2D distance functions, MIT).
float gearSdf(vec2 p, float turn, float teeth, float holes) {
	// A plain toothed disc: no hub hole and no windows.
	float r = length(p);
	float a = atan(p.y, p.x) + turn;
	float tooth = clamp(cos(a * teeth) * 3.0, -1.0, 1.0) * 0.5 + 0.5;
	return r - (0.78 + 0.16 * tooth);
}

// Outline of a temporal gear as in the first barrier: a toothed rim, a hub ring and three spokes. Distance to the lines.
float gearOutline(vec2 p, float turn) {
	float r = length(p);
	float a = atan(p.y, p.x) + turn;
	float tooth = clamp(cos(a * 8.0) * 2.5, -1.0, 1.0) * 0.5 + 0.5;
	float rim = abs(r - (0.72 + 0.18 * tooth));
	float hub = abs(r - 0.22);
	float spoke = r > 0.22 && r < 0.68 ? abs(sin(a * 3.0)) * r : 1.0;
	return min(min(rim, hub), spoke);
}

// A blocky temporal gear like a pixel-art item: the gear's own square grid of 10 x 10 pixels, square teeth, a rim, a hub
// and three spokes. x: 1 on a lit pixel of the outline, y: 1 inside the gear.
vec2 gearPixels(vec2 p, float turn) {
	vec2 q = (floor(p * 5.0) + 0.5) / 5.0;
	float r = length(q);
	float a = atan(q.y, q.x) + turn;
	float tooth = step(0.0, cos(a * 8.0));
	float outer = 0.62 + 0.26 * tooth;
	float rim = step(r, outer) * step(0.5, r);
	float hub = step(r, 0.2);
	float spoke = step(abs(sin(a * 3.0)) * r, 0.09) * step(r, 0.5);
	return vec2(max(max(rim, hub), spoke), step(r, outer));
}

// A point on the dome as flat wall coordinates in blocks: around (arc length, wrapping cleanly) and up.
vec2 wallCoords(vec3 q, float radius, float cell) {
	float around = max(4.0, floor(2.0 * PI * radius / cell));
	return vec2(atan(q.z, q.x) / (2.0 * PI) * around, q.y / cell);
}

void main() {
	vec3 dir = normalize(farPoint - camera);
	float b = dot(fromCenter, dir);
	// The look is drawn half a block inside the real barrier, so it shows before the ghost touches it.
	const float insideBy = 0.5;
	float realRadius = sqrt(max(dot(fromCenter, fromCenter) - outside, 0.0));
	float shown = max(realRadius - insideBy, 0.5);
	float outsideShown = dot(fromCenter, fromCenter) - shown * shown;
	float disc = b * b - outsideShown;
	if (disc <= 0.0) discard;
	float s = sqrt(disc);

	// The nearest point of the sphere ahead, written so that nothing large cancels out.
	float t;
	if (outsideShown <= 0.0) t = b > 0.0 ? -outsideShown / (b + s) : s - b;
	else if (b < 0.0) t = outsideShown / (s - b);
	else discard;

	float behind = sceneDistance() - t;
	if (behind < 0.0) discard;

	vec3 p = dir * t;
	vec3 q = fromCenter + p;
	float away = length(p - player);
	float glow = min(1.3, glowOf(hit0, p) + glowOf(hit1, p) + glowOf(hit2, p));

	// How far the ghost is from the wall: beyond 50 blocks nothing shows.
	float centre = length(fromCenter + player);
	float radius = sqrt(max(dot(fromCenter, fromCenter) - outside, 0.0));
	float wall = abs(radius - centre);
	float appear = 1.0 - smoothstep(40.0, 50.0, wall);
	if (appear <= 0.0 && glow < 0.01) discard;
	// 1 touching the wall, 0 from 20 blocks in; and the last few blocks.
	float approach = 1.0 - smoothstep(1.0, 20.0, wall);
	float touching = 1.0 - smoothstep(0.5, 4.0, wall);
	// How close this point of the wall is to the ghost.
	float near = 1.0 - smoothstep(1.5, 9.0 + 8.0 * approach, away);
	float far = 1.0 - smoothstep(40.0, 80.0, t);
	float seam = 0.0; // no glow along the ground
	vec3 col = texture(sceneColor, uv).rgb;

	float leap = step(0.93, fract(sin(floor(time * 2.0) * 12.9898) * 43758.5453)) * 0.8;
	float clock = time + leap;
	vec2 g = wallCoords(q, radius, 1.0);
	float closeness = 1.0 - smoothstep(0.4, 1.3, wall); // practically only within a block of the wall
	float show = appear * near * (0.2 + 0.8 * approach);
	#define FBM(P) (valuenoise(P) * 0.5 + valuenoise((P) * 2.03 + 3.1) * 0.25 + valuenoise((P) * 4.01 + 7.7) * 0.125 + valuenoise((P) * 8.05 + 1.3) * 0.0625)

	// The map: slow noise, with small buried gears (0.35 blocks apart) pulling the height down, so rings bend round them.
	vec3 pt = vec3(g * 0.22, clock * 0.04);
	float h = FBM(pt + (vec3(FBM(pt + 5.2), FBM(pt + 1.3), 0.0) - 0.47) * 0.8);
	float speed = mix(0.08, 1.6, approach * approach);
	// The gears are drawn through a crawling distortion, so their outlines bend, smear and twitch like seen through a rift.
	vec2 gw = g + (vec2(valuenoise(vec3(g * 1.7, time * 0.35)), valuenoise(vec3(g * 1.7 + 9.0, time * 0.35))) - 0.5) * 0.22
		+ (vec2(valuenoise(vec3(g * 6.0, time * 1.1)), valuenoise(vec3(g * 6.0 + 4.0, time * 1.1))) - 0.5) * 0.05;
	vec2 gc = floor(gw / 0.35), gf = fract(gw / 0.35) - 0.5;
	float gearField = 9.0, gearOn = 0.0, gearIn = 0.0, gearSpin = 0.0;
	vec2 gearCentre = vec2(1e5);
	for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) {
		vec2 c = gc + vec2(i, j);
		if (hash(c + 23.0) > 0.35) continue;
		vec2 o = vec2(i, j) + (vec2(hash(c), hash(c + 11.0)) - 0.5) * 0.4 - gf;
		float spin = (hash(c + 5.0) < 0.5 ? 1.0 : -1.0) * time * speed;
		spin += (valuenoise(vec3(c, time * 0.8)) - 0.5) * 0.9; // jerks back and forth
		// Each gear its own size, from a tenth to a half of the full size.
		float size = mix(0.1, 0.5, hash(c + 31.0));
		float d = gearSdf(-o * 2.0 / size, spin, 10.0, 4.0) * 0.12 * size;
		if (d < gearField) { gearField = d; gearSpin = spin; gearCentre = g - o * 0.35; }
		vec2 px = gearPixels(-o * 2.0 / size, spin);
		gearOn = max(gearOn, px.x); gearIn = max(gearIn, px.y);
	}
	float field = h - 0.05 * smoothstep(0.2, 0.0, gearField);
	float density = 9.0 + 14.0 * approach;
	float level = field * density;
	float width = fwidth(level) * 1.2 + 0.02;
	float iso = smoothstep(width, 0.0, min(fract(level), 1.0 - fract(level)));
	float major = smoothstep(width * 1.5, 0.0, min(fract(level / 5.0), 1.0 - fract(level / 5.0)) * 5.0);
	float gearLine = gearOn * show;
	float lines_ = (iso * 0.55 + major * 0.45) * (0.15 * appear * far + show);
	// The rings that crowd round each gear are dimmer, so the gears stay the thing to see.
	lines_ *= 1.0 - 0.45 * smoothstep(0.18, 0.0, gearField);

	// Domain-warped noise, so the smoke never looks like cells or a grid.
	float scale = 0.5;
	// The smoke is dragged round the nearest gear as it turns, and gathers on the gears.
	float pull = smoothstep(0.25, 0.0, gearField);
	vec2 rel = g - gearCentre;
	float swirl = gearSpin * 0.35 * pull;
	vec2 gs = gearCentre + vec2(cos(swirl) * rel.x - sin(swirl) * rel.y, sin(swirl) * rel.x + cos(swirl) * rel.y);
	gs = mix(g, gs, step(gearCentre.x, 1e4));
	vec3 p3 = vec3(gs * scale, clock * 0.07);
	vec2 warp = vec2(valuenoise(p3 + 3.1), valuenoise(p3 + 7.7)) - 0.5;
	vec3 w3 = vec3(gs * scale + warp * 1.6, clock * 0.1);
	float smoke = valuenoise(w3) * 0.55 + valuenoise(w3 * 2.3 + 5.0) * 0.3 + valuenoise(w3 * 5.1 + 9.0) * 0.15;
	// Which way the smoke thins out: the strands run along it, out of the smoke, like the hairs around a rift.
	vec2 e = vec2(0.15, 0.0);
	vec2 grad = vec2(valuenoise(w3 + vec3(e.xy * scale, 0.0)) - valuenoise(w3 - vec3(e.xy * scale, 0.0)),
		valuenoise(w3 + vec3(e.yx * scale, 0.0)) - valuenoise(w3 - vec3(e.yx * scale, 0.0)));
	vec2 along = normalize(grad + 1e-5), across = vec2(-along.y, along.x);
	// How much of the wall the rift takes: nothing from afar, most of it right at the wall.
	float take = appear * near * (0.1 + 0.9 * closeness);
	float smokeLevel = smoke * 0.8 + take * 0.62 - 0.6 + 0.18 * pull * take;
	float fill = smoothstep(0.0, 0.10, smokeLevel);
	float rim = smoothstep(-0.25, 0.0, smokeLevel) * (1.0 - fill);
	// Dark strands reaching out from the smoke, like the hairs around a rift.
	float hairNoise = valuenoise(vec3(dot(gs, across) * 2.5, dot(gs, along) * 0.25, clock * 0.2));
	float hair = smoothstep(0.045, 0.0, abs(hairNoise - 0.5)) * rim * 1.3;
	// The world behind bends and rusts near the smoke (the rift's sepia, darkened).
	vec2 bend = warp * 0.05 * (rim + fill) + vec2(valuenoise(vec3(g * 3.0, clock)) - 0.5) * 0.01 * rim;
	vec3 seen = texture(sceneColor, clamp(uv + bend, 0.001, 0.999)).rgb;
	float lum = dot(seen, vec3(0.30, 0.59, 0.11));
	vec3 rust = vec3(lum * 1.05, lum * 0.62, lum * 0.42) * 0.55;
	// Inside, the smoke still churns: dark maroon swirls over near-black, never a flat fill.
	float churn = valuenoise(w3 * 3.0 + vec3(0.0, 0.0, clock * 0.4));
	vec3 core = mix(vec3(0.16, 0.035, 0.025), vec3(0.015, 0.004, 0.004), smoothstep(0.0, 0.35, smokeLevel) * (0.6 + 0.4 * churn));
	vec3 rgb = mix(seen, rust, rim * 0.8);
	rgb = mix(rgb, core, fill * 0.5);
	rgb = mix(rgb, vec3(0.03, 0.01, 0.01), hair);
	// From about 4 blocks the barrier grows less see-through as the ghost closes in: a dark rift veil over the world,
	// with the lines, gears and bolts still drawn on top of it.
	float solid = (1.0 - smoothstep(0.5, 4.0, wall)) * appear;
	rgb = mix(rgb, vec3(0.085, 0.022, 0.016), solid * 0.65);

	// Bolts: in random places and directions over the wall near the ghost, more of them the closer it is. Each is a
	// jagged path between two random points that tapers towards its end, with a soft glow and a fainter twin a little
	// apart, which gives it some depth.
	float bolt = 0.0, boltGlow = 0.0;
	vec3 qp = fromCenter + player;
	vec2 pg = wallCoords(qp / max(length(qp), 0.001) * radius, radius, 1.0);
	int count = int(1.0 + 7.0 * approach * approach);
	for (int k = 0; k < 8; k++) {
		if (k >= count) break;
		float rate = 0.9 + 0.31 * float(k);
		float slot = floor(clock * rate + float(k) * 0.37);
		float life = fract(clock * rate + float(k) * 0.37);
		if (hash(vec2(slot, float(k) + 3.0)) < 0.5 - 0.3 * approach) continue;
		vec2 a = pg + (vec2(hash(vec2(slot, float(k) + 9.0)), hash(vec2(slot, float(k) + 4.0))) - 0.5) * vec2(16.0, 9.0);
		float ang = hash(vec2(slot, float(k) + 7.0)) * 6.2831;
		vec2 b = a + vec2(cos(ang), sin(ang)) * (2.0 + 4.0 * hash(vec2(slot, float(k) + 2.0)));
		vec2 ab = b - a; float len = length(ab); vec2 dir = ab / len, nrm = vec2(-dir.y, dir.x);
		float s = dot(g - a, dir) / len;
		float span = smoothstep(-0.02, 0.06, s) * (1.0 - smoothstep(0.8, 1.0, s));
		float off = dot(g - a, nrm);
		float jag = ((valuenoise(vec3(s * len * 2.2, slot, float(k))) - 0.5) * 0.9 + (valuenoise(vec3(s * len * 8.0, slot, float(k) + 2.0)) - 0.5) * 0.25) * sin(s * 3.14159);
		float dx = abs(off - jag);
		float twin = abs(off - jag * 0.8 - 0.18);
		float taper = mix(0.06, 0.004, s);
		float fade = (1.0 - smoothstep(0.3, 1.0, life)) * (0.7 + 0.3 * sin(life * 60.0 + float(k)));
		bolt = max(bolt, (smoothstep(taper, 0.0, dx) + smoothstep(taper * 0.6, 0.0, twin) * 0.35) * span * fade);
		boltGlow = max(boltGlow, exp(-dx * 4.0) * span * fade * 0.6);
	}
	bolt *= appear; boltGlow *= appear;

	vec3 line = mix(vec3(0.55, 0.16, 0.06), vec3(1.0, 0.55, 0.22), major);
	// Contour lines in the rift smoke's dark maroon instead of glowing orange; the major ones a touch warmer.
	rgb = mix(rgb, mix(vec3(0.12, 0.025, 0.018), vec3(0.24, 0.06, 0.03), major), lines_ * 0.8 * (1.0 - fill));
	// Inside each gear the picture is a little darker.
	float inside = gearIn * show;
	rgb *= 1.0 - 0.3 * inside * (1.0 - fill);
	// Gears in dark rusty brown, laid over the picture rather than lit up.
	rgb = mix(rgb, vec3(0.26, 0.10, 0.04), gearLine * 0.85 * (1.0 - fill));
	// Bolts in the orange the gear lines once had.
	rgb = mix(rgb, vec3(1.0, 0.55, 0.22), bolt);
	rgb += vec3(0.75, 0.32, 0.12) * boltGlow;
	rgb += vec3(0.55, 0.12, 0.05) * (seam * 0.8 + glow * 0.8);
	float alpha = solid * 0.65 + fill * 0.45 + rim * 0.5 + hair + lines_ * 0.8 + gearLine * 0.7 + inside * 0.2 + bolt + boltGlow * 0.5 + seam * 0.8 + glow * 0.9;
	float alpha_ = clamp(alpha, 0.0, 1.0);
	alpha_ *= mix(0.85, 1.0, smoothstep(0.3, 2.0, t));
	alpha_ *= smoothstep(0.0, 0.06, behind);
	if (alpha_ < 0.004) discard;
	outColor = vec4(rgb, alpha_);
}
