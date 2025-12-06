//--------------------------------------------------------------------------------------
// File: NBodyGravityCS11.hlsl
//
// Demonstrates how to use Compute Shader to do n-body gravity computation
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//--------------------------------------------------------------------------------------

static float softeningSquared = 0.0012500000*0.0012500000;
static float g_fG = 6.67300e-11f * 10000.0f;
static float g_fParticleMass = g_fG*10000.0f * 10000.0f;

#define blocksize 128
groupshared float4 sharedPos[blocksize];

// Body to body interaction, acceleration of the particle at position bi is updated
void bodyBodyInteraction(inout float3 ai, float4 bj, float4 bi ) 
{
    float3 r = bj.xyz - bi.xyz;

    float distSqr = dot(r, r);
    distSqr += softeningSquared;

    float invDist = 1.0f / sqrt(distSqr);
	float invDistCube =  invDist * invDist * invDist;
    
    float s = g_fParticleMass * invDistCube;

    ai += r * s;    
}

cbuffer cbCS : register( b0 )
{
    uint4   g_param;    // pcbCS->param[0] = MAX_PARTICLES;
                        // pcbCS->param[1] = dimx;              
    float4  g_paramf;   // pcbCS->paramf[0] = 0.1f;
                        // pcbCS->paramf[1] = 1; 
};

struct PosVelo
{
    float4 pos;
    float4 velo;
};

StructuredBuffer<PosVelo> oldPosVelo;
RWStructuredBuffer<PosVelo> newPosVelo;

[numthreads(blocksize, 1, 1)]
void CSMain( uint3 Gid : SV_GroupID, uint3 DTid : SV_DispatchThreadID, uint3 GTid : SV_GroupThreadID, uint GI : SV_GroupIndex )
{
    // Each thread of the CS updates one of the particles

    float4 pos = oldPosVelo[DTid.x].pos;
    float4 vel = oldPosVelo[DTid.x].velo;
    float3 accel = 0;

    // Update current particle using all other particles
    [loop]
    for (uint tile = 0; tile < g_param.y; tile++)
    {
        // Cache a tile of particles unto shared memory to increase IO efficiency
        sharedPos[GI] = oldPosVelo[tile * blocksize + GI].pos;
       
        GroupMemoryBarrierWithGroupSync();        

        [unroll]
        for (uint counter = 0; counter < blocksize; counter+=8 ) 
        {
            bodyBodyInteraction(accel, sharedPos[counter], pos); 	
            bodyBodyInteraction(accel, sharedPos[counter+1], pos); 
            bodyBodyInteraction(accel, sharedPos[counter+2], pos); 	
            bodyBodyInteraction(accel, sharedPos[counter+3], pos); 
            bodyBodyInteraction(accel, sharedPos[counter+4], pos); 	
            bodyBodyInteraction(accel, sharedPos[counter+5], pos); 
            bodyBodyInteraction(accel, sharedPos[counter+6], pos); 
            bodyBodyInteraction(accel, sharedPos[counter+7], pos); 
        }        
        
        GroupMemoryBarrierWithGroupSync();
    }  

    // Update the velocity and position of current particle using the acceleration computed above
    vel.xyz += accel.xyz * g_paramf.x;      //deltaTime;
    vel.xyz *= g_paramf.y;                  //damping;
    pos.xyz += vel.xyz * g_paramf.x;        //deltaTime;    

    if ( DTid.x < g_param.x )
    {
        newPosVelo[DTid.x].pos = pos;
        newPosVelo[DTid.x].velo = float4(vel.xyz, length(accel));
    }
}
