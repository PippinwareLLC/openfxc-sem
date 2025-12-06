//-----------------------------------------------------------------------------
// File: FinalPassCS.hlsl
//
// Desc: Tone mapping using the calculated luminance
// 
// Copyright (c) Microsoft Corporation. All rights reserved.
//-----------------------------------------------------------------------------
Texture2D<float4> Input : register( t0 ); 
Texture1D<float> Luminance : register( t1 );
RWTexture2D<float4> Result : register( u0 );

static const float  MIDDLE_GRAY = 0.72f;
static const float  LUM_WHITE = 1.5f;

[numthreads(1,1,1)]
void CSMain( uint3 Tid : SV_DispatchThreadID )
{    
    float Lum = Luminance.Load( uint2(0, 0) );
    float4 vColor = Input.Load( uint3(Tid.xy, 0) );

    // Tone mapping
    vColor.rgb *= MIDDLE_GRAY / (Lum + 0.001f);
    vColor.rgb *= (1.0f + vColor/LUM_WHITE);
    vColor.rgb /= (1.0f + vColor);
    
    vColor.a = 1.0f;

    Result[Tid.xy] = vColor;
}
