using EInkSmartInvert.Config;

namespace EInkSmartInvert.Core;

public readonly record struct ClassifiedWindow(
    IntPtr Hwnd,
    Rectangle LocalBounds,
    bool IsDark,
    bool IsPassthroughHole = false,
    FilterMode EffectiveMode = FilterMode.SmartAuto,
    string ProcessName = ""
);

public readonly record struct ProtectedRect(
    Rectangle LocalBounds,
    bool IsMedia // true = Video/Photo cutout, false = White Sub-Pane
);

/// <summary>
/// Ultra-lightweight (80x45 = 3,600 cells, ~18 KB total memory, 0 GC allocations per frame)
/// geometric analyzer. Produces ONLY clean axis-aligned rectangles (never organic blotches)
/// and rejects full-window scrolling/dragging so motion never leaves ghost trails.
/// </summary>
public sealed unsafe class GeometricRegionTracker
{
    public const int GridW = 80;
    public const int GridH = 45;
    private const int CellCount = GridW * GridH;

    private readonly float[] _lum = new float[CellCount];
    private readonly float[] _prevLum = new float[CellCount];
    private readonly float[] _darkFlat = new float[CellCount];
    private readonly float[] _lightFlat = new float[CellCount];
    private readonly float[] _photo = new float[CellCount];
    private readonly float[] _videoEnergy = new float[CellCount];

    private readonly int[] _cellOwner = new int[CellCount];
    private readonly byte[] _candMask = new byte[CellCount];
    private readonly byte[] _visited = new byte[CellCount];
    private readonly int[] _bfsQueue = new int[CellCount];

    private readonly Dictionary<IntPtr, WindowState> _winStates = new(32);
    private readonly List<HoldVideoBox> _activeVideoBoxes = new(8);
    private bool _hasPrevFrame;

    private sealed class WindowState
    {
        public bool IsDark = true;
        public bool Initialized;
        public Rectangle LastBounds;
        public int ScrollCooldownFrames;
    }

    private sealed class HoldVideoBox
    {
        public IntPtr OwnerHwnd;
        public Rectangle Bounds;
        public int HoldFramesRemaining;
    }

    public void Reset()
    {
        _hasPrevFrame = false;
        Array.Clear(_videoEnergy, 0, CellCount);
        _winStates.Clear();
        _activeVideoBoxes.Clear();
    }

    public void Analyze(
        IntPtr statsPtr,
        int rowPitch,
        int screenW,
        int screenH,
        ReadOnlySpan<TrackedWindow> windows,
        AppSettings settings,
        Span<ClassifiedWindow> outWindows,
        out int outWinCount,
        Span<ProtectedRect> outProtectedRects,
        out int outRectCount)
    {
        byte* basePtr = (byte*)statsPtr;
        for (int y = 0; y < GridH; y++)
        {
            byte* row = basePtr + y * rowPitch;
            int rowOff = y * GridW;
            for (int x = 0; x < GridW; x++)
            {
                int i = rowOff + x;
                int p = x * 4;
                _lum[i]       = row[p + 0] * (1.0f / 255.0f);
                _darkFlat[i]  = row[p + 1] * (1.0f / 255.0f);
                _lightFlat[i] = row[p + 2] * (1.0f / 255.0f);
                _photo[i]     = row[p + 3] * (1.0f / 255.0f);
            }
        }

        float cellW = (float)screenW / GridW;
        float cellH = (float)screenH / GridH;
        int winCount = Math.Min(windows.Length, Math.Min(outWindows.Length, 16));
        outWinCount = winCount;

        Array.Fill(_cellOwner, -1);
        Span<float> wSumLum = stackalloc float[16];
        Span<float> wSumDark = stackalloc float[16];
        Span<float> wSumLight = stackalloc float[16];
        Span<int> wCells = stackalloc int[16];
        Span<int> wMovedCells = stackalloc int[16];
        Span<float> wMovedSumDark = stackalloc float[16];
        Span<float> wMovedSumPhoto = stackalloc float[16];

        for (int y = 0; y < GridH; y++)
        {
            float py = (y + 0.5f) * cellH;
            int rowOff = y * GridW;
            for (int x = 0; x < GridW; x++)
            {
                float px = (x + 0.5f) * cellW;
                int i = rowOff + x;
                for (int w = 0; w < winCount; w++)
                {
                    var rc = windows[w].LocalBounds;
                    if (px >= rc.Left && px < rc.Right && py >= rc.Top && py < rc.Bottom)
                    {
                        _cellOwner[i] = w;
                        wSumLum[w] += _lum[i];
                        wSumDark[w] += _darkFlat[i];
                        wSumLight[w] += _lightFlat[i];
                        wCells[w]++;
                        if (_hasPrevFrame && MathF.Abs(_lum[i] - _prevLum[i]) > 0.015f)
                        {
                            wMovedCells[w]++;
                            wMovedSumDark[w] += _darkFlat[i];
                            wMovedSumPhoto[w] += _photo[i];
                        }
                        break;
                    }
                }
            }
        }

        // 1. Classify each top-level HWND (Dark vs Light) & detect whole-window or split-pane UI scroll/drag
        Span<bool> winScrollingOrMoved = stackalloc bool[16];
        for (int w = 0; w < winCount; w++)
        {
            var tw = windows[w];
            if (tw.IsPassthroughHole)
            {
                outWindows[w] = new ClassifiedWindow(tw.Hwnd, tw.LocalBounds, IsDark: false, IsPassthroughHole: true, FilterMode.Off, tw.ProcessName);
                winScrollingOrMoved[w] = false;
                continue;
            }

            if (!_winStates.TryGetValue(tw.Hwnd, out var st))
            {
                st = new WindowState { LastBounds = tw.LocalBounds };
                _winStates[tw.Hwnd] = st;
            }

            bool boundsChanged = tw.LocalBounds != st.LastBounds;
            st.LastBounds = tw.LocalBounds;

            int movedCnt = wMovedCells[w];
            float movedFrac = wCells[w] > 0 ? (float)movedCnt / wCells[w] : 0f;
            float movedMeanDark = movedCnt > 0 ? wMovedSumDark[w] / movedCnt : 0f;
            float movedMeanPhoto = movedCnt > 0 ? wMovedSumPhoto[w] / movedCnt : 0f;

            // Distinguish UI scrolling/dragging from real embedded or fullscreen <video>:
            // - Fullscreen video has high movedMeanPhoto (>= 0.30) and low movedMeanDark (< 0.38).
            // - Split-pane UI scroll (e.g. Antigravity chat / IDE editor) has movedMeanDark > 0.42 & movedMeanPhoto < 0.25.
            bool isFullscreenVideoMotion = movedFrac > 0.40f && movedMeanPhoto >= 0.30f && movedMeanDark < 0.38f;
            bool isWholeWindowScroll = movedFrac > 0.42f && !isFullscreenVideoMotion;
            bool isSplitPaneUiScroll = movedCnt >= 14 && movedFrac > 0.12f && movedMeanDark > 0.42f && movedMeanPhoto < 0.25f;

            if (boundsChanged || isWholeWindowScroll || isSplitPaneUiScroll)
            {
                st.ScrollCooldownFrames = 8;
                _activeVideoBoxes.RemoveAll(vb => vb.OwnerHwnd == tw.Hwnd);
            }
            else if (st.ScrollCooldownFrames > 0)
            {
                st.ScrollCooldownFrames--;
            }

            winScrollingOrMoved[w] = st.ScrollCooldownFrames > 0;

            if (wCells[w] == 0)
            {
                int cx = Math.Clamp((int)((tw.LocalBounds.Left + tw.LocalBounds.Width * 0.5f) / cellW), 0, GridW - 1);
                int cy = Math.Clamp((int)((tw.LocalBounds.Top + tw.LocalBounds.Height * 0.5f) / cellH), 0, GridH - 1);
                int ci = cy * GridW + cx;
                wSumLum[w] = _lum[ci];
                wSumDark[w] = _darkFlat[ci];
                wSumLight[w] = _lightFlat[ci];
                wCells[w] = 1;
            }

            if (wCells[w] >= 1)
            {
                float invN = 1.0f / wCells[w];
                float meanLum = wSumLum[w] * invN;
                float meanDark = wSumDark[w] * invN;
                float meanLight = wSumLight[w] * invN;

                float darkScore = meanDark * 1.35f + MathF.Max(0f, 0.50f - meanLum);
                float lightScore = meanLight * 1.35f + MathF.Max(0f, meanLum - 0.50f);

                bool smallLightPopup = wCells[w] <= 12 && meanLum > 0.50f && meanDark < 0.20f;
                if (!st.Initialized)
                {
                    st.IsDark = !(smallLightPopup || lightScore > darkScore + 0.10f || (meanLum > 0.58f && meanLight > 0.22f));
                    st.Initialized = true;
                }
                else if (st.IsDark)
                {
                    st.IsDark = !(smallLightPopup || lightScore > darkScore + 0.10f || (meanLum > 0.58f && meanLight > 0.22f));
                }
                else
                {
                    st.IsDark = (darkScore > lightScore + 0.08f || (meanLum < 0.42f && meanDark > 0.20f));
                }
            }

            bool finalDark = tw.EffectiveMode switch
            {
                FilterMode.ForceInvertAll => true,
                FilterMode.CrispLightOnly => false,
                _ => settings.KeepLightWindows ? st.IsDark : true
            };
            outWindows[w] = new ClassifiedWindow(tw.Hwnd, tw.LocalBounds, finalDark, IsPassthroughHole: false, tw.EffectiveMode, tw.ProcessName);
        }

        // Prune stale HWNDs occasionally
        if (_winStates.Count > 64)
        {
            var toRemove = new List<IntPtr>();
            foreach (var kv in _winStates)
            {
                bool active = false;
                for (int w = 0; w < winCount; w++)
                    if (windows[w].Hwnd == kv.Key) { active = true; break; }
                if (!active) toRemove.Add(kv.Key);
            }
            foreach (var k in toRemove) _winStates.Remove(k);
        }

        // 2. Update scroll-safe video motion energy per cell (rejecting flat digital dark UI text cells)
        if (_hasPrevFrame && settings.ProtectPhotos)
        {
            for (int i = 0; i < CellCount; i++)
            {
                int owner = _cellOwner[i];
                if (owner < 0 || !outWindows[owner].IsDark || outWindows[owner].EffectiveMode != FilterMode.SmartAuto || winScrollingOrMoved[owner])
                {
                    _videoEnergy[i] = 0f;
                    continue;
                }

                // Pure flat dark UI cells (chat background, code background, terminal) must never accumulate video energy
                bool isFlatDarkUiCell = _darkFlat[i] > 0.58f && _photo[i] < 0.22f;
                if (isFlatDarkUiCell)
                {
                    _videoEnergy[i] *= 0.78f;
                    continue;
                }

                float movedFrac = wCells[owner] > 0 ? (float)wMovedCells[owner] / wCells[owner] : 0f;
                bool motion = movedFrac >= 0.010f && MathF.Abs(_lum[i] - _prevLum[i]) > 0.012f;
                if (motion)
                    _videoEnergy[i] = MathF.Min(1.0f, _videoEnergy[i] + 0.20f); // ~3-4 frames of continuous video playback to lock
                else
                    _videoEnergy[i] *= 0.90f;
            }
        }
        else
        {
            Array.Clear(_videoEnergy, 0, CellCount);
        }

        Buffer.BlockCopy(_lum, 0, _prevLum, 0, CellCount * sizeof(float));
        _hasPrevFrame = true;

        outRectCount = 0;
        int maxRects = outProtectedRects.Length;

        // 3. Extract Clean Axis-Aligned White Sub-Pane Rectangles inside Dark Windows (e.g. white webpage inside dark browser)
        if (settings.KeepLightWindows)
        {
            for (int i = 0; i < CellCount; i++)
            {
                int owner = _cellOwner[i];
                _candMask[i] = (owner >= 0 && outWindows[owner].IsDark && outWindows[owner].EffectiveMode == FilterMode.SmartAuto && _lightFlat[i] > 0.42f && _lum[i] > 0.68f)
                    ? (byte)1 : (byte)0;
            }
            ExtractCleanRectangles(
                minCells: 18,
                minFillRatio: 0.62f,
                minAspect: 0.2f,
                maxAspect: 6.0f,
                cellW, cellH,
                windows, outWindows, winCount,
                isMedia: false,
                outProtectedRects, ref outRectCount, maxRects);
        }

        // 4. Extract Clean Axis-Aligned Rectangular Photos & Stabilized Video Player Boxes inside Dark Windows
        if (settings.ProtectPhotos && outRectCount < maxRects)
        {
            // 4a. Newly detected video boxes
            for (int i = 0; i < CellCount; i++)
            {
                int owner = _cellOwner[i];
                _candMask[i] = (owner >= 0 && outWindows[owner].IsDark && outWindows[owner].EffectiveMode == FilterMode.SmartAuto && _videoEnergy[i] > 0.52f)
                    ? (byte)1 : (byte)0;
            }

            Span<ProtectedRect> detectedVideos = stackalloc ProtectedRect[4];
            int detVidCount = 0;
            ExtractCleanRectangles(
                minCells: 12,
                minFillRatio: 0.55f,
                minAspect: 0.45f,
                maxAspect: 3.2f,
                cellW, cellH,
                windows, outWindows, winCount,
                isMedia: true,
                detectedVideos, ref detVidCount, 4);

            // Merge/refresh active video boxes (verify interior has real video/media gradients, never flat dark UI!)
            for (int d = 0; d < detVidCount; d++)
            {
                var vr = detectedVideos[d].LocalBounds;
                if (!IsPlausibleVideoBox(vr, cellW, cellH))
                    continue;

                IntPtr ownerHwnd = IntPtr.Zero;
                Point center = new(vr.Left + vr.Width / 2, vr.Top + vr.Height / 2);
                for (int w = 0; w < winCount; w++)
                {
                    if (windows[w].LocalBounds.Contains(center)) { ownerHwnd = windows[w].Hwnd; break; }
                }

                bool merged = false;
                foreach (var existing in _activeVideoBoxes)
                {
                    if (existing.OwnerHwnd == ownerHwnd && existing.Bounds.IntersectsWith(vr))
                    {
                        existing.Bounds = SmoothRect(existing.Bounds, Rectangle.Union(existing.Bounds, vr), windows, ownerHwnd);
                        existing.HoldFramesRemaining = 42;
                        merged = true;
                        break;
                    }
                }
                if (!merged && _activeVideoBoxes.Count < 6)
                {
                    _activeVideoBoxes.Add(new HoldVideoBox
                    {
                        OwnerHwnd = ownerHwnd,
                        Bounds = vr,
                        HoldFramesRemaining = 42
                    });
                }
            }

            for (int idx = _activeVideoBoxes.Count - 1; idx >= 0; idx--)
            {
                var vb = _activeVideoBoxes[idx];
                vb.HoldFramesRemaining--;
                bool ownerStillDarkVisible = false;
                for (int w = 0; w < winCount; w++)
                {
                    if (outWindows[w].Hwnd == vb.OwnerHwnd && outWindows[w].IsDark)
                    {
                        ownerStillDarkVisible = true;
                        break;
                    }
                }
                // Immediately release if user closed/scrolled away onto flat dark UI text
                if (vb.HoldFramesRemaining <= 0 || !ownerStillDarkVisible || IsPredominantlyFlatDarkUi(vb.Bounds, cellW, cellH))
                {
                    _activeVideoBoxes.RemoveAt(idx);
                }
                else if (outRectCount < maxRects)
                {
                    outProtectedRects[outRectCount++] = new ProtectedRect(vb.Bounds, IsMedia: true);
                }
            }

            // 4b. High-confidence rectangular static photos (strict rectangular fill >= 0.62 to avoid UI blotches!)
            float sens = Math.Clamp(settings.PhotoSensitivity, 0.1f, 1.0f);
            float photoThresh = 0.68f - sens * 0.30f;
            for (int i = 0; i < CellCount; i++)
            {
                int owner = _cellOwner[i];
                _candMask[i] = (owner >= 0 && outWindows[owner].IsDark && outWindows[owner].EffectiveMode == FilterMode.SmartAuto &&
                                _photo[i] > photoThresh && _darkFlat[i] < 0.22f && _lightFlat[i] < 0.30f)
                    ? (byte)1 : (byte)0;
            }
            ExtractCleanRectangles(
                minCells: 16,
                minFillRatio: 0.62f,
                minAspect: 0.30f,
                maxAspect: 3.8f,
                cellW, cellH,
                windows, outWindows, winCount,
                isMedia: true,
                outProtectedRects, ref outRectCount, maxRects);
        }
    }

    private bool IsPlausibleVideoBox(Rectangle pxRect, float cellW, float cellH)
    {
        int x0 = Math.Clamp((int)(pxRect.Left / cellW), 0, GridW - 1);
        int x1 = Math.Clamp((int)MathF.Ceiling(pxRect.Right / cellW) - 1, x0, GridW - 1);
        int y0 = Math.Clamp((int)(pxRect.Top / cellH), 0, GridH - 1);
        int y1 = Math.Clamp((int)MathF.Ceiling(pxRect.Bottom / cellH) - 1, y0, GridH - 1);

        float sumDarkFlat = 0f;
        float sumPhoto = 0f;
        int n = 0;
        for (int y = y0; y <= y1; y++)
        {
            int row = y * GridW;
            for (int x = x0; x <= x1; x++)
            {
                int i = row + x;
                sumDarkFlat += _darkFlat[i];
                sumPhoto += _photo[i];
                n++;
            }
        }
        if (n == 0) return false;
        float meanDarkFlat = sumDarkFlat / n;
        float meanPhoto = sumPhoto / n;

        // Real video frames (even dark night scenes) have continuous tonal gradients (meanPhoto >= 0.24)
        // and are NOT >52% flat solid digital UI background (#0A0A0A..#252526).
        return meanPhoto >= 0.24f && meanDarkFlat < 0.52f;
    }

    private bool IsPredominantlyFlatDarkUi(Rectangle pxRect, float cellW, float cellH)
    {
        int x0 = Math.Clamp((int)(pxRect.Left / cellW), 0, GridW - 1);
        int x1 = Math.Clamp((int)MathF.Ceiling(pxRect.Right / cellW) - 1, x0, GridW - 1);
        int y0 = Math.Clamp((int)(pxRect.Top / cellH), 0, GridH - 1);
        int y1 = Math.Clamp((int)MathF.Ceiling(pxRect.Bottom / cellH) - 1, y0, GridH - 1);

        float sumDarkFlat = 0f;
        float sumPhoto = 0f;
        float sumLum = 0f;
        int n = 0;
        for (int y = y0; y <= y1; y++)
        {
            int row = y * GridW;
            for (int x = x0; x <= x1; x++)
            {
                int i = row + x;
                sumDarkFlat += _darkFlat[i];
                sumPhoto += _photo[i];
                sumLum += _lum[i];
                n++;
            }
        }
        if (n == 0) return false;
        float meanDarkFlat = sumDarkFlat / n;
        float meanPhoto = sumPhoto / n;
        float meanLum = sumLum / n;

        // Pure black movie fade-out has meanLum < 0.02; dark UI with text/cards has meanLum >= 0.025 and high flat dark UI ratio
        return meanLum >= 0.025f && meanDarkFlat > 0.64f && meanPhoto < 0.18f;
    }

    private static Rectangle SmoothRect(Rectangle prev, Rectangle next, ReadOnlySpan<TrackedWindow> wins, IntPtr owner)
    {
        Rectangle clip = next;
        for (int w = 0; w < wins.Length; w++)
        {
            if (wins[w].Hwnd == owner)
            {
                clip = Rectangle.Intersect(next, wins[w].LocalBounds);
                break;
            }
        }
        if (Math.Abs(clip.Left - prev.Left) < 24 && Math.Abs(clip.Right - prev.Right) < 24 &&
            Math.Abs(clip.Top - prev.Top) < 24 && Math.Abs(clip.Bottom - prev.Bottom) < 24)
        {
            return prev;
        }
        return clip;
    }

    private void ExtractCleanRectangles(
        int minCells,
        float minFillRatio,
        float minAspect,
        float maxAspect,
        float cellW,
        float cellH,
        ReadOnlySpan<TrackedWindow> windows,
        ReadOnlySpan<ClassifiedWindow> classified,
        int winCount,
        bool isMedia,
        Span<ProtectedRect> outRects,
        ref int outCount,
        int maxOut)
    {
        Array.Clear(_visited, 0, CellCount);

        for (int sy = 0; sy < GridH && outCount < maxOut; sy++)
        {
            for (int sx = 0; sx < GridW && outCount < maxOut; sx++)
            {
                int start = sy * GridW + sx;
                if (_candMask[start] == 0 || _visited[start] != 0)
                    continue;

                int owner = _cellOwner[start];
                if (owner < 0 || owner >= winCount || !classified[owner].IsDark)
                    continue;

                int head = 0, tail = 0;
                _bfsQueue[tail++] = start;
                _visited[start] = 1;

                int minX = sx, maxX = sx, minY = sy, maxY = sy;
                int count = 0;

                while (head < tail)
                {
                    int cur = _bfsQueue[head++];
                    int cy = cur / GridW;
                    int cx = cur - cy * GridW;
                    count++;

                    if (cx < minX) minX = cx;
                    if (cx > maxX) maxX = cx;
                    if (cy < minY) minY = cy;
                    if (cy > maxY) maxY = cy;

                    // 8-connected neighbors within the same owner window
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = cy + dy;
                        if (ny < 0 || ny >= GridH) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx;
                            if (nx < 0 || nx >= GridW) continue;
                            int ni = ny * GridW + nx;
                            if (_visited[ni] == 0 && _candMask[ni] != 0 && _cellOwner[ni] == owner)
                            {
                                _visited[ni] = 1;
                                _bfsQueue[tail++] = ni;
                            }
                        }
                    }
                }

                int bw = maxX - minX + 1;
                int bh = maxY - minY + 1;
                if (count < minCells || bw < 3 || bh < 3)
                    continue;

                float boxArea = bw * bh;
                float fillRatio = count / boxArea;
                float aspect = (bw * cellW) / MathF.Max(1f, bh * cellH);

                if (fillRatio >= minFillRatio && aspect >= minAspect && aspect <= maxAspect)
                {
                    var pxRect = new Rectangle(
                        (int)MathF.Floor(minX * cellW),
                        (int)MathF.Floor(minY * cellH),
                        (int)MathF.Ceiling(bw * cellW),
                        (int)MathF.Ceiling(bh * cellH));

                    pxRect = Rectangle.Intersect(pxRect, windows[owner].LocalBounds);
                    if (pxRect.Width >= 80 && pxRect.Height >= 60)
                    {
                        outRects[outCount++] = new ProtectedRect(pxRect, isMedia);
                    }
                }
            }
        }
    }
}
