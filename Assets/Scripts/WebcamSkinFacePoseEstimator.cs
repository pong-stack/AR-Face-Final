using Unity.Collections;
using UnityEngine;

/// <summary>
/// Editor webcam face hints: YCbCr skin mask, optional morphology opening, covariance roll,
/// confidence, softened yaw/pitch. Uses <see cref="Texture2D.GetPixelData{T}"/> to skip GetPixels32 GC.
/// </summary>
public sealed class WebcamSkinFacePoseEstimator
{
    Texture2D _cpuTex;

    byte[] _maskOpen;
    byte[] _maskWork;

    public struct Sample
    {
        public bool Valid;
        public float Confidence;
        public float CenterXN;
        public float CenterYN;
        public float BboxCenterXN;
        public float BboxCenterYN;
        public float BboxNormH;
        public float BboxNormW;
        public float RollDeg;
        public float YawDeg;
        public float PitchDeg;
        public int SkinPixelCount;
        public float FillRatioWithinBbox;
    }

    public struct TemporalState
    {
        public bool Initialized;
        public float CenterXN;
        public float CenterYN;
        public float BboxCenterXN;
        public float BboxCenterYN;
        public float BboxNormH;
        public float BboxNormW;
        public float YawDeg;
        public float PitchDeg;
        public float RollDeg;
    }

    public void Release()
    {
        if (_cpuTex != null)
        {
            Object.Destroy(_cpuTex);
            _cpuTex = null;
        }

        _maskOpen = null;
        _maskWork = null;
    }

    public bool TryEstimate(
        WebCamTexture web,
        int trackWidth,
        bool horizontalMirrorSelfieStyle,
        int minSkinPixels,
        float roiMarginX,
        float roiMarginY,
        float maxYawDegrees,
        float maxPitchDegrees,
        float rollGain,
        bool useMorphology,
        int morphologyPasses,
        ref TemporalState temporal,
        float temporalSmoothStrength,
        float yawPitchCurveExponent,
        out Sample sample)
    {
        sample = default;
        temporalSmoothStrength = Mathf.Clamp01(temporalSmoothStrength);
        yawPitchCurveExponent = Mathf.Max(0.65f, yawPitchCurveExponent);

        if (web == null || !web.isPlaying || web.width <= 16)
        {
            temporal = default;
            return false;
        }

        int tw = Mathf.Max(32, trackWidth);
        int webH = Mathf.Max(1, web.height);
        int webW = Mathf.Max(1, web.width);
        int th = Mathf.Max(24, Mathf.RoundToInt((float)tw * webH / webW));

        tw = Mathf.Max(1, tw);
        th = Mathf.Max(1, th);

        int pixels = tw * th;

        RenderTexture rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(web, rt);
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = rt;

        if (_cpuTex == null || _cpuTex.width != tw || _cpuTex.height != th)
        {
            if (_cpuTex != null)
                Object.Destroy(_cpuTex);

            _cpuTex = new Texture2D(tw, th, TextureFormat.RGBA32, mipChain: false, linear: false);
        }

        _cpuTex.ReadPixels(new Rect(0, 0, tw, th), 0, 0, false);
        _cpuTex.Apply(false, false);
        RenderTexture.active = active;
        RenderTexture.ReleaseTemporary(rt);

        NativeArray<Color32> pix = _cpuTex.GetPixelData<Color32>(0);
        if (!pix.IsCreated || pix.Length < pixels)
            return false;

        if (_maskWork == null || _maskWork.Length < pixels)
            _maskWork = new byte[pixels];

        if (_maskOpen == null || _maskOpen.Length < pixels)
            _maskOpen = new byte[pixels];

        ComputeRoiBounds(tw, th, roiMarginX, roiMarginY, out int xr0, out int xr1, out int yr0, out int yr1);

        BuildSkinMaskRoi(pix, tw, th, xr0, xr1, yr0, yr1, _maskWork);

        if (useMorphology && morphologyPasses > 0)
        {
            for (var p = 0; p < morphologyPasses; p++)
                MorphOpeningInPlace(_maskWork, _maskOpen, tw, th);
        }

        if (!MomentPass(
                _maskWork,
                tw,
                th,
                minSkinPixels,
                horizontalMirrorSelfieStyle,
                maxYawDegrees,
                maxPitchDegrees,
                rollGain,
                yawPitchCurveExponent,
                out sample))
        {
            temporal = default;
            return false;
        }

        SmoothTemporal(ref temporal, temporalSmoothStrength, ref sample);
        return true;
    }

    /// <summary>ROI in pixel coords, always inside [0, tw-1] / [0, th-1] with at least modest span when possible.</summary>
    static void ComputeRoiBounds(int tw, int th, float roiMarginX, float roiMarginY, out int xmin, out int xmax,
        out int ymin, out int ymax)
    {
        int maxX = tw - 1;
        int maxY = th - 1;

        xmin = Mathf.Clamp(Mathf.RoundToInt(tw * Mathf.Clamp01(roiMarginX)), 0, maxX);
        xmax = Mathf.Clamp(Mathf.RoundToInt(tw * Mathf.Clamp01(1f - roiMarginX)), 0, maxX);

        ymin = Mathf.Clamp(Mathf.RoundToInt(th * Mathf.Clamp01(roiMarginY)), 0, maxY);
        ymax = Mathf.Clamp(Mathf.RoundToInt(th * Mathf.Clamp01(1f - roiMarginY)), 0, maxY);

        if (xmax < xmin)
            (xmin, xmax) = (xmax, xmin);

        if (ymax < ymin)
            (ymin, ymax) = (ymax, ymin);

        const int minSpan = 5;
        if (tw >= minSpan && xmax - xmin < minSpan && maxX >= minSpan)
            xmax = Mathf.Min(maxX, Mathf.Max(xmax, xmin + minSpan));

        if (th >= minSpan && ymax - ymin < minSpan && maxY >= minSpan)
            ymax = Mathf.Min(maxY, Mathf.Max(ymax, ymin + minSpan));

        xmax = Mathf.Clamp(xmax, xmin, maxX);
        ymax = Mathf.Clamp(ymax, ymin, maxY);
    }

    static void BuildSkinMaskRoi(NativeArray<Color32> pixels, int tw, int th, int xmin, int xmax, int ymin,
        int ymax, byte[] dst)
    {
        if (tw <= 0 || th <= 0 || dst == null)
            return;

        int len = Mathf.Min(tw * th, Mathf.Min(dst.Length, pixels.Length));
        if (len <= 0)
            return;

        for (var i = 0; i < len; i++)
            dst[i] = 0;

        int maxX = tw - 1;
        int maxY = th - 1;

        xmin = Mathf.Clamp(xmin, 0, maxX);
        xmax = Mathf.Clamp(xmax, 0, maxX);
        ymin = Mathf.Clamp(ymin, 0, maxY);
        ymax = Mathf.Clamp(ymax, 0, maxY);

        if (ymax < ymin || xmax < xmin)
            return;

        for (int y = ymin; y <= ymax; y++)
        {
            int row = y * tw;

            for (int x = xmin; x <= xmax; x++)
            {
                int idx = row + x;
                if ((uint)idx >= (uint)len)
                    continue;

                dst[idx] = IsSkinCbCr(pixels[idx]) ? byte.MaxValue : (byte)0;
            }
        }
    }

    static bool IsSkinCbCr(Color32 p)
    {
        float r = p.r;
        float g = p.g;
        float b = p.b;

        float y = 0.257f * r + 0.504f * g + 0.098f * b + 16f;
        float cb = -0.148f * r - 0.291f * g + 0.439f * b + 128f;
        float cr = 0.439f * r - 0.368f * g - 0.071f * b + 128f;

        if (cb >= 77f && cb <= 127f && cr >= 133f && cr <= 173f && y > 50f && y < 246f)
            return true;

        if ((int)p.r > 94 && (int)p.g > 37 && (int)p.b > 13 && (int)p.r > (int)p.g && (int)p.r > (int)p.b
            && Mathf.Abs((int)p.r - (int)p.g) > 12 && y > 40f && y < 246f && cr >= 126f && cr <= 178f
            && cb >= 73f && cb <= 133f)
            return true;

        return false;
    }

    static void MorphOpeningInPlace(byte[] ioBuffer, byte[] scratch, int tw, int th)
    {
        MorphMin3x3(ioBuffer, scratch, tw, th);
        MorphMax3x3(scratch, ioBuffer, tw, th);
    }

    /// <remarks>Min filter (binary erode).</remarks>
    static void MorphMin3x3(byte[] src, byte[] dst, int tw, int th)
    {
        for (int y = 0; y < th; y++)
        {
            int row = y * tw;
            for (int x = 0; x < tw; x++)
            {
                byte vMin = byte.MaxValue;
                for (int oy = -1; oy <= 1; oy++)
                {
                    int yy = Mathf.Clamp(y + oy, 0, th - 1);
                    int rOff = yy * tw;
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int xx = Mathf.Clamp(x + ox, 0, tw - 1);
                        vMin = (byte)Mathf.Min(vMin, src[rOff + xx]);
                    }
                }

                dst[row + x] = vMin;
            }
        }
    }

    /// <remarks>Max filter (binary dilate).</remarks>
    static void MorphMax3x3(byte[] src, byte[] dst, int tw, int th)
    {
        for (int y = 0; y < th; y++)
        {
            int row = y * tw;
            for (int x = 0; x < tw; x++)
            {
                byte vMax = 0;
                for (int oy = -1; oy <= 1; oy++)
                {
                    int yy = Mathf.Clamp(y + oy, 0, th - 1);
                    int rOff = yy * tw;
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int xx = Mathf.Clamp(x + ox, 0, tw - 1);
                        vMax = (byte)Mathf.Max(vMax, src[rOff + xx]);
                    }
                }

                dst[row + x] = vMax;
            }
        }
    }

    static bool MomentPass(
        byte[] mask,
        int tw,
        int th,
        int minSkinPixels,
        bool mirroredSelfie,
        float yawMaxDeg,
        float pitchMaxDeg,
        float rollGain,
        float yawPow,
        out Sample sample)
    {
        sample = default;

        long sx = 0;
        long sy = 0;
        int count = 0;
        int xmin = int.MaxValue;
        int xmax = int.MinValue;
        int ymin = int.MaxValue;
        int ymax = int.MinValue;

        for (int y = 0; y < th; y++)
        {
            int rowBase = y * tw;
            for (int x = 0; x < tw; x++)
            {
                int i = rowBase + x;
                if (mask[i] < 128)
                    continue;

                sx += x;
                sy += y;
                count++;

                if (x < xmin)
                    xmin = x;
                if (x > xmax)
                    xmax = x;
                if (y < ymin)
                    ymin = y;
                if (y > ymax)
                    ymax = y;
            }
        }

        if (count < minSkinPixels)
            return false;

        float mx = sx / (float)count;
        float my = sy / (float)count;

        double cxx = 0;
        double cyy = 0;
        double cxy = 0;

        for (int y = 0; y < th; y++)
        {
            int rowBase = y * tw;
            for (int x = 0; x < tw; x++)
            {
                int i = rowBase + x;
                if (mask[i] < 128)
                    continue;

                float dx = x - mx;
                float dy = y - my;
                cxx += dx * dx;
                cyy += dy * dy;
                cxy += dx * dy;
            }
        }

        float rollDeg = Mathf.Atan2((float)(2 * cxy), Mathf.Max((float)(cxx - cyy), 1e-6f))
            * 0.5f * Mathf.Rad2Deg * Mathf.Clamp01(rollGain);
        rollDeg = Mathf.Clamp(rollDeg, -54f, 54f);

        float bw = xmax >= xmin ? (xmax - xmin + 1f) / tw : 0.1f;
        float bh = ymax >= ymin ? (ymax - ymin + 1f) / th : 0.1f;
        float bboxAreaPx = Mathf.Max((xmax - xmin + 1) * (ymax - ymin + 1), 1);
        float fill = count / bboxAreaPx;

        float bxMid = Mathf.Clamp01(((xmin + xmax + 1f) * 0.5f) / tw);
        float byMid = Mathf.Clamp01(((ymin + ymax + 1f) * 0.5f) / th);

        float cxNorm = Mathf.Clamp01(mx / Mathf.Max(tw - 1, 1));
        float cyNorm = Mathf.Clamp01(my / Mathf.Max(th - 1, 1));

        float blendX = Mathf.Lerp(cxNorm, bxMid, 0.55f);
        float cxYaw = mirroredSelfie ? 1f - blendX : blendX;

        float offX = Mathf.Abs(cxYaw - 0.5f) * 2f;
        float yawDeg = Mathf.Sign(cxYaw - 0.5f) * Mathf.Pow(offX, yawPow) * yawMaxDeg;
        yawDeg = Mathf.Clamp(yawDeg, -yawMaxDeg, yawMaxDeg);

        float cyPitch = Mathf.Lerp(cyNorm, byMid, 0.18f);
        float offY = Mathf.Abs((0.5f - cyPitch)) * 2f;
        float pitchDeg = Mathf.Sign(0.5f - cyPitch) * Mathf.Pow(offY, yawPow) * pitchMaxDeg;
        pitchDeg = Mathf.Clamp(pitchDeg, -pitchMaxDeg, pitchMaxDeg);

        float aspect = bw / Mathf.Max(bh, 1e-4f);
        float aspectScore = Mathf.Clamp01(1f - Mathf.Abs(aspect - 0.88f) * 6f);

        float fillScore = Mathf.Clamp01((fill - 0.07f) / 0.5f);

        float sizeScore = Mathf.Clamp01(count / Mathf.Max(tw * th * 0.11f, 1200f));

        float confidence = Mathf.Clamp01(0.35f + 0.37f * fillScore + 0.35f * aspectScore + 0.26f * sizeScore);

        sample = new Sample
        {
            Valid = true,
            Confidence = confidence,
            CenterXN = cxNorm,
            CenterYN = cyNorm,
            BboxCenterXN = bxMid,
            BboxCenterYN = byMid,
            BboxNormH = Mathf.Max(bh, 1e-3f),
            BboxNormW = Mathf.Max(bw, 1e-3f),
            RollDeg = rollDeg,
            YawDeg = yawDeg,
            PitchDeg = pitchDeg,
            SkinPixelCount = count,
            FillRatioWithinBbox = fill,
        };

        return true;
    }

    /// <param name="temporalStrength01">Higher = smoother (sticks to prior estimate); 0 leaves sample raw.</param>
    static void SmoothTemporal(ref TemporalState t, float temporalStrength01, ref Sample sample)
    {
        if (temporalStrength01 < 1e-5f)
            return;

        float conf = Mathf.Clamp01(sample.Confidence + 0.12f);

        // Blend factor toward freshly measured pose; low k = softer motion.
        float strength = Mathf.Clamp01(temporalStrength01);
        float k = Mathf.Lerp(1f, Mathf.Clamp01(0.042f + 0.93f * (conf * conf)), strength);

        if (!t.Initialized)
        {
            t.CenterXN = sample.CenterXN;
            t.CenterYN = sample.CenterYN;
            t.BboxCenterXN = sample.BboxCenterXN;
            t.BboxCenterYN = sample.BboxCenterYN;
            t.BboxNormH = sample.BboxNormH;
            t.BboxNormW = sample.BboxNormW;
            t.YawDeg = sample.YawDeg;
            t.PitchDeg = sample.PitchDeg;
            t.RollDeg = sample.RollDeg;
            t.Initialized = true;
            return;
        }

        t.CenterXN = Mathf.Lerp(t.CenterXN, sample.CenterXN, k);
        t.CenterYN = Mathf.Lerp(t.CenterYN, sample.CenterYN, k);
        t.BboxCenterXN = Mathf.Lerp(t.BboxCenterXN, sample.BboxCenterXN, k);
        t.BboxCenterYN = Mathf.Lerp(t.BboxCenterYN, sample.BboxCenterYN, k);
        t.BboxNormH = Mathf.Lerp(t.BboxNormH, sample.BboxNormH, Mathf.Lerp(0.2f, k, 0.55f));
        t.BboxNormW = Mathf.Lerp(t.BboxNormW, sample.BboxNormW, Mathf.Lerp(0.2f, k, 0.55f));
        t.YawDeg = Mathf.Lerp(t.YawDeg, sample.YawDeg, k * 0.75f);
        t.PitchDeg = Mathf.Lerp(t.PitchDeg, sample.PitchDeg, k * 0.75f);
        t.RollDeg = Mathf.Lerp(t.RollDeg, sample.RollDeg, k * 0.5f);

        sample.CenterXN = t.CenterXN;
        sample.CenterYN = t.CenterYN;
        sample.BboxCenterXN = t.BboxCenterXN;
        sample.BboxCenterYN = t.BboxCenterYN;
        sample.BboxNormH = t.BboxNormH;
        sample.BboxNormW = t.BboxNormW;
        sample.YawDeg = t.YawDeg;
        sample.PitchDeg = t.PitchDeg;
        sample.RollDeg = t.RollDeg;
    }
}
