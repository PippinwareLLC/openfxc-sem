float4 VSFunc(float4 pos : POSITION) : POSITION
{
    return pos;
}

float4 PSFunc(float4 pos : POSITION) : COLOR0
{
    return pos;
}

technique ChooseMe
{
    pass P0
    {
        VertexShader = compile vs_2_0 VSFunc();
        PixelShader = compile ps_2_0 PSFunc();
    }
}
