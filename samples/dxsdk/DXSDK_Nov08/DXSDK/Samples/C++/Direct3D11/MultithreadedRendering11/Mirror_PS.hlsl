//--------------------------------------------------------------------------------------
// File: Mirror_PS.hlsl
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//--------------------------------------------------------------------------------------

//--------------------------------------------------------------------------------------
// Textures and Samplers
//--------------------------------------------------------------------------------------
Texture2D	g_txDiffuse : register( t0 );
SamplerState g_samLinear : register( s0 );

//--------------------------------------------------------------------------------------
// Input / Output structures
//--------------------------------------------------------------------------------------
struct PS_INPUT
{
	float2 vTexcoord	: TEXCOORD0;
};

//--------------------------------------------------------------------------------------
// Pixel Shader
//--------------------------------------------------------------------------------------
float4 PSMain( PS_INPUT Input ) : SV_TARGET
{
	float2 flipcoord = Input.vTexcoord;
	flipcoord.y = 1- Input.vTexcoord.y;
	float4 vDiffuse = g_txDiffuse.Sample( g_samLinear, flipcoord  );

	return vDiffuse;
}

