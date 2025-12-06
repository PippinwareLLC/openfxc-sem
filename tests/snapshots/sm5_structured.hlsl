cbuffer PerFrame : register(b0)
{
    float4x4 World;
};

StructuredBuffer<float4> Input : register(t0);
RWStructuredBuffer<float4> Output : register(u0);

float4 main(uint idx : SV_DispatchThreadID) : SV_Target
{
    return Input[idx];
}
