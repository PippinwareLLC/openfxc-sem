//--------------------------------------------------------------------------------------
// File: BasicCompute11.hlsl
//
// This file contains the Compute Shader to perform array A + array B
// 
// Copyright (c) Microsoft Corporation. All rights reserved.
//--------------------------------------------------------------------------------------

struct BufType
{
    int i;
    float f;
};

StructuredBuffer<BufType> Buffer0 : register(t0);
StructuredBuffer<BufType> Buffer1 : register(t1);
RWStructuredBuffer<BufType> BufferOut : register(u0);

[numthreads(1, 1, 1)]
void CSMain( uint3 DTid : SV_DispatchThreadID )
{
    BufferOut[DTid.x].i = Buffer0[DTid.x].i + Buffer1[DTid.x].i;
    BufferOut[DTid.x].f = Buffer0[DTid.x].f + Buffer1[DTid.x].f;
}

// Use below instead if are playing with raw buffer type
//ByteAddressBuffer Buffer0 : register(t0);
//ByteAddressBuffer Buffer1 : register(t1);
//RWByteAddressBuffer BufferOut : register(u0);
//
//[numthreads(1, 1, 1)]
//void CSMain( uint3 DTid : SV_DispatchThreadID )
//{
//    int i0 = asint( Buffer0.Load( DTid.x*8 ) );
//    float f0 = asfloat( Buffer0.Load( DTid.x*8+4 ) );
//    int i1 = asint( Buffer1.Load( DTid.x*8 ) );
//    float f1 = asfloat( Buffer1.Load( DTid.x*8+4 ) );
//
//    BufferOut.Store( DTid.x*8, asuint(i0 + i1) );
//    BufferOut.Store( DTid.x*8+4, asuint(f0 + f1) );
//}
