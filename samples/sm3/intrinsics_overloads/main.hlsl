float4x4 WorldViewProj;
float3 NormalBias;

float4 main(float4 pos : POSITION, float3 normal : NORMAL) : POSITION
{
    float3 n = normalize(normal + NormalBias);
    float dotN = dot(n.xy, n.xy);
    float4 transformed = mul(pos, WorldViewProj);
    float3 powN = pow(n, float3(2, 2, 2));
    float3 saturated = saturate(powN);
    return float4(transformed.xyz, dotN + saturated.x);
}
