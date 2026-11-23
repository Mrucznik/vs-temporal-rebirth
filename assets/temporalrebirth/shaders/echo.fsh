#version 330 core

in vec2 uv;
in vec3 worldPoint;
in vec4 clipPoint;

out vec4 outColor;

uniform sampler2D sceneColor;
uniform sampler2D sceneDepth;
uniform mat4 inverseViewProjection;
uniform vec3 camera;
uniform float time;
uniform float seed;
uniform float height;

#include noise3d.ash

// Looks like the game's temporal rift (VSSurvivalMod RiftRenderer with survival/shaders/rift.fsh; only how it looks is
// taken, not its code): a rift shows no colour of its own. It bends the picture behind it with noise of several sizes
// that crawls upward, turns that picture into a dark rust (sepia twice, red pushed up by a noise that changes along the
// edge, everything divided by four), lets its ragged edge fade, jumps now and then (its noise clock leaps forward at
// random, 1.2 % of frames) and gives off small particles of 10, 35, 58 (a dark navy). Its outline breathes slowly.
const vec3 RIFT_MOTE = vec3(0.039, 0.137, 0.227);
// Seen beside a real rift in the game: a near-black maroon smear with thin dark strands, no glow. The outline here is
// a dim crimson so the figure still reads; embers only where it comes apart at the feet.
const vec3 CRIMSON = vec3(0.62, 0.13, 0.08);
const vec3 EMBER = vec3(1.0, 0.56, 0.26);

// The rift's colour change, own formulation: brightness of the picture behind, as rust; push: how far red is pushed.
vec3 rust(vec3 col, float push) {
	float lum = dot(col, vec3(0.30, 0.59, 0.11));
	vec3 r = lum * vec3(1.25, 0.55, 0.38);
	return vec3(r.r * push, r.g / push, r.b / push);
}

float sceneDistance(vec2 at) {
	vec4 p = inverseViewProjection * vec4(at * 2.0 - 1.0, texture(sceneDepth, at).r * 2.0 - 1.0, 1.0);
	return length(p.xyz / p.w - camera);
}

// A box that can turn around a joint: p is moved into the frame of the part first.
float box(vec2 p, vec2 center, vec2 hs) {
	vec2 d = abs(p - center) - hs;
	return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
}

vec2 turn(vec2 p, vec2 joint, float angle) {
	float c = cos(angle), s = sin(angle);
	vec2 q = p - joint;
	return joint + vec2(c * q.x + s * q.y, -s * q.x + c * q.y);
}

// The game's humanoid seen from the front, in blocks (1/16 = one pixel of its model): head 8x8, body 8x12, arms and
// legs 4x12. Each part is a rigid box that sways on its own joint, so the outline stays square while the figure moves.
// A gap of half a pixel keeps arms and legs apart from the body even far away. x: outline, y: nearest part edge.
vec2 figure(vec2 p, float t) {
	float gap = 0.035;
	float arm = 0.07 * sin(t * 1.1 + seed), leg = 0.05 * sin(t * 1.1 + seed + 1.6);
	vec2 head = vec2(0.015 * sin(t * 0.9 + seed), 1.75 + 0.02 * sin(t * 1.3 + seed));
	float dHead = box(p, head, vec2(0.25, 0.25) - gap * 0.5);
	float dBody = box(p, vec2(0.0, 1.125), vec2(0.25, 0.375) - vec2(gap * 0.5, 0.0));
	float dArmL = box(turn(p, vec2(-0.375, 1.47), arm), vec2(-0.375 - gap, 1.125), vec2(0.125 - gap * 0.5, 0.375));
	float dArmR = box(turn(p, vec2(0.375, 1.47), arm), vec2(0.375 + gap, 1.125), vec2(0.125 - gap * 0.5, 0.375));
	float dLegL = box(turn(p, vec2(-0.125, 0.75), leg), vec2(-0.125 - gap * 0.5, 0.375), vec2(0.125 - gap * 0.5, 0.375));
	float dLegR = box(turn(p, vec2(0.125, 0.75), -leg), vec2(0.125 + gap * 0.5, 0.375), vec2(0.125 - gap * 0.5, 0.375));
	float d = min(min(dHead, dBody), min(min(dArmL, dArmR), min(dLegL, dLegR)));
	float seam = min(min(abs(dHead), abs(dBody)), min(min(abs(dArmL), abs(dArmR)), min(abs(dLegL), abs(dLegR))));
	return vec2(d, seam);
}

void main() {
	vec2 screen = clipPoint.xy / clipPoint.w * 0.5 + 0.5;
	float dist = length(worldPoint - camera);
	float behind = sceneDistance(screen) - dist;
	if (behind < 0.0) discard;

	vec2 p = vec2((uv.x * 2.0 - 1.0) * 0.7, uv.y * height);
	float slow = valuenoise(vec3(p * 2.2, time * 0.5 + seed));
	float quick = valuenoise(vec3(p * 7.0 + seed, time * 1.6));
	// The motion of the first, smooth echo, applied to the whole figure at once: it sways and drifts without bending.
	float sway = sin(time * 1.7 + seed) * 0.03 + (valuenoise(vec3(0.0, time * 0.5, seed)) - 0.5) * 0.10;
	vec2 f = figure(p - vec2(sway, 0.0), time);
	float d = f.x;

	float body = smoothstep(0.012, -0.012, d);
	float rim = exp(-abs(d) * 30.0);
	float seam = body * exp(-f.y * 45.0);

	// The rift's clock: steady, with a leap now and then (a hash of the second decides), so the pattern jumps.
	float second = floor(time * 3.0);
	float leap = step(0.93, fract(sin(second * 12.9898 + seed) * 43758.5453)) * 7.0;
	float clock = time + leap;
	// Dark motes like the rift's particles, drifting up through the figure.
	float motes = smoothstep(0.80, 0.96, valuenoise(vec3(floor(p * 16.0) / 16.0 * vec2(6.0, 3.0) - vec2(0.0, clock * 0.8), seed + 3.0)));

	// Comes apart at the feet only, in square steps, the broken edge glowing like embers.
	vec2 cell = floor(p * 16.0) / 16.0;
	float grain = valuenoise(vec3(cell * 3.0, seed + floor(clock * 4.0) * 0.25));
	float level = cell.y + (slow - 0.5) * 0.25 + (grain - 0.5) * 0.25;
	float whole = smoothstep(0.0, 0.4, level);
	float edge = smoothstep(0.0, 0.2, level) * (1.0 - smoothstep(0.2, 0.4, level));

	// The world behind bends with noise of three sizes that crawls upward, as through a rift.
	vec2 at = screen * vec2(16.0, 9.0);
	vec2 bend = vec2(valuenoise(vec3(at * 0.35 - vec2(0.0, clock * 0.30), seed)), valuenoise(vec3(at * 0.5 - vec2(0.0, clock * 0.25), seed + 7.0))) - 0.5;
	bend = bend * 0.030 + (vec2(quick, slow) - 0.5) * 0.010 + (vec2(valuenoise(vec3(at * 6.0, clock * 3.0)), valuenoise(vec3(at * 6.0 + 5.0, clock * 3.0))) - 0.5) * 0.004;
	bend /= max(1.0, dist * 0.25);
	vec3 col = texture(sceneColor, clamp(screen + bend, 0.001, 0.999)).rgb;
	// Red pushed by a noise that changes along the outline and over time, like the streaks of a rift.
	float push = 1.0 + 0.35 * valuenoise(vec3(f.y * 8.0 + p.y * 3.0, p.x * 3.0, clock * 0.4 + seed));
	vec3 inside = rust(col, push) * 0.32 + RIFT_MOTE * motes * 0.9;
	vec3 lines = mix(CRIMSON, EMBER, 0.25 * valuenoise(vec3(p * 5.0, clock * 1.5)));
	// Thin dark strands reaching out of the outline, like those around a rift.
	float strand = valuenoise(vec3(p * vec2(3.0, 14.0), clock * 0.3 + seed));
	float strands = step(0.0, d) * smoothstep(0.035, 0.0, abs(strand - 0.5)) * exp(-d * 9.0);
	vec3 rgb = mix(col, inside, body) + lines * (rim * 0.75 + seam * 0.5) + EMBER * edge * body * 0.5;
	rgb = mix(rgb, RIFT_MOTE * 0.5, strands * 0.7 * whole);

	float alpha = clamp(body * 0.88 + rim * 0.8 + seam * 0.3 + strands * 0.6, 0.0, 1.0) * whole;
	alpha *= (1.0 - smoothstep(36.0, 48.0, dist)) * smoothstep(0.0, 0.2, behind);
	if (alpha < 0.01) discard;
	outColor = vec4(rgb, alpha);
}
