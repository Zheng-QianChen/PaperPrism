Texture2D<float4> SrcTex : register(t0);
SamplerState PointClamp  : register(s0);

cbuffer StatsParams : register(b0)
{
    float2 SrcSize;
    float2 GridSize;
    uint   Rotation;
    uint   SrcIsLinear;
    float2 _pad;
};

struct VSOut
{
    float4 Pos : SV_POSITION;
    float2 UV  : TEXCOORD0;
};

VSOut VS_Fullscreen(uint vid : SV_VertexID)
{
    VSOut o;
    o.UV = float2((vid << 1) & 2, vid & 2);
    o.Pos = float4(o.UV * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
    return o;
}

float2 ApplyRotation(float2 uv, uint rot)
{
    if (rot == 2) return float2(uv.y, 1.0f - uv.x);
    if (rot == 3) return float2(1.0f - uv.x, 1.0f - uv.y);
    if (rot == 4) return float2(1.0f - uv.y, uv.x);
    return uv;
}

float LinearScalarToSrgb(float c)
{
    c = saturate(c);
    return (c <= 0.0031308f) ? (c * 12.92f) : (1.055f * pow(c, 1.0f / 2.4f) - 0.055f);
}

float3 SampleSrcSrgb(float2 uv)
{
    float3 raw = SrcTex.SampleLevel(PointClamp, uv, 0).rgb;
    if (SrcIsLinear != 0)
        return float3(LinearScalarToSrgb(raw.r), LinearScalarToSrgb(raw.g), LinearScalarToSrgb(raw.b));
    return saturate(raw);
}

float4 PS_DownsampleStats(VSOut pin) : SV_TARGET
{
    float2 cellSize = 1.0f / GridSize;
    float2 cellOrigin = floor(pin.UV * GridSize) * cellSize;

    float lum[4][4];
    float sat[4][4];
    float sumLum = 0.0f;
    float sumLumSq = 0.0f;
    float sumSat = 0.0f;

    [unroll]
    for (int r = 0; r < 4; ++r)
    {
        [unroll]
        for (int c = 0; c < 4; ++c)
        {
            float2 subUV = cellOrigin + cellSize * float2((c + 0.5f) * 0.25f, (r + 0.5f) * 0.25f);
            float3 rgb = SampleSrcSrgb(ApplyRotation(saturate(subUV), Rotation));
            float y = dot(rgb, float3(0.2126f, 0.7152f, 0.0722f));
            float mx = max(rgb.r, max(rgb.g, rgb.b));
            float mn = min(rgb.r, min(rgb.g, rgb.b));
            float s = mx - mn;

            lum[r][c] = y;
            sat[r][c] = s;
            sumLum += y;
            sumLumSq += y * y;
            sumSat += s;
        }
    }

    float meanLum = sumLum * (1.0f / 16.0f);
    float varLum = max(0.0f, sumLumSq * (1.0f / 16.0f) - meanLum * meanLum);
    float stdLum = sqrt(varLum);
    float meanSat = sumSat * (1.0f / 16.0f);

    float darkFlatCount = 0.0f;
    float lightFlatCount = 0.0f;
    float anyFlatCount = 0.0f;
    float smoothGradCount = 0.0f;

    [unroll]
    for (int r2 = 0; r2 < 4; ++r2)
    {
        [unroll]
        for (int c2 = 0; c2 < 4; ++c2)
        {
            int cn = (c2 < 3) ? (c2 + 1) : 2;
            int rn = (r2 < 3) ? (r2 + 1) : 2;
            float gx = abs(lum[r2][c2] - lum[r2][cn]);
            float gy = abs(lum[r2][c2] - lum[rn][c2]);
            float g = max(gx, gy);

            float y = lum[r2][c2];
            float s = sat[r2][c2];

            if (g < 0.0055f)
            {
                anyFlatCount += 1.0f;
                if (y < 0.34f && s < 0.65f)
                    darkFlatCount += 1.0f;
                else if (y > 0.68f && s < 0.30f)
                    lightFlatCount += 1.0f;
            }

            if (g >= 0.0055f && g < 0.28f)
                smoothGradCount += 1.0f;
        }
    }

    float darkFlatRatio = darkFlatCount * (1.0f / 16.0f);
    float lightFlatRatio = lightFlatCount * (1.0f / 16.0f);
    float anyFlatRatio = anyFlatCount * (1.0f / 16.0f);
    float smoothGradRatio = smoothGradCount * (1.0f / 16.0f);

    float nonFlatRatio = 1.0f - anyFlatRatio;
    float chromaBonus = saturate((meanSat - 0.08f) * 1.8f) * saturate(nonFlatRatio * 1.5f);
    float midtoneSpread = saturate(stdLum * 3.2f) * saturate(1.0f - (darkFlatRatio + lightFlatRatio) * 1.4f);
    float photoScore = saturate(smoothGradRatio * 0.70f + nonFlatRatio * 0.25f + chromaBonus * 0.25f + midtoneSpread * 0.25f);

    return float4(meanLum, darkFlatRatio, lightFlatRatio, photoScore);
}
