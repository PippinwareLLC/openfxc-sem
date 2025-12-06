sampler2D S;

float4 main(float2 uv : TEXCOORD0) : SV_Target
{
    return tex2D(S, uv);
}
