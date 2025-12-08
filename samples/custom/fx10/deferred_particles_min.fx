DepthStencilState DisableDepthStencil
{
    DepthEnable = FALSE;
};

BlendState AdditiveBlend
{
    AlphaToCoverageEnable = FALSE;
    BlendEnable[0] = TRUE;
    SrcBlend = ONE;
    DestBlend = ONE;
    BlendOp = ADD;
    RenderTargetWriteMask[0] = 0x0F;
};

RasterizerState BackCull
{
    CullMode = BACK;
};

SamplerState LinearSampler
{
    Filter = MIN_MAG_MIP_LINEAR;
    AddressU = Clamp;
    AddressV = Clamp;
};

float4 VSMain(float4 pos : POSITION) : SV_Position
{
    return pos;
}

float4 PSMain() : SV_Target
{
    return float4(1, 1, 1, 1);
}

technique10 DeferredParticles
{
    pass P0
    {
        SetVertexShader( CompileShader( vs_4_0, VSMain() ) );
        SetGeometryShader( NULL );
        SetPixelShader( CompileShader( ps_4_0, PSMain() ) );
        SetBlendState( AdditiveBlend, float4(0, 0, 0, 0), 0xFFFFFFFF );
        SetDepthStencilState( DisableDepthStencil, 0 );
        SetRasterizerState( BackCull );
    }
}
