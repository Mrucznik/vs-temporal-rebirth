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

const vec3 TEAL = vec3(0.35, 0.92, 0.84);
const vec3 WHITE = vec3(0.88, 1.0, 0.97);

float sceneDistance(vec2 at) {
	vec4 p = inverseViewProjection * vec4(at * 2.0 - 1.0, texture(sceneDepth, at).r * 2.0 - 1.0, 1.0);
	return length(p.xyz / p.w - camera);
}

// The core of a wisp: a bright point in a teal halo, flickering, with sparks drifting through the halo.
void main() {
	vec2 screen = clipPoint.xy / clipPoint.w * 0.5 + 0.5;
	float dist = length(worldPoint - camera);
	float behind = sceneDistance(screen) - dist;
	if (behind < 0.0) discard;

	vec2 p = uv * 2.0 - 1.0;
	float r = length(p);
	float flicker = 0.85 + 0.15 * sin(time * 9.0 + seed) * sin(time * 3.1 + seed * 2.0);
	float core = exp(-r * r * 22.0);
	float halo = exp(-r * 4.0) * 0.55 * flicker;
	float n = valuenoise(vec3(p * 5.0 + vec2(0.0, -time * 0.8), time * 1.5 + seed));
	float sparks = smoothstep(0.72, 0.95, n) * exp(-r * 2.2);

	vec3 rgb = mix(TEAL, WHITE, core) * (0.7 + 0.6 * core) + TEAL * sparks * 0.5;
	float alpha = clamp(core * 1.3 + halo + sparks * 0.7, 0.0, 1.0) * smoothstep(1.0, 0.75, r);
	alpha *= (1.0 - smoothstep(80.0, 96.0, dist)) * smoothstep(0.0, 0.2, behind);
	if (alpha < 0.01) discard;
	outColor = vec4(rgb, alpha);
}
