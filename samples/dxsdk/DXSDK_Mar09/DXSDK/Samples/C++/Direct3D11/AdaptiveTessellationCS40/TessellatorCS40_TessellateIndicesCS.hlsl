//--------------------------------------------------------------------------------------
// File: TessellatorCS40_TessellateIndicesCS.hlsl
//
// The CS to tessellate indices
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//--------------------------------------------------------------------------------------
StructuredBuffer<uint2> InputTriIDIndexID : register(t0);
StructuredBuffer<float> InputEdgeFactor : register(t1);
StructuredBuffer<uint2> InputScanned : register(t2);

RWByteAddressBuffer TessedIndicesOut : register(u0);

cbuffer cbCS
{
    uint4 g_param;
}

[numthreads(128, 1, 1)]
void CSTessellationIndices( uint3 DTid : SV_DispatchThreadID, uint3 Gid : SV_GroupID, uint GI : SV_GroupIndex )
{
    uint id = DTid.x;
    //uint id = Gid.x * 128 + GI; // Workaround for some CS4x preview drivers
    
    if ( id < g_param.x )
    {
        uint tri_id = InputTriIDIndexID[id].x;
        uint index_id = InputTriIDIndexID[id].y;
        uint base_vertex = InputScanned[tri_id-1].x;
        
        uint tessed_indices;

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
        uint num_block_indices = (ceil_min_factor * 2 + 2) * (ceil_min_factor - 1);

        if (index_id < num_block_indices * 3)
        {
            uint block_id = index_id / num_block_indices;
            uint index_in_block = index_id - block_id * num_block_indices;
            uint level = (uint)sqrt(1.0f + index_in_block / 2);

            uint index_in_level = index_in_block - (level * 2 + 2) * (level - 1);
            uint level_size = level * 4 + 2;

            if (index_in_level == level_size - 1)
            {
                tessed_indices = -1;
            }
            else
            {
                tessed_indices = base_vertex + index_in_level / 2 + block_id * num_block_vertices;
                if (index_in_level & 1)
                {
                    tessed_indices += (level - 1) * (level - 1);

                    if (index_in_level >= level_size / 2)
                    {
                        -- tessed_indices;
                    }
                }
                else
                {
                    tessed_indices += level * level;
                }
            }
        }
        else
        {
            uint start_index = num_block_indices * 3;
            uint start_vertex = num_block_vertices * 3;
            uint block_id = 0;
            while ((block_id < 2) && (index_id >= start_index + ceil_edge_factor[block_id] * 4 + 2))
            {
                start_index += ceil_edge_factor[block_id] * 4 + 2;
                start_vertex += ceil_edge_factor[block_id] * 2 + 1;
                ++ block_id;
            }

            uint ceil_factor = ceil_edge_factor[block_id];
            float sub_factor = frac(edge_factor[block_id]);
            if (0 == sub_factor)
            {
                sub_factor = 1;
            }

            uint index_in_level = index_id - start_index;
            uint level_size = ceil_factor * 4 + 2;
            
            if (index_in_level == level_size - 1)
            {
                tessed_indices = -1;
            }
            else
            {
                if (index_in_level & 1)
                {
                    uint level = ceil_min_factor - 1;
                    tessed_indices = base_vertex + level * level + block_id * num_block_vertices;

                    if (index_in_level >= level_size / 2)
                    {
                        tessed_indices = max(tessed_indices + level, tessed_indices + index_in_level / 2 - (ceil_factor - level) * 2 + 1);
                    }
                    else
                    {
                        tessed_indices += min(index_in_level / 2, level);
                    }
                }
                else
                {
                    tessed_indices = base_vertex + index_in_level / 2 + start_vertex;
                }
            }
        }

        TessedIndicesOut.Store(id*4, tessed_indices);
    }       
}