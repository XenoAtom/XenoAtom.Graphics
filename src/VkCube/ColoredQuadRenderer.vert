#version 450

layout(location = 0) in vec3 Position;
layout(location = 1) in vec4 Color;

layout(location = 0) out vec4 fsin_Color;

layout(set = 0, binding = 0) uniform MVP {
    mat4 ModelViewProjection;
};

void main()
{
    gl_Position = ModelViewProjection * vec4(Position, 1.0);
    fsin_Color = Color;
}