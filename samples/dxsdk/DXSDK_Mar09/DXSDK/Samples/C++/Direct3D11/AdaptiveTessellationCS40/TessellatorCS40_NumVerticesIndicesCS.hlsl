//--------------------------------------------------------------------------------------
// File: TessellatorCS40_NumVerticesIndicesCS.hlsl
//
// The CS to compute number of vertices and triangles to be generated from edge tessellation factor
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//--------------------------------------------------------------------------------------
StructuredBuffer<float> InputEdgeFactor : register(t0);
RWStructuredBuffer<uint2> NumVerticesIndicesOut : register(u0);

[numthreads(1, 1, 1)]
void CSNumVerticesIndices( uint3 DTid : SV_DispatchThreadID )
{
    float3 edge_factor = float3( InputEdgeFactor[DTid.x*3+0], InputEdgeFactor[DTid.x*3+1], InputEdgeFactor[DTid.x*3+2] );
    float min_factor = min(edge_factor.x, min(edge_factor.y, edge_factor.z));
    uint ceil_min_factor = (uint)ceil(min_factor);

    if (ceil_min_factor != 0)
    {
        uint3 ceil_edge_factor = (uint3)ceil(edge_factor);

        uint num_block_vertices = ceil_min_factor * ceil_min_factor;
        uint num_block_indices = (ceil_min_factor * 2 + 2) * (ceil_min_factor - 1);
        NumVerticesIndicesOut[DTid.x] = uint2(num_block_vertices, num_block_indices) * 3 + 
            (ceil_edge_factor.x + ceil_edge_factor.y + ceil_edge_factor.z) * uint2(2, 4) + uint2(3, 6);
    }
    else
    {
        NumVerticesIndicesOut[DTid.x] = 0;
    }
}
