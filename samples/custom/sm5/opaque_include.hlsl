#include "opaque_header.h"

float4 main(float2 uv : TEXCOORD0) : SV_Target
{
    // The included header is non-HLSL, so this should still parse even when it is present.
    return float4(uv, 0, 1);
}
