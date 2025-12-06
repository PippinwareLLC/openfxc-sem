//-----------------------------------------------------------------------------
// File: OITPS.hlsl
//
// Desc: Pixel shaders used in the Order Independent Transparency sample.
// 
// Copyright (c) Microsoft Corporation. All rights reserved.
//-----------------------------------------------------------------------------
//TODO: Use structured buffers
RWTexture2D<uint> fragmentCount     : register( u1 );
RWBuffer<float>   deepBufferDepth   : register( u2 );
RWBuffer<uint4>   deepBufferColor   : register( u3 );
RWBuffer<uint>    prefixSum         : register( u4 );

cbuffer CB : register( b0 )
{
    uint g_nFrameWidth      : packoffset( c0.x );
    uint g_nFrameHeight     : packoffset( c0.y );
    uint g_nReserved0       : packoffset( c0.z );
    uint g_nReserved1       : packoffset( c0.w );
}

struct SceneVS_Output
{
    float4 pos   : SV_POSITION;
    float4 color : COLOR0;
};

void FragmentCountPS( SceneVS_Output input)
{
    fragmentCount[input.pos.xy]++;
}

void FillDeepBufferPS( SceneVS_Output input )
{
    uint x = input.pos.x;
    uint y = input.pos.y;
    uint nPrefixSumPos = y*g_nFrameWidth + x;
    uint nDeepBufferPos;
    if( nPrefixSumPos == 0 )
        nDeepBufferPos = fragmentCount[input.pos.xy];
    else
        nDeepBufferPos = prefixSum[nPrefixSumPos-1] + fragmentCount[input.pos.xy];
    
    deepBufferDepth[nDeepBufferPos] = input.pos.z;
    deepBufferColor[nDeepBufferPos] = clamp(input.color, 0, 1)*255;
    fragmentCount[input.pos.xy]++;
}

