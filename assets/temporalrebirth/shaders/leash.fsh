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

const vec3 TEAL = vec3(0.35, 0.92, 0.84);

float sceneDistance() {
	vec4 p = inverseViewProjection * vec4(uv * 2.0 - 1.0, texture(sceneDepth, uv).r * 2.0 - 1.0, 1.0);
	return length(p.xyz / p.w - camera);
}

// A flash where the sphere was hit and a ring running out along it.
float glowOf(vec4 hit, vec3 p) {
	if (hit.w < 0.0) return 0.0;
	float r = distance(p, hit.xyz);
	float ring = (r - 0.15 - hit.w * 2.4) / (0.10 + hit.w * 0.22);
	return exp(-ring * ring) * exp(-hit.w * 1.5) + 0.8 * exp(-r * r * 2.5) * exp(-hit.w * 4.0);
}

void main() {
	vec3 dir = normalize(farPoint - camera);
	float b = dot(fromCenter, dir);
	float disc = b * b - outside;
	if (disc <= 0.0) discard;
	float s = sqrt(disc);

	// The nearest point of the sphere ahead, written so that nothing large cancels out.
	float t;
	if (outside <= 0.0) t = b > 0.0 ? -outside / (b + s) : s - b;
	else if (b < 0.0) t = outside / (s - b);
	else discard;

	float behind = sceneDistance() - t;
	if (behind < 0.0) discard;

	vec3 p = dir * t;
	vec3 q = fromCenter + p;
	float away = length(p - player);
	float glow = min(1.3, glowOf(hit0, p) + glowOf(hit1, p) + glowOf(hit2, p));

	// Most of the sphere is far away: hardly anything to see, so hardly any work.
	if (away > 8.0 && glow < 0.01) {
		float haze = 0.045 * valuenoise(q * 0.9 + vec3(0.0, time * 0.22, 0.0)) * (1.0 - smoothstep(12.0, 60.0, away)) * smoothstep(0.0, 0.35, behind);
		if (haze < 0.004) discard;
		outColor = vec4(texture(sceneColor, uv).rgb + TEAL * haze, haze);
		return;
	}

	float n = valuenoise(q * 0.9 + vec3(0.0, time * 0.22, 0.0));
	float torn = valuenoise(q.zxy * 2.7 + n * 1.5 - vec3(time * 0.35, 0.0, time * 0.2));
	// Pulled along by the larger noise, so its grid does not show.
	float fine = valuenoise(q.yzx * 5.0 + vec3(torn, n, -torn) * 3.5 + vec3(0.0, -time * 0.9, 0.0));

	// Visible only close to the player, with a torn edge like that of a rift.
	float d = away + (n - 0.5) * 2.2 + (torn - 0.5) * 2.0;
	float near = 1.0 - smoothstep(0.8, 4.6, d);
	float edge = near * (1.0 - near) * 4.0;
	// Right at the wall it fills the view; stay able to see.
	near *= mix(0.45, 1.0, smoothstep(0.3, 2.2, t));
	float faint = 0.045 * n * (1.0 - smoothstep(12.0, 60.0, away));

	vec2 bend = (vec2(n, torn) - 0.5) * 0.06 * near + (vec2(torn, fine) - 0.5) * 0.02 * (near + glow);
	vec3 col = texture(sceneColor, clamp(uv + bend, 0.001, 0.999)).rgb;
	float light = dot(col, vec3(0.3, 0.59, 0.11));
	vec3 rust = light * vec3(0.92, 0.50, 0.28) * (0.55 + 0.35 * fine) + vec3(0.05, 0.02, 0.01);

	vec3 rgb = mix(col, rust, near * 0.7);
	rgb += vec3(0.55, 0.27, 0.12) * edge * smoothstep(0.35, 0.8, torn) * 0.45;
	rgb += TEAL * (glow * (0.55 + 0.45 * fine) + glow * edge * 0.5 + faint);

	float alpha = clamp(near * (0.5 + 0.35 * torn) + edge * 0.15 + faint + glow * 0.9, 0.0, 1.0);
	// Soft where the ground cuts through it.
	alpha *= smoothstep(0.0, 0.35, behind);
	if (alpha < 0.004) discard;
	outColor = vec4(rgb, alpha);
}
