float4 TernaryDemo(float x, float y) : SV_Target
{
    float ax = x > 0.5f ? x : 0.0f;
    float ay = y < 0 ? -y : y;
    return float4(ax, ay, 0.0f, 1.0f);
}
