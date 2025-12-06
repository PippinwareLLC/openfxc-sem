sampler2D S;

float4 main(float3 uvw : TEXCOORD0) : SV_Target
{
    return tex2D(S, uvw);
}
