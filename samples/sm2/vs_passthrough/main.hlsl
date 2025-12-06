float4x4 WorldViewProj;
sampler2D DiffuseSampler;

struct VSInput
{
    float4 pos : POSITION0;
};

float4 main(float4 pos : POSITION0) : POSITION
{
    return mul(pos, WorldViewProj);
}
