float4x4 WorldViewProj;

float4 main(float4 pos : POSITION) : POSITION
{
    return mul(pos, WorldViewProj);
}
