#ifndef PREPROCESSOR_DEMO_HELPERS
#define PREPROCESSOR_DEMO_HELPERS

#define SCALE_FACTOR 2.0f
#define DOUBLE_COLOR(c) ((c) * SCALE_FACTOR)

float4 MultiplyScale(float4 value)
{
    return value * SCALE_FACTOR;
}

#endif
