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

const vec3 TEAL = vec3(0.35, 0.92, 0.84);

float sceneDistance(vec2 at) {
	vec4 p = inverseViewProjection * vec4(at * 2.0 - 1.0, texture(sceneDepth, at).r * 2.0 - 1.0, 1.0);
	return length(p.xyz / p.w - camera);
}

float capsule(vec2 p, vec2 a, vec2 b, float r) {
	vec2 pa = p - a, ba = b - a;
	return length(pa - ba * clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0)) - r;
}

float join(float a, float b, float k) {
	float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
	return mix(b, a, h) - k * h * (1.0 - h);
}

// Distance to the outline of a standing figure, in blocks; negative inside.
float figure(vec2 p) {
	p.x = abs(p.x);
	float d = length((p - vec2(0.0, 1.70)) * vec2(1.1, 1.0)) - 0.19;
	d = join(d, capsule(p, vec2(0.0, 1.36), vec2(0.0, 0.98), 0.20), 0.14);
	d = join(d, capsule(p, vec2(0.17, 1.40), vec2(0.36, 0.88), 0.075), 0.10);
	d = join(d, capsule(p, vec2(0.09, 0.95), vec2(0.15, 0.10), 0.095), 0.10);
	return d;
}

void main() {
	vec2 screen = clipPoint.xy / clipPoint.w * 0.5 + 0.5;
	float dist = length(worldPoint - camera);
	float behind = sceneDistance(screen) - dist;
	if (behind < 0.0) discard;

	vec2 p = vec2((uv.x * 2.0 - 1.0) * 0.7, uv.y * height);
	float slow = valuenoise(vec3(p * 2.2, time * 0.5 + seed));
	float quick = valuenoise(vec3(p * 7.0 + seed, time * 1.6));
	p.x += sin(p.y * 5.0 + time * 1.7 + seed) * 0.025 + (slow - 0.5) * 0.10;
	float d = figure(p) + (quick - 0.5) * 0.04;

	float body = smoothstep(0.02, -0.04, d);
	float rim = exp(-abs(d) * 26.0);
	// Comes apart downwards.
	float whole = smoothstep(0.15, 1.15, p.y + (slow - 0.5) * 0.7 + (quick - 0.5) * 0.35);

	vec2 bend = (vec2(slow, quick) - 0.5) * 0.05 / max(1.0, dist * 0.25);
	vec3 col = texture(sceneColor, clamp(screen + bend, 0.001, 0.999)).rgb;
	vec3 rgb = col * 0.82 + TEAL * (0.05 + rim * 0.85);

	float alpha = clamp(body * 0.88 + rim * 0.75, 0.0, 1.0) * whole;
	alpha *= (1.0 - smoothstep(36.0, 48.0, dist)) * smoothstep(0.0, 0.2, behind);
	if (alpha < 0.01) discard;
	outColor = vec4(rgb, alpha);
}
