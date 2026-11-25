Texture2D<float4> SrcTex : register(t0);
SamplerState PointClamp  : register(s0);

cbuffer SmartInvertParams : register(b0)
{
    float2 ScreenSize;
    uint   Rotation;
    uint   Mode; // 0=Off, 1=SmartAuto, 2=ForceInvertAll, 3=CrispLightOnly

    float  PaperWhiteKnee;
    float  InkBlackKnee;
    float  SaturationBoost;
    float  ContrastGamma;

    uint   KeepLightWindows;
    uint   ProtectPhotos;
    uint   PreserveHue;
    uint   EnhanceLightRegions;

    uint   WindowCount;
    uint   ProtectedCount;
    uint   DebugView;
    uint   SrcIsLinear;

    uint   UseHardwareHolePunch;
    uint   UncoveredDesktopPassthrough;
    float2 _pad0;

    float4 WindowRects[16];     // (left, top, right, bottom) in monitor pixels
    float4 WindowFlags[16];     // .x = IsDark, .y = IsPassthroughHole, .z = EffectiveMode (1=Smart, 2=Force, 3=CrispLight)
    float4 ProtectedRects[8];   // Clean axis-aligned rectangles (left, top, right, bottom)
    float4 ProtectedFlags[8];   // .x = 1.0 if Video/Photo Hole-Punch, 0.0 if White Sub-Pane
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

float SrgbScalarToLinear(float c)
{
    return (c <= 0.04045f) ? (c / 12.92f) : pow(max((c + 0.055f) / 1.055f, 0.0f), 2.4f);
}

float LinearScalarToSrgb(float c)
{
    c = saturate(c);
    return (c <= 0.0031308f) ? (c * 12.92f) : (1.055f * pow(c, 1.0f / 2.4f) - 0.055f);
}

float3 SrgbToLinear(float3 c)
{
    return float3(SrgbScalarToLinear(c.r), SrgbScalarToLinear(c.g), SrgbScalarToLinear(c.b));
}

float3 LinearToSrgb(float3 c)
{
    return float3(LinearScalarToSrgb(c.r), LinearScalarToSrgb(c.g), LinearScalarToSrgb(c.b));
}

float4 SampleSrcRgba(float2 uv)
{
    float4 raw = SrcTex.SampleLevel(PointClamp, uv, 0);
    // Unmultiply premultiplied alpha along anti-aliased rounded window edges
    float3 rgb = (raw.a > 0.01f && raw.a < 0.995f) ? saturate(raw.rgb / raw.a) : saturate(raw.rgb);
    if (SrcIsLinear != 0)
        rgb = LinearToSrgb(rgb);
    return float4(rgb, raw.a);
}

float3 SampleSrcSrgb(float2 uv)
{
    return SampleSrcRgba(uv).rgb;
}

float3 LinearSrgbToOklab(float3 c)
{
    float l = 0.4122214708f * c.r + 0.5363325363f * c.g + 0.0514459929f * c.b;
    float m = 0.2119034982f * c.r + 0.6806995451f * c.g + 0.1073969566f * c.b;
    float s = 0.0883024619f * c.r + 0.2817188376f * c.g + 0.6299787005f * c.b;

    float l_ = pow(max(l, 0.0f), 1.0f / 3.0f);
    float m_ = pow(max(m, 0.0f), 1.0f / 3.0f);
    float s_ = pow(max(s, 0.0f), 1.0f / 3.0f);

    return float3(
        0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_,
        1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_,
        0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_
    );
}

float3 OklabToLinearSrgb(float3 lab)
{
    float l_ = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
    float m_ = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
    float s_ = lab.x - 0.0894841775f * lab.y - 1.2914855480f * lab.z;

    float l = l_ * l_ * l_;
    float m = m_ * m_ * m_;
    float s = s_ * s_ * s_;

    return float3(
        +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
        -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
        -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s
    );
}

bool InLinearGamut(float3 rgb)
{
    return (rgb.r >= -0.001f && rgb.r <= 1.001f &&
            rgb.g >= -0.001f && rgb.g <= 1.001f &&
            rgb.b >= -0.001f && rgb.b <= 1.001f);
}

float3 OklabGamutMapToSrgb(float targetL, float2 ab)
{
    float3 lin = OklabToLinearSrgb(float3(targetL, ab.x, ab.y));
    if (!InLinearGamut(lin))
    {
        float lo = 0.0f;
        float hi = 1.0f;
        [unroll]
        for (int i = 0; i < 6; ++i)
        {
            float mid = 0.5f * (lo + hi);
            float3 test = OklabToLinearSrgb(float3(targetL, ab * mid));
            if (InLinearGamut(test))
                lo = mid;
            else
                hi = mid;
        }
        lin = OklabToLinearSrgb(float3(targetL, ab * lo));
    }
    return LinearToSrgb(saturate(lin));
}

float ComputeClearTypeDeFringeWeight(float2 uv, float3 src, float sat)
{
    if (sat < 0.015f || sat > 0.38f)
        return 1.0f;

    float2 dx = float2(1.0f / max(ScreenSize.x, 1.0f), 0.0f);
    float3 leftRgb  = SampleSrcSrgb(ApplyRotation(saturate(uv - dx), Rotation));
    float3 rightRgb = SampleSrcSrgb(ApplyRotation(saturate(uv + dx), Rotation));

    float yL = dot(leftRgb,  float3(0.2126f, 0.7152f, 0.0722f));
    float yR = dot(rightRgb, float3(0.2126f, 0.7152f, 0.0722f));
    float sL = max(leftRgb.r, max(leftRgb.g, leftRgb.b)) - min(leftRgb.r, min(leftRgb.g, leftRgb.b));
    float sR = max(rightRgb.r, max(rightRgb.g, rightRgb.b)) - min(rightRgb.r, min(rightRgb.g, rightRgb.b));

    float edgeJump = abs(yL - yR);
    bool neighborNeutral = (min(sL, sR) < 0.05f && max(sL, sR) < 0.22f);
    if (edgeJump > 0.22f && neighborNeutral)
    {
        return saturate(1.0f - (edgeJump - 0.22f) * 3.5f);
    }
    return 1.0f;
}

float3 ComputeSmartInvertedColor(float2 uv, float3 src)
{
    float mx = max(src.r, max(src.g, src.b));
    float mn = min(src.r, min(src.g, src.b));
    float sat = mx - mn;
    float ySrgb = dot(src, float3(0.2126f, 0.7152f, 0.0722f));

    if (PreserveHue == 0)
    {
        float3 inv = 1.0f - src;
        float bk = InkBlackKnee;
        float wk = max(PaperWhiteKnee, bk + 0.05f);
        inv = saturate((inv - bk) / (wk - bk));
        return pow(inv, max(ContrastGamma, 0.2f));
    }

    float deFringe = ComputeClearTypeDeFringeWeight(uv, src, sat);
    float effSat = sat * deFringe;

    float yInv = 1.0f - ySrgb;
    float chromaProtect = saturate(effSat * 2.6f);
    float wk = lerp(PaperWhiteKnee, 0.96f, chromaProtect);
    float bk = lerp(InkBlackKnee,   0.04f, chromaProtect);
    wk = max(wk, bk + 0.08f);

    float yTarget = saturate((yInv - bk) / (wk - bk));
    yTarget = pow(yTarget, max(ContrastGamma, 0.2f));

    if (effSat > 0.14f && ySrgb > 0.45f)
    {
        yTarget = clamp(yTarget * 0.88f + 0.05f, 0.16f, 0.52f);
    }

    if (SaturationBoost <= 0.01f || effSat < 0.012f)
    {
        return float3(yTarget, yTarget, yTarget);
    }

    float3 lab = LinearSrgbToOklab(SrgbToLinear(src));
    float targetLok = pow(max(SrgbScalarToLinear(yTarget), 0.0f), 1.0f / 3.0f);
    float2 boostedAB = lab.yz * (SaturationBoost * deFringe);

    return OklabGamutMapToSrgb(targetLok, boostedAB);
}

float3 ComputeLightEnhancedColor(float3 src)
{
    if (EnhanceLightRegions == 0)
        return src;

    float mx = max(src.r, max(src.g, src.b));
    float mn = min(src.r, min(src.g, src.b));
    float sat = mx - mn;
    float ySrgb = dot(src, float3(0.2126f, 0.7152f, 0.0722f));

    float wk = 0.955f;
    float bk = 0.055f;
    float yCrisp = saturate((ySrgb - bk) / (wk - bk));

    if (SaturationBoost <= 0.01f || sat < 0.04f)
        return float3(yCrisp, yCrisp, yCrisp);

    float mildBoost = lerp(1.0f, SaturationBoost, 0.45f);
    float3 lab = LinearSrgbToOklab(SrgbToLinear(src));
    float neutralBlend = saturate((0.12f - sat) / 0.08f);
    float crispLok = pow(max(SrgbScalarToLinear(yCrisp), 0.0f), 1.0f / 3.0f);
    float targetLok = lerp(lab.x, crispLok, neutralBlend);
    return OklabGamutMapToSrgb(targetLok, lab.yz * mildBoost);
}

// Strict binary geometric decision: NO fractional semi-transparent blending (eliminates all ghosting & blotches!)
// Returns: x = shouldInvert (1 or 0), y = shouldHolePunch (1 or 0)
float2 ResolveGeometricRegion(float2 uv, float3 src, float srcAlpha)
{
    if (Mode == 0) return float2(0.0f, 1.0f);

    if (UncoveredDesktopPassthrough != 0 && srcAlpha < 0.15f)
    {
        // Outside rounded window corners over uncovered desktop -> exact Alpha=0 passthrough!
        return float2(0.0f, 1.0f);
    }

    float2 px = uv * ScreenSize;

    // 1. Front-most Win32 top-level window + immediate behind-window check for rounded-corner popups
    bool foundWin = false;
    bool inDarkWin = true;
    bool isPassthroughWin = false;
    uint winMode = Mode;
    float4 frontRect = float4(0.0f, 0.0f, 0.0f, 0.0f);
    bool hasDarkBehind = false;
    bool hasLightBehind = false;

    [loop]
    for (uint i = 0; i < WindowCount; ++i)
    {
        float4 wrc = WindowRects[i];
        if (px.x >= wrc.x && px.x < wrc.z && px.y >= wrc.y && px.y < wrc.w)
        {
            if (!foundWin)
            {
                foundWin = true;
                inDarkWin = (WindowFlags[i].x > 0.5f);
                isPassthroughWin = (WindowFlags[i].y > 0.5f);
                if (WindowFlags[i].z > 0.5f)
                    winMode = (uint)(WindowFlags[i].z + 0.5f);
                frontRect = wrc;
            }
            else if (WindowFlags[i].y < 0.5f)
            {
                if (WindowFlags[i].x > 0.5f) hasDarkBehind = true;
                else hasLightBehind = true;
                break;
            }
        }
    }

    if (isPassthroughWin || (!foundWin && UncoveredDesktopPassthrough != 0))
    {
        // Uncaptured front system shell bar, non-whitelisted app, or uncovered desktop -> Alpha=0 passthrough!
        return float2(0.0f, 1.0f);
    }

    // Seamless Rounded-Corner & Drop-Shadow Margin Ownership for Floating Popups (e.g. browser text-selection UI):
    // Where a rounded popup's outer corners are transparent, Premultiplied Alpha preserved the behind-window's pixels.
    if (foundWin && (hasDarkBehind || hasLightBehind))
    {
        float distEdge = min(min(px.x - frontRect.x, frontRect.z - px.x), min(px.y - frontRect.y, frontRect.w - px.y));
        if (distEdge < 36.0f)
        {
            float yPx = dot(src, float3(0.2126f, 0.7152f, 0.0722f));
            float satPx = max(src.r, max(src.g, src.b)) - min(src.r, min(src.g, src.b));
            if (!inDarkWin && hasDarkBehind && yPx < 0.36f && satPx < 0.18f)
                inDarkWin = true;
            else if (inDarkWin && hasLightBehind && yPx > 0.68f && satPx < 0.18f)
                inDarkWin = false;
        }
    }

    if (winMode == 2) return float2(1.0f, 0.0f);
    if (winMode == 3) return float2(0.0f, 0.0f);

    if (!inDarkWin && KeepLightWindows != 0)
    {
        // Already-light window: pure native passthrough when EnhanceLightRegions==0, or crisp light paper
        return float2(0.0f, (EnhanceLightRegions == 0) ? 1.0f : 0.0f);
    }

    // 2. Clean Axis-Aligned Protected Rectangles inside Dark Windows (Web <video>, Rectangular Photos, White Viewports)
    float cellMarginX = max(ScreenSize.x / 80.0f, 16.0f);
    float cellMarginY = max(ScreenSize.y / 45.0f, 16.0f);

    [loop]
    for (uint k = 0; k < ProtectedCount; ++k)
    {
        float4 prc = ProtectedRects[k];
        // Expand check slightly by 1 grid cell so pixel-exact edge snapping catches the exact border
        if (px.x >= prc.x - cellMarginX * 0.5f && px.x < prc.z + cellMarginX * 0.5f &&
            px.y >= prc.y - cellMarginY * 0.5f && px.y < prc.w + cellMarginY * 0.5f)
        {
            bool isDeepInterior =
                (px.x >= prc.x + cellMarginX) && (px.x < prc.z - cellMarginX) &&
                (px.y >= prc.y + cellMarginY) && (px.y < prc.w - cellMarginY);

            bool isMediaBox = (ProtectedFlags[k].x > 0.5f);

            if (!isDeepInterior)
            {
                // Snap 1-cell grid rim to the exact 1-pixel UI border:
                // If pixel is flat dark UI background (#000..#383838), it belongs to the surrounding dark UI!
                float y = dot(src, float3(0.2126f, 0.7152f, 0.0722f));
                float sat = max(src.r, max(src.g, src.b)) - min(src.r, min(src.g, src.b));
                float2 stepUV = 2.0f / max(ScreenSize, float2(1.0f, 1.0f));
                float3 nbr = SampleSrcSrgb(ApplyRotation(saturate(uv + float2(stepUV.x, 0.0f)), Rotation));
                float yNbr = dot(nbr, float3(0.2126f, 0.7152f, 0.0722f));
                bool flatDarkUi = (abs(y - yNbr) < 0.018f) && (y < 0.28f) && (sat < 0.15f);

                if (flatDarkUi)
                    continue; // Outside the card/video -> invert as clean white UI!

                // Outside raw box: flatDarkUi was already excluded above, so non-dark photo rim is included; for white cards require light tone
                bool insideRaw = (px.x >= prc.x && px.x < prc.z && px.y >= prc.y && px.y < prc.w);
                if (!insideRaw && !isMediaBox && y < 0.65f)
                    continue;
            }

            // Protected clean rectangle! Media/Video ALWAYS gets Alpha=0 hardware cutout (native 60/120Hz!)
            return float2(0.0f, isMediaBox ? 1.0f : ((EnhanceLightRegions == 0) ? 1.0f : 0.0f));
        }
    }

    return float2(1.0f, 0.0f);
}

float4 PS_SmartInvert(VSOut pin) : SV_TARGET
{
    float2 sampleUV = ApplyRotation(pin.UV, Rotation);
    float4 srcRgba = SampleSrcRgba(sampleUV);
    float3 src = srcRgba.rgb;

    if (Mode == 0)
        return (UseHardwareHolePunch != 0) ? float4(0.0f, 0.0f, 0.0f, 0.0f) : float4(src, 1.0f);

    float2 dec = ResolveGeometricRegion(pin.UV, src, srcRgba.a);
    bool shouldInvert = (dec.x > 0.5f);
    bool shouldHolePunch = (dec.y > 0.5f);

    if (DebugView != 0)
    {
        float3 baseRgb = shouldInvert ? ComputeSmartInvertedColor(pin.UV, src) : ComputeLightEnhancedColor(src);
        float3 tint = shouldHolePunch
            ? float3(1.0f, 0.50f, 0.10f) // Orange = Native Alpha=0 Video/Photo Cutout
            : (shouldInvert ? float3(0.15f, 0.55f, 1.0f) : float3(0.10f, 0.85f, 0.30f));
        return float4(lerp(baseRgb, tint, 0.22f), 1.0f);
    }

    // Strict binary Alpha (ONLY 0.0 or 1.0 — zero semi-transparent delayed-frame double-vision!)
    if (!shouldInvert)
    {
        if (UseHardwareHolePunch != 0 && shouldHolePunch)
            return float4(0.0f, 0.0f, 0.0f, 0.0f); // 100% transparent DWM hole -> live 60/120Hz browser video!

        return float4(ComputeLightEnhancedColor(src), 1.0f);
    }

    return float4(ComputeSmartInvertedColor(pin.UV, src), 1.0f);
}
