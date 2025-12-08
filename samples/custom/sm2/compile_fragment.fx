// Custom sample exercising compile_fragment and brace initializers.

void Projection(bool enabled)
{
    float4 pos = 0;
    if (enabled) { pos.x += 1; }
}

vertexfragment ProjectionFragment_Animated = compile_fragment vs_2_0 Projection(true);

technique Unselected
{
    pass P0
    {
        MaterialAmbient = {1.0, 1.0, 1.0, 1.0};
        MaterialDiffuse = {0.5, 0.5, 0.5, 1.0};
    }
}
