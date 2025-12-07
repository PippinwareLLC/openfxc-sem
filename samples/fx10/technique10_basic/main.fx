shared float4x4 SharedMVP;

RasterizerState NoCull
{
    CullMode = NONE;
};

DepthStencilState DepthOn
{
    DepthEnable = TRUE;
    DepthWriteMask = ALL;
};

float4 VSMain(float4 pos : POSITION) : SV_Position
{
    return mul(pos, SharedMVP);
}

float4 PSMain(float4 pos : SV_Position) : SV_Target
{
    return float4(1, 1, 1, 1);
}

technique10 BasicTech
{
    pass P0
    {
        SetVertexShader( CompileShader( vs_4_0, VSMain() ) );
        SetPixelShader( CompileShader( ps_4_0, PSMain() ) );
        SetRasterizerState( NoCull );
        SetDepthStencilState( DepthOn, 0 );
    }
}
