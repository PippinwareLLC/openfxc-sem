#include "preprocessor_demo_helpers.hlsl"
#define USE_COLOR(inputColor) DOUBLE_COLOR(inputColor)

float4 main(float4 pos : POSITION) : SV_Position
{
    float4 baseColor = float4(1.0f, 0.25f, 1.0f, 1.0f);
    float4 scaled = MultiplyScale(pos);
    return USE_COLOR(scaled) + baseColor;
}
