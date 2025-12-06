//--------------------------------------------------------------------------------------
// File: TessellatorCS40_TessellateVerticesCS.hlsl
//
// The CS to tessellate vertices
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//--------------------------------------------------------------------------------------
StructuredBuffer<uint2> InputTriIDIndexID : register(t0);
StructuredBuffer<float> InputEdgeFactor : register(t1);

struct TessedVertex
{
    uint BaseTriID;
    float2 bc;
};
RWStructuredBuffer<TessedVertex> TessedVerticesOut : register(u0);

cbuffer cbCS
{
    uint4 g_param;
}

[numthreads(128, 1, 1)]
void CSTessellationVertices( uint3 DTid : SV_DispatchThreadID, uint3 Gid : SV_GroupID, uint GI : SV_GroupIndex  )
{
    uint id = DTid.x;
    //uint id = Gid.x * 128 + GI; // Workaround for some CS4x preview drivers
    
    if ( id < g_param.x )
    {
        uint tri_id = InputTriIDIndexID[id].x;
        uint index_id = InputTriIDIndexID[id].y;

        float2 tessed_vertices;

        const float2 tri_center = float2(1.0f / 3, 1.0f / 3);
        const float4 tri_vert[3] = 
        {
            float4(0, 0, 1, 0),
            float4(1, 0, 0, 1),
            float4(0, 1, 0, 0)
        };
        
        float3 edge_factor;
        edge_factor.x = InputEdgeFactor[tri_id * 3 + 0];
        edge_factor.y = InputEdgeFactor[tri_id * 3 + 1];
        edge_factor.z = InputEdgeFactor[tri_id * 3 + 2];
        
        uint3 ceil_edge_factor = (uint3)ceil(edge_factor);

        float min_factor = min(edge_factor.x, min(edge_factor.y, edge_factor.z));
        uint ceil_min_factor = (uint)ceil(min_factor);
        float sub_min_factor = frac(min_factor);
        if (0 == sub_min_factor)
        {
            sub_min_factor = 1;
        }
        
        uint num_block_vertices = ceil_min_factor * ceil_min_factor;
        
        if (index_id < num_block_vertices * 3)
        {
            uint block_id = index_id / num_block_vertices;
            uint index_in_block = index_id - block_id * num_block_vertices;
            if (0 == index_in_block)
            {
                tessed_vertices = tri_center;
            }
            else
            {
                uint level = (uint)sqrt(index_in_block);

                uint index_in_level = index_in_block - level * level;
                uint level_size = level * 2 + 1;

                float4 left_right = lerp(tri_center.xyxy, tri_vert[block_id], (level - 1 + sub_min_factor) / min_factor);
                float2 center = (left_right.xy + left_right.zw) / 2;

                if (index_in_level >= level_size / 2)
                {
                    tessed_vertices = lerp(center, left_right.zw, clamp((index_in_level + sub_min_factor - level_size / 2 - 1) / (level - 1 + sub_min_factor), 0.0f, 1.0f));
                }
                else
                {
                    tessed_vertices = lerp(left_right.xy, center, index_in_level / (level - 1 + sub_min_factor));
                }
            }
        }
        else
        {
            uint start = num_block_vertices * 3;
            uint block_id = 0;
            while ((block_id < 2) && (index_id >= start + ceil_edge_factor[block_id] * 2 + 1))
            {
                start += ceil_edge_factor[block_id] * 2 + 1;
                ++ block_id;
            }

            uint ceil_factor = ceil_edge_factor[block_id];
            float sub_factor = frac(edge_factor[block_id]);
            if (0 == sub_factor)
            {
                sub_factor = 1;
            }

            uint index_in_level = index_id - start;
            uint level_size = ceil_factor * 2 + 1;

            float4 left_right = tri_vert[block_id];
            float2 center = (left_right.xy + left_right.zw) / 2;

            if (index_in_level == level_size / 2)
            {
                tessed_vertices = center;
            }
            else
            {
                if (index_in_level >= level_size / 2)
                {
                    tessed_vertices = lerp(center, left_right.zw, (index_in_level - level_size / 2 - 1 + sub_factor) / (ceil_factor - 1 + sub_factor));
                }
                else
                {
                    tessed_vertices = lerp(left_right.xy, center, index_in_level / (ceil_factor - 1 + sub_factor));
                }
            }
        }

        TessedVerticesOut[id].BaseTriID = tri_id;
        TessedVerticesOut[id].bc = tessed_vertices;
    }    
}