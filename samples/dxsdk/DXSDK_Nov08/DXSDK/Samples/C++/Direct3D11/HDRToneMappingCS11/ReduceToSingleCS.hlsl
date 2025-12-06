//-----------------------------------------------------------------------------
// File: ReduceToSingleCS.hlsl
//
// Desc: Reduce an input Texture1D by a factor of groupthreads
// 
// Copyright (c) Microsoft Corporation. All rights reserved.
//-----------------------------------------------------------------------------

Texture1D<float> Input : register( t0 );
RWTexture1D<float> Result : register( u0 );

cbuffer cbCS : register( b0 )
{
    uint4    g_param;   // g_param.x is the actual elements contained in Input
                        // g_param.y is the x dimension of the Dispatch call
};

#define groupthreads 128
groupshared float accum[groupthreads];

[numthreads(groupthreads,1,1)]
void CSMain( uint3 Gid : SV_GroupID, uint3 DTid : SV_DispatchThreadID, uint3 GTid : SV_GroupThreadID )
{
    if ( DTid.x < g_param.x )
        accum[GTid.x] = Input.Load( uint2(DTid.x, 0) );
    else
        accum[GTid.x] = 0;

    for ( uint stride = groupthreads/2; stride > 0; stride >>= 1 ) // for this parallel reduction operates correctly, groupthreads must be 2^N
    {
        GroupMemoryBarrierWithGroupSync();
        if ( GTid.x < stride )
            accum[GTid.x] += accum[stride+GTid.x];
    }    

    if ( GTid.x == 0 )
    {
        // if you only want to accumlate(rather than calculate the average) all the elements being reduced,
        // put 
        // Result[Gid.x] = accum[0];
        // instead of the following if statement here.
        //
        // The following if statement is used to determine the correct number of elements being reduced within
        // the thread group
        if ( Gid.x < (g_param.y - 1) )
            Result[Gid.x] = accum[0] / groupthreads;
        else
            Result[Gid.x] = accum[0] / (g_param.x - Gid.x * groupthreads);
    }
}