#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 uvIn;

uniform mat4 inverseViewProjection;

out vec2 uv;
out vec3 farPoint;

// The whole screen; the sphere is found per pixel, so it stays round at any size.
void main() {
	uv = uvIn;
	vec2 screen = uvIn * 2.0 - 1.0;
	vec4 far = inverseViewProjection * vec4(screen, 1.0, 1.0);
	farPoint = far.xyz / far.w;
	gl_Position = vec4(screen, 0.0, 1.0);
}
