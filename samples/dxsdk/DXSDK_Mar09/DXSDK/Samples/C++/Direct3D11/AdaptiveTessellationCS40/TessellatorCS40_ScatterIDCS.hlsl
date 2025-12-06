//--------------------------------------------------------------------------------------
// File: TessellatorCS40_ScatterIDCS.hlsl
//
// The CS to scatter vertex ID and triangle ID
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//--------------------------------------------------------------------------------------
StructuredBuffer<uint2> InputScanned : register(t0);
RWStructuredBuffer<uint2> TriIDIndexIDOut : register(u0);

[numthreads(1, 1, 1)]
void CSScatterVertexTriIDIndexID( uint3 DTid : SV_DispatchThreadID )
{
    uint start = InputScanned[DTid.x-1].x;
    uint end = InputScanned[DTid.x].x;

    for ( uint i = start; i < end; ++i ) 
    {
        TriIDIndexIDOut[i].x = DTid.x;
        TriIDIndexIDOut[i].y = i - start;
    }
}

[numthreads(1, 1, 1)]
void CSScatterIndexTriIDIndexID( uint3 DTid : SV_DispatchThreadID )
{
    uint start = InputScanned[DTid.x-1].y;
    uint end = InputScanned[DTid.x].y;

    for ( uint i = start; i < end; ++i ) 
    {
        TriIDIndexIDOut[i].x = DTid.x;
        TriIDIndexIDOut[i].y = i - start;
    }
}