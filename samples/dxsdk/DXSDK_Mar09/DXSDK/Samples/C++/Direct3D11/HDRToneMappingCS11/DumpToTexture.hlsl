//--------------------------------------------------------------------------------------
// File: DumpToTexture.hlsl
//
// The PS for converting CS output buffer to a texture, used in CS path of 
// HDRToneMappingCS11 sample
// 
// Copyright (c) Microsoft Corporation. All rights reserved.
//--------------------------------------------------------------------------------------
StructuredBuffer<float4> buffer : register( t0 );

struct QuadVS_Output
{
    float4 Pos : SV_POSITION;              
    float2 Tex : TEXCOORD0;
};

cbuffer cbPS : register( b0 )
{
    uint4    g_param;   
};

float4 PSDump( QuadVS_Output Input ) : SV_TARGET
{
    return buffer[(Input.Tex.x-0.5)*g_param.x+(Input.Tex.y)*g_param.x*g_param.y];
}
