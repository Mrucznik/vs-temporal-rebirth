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

#include noise3d.ash

// A wisp is a small temporal rift: a dark knot that bends and rusts the world behind it, with a ragged edge that keeps
// tearing and a smoulder at its heart so it can be found at night. Only how a rift looks is borrowed
// (survival/shaders/rift.fsh), not its code.

const vec3 SMOULDER = vec3(0.55, 0.10, 0.04);
const vec3 EMBER = vec3(1.0, 0.45, 0.15);

float sceneDistance(vec2 at) {
	vec4 p = inverseViewProjection * vec4(at * 2.0 - 1.0, texture(sceneDepth, at).r * 2.0 - 1.0, 1.0);
	return length(p.xyz / p.w - camera);
}

void main() {
	vec2 screen = clipPoint.xy / clipPoint.w * 0.5 + 0.5;
	float dist = length(worldPoint - camera);
	float behind = sceneDistance(screen) - dist;
	if (behind < 0.0) discard;

	float leap = step(0.94, fract(sin(floor(time * 3.0) * 12.9898 + seed) * 43758.5453)) * 5.0;
	float clock = time + leap;

	vec2 p = uv * 2.0 - 1.0;
	float r = length(p);
	// The edge is torn by noise around the circle, like the spikes of a rift.
	float angle = atan(p.y, p.x);
	float spikes = valuenoise(vec3(cos(angle) * 3.0, sin(angle) * 3.0, clock * 0.7 + seed)) * 0.6
		+ valuenoise(vec3(cos(angle) * 9.0, sin(angle) * 9.0, clock * 1.6 + seed)) * 0.4;
	float reach = 0.42 + 0.38 * spikes;
	float f = r / reach;
	float shape = pow(clamp(1.15 - f, 0.0, 1.0), 1.5);
	if (shape <= 0.0) discard;

	vec2 at = gl_FragCoord.xy;
	vec2 bend = (vec2(valuenoise(vec3(at / 120.0 - vec2(0.0, clock / 2.0), seed)), valuenoise(vec3(at / 120.0 - vec2(0.0, clock / 2.0), seed + 7.0))) - 0.5) / 18.0
		+ (vec2(valuenoise(vec3(at / 2.0 - vec2(0.0, clock), seed)), valuenoise(vec3(at / 2.0 - vec2(0.0, clock), seed + 4.0))) - 0.5) / 100.0;
	bend *= shape / max(1.0, dist * 0.2);
	vec3 col = texture(sceneColor, clamp(screen + bend, 0.001, 0.999)).rgb;

	float lum = dot(col, vec3(0.30, 0.59, 0.11));
	float push = 1.0 + 1.2 * smoothstep(0.4, 1.0, f) * spikes;
	vec3 dark = lum * vec3(1.35 * push, 0.72 / push, 0.50 / push) * 0.28;

	float flicker = 0.8 + 0.2 * sin(time * 7.0 + seed) * sin(time * 2.3 + seed * 2.0);
	float heart = exp(-r * r * 30.0) * flicker;
	float rim = exp(-abs(f - 0.85) * 9.0) * spikes;
	vec3 rgb = dark + SMOULDER * (heart * 1.2 + rim * 0.5) + EMBER * heart * heart * 0.5;

	float alpha = clamp(shape * 1.3, 0.0, 1.0);
	alpha *= (1.0 - smoothstep(80.0, 96.0, dist)) * smoothstep(0.0, 0.2, behind);
	if (alpha < 0.01) discard;
	outColor = vec4(rgb, alpha);
}
