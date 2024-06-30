#version 450

layout(location = 0) in vec4 fsin_Color;

layout(location = 0) out vec4 fsout_Color;

void main()
{
    fsout_Color = fsin_Color;
}