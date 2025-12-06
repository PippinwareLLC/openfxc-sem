cbuffer PerFrame : register(b0)
{
    float4x4 World;
    float4 Color;
};

struct VSIn
{
    float4 pos : POSITION;
};

float4 main(VSIn input) : SV_Position
{
    float4 p = mul(input.pos, World);
    return p * Color;
}
