#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 uvIn;

uniform mat4 viewProjection;
uniform vec3 feet;
uniform vec3 side;
uniform float height;

out vec2 uv;
out vec3 worldPoint;
out vec4 clipPoint;

void main() {
	uv = uvIn;
	worldPoint = feet + side * (uvIn.x * 2.0 - 1.0) + vec3(0.0, height * uvIn.y, 0.0);
	clipPoint = viewProjection * vec4(worldPoint, 1.0);
	gl_Position = clipPoint;
}
