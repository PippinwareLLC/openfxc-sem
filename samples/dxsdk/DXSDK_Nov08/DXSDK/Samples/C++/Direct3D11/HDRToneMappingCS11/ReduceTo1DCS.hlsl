//-----------------------------------------------------------------------------
// File: ReduceTo1DCS.hlsl
//
// Desc: Reduce an input Texture2D to a Texture1D using a 16x16 grid
// 
// Copyright (c) Microsoft Corporation. All rights reserved.
//-----------------------------------------------------------------------------
Texture2D<float4> Input : register( t0 ); 
RWTexture1D<float> Result : register( u0 );

cbuffer cbCS : register( b0 )
{
    uint4    g_param;   // (g_param.x, g_param.y) is the x and y dimensions of the Dispatch call
                        // (g_param.z, g_param.w) is the size of the above Input Texture2D
};

#define blocksize 16
#define groupthreads (blocksize*blocksize)
groupshared float accum[groupthreads];

static const float4 LUM_VECTOR = float4(.299, .587, .114, 0);

[numthreads(blocksize,blocksize,1)]
void CSMain( uint3 Gid : SV_GroupID, uint3 DTid : SV_DispatchThreadID, uint3 GTid : SV_GroupThreadID )
{
    uint idx = blocksize*GTid.y+GTid.x;
    if ( DTid.x < g_param.z && DTid.y < g_param.w )
        accum[idx] = dot(Input.Load( uint3(DTid.xy, 0) ), LUM_VECTOR);            
    else
        accum[idx] = 0;

    for ( uint stride = groupthreads/2; stride > 0; stride >>= 1 ) // for this parallel reduction operates correctly, groupthreads must be 2^N
    {
        GroupMemoryBarrierWithGroupSync();
        if ( idx < stride )
            accum[idx] += accum[stride+idx];
    }    

    if ( idx == 0 )
    {                
        // if you only want to accumlate(rather than calculate the average) all the elements being reduced,
        // put 
        // Result[Gid.y*g_param.x+Gid.x] = accum[0];
        // instead of the following if statement here.
        //
        // The following if statement is used to determine the correct number of elements being reduced within
        // the thread group        
        if ( Gid.x < (g_param.x - 1) && Gid.y < (g_param.y - 1) )
            Result[Gid.y*g_param.x+Gid.x] = accum[0] / groupthreads;
        else
        if ( Gid.x == (g_param.x - 1) && Gid.y == (g_param.y - 1) )
            Result[Gid.y*g_param.x+Gid.x] = accum[0] / ( (g_param.z - Gid.x * blocksize) * (g_param.w - Gid.y * blocksize) );
        else
        if ( Gid.x < (g_param.x - 1) )
            Result[Gid.y*g_param.x+Gid.x] = accum[0] / ( blocksize * (g_param.w - Gid.y * blocksize) );
        else
            Result[Gid.y*g_param.x+Gid.x] = accum[0] / ( (g_param.z - Gid.x * blocksize) * blocksize );
    }
}

