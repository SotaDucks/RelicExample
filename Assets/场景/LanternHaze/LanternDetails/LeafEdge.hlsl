#ifndef RELIC_LEAF_EDGE_INCLUDED
#define RELIC_LEAF_EDGE_INCLUDED

void LeafEdge_float(float3 BaseColor, UnityTexture2D LeafTexture, float2 UV, float Threshold, out float3 Out)
{
    float2 uv = LeafTexture.GetTransformedUV(UV);
    // Use at least a texture texel so the line remains visible up close.
    float2 width = max(1.75 * LeafTexture.texelSize.xy, 1.25 * fwidth(uv));
    float2 horizontal = float2(width.x, 0.0);
    float2 vertical = float2(0.0, width.y);
    float neighbour = min(
        min(LeafTexture.Sample(LeafTexture.samplerstate, uv + horizontal).a,
            LeafTexture.Sample(LeafTexture.samplerstate, uv - horizontal).a),
        min(LeafTexture.Sample(LeafTexture.samplerstate, uv + vertical).a,
            LeafTexture.Sample(LeafTexture.samplerstate, uv - vertical).a));
    float edge = 1.0 - smoothstep(Threshold - 0.13, Threshold + 0.04, neighbour);
    float3 edgeColor = BaseColor * 0.43 + float3(0.028, 0.005, 0.007);
    Out = lerp(BaseColor, edgeColor, edge * 0.92);
}

#endif
