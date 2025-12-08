float4 ParamArrayDemo(
    in float2 uv : TEXCOORD0,
    uniform float2 offsets[3],
    out float4 colorOut
) : SV_Target
{
    float2 acc = uv;
    for (int i = 0; i < 3; i++)
    {
        acc += offsets[i];
    }
    colorOut = float4(acc, 0, 1);
    return colorOut;
}