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

// The echo is a temporal rift in the shape of a person. Like a rift it has almost no colour of its own: it bends the
// world behind it, turns it into dark rust, and its edge is ragged, torn by noise that never stays still. Only how a
// rift looks is borrowed here (survival/shaders/rift.fsh), not its code.

const vec3 SMOULDER = vec3(0.42, 0.07, 0.03);
const vec3 EMBER = vec3(0.95, 0.38, 0.12);

float sceneDistance(vec2 at) {
	vec4 p = inverseViewProjection * vec4(at * 2.0 - 1.0, texture(sceneDepth, at).r * 2.0 - 1.0, 1.0);
	return length(p.xyz / p.w - camera);
}

float box(vec2 p, vec2 center, vec2 hs) {
	vec2 d = abs(p - center) - hs;
	return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
}

vec2 turn(vec2 p, vec2 joint, float angle) {
	float c = cos(angle), s = sin(angle);
	vec2 q = p - joint;
	return joint + vec2(c * q.x + s * q.y, -s * q.x + c * q.y);
}

// The game's humanoid from the front, in blocks (1/16 = one pixel of its model): head 8x8, body 8x12, arms and legs
// 4x12, each a rigid box swinging on its joint. A gap keeps arms and legs apart from the body at a distance.
float figure(vec2 p, float t) {
	float gap = 0.04;
	float arm = 0.09 * sin(t * 1.1 + seed), leg = 0.06 * sin(t * 1.1 + seed + 1.6);
	float dHead = box(p, vec2(0.0, 1.75), vec2(0.25, 0.25) - gap * 0.5);
	float dBody = box(p, vec2(0.0, 1.125), vec2(0.25 - gap * 0.5, 0.375));
	float dArmL = box(turn(p, vec2(-0.375, 1.47), arm), vec2(-0.375 - gap, 1.125), vec2(0.125 - gap * 0.5, 0.375));
	float dArmR = box(turn(p, vec2(0.375, 1.47), -arm), vec2(0.375 + gap, 1.125), vec2(0.125 - gap * 0.5, 0.375));
	float dLegL = box(turn(p, vec2(-0.125, 0.75), leg), vec2(-0.125 - gap * 0.5, 0.375), vec2(0.125 - gap * 0.5, 0.375));
	float dLegR = box(turn(p, vec2(0.125, 0.75), -leg), vec2(0.125 + gap * 0.5, 0.375), vec2(0.125 - gap * 0.5, 0.375));
	return min(min(dHead, dBody), min(min(dArmL, dArmR), min(dLegL, dLegR)));
}

// Dark rust of the picture behind: brightness kept, hue pulled to an old brown, red pushed by `push`, then darkened.
vec3 rust(vec3 col, float push) {
	float lum = dot(col, vec3(0.30, 0.59, 0.11));
	vec3 r = lum * vec3(1.35, 0.72, 0.50) + vec3(0.03, 0.0, 0.0);
	r.r *= push;
	r.gb /= push;
	return r * 0.30;
}

void main() {
	vec2 screen = clipPoint.xy / clipPoint.w * 0.5 + 0.5;
	float dist = length(worldPoint - camera);
	float behind = sceneDistance(screen) - dist;
	if (behind < 0.0) discard;

	// A rift's clock runs steadily and now and then leaps forward, so its pattern jumps.
	float leap = step(0.94, fract(sin(floor(time * 3.0) * 12.9898 + seed) * 43758.5453)) * 5.0;
	float clock = time + leap;

	vec2 p = vec2((uv.x * 2.0 - 1.0) * 0.7, uv.y * height);
	vec2 cell = floor(p * 16.0) / 16.0;
	// The wave of the first echo, but row by row in model pixels, so the outline ripples and stays square.
	float slow = valuenoise(vec3(cell.y * 2.2, time * 0.5, seed));
	vec2 q = p;
	q.x += sin(cell.y * 5.0 + time * 1.7 + seed) * 0.03 + (slow - 0.5) * 0.10;
	q.y += sin(time * 0.8 + seed) * 0.03;
	float d = figure(q, time);

	// The edge is torn like a rift's: noise along it pulls it in and out, faster where it is thin.
	float tear = valuenoise(vec3(cell * vec2(5.0, 5.0), clock * 0.6 + seed)) - 0.5;
	float fine = valuenoise(vec3(p * 20.0, clock * 1.8 + seed)) - 0.5;
	float de = d + tear * 0.07 + fine * 0.02;

	float body = smoothstep(0.02, -0.03, de);
	// Outside, the rift's soft fade: strongest at the edge, falling off steeply.
	float halo = pow(clamp(1.0 - de / 0.14, 0.0, 1.0), 4.0) * step(0.0, de);
	float edge = exp(-abs(de) * 28.0);

	// Comes apart at the feet, in model pixels, smouldering where it breaks.
	float grain = valuenoise(vec3(cell * 3.0, seed + floor(clock * 4.0) * 0.25));
	float level = cell.y + (slow - 0.5) * 0.25 + (grain - 0.5) * 0.3;
	float whole = smoothstep(0.0, 0.45, level);
	float breaking = smoothstep(0.0, 0.2, level) * (1.0 - smoothstep(0.2, 0.45, level));

	// The world behind bends with noise of three sizes crawling upward, as through a rift.
	vec2 at = gl_FragCoord.xy;
	vec2 bend = (vec2(valuenoise(vec3(at / 300.0 - vec2(0.0, clock / 3.0), seed)), valuenoise(vec3(at / 300.0 - vec2(0.0, clock / 3.0), seed + 7.0))) - 0.5) / 25.0
		+ (vec2(valuenoise(vec3(at / 200.0 - vec2(0.0, clock / 4.0), seed + 2.0)), valuenoise(vec3(at / 200.0 - vec2(0.0, clock / 4.0), seed + 9.0))) - 0.5) / 37.0
		+ (vec2(valuenoise(vec3(at / 2.0 - vec2(0.0, clock), seed)), valuenoise(vec3(at / 2.0 - vec2(0.0, clock), seed + 4.0))) - 0.5) / 100.0;
	bend *= (body + halo) / max(1.0, dist * 0.2);
	vec3 col = texture(sceneColor, clamp(screen + bend, 0.001, 0.999)).rgb;

	// Red pushed harder towards the edge and in streaks along it, like a rift's spikes.
	float streak = valuenoise(vec3(cell * vec2(14.0, 3.0), clock * 0.3 + seed + 11.0));
	float push = 1.0 + (0.25 + 0.9 * edge) * streak;
	vec3 dark = rust(col, push);
	// Rising dark motes, the rift's particles.
	float motes = smoothstep(0.82, 0.97, valuenoise(vec3(cell * vec2(6.0, 3.0) - vec2(0.0, clock * 0.8), seed + 3.0)));
	dark = mix(dark, vec3(0.02, 0.05, 0.09), motes * body * 0.8);

	// A smoulder deep in the edge so the figure is still found at night; never a bright outline.
	float glow = edge * (0.35 + 0.65 * streak) * (0.8 + 0.2 * sin(time * 2.3 + seed));
	vec3 rgb = dark + SMOULDER * glow + EMBER * breaking * body * 0.35;

	float alpha = clamp(body * 0.92 + halo * 0.85 + glow * 0.4, 0.0, 1.0) * whole;
	alpha *= (1.0 - smoothstep(36.0, 48.0, dist)) * smoothstep(0.0, 0.2, behind);
	if (alpha < 0.01) discard;
	outColor = vec4(rgb, alpha);
}
